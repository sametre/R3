using System.Data;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace R3.Desktop.ViewModels;

public enum CashDirectionKind { In, Out }

/// <summary>Backs both Nakit Giriş (§21) and Nakit Çıkış (§22) dialogs - same shape, only the
/// allowed transaction-type set and posting call differ. Posting math (negative-balance guard,
/// cari-side debit/credit) lives entirely in <see cref="R3.Infrastructure.LocalCashService"/>.</summary>
public sealed partial class CashInOutViewModel : ObservableObject
{
    private readonly CashServices _services;
    private readonly CashDirectionKind _kind;
    private readonly ILogger<CashInOutViewModel> _logger;

    public CashInOutViewModel(CashServices services, CashDirectionKind kind, string? preselectedCashAccountId, ILogger<CashInOutViewModel> logger)
    {
        _services = services;
        _kind = kind;
        _logger = logger;
        CashAccounts = services.Cash.Lookup(services.CompanyId).DefaultView;
        Date = DateTime.Today;
        TransactionType = AvailableTransactionTypes[0].Value;
        var accountRole = kind == CashDirectionKind.In ? "Customer" : "Supplier";
        Accounts = services.Accounts.Lookup(services.CompanyId, accountRole).DefaultView;
        SelectedCashAccountId = preselectedCashAccountId ?? (CashAccounts.Count > 0 ? CashAccounts[0]["Id"].ToString() : null);
    }

    public string Title => _kind == CashDirectionKind.In ? "Nakit Giriş" : "Nakit Çıkış";
    public DataView CashAccounts { get; }
    public DataView Accounts { get; }

    public IReadOnlyList<Option> AvailableTransactionTypes => _kind == CashDirectionKind.In
        ? [new("CustomerReceipt", "Müşteri Tahsilatı"), new("CashIncome", "Diğer Gelir"), new("OpeningBalance", "Açılış Bakiyesi"), new("ManualIn", "Manuel Giriş")]
        : [new("SupplierPayment", "Tedarikçi Ödemesi"), new("CashExpense", "Masraf"), new("ManualOut", "Manuel Çıkış")];

    [ObservableProperty] private string? _selectedCashAccountId;
    [ObservableProperty] private string _currencyCode = "";
    [ObservableProperty] private decimal _currentBalance;
    [ObservableProperty] private string _transactionType = "";
    [ObservableProperty] private string? _accountId;
    [ObservableProperty] private DateTime _date;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private string _documentNumber = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string? _errorMessage;

    public bool AccountRequired => TransactionType is "CustomerReceipt" or "SupplierPayment";
    public decimal NewBalance => _kind == CashDirectionKind.In ? CurrentBalance + Amount : CurrentBalance - Amount;

    partial void OnTransactionTypeChanged(string value) => OnPropertyChanged(nameof(AccountRequired));
    partial void OnAmountChanged(decimal value) => OnPropertyChanged(nameof(NewBalance));
    partial void OnSelectedCashAccountIdChanged(string? value)
    {
        if (string.IsNullOrEmpty(value)) { CurrentBalance = 0; CurrencyCode = ""; return; }
        CurrentBalance = _services.Cash.GetBalance(_services.CompanyId, value);
        var row = CashAccounts.Cast<DataRowView>().FirstOrDefault(r => r["Id"].ToString() == value);
        CurrencyCode = row?["CurrencyCode"].ToString() ?? "";
        OnPropertyChanged(nameof(NewBalance));
    }

    public event EventHandler? Saved;

    [RelayCommand]
    private void Save()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(SelectedCashAccountId)) throw new ArgumentException("Kasa seçimi zorunludur.");
            if (_kind == CashDirectionKind.In)
                _services.Cash.PostCashIn(_services.CompanyId, _services.BranchId, SelectedCashAccountId, TransactionType, AccountId, Date, Amount, CurrencyCode, 1, DocumentNumber, Description, _services.UserName);
            else
                _services.Cash.PostCashOut(_services.CompanyId, _services.BranchId, SelectedCashAccountId, TransactionType, AccountId, Date, Amount, CurrencyCode, 1, DocumentNumber, Description, _services.UserName);
            ErrorMessage = null;
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Kind} posting failed. CashAccountId={CashAccountId} TransactionType={TransactionType}", _kind, SelectedCashAccountId, TransactionType);
            ErrorMessage = _kind == CashDirectionKind.In ? "Nakit giriş kaydedilirken beklenmeyen bir hata oluştu." : "Nakit çıkış kaydedilirken beklenmeyen bir hata oluştu.";
        }
    }
}
