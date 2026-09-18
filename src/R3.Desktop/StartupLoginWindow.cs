using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using R3.Infrastructure;

namespace R3.Desktop;

public sealed record StartupSession(string DatabasePath, Guid CompanyId, string CompanyName, Guid BranchId, string BranchName, string UserName);

public sealed class StartupLoginWindow : Window
{
    private readonly TextBox _databasePath = new();
    private readonly ComboBox _company = new();
    private readonly ComboBox _branch = new();
    private readonly TextBox _username = new() { Text = "admin" };
    private readonly PasswordBox _password = new();
    private readonly TextBlock _status = new();
    private StoreDatabase? _database;
    private bool _loading;

    public StartupSession? Session { get; private set; }

    public StartupLoginWindow()
    {
        Title = "R3 ERP • Oturum Aç";
        Width = 500;
        Height = 600;
        MinWidth = 460;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(244, 247, 251));
        FontFamily = new FontFamily("Segoe UI");
        ShowInTaskbar = true;
        BuildView();
        Loaded += (_, _) => LoadDatabase(_databasePath.Text);
    }

    private void BuildView()
    {
        var root = new Grid { Margin = new Thickness(34) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
        heading.Children.Add(new TextBlock { Text = "R3", Foreground = new SolidColorBrush(Color.FromRgb(53, 120, 184)), FontSize = 32, FontWeight = FontWeights.Bold });
        heading.Children.Add(new TextBlock { Text = "ERP çalışma alanı", Foreground = new SolidColorBrush(Color.FromRgb(81, 103, 125)), FontSize = 16, Margin = new Thickness(2, 0, 0, 0) });
        heading.Children.Add(new TextBlock { Text = "Veritabanı, firma ve şubenizi seçerek giriş yapın.", Foreground = new SolidColorBrush(Color.FromRgb(113, 128, 150)), FontSize = 11, Margin = new Thickness(2, 8, 0, 0) });
        root.Children.Add(heading);

        var form = new StackPanel(); Grid.SetRow(form, 1); root.Children.Add(form);
        AddLabel(form, "Veritabanı");
        var databaseRow = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var browse = new Button { Content = "Seç…", Width = 64, Padding = new Thickness(6, 5, 6, 5), Margin = new Thickness(6, 0, 0, 0) }; DockPanel.SetDock(browse, Dock.Right); databaseRow.Children.Add(browse);
        _databasePath.Text = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "R3", "data", "r3.db");
        _databasePath.Padding = new Thickness(8, 6, 8, 6); databaseRow.Children.Add(_databasePath); form.Children.Add(databaseRow);
        browse.Click += (_, _) => BrowseDatabase();

        AddLabel(form, "Firma");
        _company.DisplayMemberPath = "Name"; _company.SelectedValuePath = "Id"; _company.Height = 32; _company.Margin = new Thickness(0, 0, 0, 14); form.Children.Add(_company);
        _company.SelectionChanged += (_, _) => LoadBranches();

        AddLabel(form, "Şube");
        _branch.DisplayMemberPath = "Name"; _branch.SelectedValuePath = "Id"; _branch.Height = 32; _branch.Margin = new Thickness(0, 0, 0, 14); form.Children.Add(_branch);

        AddLabel(form, "Kullanıcı adı");
        _username.Height = 32; _username.Padding = new Thickness(8, 5, 8, 5); _username.Margin = new Thickness(0, 0, 0, 14); form.Children.Add(_username);

        AddLabel(form, "Şifre");
        _password.Height = 32; _password.Padding = new Thickness(8, 5, 8, 5); _password.Margin = new Thickness(0, 0, 0, 8); form.Children.Add(_password);

        _status.Foreground = new SolidColorBrush(Color.FromRgb(192, 57, 43)); _status.FontSize = 11; _status.TextWrapping = TextWrapping.Wrap; form.Children.Add(_status);

        var footer = new DockPanel { Margin = new Thickness(0, 20, 0, 0) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        var hint = new TextBlock { Text = "İlk kurulum: admin / R3Admin2026!", Foreground = new SolidColorBrush(Color.FromRgb(113, 128, 150)), FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        footer.Children.Add(hint);
        var login = new Button { Content = "Giriş yap", Width = 110, Padding = new Thickness(12, 8, 12, 8), Background = new SolidColorBrush(Color.FromRgb(53, 120, 184)), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold }; DockPanel.SetDock(login, Dock.Right); footer.Children.Add(login);
        login.Click += (_, _) => Login();
        Content = root;
    }

    private static void AddLabel(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(81, 103, 125)), FontSize = 11, Margin = new Thickness(2, 0, 0, 5) });

    private void BrowseDatabase()
    {
        var dialog = new OpenFileDialog { Filter = "R3 SQLite veritabanı|*.db;*.sqlite;*.sqlite3|Tüm dosyalar|*.*", CheckFileExists = false, FileName = "r3.db" };
        if (dialog.ShowDialog(this) == true) { _databasePath.Text = dialog.FileName; LoadDatabase(dialog.FileName); }
    }

    private void LoadDatabase(string path)
    {
        try
        {
            _loading = true; _database = new StoreDatabase(path); _company.ItemsSource = _database.Query("SELECT id AS Id, code AS Code, name AS Name FROM companies WHERE is_active=1 ORDER BY code").DefaultView; _company.SelectedIndex = _company.Items.Count > 0 ? 0 : -1; _status.Text = "";
        }
        catch (Exception ex) { _company.ItemsSource = null; _branch.ItemsSource = null; _status.Text = $"Veritabanı açılamadı: {ex.Message}"; }
        finally { _loading = false; LoadBranches(); }
    }

    private void LoadBranches()
    {
        if (_loading || _database == null) return;
        _loading = true;
        try
        {
            _branch.ItemsSource = _company.SelectedValue is string company ? _database.Query("SELECT id AS Id, code AS Code, name AS Name FROM branches WHERE company_id=$id AND is_active=1 ORDER BY code", ("$id", company)).DefaultView : null;
            _branch.SelectedIndex = _branch.Items.Count > 0 ? 0 : -1;
        }
        finally { _loading = false; }
    }

    private void Login()
    {
        if (_database == null) { _status.Text = "Önce geçerli bir veritabanı seçin."; return; }
        if (_company.SelectedItem is not DataRowView company || _branch.SelectedItem is not DataRowView branch) { _status.Text = "Firma ve şube seçimi zorunludur."; return; }
        var displayName = _database.Authenticate(_username.Text, _password.Password);
        if (displayName == null) { _status.Text = "Kullanıcı adı veya şifre hatalı."; return; }
        if (!Guid.TryParse(company["Id"].ToString(), out var companyId) || !Guid.TryParse(branch["Id"].ToString(), out var branchId)) { _status.Text = "Firma veya şube kaydı geçersiz."; return; }
        Session = new StartupSession(_database.Path, companyId, company["Name"].ToString()!, branchId, branch["Name"].ToString()!, displayName);
        DialogResult = true;
    }
}
