using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3.Desktop.Logging;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

public sealed record NoteRow(string Id, string NoteType, string Title, string Content, string CreatedBy, string CreatedAt, bool IsPinned);

public sealed partial class AccountNoteListViewModel : ObservableObject
{
    private readonly LocalAccountNoteService _notes;
    private readonly string _accountId;
    private readonly string _userName;
    private readonly ILogger<AccountNoteListViewModel> _logger;

    public AccountNoteListViewModel(LocalAccountNoteService notes, string accountId, string userName, ILogger<AccountNoteListViewModel> logger)
    {
        _notes = notes;
        _accountId = accountId;
        _userName = userName;
        _logger = logger;
        Refresh();
    }

    public ObservableCollection<NoteRow> Notes { get; } = [];

    [ObservableProperty] private NoteRow? _selected;
    [ObservableProperty] private string? _statusMessage;

    [RelayCommand]
    private void Refresh()
    {
        StatusMessage = null;
        try
        {
            Notes.Clear();
            foreach (DataRow row in _notes.List(_accountId).Rows)
            {
                Notes.Add(new NoteRow(row["Id"].ToString()!, row["Tip"].ToString()!, row["Baslik"].ToString()!, row["Icerik"].ToString()!,
                    row["Kullanici"].ToString()!, row["Tarih"].ToString()!, Convert.ToBoolean(row["Sabit"])));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Note list load failed. AccountId={AccountId}", _accountId);
            StatusMessage = "Notlar yüklenirken bir hata oluştu.";
        }
    }

    public AccountNoteEditViewModel CreateEditViewModel() => new(_notes, _accountId, _userName, DesktopLogging.CreateLogger<AccountNoteEditViewModel>());

    [RelayCommand]
    private void TogglePinned()
    {
        if (Selected == null) { StatusMessage = "Önce bir not seçin."; return; }
        try { _notes.TogglePinned(Selected.Id, !Selected.IsPinned); Refresh(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Note pin toggle failed. NoteId={NoteId}", Selected.Id);
            StatusMessage = "Not güncellenirken bir hata oluştu.";
        }
    }
}

public sealed partial class AccountNoteEditViewModel : ObservableObject
{
    private readonly LocalAccountNoteService _notes;
    private readonly string _accountId;
    private readonly string _userName;
    private readonly ILogger<AccountNoteEditViewModel> _logger;

    public AccountNoteEditViewModel(LocalAccountNoteService notes, string accountId, string userName, ILogger<AccountNoteEditViewModel> logger)
    {
        _notes = notes;
        _accountId = accountId;
        _userName = userName;
        _logger = logger;
    }

    public IReadOnlyList<string> NoteTypes { get; } = ["Genel", "Satış", "Finans", "Tahsilat", "Risk", "Şikayet", "Diğer"];

    [ObservableProperty] private string _noteType = "Genel";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private string? _errorMessage;

    public event EventHandler? Saved;

    [RelayCommand]
    private void Save()
    {
        try
        {
            _notes.Save(new AccountNoteEdit("", _accountId, NoteType, Title, Content, IsPinned, _userName));
            ErrorMessage = null;
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Note save failed. AccountId={AccountId}", _accountId);
            ErrorMessage = "Not kaydedilirken beklenmeyen bir hata oluştu.";
        }
    }
}
