using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

// Electronic Document Engine core (spec §7-23). Invoice/Shipment stay the business source of truth;
// this table is only their GİB/UBL representation. No provider/outbox/UBL-generation code here yet —
// that is Phase 3+. This phase only owns: idempotent creation, the status state machine, the event
// log, and immutable payload storage.
public sealed class LocalElectronicDocumentService(StoreDatabase database)
{
    private static readonly Dictionary<ElectronicDocumentStatus, ElectronicDocumentStatus[]> Allowed = new()
    {
        [ElectronicDocumentStatus.Draft] = [ElectronicDocumentStatus.Ready],
        [ElectronicDocumentStatus.Ready] = [ElectronicDocumentStatus.Generated],
        [ElectronicDocumentStatus.Generated] = [ElectronicDocumentStatus.Queued],
        [ElectronicDocumentStatus.Queued] = [ElectronicDocumentStatus.Sending],
        [ElectronicDocumentStatus.Sending] = [ElectronicDocumentStatus.Sent, ElectronicDocumentStatus.Failed],
        [ElectronicDocumentStatus.Sent] = [ElectronicDocumentStatus.Delivered, ElectronicDocumentStatus.Failed],
        [ElectronicDocumentStatus.Delivered] = [ElectronicDocumentStatus.Accepted, ElectronicDocumentStatus.Rejected],
        [ElectronicDocumentStatus.Accepted] = [ElectronicDocumentStatus.Archived, ElectronicDocumentStatus.CancellationRequested],
        [ElectronicDocumentStatus.Rejected] = [],
        [ElectronicDocumentStatus.Failed] = [ElectronicDocumentStatus.Queued, ElectronicDocumentStatus.CancellationRequested],
        [ElectronicDocumentStatus.CancellationRequested] = [ElectronicDocumentStatus.Cancelled],
        [ElectronicDocumentStatus.Cancelled] = [],
        [ElectronicDocumentStatus.Archived] = [],
    };

    // Idempotent: same (CompanyId, DocumentType, SourceEntityType, SourceEntityId) returns the existing,
    // non-cancelled document instead of creating a duplicate (spec §23). Also rejects a reused UUID
    // outright (spec §38 duplicate incoming control; applies to outgoing generation just as well).
    public string CreateOrGetForSource(ElectronicDocumentDraft draft)
    {
        using var c = Open(); c.Open(); using var tx = c.BeginTransaction();
        using (var existing = c.CreateCommand())
        {
            existing.Transaction = tx;
            existing.CommandText = "SELECT id FROM electronic_documents WHERE company_id=$c AND document_type=$t AND source_entity_type=$st AND source_entity_id=$si AND status<>'Cancelled'";
            Add(existing, "$c", draft.CompanyId); Add(existing, "$t", draft.DocumentType.ToString()); Add(existing, "$st", draft.SourceEntityType); Add(existing, "$si", draft.SourceEntityId);
            if (existing.ExecuteScalar() is string foundId) { tx.Commit(); return foundId; }
        }
        var uuid = draft.Uuid ?? Guid.NewGuid().ToString();
        using (var dup = c.CreateCommand())
        {
            dup.Transaction = tx; dup.CommandText = "SELECT COUNT(1) FROM electronic_documents WHERE uuid=$u"; Add(dup, "$u", uuid);
            if (Convert.ToInt32(dup.ExecuteScalar()) > 0) throw new InvalidOperationException("Bu UUID ile bir elektronik belge zaten kayıtlı.");
        }
        var id = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        using (var insert = c.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO electronic_documents(id,company_id,branch_id,document_type,direction,source_entity_type,source_entity_id,account_id,document_number,uuid,provider_type,status,issue_date,currency_code,payable_amount,recipient_snapshot_json,created_at,created_by,updated_at,updated_by)
                VALUES($id,$c,$b,$t,$dir,$st,$si,$a,$no,$uuid,'','Draft',$issue,$cur,$amt,$snap,$now,$by,$now,$by)
                """;
            Add(insert, "$id", id); Add(insert, "$c", draft.CompanyId); Add(insert, "$b", draft.BranchId); Add(insert, "$t", draft.DocumentType.ToString()); Add(insert, "$dir", draft.Direction.ToString());
            Add(insert, "$st", draft.SourceEntityType); Add(insert, "$si", draft.SourceEntityId); AddNullable(insert, "$a", draft.AccountId); AddNullable(insert, "$no", draft.DocumentNumber);
            Add(insert, "$uuid", uuid); Add(insert, "$issue", draft.IssueDate.ToString("O")); Add(insert, "$cur", draft.CurrencyCode);
            if (draft.PayableAmount is { } amount) Add(insert, "$amt", amount); else insert.Parameters.AddWithValue("$amt", DBNull.Value);
            Add(insert, "$snap", draft.RecipientSnapshotJson); Add(insert, "$now", now); Add(insert, "$by", draft.CreatedBy);
            insert.ExecuteNonQuery();
        }
        InsertEvent(c, tx, id, "ElectronicDocumentCreated", null, "Draft", draft.CreatedBy);
        tx.Commit();
        return id;
    }

    public void Ready(string id, string userId) => Transition(id, ElectronicDocumentStatus.Ready, "DocumentReady", userId);
    public void Generate(string id, string userId) => Transition(id, ElectronicDocumentStatus.Generated, "DocumentGenerated", userId);
    public void Queue(string id, string userId) => Transition(id, ElectronicDocumentStatus.Queued, "DocumentQueued", userId);
    public void Retry(string id, string userId) => Transition(id, ElectronicDocumentStatus.Queued, "DocumentRetryRequested", userId);
    public void StartSending(string id, string userId) => Transition(id, ElectronicDocumentStatus.Sending, "DocumentSendingStarted", userId);
    public void MarkSent(string id, string userId, string? providerDocumentId = null) => Transition(id, ElectronicDocumentStatus.Sent, "DocumentSent", userId, providerDocumentId: providerDocumentId);
    public void MarkDelivered(string id, string userId) => Transition(id, ElectronicDocumentStatus.Delivered, "DocumentDelivered", userId);
    public void Accept(string id, string userId, string? providerMessage = null) => Transition(id, ElectronicDocumentStatus.Accepted, "DocumentAccepted", userId, providerMessage: providerMessage);
    public void Reject(string id, string userId, string? providerMessage = null) => Transition(id, ElectronicDocumentStatus.Rejected, "DocumentRejected", userId, providerMessage: providerMessage);
    public void Fail(string id, string userId, string errorCode, string errorMessage, DateTime? nextRetryAt = null) => Transition(id, ElectronicDocumentStatus.Failed, "DocumentFailed", userId, errorCode: errorCode, errorMessage: errorMessage, nextRetryAt: nextRetryAt);
    public void RequestCancellation(string id, string userId) => Transition(id, ElectronicDocumentStatus.CancellationRequested, "DocumentCancellationRequested", userId);
    public void Cancel(string id, string userId) => Transition(id, ElectronicDocumentStatus.Cancelled, "DocumentCancelled", userId);
    public void Archive(string id, string userId) => Transition(id, ElectronicDocumentStatus.Archived, "DocumentArchived", userId);

    private void Transition(string id, ElectronicDocumentStatus target, string eventType, string? userId, string? providerCode = null, string? providerMessage = null, string? providerDocumentId = null, string? errorCode = null, string? errorMessage = null, DateTime? nextRetryAt = null)
    {
        using var c = Open(); c.Open(); using var tx = c.BeginTransaction();
        using var read = c.CreateCommand(); read.Transaction = tx; read.CommandText = "SELECT status FROM electronic_documents WHERE id=$id"; Add(read, "$id", id);
        var currentText = read.ExecuteScalar() as string ?? throw new KeyNotFoundException("Elektronik belge bulunamadı.");
        var current = Enum.Parse<ElectronicDocumentStatus>(currentText);
        if (!Allowed.TryGetValue(current, out var next) || !next.Contains(target)) throw new InvalidOperationException($"{current} durumundan {target} durumuna geçiş yapılamaz.");
        var now = DateTime.UtcNow.ToString("O");
        var timestampColumn = target switch
        {
            ElectronicDocumentStatus.Generated => "generated_at", ElectronicDocumentStatus.Queued => "queued_at", ElectronicDocumentStatus.Sent => "sent_at",
            ElectronicDocumentStatus.Delivered => "delivered_at", ElectronicDocumentStatus.Accepted => "accepted_at", ElectronicDocumentStatus.Rejected => "rejected_at",
            ElectronicDocumentStatus.Cancelled => "cancelled_at", _ => null
        };
        var extra = timestampColumn != null ? $",{timestampColumn}=$now" : "";
        if (target == ElectronicDocumentStatus.Sending) extra += ",send_attempt_count=send_attempt_count+1,last_attempt_at=$now";
        if (errorCode != null || errorMessage != null) extra += ",last_error_code=$errcode,last_error_message=$errmsg";
        if (nextRetryAt.HasValue) extra += ",next_retry_at=$retry";
        if (providerDocumentId != null) extra += ",provider_document_id=$provdoc";
        using var update = c.CreateCommand(); update.Transaction = tx;
        update.CommandText = $"UPDATE electronic_documents SET status=$status,updated_at=$now,updated_by=$by{extra} WHERE id=$id";
        Add(update, "$status", target.ToString()); Add(update, "$now", now); Add(update, "$by", userId ?? ""); Add(update, "$id", id);
        if (errorCode != null || errorMessage != null) { AddNullable(update, "$errcode", errorCode); AddNullable(update, "$errmsg", errorMessage); }
        if (nextRetryAt.HasValue) Add(update, "$retry", nextRetryAt.Value.ToString("O"));
        if (providerDocumentId != null) Add(update, "$provdoc", providerDocumentId);
        update.ExecuteNonQuery();
        InsertEvent(c, tx, id, eventType, currentText, target.ToString(), userId, providerCode, providerMessage);
        tx.Commit();
    }

    // Payload immutability (spec §17, §69-70): once the document has left Sending, its UBL/signed XML
    // cannot be silently overwritten — only a brand-new version could, and only before that point.
    public string SavePayload(string electronicDocumentId, ElectronicDocumentPayloadType payloadType, string content, string mimeType, bool isSigned, string userId)
    {
        using var c = Open(); c.Open(); using var tx = c.BeginTransaction();
        using var status = c.CreateCommand(); status.Transaction = tx; status.CommandText = "SELECT status FROM electronic_documents WHERE id=$id"; Add(status, "$id", electronicDocumentId);
        var statusText = status.ExecuteScalar() as string ?? throw new KeyNotFoundException("Elektronik belge bulunamadı.");
        if (payloadType is ElectronicDocumentPayloadType.UblXml or ElectronicDocumentPayloadType.SignedXml)
        {
            var locked = Enum.Parse<ElectronicDocumentStatus>(statusText) is ElectronicDocumentStatus.Sent or ElectronicDocumentStatus.Delivered or ElectronicDocumentStatus.Accepted
                or ElectronicDocumentStatus.Rejected or ElectronicDocumentStatus.Archived or ElectronicDocumentStatus.Cancelled;
            using var exists = c.CreateCommand(); exists.Transaction = tx; exists.CommandText = "SELECT COUNT(1) FROM electronic_document_payloads WHERE electronic_document_id=$id AND payload_type=$t";
            Add(exists, "$id", electronicDocumentId); Add(exists, "$t", payloadType.ToString());
            if (locked && Convert.ToInt32(exists.ExecuteScalar()) > 0) throw new InvalidOperationException("Gönderilmiş belgenin XML içeriği değiştirilemez; yeniden düzenleme (iptal + Reissue) akışı kullanılmalıdır.");
        }
        int version;
        using (var v = c.CreateCommand()) { v.Transaction = tx; v.CommandText = "SELECT COALESCE(MAX(version),0)+1 FROM electronic_document_payloads WHERE electronic_document_id=$id AND payload_type=$t"; Add(v, "$id", electronicDocumentId); Add(v, "$t", payloadType.ToString()); version = Convert.ToInt32(v.ExecuteScalar()); }
        var id = Guid.NewGuid().ToString(); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        using (var insert = c.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO electronic_document_payloads(id,electronic_document_id,payload_type,content,content_hash,mime_type,version,is_signed,created_at) VALUES($id,$doc,$type,$content,$hash,$mime,$ver,$signed,$now)";
            Add(insert, "$id", id); Add(insert, "$doc", electronicDocumentId); Add(insert, "$type", payloadType.ToString()); Add(insert, "$content", content); Add(insert, "$hash", hash);
            Add(insert, "$mime", mimeType); Add(insert, "$ver", version); Add(insert, "$signed", isSigned ? 1 : 0); Add(insert, "$now", DateTime.UtcNow.ToString("O"));
            insert.ExecuteNonQuery();
        }
        InsertEvent(c, tx, electronicDocumentId, $"Payload{payloadType}Saved", statusText, statusText, userId);
        tx.Commit();
        return id;
    }

    public ElectronicDocumentRow? Get(string id)
    {
        var t = database.Query("SELECT * FROM electronic_documents WHERE id=$id", ("$id", id));
        return t.Rows.Count == 0 ? null : ToRow(t.Rows[0]);
    }

    // The outbox is a projection over electronic_documents, not a separate table: status/attempt
    // columns already carry everything spec §58's Outbox screen needs, so a second copy of that
    // state would just be a synchronization bug waiting to happen.
    public IReadOnlyList<string> GetDueForSending(string companyId) => database
        .Query("SELECT id FROM electronic_documents WHERE company_id=$c AND status='Queued' AND (next_retry_at IS NULL OR next_retry_at<=$now) ORDER BY created_at",
            ("$c", companyId), ("$now", DateTime.UtcNow.ToString("O")))
        .Rows.Cast<DataRow>().Select(r => r[0].ToString()!).ToList();

    public IReadOnlyList<string> GetDueForRetryPromotion(string companyId) => database
        .Query("SELECT id FROM electronic_documents WHERE company_id=$c AND status='Failed' AND next_retry_at IS NOT NULL AND next_retry_at<=$now",
            ("$c", companyId), ("$now", DateTime.UtcNow.ToString("O")))
        .Rows.Cast<DataRow>().Select(r => r[0].ToString()!).ToList();

    public DataTable GetOutbox(string companyId) => database.Query("""
        SELECT d.id AS Id,d.created_at AS Olusturma,d.document_type AS BelgeTipi,d.document_number AS BelgeNo,
               COALESCE(a.name,'') AS Cari,d.status AS Durum,d.send_attempt_count AS Deneme,d.last_attempt_at AS SonDeneme,
               d.next_retry_at AS SonrakiDeneme,d.last_error_message AS SonHata
        FROM electronic_documents d LEFT JOIN accounts a ON a.id=d.account_id
        WHERE d.company_id=$c AND d.status IN ('Queued','Sending','Failed')
        ORDER BY d.created_at
        """, ("$c", companyId));

    public ElectronicDocumentPayloadRow? LatestSendablePayload(string electronicDocumentId) => GetPayloads(electronicDocumentId)
        .Where(p => p.PayloadType is ElectronicDocumentPayloadType.SignedXml or ElectronicDocumentPayloadType.UblXml)
        .OrderByDescending(p => p.PayloadType == ElectronicDocumentPayloadType.SignedXml).ThenByDescending(p => p.Version)
        .FirstOrDefault();

    public DataTable Search(string companyId, string? documentType = null, string? status = null, string? search = null) => database.Query("""
        SELECT d.id AS Id,d.document_type AS BelgeTipi,d.direction AS Yon,d.document_number AS BelgeNo,d.uuid AS UUID,
               COALESCE(a.code,'') AS CariKod,COALESCE(a.name,'') AS Cari,d.status AS Durum,d.issue_date AS BelgeTarihi,
               d.payable_amount AS Tutar,d.send_attempt_count AS Deneme,d.last_error_message AS SonHata
        FROM electronic_documents d LEFT JOIN accounts a ON a.id=d.account_id
        WHERE d.company_id=$c AND ($t='' OR d.document_type=$t) AND ($s='' OR d.status=$s)
              AND ($q='' OR d.document_number LIKE $q OR d.uuid LIKE $q OR a.code LIKE $q OR a.name LIKE $q)
        ORDER BY d.issue_date DESC
        """, ("$c", companyId), ("$t", documentType ?? ""), ("$s", status ?? ""), ("$q", $"%{search?.Trim() ?? ""}%"));

    public IReadOnlyList<ElectronicDocumentEventRow> GetEvents(string electronicDocumentId) => database
        .Query("SELECT * FROM electronic_document_events WHERE electronic_document_id=$id ORDER BY occurred_at", ("$id", electronicDocumentId))
        .Rows.Cast<DataRow>().Select(r => new ElectronicDocumentEventRow(r["id"].ToString()!, r["electronic_document_id"].ToString()!, r["event_type"].ToString()!,
            r["old_status"] as string, r["new_status"] as string, r["provider_code"] as string, r["provider_message"] as string,
            DateTime.Parse(r["occurred_at"].ToString()!), r["user_id"] as string)).ToList();

    public IReadOnlyList<ElectronicDocumentPayloadRow> GetPayloads(string electronicDocumentId) => database
        .Query("SELECT * FROM electronic_document_payloads WHERE electronic_document_id=$id ORDER BY payload_type,version", ("$id", electronicDocumentId))
        .Rows.Cast<DataRow>().Select(r => new ElectronicDocumentPayloadRow(r["id"].ToString()!, r["electronic_document_id"].ToString()!,
            Enum.Parse<ElectronicDocumentPayloadType>(r["payload_type"].ToString()!), r["content"].ToString()!, r["content_hash"].ToString()!,
            r["mime_type"].ToString()!, Convert.ToInt32(r["version"]), Convert.ToBoolean(r["is_signed"]), DateTime.Parse(r["created_at"].ToString()!))).ToList();

    public ElectronicDocumentCompanyProfileEdit GetCompanyProfile(string companyId)
    {
        var t = database.Query("SELECT * FROM electronic_document_company_profiles WHERE company_id=$id", ("$id", companyId));
        if (t.Rows.Count == 0) return new(companyId, "", "", "", "", "", "Test", false, true, "Temel", "", "", "", "", "", "");
        var r = t.Rows[0];
        return new(companyId, S(r, "tax_number"), S(r, "legal_title"), S(r, "default_einvoice_alias"), S(r, "default_edespatch_alias"),
            S(r, "provider_type"), S(r, "environment"), Convert.ToBoolean(r["auto_send"]), Convert.ToBoolean(r["auto_check_recipient"]),
            S(r, "default_invoice_scenario"), S(r, "earchive_sender_email"), S(r, "earchive_unit_code"), S(r, "internet_sales_unit_code"),
            S(r, "internet_website"), S(r, "carrier_tax_number"), S(r, "carrier_title"));
    }

    public void SaveCompanyProfile(ElectronicDocumentCompanyProfileEdit profile)
    {
        database.Execute("""
            INSERT INTO electronic_document_company_profiles(company_id,tax_number,legal_title,default_einvoice_alias,default_edespatch_alias,provider_type,environment,auto_send,auto_check_recipient,default_invoice_scenario,earchive_sender_email,earchive_unit_code,internet_sales_unit_code,internet_website,carrier_tax_number,carrier_title,updated_at)
            VALUES($c,$tax,$legal,$einv,$edisp,$prov,$env,$auto,$check,$scenario,$email,$unit,$netunit,$web,$ctax,$ctitle,$now)
            ON CONFLICT(company_id) DO UPDATE SET tax_number=$tax,legal_title=$legal,default_einvoice_alias=$einv,default_edespatch_alias=$edisp,
                provider_type=$prov,environment=$env,auto_send=$auto,auto_check_recipient=$check,default_invoice_scenario=$scenario,
                earchive_sender_email=$email,earchive_unit_code=$unit,internet_sales_unit_code=$netunit,internet_website=$web,
                carrier_tax_number=$ctax,carrier_title=$ctitle,updated_at=$now
            """,
            ("$c", profile.CompanyId), ("$tax", profile.TaxNumber), ("$legal", profile.LegalTitle), ("$einv", profile.DefaultEInvoiceAlias), ("$edisp", profile.DefaultEDespatchAlias),
            ("$prov", profile.ProviderType), ("$env", profile.Environment), ("$auto", profile.AutoSend ? 1 : 0), ("$check", profile.AutoCheckRecipient ? 1 : 0),
            ("$scenario", profile.DefaultInvoiceScenario), ("$email", profile.EArchiveSenderEmail), ("$unit", profile.EArchiveUnitCode), ("$netunit", profile.InternetSalesUnitCode),
            ("$web", profile.InternetWebsite), ("$ctax", profile.CarrierTaxNumber), ("$ctitle", profile.CarrierTitle), ("$now", DateTime.UtcNow.ToString("O")));
    }

    private static ElectronicDocumentRow ToRow(DataRow r) => new(
        r["id"].ToString()!, r["company_id"].ToString()!, r["branch_id"].ToString()!, Enum.Parse<ElectronicDocumentType>(r["document_type"].ToString()!),
        Enum.Parse<ElectronicDocumentDirection>(r["direction"].ToString()!), r["source_entity_type"].ToString()!, r["source_entity_id"].ToString()!,
        r["account_id"] as string, r["document_number"] as string, r["uuid"].ToString()!, r["envelope_id"] as string, r["provider_document_id"] as string,
        S(r, "provider_type"), Enum.Parse<ElectronicDocumentStatus>(r["status"].ToString()!), DateTime.Parse(r["issue_date"].ToString()!), S(r, "currency_code"),
        r["payable_amount"] is DBNull ? null : Convert.ToDecimal(r["payable_amount"]), S(r, "recipient_snapshot_json"), Convert.ToInt32(r["send_attempt_count"]),
        r["last_attempt_at"] is DBNull ? null : DateTime.Parse(r["last_attempt_at"].ToString()!), r["next_retry_at"] is DBNull ? null : DateTime.Parse(r["next_retry_at"].ToString()!),
        r["last_error_code"] as string, r["last_error_message"] as string, DateTime.Parse(r["created_at"].ToString()!), S(r, "created_by"));

    private static string S(DataRow r, string column) => r[column] is DBNull ? "" : r[column].ToString()!;
    private static void InsertEvent(SqliteConnection c, SqliteTransaction tx, string documentId, string eventType, string? oldStatus, string? newStatus, string? userId, string? providerCode = null, string? providerMessage = null)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO electronic_document_events(id,electronic_document_id,event_type,old_status,new_status,provider_code,provider_message,occurred_at,user_id) VALUES($id,$doc,$type,$old,$new,$pcode,$pmsg,$now,$user)";
        Add(cmd, "$id", Guid.NewGuid().ToString()); Add(cmd, "$doc", documentId); Add(cmd, "$type", eventType); AddNullable(cmd, "$old", oldStatus); AddNullable(cmd, "$new", newStatus);
        AddNullable(cmd, "$pcode", providerCode); AddNullable(cmd, "$pmsg", providerMessage); Add(cmd, "$now", DateTime.UtcNow.ToString("O")); AddNullable(cmd, "$user", userId);
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open() => new($"Data Source={database.Path};Foreign Keys=True;Default Timeout=5");
    private static void Add(SqliteCommand c, string n, object v) => c.Parameters.AddWithValue(n, v);
    private static void AddNullable(SqliteCommand c, string n, string? v) => c.Parameters.AddWithValue(n, (object?)v ?? DBNull.Value);
}
