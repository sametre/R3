using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record PurchaseReceiptLineEdit(string OrderLineId, decimal Quantity, string? LocationId = null, string LotNo = "", string SerialNo = "", string Description = "");
public sealed record PurchaseInvoiceFromReceiptEdit(DateTime InvoiceDate, DateTime? DueDate, string Description, string? ExternalNumber = null, string? Series = null);

/// <summary>Alış irsaliyesini siparişten bağımsız belge olarak izler. Stok hareketinin tek sahibi
/// onay aşamasıdır; aynı irsaliye ikinci kez onaylanamaz.</summary>
public sealed class LocalPurchaseReceiptService(StoreDatabase database)
{
    public StoreDatabase Database { get; } = database;

    public DataTable Search(string companyId, string? search = null, string? status = null) => Database.Query("""
        SELECT r.id AS Id,COALESCE(r.receipt_no,'Taslak') AS IrsaliyeNo,r.receipt_date AS Tarih,
               a.code AS TedarikciKodu,a.name AS Tedarikci,COALESCE(o.document_no,'Taslak') AS SiparisNo,
               COUNT(l.id) AS Satir,COALESCE(SUM(l.quantity),0) AS Miktar,r.status AS Durum,r.description AS Aciklama
        FROM purchase_receipts r JOIN accounts a ON a.id=r.supplier_id
        JOIN purchase_documents o ON o.id=r.source_order_id LEFT JOIN purchase_receipt_lines l ON l.purchase_receipt_id=r.id
        WHERE r.company_id=$company AND ($status='' OR r.status=$status)
          AND ($search='' OR r.receipt_no LIKE $like OR o.document_no LIKE $like OR a.code LIKE $like OR a.name LIKE $like)
        GROUP BY r.id ORDER BY r.receipt_date DESC,r.created_at DESC
        """, ("$company", companyId), ("$status", status ?? ""), ("$search", search?.Trim() ?? ""), ("$like", $"%{search?.Trim() ?? ""}%"));

    public DataTable Lines(string receiptId) => Database.Query("""
        SELECT l.id AS Id,l.source_order_line_id AS SiparisSatirId,p.code AS UrunKodu,p.name AS Urun,
               u.code AS Birim,l.quantity AS Miktar,l.unit_cost AS BirimMaliyet,l.location_id AS Lokasyon,
               l.lot_no AS Lot,l.serial_no AS Seri
        FROM purchase_receipt_lines l JOIN products p ON p.id=l.product_id JOIN units u ON u.id=l.unit_id
        WHERE l.purchase_receipt_id=$id ORDER BY l.line_no
        """, ("$id", receiptId));

    public DataTable Header(string receiptId) => Database.Query("""
        SELECT r.id AS Id,r.company_id AS CompanyId,r.branch_id AS BranchId,r.warehouse_id AS WarehouseId,
               r.supplier_id AS SupplierId,r.receipt_no AS IrsaliyeNo,r.receipt_date AS Tarih,r.status AS Durum,
               COALESCE(a.code,'') AS TedarikciKodu,COALESCE(a.name,'') AS Tedarikci,
               COALESCE((SELECT address_line FROM account_addresses WHERE account_id=a.id AND is_default=1 LIMIT 1),'') AS Adres,
               '' AS VergiDairesi,COALESCE(a.tax_number,'') AS VergiNo,
               COALESCE(b.name,r.branch_id) AS Sube,COALESCE(w.name,r.warehouse_id) AS Depo
        FROM purchase_receipts r JOIN accounts a ON a.id=r.supplier_id
        LEFT JOIN branches b ON b.id=r.branch_id LEFT JOIN warehouses w ON w.id=r.warehouse_id
        WHERE r.id=$id
        """, ("$id", receiptId));

    /// <summary>Onaylanmış alış irsaliyesini tek seferlik alış faturasına dönüştürür.</summary>
    public string CreateInvoiceFromReceipt(string receiptId, PurchaseInvoiceFromReceiptEdit edit, string userName)
    {
        var header = Header(receiptId);
        if (header.Rows.Count == 0) throw new KeyNotFoundException("Alış irsaliyesi bulunamadı.");
        var h = header.Rows[0];
        if (!string.Equals(h["Durum"].ToString(), "Received", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fatura yalnızca depoya alınmış irsaliyeden oluşturulabilir.");
        var existing = Database.Query("SELECT target_document_id FROM document_relations WHERE source_document_type='PurchaseReceipt' AND source_document_id=$id AND target_document_type='PurchaseInvoice' LIMIT 1", ("$id", receiptId));
        if (existing.Rows.Count > 0) return existing.Rows[0][0].ToString()!;

        var rows = Database.Query("""
            SELECT r.product_id AS ProductId,r.variant_id AS VariantId,r.unit_id AS UnitId,r.quantity AS Quantity,
                   r.unit_cost AS UnitPrice,COALESCE(l.discount_rate,0) AS DiscountRate,COALESCE(l.vat_rate,p.purchase_vat_rate,0) AS VatRate,
                   p.name AS ProductName
            FROM purchase_receipt_lines r
            LEFT JOIN purchase_document_lines l ON l.id=r.source_order_line_id
            JOIN products p ON p.id=r.product_id WHERE r.purchase_receipt_id=$id ORDER BY r.line_no
            """, ("$id", receiptId));
        if (rows.Rows.Count == 0) throw new InvalidOperationException("Satır içermeyen irsaliyeden fatura oluşturulamaz.");
        var lines = rows.Rows.Cast<DataRow>().Select(r => new PurchaseLineEdit(
            r["ProductId"].ToString()!, r["UnitId"].ToString()!, Convert.ToDecimal(r["Quantity"]),
            Convert.ToDecimal(r["UnitPrice"]), Convert.ToDecimal(r["DiscountRate"]), Convert.ToDecimal(r["VatRate"]),
            r["VariantId"] is DBNull ? null : r["VariantId"].ToString(), r["ProductName"].ToString()!)).ToArray();
        var description = string.IsNullOrWhiteSpace(edit.Description) ? $"İrsaliyeden oluşturuldu: {h["IrsaliyeNo"]}" : edit.Description.Trim();
        var invoiceId = new LocalPurchasingService(Database).Create(new(
            h["CompanyId"].ToString()!, h["BranchId"].ToString()!, h["WarehouseId"].ToString()!, h["SupplierId"].ToString()!,
            "Invoice", edit.InvoiceDate, edit.DueDate, "TRY", description, lines), userName);
        using var connection = Database.OpenConnection(); using var transaction = connection.BeginTransaction();
        var now = DateTime.UtcNow.ToString("O");
        using (var update = connection.CreateCommand()) { update.Transaction = transaction; update.CommandText = "UPDATE purchase_documents SET expected_date=$due,external_document_no=$external,document_series=$series,updated_at=$now WHERE id=$id"; update.Parameters.AddWithValue("$due", (object?)edit.DueDate?.ToString("O") ?? DBNull.Value); update.Parameters.AddWithValue("$external", (object?)edit.ExternalNumber ?? DBNull.Value); update.Parameters.AddWithValue("$series", (object?)edit.Series ?? DBNull.Value); update.Parameters.AddWithValue("$now", now); update.Parameters.AddWithValue("$id", invoiceId); update.ExecuteNonQuery(); }
        using (var relation = connection.CreateCommand()) { relation.Transaction = transaction; relation.CommandText = "INSERT INTO document_relations(id,company_id,source_document_type,source_document_id,target_document_type,target_document_id,relation_type,created_by,created_at) VALUES($id,$company,'PurchaseReceipt',$receipt,'PurchaseInvoice',$invoice,'ReceiptToInvoice',$user,$now)"; relation.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); relation.Parameters.AddWithValue("$company", h["CompanyId"].ToString()!); relation.Parameters.AddWithValue("$receipt", receiptId); relation.Parameters.AddWithValue("$invoice", invoiceId); relation.Parameters.AddWithValue("$user", userName); relation.Parameters.AddWithValue("$now", now); relation.ExecuteNonQuery(); }
        transaction.Commit(); return invoiceId;
    }

    public string CreateFromOrder(string orderId, IReadOnlyList<PurchaseReceiptLineEdit> lines, string userName)
    {
        if (lines.Count == 0 || lines.All(x => x.Quantity <= 0)) throw new ArgumentException("İrsaliyeye en az bir teslim satırı ekleyin.");
        using var connection = Database.OpenConnection(); using var transaction = connection.BeginTransaction();
        var order = ReadOrder(connection, transaction, orderId);
        if (order.Status is not ("Approved" or "PartiallyReceived")) throw new InvalidOperationException("İrsaliye yalnızca onaylı veya kısmi teslim durumundaki siparişten oluşturulabilir.");
        var id = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O"); var number = AllocateNumber(connection, transaction, order.Company, order.Date.Year);
        using (var insert = Command(connection, transaction, "INSERT INTO purchase_receipts(id,company_id,branch_id,warehouse_id,supplier_id,source_order_id,receipt_no,receipt_date,status,description,created_at,created_by) VALUES($id,$company,$branch,$warehouse,$supplier,$order,$number,$date,'Draft',$description,$now,$user)"))
        { Add(insert,"$id",id); Add(insert,"$company",order.Company); Add(insert,"$branch",order.Branch); Add(insert,"$warehouse",order.Warehouse); Add(insert,"$supplier",order.Supplier); Add(insert,"$order",orderId); Add(insert,"$number",number); Add(insert,"$date",now); Add(insert,"$description",$"Sipariş {order.Number} teslimi"); Add(insert,"$now",now); Add(insert,"$user",userName); insert.ExecuteNonQuery(); }
        var lineNo = 1;
        foreach (var edit in lines.Where(x => x.Quantity > 0))
        {
            using var read = Command(connection, transaction, "SELECT l.product_id,l.variant_id,l.unit_id,l.quantity,l.received_quantity,l.unit_price,p.is_active FROM purchase_document_lines l JOIN products p ON p.id=l.product_id WHERE l.id=$line AND l.purchase_document_id=$order");
            Add(read,"$line",edit.OrderLineId); Add(read,"$order",orderId); using var reader = read.ExecuteReader();
            if (!reader.Read()) throw new KeyNotFoundException("Sipariş satırı bulunamadı.");
            var product = reader.GetString(0); var variant = reader.IsDBNull(1) ? null : reader.GetString(1); var unit = reader.GetString(2); var ordered = reader.GetDecimal(3); var received = reader.GetDecimal(4); var cost = reader.GetDecimal(5); var active = reader.GetBoolean(6); reader.Close();
            if (!active) throw new InvalidOperationException("Pasif ürün için alış irsaliyesi oluşturulamaz.");
            if (edit.Quantity > ordered - received) throw new InvalidOperationException("Teslim miktarı sipariş satırındaki kalan miktarı aşamaz.");
            using var insert = Command(connection, transaction, "INSERT INTO purchase_receipt_lines(id,purchase_receipt_id,source_order_line_id,line_no,product_id,variant_id,unit_id,quantity,unit_cost,location_id,lot_no,serial_no,description) VALUES($id,$receipt,$orderLine,$line,$product,$variant,$unit,$quantity,$cost,$location,$lot,$serial,$description)");
            Add(insert,"$id",Guid.NewGuid().ToString()); Add(insert,"$receipt",id); Add(insert,"$orderLine",edit.OrderLineId); Add(insert,"$line",lineNo++); Add(insert,"$product",product); Add(insert,"$variant",(object?)variant??DBNull.Value); Add(insert,"$unit",unit); Add(insert,"$quantity",edit.Quantity); Add(insert,"$cost",cost); Add(insert,"$location",(object?)edit.LocationId??DBNull.Value); Add(insert,"$lot",edit.LotNo); Add(insert,"$serial",edit.SerialNo); Add(insert,"$description",edit.Description); insert.ExecuteNonQuery();
        }
        Relation(connection, transaction, order.Company, "PurchaseOrder", orderId, "PurchaseReceipt", id, "OrderToReceipt", userName, now);
        Audit(connection, transaction, order.Company, id, userName, "AlışIrsaliyesiOlusturuldu", $"orderId={orderId};receiptNo={number}", now);
        transaction.Commit(); return id;
    }

    public void Approve(string receiptId, string userName)
    {
        var lines = Lines(receiptId).Rows.Cast<DataRow>().Select(row => new PurchaseReceiptLine(row["SiparisSatirId"].ToString()!, Convert.ToDecimal(row["Miktar"]))).ToArray();
        if (lines.Length == 0) throw new InvalidOperationException("Boş irsaliye onaylanamaz.");
        var receipt = Database.Query("SELECT source_order_id,status FROM purchase_receipts WHERE id=$id", ("$id", receiptId));
        if (receipt.Rows.Count == 0) throw new KeyNotFoundException("Alış irsaliyesi bulunamadı.");
        if (receipt.Rows[0]["status"].ToString() != "Draft") throw new InvalidOperationException("Bu irsaliye daha önce işlenmiş.");
        new LocalPurchasingService(Database).Receive(receipt.Rows[0]["source_order_id"].ToString()!, lines, userName);
        using var connection = Database.OpenConnection(); using var transaction = connection.BeginTransaction(); var now = DateTime.UtcNow.ToString("O");
        using (var update = Command(connection, transaction, "UPDATE purchase_receipts SET status='Received',approved_at=$now,approved_by=$user WHERE id=$id")) { Add(update,"$now",now); Add(update,"$user",userName); Add(update,"$id",receiptId); update.ExecuteNonQuery(); }
        var company = Convert.ToString(Scalar(connection, transaction, "SELECT company_id FROM purchase_receipts WHERE id=$id", ("$id",receiptId)))!;
        Audit(connection, transaction, company, receiptId, userName, "AlışIrsaliyesiOnaylandi", "inventory=PurchaseReceipt", now); transaction.Commit();
    }

    public void Cancel(string receiptId, string userName)
    {
        using var connection = Database.OpenConnection(); using var transaction = connection.BeginTransaction();
        var row = Database.Query("SELECT company_id,status FROM purchase_receipts WHERE id=$id", ("$id",receiptId)); if (row.Rows.Count == 0) throw new KeyNotFoundException("Alış irsaliyesi bulunamadı.");
        if (row.Rows[0]["status"].ToString() != "Draft") throw new InvalidOperationException("Yalnızca taslak irsaliye iptal edilebilir.");
        using var update = Command(connection, transaction, "UPDATE purchase_receipts SET status='Cancelled' WHERE id=$id"); Add(update,"$id",receiptId); update.ExecuteNonQuery();
        Audit(connection, transaction, row.Rows[0]["company_id"].ToString()!, receiptId, userName, "AlışBelgesiIptalEdildi", "type=PurchaseReceipt", DateTime.UtcNow.ToString("O")); transaction.Commit();
    }

    private sealed record Order(string Company,string Branch,string Warehouse,string Supplier,string Number,string Status,DateTime Date);
    private static Order ReadOrder(SqliteConnection c, SqliteTransaction t, string id) { using var cmd=Command(c,t,"SELECT company_id,branch_id,warehouse_id,supplier_id,COALESCE(document_no,''),status,document_date FROM purchase_documents WHERE id=$id AND document_type='Order'"); Add(cmd,"$id",id); using var r=cmd.ExecuteReader(); if(!r.Read()) throw new KeyNotFoundException("Satınalma siparişi bulunamadı."); return new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),DateTime.Parse(r.GetString(6))); }
    private static string AllocateNumber(SqliteConnection c,SqliteTransaction t,string company,int year){using var cmd=Command(c,t,"INSERT INTO number_sequences(company_id,sequence_type,year,prefix,last_number) VALUES($company,'PurchaseReceipt',$year,'AIRS',1) ON CONFLICT(company_id,sequence_type,year) DO UPDATE SET last_number=last_number+1 RETURNING prefix,last_number");Add(cmd,"$company",company);Add(cmd,"$year",year);using var r=cmd.ExecuteReader();r.Read();return $"{r.GetString(0)}-{year}-{r.GetInt32(1):D6}";}
    private static void Relation(SqliteConnection c,SqliteTransaction t,string company,string sourceType,string sourceId,string targetType,string targetId,string relation,string user,string now){using var cmd=Command(c,t,"INSERT OR IGNORE INTO document_relations(id,company_id,source_document_type,source_document_id,target_document_type,target_document_id,relation_type,created_by,created_at) VALUES($id,$company,$sourceType,$sourceId,$targetType,$targetId,$relation,$user,$now)");Add(cmd,"$id",Guid.NewGuid().ToString());Add(cmd,"$company",company);Add(cmd,"$sourceType",sourceType);Add(cmd,"$sourceId",sourceId);Add(cmd,"$targetType",targetType);Add(cmd,"$targetId",targetId);Add(cmd,"$relation",relation);Add(cmd,"$user",user);Add(cmd,"$now",now);cmd.ExecuteNonQuery();}
    private static void Audit(SqliteConnection c,SqliteTransaction t,string company,string entity,string user,string action,string values,string now){using var cmd=Command(c,t,"INSERT INTO audit_logs(id,user_id,company_id,entity_type,entity_id,action,new_values,created_at) VALUES($id,$user,$company,'PurchaseReceipt',$entity,$action,$values,$now)");Add(cmd,"$id",Guid.NewGuid().ToString());Add(cmd,"$user",user);Add(cmd,"$company",company);Add(cmd,"$entity",entity);Add(cmd,"$action",action);Add(cmd,"$values",values);Add(cmd,"$now",now);cmd.ExecuteNonQuery();}
    private static object Scalar(SqliteConnection c,SqliteTransaction t,string sql,params (string Name,object Value)[] ps){using var cmd=Command(c,t,sql);foreach(var p in ps)Add(cmd,p.Name,p.Value);return cmd.ExecuteScalar()??"";}
    private static SqliteCommand Command(SqliteConnection c,SqliteTransaction t,string sql){var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText=sql;return cmd;}
    private static void Add(SqliteCommand c,string n,object v)=>c.Parameters.AddWithValue(n,v);
}
