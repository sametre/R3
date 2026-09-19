using System.Data;

namespace R3.Infrastructure;

// Phase 9 (§5/§36): read-only projections for the operations screens (dashboard, outgoing list,
// outbox queue, error center). Deliberately a separate class from LocalElectronicDocumentService
// (Phase 2, owns the status state machine + event log) and ElectronicDocumentOutboxService
// (Phase 3, owns claim/retry/dead-letter) - this one never writes, it only shapes the same tables
// those two already own into UI-ready DataTables, so the write-path classes stay exactly as they
// were. Every query here is a single join, never N+1 (spec §35): one row in, one row out.
public sealed record ElectronicDocumentDashboardSummary(
    int CreatedToday, int Generated, int Queued, int Sent, int Accepted, int Rejected, int RetryPending, int DeadLetter, int Failed);

// Server/service-side filter (spec §9/§13/§34) - every predicate here becomes a SQL WHERE clause,
// never a client-side LINQ filter over an already-loaded table.
public sealed record ElectronicDocumentFilter(
    DateTime? Start = null, DateTime? End = null, string? DocumentType = null, string? Status = null,
    string? OutboxStatus = null, string? Search = null, int Limit = 100);

public sealed class ElectronicDocumentOperationsService(StoreDatabase database)
{
    public ElectronicDocumentDashboardSummary GetDashboardSummary(string companyId)
    {
        var row = database.Query("""
            SELECT
              SUM(CASE WHEN date(d.created_at)=date('now','localtime') THEN 1 ELSE 0 END) AS CreatedToday,
              SUM(CASE WHEN d.status='Generated' THEN 1 ELSE 0 END) AS Generated,
              SUM(CASE WHEN d.status='Queued' THEN 1 ELSE 0 END) AS Queued,
              SUM(CASE WHEN d.status='Sent' THEN 1 ELSE 0 END) AS Sent,
              SUM(CASE WHEN d.status='Accepted' THEN 1 ELSE 0 END) AS Accepted,
              SUM(CASE WHEN d.status='Rejected' THEN 1 ELSE 0 END) AS Rejected,
              SUM(CASE WHEN d.status='Failed' THEN 1 ELSE 0 END) AS Failed
            FROM electronic_documents d WHERE d.company_id=$c
            """, ("$c", companyId)).Rows[0];
        var outbox = database.Query("""
            SELECT
              SUM(CASE WHEN o.status='Pending' AND o.attempt_count>0 THEN 1 ELSE 0 END) AS RetryPending,
              SUM(CASE WHEN o.status='DeadLetter' THEN 1 ELSE 0 END) AS DeadLetter
            FROM electronic_document_outbox o JOIN electronic_documents d ON d.id=o.electronic_document_id
            WHERE d.company_id=$c
            """, ("$c", companyId)).Rows[0];
        return new(N(row["CreatedToday"]), N(row["Generated"]), N(row["Queued"]), N(row["Sent"]), N(row["Accepted"]), N(row["Rejected"]),
            N(outbox["RetryPending"]), N(outbox["DeadLetter"]), N(row["Failed"]));
    }

    // §6: real GROUP BY, zero-filled for types that legitimately have no documents yet (e.g.
    // EDespatch before a real e-İrsaliye flow exists) - never a fabricated placeholder number.
    public IReadOnlyDictionary<ElectronicDocumentType, int> GetTypeDistribution(string companyId)
    {
        var counts = Enum.GetValues<ElectronicDocumentType>().ToDictionary(t => t, _ => 0);
        var table = database.Query("SELECT document_type,COUNT(*) AS Cnt FROM electronic_documents WHERE company_id=$c AND status<>'Cancelled' GROUP BY document_type", ("$c", companyId));
        foreach (DataRow row in table.Rows) if (Enum.TryParse<ElectronicDocumentType>(row["document_type"].ToString(), out var type)) counts[type] = Convert.ToInt32(row["Cnt"]);
        return counts;
    }

    // §8-9: Giden Belgeler. No "Profil" column - UBL scenario (Temel/Ticari/...) is a company-wide
    // setting read at generation time, never stored per document, so a later profile change would
    // make a per-row value misleading; omitted rather than faked (see docs/architecture/EDOCUMENT-OPERATIONS.md).
    public DataTable SearchOutgoing(string companyId, ElectronicDocumentFilter filter) => database.Query("""
        SELECT d.id AS Id,d.issue_date AS BelgeTarihi,d.document_type AS BelgeTipi,COALESCE(d.document_number,'Taslak') AS BelgeNo,
               COALESCE(a.name,'') AS Cari,COALESCE(NULLIF(a.tax_number,''),a.identity_number,'') AS VknTckn,d.uuid AS UUID,
               d.payable_amount AS Tutar,d.status AS Durum,d.provider_type AS Provider,d.sent_at AS GonderimTarihi,
               d.send_attempt_count AS Deneme,d.last_error_message AS SonHata,d.source_entity_type AS KaynakTipi,d.source_entity_id AS KaynakId,d.account_id AS AccountId
        FROM electronic_documents d LEFT JOIN accounts a ON a.id=d.account_id
        WHERE d.company_id=$c
          AND ($start IS NULL OR d.issue_date>=$start) AND ($end IS NULL OR d.issue_date<=$end)
          AND ($type='' OR d.document_type=$type) AND ($status='' OR d.status=$status)
          AND ($q='' OR d.document_number LIKE $like OR d.uuid LIKE $like OR a.name LIKE $like OR a.tax_number LIKE $like OR a.identity_number LIKE $like)
        ORDER BY d.issue_date DESC LIMIT $limit
        """,
        ("$c", companyId), ("$start", (object?)filter.Start?.ToString("O") ?? DBNull.Value), ("$end", (object?)filter.End?.ToString("O") ?? DBNull.Value),
        ("$type", filter.DocumentType ?? ""), ("$status", filter.Status ?? ""), ("$q", filter.Search?.Trim() ?? ""), ("$like", $"%{filter.Search?.Trim() ?? ""}%"), ("$limit", filter.Limit));

    // §12-13: Gönderim Kuyruğu - richer than ElectronicDocumentOutboxService.GetQueue (adds the
    // document's own status, provider, max attempts) since this screen needs both the queue's view
    // of the world and the document's, in one row, not two separate lookups per row.
    public DataTable SearchOutbox(string companyId, ElectronicDocumentFilter filter) => database.Query("""
        SELECT o.id AS Id,o.created_at AS Olusturma,d.document_type AS BelgeTipi,COALESCE(d.document_number,'Taslak') AS BelgeNo,
               COALESCE(a.name,'') AS Cari,o.operation_type AS Operation,o.status AS OutboxDurumu,d.status AS EBelgeDurumu,
               o.attempt_count AS Deneme,o.max_attempts AS MaksDeneme,o.last_attempt_at AS SonDeneme,o.next_attempt_at AS SonrakiDeneme,
               o.locked_by AS Kilit,d.provider_type AS Provider,o.last_error_message AS SonHata,d.id AS ElectronicDocumentId,d.account_id AS AccountId
        FROM electronic_document_outbox o
        JOIN electronic_documents d ON d.id=o.electronic_document_id
        LEFT JOIN accounts a ON a.id=d.account_id
        WHERE d.company_id=$c
          AND ($opStatus='' OR o.status=$opStatus) AND ($type='' OR d.document_type=$type)
          AND ($q='' OR d.document_number LIKE $like OR d.uuid LIKE $like OR a.name LIKE $like)
        ORDER BY o.created_at DESC LIMIT $limit
        """,
        ("$c", companyId), ("$opStatus", filter.OutboxStatus ?? ""), ("$type", filter.DocumentType ?? ""),
        ("$q", filter.Search?.Trim() ?? ""), ("$like", $"%{filter.Search?.Trim() ?? ""}%"), ("$limit", filter.Limit));

    // §20-21: Failed/Rejected documents, plus any document whose latest Send operation is
    // DeadLetter or a scheduled (attempt_count>0) retry - one screen, one query, not four.
    public DataTable SearchErrors(string companyId, ElectronicDocumentFilter filter) => database.Query("""
        SELECT d.id AS Id,d.issue_date AS BelgeTarihi,COALESCE(d.document_number,'Taslak') AS BelgeNo,d.document_type AS BelgeTipi,
               COALESCE(a.name,'') AS Cari,d.uuid AS UUID,d.status AS EBelgeDurumu,COALESCE(o.status,'—') AS OutboxDurumu,
               COALESCE(o.last_error_code,d.last_error_code,'') AS HataKodu,COALESCE(o.last_error_message,d.last_error_message,'') AS SonHata,
               d.send_attempt_count AS Deneme,o.last_attempt_at AS SonDeneme,o.next_attempt_at AS SonrakiDeneme,d.provider_type AS Provider,
               d.account_id AS AccountId,d.source_entity_type AS KaynakTipi,d.source_entity_id AS KaynakId
        FROM electronic_documents d
        LEFT JOIN accounts a ON a.id=d.account_id
        LEFT JOIN electronic_document_outbox o ON o.id=(SELECT x.id FROM electronic_document_outbox x WHERE x.electronic_document_id=d.id ORDER BY x.created_at DESC LIMIT 1)
        WHERE d.company_id=$c
          AND (d.status IN ('Failed','Rejected') OR o.status='DeadLetter' OR (o.status='Pending' AND o.attempt_count>0))
          AND ($type='' OR d.document_type=$type)
          AND ($q='' OR d.document_number LIKE $like OR d.uuid LIKE $like OR a.name LIKE $like)
        ORDER BY d.updated_at DESC LIMIT $limit
        """,
        ("$c", companyId), ("$type", filter.DocumentType ?? ""), ("$q", filter.Search?.Trim() ?? ""), ("$like", $"%{filter.Search?.Trim() ?? ""}%"), ("$limit", filter.Limit));

    // §7: dashboard's bounded "Son Hatalı Belgeler" panel - same shape as SearchErrors, capped small.
    public DataTable GetRecentErrors(string companyId, int limit = 15) => SearchErrors(companyId, new ElectronicDocumentFilter(Limit: limit));

    private static int N(object value) => value is DBNull ? 0 : Convert.ToInt32(value);
}
