using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

public sealed record InvoiceRow(string Date, string DocumentNo, string Type, string Status, string Currency, decimal Total);
public sealed record LedgerDocumentRow(string Date, string DocumentNo, string Description, decimal Debit, decimal Credit, string Currency);

/// <summary>Backs the Cari Kartı "Belgeler" tab. Faturalar/Tahsilatlar/Ödemeler read real ledger
/// data (sales_documents / account_transactions); Siparişler has no backing document type yet, so
/// it stays an explicit empty state instead of showing invented rows (spec §33).</summary>
public sealed partial class AccountDocumentsViewModel : ObservableObject
{
    private readonly LocalAccountService _accounts;
    private readonly string _companyId;
    private readonly string _accountId;
    private readonly ILogger<AccountDocumentsViewModel> _logger;

    public AccountDocumentsViewModel(LocalAccountService accounts, string companyId, string accountId, ILogger<AccountDocumentsViewModel> logger)
    {
        _accounts = accounts;
        _companyId = companyId;
        _accountId = accountId;
        _logger = logger;
        Refresh();
    }

    public ObservableCollection<InvoiceRow> Invoices { get; } = [];
    public ObservableCollection<LedgerDocumentRow> Receipts { get; } = [];
    public ObservableCollection<LedgerDocumentRow> Payments { get; } = [];

    [ObservableProperty] private string? _statusMessage;

    public bool HasInvoices => Invoices.Count > 0;

    public void Refresh()
    {
        StatusMessage = null;
        try
        {
            Invoices.Clear();
            foreach (DataRow row in _accounts.Invoices(_companyId, _accountId).Rows)
                Invoices.Add(new InvoiceRow(row["Tarih"].ToString()!, row["BelgeNo"].ToString()!, row["Tip"].ToString()!, row["Durum"].ToString()!, row["Doviz"].ToString()!, Convert.ToDecimal(row["Tutar"])));

            Receipts.Clear();
            foreach (DataRow row in _accounts.TransactionsByType(_companyId, _accountId, "Receipt").Rows)
                Receipts.Add(new LedgerDocumentRow(row["Tarih"].ToString()!, row["BelgeNo"].ToString()!, row["Aciklama"].ToString()!, Convert.ToDecimal(row["Borc"]), Convert.ToDecimal(row["Alacak"]), row["Doviz"].ToString()!));

            Payments.Clear();
            foreach (DataRow row in _accounts.TransactionsByType(_companyId, _accountId, "Payment").Rows)
                Payments.Add(new LedgerDocumentRow(row["Tarih"].ToString()!, row["BelgeNo"].ToString()!, row["Aciklama"].ToString()!, Convert.ToDecimal(row["Borc"]), Convert.ToDecimal(row["Alacak"]), row["Doviz"].ToString()!));

            OnPropertyChanged(nameof(HasInvoices));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Account documents load failed. AccountId={AccountId}", _accountId);
            StatusMessage = "Belgeler yüklenirken bir hata oluştu.";
        }
    }
}
