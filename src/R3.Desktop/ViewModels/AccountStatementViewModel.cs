using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3.Desktop.Presentation;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

public sealed record StatementLineRow(string Date, string DocumentNo, string Description, decimal Debit, decimal Credit, decimal Balance, string Currency, decimal ExchangeRate, string TransactionType, string AccountId = "", string SourceType = "", string SourceId = "", string CashAccountId = "", string BankAccountId = "") : ILedgerLinkRow
{
    public string TransactionTypeLabel => InventoryPresentation.TransactionTypeLabel(TransactionType);
}
public sealed record AccountLookupRow(string Id, string Code, string Name);

/// <summary>Backs the standalone Cari Ekstre screen (§40). The running-balance column is computed
/// in SQL by <see cref="LocalAccountService.Statement"/> (window function over the same debit-credit
/// values the ledger already stores) - this view model does not re-derive it.</summary>
public sealed partial class AccountStatementViewModel : ObservableObject
{
    private readonly LocalAccountService _accounts;
    private readonly string _companyId;
    private readonly string? _initialAccountId;
    private readonly ILogger<AccountStatementViewModel> _logger;
    private bool _initialized;
    private int _refreshVersion;

    public AccountStatementViewModel(LocalAccountService accounts, string companyId, string? initialAccountId, ILogger<AccountStatementViewModel> logger)
    {
        _accounts = accounts;
        _companyId = companyId;
        _initialAccountId = initialAccountId;
        _logger = logger;
    }

    public ObservableCollection<AccountLookupRow> Accounts { get; } = [];
    public ObservableCollection<StatementLineRow> Lines { get; } = [];

    [ObservableProperty] private AccountLookupRow? _selectedAccount;
    [ObservableProperty] private string? _statusMessage;

    public decimal TotalDebit => Lines.Sum(l => l.Debit);
    public decimal TotalCredit => Lines.Sum(l => l.Credit);
    public decimal ClosingBalance => Lines.Count == 0 ? 0 : Lines[^1].Balance;

    partial void OnSelectedAccountChanged(AccountLookupRow? value)
    {
        if (_initialized) _ = RefreshAsync();
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        try
        {
            StatusMessage = "Cari hesaplar yükleniyor…";
            var table = await Task.Run(() => _accounts.Lookup(_companyId));
            Accounts.Clear();
            foreach (DataRow row in table.Rows)
                Accounts.Add(new AccountLookupRow(row["Id"].ToString()!, row["Code"].ToString()!, row["Name"].ToString()!));
            SelectedAccount = _initialAccountId != null ? Accounts.FirstOrDefault(a => a.Id == _initialAccountId) : Accounts.FirstOrDefault();
            _initialized = true;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Statement load failed. CompanyId={CompanyId} AccountId={AccountId}", _companyId, SelectedAccount?.Id);
            StatusMessage = "Cari hesaplar yüklenirken bir hata oluştu.";
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!_initialized || SelectedAccount == null) return;
        var version = ++_refreshVersion;
        StatusMessage = "Ekstre yükleniyor…";
        try
        {
            var table = await Task.Run(() => _accounts.Statement(_companyId, SelectedAccount.Id));
            if (version != _refreshVersion) return;
            Lines.Clear();
            foreach (DataRow row in table.Rows)
                Lines.Add(new StatementLineRow(row["Tarih"].ToString()!, row["Belge"].ToString()!, row["Aciklama"].ToString()!,
                    Convert.ToDecimal(row["Borc"]), Convert.ToDecimal(row["Alacak"]), Convert.ToDecimal(row["Bakiye"]),
                    row["Doviz"].ToString()!, Convert.ToDecimal(row["Kur"]), row["IslemTipi"].ToString()!,
                    row["CariId"].ToString()!, row["BelgeTipi"].ToString()!, row["BelgeId"].ToString()!, row["KasaId"].ToString()!, row["BankaId"].ToString()!));
            StatusMessage = null;
            OnPropertyChanged(nameof(TotalDebit)); OnPropertyChanged(nameof(TotalCredit)); OnPropertyChanged(nameof(ClosingBalance));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Statement load failed. CompanyId={CompanyId} AccountId={AccountId}", _companyId, SelectedAccount?.Id);
            StatusMessage = "Cari ekstre yüklenirken bir hata oluştu.";
        }
    }
}
