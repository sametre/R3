# Outbox Dispatch Engine (Phase 7)

Connects an already-`Generated` electronic document (immutable UBL payload, see
`docs/architecture/UBL-ENGINE.md`) to actual delivery: `Queued -> Sending -> Sent ->
Delivered -> Accepted/Rejected`, through a durable outbox and a provider abstraction.

## Outbox schema (`electronic_document_outbox`, migration 7, Phase 3)

One row per queued operation against one electronic document. Not a duplicate of
`electronic_documents.status` — the document row is the business-status source of
truth, the outbox row is queue/retry/lock bookkeeping only.

Columns: `id`, `electronic_document_id`, `operation_type` (`Send` | `QueryStatus`),
`status` (`Pending` | `Processing` | `Done` | `DeadLetter`), `idempotency_key`
(unique), `correlation_id`, `attempt_count`, `next_attempt_at`, `locked_by`,
`locked_at`, `last_error_code`, `last_error_message`, `created_at`, `updated_at`.

## Generated -> Queued flow

`LocalElectronicDocumentService.QueueWithinTransaction` (document status
`Generated -> Queued`) and `ElectronicDocumentOutboxService.QueueForSendAsync`
(outbox insert) run inside the same `BEGIN/COMMIT` as the event-log insert — the
document can never end up `Queued` without a corresponding outbox row, or vice versa.
`QueueForSendAsync` is idempotent: calling it twice for the same document is a no-op
if a live (non-terminal) `Send` row already exists.

`ElectronicDocumentOutboxService.QueueStatusQuery` is the one asymmetry: it inserts
an outbox row but never touches `electronic_documents.status`, since polling doesn't
drive a state transition by itself — only the provider's answer does.

## Dispatcher (`ElectronicDocumentDispatcher`)

`DispatchAsync(workerId, userId)`: `ReclaimStale()` -> `ClaimDue(workerId)` -> loop
`ProcessOneAsync` per claimed row, branching on `operation_type` into
`ProcessSendAsync` / `ProcessStatusQueryAsync`. Runs entirely outside any open SQLite
transaction — a slow or unreachable provider can never block or roll back invoice
posting. There is no hosted background service yet; this is invoked from a timer or a
manual "Şimdi Gönder" action.

`ProcessSendAsync` never regenerates XML. It reads the already-persisted
`ElectronicDocumentPayload` (`PayloadType=UblXml`) via `LatestSendablePayload` and
sends exactly those bytes, with their stored SHA-256 hash, inside a canonical
`ElectronicDocumentSendRequest` DTO — never the raw `Invoice`/`Product`/`Account`
rows. `Alias`/`Profile` are deliberately not fields on that DTO: they're already
embedded in the UBL payload's `cbc:ProfileID` and party-alias elements, so adding
them again would create a second source of truth for GİB routing metadata.

On success, `ProcessSendAsync` chains straight into `outbox.QueueStatusQuery(...)` —
a sent document is not "done" until its remote status has been polled at least once.

`ProcessStatusQueryAsync` drives `Sent -> Delivered -> Accepted/Rejected`. It never
skips `Delivered`, even when the provider reports `Accepted`/`Rejected` directly on
the first poll, because the `ElectronicDocumentStatus` transition graph requires
passing through it. If the remote status is still `Sent`/`Delivered` (not yet final)
it completes the current poll and immediately requeues another one.

## Provider abstraction (`IElectronicDocumentProvider`)

`SendAsync(ElectronicDocumentSendRequest)` and
`QueryStatusAsync(ElectronicDocumentStatusQueryRequest)`, canonical DTOs in, canonical
result DTOs out. `DevelopmentElectronicDocumentProvider` is the only implementation
today (not a production default) — a real GİB adapter is future work.

## Idempotency

Deterministic key: `{CompanyId}:{DocumentType}:{ElectronicDocumentId}:{Uuid}:{Operation}`.
Stable across every automatic retry and every manual retry of the same outbox row —
a real idempotency-aware provider endpoint can dedupe a crash-window resend using
this key without R3 doing anything special. Covered by
`IdempotencyKeyStaysStableAcrossAnAutomaticRetryCycle`.

## Locking / claim

`ClaimDue` is a single atomic `UPDATE ... WHERE status='Pending' AND next_attempt_at
<= now` setting `status='Processing', locked_by, locked_at`. Relies on SQLite's WAL
single-writer serialization rather than a separate compare-and-swap primitive — two
concurrent dispatcher workers issuing this statement against the same database file
cannot both win the same row. Covered by `TwoWorkersCannotClaimSameOutboxRow` (Phase 3).

## Stale lock recovery

`ReclaimStale()` resets any row still `Processing` past a timeout threshold back to
`Pending`, clearing `locked_by`/`locked_at`, so a worker that crashed mid-dispatch
doesn't strand its claimed rows forever. Runs at the start of every `DispatchAsync`.

## Retry policy

Provider result classifies failures as transient or permanent.
Transient (timeout/network/408/429/500/502/503/504, and any unexpected exception —
we don't know whether the provider processed the request before the exception, so we
never assume that pushed it into the permanent-failure branch) -> `ScheduleRetry`
with exponential backoff. Permanent/business (invalid XML, invalid VKN/TCKN, invalid
alias, provider validation rejection) -> `DeadLetter`, never retried.

## Dead-letter

`DeadLetter(outboxId, code, message)` sets the row to a terminal `DeadLetter` status
and mirrors the failure onto the document (`Fail`, then a permanent case does not
auto-retry). `ManualRetry` (Phase 3) is the only way out of dead-letter, requiring an
explicit operator action.

## Status query

`ElectronicDocumentStatusQueryRequest` carries `ProviderDocumentId`/`Uuid`/
`IdempotencyKey`/`CorrelationId`; the result (`ElectronicDocumentStatusQueryResult`)
reports `Sent | Delivered | Accepted | Rejected`, plus the same transient/permanent
failure classification as `Send`. Every raw provider response, if the provider
returns one, is persisted as an `ElectronicDocumentPayloadType.ProviderResponse`
payload row — never logged directly (see Logging below).

## Crash-window behavior

Scenario under test: provider accepts and returns a provider document id, then the
local DB update that would record `MarkSent` fails (process crash / connection
drop) before the outbox row is marked `Done`. The row stays `Pending`/`Processing`
and is retried with the *same* `IdempotencyKey`. `DevelopmentElectronicDocumentProvider`
has no idempotency awareness, so the crash-window guarantee is verified against a
purpose-built `IdempotentFakeProvider` test double that maps
`IdempotencyKey -> ProviderDocumentId` the way a real GİB endpoint would, and asserts
no duplicate remote submission occurs across the retried attempt.
Covered by `CrashWindowBetweenProviderSuccessAndLocalPersistenceDoesNotCreateADuplicateRemoteSubmission`.

## Logging

Structured logging via `ILogger<ElectronicDocumentDispatcher>` (optional constructor
parameter, defaults to `NullLogger`), scoped per outbox row with
`OutboxId`/`ElectronicDocumentId`/`Operation`/`WorkerId`. Payload content and raw
provider request/response bodies are never logged — only ids, status, and provider
error codes. Full raw responses go to a `ProviderResponse` payload row, not the log.

## Tests

`tests/R3.Domain.Tests/ElectronicDocumentTests.cs` — Phase 3 queueing/claim/retry/
dead-letter coverage plus, added in Phase 7: `StatusQueryAdvancesSentDocumentThroughDeliveredToAccepted`,
`StatusQueryCanRejectAfterDelivered`, `StatusQueryStaysAtDeliveredAndRequeuesAnotherPollWhenNotYetFinal`,
`IdempotencyKeyStaysStableAcrossAnAutomaticRetryCycle`,
`CrashWindowBetweenProviderSuccessAndLocalPersistenceDoesNotCreateADuplicateRemoteSubmission`.
