using R3.Infrastructure;

namespace R3.Desktop.Presentation;

// Phase 8 (§6/§44): the single place backend enums turn into Turkish labels, badge colors and
// action availability. Nothing else in R3.Desktop hardcodes a Turkish status string or decides for
// itself which e-document action a ViewModel/View may show - it asks here, so the answer used to
// enable a button is the exact same answer documented in docs/architecture/INVOICE-EDOCUMENT-UI.md's
// state table (built from ActionsFor, not typed out by hand twice).
public static class EDocumentPresentation
{
    public static string SalesStatusLabel(string status) => status switch
    {
        "Draft" => "Taslak",
        "Posted" => "Kesildi",
        "Cancelled" => "İptal",
        _ => status
    };

    public static string SalesStatusColor(string status) => status switch
    {
        "Draft" => "#8A96A0",
        "Posted" => "#2A8F7B",
        "Cancelled" => "#C4514B",
        _ => "#8A96A0"
    };

    public static string TypeLabel(ElectronicDocumentType type) => type switch
    {
        ElectronicDocumentType.EInvoice => "E-Fatura",
        ElectronicDocumentType.EArchiveInvoice => "E-Arşiv Fatura",
        ElectronicDocumentType.EDespatch => "E-İrsaliye",
        ElectronicDocumentType.DespatchReceipt => "İrsaliye Yanıtı",
        ElectronicDocumentType.ApplicationResponse => "Uygulama Yanıtı",
        _ => type.ToString()
    };

    public static string StatusLabel(ElectronicDocumentStatus status) => status switch
    {
        ElectronicDocumentStatus.Draft => "Hazırlanıyor",
        ElectronicDocumentStatus.Ready => "Hazır",
        ElectronicDocumentStatus.Generated => "UBL Oluşturuldu",
        ElectronicDocumentStatus.Queued => "Kuyrukta",
        ElectronicDocumentStatus.Sending => "Gönderiliyor",
        ElectronicDocumentStatus.Sent => "Gönderildi",
        ElectronicDocumentStatus.Delivered => "Teslim Edildi",
        ElectronicDocumentStatus.Accepted => "Kabul Edildi",
        ElectronicDocumentStatus.Rejected => "Reddedildi",
        ElectronicDocumentStatus.Failed => "Hatalı",
        ElectronicDocumentStatus.CancellationRequested => "İptal Talep Edildi",
        ElectronicDocumentStatus.Cancelled => "İptal Edildi",
        ElectronicDocumentStatus.Archived => "Arşivlendi",
        _ => status.ToString()
    };

    // §10/§41: no screen picks a color for a status itself - it asks StatusSemantic, then resolves
    // that semantic state to a brush via SemanticColor below. This is the one seam a real dark-mode
    // palette would plug into later (App.xaml is parallel-owned this phase - see
    // docs/architecture/EDOCUMENT-OPERATIONS.md for why real DynamicResource theme brushes are
    // deferred rather than built against it); today SemanticColor is the only place a hex value
    // is written, instead of every grid/badge choosing its own.
    public static SemanticState StatusSemantic(ElectronicDocumentStatus status) => status switch
    {
        ElectronicDocumentStatus.Draft or ElectronicDocumentStatus.Ready => SemanticState.Neutral,
        ElectronicDocumentStatus.Generated or ElectronicDocumentStatus.Queued or ElectronicDocumentStatus.Sending => SemanticState.Warning,
        ElectronicDocumentStatus.Sent or ElectronicDocumentStatus.Delivered => SemanticState.Info,
        ElectronicDocumentStatus.Accepted => SemanticState.Success,
        ElectronicDocumentStatus.Rejected or ElectronicDocumentStatus.Failed => SemanticState.Danger,
        ElectronicDocumentStatus.CancellationRequested or ElectronicDocumentStatus.Cancelled => SemanticState.Neutral,
        ElectronicDocumentStatus.Archived => SemanticState.Neutral,
        _ => SemanticState.Neutral
    };

    public static SemanticState OutboxStatusSemantic(ElectronicDocumentOutboxStatus status) => status switch
    {
        ElectronicDocumentOutboxStatus.Pending => SemanticState.Warning,
        ElectronicDocumentOutboxStatus.Processing => SemanticState.Info,
        ElectronicDocumentOutboxStatus.Completed => SemanticState.Success,
        ElectronicDocumentOutboxStatus.Failed or ElectronicDocumentOutboxStatus.DeadLetter => SemanticState.Danger,
        ElectronicDocumentOutboxStatus.Cancelled => SemanticState.Neutral,
        _ => SemanticState.Neutral
    };

    public static string SemanticColor(SemanticState state) => state switch
    {
        SemanticState.Neutral => "#8A96A0",
        SemanticState.Info => "#2E6F95",
        SemanticState.Success => "#2A8F7B",
        SemanticState.Warning => "#C0832B",
        SemanticState.Danger => "#C4514B",
        _ => "#8A96A0"
    };

    public static string StatusColor(ElectronicDocumentStatus status) => SemanticColor(StatusSemantic(status));
    public static string OutboxStatusColor(ElectronicDocumentOutboxStatus status) => SemanticColor(OutboxStatusSemantic(status));

    // §31: the event timeline shows the same technical event_type strings InsertEvent writes
    // (electronic_document_events.event_type) - this is the one place they become readable labels.
    public static string EventLabel(string eventType) => eventType switch
    {
        "ElectronicDocumentCreated" => "E-Belge Oluşturuldu",
        "DocumentReady" => "Hazır",
        "DocumentGenerated" => "UBL Oluşturuldu",
        "DocumentQueued" => "Kuyruğa Alındı",
        "DocumentRetryRequested" => "Tekrar Gönderim Talep Edildi",
        "DocumentSendingStarted" => "Gönderiliyor",
        "DocumentSent" => "Gönderildi",
        "DocumentDelivered" => "Teslim Edildi",
        "DocumentAccepted" => "Kabul Edildi",
        "DocumentRejected" => "Reddedildi",
        "DocumentFailed" => "Hata",
        "DocumentCancellationRequested" => "İptal Talep Edildi",
        "DocumentCancelled" => "İptal Edildi",
        "DocumentArchived" => "Arşivlendi",
        "ElectronicDocumentGenerationFailed" => "UBL Üretimi Başarısız",
        var t when t.StartsWith("Payload") && t.EndsWith("Saved") => "İçerik Kaydedildi",
        _ => eventType
    };

    public static string OutboxStatusLabel(ElectronicDocumentOutboxStatus status) => status switch
    {
        ElectronicDocumentOutboxStatus.Pending => "Bekliyor",
        ElectronicDocumentOutboxStatus.Processing => "İşleniyor",
        ElectronicDocumentOutboxStatus.Completed => "Tamamlandı",
        ElectronicDocumentOutboxStatus.Failed => "Başarısız",
        ElectronicDocumentOutboxStatus.DeadLetter => "Durduruldu",
        ElectronicDocumentOutboxStatus.Cancelled => "İptal",
        _ => status.ToString()
    };

    // §21-27, §44: which e-document actions a document in this status may show, and which are
    // actually clickable right now (busy state / permission are applied on top of this by the
    // caller - this only answers "does the state machine allow it").
    public static EDocumentActions ActionsFor(ElectronicDocumentStatus status) => status switch
    {
        ElectronicDocumentStatus.Ready => new(CanGenerate: true),
        ElectronicDocumentStatus.Generated => new(CanViewXml: true, CanQueue: true),
        ElectronicDocumentStatus.Queued => new(CanViewXml: true, CanSend: true),
        ElectronicDocumentStatus.Sending => new(CanViewXml: true),
        ElectronicDocumentStatus.Sent or ElectronicDocumentStatus.Delivered =>
            new(CanViewXml: true, CanViewProviderResponse: true, CanQueryStatus: true, CanViewEvents: true),
        ElectronicDocumentStatus.Accepted or ElectronicDocumentStatus.Rejected =>
            new(CanViewXml: true, CanViewProviderResponse: true, CanViewEvents: true),
        ElectronicDocumentStatus.Failed => new(CanViewXml: true, CanRetry: true, CanViewEvents: true),
        ElectronicDocumentStatus.CancellationRequested or ElectronicDocumentStatus.Cancelled or ElectronicDocumentStatus.Archived =>
            new(CanViewXml: true, CanViewEvents: true),
        _ => new()
    };
}

// One row per state in the table §54 asks the final report to produce. Deliberately plain booleans,
// not a permission check - EDocumentActionGate below applies HasPermission on top of these.
public sealed record EDocumentActions(
    bool CanGenerate = false, bool CanViewXml = false, bool CanQueue = false, bool CanSend = false,
    bool CanQueryStatus = false, bool CanRetry = false, bool CanViewProviderResponse = false, bool CanViewEvents = false);

// §41: five states, not an open palette - every badge/grid cell in the e-document UI is one of
// these, never a screen-specific color choice.
public enum SemanticState { Neutral, Info, Success, Warning, Danger }
