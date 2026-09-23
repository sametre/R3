using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using R3.Desktop.Presentation;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

public sealed record AuditRow(string Date, string User, string Entity, string Action, string OldValue, string NewValue);

/// <summary>Backs the Cari Kartı "Geçmiş" tab - read-only, reuses audit_logs written by every
/// Local*Service.Save/SetActive already in this feature (no separate audit pipeline).</summary>
public sealed partial class AccountHistoryViewModel : ObservableObject
{
    private readonly LocalAccountService _accounts;
    private readonly string _accountId;
    private readonly ILogger<AccountHistoryViewModel> _logger;

    public AccountHistoryViewModel(LocalAccountService accounts, string accountId, ILogger<AccountHistoryViewModel> logger)
    {
        _accounts = accounts;
        _accountId = accountId;
        _logger = logger;
        Refresh();
    }

    public ObservableCollection<AuditRow> Entries { get; } = [];

    [ObservableProperty] private string? _statusMessage;

    public void Refresh()
    {
        StatusMessage = null;
        try
        {
            Entries.Clear();
            foreach (DataRow row in _accounts.AuditHistory(_accountId).Rows)
                Entries.Add(new AuditRow(row["Tarih"].ToString()!, row["Kullanici"].ToString()!, row["Alan"].ToString()!, InventoryPresentation.AuditActionLabel(row["Islem"].ToString() ?? ""), row["EskiDeger"].ToString()!, row["YeniDeger"].ToString()!));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Account history load failed. AccountId={AccountId}", _accountId);
            StatusMessage = "Geçmiş yüklenirken bir hata oluştu.";
        }
    }
}
