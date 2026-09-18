using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3.Desktop.Logging;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

public sealed record AddressRow(string Id, string Type, string Title, string City, string District, string AddressLine, string ContactName, string Phone, bool IsDefault, bool IsActive)
{
    public string TypeLabel => Type switch
    {
        "HeadOffice" => "Merkez", "Invoice" => "Fatura", "Billing" => "Muhasebe", "Shipping" => "Sevk", "Branch" => "Şube", _ => "Diğer"
    };
    public string StatusLabel => IsActive ? "Aktif" : "Pasif";
}

public sealed partial class AccountAddressListViewModel : ObservableObject
{
    private readonly LocalAccountAddressService _addresses;
    private readonly string _accountId;
    private readonly ILogger<AccountAddressListViewModel> _logger;

    public AccountAddressListViewModel(LocalAccountAddressService addresses, string accountId, ILogger<AccountAddressListViewModel> logger)
    {
        _addresses = addresses;
        _accountId = accountId;
        _logger = logger;
        Refresh();
    }

    public ObservableCollection<AddressRow> Addresses { get; } = [];

    [ObservableProperty] private AddressRow? _selected;
    [ObservableProperty] private string? _statusMessage;

    [RelayCommand]
    private void Refresh()
    {
        StatusMessage = null;
        try
        {
            Addresses.Clear();
            foreach (DataRow row in _addresses.List(_accountId).Rows)
            {
                Addresses.Add(new AddressRow(
                    row["Id"].ToString()!, row["Tip"].ToString()!, row["AdresAdi"].ToString()!, row["Il"].ToString()!,
                    row["Ilce"].ToString()!, row["Adres"].ToString()!, row["Yetkili"].ToString()!, row["Telefon"].ToString()!,
                    Convert.ToBoolean(row["Varsayilan"]), Convert.ToBoolean(row["Aktif"])));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Address list load failed. AccountId={AccountId}", _accountId);
            StatusMessage = "Adresler yüklenirken bir hata oluştu.";
        }
    }

    public AccountAddressEditViewModel CreateEditViewModel(bool asNew) =>
        new(_addresses, _accountId, asNew ? null : Selected, DesktopLogging.CreateLogger<AccountAddressEditViewModel>());

    [RelayCommand]
    private void Deactivate()
    {
        if (Selected == null) { StatusMessage = "Önce bir adres seçin."; return; }
        try { _addresses.SetActive(Selected.Id, false); Refresh(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Address deactivate failed. AddressId={AddressId}", Selected.Id);
            StatusMessage = "Adres pasife alınırken bir hata oluştu.";
        }
    }

    [RelayCommand]
    private void MakeDefault()
    {
        if (Selected == null) { StatusMessage = "Önce bir adres seçin."; return; }
        try { _addresses.SetDefault(Selected.Id); Refresh(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Address set-default failed. AddressId={AddressId}", Selected.Id);
            StatusMessage = "Varsayılan adres güncellenirken bir hata oluştu.";
        }
    }
}

public sealed partial class AccountAddressEditViewModel : ObservableObject
{
    private readonly LocalAccountAddressService _addresses;
    private readonly string _accountId;
    private readonly string _id;
    private readonly ILogger<AccountAddressEditViewModel> _logger;

    public AccountAddressEditViewModel(LocalAccountAddressService addresses, string accountId, AddressRow? existing, ILogger<AccountAddressEditViewModel> logger)
    {
        _addresses = addresses;
        _accountId = accountId;
        _logger = logger;
        _id = existing?.Id ?? "";
        Title = existing == null ? "Yeni Adres" : "Adres Düzenle";
        if (existing != null)
        {
            var full = addresses.Get(existing.Id);
            if (full != null)
            {
                AddressType = full.AddressType; AddressTitle = full.Title; Country = full.Country; City = full.City;
                District = full.District; Neighborhood = full.Neighborhood; AddressLine = full.AddressLine; PostalCode = full.PostalCode;
                ContactName = full.ContactName; Phone = full.Phone; MobilePhone = full.MobilePhone;
                DeliveryRegionCode = full.DeliveryRegionCode; IsDefault = full.IsDefault;
            }
        }
    }

    public string Title { get; }
    public IReadOnlyList<string> AddressTypes { get; } = ["HeadOffice", "Invoice", "Billing", "Shipping", "Branch", "Other"];

    [ObservableProperty] private string _addressType = "Shipping";
    [ObservableProperty] private string _addressTitle = "";
    [ObservableProperty] private string _country = "Türkiye";
    [ObservableProperty] private string _city = "";
    [ObservableProperty] private string _district = "";
    [ObservableProperty] private string _neighborhood = "";
    [ObservableProperty] private string _addressLine = "";
    [ObservableProperty] private string _postalCode = "";
    [ObservableProperty] private string _contactName = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _mobilePhone = "";
    [ObservableProperty] private string _deliveryRegionCode = "";
    [ObservableProperty] private bool _isDefault;
    [ObservableProperty] private string? _errorMessage;

    public event EventHandler? Saved;

    [RelayCommand]
    private void Save()
    {
        try
        {
            _addresses.Save(new AccountAddressEdit(_id, _accountId, AddressType, AddressTitle, Country, City, District,
                Neighborhood, AddressLine, PostalCode, ContactName, Phone, MobilePhone, DeliveryRegionCode, IsDefault));
            ErrorMessage = null;
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Address save failed. AccountId={AccountId}", _accountId);
            ErrorMessage = "Adres kaydedilirken beklenmeyen bir hata oluştu.";
        }
    }
}
