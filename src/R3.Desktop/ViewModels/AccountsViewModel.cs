using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

public sealed class AccountRowViewModel(
    string id, string code, string name, string accountType, string taxOffice, string taxNumber,
    string phone, string mobilePhone, string email, decimal debit, decimal credit, decimal balance,
    bool isActive, decimal creditLimit, decimal riskLimit)
{
    public string Id { get; } = id;
    public string Code { get; } = code;
    public string Name { get; } = name;
    public string AccountType { get; } = accountType;
    public string TaxOffice { get; } = taxOffice;
    public string TaxNumber { get; } = taxNumber;
    public string Phone { get; } = phone;
    public string MobilePhone { get; } = mobilePhone;
    public string Email { get; } = email;
    public decimal Debit { get; } = debit;
    public decimal Credit { get; } = credit;
    public decimal Balance { get; } = balance;
    public string BalanceStatus => Balance switch { > 0 => "Borçlu", < 0 => "Alacaklı", _ => "Sıfır" };
    public bool IsActive { get; } = isActive;
    public decimal CreditLimit { get; } = creditLimit;
    public decimal RiskLimit { get; } = riskLimit;
    public decimal AvailableCredit => CreditLimit <= 0 ? 0 : Math.Max(0, CreditLimit - Math.Max(0, Balance));
}

/// <summary>
/// MVVM reference implementation for the Cari Kartlar (Account) screen.
/// Owns UI state only; every read/write goes through <see cref="LocalAccountService"/>.
/// </summary>
public sealed partial class AccountsViewModel : ObservableObject
{
    private readonly LocalAccountService _accounts;
    private readonly string _companyId;

    public AccountsViewModel(LocalAccountService accounts, string companyId)
    {
        _accounts = accounts;
        _companyId = companyId;
    }

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private AccountRowViewModel? _selectedAccount;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    public ObservableCollection<AccountRowViewModel> Accounts { get; } = [];

    public IReadOnlyList<string> AccountTypes { get; } = ["Customer", "Supplier", "CustomerAndSupplier", "Other"];

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusMessage = null;
        try
        {
            var search = SearchText;
            var table = await Task.Run(() => _accounts.Search(_companyId, search));
            Accounts.Clear();
            foreach (DataRow row in table.Rows)
            {
                Accounts.Add(new AccountRowViewModel(
                    row["Id"].ToString()!, row["Kod"].ToString()!, row["Cari"].ToString()!, row["Tip"].ToString()!,
                    row["VergiDairesi"].ToString()!, row["VergiNo"].ToString()!, row["Telefon"].ToString()!,
                    row["CepTelefonu"].ToString()!, row["Eposta"].ToString()!,
                    Convert.ToDecimal(row["Borc"]), Convert.ToDecimal(row["Alacak"]), Convert.ToDecimal(row["Bakiye"]),
                    Convert.ToBoolean(row["Aktif"]), Convert.ToDecimal(row["KrediLimiti"]), Convert.ToDecimal(row["RiskLimiti"])));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public AccountEditViewModel CreateEditViewModel(bool asNew) =>
        new(_accounts, _companyId, asNew ? null : SelectedAccount);

    partial void OnSearchTextChanged(string value) => _ = RefreshAsync();
}
