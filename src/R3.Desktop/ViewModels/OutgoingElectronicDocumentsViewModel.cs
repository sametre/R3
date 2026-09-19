using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3.Desktop.Logging;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

// Phase 9 (§8-10): Giden Belgeler. Every filter here becomes a WHERE clause inside
// ElectronicDocumentOperationsService.SearchOutgoing - never a client-side re-filter of an
// already-loaded table (§9/§34).
public sealed partial class OutgoingElectronicDocumentsViewModel : ObservableObject
{
    private readonly ElectronicDocumentOperationsService _operations;
    private readonly string _companyId;
    private readonly ILogger<OutgoingElectronicDocumentsViewModel> _logger;

    public OutgoingElectronicDocumentsViewModel(StoreDatabase database, string companyId, ILogger<OutgoingElectronicDocumentsViewModel>? logger = null)
    {
        _operations = new ElectronicDocumentOperationsService(database); _companyId = companyId;
        _logger = logger ?? DesktopLogging.CreateLogger<OutgoingElectronicDocumentsViewModel>();
    }

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string? _documentTypeFilter;
    [ObservableProperty] private string? _statusFilter;
    [ObservableProperty] private DateTime? _startDate;
    [ObservableProperty] private DateTime? _endDate;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    public DataView? Rows { get; private set; }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct = default)
    {
        IsBusy = true; StatusMessage = null;
        try
        {
            var filter = new ElectronicDocumentFilter(StartDate, EndDate, DocumentTypeFilter, StatusFilter, null, SearchText, 100);
            var table = await Task.Run(() => _operations.SearchOutgoing(_companyId, filter), ct);
            if (ct.IsCancellationRequested) return;
            Rows = table.DefaultView;
            OnPropertyChanged(nameof(Rows));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Outgoing electronic documents load failed. CompanyId={CompanyId}", _companyId);
            StatusMessage = "Giden belgeler yüklenirken bir hata oluştu.";
        }
        finally { IsBusy = false; }
    }
}
