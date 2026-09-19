using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using R3.Desktop.Logging;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

/// <summary>Backs the Kasa Kartı create/edit dialog (§16-19). Persistence and validation live in
/// <see cref="LocalCashService.Save"/> - this view model only shapes input.</summary>
public sealed partial class CashAccountEditViewModel : ObservableObject
{
    private readonly CashServices _services;
    private readonly string _id;
    private readonly ILogger<CashAccountEditViewModel> _logger;

    public CashAccountEditViewModel(CashServices services, string? cashAccountId, ILogger<CashAccountEditViewModel>? logger = null)
    {
        _services = services;
        _id = cashAccountId ?? "";
        _logger = logger ?? DesktopLogging.CreateLogger<CashAccountEditViewModel>();

        Companies = services.Database.Query("SELECT id AS Id, name AS Ad FROM companies WHERE is_active=1 ORDER BY code").DefaultView;
        Branches = services.Database.Query("SELECT id AS Id, name AS Ad FROM branches WHERE company_id=$c AND is_active=1 ORDER BY code", ("$c", services.CompanyId)).DefaultView;
        Currencies = services.MasterData.List("currencies").DefaultView;

        Title = string.IsNullOrEmpty(_id) ? "Yeni Kasa" : "Kasa Kartı";
        CompanyId = services.CompanyId;
        BranchId = services.BranchId;

        if (!string.IsNullOrEmpty(_id))
        {
            var detail = services.Cash.GetDetail(services.CompanyId, _id);
            if (detail != null)
            {
                Code = detail.Account.Code; Name = detail.Account.Name; CompanyId = detail.Account.CompanyId; BranchId = detail.Account.BranchId;
                CashAccountType = detail.Account.CashAccountType; CurrencyCode = detail.Account.CurrencyCode; IsActive = detail.Account.IsActive;
                AllowNegativeBalance = detail.Account.AllowNegativeBalance; Description = detail.Account.Description;
                Balance = detail.Balance; TotalIn = detail.TotalIn; TotalOut = detail.TotalOut;
            }
        }
    }

    public string Title { get; }
    public bool IsNew => string.IsNullOrEmpty(_id);
    public DataView Companies { get; }
    public DataView Branches { get; }
    public DataView Currencies { get; }

    public IReadOnlyList<Option> CashAccountTypes { get; } =
    [
        new("MainCash", "Ana Kasa"), new("BranchCash", "Şube Kasası"), new("StoreCash", "Mağaza Kasası"), new("POSCash", "POS Kasası"),
        new("ForeignCurrencyCash", "Döviz Kasası"), new("PettyCash", "Masraf Kasası"), new("Other", "Diğer")
    ];

    [ObservableProperty] private string _companyId;
    [ObservableProperty] private string _branchId;
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _cashAccountType = "MainCash";
    [ObservableProperty] private string _currencyCode = "TRY";
    [ObservableProperty] private bool _allowNegativeBalance;
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private decimal _balance;
    [ObservableProperty] private decimal _totalIn;
    [ObservableProperty] private decimal _totalOut;
    [ObservableProperty] private string? _errorMessage;

    public event EventHandler? Saved;

    [RelayCommand]
    private void Save()
    {
        var isNew = IsNew;
        try
        {
            _services.Cash.Save(new CashAccountEdit(_id, CompanyId, BranchId, Code, Name, CurrencyCode, CashAccountType, null, IsActive, AllowNegativeBalance, Description), _services.UserName);
            ErrorMessage = null;
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (SqliteException ex)
        {
            _logger.LogError(ex, "Cash account {Action} failed. CompanyId={CompanyId} SqliteErrorCode={SqliteErrorCode}", isNew ? "creation" : "update", CompanyId, ex.SqliteErrorCode);
            ErrorMessage = ex.SqliteErrorCode == 19 ? "Bu kasa kodu bu şubede zaten kullanılıyor." : "Kasa kaydedilirken bir hata oluştu.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cash account {Action} failed. CompanyId={CompanyId}", isNew ? "creation" : "update", CompanyId);
            ErrorMessage = "Kasa kaydedilirken beklenmeyen bir hata oluştu.";
        }
    }
}
