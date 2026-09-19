using System.Data;
using R3.Desktop.Presentation;
using R3.Desktop.Views;
using R3.Infrastructure;

namespace R3.Desktop.ContextActions;

// Phase 9 (§11/§16-19/§42): right-click actions for the Giden Belgeler and Hatalı Belgeler grids
// (both are electronic_documents-keyed DataTables, same "Id" column) and the Gönderim Kuyruğu grid
// (electronic_document_outbox-keyed). Every mutating entry calls the exact same canonical services
// InvoiceDetailViewModel calls (ElectronicDocumentOutboxService.ManualRetry,
// ElectronicDocumentDispatcher.DispatchAsync, ElectronicDocumentOutboxService.QueueStatusQuery) -
// never a direct provider call, never a raw status UPDATE (§ SON HEDEF).
public static class ElectronicDocumentContextActions
{
    private static Task<ContextActionResult> Run(Action action, bool refresh = false) { action(); return Task.FromResult(ContextActionResult.Ok(refresh: refresh)); }
    private static async Task<ContextActionResult> RunAsync(Func<Task> action, bool refresh = true)
    {
        try { await action(); return ContextActionResult.Ok(refresh: refresh); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException) { return ContextActionResult.Failed(ex.Message); }
    }

    // Used by both the Giden Belgeler grid (all statuses) and the Hatalı Belgeler grid (error
    // subset) - the row shape (Id/AccountId/KaynakTipi/KaynakId/EBelgeDurumu or Durum) is the same
    // projection family from ElectronicDocumentOperationsService.
    public static IReadOnlyList<ContextActionDefinition> ForDocument(
        StoreDatabase database, string userId, Action<string> openDocument, Action<string, string> openSourceDocument, Action<string> openAccount, Action<string> openQueueRecord)
    {
        var documents = new LocalElectronicDocumentService(database);
        var outbox = new ElectronicDocumentOutboxService(database, documents);
        var dispatcher = new ElectronicDocumentDispatcher(documents, outbox, new DevelopmentElectronicDocumentProvider());

        string Id(object? x) => (x as DataRowView)?["Id"]?.ToString() ?? "";
        ElectronicDocumentStatus? Status(object? x)
        {
            var row = x as DataRowView; var text = row != null && row.Row.Table.Columns.Contains("EBelgeDurumu") ? row["EBelgeDurumu"]?.ToString() : row?["Durum"]?.ToString();
            return Enum.TryParse<ElectronicDocumentStatus>(text, out var s) ? s : null;
        }
        bool HasPayload(object? x) => EDocumentPresentation.ActionsFor(Status(x) ?? ElectronicDocumentStatus.Draft).CanViewXml;
        bool HasProviderResponse(object? x) => EDocumentPresentation.ActionsFor(Status(x) ?? ElectronicDocumentStatus.Draft).CanViewProviderResponse;
        bool CanQuery(object? x) => Status(x) is ElectronicDocumentStatus.Sent or ElectronicDocumentStatus.Delivered;
        bool CanRetry(object? x) => Status(x) == ElectronicDocumentStatus.Failed;
        bool HasSource(object? x) => x is DataRowView { Row.Table.Columns: var cols } row && cols.Contains("KaynakId") && !string.IsNullOrWhiteSpace(row["KaynakId"]?.ToString());
        bool HasAccount(object? x) => x is DataRowView { Row.Table.Columns: var cols } row && cols.Contains("AccountId") && !string.IsNullOrWhiteSpace(row["AccountId"]?.ToString());

        return
        [
            A("edoc.open", "Belgeyi Aç", "edocuments.view", 10, ContextActionGroup.Primary, x => Run(() => openDocument(Id(x)))),
            A("edoc.source", "Kaynak Faturayı Aç", "invoices.view", 10, ContextActionGroup.Related, x => Run(() => openSourceDocument(((DataRowView)x!)["KaynakTipi"].ToString()!, ((DataRowView)x!)["KaynakId"].ToString()!)), HasSource),
            A("edoc.account", "Cari Kartını Aç", "accounts.view", 20, ContextActionGroup.Related, x => Run(() => openAccount(((DataRowView)x!)["AccountId"].ToString()!)), HasAccount),
            A("edoc.xml", "XML Görüntüle", "edocuments.payload.view", 10, ContextActionGroup.Document,
                x => Run(() => ElectronicDocumentDialogs.ShowXmlViewer(documents.GetPayloads(Id(x)))), HasPayload),
            A("edoc.provider_response", "Provider Yanıtı", "edocuments.provider_response.view", 20, ContextActionGroup.Document,
                x => Run(() => { var doc = documents.Get(Id(x)); if (doc != null) ElectronicDocumentDialogs.ShowProviderResponse(doc, documents.GetPayloads(Id(x))); }), HasProviderResponse),
            A("edoc.query_status", "Durumu Sorgula", "edocuments.status.query", 10, ContextActionGroup.Operational,
                x => RunAsync(async () => { outbox.QueueStatusQuery(Id(x)); await dispatcher.DispatchAsync("desktop-context", userId); }), CanQuery),
            A("edoc.retry", "Tekrar Dene", "edocuments.retry", 20, ContextActionGroup.Operational,
                x => RunAsync(async () => { outbox.ManualRetry(Id(x), userId); await dispatcher.DispatchAsync("desktop-context", userId); }), CanRetry, null, true,
                selected => $"Gönderim yeniden denenecek.\n\nBelge:\n{(selected as DataRowView)?["BelgeNo"]}\n\nSon hata:\n{(selected as DataRowView)?["SonHata"]}"),
            A("edoc.queue_record", "Kuyruk Kaydını Gör", "edocuments.outbox.view", 10, ContextActionGroup.Related, x => Run(() => openQueueRecord(Id(x)))),
            A("edoc.events", "Olay Geçmişi", "edocuments.audit.view", 10, ContextActionGroup.Audit,
                x => Run(() => { var doc = documents.Get(Id(x)); if (doc != null) ElectronicDocumentDialogs.ShowEventTimeline(doc, documents.GetEvents(doc.Id)); })),
            A("edoc.audit", "Audit", "edocuments.audit.view", 20, ContextActionGroup.Audit, x => Run(() => openDocument(Id(x))))
        ];
    }

    // Gönderim Kuyruğu grid - rows are outbox operations, not documents (§16-19).
    public static IReadOnlyList<ContextActionDefinition> ForOutbox(StoreDatabase database, string userId, Action<string> openDrawer, Action<string> openDocument)
    {
        var documents = new LocalElectronicDocumentService(database);
        var outbox = new ElectronicDocumentOutboxService(database, documents);
        var dispatcher = new ElectronicDocumentDispatcher(documents, outbox, new DevelopmentElectronicDocumentProvider());

        string Id(object? x) => (x as DataRowView)?["Id"]?.ToString() ?? "";
        string DocumentId(object? x) => (x as DataRowView)?["ElectronicDocumentId"]?.ToString() ?? "";
        ElectronicDocumentOutboxStatus? OutboxStatus(object? x) => Enum.TryParse<ElectronicDocumentOutboxStatus>((x as DataRowView)?["OutboxDurumu"]?.ToString(), out var s) ? s : null;
        bool Pending(object? x) => OutboxStatus(x) == ElectronicDocumentOutboxStatus.Pending;
        bool DeadLetter(object? x) => OutboxStatus(x) == ElectronicDocumentOutboxStatus.DeadLetter;
        bool Completed(object? x) => OutboxStatus(x) == ElectronicDocumentOutboxStatus.Completed;

        return
        [
            A("outbox.open", "Kaydı Aç", "edocuments.outbox.view", 10, ContextActionGroup.Primary, x => Run(() => openDrawer(Id(x)))),
            A("outbox.run", "Şimdi Çalıştır", "edocuments.send", 10, ContextActionGroup.Operational,
                x => RunAsync(() => dispatcher.DispatchAsync("desktop-context", userId)), Pending),
            A("outbox.retry", "Tekrar Dene", "edocuments.retry", 20, ContextActionGroup.Operational,
                x => RunAsync(async () => { outbox.ManualRetry(DocumentId(x), userId); await dispatcher.DispatchAsync("desktop-context", userId); }), DeadLetter, null, true,
                selected => $"Gönderim yeniden denenecek.\n\nBelge:\n{(selected as DataRowView)?["BelgeNo"]}\n\nÖnceki deneme:\n{(selected as DataRowView)?["Deneme"]}\n\nSon hata:\n{(selected as DataRowView)?["SonHata"]}"),
            A("outbox.open_document", "Belgeyi Aç", "edocuments.view", 20, ContextActionGroup.Related, x => Run(() => openDocument(DocumentId(x))), Completed)
        ];
    }

    private static ContextActionDefinition A(string id, string header, string permission, int order, ContextActionGroup group, Func<object?, Task<ContextActionResult>> execute, Func<object?, bool>? visible = null, Func<object?, bool>? enabled = null, bool critical = false, Func<object?, string>? confirm = null) =>
        new(id, header, permission, "", order, group, execute, visible, enabled, ContextSelectionMode.Single, true, critical, confirm, null, "ElectronicDocument",
            selected => (selected as DataRowView)?["Id"]?.ToString());
}
