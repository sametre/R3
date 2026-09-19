namespace R3.Infrastructure;

public enum ElectronicDocumentType { EInvoice, EArchiveInvoice, EDespatch, DespatchReceipt, ApplicationResponse }
public enum ElectronicDocumentDirection { Incoming, Outgoing }
public enum ElectronicDocumentPayloadType { UblXml, SignedXml, Xslt, Pdf, ProviderResponse }

// Status engine (spec §20): a document only ever moves forward through this graph, never via a free-text setter.
// Draft -> Ready -> Generated -> Queued -> Sending -> Sent -> Delivered -> Accepted -> Archived
//                                             \-> Failed -> Queued (retry) / CancellationRequested -> Cancelled
public enum ElectronicDocumentStatus { Draft, Ready, Generated, Queued, Sending, Sent, Delivered, Accepted, Rejected, Failed, CancellationRequested, Cancelled, Archived }

public sealed record ElectronicDocumentDraft(
    string CompanyId, string BranchId, ElectronicDocumentType DocumentType, ElectronicDocumentDirection Direction,
    string SourceEntityType, string SourceEntityId, string? AccountId, string? DocumentNumber,
    DateTime IssueDate, string CurrencyCode, decimal? PayableAmount, string RecipientSnapshotJson,
    string CreatedBy, string? Uuid = null);

public sealed record ElectronicDocumentRow(
    string Id, string CompanyId, string BranchId, ElectronicDocumentType DocumentType, ElectronicDocumentDirection Direction,
    string SourceEntityType, string SourceEntityId, string? AccountId, string? DocumentNumber, string Uuid,
    string? EnvelopeId, string? ProviderDocumentId, string ProviderType, ElectronicDocumentStatus Status,
    DateTime IssueDate, string CurrencyCode, decimal? PayableAmount, string RecipientSnapshotJson,
    int SendAttemptCount, DateTime? LastAttemptAt, DateTime? NextRetryAt, string? LastErrorCode, string? LastErrorMessage,
    DateTime CreatedAt, string CreatedBy);

public sealed record ElectronicDocumentEventRow(string Id, string ElectronicDocumentId, string EventType, string? OldStatus, string? NewStatus, string? ProviderCode, string? ProviderMessage, DateTime OccurredAt, string? UserId);

public sealed record ElectronicDocumentPayloadRow(string Id, string ElectronicDocumentId, ElectronicDocumentPayloadType PayloadType, string Content, string ContentHash, string MimeType, int Version, bool IsSigned, DateTime CreatedAt);

public sealed record ElectronicDocumentCompanyProfileEdit(
    string CompanyId, string TaxNumber, string LegalTitle, string DefaultEInvoiceAlias, string DefaultEDespatchAlias,
    string ProviderType, string Environment, bool AutoSend, bool AutoCheckRecipient, string DefaultInvoiceScenario,
    string EArchiveSenderEmail, string EArchiveUnitCode, string InternetSalesUnitCode, string InternetWebsite,
    string CarrierTaxNumber, string CarrierTitle,
    string AddressLine = "", string City = "", string District = "", string PostalCode = "", string Country = "Türkiye");

// Phase 3 (spec §12-13): MVP only processes Send. The rest are reserved so the enum doesn't need to
// change shape again when status-polling/generation/incoming-document operations are built later.
public enum ElectronicDocumentOutboxOperation { Send, QueryStatus, Generate, Sign, Cancel, DownloadIncoming, ProcessIncoming }

// No separate "Retrying" status (spec §14): a record waiting to retry is just Pending with a future
// NextAttemptAt — a third status would be redundant information, not a new state.
public enum ElectronicDocumentOutboxStatus { Pending, Processing, Completed, Failed, DeadLetter, Cancelled }

public sealed record ElectronicDocumentOutboxRow(
    string Id, string ElectronicDocumentId, ElectronicDocumentOutboxOperation OperationType, ElectronicDocumentOutboxStatus Status,
    int AttemptCount, int MaxAttempts, DateTime CreatedAt, DateTime AvailableAt, DateTime? LockedAt, string? LockedBy,
    DateTime? StartedAt, DateTime? CompletedAt, DateTime? LastAttemptAt, DateTime? NextAttemptAt,
    string? LastErrorCode, string? LastErrorMessage, string IdempotencyKey, string? CorrelationId);

// Canonical provider contract (spec §18-19): the provider never sees an Invoice/Account/SQLite row,
// only these DTOs — so a real GİB entegratör adapter can be dropped in without any provider-specific
// shape leaking into the rest of R3. Deliberately does NOT carry Alias/Profile (spec's provider-request
// field list includes them): both are already embedded in the UBL payload itself
// (cbc:ProfileID / party alias) - duplicating GİB routing metadata outside the canonical document would
// be a second source of truth for the same fact. See docs/architecture/OUTBOX-DISPATCH.md.
public sealed record ElectronicDocumentSendRequest(
    string ElectronicDocumentId, ElectronicDocumentType DocumentType, string Uuid, string? DocumentNumber, string Payload, string PayloadHash,
    string? Sender, string? Recipient, string IdempotencyKey, string? CorrelationId);

public sealed record ElectronicDocumentProviderResult(
    bool Success, string? ProviderDocumentId, string? ProviderEnvelopeId, string? ProviderStatus,
    string? ProviderCode, string? ProviderMessage, bool IsTransientFailure, string? RawResponse)
{
    public static ElectronicDocumentProviderResult Ok(string? providerDocumentId = null, string? providerEnvelopeId = null, string? rawResponse = null) =>
        new(true, providerDocumentId, providerEnvelopeId, "Sent", null, null, false, rawResponse);
    // Network/timeout-class failures: safe to retry automatically.
    public static ElectronicDocumentProviderResult TransientFailure(string code, string message, string? rawResponse = null) =>
        new(false, null, null, null, code, message, true, rawResponse);
    // Validation/business rejection: must never be retried automatically (spec §24).
    public static ElectronicDocumentProviderResult Rejected(string code, string message, string? rawResponse = null) =>
        new(false, null, null, null, code, message, false, rawResponse);
}

// The remote milestones a QueryStatus poll can observe, mapped 1:1 onto the existing
// ElectronicDocumentStatus graph (Sent -> Delivered -> Accepted/Rejected) - no new business status is
// introduced, this just names what the provider told us.
public enum ElectronicDocumentRemoteStatus { Sent, Delivered, Accepted, Rejected }

public sealed record ElectronicDocumentStatusQueryRequest(
    string ElectronicDocumentId, ElectronicDocumentType DocumentType, string Uuid, string? ProviderDocumentId, string IdempotencyKey, string? CorrelationId);

public sealed record ElectronicDocumentStatusQueryResult(
    bool Success, ElectronicDocumentRemoteStatus? RemoteStatus, string? ProviderCode, string? ProviderMessage, bool IsTransientFailure, string? RawResponse)
{
    public static ElectronicDocumentStatusQueryResult Ok(ElectronicDocumentRemoteStatus status, string? rawResponse = null) => new(true, status, null, null, false, rawResponse);
    public static ElectronicDocumentStatusQueryResult TransientFailure(string code, string message, string? rawResponse = null) => new(false, null, code, message, true, rawResponse);
    public static ElectronicDocumentStatusQueryResult PermanentFailure(string code, string message, string? rawResponse = null) => new(false, null, code, message, false, rawResponse);
}
