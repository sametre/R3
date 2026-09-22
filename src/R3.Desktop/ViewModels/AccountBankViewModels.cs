using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

public sealed record AccountBankRow(string Id, string BankName, string BranchName, string BranchCode, string AccountName, string Iban, string CurrencyCode, bool IsDefault, bool IsActive)
{
    public string StatusLabel => IsActive ? (IsDefault ? "Varsayılan" : "Aktif") : "Pasif";
}

public sealed partial class AccountBankListViewModel : ObservableObject
{
    private readonly LocalAccountBankService _banks;
    private readonly string _accountId;
    private readonly ILogger<AccountBankListViewModel> _logger;
    public AccountBankListViewModel(LocalAccountBankService banks, string accountId, ILogger<AccountBankListViewModel> logger) { _banks = banks; _accountId = accountId; _logger = logger; Refresh(); }
    public ObservableCollection<AccountBankRow> Banks { get; } = [];
    [ObservableProperty] private AccountBankRow? _selected;
    [ObservableProperty] private string? _statusMessage;
    public bool HasBanks => Banks.Count > 0;

    [RelayCommand]
    private void Refresh()
    {
        try
        {
            Banks.Clear();
            foreach (DataRow row in _banks.List(_accountId).Rows)
                Banks.Add(new AccountBankRow(row["Id"].ToString()!, row["Banka"].ToString()!, row["Sube"].ToString()!, row["SubeKodu"].ToString()!, row["HesapAdi"].ToString()!, row["IBAN"].ToString()!, row["Doviz"].ToString()!, Convert.ToBoolean(row["Varsayilan"]), Convert.ToBoolean(row["Aktif"])));
            OnPropertyChanged(nameof(HasBanks)); StatusMessage = null;
        }
        catch (Exception ex) { _logger.LogError(ex, "Bank list load failed. AccountId={AccountId}", _accountId); StatusMessage = "Banka hesapları yüklenirken hata oluştu."; }
    }

    public AccountBankEditViewModel CreateEditViewModel(bool asNew) => new(_banks, _accountId, asNew ? null : Selected);

    [RelayCommand]
    private void Deactivate() { if (Selected == null) { StatusMessage = "Önce bir banka hesabı seçin."; return; } try { _banks.SetActive(Selected.Id, false); Refresh(); } catch (Exception ex) { _logger.LogError(ex, "Bank deactivate failed"); StatusMessage = "Banka hesabı pasife alınamadı."; } }
}

public sealed partial class AccountBankEditViewModel : ObservableObject
{
    private readonly LocalAccountBankService _banks; private readonly string _accountId; private readonly string _id;
    public AccountBankEditViewModel(LocalAccountBankService banks, string accountId, AccountBankRow? existing) { _banks = banks; _accountId = accountId; _id = existing?.Id ?? ""; if (existing != null) { var x = banks.Get(existing.Id); if (x != null) { BankName=x.BankName; BranchName=x.BranchName; BranchCode=x.BranchCode; AccountName=x.AccountName; Iban=x.Iban; AccountNumber=x.AccountNumber; CurrencyCode=x.CurrencyCode; IsDefault=x.IsDefault; IsActive=x.IsActive; Notes=x.Notes; } } }
    [ObservableProperty] private string _bankName=""; [ObservableProperty] private string _branchName=""; [ObservableProperty] private string _branchCode=""; [ObservableProperty] private string _accountName=""; [ObservableProperty] private string _iban=""; [ObservableProperty] private string _accountNumber=""; [ObservableProperty] private string _currencyCode="TRY"; [ObservableProperty] private bool _isDefault; [ObservableProperty] private bool _isActive=true; [ObservableProperty] private string _notes=""; [ObservableProperty] private string? _errorMessage;
    public event EventHandler? Saved;
    [RelayCommand] private void Save() { try { _banks.Save(new AccountBankEdit(_id,_accountId,BankName,BranchName,BranchCode,AccountName,Iban,AccountNumber,CurrencyCode,IsDefault,IsActive,Notes)); ErrorMessage=null; Saved?.Invoke(this,EventArgs.Empty); } catch (ArgumentException ex) { ErrorMessage=ex.Message; } }
}
