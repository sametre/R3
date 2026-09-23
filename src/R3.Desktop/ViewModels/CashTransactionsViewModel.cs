using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace R3.Desktop.ViewModels;

public sealed record CashTransactionRow(string Id, string Date, string CashAccount, string TransactionType, string Document, string Account,
    string Description, decimal In, decimal Out, string Currency, decimal ExchangeRate, string User, string Status, string AccountId = "", string SourceType = "", string SourceId = "", string CashAccountId = "", string BankAccountId = "") : ILedgerLinkRow;

/// <summary>Backs the standalone Kasa Hareketleri screen (§20). Read-only besides the reversal
/// action (§30), which delegates to <see cref="R3.Infrastructure.LocalCashService.Reverse"/>.</summary>
public sealed partial class CashTransactionsViewModel : ObservableObject
{
    private readonly CashServices _services;
    private readonly ILogger<CashTransactionsViewModel> _logger;

    public CashTransactionsViewModel(CashServices services, ILogger<CashTransactionsViewModel> logger, string? cashAccountId = null)
    {
        _services = services;
        _logger = logger;
        CashAccounts = services.Cash.Lookup(services.CompanyId).DefaultView;
        _selectedCashAccountId = cashAccountId;
        Refresh();
    }

    public DataView CashAccounts { get; }
    public ObservableCollection<CashTransactionRow> Transactions { get; } = [];

    public IReadOnlyList<Option> TransactionTypes { get; } =
    [
        new("", "Tümü"), new("OpeningBalance", "Açılış Bakiyesi"), new("CustomerReceipt", "Müşteri Tahsilatı"), new("SupplierPayment", "Tedarikçi Ödemesi"),
        new("CashIncome", "Diğer Gelir"), new("CashExpense", "Masraf"), new("CashTransferOut", "Kasa Transfer Çıkış"), new("CashTransferIn", "Kasa Transfer Giriş"),
        new("ManualIn", "Manuel Giriş"), new("ManualOut", "Manuel Çıkış"), new("Cancellation", "İptal")
    ];
    public IReadOnlyList<Option> Directions { get; } = [new("", "Tümü"), new("In", "Giriş"), new("Out", "Çıkış")];

    [ObservableProperty] private string? _selectedCashAccountId;
    [ObservableProperty] private DateTime? _fromDate;
    [ObservableProperty] private DateTime? _toDate;
    [ObservableProperty] private string _transactionType = "";
    [ObservableProperty] private string _direction = "";
    [ObservableProperty] private CashTransactionRow? _selected;
    [ObservableProperty] private string? _statusMessage;

    [RelayCommand]
    public void Refresh()
    {
        StatusMessage = null;
        try
        {
            Transactions.Clear();
            var table = _services.Cash.GetTransactions(_services.CompanyId, SelectedCashAccountId, FromDate, ToDate,
                string.IsNullOrEmpty(TransactionType) ? null : TransactionType, string.IsNullOrEmpty(Direction) ? null : Direction);
            foreach (DataRow row in table.Rows)
            {
                Transactions.Add(new CashTransactionRow(row["Id"].ToString()!, row["Tarih"].ToString()!, row["Kasa"].ToString()!, row["IslemTipi"].ToString()!,
                    row["Belge"].ToString()!, row["Cari"].ToString()!, row["Aciklama"].ToString()!, Convert.ToDecimal(row["Giris"]), Convert.ToDecimal(row["Cikis"]),
                    row["Doviz"].ToString()!, Convert.ToDecimal(row["Kur"]), row["Kullanici"].ToString()!, row["Durum"].ToString()!,
                    row["CariId"].ToString()!, row["BelgeTipi"].ToString()!, row["BelgeId"].ToString()!, row["KasaId"].ToString()!, row["BankaId"].ToString()!));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cash transactions load failed. CompanyId={CompanyId}", _services.CompanyId);
            StatusMessage = "Kasa hareketleri yüklenirken bir hata oluştu.";
        }
    }

    [RelayCommand]
    private void ReverseSelected()
    {
        if (Selected == null) { StatusMessage = "Önce bir hareket seçin."; return; }
        if (Selected.Status == "Reversed") { StatusMessage = "Bu hareket zaten iptal edilmiş."; return; }
        try
        {
            _services.Cash.Reverse(_services.CompanyId, Selected.Id, _services.UserName);
            Refresh();
        }
        catch (ArgumentException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cash transaction reversal failed. CashTransactionId={CashTransactionId}", Selected.Id);
            StatusMessage = "Hareket iptal edilirken bir hata oluştu.";
        }
    }
}
