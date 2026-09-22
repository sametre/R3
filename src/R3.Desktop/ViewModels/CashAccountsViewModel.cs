using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3.Desktop.Logging;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

public sealed class CashAccountRowViewModel(string id, string code, string name, string branch, string cashAccountType, string currencyCode,
    decimal totalIn, decimal totalOut, decimal balance, bool isActive, bool allowNegativeBalance)
{
    public string Id { get; } = id;
    public string Code { get; } = code;
    public string Name { get; } = name;
    public string Branch { get; } = branch;
    public string CashAccountType { get; } = cashAccountType;
    public string CashAccountTypeLabel => CashAccountType switch
    {
        "MainCash" => "Ana Kasa", "BranchCash" => "Şube Kasası", "StoreCash" => "Mağaza Kasası", "POSCash" => "POS Kasası",
        "ForeignCurrencyCash" => "Döviz Kasası", "PettyCash" => "Masraf Kasası", _ => "Diğer"
    };
    public string CurrencyCode { get; } = currencyCode;
    public decimal TotalIn { get; } = totalIn;
    public decimal TotalOut { get; } = totalOut;
    public decimal Balance { get; } = balance;
    public bool IsActive { get; } = isActive;
    public string StatusLabel => IsActive ? "Aktif" : "Pasif";
    public bool AllowNegativeBalance { get; } = allowNegativeBalance;
}

/// <summary>MVVM screen for Kasa Kartları (§15-19). Owns UI state only; every read/write goes
/// through <see cref="LocalCashService"/> - mirrors AccountsViewModel's shape.</summary>
public sealed partial class CashAccountsViewModel : ObservableObject
{
    private readonly CashServices _services;
    private readonly ILogger<CashAccountsViewModel> _logger;
    private CancellationTokenSource? _searchDebounce;

    public CashAccountsViewModel(CashServices services, ILogger<CashAccountsViewModel> logger)
    {
        _services = services;
        _logger = logger;
    }

    public string Title => "Kasa Kartları";
    public string Subtitle => "Nakit kasa hesaplarının merkezi görünümü";
    public StoreDatabase Database => _services.Database;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private CashAccountRowViewModel? _selectedCashAccount;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;

    public ObservableCollection<CashAccountRowViewModel> CashAccounts { get; } = [];

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusMessage = null;
        try
        {
            var search = SearchText;
            var table = await Task.Run(() => _services.Cash.Search(_services.CompanyId, search: search));
            CashAccounts.Clear();
            foreach (DataRow row in table.Rows)
            {
                CashAccounts.Add(new CashAccountRowViewModel(
                    row["Id"].ToString()!, row["Kod"].ToString()!, row["Ad"].ToString()!, row["Sube"].ToString()!, row["Tip"].ToString()!,
                    row["ParaBirimi"].ToString()!, Convert.ToDecimal(row["Giris"]), Convert.ToDecimal(row["Cikis"]), Convert.ToDecimal(row["Bakiye"]),
                    Convert.ToBoolean(row["Aktif"]), Convert.ToBoolean(row["NegatifIzinli"])));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cash account list load failed. CompanyId={CompanyId}", _services.CompanyId);
            StatusMessage = "Kasa listesi yüklenirken bir hata oluştu.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public CashAccountEditViewModel CreateEditViewModel(bool asNew) =>
        new(_services, asNew ? null : SelectedCashAccount?.Id);

    public CashInOutViewModel CreateCashInViewModel() =>
        new(_services, CashDirectionKind.In, SelectedCashAccount?.Id, DesktopLogging.CreateLogger<CashInOutViewModel>());

    public CashInOutViewModel CreateCashOutViewModel() =>
        new(_services, CashDirectionKind.Out, SelectedCashAccount?.Id, DesktopLogging.CreateLogger<CashInOutViewModel>());

    public CashTransferViewModel CreateTransferViewModel() =>
        new(_services, SelectedCashAccount?.Id, DesktopLogging.CreateLogger<CashTransferViewModel>());

    public async Task SetSelectedActiveAsync(bool isActive)
    {
        if (SelectedCashAccount == null) return;
        var id = SelectedCashAccount.Id;
        try
        {
            await Task.Run(() => _services.Cash.SetActive(_services.CompanyId, id, isActive, _services.UserName));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cash account status update failed. CompanyId={CompanyId} CashAccountId={CashAccountId}", _services.CompanyId, id);
            StatusMessage = "Kasa durumu güncellenemedi.";
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce?.Cancel(); _searchDebounce?.Dispose(); _searchDebounce = new CancellationTokenSource();
        _ = DebouncedSearchAsync(_searchDebounce.Token);
    }
    private async Task DebouncedSearchAsync(CancellationToken token)
    {
        try { await Task.Delay(240, token); await RefreshAsync(); }
        catch (OperationCanceledException) { }
    }
}
