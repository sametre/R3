using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record DespatchDocumentSummary(string Id, string DespatchNo, string Account, string Status, DateTime DocumentDate, decimal Quantity, string? SourceInvoiceId);

/// <summary>Outbound despatch lifecycle. A despatch created from a posted invoice is a document
/// relation and does not post inventory a second time; the invoice remains the stock source.</summary>
public sealed class LocalDespatchService(StoreDatabase database)
{
    public string CreateFromSalesInvoice(string invoiceId, string userId)
    {
        using var connection = database.OpenConnection(); using var transaction = connection.BeginTransaction();
        string company; string branch; string warehouse; string account; string documentDate; string description; string invoiceStatus;
        using (var read = Command(connection, transaction, "SELECT company_id,branch_id,warehouse_id,account_id,document_date,description,status FROM sales_documents WHERE id=$id"))
        {
            read.Parameters.AddWithValue("$id", invoiceId); using var reader = read.ExecuteReader();
            if (!reader.Read()) throw new KeyNotFoundException("Satış faturası bulunamadı.");
            company = reader.GetString(0); branch = reader.GetString(1); warehouse = reader.GetString(2); account = reader.GetString(3); documentDate = reader.GetString(4); description = reader.GetString(5); invoiceStatus = reader.GetString(6);
        }
        if (invoiceStatus != "Posted") throw new InvalidOperationException("İrsaliye yalnızca kesinleşmiş satış faturasından oluşturulabilir.");
        using (var existing = Command(connection, transaction, "SELECT target_document_id FROM document_relations WHERE source_document_type='SalesInvoice' AND source_document_id=$id AND target_document_type='DespatchDocument' AND relation_type='InvoiceToDespatch' LIMIT 1"))
        {
            existing.Parameters.AddWithValue("$id", invoiceId); var value = existing.ExecuteScalar(); if (value != null && value != DBNull.Value) return value.ToString()!;
        }
        var id = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O"); var number = AllocateNumber(connection, transaction, company, DateTime.Parse(documentDate).Year);
        using (var insert = Command(connection, transaction, "INSERT INTO despatch_documents(id,company_id,branch_id,warehouse_id,account_id,source_sales_invoice_id,despatch_no,document_date,direction,status,description,created_at,created_by,updated_at,updated_by) VALUES($id,$company,$branch,$warehouse,$account,$invoice,$number,$date,'Outbound','Draft',$description,$now,$user,$now,$user)"))
        { Add(insert, "$id", id); Add(insert, "$company", company); Add(insert, "$branch", branch); Add(insert, "$warehouse", warehouse); Add(insert, "$account", account); Add(insert, "$invoice", invoiceId); Add(insert, "$number", number); Add(insert, "$date", documentDate); Add(insert, "$description", description); Add(insert, "$now", now); Add(insert, "$user", userId); insert.ExecuteNonQuery(); }
        using (var lines = Command(connection, transaction, "SELECT product_id,variant_id,unit_id,quantity,description FROM sales_document_lines WHERE sales_document_id=$id ORDER BY line_no"))
        {
            lines.Parameters.AddWithValue("$id", invoiceId); using var reader = lines.ExecuteReader(); var lineNo = 1;
            while (reader.Read())
            {
                using var insert = Command(connection, transaction, "INSERT INTO despatch_document_lines(id,despatch_document_id,line_no,product_id,variant_id,unit_id,quantity,description) VALUES($id,$doc,$line,$product,$variant,$unit,$quantity,$description)");
                Add(insert, "$id", Guid.NewGuid().ToString()); Add(insert, "$doc", id); Add(insert, "$line", lineNo++); Add(insert, "$product", reader.GetString(0)); Add(insert, "$variant", reader.IsDBNull(1) ? DBNull.Value : reader.GetString(1)); Add(insert, "$unit", reader.GetString(2)); Add(insert, "$quantity", reader.GetDecimal(3)); Add(insert, "$description", reader.GetString(4)); insert.ExecuteNonQuery();
            }
        }
        Relation(connection, transaction, company, "SalesInvoice", invoiceId, "DespatchDocument", id, "InvoiceToDespatch", userId, now);
        Audit(connection, transaction, company, id, userId, "DespatchCreated", $"sourceInvoice={invoiceId};despatchNo={number}", now);
        transaction.Commit(); return id;
    }

    public DataTable Search(string companyId, string? search = null, string? status = null) => database.Query("""
        SELECT d.id AS Id, d.despatch_no AS IrsaliyeNo, d.document_date AS Tarih, a.code AS CariKodu, a.name AS Cari,
               d.status AS Durum, d.direction AS Yon, COALESCE(SUM(l.quantity),0) AS Miktar, d.source_sales_invoice_id AS FaturaId
        FROM despatch_documents d JOIN accounts a ON a.id=d.account_id LEFT JOIN despatch_document_lines l ON l.despatch_document_id=d.id
        WHERE d.company_id=$company AND ($search='' OR d.despatch_no LIKE $like OR a.code LIKE $like OR a.name LIKE $like)
          AND ($status='' OR d.status=$status)
        GROUP BY d.id ORDER BY d.document_date DESC,d.created_at DESC
        """, ("$company", companyId), ("$search", search?.Trim() ?? ""), ("$like", $"%{search?.Trim() ?? ""}%"), ("$status", status ?? ""));

    public void Transition(string despatchId, string nextStatus, string userId)
    {
        using var connection = database.OpenConnection(); using var transaction = connection.BeginTransaction();
        string company; string current;
        using (var read = Command(connection, transaction, "SELECT company_id,status FROM despatch_documents WHERE id=$id")) { Add(read, "$id", despatchId); using var reader = read.ExecuteReader(); if (!reader.Read()) throw new KeyNotFoundException("İrsaliye bulunamadı."); company = reader.GetString(0); current = reader.GetString(1); }
        var allowed = (current, nextStatus) switch { ("Draft", "Planned") or ("Planned", "ReadyForShipment") or ("ReadyForShipment", "InTransit") or ("InTransit", "Delivered") => true, ("Draft", "Cancelled") or ("Planned", "Cancelled") or ("ReadyForShipment", "Cancelled") => true, _ => false };
        if (!allowed) throw new InvalidOperationException($"{current} durumundaki irsaliye {nextStatus} durumuna geçirilemez.");
        var now = DateTime.UtcNow.ToString("O");
        using (var update = Command(connection, transaction, "UPDATE despatch_documents SET status=$status,updated_at=$now,updated_by=$user WHERE id=$id")) { Add(update, "$status", nextStatus); Add(update, "$now", now); Add(update, "$user", userId); Add(update, "$id", despatchId); update.ExecuteNonQuery(); }
        using (var history = Command(connection, transaction, "INSERT INTO despatch_status_history(id,despatch_document_id,from_status,to_status,changed_by,changed_at) VALUES($id,$doc,$from,$to,$user,$now)")) { Add(history, "$id", Guid.NewGuid().ToString()); Add(history, "$doc", despatchId); Add(history, "$from", current); Add(history, "$to", nextStatus); Add(history, "$user", userId); Add(history, "$now", now); history.ExecuteNonQuery(); }
        Audit(connection, transaction, company, despatchId, userId, "DespatchStatusChanged", $"{current}->{nextStatus}", now); transaction.Commit();
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string text) { var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = text; return command; }
    private static void Add(SqliteCommand command, string name, object value) => command.Parameters.AddWithValue(name, value);
    private static string AllocateNumber(SqliteConnection c, SqliteTransaction tx, string company, int year) { using var command = Command(c, tx, "INSERT INTO number_sequences(company_id,sequence_type,year,prefix,last_number) VALUES($company,'Despatch',$year,'IRS',$start) ON CONFLICT(company_id,sequence_type,year) DO UPDATE SET last_number=last_number+1 RETURNING prefix,last_number"); Add(command, "$company", company); Add(command, "$year", year); Add(command, "$start", 1); using var reader = command.ExecuteReader(); reader.Read(); return $"IRS-{year}-{reader.GetInt32(1):D6}"; }
    private static void Relation(SqliteConnection c, SqliteTransaction tx, string company, string sourceType, string sourceId, string targetType, string targetId, string relation, string user, string now) { using var command = Command(c, tx, "INSERT INTO document_relations(id,company_id,source_document_type,source_document_id,target_document_type,target_document_id,relation_type,created_by,created_at) VALUES($id,$company,$sourceType,$sourceId,$targetType,$targetId,$relation,$user,$now)"); Add(command, "$id", Guid.NewGuid().ToString()); Add(command, "$company", company); Add(command, "$sourceType", sourceType); Add(command, "$sourceId", sourceId); Add(command, "$targetType", targetType); Add(command, "$targetId", targetId); Add(command, "$relation", relation); Add(command, "$user", user); Add(command, "$now", now); command.ExecuteNonQuery(); }
    private static void Audit(SqliteConnection c, SqliteTransaction tx, string company, string entityId, string user, string action, string values, string now) { using var command = Command(c, tx, "INSERT INTO audit_logs(id,user_id,company_id,entity_type,entity_id,action,new_values,created_at) VALUES($id,$user,$company,'DespatchDocument',$entity,$action,$values,$now)"); Add(command, "$id", Guid.NewGuid().ToString()); Add(command, "$user", user); Add(command, "$company", company); Add(command, "$entity", entityId); Add(command, "$action", action); Add(command, "$values", values); Add(command, "$now", now); command.ExecuteNonQuery(); }
}
