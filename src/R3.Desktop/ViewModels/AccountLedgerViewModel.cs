using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3.Desktop.Presentation;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

public sealed record TransactionRow(string Date, string AccountCode, string AccountName, string TransactionType, string DocumentNo, string Description, decimal Debit, decimal Credit, string Currency, string AccountId = "", string SourceType = "", string SourceId = "", string CashAccountId = "", string BankAccountId = "") : ILedgerLinkRow
{
    public string TransactionTypeLabel => InventoryPresentation.TransactionTypeLabel(TransactionType);
}

/// <summary>Backs both the standalone "Cari Hareketler" screen (§39, accountId == null shows every
/// account) and the Cari Kartı "Hareketler" tab (§31-32, accountId fixed to one card) - same
/// read-only query (<see cref="LocalAccountService.RecentTransactions"/>), only the scope differs.</summary>
public sealed partial class AccountLedgerViewModel : ObservableObject
{
    private readonly LocalAccountService _accounts;
    private readonly string _companyId;
    private readonly string? _accountId;
    private readonly ILogger<AccountLedgerViewModel> _logger;

    public AccountLedgerViewModel(LocalAccountService accounts, string companyId, string? accountId, ILogger<AccountLedgerViewModel> logger)
    {
        _accounts = accounts;
        _companyId = companyId;
        _accountId = accountId;
        _logger = logger;
        Refresh();
    }

    public bool IsScopedToAccount => !string.IsNullOrEmpty(_accountId);
    public ObservableCollection<TransactionRow> Transactions { get; } = [];

    [ObservableProperty] private string? _statusMessage;

    [RelayCommand]
    public void Refresh()
    {
        StatusMessage = null;
        try
        {
            Transactions.Clear();
            foreach (DataRow row in _accounts.RecentTransactions(_companyId, 500, _accountId).Rows)
            {
                Transactions.Add(new TransactionRow(row["Tarih"].ToString()!, row["CariKodu"].ToString()!, row["Cari"].ToString()!,
                    row["IslemTipi"].ToString()!, row["BelgeNo"].ToString()!, row["Aciklama"].ToString()!,
                    Convert.ToDecimal(row["Borc"]), Convert.ToDecimal(row["Alacak"]), row["Doviz"].ToString()!,
                    row["CariId"].ToString()!, row["BelgeTipi"].ToString()!, row["BelgeId"].ToString()!, row["KasaId"].ToString()!, row["BankaId"].ToString()!));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ledger load failed. CompanyId={CompanyId} AccountId={AccountId}", _companyId, _accountId);
            StatusMessage = "Cari hareketleri yüklenirken bir hata oluştu.";
        }
    }
}
