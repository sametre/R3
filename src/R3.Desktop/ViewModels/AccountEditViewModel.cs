using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public AccountEditViewModel(LocalAccountService accounts, string companyId, AccountRowViewModel? existing)
    {
        _accounts = accounts;
        _companyId = companyId;
        _id = existing?.Id ?? "";
        Title = existing == null ? "Yeni Cari" : "Cari Kartı";
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
        try
        {
            _accounts.Save(new AccountEdit(_id, _companyId, Code, Name, AccountType, TaxOffice, TaxNumber, Phone, MobilePhone, Email, CreditLimit, RiskLimit, IsActive));
            ErrorMessage = null;
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
