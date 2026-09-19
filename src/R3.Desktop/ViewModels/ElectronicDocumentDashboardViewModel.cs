using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3.Desktop.Logging;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

// Phase 9 (§4-7): "E-Belge Genel Bakış" - one KPI read (ElectronicDocumentOperationsService.
// GetDashboardSummary), one type-distribution read, one bounded recent-errors read. Never counts
// rows itself (§5 "UI tablo tablo kendisi sayım yapmasın").
public sealed partial class ElectronicDocumentDashboardViewModel : ObservableObject
{
    private readonly ElectronicDocumentOperationsService _operations;
    private readonly string _companyId;
    private readonly ILogger<ElectronicDocumentDashboardViewModel> _logger;

    public ElectronicDocumentDashboardViewModel(StoreDatabase database, string companyId, ILogger<ElectronicDocumentDashboardViewModel>? logger = null)
    {
        _operations = new ElectronicDocumentOperationsService(database); _companyId = companyId;
        _logger = logger ?? DesktopLogging.CreateLogger<ElectronicDocumentDashboardViewModel>();
    }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private ElectronicDocumentDashboardSummary? _summary;
    [ObservableProperty] private int _eInvoiceCount;
    [ObservableProperty] private int _eArchiveCount;
    [ObservableProperty] private int _eDespatchCount;
    public DataView? RecentErrors { get; private set; }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct = default)
    {
        IsBusy = true; StatusMessage = null;
        try
        {
            var summary = await Task.Run(() => _operations.GetDashboardSummary(_companyId), ct);
            var distribution = await Task.Run(() => _operations.GetTypeDistribution(_companyId), ct);
            var errors = await Task.Run(() => _operations.GetRecentErrors(_companyId, 15), ct);
            if (ct.IsCancellationRequested) return; // §33: a closed/superseded load must not write stale state
            Summary = summary;
            EInvoiceCount = distribution[ElectronicDocumentType.EInvoice];
            EArchiveCount = distribution[ElectronicDocumentType.EArchiveInvoice];
            EDespatchCount = distribution[ElectronicDocumentType.EDespatch];
            RecentErrors = errors.DefaultView;
            OnPropertyChanged(nameof(RecentErrors));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Electronic document dashboard load failed. CompanyId={CompanyId}", _companyId);
            StatusMessage = "Genel bakış yüklenirken bir hata oluştu.";
        }
        finally { IsBusy = false; }
    }
}
