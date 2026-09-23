using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3.Desktop.Presentation;

namespace R3.Desktop.ViewModels;

public sealed record CashLookupRow(string Id, string Code, string Name);
public sealed record CashStatementLineRow(string Date, string Document, string TransactionType, string Description, decimal In, decimal Out, decimal Balance, string AccountId = "", string SourceType = "", string SourceId = "", string CashAccountId = "", string BankAccountId = "") : ILedgerLinkRow
{
    public string TransactionTypeLabel => InventoryPresentation.TransactionTypeLabel(TransactionType);
}

/// <summary>Backs the Kasa Ekstresi screen (§28). Devir + running balance are computed entirely by
/// <see cref="R3.Infrastructure.LocalCashService.GetStatement"/> - this view model only shapes them
/// for the grid and the Toplam Giriş/Çıkış/Kapanış summary.</summary>
public sealed partial class CashStatementViewModel : ObservableObject
{
    private readonly CashServices _services;
    private readonly ILogger<CashStatementViewModel> _logger;

    public CashStatementViewModel(CashServices services, string? initialCashAccountId, ILogger<CashStatementViewModel> logger)
    {
        _services = services;
        _logger = logger;
        foreach (DataRow row in services.Cash.Lookup(services.CompanyId).Rows)
            CashAccounts.Add(new CashLookupRow(row["Id"].ToString()!, row["Code"].ToString()!, row["Name"].ToString()!));
        SelectedCashAccount = initialCashAccountId != null ? CashAccounts.FirstOrDefault(a => a.Id == initialCashAccountId) : CashAccounts.FirstOrDefault();
    }

    public ObservableCollection<CashLookupRow> CashAccounts { get; } = [];
    public ObservableCollection<CashStatementLineRow> Lines { get; } = [];

    [ObservableProperty] private CashLookupRow? _selectedCashAccount;
    [ObservableProperty] private DateTime? _fromDate;
    [ObservableProperty] private DateTime? _toDate;
    [ObservableProperty] private decimal _openingBalance;
    [ObservableProperty] private string? _statusMessage;

    public decimal TotalIn => Lines.Sum(l => l.In);
    public decimal TotalOut => Lines.Sum(l => l.Out);
    public decimal ClosingBalance => Lines.Count == 0 ? OpeningBalance : Lines[^1].Balance;

    partial void OnSelectedCashAccountChanged(CashLookupRow? value) => Refresh();

    [RelayCommand]
    public void Refresh()
    {
        StatusMessage = null;
        try
        {
            Lines.Clear();
            if (SelectedCashAccount != null)
            {
                var (opening, lines) = _services.Cash.GetStatement(_services.CompanyId, SelectedCashAccount.Id, FromDate, ToDate);
                OpeningBalance = opening;
                foreach (DataRow row in lines.Rows)
                    Lines.Add(new CashStatementLineRow(row["Tarih"].ToString()!, row["Belge"].ToString()!, row["IslemTipi"].ToString()!, row["Aciklama"].ToString()!,
                        Convert.ToDecimal(row["Giris"]), Convert.ToDecimal(row["Cikis"]), Convert.ToDecimal(row["Bakiye"]),
                        row["CariId"].ToString()!, row["BelgeTipi"].ToString()!, row["BelgeId"].ToString()!));
            }
            OnPropertyChanged(nameof(TotalIn)); OnPropertyChanged(nameof(TotalOut)); OnPropertyChanged(nameof(ClosingBalance));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cash statement load failed. CompanyId={CompanyId} CashAccountId={CashAccountId}", _services.CompanyId, SelectedCashAccount?.Id);
            StatusMessage = "Kasa ekstresi yüklenirken bir hata oluştu.";
        }
    }
}
