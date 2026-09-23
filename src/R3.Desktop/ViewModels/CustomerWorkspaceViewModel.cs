using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3.Desktop.Presentation;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

public sealed record CustomerChoice(string Id, string Code, string Name)
{
    public string Display => $"{Code} — {Name}";
}

public sealed partial class CustomerWorkspaceViewModel(
    LocalCustomerWorkspaceService service, string companyId, ILogger<CustomerWorkspaceViewModel> logger) : ObservableObject
{
    private int _version;
    private bool _initialized;
    public ObservableCollection<CustomerChoice> Customers { get; } = [];
    public string[] Currencies { get; } = ["TRY", "USD", "EUR", "GBP"];
    [ObservableProperty] private CustomerChoice? _selectedCustomer;
    [ObservableProperty] private string _currency = "TRY";
    [ObservableProperty] private CustomerWorkspaceData? _data;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Müşteri kodu veya adıyla seçim yapın. F4: müşteri seçimi • F5: yenile";

    public bool CanOperate => SelectedCustomer != null && Data != null && !IsBusy;
    public DataView? Statement => Data?.Statement.DefaultView;
    public DataView? Notes => Data?.Notes.DefaultView;
    public DataView? PendingProducts => Data?.PendingProducts.DefaultView;
    public DataView? DeliveredProducts => Data?.DeliveredProducts.DefaultView;
    public DataView? Installments => Data == null ? null : new DataView(Data.Installments) { RowFilter = "Kalan > 0" };
    public DataView? CollectionPerformance => Data?.Installments.DefaultView;
    public DataView? Cheques => Data?.Cheques.DefaultView;
    public DataView? History => Data?.History.DefaultView;
    public string Phone => Data?.Customer.Account.MobilePhone is { Length: > 0 } mobile ? mobile : Data?.Customer.Account.Phone ?? "";
    public string IdentityNumber => Data?.Customer.Account.IdentityNumber ?? "";
    public string Email => Data?.Customer.Account.Email ?? "";
    public string CreditCurrency => Data?.Customer.Account.DefaultCurrencyCode ?? "TRY";

    partial void OnSelectedCustomerChanged(CustomerChoice? value) => _ = RefreshAsync();
    partial void OnCurrencyChanged(string value) => _ = RefreshAsync();
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanOperate));
    partial void OnDataChanged(CustomerWorkspaceData? value)
    {
        foreach (var property in new[] { nameof(CanOperate), nameof(Statement), nameof(Notes), nameof(PendingProducts),
            nameof(DeliveredProducts), nameof(Installments), nameof(CollectionPerformance), nameof(Cheques), nameof(History),
            nameof(Phone), nameof(IdentityNumber), nameof(Email), nameof(CreditCurrency) }) OnPropertyChanged(property);
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await ReloadCustomersAsync();
    }

    public async Task ReloadCustomersAsync(string? selectId = null)
    {
        try
        {
            var rows = await Task.Run(() => service.Customers(companyId));
            var selectedId = selectId ?? SelectedCustomer?.Id;
            Customers.Clear();
            foreach (DataRow row in rows.Rows)
                Customers.Add(new(row["Id"].ToString()!, row["Code"].ToString()!, row["Name"].ToString()!));
            SelectedCustomer = Customers.FirstOrDefault(x => x.Id == selectedId);
            if (Customers.Count == 0) Status = "Aktif müşteri bulunamadı. Yeni Müşteri ile müşteri kartı oluşturabilirsiniz.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Customer lookup failed for {CompanyId}", companyId);
            Status = "Müşteri listesi yüklenemedi. Yenile ile tekrar deneyin.";
            _initialized = false;
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var version = ++_version;
        var customer = SelectedCustomer;
        var currency = Currency?.Trim().ToUpperInvariant() ?? "";
        Data = null;
        if (customer == null) { IsBusy = false; Status = "Müşteri kodu veya adıyla seçim yapın."; return; }
        if (currency.Length != 3) { IsBusy = false; Status = "Üç harfli bir döviz kodu seçin (ör. TRY)."; return; }
        IsBusy = true;
        Status = "Müşteri cari bilgileri yükleniyor…";
        try
        {
            var result = await Task.Run(() => service.Load(companyId, customer.Id, currency, DateTime.Today));
            if (version != _version) return;
            foreach (DataRow row in result.Statement.Rows)
                row["Islem"] = row["Islem"].ToString() switch
                {
                    "ManualEntry" => "Manuel Cari Fişi", "SalesInvoice" => "Satış Faturası",
                    "SalesReturn" => "Satış İadesi", "CustomerReceipt" => "Müşteri Tahsilatı",
                    "Cancellation" => "İptal / Ters Kayıt",
                    var type => InventoryPresentation.TransactionTypeLabel(type ?? "")
                };
            Data = result;
            Status = result.Statement.Rows.Count == 0 ? $"{currency} için cari hareket bulunamadı."
                : $"{result.Statement.Rows.Count:N0} hareket • {customer.Code} • {currency}";
        }
        catch (Exception ex)
        {
            if (version != _version) return;
            logger.LogError(ex, "Customer workspace failed. Company={CompanyId} Account={AccountId}", companyId, customer.Id);
            Status = "Müşteri bilgileri yüklenemedi. Yenile ile tekrar deneyin.";
        }
        finally { if (version == _version) IsBusy = false; }
    }
}
