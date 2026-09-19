using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3.Desktop.Logging;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

public sealed class AccountRowViewModel(
    string id, string code, string name, string accountType, string taxOffice, string taxNumber,
    string phone, string mobilePhone, string email, decimal debit, decimal credit, decimal balance,
    bool isActive, decimal creditLimit, decimal riskLimit, string city)
{
    public string Id { get; } = id;
    public string Code { get; } = code;
    public string Name { get; } = name;
    public string AccountType { get; } = accountType;
    public string AccountTypeLabel => AccountType switch { "Customer" => "Müşteri", "Supplier" => "Tedarikçi", "CustomerAndSupplier" => "Müşteri + Tedarikçi", _ => "Diğer" };
    public string TaxOffice { get; } = taxOffice;
    public string TaxNumber { get; } = taxNumber;
    public string Phone { get; } = phone;
    public string MobilePhone { get; } = mobilePhone;
    public string Email { get; } = email;
    public string City { get; } = city;
    public decimal Debit { get; } = debit;
    public decimal Credit { get; } = credit;
    public decimal Balance { get; } = balance;
    public string BalanceStatus => Balance switch { > 0 => "Borçlu", < 0 => "Alacaklı", _ => "Dengede" };
    public bool IsActive { get; } = isActive;
    public string StatusLabel => IsActive ? "Aktif" : "Pasif";
    public decimal CreditLimit { get; } = creditLimit;
    public decimal RiskLimit { get; } = riskLimit;
    public decimal AvailableCredit => CreditLimit <= 0 ? 0 : Math.Max(0, CreditLimit - Math.Max(0, Balance));
}

/// <summary>
/// MVVM reference implementation for the Cari Kartlar (Account) screen, also reused (via
/// accountTypeFilter) for the Müşteriler/Tedarikçiler filtered lists. Owns UI state only; every
/// read/write goes through <see cref="LocalAccountService"/>.
/// </summary>
public sealed partial class AccountsViewModel : ObservableObject
{
    private readonly AccountServices _services;
    private readonly string _userName;
    private readonly string? _accountTypeFilter;
    private readonly ILogger<AccountsViewModel> _logger;

    public AccountsViewModel(AccountServices services, string userName, ILogger<AccountsViewModel> logger, string? accountTypeFilter = null, string title = "Cari Kartlar")
    {
        _services = services;
        _userName = userName;
        _logger = logger;
        _accountTypeFilter = accountTypeFilter;
        Title = title;
        RefreshSummary();
    }

    public string Title { get; }
    public R3.Infrastructure.StoreDatabase Database => _services.Database;
    public string Subtitle => _accountTypeFilter switch
    {
        "Customer" => "Müşteri rolündeki aktif ve pasif cari hesaplar",
        "Supplier" => "Tedarikçi rolündeki aktif ve pasif cari hesaplar",
        _ => "Müşteri ve tedarikçi hesaplarının merkezi görünümü"
    };

    [ObservableProperty] private string _searchText = "";

    [ObservableProperty]
    private AccountRowViewModel? _selectedAccount;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private AccountDashboardSummary? _summary;

    public ObservableCollection<AccountRowViewModel> Accounts { get; } = [];

    public IReadOnlyList<string> AccountTypes { get; } = ["Customer", "Supplier", "CustomerAndSupplier", "Other"];

    private void RefreshSummary()
    {
        try { Summary = _services.Accounts.GetDashboardSummary(_services.CompanyId); }
        catch (Exception ex) { _logger.LogError(ex, "Account dashboard summary load failed. CompanyId={CompanyId}", _services.CompanyId); }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusMessage = null;
        try
        {
            var search = SearchText;
            var table = await Task.Run(() => _services.Accounts.Search(_services.CompanyId, search, _accountTypeFilter));
            Accounts.Clear();
            foreach (DataRow row in table.Rows)
            {
                Accounts.Add(new AccountRowViewModel(
                    row["Id"].ToString()!, row["Kod"].ToString()!, row["Cari"].ToString()!, row["Tip"].ToString()!,
                    row["VergiDairesi"].ToString()!, row["VergiNo"].ToString()!, row["Telefon"].ToString()!,
                    row["CepTelefonu"].ToString()!, row["Eposta"].ToString()!,
                    Convert.ToDecimal(row["Borc"]), Convert.ToDecimal(row["Alacak"]), Convert.ToDecimal(row["Bakiye"]),
                    Convert.ToBoolean(row["Aktif"]), Convert.ToDecimal(row["KrediLimiti"]), Convert.ToDecimal(row["RiskLimiti"]), row["Il"].ToString()!));
            }
            RefreshSummary();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Account list load failed. CompanyId={CompanyId}", _services.CompanyId);
            StatusMessage = "Cari listesi yüklenirken bir hata oluştu.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public AccountEditViewModel CreateEditViewModel(bool asNew) =>
        new(_services, _userName, asNew ? null : SelectedAccount?.Id);

    public ReceiptPaymentViewModel? CreateReceiptViewModel() =>
        SelectedAccount == null ? null : new ReceiptPaymentViewModel(_services, ReceiptPaymentKind.Receipt, SelectedAccount.Id, SelectedAccount.Name, DesktopLogging.CreateLogger<ReceiptPaymentViewModel>());

    public ReceiptPaymentViewModel? CreatePaymentViewModel() =>
        SelectedAccount == null ? null : new ReceiptPaymentViewModel(_services, ReceiptPaymentKind.Payment, SelectedAccount.Id, SelectedAccount.Name, DesktopLogging.CreateLogger<ReceiptPaymentViewModel>());

    public async Task SetSelectedActiveAsync(bool isActive)
    {
        if (SelectedAccount == null) return;
        var accountId = SelectedAccount.Id;
        try
        {
            await Task.Run(() => _services.Accounts.SetActive(_services.CompanyId, accountId, isActive));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Account status update failed. CompanyId={CompanyId} AccountId={AccountId} IsActive={IsActive}", _services.CompanyId, accountId, isActive);
            StatusMessage = "Cari durumu güncellenemedi.";
        }
    }

    partial void OnSearchTextChanged(string value) => _ = RefreshAsync();
}
