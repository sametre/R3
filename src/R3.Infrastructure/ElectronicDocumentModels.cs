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
    string CarrierTaxNumber, string CarrierTitle);
