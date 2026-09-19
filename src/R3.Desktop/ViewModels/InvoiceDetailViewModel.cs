using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3.Application.Security;
using R3.Desktop.Logging;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

// Phase 8: the one place that owns "Fatura Taslağı -> Faturayı Kes -> UBL Oluştur -> Gönder ->
// Durumu takip et" for a single invoice. Every mutating action here is a thin call into the
// canonical Posting/UBL/Outbox/Provider engines (LocalSalesService, ElectronicDocumentGenerationService,
// ElectronicDocumentOutboxService, ElectronicDocumentDispatcher) - this class never decides business
// status itself (§44 "UI State Source"), it only reloads and re-exposes what those engines decided.
// Kept WPF-free (no Window/Control types) on purpose so it can be unit tested the same way
// AccountEditViewModel/AccountsViewModel already are (see tests/R3.Desktop.Tests).
public sealed partial class InvoiceDetailViewModel : ObservableObject
{
    private readonly StoreDatabase _database;
    private readonly LocalSalesService _sales;
    private readonly LocalElectronicDocumentService _documents;
    private readonly ElectronicDocumentOutboxService _outbox;
    private readonly ElectronicDocumentGenerationService _generation;
    private readonly ElectronicDocumentDispatcher _dispatcher;
    private readonly IPermissionService _permissions;
    private readonly string _userId;
    private readonly ILogger<InvoiceDetailViewModel> _logger;

    public string InvoiceId { get; }

    // §30: permission defaults to a real LocalPermissionService (same "zero grants -> allow" bootstrap
    // rule as everywhere else in R3.Desktop), but callers - and tests - can inject a fake one; either
    // way, CanExecute AND the action body itself both check it (RequirePermission below), so a direct
    // ExecuteAsync call that bypasses the UI's CanExecute gate is still refused, not just hidden.
    public InvoiceDetailViewModel(StoreDatabase database, string invoiceId, string userId, ILogger<InvoiceDetailViewModel>? logger = null, IPermissionService? permissions = null)
    {
        _database = database; InvoiceId = invoiceId; _userId = userId;
        _logger = logger ?? DesktopLogging.CreateLogger<InvoiceDetailViewModel>();
        _documents = new LocalElectronicDocumentService(database);
        _sales = new LocalSalesService(database, new ElectronicDocumentRoutingService(database), _documents);
        _outbox = new ElectronicDocumentOutboxService(database, _documents);
        _generation = new ElectronicDocumentGenerationService(database, _documents, new UblInvoiceGenerator(database, _documents));
        _dispatcher = new ElectronicDocumentDispatcher(_documents, _outbox, new DevelopmentElectronicDocumentProvider());
        _permissions = permissions ?? new LocalPermissionService(database, userId);
    }

    // --- header / totals (§7/§10) ---
    [ObservableProperty] private string _documentNo = "";
    [ObservableProperty] private string _accountName = "";
    [ObservableProperty] private string _accountCode = "";
    [ObservableProperty] private string _branchName = "";
    [ObservableProperty] private string _warehouseName = "";
    [ObservableProperty] private DateTime _documentDate;
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _salesStatus = "Draft";
    [ObservableProperty] private decimal _subtotal;
    [ObservableProperty] private decimal _discountTotal;
    [ObservableProperty] private decimal _taxTotal;
    [ObservableProperty] private decimal _grandTotal;
    [ObservableProperty] private decimal _accountBalance;

    public bool IsDraft => SalesStatus == "Draft";
    public bool IsPosted => SalesStatus == "Posted";

    public ObservableCollection<SalesDocumentLineRow> Lines { get; } = [];
    public DataView AccountTransactions => _sales.GetAccountTransactions(InvoiceId).DefaultView;
    public DataView InventoryTransactions => _sales.GetInventoryTransactions(InvoiceId).DefaultView;
    public DataView AuditHistory => _sales.GetAuditHistory(InvoiceId).DefaultView;

    // --- electronic document (§19-31) ---
    [ObservableProperty] private string? _electronicDocumentId;
    [ObservableProperty] private ElectronicDocumentType? _eDocType;
    [ObservableProperty] private ElectronicDocumentStatus? _eDocStatus;
    [ObservableProperty] private string? _uuid;
    [ObservableProperty] private string? _providerDocumentId;
    [ObservableProperty] private int _sendAttemptCount;
    [ObservableProperty] private DateTime? _nextRetryAt;
    [ObservableProperty] private string? _lastErrorCode;
    [ObservableProperty] private string? _lastErrorMessage;

    public ObservableCollection<ElectronicDocumentEventRow> Events { get; } = [];
    public IReadOnlyList<ElectronicDocumentPayloadRow> Payloads { get; private set; } = [];
    // §26: the full row, for ElectronicDocumentDialogs.ShowProviderResponse's structured fields
    // (Provider/ProviderDocumentId/EnvelopeId) - avoids duplicating those onto separate properties here.
    public ElectronicDocumentRow? Document { get; private set; }

    // --- interaction state (§46-47) ---
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyText = "";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;

    public void Load()
    {
        var header = _sales.GetHeader(InvoiceId) ?? throw new KeyNotFoundException("Fatura bulunamadı.");
        DocumentNo = string.IsNullOrWhiteSpace(header.DocumentNo) ? "Taslak" : header.DocumentNo;
        AccountName = header.AccountName; AccountCode = header.AccountCode; BranchName = header.BranchName; WarehouseName = header.WarehouseName;
        DocumentDate = header.DocumentDate; Description = header.Description; SalesStatus = header.Status;
        Subtotal = header.Totals.Subtotal; DiscountTotal = header.Totals.DiscountTotal; TaxTotal = header.Totals.TaxTotal; GrandTotal = header.Totals.GrandTotal;
        AccountBalance = _sales.GetAccountBalance(header.AccountId);

        Lines.Clear();
        foreach (var line in _sales.GetLines(InvoiceId)) Lines.Add(line);

        var eDoc = _documents.GetBySource("SalesInvoice", InvoiceId);
        Document = eDoc;

        if (eDoc == null) { ElectronicDocumentId = null; EDocType = null; EDocStatus = null; Events.Clear(); Payloads = []; }
        else
        {
            ElectronicDocumentId = eDoc.Id; EDocType = eDoc.DocumentType; EDocStatus = eDoc.Status; Uuid = eDoc.Uuid;
            ProviderDocumentId = eDoc.ProviderDocumentId; SendAttemptCount = eDoc.SendAttemptCount; NextRetryAt = eDoc.NextRetryAt;
            LastErrorCode = eDoc.LastErrorCode; LastErrorMessage = eDoc.LastErrorMessage;
            Events.Clear(); foreach (var e in _documents.GetEvents(eDoc.Id)) Events.Add(e);
            Payloads = _documents.GetPayloads(eDoc.Id);
        }
        OnPropertyChanged(nameof(IsDraft)); OnPropertyChanged(nameof(IsPosted));
        OnPropertyChanged(nameof(AccountTransactions)); OnPropertyChanged(nameof(InventoryTransactions)); OnPropertyChanged(nameof(AuditHistory));
        RaiseCanExecuteChanged();
    }

    public string? XmlPayload => Payloads.Where(p => p.PayloadType is ElectronicDocumentPayloadType.SignedXml or ElectronicDocumentPayloadType.UblXml)
        .OrderByDescending(p => p.PayloadType == ElectronicDocumentPayloadType.SignedXml).ThenByDescending(p => p.Version).FirstOrDefault()?.Content;

    [RelayCommand(CanExecute = nameof(CanPost))]
    private async Task PostAsync()
    {
        await RunAsync("Fatura kesiliyor...", () =>
        {
            RequirePermission("sales.invoice.post");
            _sales.Post(InvoiceId, _userId);
            return Task.CompletedTask;
        }, logUnexpected: "Invoice posting failed. InvoiceId={InvoiceId}");
    }
    private bool CanPost() => IsDraft && !IsBusy && _permissions.HasPermission("sales.invoice.post");

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        await RunAsync("UBL oluşturuluyor...", () =>
        {
            RequirePermission("edocuments.generate");
            var result = _generation.Generate(ElectronicDocumentId!, _userId);
            if (!result.Success) throw new InvalidOperationException(result.ErrorMessage ?? "UBL üretilemedi.");
            return Task.CompletedTask;
        }, logUnexpected: "UBL generation failed. ElectronicDocumentId={ElectronicDocumentId}");
    }
    private bool CanGenerate() => EDocStatus == ElectronicDocumentStatus.Ready && !IsBusy && _permissions.HasPermission("edocuments.generate");

    [RelayCommand(CanExecute = nameof(CanQueue))]
    private async Task QueueAsync()
    {
        await RunAsync("Kuyruğa alınıyor...", () =>
        {
            RequirePermission("edocuments.send");
            _outbox.QueueForSendAsync(ElectronicDocumentId!, _userId);
            return Task.CompletedTask;
        }, logUnexpected: "Queueing for send failed. ElectronicDocumentId={ElectronicDocumentId}");
    }
    private bool CanQueue() => EDocStatus == ElectronicDocumentStatus.Generated && !IsBusy && _permissions.HasPermission("edocuments.send");

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        await RunAsync("Gönderim hazırlanıyor...", () =>
        {
            RequirePermission("edocuments.send");
            return _dispatcher.DispatchAsync("desktop-manual", _userId);
        }, logUnexpected: "Manual dispatch failed. ElectronicDocumentId={ElectronicDocumentId}");
    }
    private bool CanSend() => EDocStatus == ElectronicDocumentStatus.Queued && !IsBusy && _permissions.HasPermission("edocuments.send");

    [RelayCommand(CanExecute = nameof(CanQueryStatus))]
    private async Task QueryStatusAsync()
    {
        await RunAsync("Durum sorgulanıyor...", async () =>
        {
            RequirePermission("edocuments.status.query");
            _outbox.QueueStatusQuery(ElectronicDocumentId!);
            await _dispatcher.DispatchAsync("desktop-manual", _userId);
        }, logUnexpected: "Status query failed. ElectronicDocumentId={ElectronicDocumentId}");
    }
    private bool CanQueryStatus() => EDocStatus is ElectronicDocumentStatus.Sent or ElectronicDocumentStatus.Delivered && !IsBusy && _permissions.HasPermission("edocuments.status.query");

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private async Task RetryAsync()
    {
        await RunAsync("Tekrar gönderim hazırlanıyor...", async () =>
        {
            RequirePermission("edocuments.retry");
            _outbox.ManualRetry(ElectronicDocumentId!, _userId);
            await _dispatcher.DispatchAsync("desktop-manual", _userId);
        }, logUnexpected: "Manual retry failed. ElectronicDocumentId={ElectronicDocumentId}");
    }
    private bool CanRetry() => EDocStatus == ElectronicDocumentStatus.Failed && !IsBusy && _permissions.HasPermission("edocuments.retry");

    public bool CanViewPayload => _permissions.HasPermission("edocuments.payload.view");
    public bool CanViewProviderResponse => _permissions.HasPermission("edocuments.provider_response.view");

    // §30: the enforcement point a direct ExecuteAsync (bypassing CanExecute/the UI button) still
    // hits - R3 has no service-layer permission interceptor anywhere yet (not just here), so this is
    // the closest thing to "backend" authorization this architecture has today; see
    // docs/architecture/EDOCUMENT-OPERATIONS.md §13 for why that is a disclosed gap, not hidden.
    private void RequirePermission(string code) { if (!_permissions.HasPermission(code)) throw new InvalidOperationException("Bu işlem için yetkiniz bulunmuyor."); }

    // Every action shares the same shape (§46-47/§48 "Post command disabled while busy"): set
    // IsBusy+BusyText, run the one real backend call, reload the canonical state, and translate
    // expected business rejections (ArgumentException/InvalidOperationException/KeyNotFoundException -
    // the same families LocalSalesService/generation/outbox already throw with Turkish messages) into
    // ErrorMessage without logging them as bugs; anything else is logged and shown generically.
    private async Task RunAsync(string busyText, Func<Task> action, string logUnexpected)
    {
        IsBusy = true; BusyText = busyText; ErrorMessage = null; StatusMessage = null;
        RaiseCanExecuteChanged();
        try
        {
            await action();
            Load();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, logUnexpected, ElectronicDocumentId ?? InvoiceId);
            ErrorMessage = "İşlem tamamlanamadı. Lütfen kayıt durumunu kontrol edip yeniden deneyin.";
        }
        finally
        {
            IsBusy = false; BusyText = ""; RaiseCanExecuteChanged();
        }
    }

    private void RaiseCanExecuteChanged()
    {
        PostCommand.NotifyCanExecuteChanged(); GenerateCommand.NotifyCanExecuteChanged(); QueueCommand.NotifyCanExecuteChanged();
        SendCommand.NotifyCanExecuteChanged(); QueryStatusCommand.NotifyCanExecuteChanged(); RetryCommand.NotifyCanExecuteChanged();
    }
}
