using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3.Desktop.Logging;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

// Phase 9 (§20-23): Hatalı Belgeler - Failed/Rejected/DeadLetter/RetryPending in one screen
// (ElectronicDocumentOperationsService.SearchErrors already unions them in one query, §20).
public sealed partial class FailedElectronicDocumentsViewModel : ObservableObject
{
    private readonly ElectronicDocumentOperationsService _operations;
    private readonly LocalElectronicDocumentService _documents;
    private readonly ElectronicDocumentOutboxService _outbox;
    private readonly ElectronicDocumentDispatcher _dispatcher;
    private readonly string _companyId, _userId;
    private readonly ILogger<FailedElectronicDocumentsViewModel> _logger;

    public FailedElectronicDocumentsViewModel(StoreDatabase database, string companyId, string userId, ILogger<FailedElectronicDocumentsViewModel>? logger = null)
    {
        _operations = new ElectronicDocumentOperationsService(database);
        _documents = new LocalElectronicDocumentService(database);
        _outbox = new ElectronicDocumentOutboxService(database, _documents);
        _dispatcher = new ElectronicDocumentDispatcher(_documents, _outbox, new DevelopmentElectronicDocumentProvider());
        _companyId = companyId; _userId = userId;
        _logger = logger ?? DesktopLogging.CreateLogger<FailedElectronicDocumentsViewModel>();
    }

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string? _documentTypeFilter;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;
    public DataView? Rows { get; private set; }
    // §5: KPI cards read the same untruncated dashboard aggregate InvoiceDetail's dashboard uses -
    // never a count of the (LIMIT-capped) grid rows below, which would undercount past the limit.
    public ElectronicDocumentDashboardSummary? Summary { get; private set; }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct = default)
    {
        IsBusy = true; StatusMessage = null;
        try
        {
            var filter = new ElectronicDocumentFilter(DocumentType: DocumentTypeFilter, Search: SearchText, Limit: 100);
            var table = await Task.Run(() => _operations.SearchErrors(_companyId, filter), ct);
            var summary = await Task.Run(() => _operations.GetDashboardSummary(_companyId), ct);
            if (ct.IsCancellationRequested) return;
            Rows = table.DefaultView;
            Summary = summary;
            OnPropertyChanged(nameof(Rows)); OnPropertyChanged(nameof(Summary));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed electronic documents load failed. CompanyId={CompanyId}", _companyId);
            StatusMessage = "Hatalı belgeler yüklenirken bir hata oluştu.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RetryAsync(string? electronicDocumentId)
    {
        if (string.IsNullOrWhiteSpace(electronicDocumentId)) return;
        IsBusy = true; ErrorMessage = null;
        try { _outbox.ManualRetry(electronicDocumentId, _userId); await _dispatcher.DispatchAsync("desktop-errors", _userId); await RefreshAsync(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException) { ErrorMessage = ex.Message; }
        catch (Exception ex) { _logger.LogError(ex, "Retry from error center failed. ElectronicDocumentId={ElectronicDocumentId}", electronicDocumentId); ErrorMessage = "İşlem tamamlanamadı."; }
        finally { IsBusy = false; }
    }
}
