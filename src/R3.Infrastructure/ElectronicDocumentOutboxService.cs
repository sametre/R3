using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

// Phase 3 (spec §12-30). Owns the outbox table only - it never talks to a provider and never decides
// electronic_documents' business status by itself; ElectronicDocumentDispatcher orchestrates both
// together. Kept separate from LocalElectronicDocumentService per the service-boundary guidance
// (review §36): one class per concern, not fifteen.
public sealed class ElectronicDocumentOutboxService(StoreDatabase database, LocalElectronicDocumentService documents)
{
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)];
    private const int DefaultMaxAttempts = 8;
    private static readonly TimeSpan StaleLockTimeout = TimeSpan.FromMinutes(10);

    // Atomic per spec §15: ElectronicDocument Ready->Queued and the outbox row are written in one
    // SQLite transaction, so an outbox-insert failure can never leave the document Queued with
    // nothing to actually send it, and a document can never sit Queued with two competing outbox rows.
    // Idempotent per spec §16: a second call for a document that already has an active (Pending/
    // Processing) Send entry returns that same row instead of creating a duplicate - both via an
    // app-level check and a DB-level partial unique index as a backstop.
    public string QueueForSendAsync(string electronicDocumentId, string userId, string? correlationId = null)
    {
        using var c = Open(); c.Open(); using var tx = c.BeginTransaction();
        using (var existing = c.CreateCommand())
        {
            existing.Transaction = tx;
            existing.CommandText = "SELECT id FROM electronic_document_outbox WHERE electronic_document_id=$doc AND operation_type='Send' AND status IN ('Pending','Processing')";
            Add(existing, "$doc", electronicDocumentId);
            if (existing.ExecuteScalar() is string activeId) { tx.Commit(); return activeId; }
        }
        var document = documents.Get(electronicDocumentId) ?? throw new KeyNotFoundException("Elektronik belge bulunamadı.");
        // Ready/Generated -> Queued (first send) or Failed -> Queued (manual retry via ManualRetry
        // below), in THIS transaction; throws for any other current status - including every terminal
        // one - so a terminal document can never be queued again (review §5/spec §13).
        var eventType = document.Status == ElectronicDocumentStatus.Failed ? "DocumentRetryRequested" : "DocumentQueued";
        documents.QueueWithinTransaction(c, tx, electronicDocumentId, userId, eventType);

        var id = Guid.NewGuid().ToString(); var now = DateTime.UtcNow;
        // Deterministic (spec §27): stable across every retry AND across a manual re-queue after
        // DeadLetter, so a provider that recognizes idempotency keys can never be tricked into
        // processing the same document twice even if our own bookkeeping got interrupted mid-flight.
        var idempotencyKey = $"{document.CompanyId}:{document.DocumentType}:{electronicDocumentId}:{document.Uuid}:Send";
        using (var insert = c.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO electronic_document_outbox(id,electronic_document_id,operation_type,status,attempt_count,max_attempts,created_at,available_at,idempotency_key,correlation_id)
                VALUES($id,$doc,'Send','Pending',0,$max,$now,$now,$key,$corr)
                """;
            Add(insert, "$id", id); Add(insert, "$doc", electronicDocumentId); Add(insert, "$max", DefaultMaxAttempts);
            Add(insert, "$now", now.ToString("O")); Add(insert, "$key", idempotencyKey); AddNullable(insert, "$corr", correlationId);
            insert.ExecuteNonQuery();
        }
        tx.Commit();
        return id;
    }

    // Claim (spec §22/§25): a single atomic UPDATE ... WHERE status='Pending' is the claim - SQLite
    // serializes all writers to one file (WAL mode, see StoreDatabase), so a second concurrent caller's
    // UPDATE simply sees zero rows affected once the first has committed; no separate compare-and-swap
    // primitive is needed for a single-writer-per-file desktop deployment. LockedAt/LockedBy are kept
    // anyway for observability and for the crash-recovery path below, and because a future multi-process
    // dispatcher (e.g. R3.Server also processing the same file) would need them for real.
    public IReadOnlyList<string> ClaimDue(string workerId, int batchSize = 20)
    {
        var due = database.Query(
            "SELECT id FROM electronic_document_outbox WHERE status='Pending' AND available_at<=$now ORDER BY available_at LIMIT $n",
            ("$now", DateTime.UtcNow.ToString("O")), ("$n", batchSize)).Rows.Cast<DataRow>().Select(r => r[0].ToString()!).ToList();
        var claimed = new List<string>();
        foreach (var id in due)
        {
            var rows = database.Execute(
                "UPDATE electronic_document_outbox SET status='Processing',locked_at=$now,locked_by=$worker,started_at=$now WHERE id=$id AND status='Pending'",
                ("$now", DateTime.UtcNow.ToString("O")), ("$worker", workerId), ("$id", id));
            if (rows == 1) claimed.Add(id);
        }
        return claimed;
    }

    // Crash recovery (spec §26): a row stuck Processing past the stale timeout gets released back to
    // Pending so the next ClaimDue can pick it up. This does NOT by itself make a resend safe - that's
    // what the stable IdempotencyKey (unchanged by this call) is for; see ElectronicDocumentDispatcher.
    public int ReclaimStale(TimeSpan? staleAfter = null)
    {
        var threshold = DateTime.UtcNow - (staleAfter ?? StaleLockTimeout);
        return database.Execute(
            "UPDATE electronic_document_outbox SET status='Pending',locked_at=NULL,locked_by=NULL,available_at=$now WHERE status='Processing' AND locked_at<=$threshold",
            ("$now", DateTime.UtcNow.ToString("O")), ("$threshold", threshold.ToString("O")));
    }

    public void Complete(string outboxId)
    {
        database.Execute("UPDATE electronic_document_outbox SET status='Completed',completed_at=$now,last_attempt_at=$now,attempt_count=attempt_count+1 WHERE id=$id",
            ("$now", DateTime.UtcNow.ToString("O")), ("$id", outboxId));
    }

    // Transient (spec §24): schedules the next attempt with exponential backoff (capped at the table's
    // configured MaxAttempts, after which it dead-letters instead of retrying forever) plus jitter, and
    // goes back to Pending - never stays Processing, so a normal ClaimDue picks it up again without any
    // special "retry" status.
    public void ScheduleRetry(string outboxId, string errorCode, string errorMessage)
    {
        var row = Get(outboxId) ?? throw new KeyNotFoundException("Outbox kaydı bulunamadı.");
        var attempt = row.AttemptCount + 1;
        if (attempt >= row.MaxAttempts) { DeadLetter(outboxId, errorCode, errorMessage); return; }
        var delay = RetryDelays[Math.Min(attempt - 1, RetryDelays.Length - 1)];
        var jitter = TimeSpan.FromSeconds(Random.Shared.Next(0, 30));
        var nextAttemptAt = DateTime.UtcNow + delay + jitter;
        database.Execute("""
            UPDATE electronic_document_outbox SET status='Pending',attempt_count=$attempt,locked_at=NULL,locked_by=NULL,
                last_attempt_at=$now,next_attempt_at=$next,available_at=$next,last_error_code=$code,last_error_message=$msg
            WHERE id=$id
            """,
            ("$attempt", attempt), ("$now", DateTime.UtcNow.ToString("O")), ("$next", nextAttemptAt.ToString("O")),
            ("$code", errorCode), ("$msg", errorMessage), ("$id", outboxId));
    }

    // Permanent (spec §24): a validation/business rejection must never auto-retry - straight to
    // DeadLetter, no NextAttemptAt scheduled.
    public void DeadLetter(string outboxId, string errorCode, string errorMessage)
    {
        database.Execute("""
            UPDATE electronic_document_outbox SET status='DeadLetter',attempt_count=attempt_count+1,locked_at=NULL,locked_by=NULL,
                last_attempt_at=$now,last_error_code=$code,last_error_message=$msg
            WHERE id=$id
            """,
            ("$now", DateTime.UtcNow.ToString("O")), ("$code", errorCode), ("$msg", errorMessage), ("$id", outboxId));
    }

    // Manual retry (spec §29): only for a document actually stuck (Failed, whose outbox entry is
    // DeadLetter or Cancelled) - never overwrites the old outbox row (its history/attempt-count stays
    // exactly as it was), just opens a brand-new one. The SAME idempotency key is reused (see
    // QueueForSendAsync) precisely so a provider that saw the original attempt can dedupe it.
    public string ManualRetry(string electronicDocumentId, string userId)
    {
        var document = documents.Get(electronicDocumentId) ?? throw new KeyNotFoundException("Elektronik belge bulunamadı.");
        if (document.Status != ElectronicDocumentStatus.Failed) throw new InvalidOperationException("Yalnızca Failed durumundaki belgeler yeniden gönderilebilir.");
        return QueueForSendAsync(electronicDocumentId, userId); // Failed -> Queued happens inside, same transaction as the new outbox row.
    }

    public ElectronicDocumentOutboxRow? Get(string id)
    {
        var t = database.Query("SELECT * FROM electronic_document_outbox WHERE id=$id", ("$id", id));
        return t.Rows.Count == 0 ? null : ToRow(t.Rows[0]);
    }

    public DataTable GetQueue(string companyId) => database.Query("""
        SELECT o.id AS Id,o.created_at AS Olusturma,d.document_type AS BelgeTipi,d.document_number AS BelgeNo,
               COALESCE(a.name,'') AS Cari,o.operation_type AS Operation,o.status AS Durum,o.attempt_count AS Deneme,
               o.last_attempt_at AS SonDeneme,o.next_attempt_at AS SonrakiDeneme,o.last_error_message AS SonHata
        FROM electronic_document_outbox o
        JOIN electronic_documents d ON d.id=o.electronic_document_id
        LEFT JOIN accounts a ON a.id=d.account_id
        WHERE d.company_id=$c AND o.status IN ('Pending','Processing','Failed','DeadLetter')
        ORDER BY o.created_at
        """, ("$c", companyId));

    private static ElectronicDocumentOutboxRow ToRow(DataRow r) => new(
        r["id"].ToString()!, r["electronic_document_id"].ToString()!, Enum.Parse<ElectronicDocumentOutboxOperation>(r["operation_type"].ToString()!),
        Enum.Parse<ElectronicDocumentOutboxStatus>(r["status"].ToString()!), Convert.ToInt32(r["attempt_count"]), Convert.ToInt32(r["max_attempts"]),
        DateTime.Parse(r["created_at"].ToString()!), DateTime.Parse(r["available_at"].ToString()!),
        r["locked_at"] is DBNull ? null : DateTime.Parse(r["locked_at"].ToString()!), r["locked_by"] as string,
        r["started_at"] is DBNull ? null : DateTime.Parse(r["started_at"].ToString()!), r["completed_at"] is DBNull ? null : DateTime.Parse(r["completed_at"].ToString()!),
        r["last_attempt_at"] is DBNull ? null : DateTime.Parse(r["last_attempt_at"].ToString()!), r["next_attempt_at"] is DBNull ? null : DateTime.Parse(r["next_attempt_at"].ToString()!),
        r["last_error_code"] as string, r["last_error_message"] as string, r["idempotency_key"].ToString()!, r["correlation_id"] as string);

    private SqliteConnection Open() => new($"Data Source={database.Path};Foreign Keys=True;Default Timeout=5");
    private static void Add(SqliteCommand c, string n, object v) => c.Parameters.AddWithValue(n, v);
    private static void AddNullable(SqliteCommand c, string n, string? v) => c.Parameters.AddWithValue(n, (object?)v ?? DBNull.Value);
}
