using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

/// <summary>
/// Backs the Cari Kartı create/edit dialog. All persistence and validation rules
/// live in <see cref="LocalAccountService.Save"/>; this view model only shapes input.
/// </summary>
public sealed partial class AccountEditViewModel : ObservableObject
{
    private readonly LocalAccountService _accounts;
    private readonly string _companyId;
    private readonly string _id;
    private readonly ILogger<AccountEditViewModel> _logger;

    public AccountEditViewModel(LocalAccountService accounts, string companyId, AccountRowViewModel? existing, ILogger<AccountEditViewModel> logger)
    {
        _accounts = accounts;
        _companyId = companyId;
        _logger = logger;
        _id = existing?.Id ?? "";
        Title = existing == null ? "Yeni Cari" : "Cari Kartı";
        Balance = existing?.Balance ?? 0;
        AvailableCredit = existing?.AvailableCredit ?? 0;
        BalanceStatus = existing?.BalanceStatus ?? "Yeni kayıt";
        if (existing != null)
        {
            Code = existing.Code;
            Name = existing.Name;
            AccountType = existing.AccountType;
            TaxOffice = existing.TaxOffice;
            TaxNumber = existing.TaxNumber;
            Phone = existing.Phone;
            MobilePhone = existing.MobilePhone;
            Email = existing.Email;
            CreditLimit = existing.CreditLimit;
            RiskLimit = existing.RiskLimit;
            IsActive = existing.IsActive;
        }
    }

    public string Title { get; }
    public decimal Balance { get; }
    public decimal AvailableCredit { get; }
    public string BalanceStatus { get; }
    public IReadOnlyList<string> AccountTypes { get; } = ["Customer", "Supplier", "CustomerAndSupplier", "Other"];

    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _accountType = "Customer";
    [ObservableProperty] private string _taxOffice = "";
    [ObservableProperty] private string _taxNumber = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _mobilePhone = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private decimal _creditLimit;
    [ObservableProperty] private decimal _riskLimit;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string? _errorMessage;

    public event EventHandler? Saved;

    [RelayCommand]
    private void Save()
    {
        var isNew = string.IsNullOrEmpty(_id);
        try
        {
            _accounts.Save(new AccountEdit(_id, _companyId, Code, Name, AccountType, TaxOffice, TaxNumber, Phone, MobilePhone, Email, CreditLimit, RiskLimit, IsActive));
            ErrorMessage = null;
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (ArgumentException ex)
        {
            // LocalAccountService already produces a user-facing Turkish message for
            // known validation failures (missing fields, invalid account type). This
            // is expected input rejection, not a bug - no Error-level log needed.
            ErrorMessage = ex.Message;
        }
        catch (SqliteException ex)
        {
            _logger.LogError(ex, "Account {Action} failed. CompanyId={CompanyId} SqliteErrorCode={SqliteErrorCode}",
                isNew ? "creation" : "update", _companyId, ex.SqliteErrorCode);
            ErrorMessage = ex.SqliteErrorCode == 19
                ? "Bu cari kodu zaten kullanılıyor."
                : "Cari kaydı kaydedilirken bir hata oluştu.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Account {Action} failed. CompanyId={CompanyId}", isNew ? "creation" : "update", _companyId);
            ErrorMessage = "Cari kaydı kaydedilirken beklenmeyen bir hata oluştu.";
        }
    }
}
