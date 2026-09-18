using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3.Desktop.Logging;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

public sealed record ContactRow(string Id, string FullName, string Title, string Department, string Phone, string MobilePhone, string Email, bool IsPrimary, bool IsActive)
{
    public string StatusLabel => IsActive ? "Aktif" : "Pasif";
}

public sealed partial class AccountContactListViewModel : ObservableObject
{
    private readonly LocalAccountContactService _contacts;
    private readonly string _accountId;
    private readonly ILogger<AccountContactListViewModel> _logger;

    public AccountContactListViewModel(LocalAccountContactService contacts, string accountId, ILogger<AccountContactListViewModel> logger)
    {
        _contacts = contacts;
        _accountId = accountId;
        _logger = logger;
        Refresh();
    }

    public ObservableCollection<ContactRow> Contacts { get; } = [];

    [ObservableProperty] private ContactRow? _selected;
    [ObservableProperty] private string? _statusMessage;

    [RelayCommand]
    private void Refresh()
    {
        StatusMessage = null;
        try
        {
            Contacts.Clear();
            foreach (DataRow row in _contacts.List(_accountId).Rows)
            {
                Contacts.Add(new ContactRow(
                    row["Id"].ToString()!, row["AdSoyad"].ToString()!, row["Gorev"].ToString()!, row["Departman"].ToString()!,
                    row["Telefon"].ToString()!, row["Cep"].ToString()!, row["Eposta"].ToString()!,
                    Convert.ToBoolean(row["AnaYetkili"]), Convert.ToBoolean(row["Aktif"])));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Contact list load failed. AccountId={AccountId}", _accountId);
            StatusMessage = "Yetkililer yüklenirken bir hata oluştu.";
        }
    }

    public AccountContactEditViewModel CreateEditViewModel(bool asNew) =>
        new(_contacts, _accountId, asNew ? null : Selected, DesktopLogging.CreateLogger<AccountContactEditViewModel>());

    [RelayCommand]
    private void Deactivate()
    {
        if (Selected == null) { StatusMessage = "Önce bir yetkili seçin."; return; }
        try { _contacts.SetActive(Selected.Id, false); Refresh(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Contact deactivate failed. ContactId={ContactId}", Selected.Id);
            StatusMessage = "Yetkili pasife alınırken bir hata oluştu.";
        }
    }

    [RelayCommand]
    private void MakePrimary()
    {
        if (Selected == null) { StatusMessage = "Önce bir yetkili seçin."; return; }
        try { _contacts.SetPrimary(Selected.Id); Refresh(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Contact set-primary failed. ContactId={ContactId}", Selected.Id);
            StatusMessage = "Ana yetkili güncellenirken bir hata oluştu.";
        }
    }
}

public sealed partial class AccountContactEditViewModel : ObservableObject
{
    private readonly LocalAccountContactService _contacts;
    private readonly string _accountId;
    private readonly string _id;
    private readonly ILogger<AccountContactEditViewModel> _logger;

    public AccountContactEditViewModel(LocalAccountContactService contacts, string accountId, ContactRow? existing, ILogger<AccountContactEditViewModel> logger)
    {
        _contacts = contacts;
        _accountId = accountId;
        _logger = logger;
        _id = existing?.Id ?? "";
        Title = existing == null ? "Yeni Yetkili" : "Yetkili Düzenle";
        if (existing != null)
        {
            var full = contacts.Get(existing.Id);
            if (full != null)
            {
                FirstName = full.FirstName; LastName = full.LastName; JobTitle = full.Title; Department = full.Department;
                Phone = full.Phone; MobilePhone = full.MobilePhone; Email = full.Email; IsPrimary = full.IsPrimary; Notes = full.Notes;
            }
        }
    }

    public string Title { get; }

    [ObservableProperty] private string _firstName = "";
    [ObservableProperty] private string _lastName = "";
    [ObservableProperty] private string _jobTitle = "";
    [ObservableProperty] private string _department = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _mobilePhone = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private bool _isPrimary;
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string? _errorMessage;

    public event EventHandler? Saved;

    [RelayCommand]
    private void Save()
    {
        try
        {
            _contacts.Save(new AccountContactEdit(_id, _accountId, FirstName, LastName, JobTitle, Department, Phone, MobilePhone, Email, IsPrimary, Notes: Notes));
            ErrorMessage = null;
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Contact save failed. AccountId={AccountId}", _accountId);
            ErrorMessage = "Yetkili kaydedilirken beklenmeyen bir hata oluştu.";
        }
    }
}
