using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace R3.Desktop.ViewModels;

/// <summary>Backs the Kasalar Arası Transfer dialog (§26-27). Same-currency-only in this phase -
/// <see cref="R3.Infrastructure.LocalCashService.Transfer"/> rejects a mismatch explicitly rather
/// than silently converting.</summary>
public sealed partial class CashTransferViewModel : ObservableObject
{
    private readonly CashServices _services;
    private readonly ILogger<CashTransferViewModel> _logger;

    public CashTransferViewModel(CashServices services, string? preselectedSourceId, ILogger<CashTransferViewModel> logger)
    {
        _services = services;
        _logger = logger;
        CashAccounts = services.Cash.Lookup(services.CompanyId).DefaultView;
        Date = DateTime.Today;
        if (preselectedSourceId != null) SourceCashAccountId = preselectedSourceId;
    }

    public DataView CashAccounts { get; }

    [ObservableProperty] private string? _sourceCashAccountId;
    [ObservableProperty] private string? _targetCashAccountId;
    [ObservableProperty] private DateTime _date;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string? _errorMessage;

    public event EventHandler? Saved;

    [RelayCommand]
    private void Save()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(SourceCashAccountId) || string.IsNullOrWhiteSpace(TargetCashAccountId))
                throw new ArgumentException("Kaynak ve hedef kasa seçimi zorunludur.");
            _services.Cash.Transfer(_services.CompanyId, _services.BranchId, SourceCashAccountId, TargetCashAccountId, Date, Amount, Description, _services.UserName);
            ErrorMessage = null;
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cash transfer failed. CompanyId={CompanyId} Source={Source} Target={Target}", _services.CompanyId, SourceCashAccountId, TargetCashAccountId);
            ErrorMessage = "Transfer kaydedilirken beklenmeyen bir hata oluştu.";
        }
    }
}
