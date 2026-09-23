using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

public sealed record CreditRiskRow(string Code, string Name, decimal Balance, decimal CreditLimit, decimal ExtraCreditLimit, decimal BlockedCreditAmount, decimal AvailableCredit, decimal UsagePercent, string Status, string AccountId = "") : ILedgerLinkRow
{
    /// <summary>Same CreditLimit+Extra-Blocked formula the Cari Kartı Finans tab uses (§25) - not
    /// recomputed differently here.</summary>
    public decimal EffectiveCreditLimit => CreditLimit + ExtraCreditLimit - BlockedCreditAmount;
    string ILedgerLinkRow.SourceType => "";
    string ILedgerLinkRow.SourceId => "";
    string ILedgerLinkRow.CashAccountId => "";
    string ILedgerLinkRow.BankAccountId => "";
}

/// <summary>Backs the standalone Risk & Kredi screen (§45). Row-click detail (§46) is limited to
/// fields the schema actually has (CurrentBalance/CreditLimit/ExtraCredit/BlockedCredit/Available);
/// OpenOrders/OpenInvoices have no backing document type yet, so they are not shown.</summary>
public sealed partial class CreditRiskViewModel : ObservableObject
{
    private readonly LocalAccountService _accounts;
    private readonly string _companyId;
    private readonly ILogger<CreditRiskViewModel> _logger;

    public CreditRiskViewModel(LocalAccountService accounts, string companyId, ILogger<CreditRiskViewModel> logger)
    {
        _accounts = accounts;
        _companyId = companyId;
        _logger = logger;
        Refresh();
    }

    public ObservableCollection<CreditRiskRow> Rows { get; } = [];

    [ObservableProperty] private CreditRiskRow? _selected;
    [ObservableProperty] private string? _statusMessage;

    [RelayCommand]
    public void Refresh()
    {
        StatusMessage = null;
        try
        {
            Rows.Clear();
            foreach (DataRow row in _accounts.CreditRisk(_companyId).Rows)
            {
                Rows.Add(new CreditRiskRow(row["CariKodu"].ToString()!, row["Cari"].ToString()!, Convert.ToDecimal(row["CariBakiye"]),
                    Convert.ToDecimal(row["KrediLimiti"]), Convert.ToDecimal(row["EkLimit"]), Convert.ToDecimal(row["BlokeLimit"]),
                    Convert.ToDecimal(row["KullanilabilirLimit"]), Convert.ToDecimal(row["KullanimYuzdesi"]), row["RiskDurumu"].ToString()!, row["CariId"].ToString()!));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Credit risk load failed. CompanyId={CompanyId}", _companyId);
            StatusMessage = "Risk & kredi verisi yüklenirken bir hata oluştu.";
        }
    }
}
