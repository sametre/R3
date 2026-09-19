using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

public enum ReceiptPaymentKind { Receipt, Payment }

/// <summary>Backs both the Tahsilat (§41-42) and Ödeme (§43) dialogs - same shape, only the ledger
/// direction and dialog title differ. Posting math (debit/credit, running balance) lives entirely
/// in <see cref="LocalAccountService.PostReceipt"/>/<see cref="LocalAccountService.PostPayment"/>;
/// the "Yeni Bakiye" preview here mirrors that same debit-credit formula, it does not recompute it.</summary>
public sealed partial class ReceiptPaymentViewModel : ObservableObject
{
    private readonly AccountServices _services;
    private readonly ReceiptPaymentKind _kind;
    private readonly ILogger<ReceiptPaymentViewModel> _logger;

    public ReceiptPaymentViewModel(AccountServices services, ReceiptPaymentKind kind, string accountId, string accountName, ILogger<ReceiptPaymentViewModel> logger)
    {
        _services = services;
        _kind = kind;
        _logger = logger;
        AccountId = accountId;
        AccountName = accountName;
        CurrentBalance = services.Accounts.GetBalance(services.CompanyId, accountId);
        Title = kind == ReceiptPaymentKind.Receipt ? "Yeni Tahsilat" : "Yeni Ödeme";
        Date = DateTime.Today;
    }

    public string Title { get; }
    public string AccountId { get; }
    public string AccountName { get; }
    public decimal CurrentBalance { get; }
    public IReadOnlyList<Option> PaymentMethods { get; } =
        [new("Cash", "Nakit"), new("BankTransfer", "Banka"), new("CreditCard", "Kredi Kartı"), new("Cheque", "Çek"), new("PromissoryNote", "Senet")];
    public IReadOnlyList<string> Currencies { get; } = ["TRY", "USD", "EUR", "GBP"];

    [ObservableProperty] private DateTime _date;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private string _currencyCode = "TRY";
    [ObservableProperty] private decimal _exchangeRate = 1;
    [ObservableProperty] private string _paymentMethod = "Cash";
    [ObservableProperty] private string _documentNo = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string? _errorMessage;

    public bool RequiresCashAccount => PaymentMethod == "Cash";
    public bool RequiresBankAccount => PaymentMethod == "BankTransfer";
    public bool IsForeignCurrency => CurrencyCode != "TRY";

    /// <summary>Mirrors LocalAccountService.Post's debit-credit=balance formula for the preview only;
    /// the authoritative value is whatever the posting call itself writes to account_balances.</summary>
    public decimal NewBalance => _kind == ReceiptPaymentKind.Receipt ? CurrentBalance - Amount : CurrentBalance + Amount;

    partial void OnPaymentMethodChanged(string value) { OnPropertyChanged(nameof(RequiresCashAccount)); OnPropertyChanged(nameof(RequiresBankAccount)); }
    partial void OnCurrencyCodeChanged(string value) { OnPropertyChanged(nameof(IsForeignCurrency)); if (!IsForeignCurrency) ExchangeRate = 1; }
    partial void OnAmountChanged(decimal value) => OnPropertyChanged(nameof(NewBalance));

    public event EventHandler? Saved;

    [RelayCommand]
    private void Save()
    {
        try
        {
            if (_kind == ReceiptPaymentKind.Receipt)
                _services.Accounts.PostReceipt(_services.CompanyId, _services.BranchId, AccountId, Date, Amount, CurrencyCode, ExchangeRate, PaymentMethod, DocumentNo, Description);
            else
                _services.Accounts.PostPayment(_services.CompanyId, _services.BranchId, AccountId, Date, Amount, CurrencyCode, ExchangeRate, PaymentMethod, DocumentNo, Description);
            ErrorMessage = null;
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Kind} posting failed. AccountId={AccountId}", _kind, AccountId);
            ErrorMessage = _kind == ReceiptPaymentKind.Receipt ? "Tahsilat kaydedilirken beklenmeyen bir hata oluştu." : "Ödeme kaydedilirken beklenmeyen bir hata oluştu.";
        }
    }
}

/// <summary>Backs the manual cari borç/alacak dialog (§44). Requires a mandatory description and
/// posts through the same <see cref="LocalAccountService.PostManualEntry"/> ledger path as every
/// other posting in this feature - no separate "manual" balance math.</summary>
public sealed partial class ManualLedgerEntryViewModel : ObservableObject
{
    private readonly AccountServices _services;
    private readonly ILogger<ManualLedgerEntryViewModel> _logger;

    public ManualLedgerEntryViewModel(AccountServices services, string accountId, string accountName, ILogger<ManualLedgerEntryViewModel> logger)
    {
        _services = services;
        _logger = logger;
        AccountId = accountId;
        AccountName = accountName;
        Date = DateTime.Today;
    }

    public string AccountId { get; }
    public string AccountName { get; }
    public IReadOnlyList<Option> Directions { get; } = [new("Debit", "Borçlandır"), new("Credit", "Alacaklandır")];

    [ObservableProperty] private string _direction = "Debit";
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private string _currencyCode = "TRY";
    [ObservableProperty] private decimal _exchangeRate = 1;
    [ObservableProperty] private DateTime _date;
    [ObservableProperty] private DateTime? _dueDate;
    [ObservableProperty] private string _documentNo = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string? _errorMessage;

    public event EventHandler? Saved;

    [RelayCommand]
    private void Save()
    {
        try
        {
            _services.Accounts.PostManualEntry(_services.CompanyId, _services.BranchId, AccountId, Direction, Amount, CurrencyCode, ExchangeRate, Date, DocumentNo, Description);
            ErrorMessage = null;
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual ledger entry failed. AccountId={AccountId}", AccountId);
            ErrorMessage = "Cari hareketi kaydedilirken beklenmeyen bir hata oluştu.";
        }
    }
}
