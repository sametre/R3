using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3.Desktop.Logging;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

// Phase 9 (§12-19): Gönderim Kuyruğu. ManualRun/ManualRetry only ever call the canonical
// ElectronicDocumentOutboxService/ElectronicDocumentDispatcher - this class never flips an outbox
// or document status itself (§17/§18/§ SON HEDEF).
public sealed partial class ElectronicDocumentOutboxViewModel : ObservableObject
{
    private readonly StoreDatabase _database;
    private readonly ElectronicDocumentOperationsService _operations;
    private readonly LocalElectronicDocumentService _documents;
    private readonly ElectronicDocumentOutboxService _outbox;
    private readonly ElectronicDocumentDispatcher _dispatcher;
    private readonly string _companyId, _userId;
    private readonly ILogger<ElectronicDocumentOutboxViewModel> _logger;

    public ElectronicDocumentOutboxViewModel(StoreDatabase database, string companyId, string userId, ILogger<ElectronicDocumentOutboxViewModel>? logger = null)
    {
        _database = database; _companyId = companyId; _userId = userId;
        _operations = new ElectronicDocumentOperationsService(database);
        _documents = new LocalElectronicDocumentService(database);
        _outbox = new ElectronicDocumentOutboxService(database, _documents);
        _dispatcher = new ElectronicDocumentDispatcher(_documents, _outbox, new DevelopmentElectronicDocumentProvider());
        _logger = logger ?? DesktopLogging.CreateLogger<ElectronicDocumentOutboxViewModel>();
    }

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string? _outboxStatusFilter;
    [ObservableProperty] private string? _documentTypeFilter;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;
    public DataView? Rows { get; private set; }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct = default)
    {
        IsBusy = true; StatusMessage = null;
        try
        {
            var filter = new ElectronicDocumentFilter(DocumentType: DocumentTypeFilter, OutboxStatus: OutboxStatusFilter, Search: SearchText, Limit: 100);
            var table = await Task.Run(() => _operations.SearchOutbox(_companyId, filter), ct);
            if (ct.IsCancellationRequested) return;
            Rows = table.DefaultView;
            OnPropertyChanged(nameof(Rows));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Outbox queue load failed. CompanyId={CompanyId}", _companyId);
            StatusMessage = "Gönderim kuyruğu yüklenirken bir hata oluştu.";
        }
        finally { IsBusy = false; }
    }

    public ElectronicDocumentOutboxRow? GetRow(string outboxId) => _outbox.Get(outboxId);

    // §18: "Şimdi Çalıştır" - the dispatcher's only public entry point is a full DispatchAsync batch
    // pass (ProcessOneAsync is private, by design - see docs/architecture/OUTBOX-DISPATCH.md); this
    // is the same canonical call InvoiceDetailViewModel.SendCommand makes, not a direct provider call.
    [RelayCommand]
    private async Task ManualRunAsync()
    {
        IsBusy = true; ErrorMessage = null;
        try { await _dispatcher.DispatchAsync("desktop-outbox", _userId); await RefreshAsync(); }
        catch (Exception ex) { _logger.LogError(ex, "Manual dispatch run failed."); ErrorMessage = "İşlem tamamlanamadı."; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ManualRetryAsync(string? electronicDocumentId)
    {
        if (string.IsNullOrWhiteSpace(electronicDocumentId)) return;
        IsBusy = true; ErrorMessage = null;
        try { _outbox.ManualRetry(electronicDocumentId, _userId); await _dispatcher.DispatchAsync("desktop-outbox", _userId); await RefreshAsync(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException) { ErrorMessage = ex.Message; }
        catch (Exception ex) { _logger.LogError(ex, "Manual retry failed. ElectronicDocumentId={ElectronicDocumentId}", electronicDocumentId); ErrorMessage = "İşlem tamamlanamadı."; }
        finally { IsBusy = false; }
    }
}
