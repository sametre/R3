using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        Width = 920;
        Height = 520;
        MinWidth = 920;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(231, 233, 235));
        FontFamily = new FontFamily("Segoe UI");
        ShowInTaskbar = true;
        BuildView();
        Loaded += (_, _) => LoadDatabase(_databasePath.Text);
    }

    private void BuildView()
    {
        var dark = new SolidColorBrush(Color.FromRgb(62, 66, 70));
        var medium = new SolidColorBrush(Color.FromRgb(103, 109, 115));
        var border = new SolidColorBrush(Color.FromRgb(199, 203, 207));
        var root = new Border { Margin = new Thickness(22), Background = new SolidColorBrush(Color.FromRgb(245, 246, 247)), BorderBrush = border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10) };
        var layout = new Grid(); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) }); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); root.Child = layout;

        var identity = new Border { Background = dark, CornerRadius = new CornerRadius(9, 0, 0, 9), Padding = new Thickness(34) };
        var identityBody = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        identityBody.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/R3-matte.png")), Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Left, Opacity = .92 });
        identityBody.Children.Add(new TextBlock { Text = "R3 ERP", Foreground = Brushes.White, FontSize = 30, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 2) });
        identityBody.Children.Add(new TextBlock { Text = "İşletme Yönetim Platformu", Foreground = new SolidColorBrush(Color.FromRgb(205, 209, 212)), FontSize = 13 });
        identityBody.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(91, 96, 101)), Margin = new Thickness(0, 25, 0, 22) });
        identityBody.Children.Add(new TextBlock { Text = "• Firma ve şube bazlı çalışma\n• Yerel SQLite veri güvenliği\n• Satış, stok ve cari yönetimi", Foreground = new SolidColorBrush(Color.FromRgb(218, 221, 224)), FontSize = 11, LineHeight = 24 });
        identity.Child = identityBody; layout.Children.Add(identity);

        var formPanel = new Grid { Margin = new Thickness(36, 28, 36, 24) };
        formPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        formPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        formPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(formPanel, 1); layout.Children.Add(formPanel);
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        heading.Children.Add(new TextBlock { Text = "Oturum aç", Foreground = dark, FontSize = 23, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Çalışma alanı bilgilerinizi kontrol edip devam edin.", Foreground = medium, FontSize = 11, Margin = new Thickness(0, 5, 0, 0) });
        formPanel.Children.Add(heading);

        var form = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetRow(form, 1); formPanel.Children.Add(form);
        AddLabel(form, "Veritabanı dosyası");
        var databaseRow = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var browse = SmallButton("Gözat", false); browse.Width = 72; browse.Margin = new Thickness(7, 0, 0, 0); DockPanel.SetDock(browse, Dock.Right); databaseRow.Children.Add(browse);
        _databasePath.Text = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "R3", "data", "r3.db");
        PrepareInput(_databasePath); databaseRow.Children.Add(_databasePath); form.Children.Add(databaseRow); browse.Click += (_, _) => BrowseDatabase();

        var organization = TwoColumnRow(); form.Children.Add(organization.Container);
        AddLabel(organization.Left, "Firma"); _company.DisplayMemberPath = "Name"; _company.SelectedValuePath = "Id"; PrepareCombo(_company); organization.Left.Children.Add(_company); _company.SelectionChanged += (_, _) => LoadBranches();
        AddLabel(organization.Right, "Şube"); _branch.DisplayMemberPath = "Name"; _branch.SelectedValuePath = "Id"; PrepareCombo(_branch); organization.Right.Children.Add(_branch);

        var credentials = TwoColumnRow(); credentials.Container.Margin = new Thickness(0, 14, 0, 0); form.Children.Add(credentials.Container);
        AddLabel(credentials.Left, "Kullanıcı adı"); PrepareInput(_username); credentials.Left.Children.Add(_username);
        AddLabel(credentials.Right, "Şifre"); _password.Height = 30; _password.Padding = new Thickness(8, 4, 8, 4); _password.Background = Brushes.White; _password.BorderBrush = border; _password.BorderThickness = new Thickness(1); credentials.Right.Children.Add(_password);

        _status.Foreground = new SolidColorBrush(Color.FromRgb(166, 60, 55)); _status.FontSize = 10.5; _status.TextWrapping = TextWrapping.Wrap; _status.Margin = new Thickness(0, 12, 0, 0); form.Children.Add(_status);

        var footer = new Grid { Margin = new Thickness(0, 18, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); Grid.SetRow(footer, 2); formPanel.Children.Add(footer);
        footer.Children.Add(new TextBlock { Text = "İlk kurulum: admin / R3Admin2026!", Foreground = medium, FontSize = 9.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 14, 0) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(buttons, 1); footer.Children.Add(buttons);
        var cancel = SmallButton("Kapat", false); cancel.Margin = new Thickness(0, 0, 7, 0); cancel.IsCancel = true; cancel.Click += (_, _) => Close(); buttons.Children.Add(cancel);
        var login = SmallButton("Giriş yap", true); login.IsDefault = true; login.Click += (_, _) => Login(); buttons.Children.Add(login);
        Content = root;
    }

    private static Button SmallButton(string text, bool primary) => new()
    {
        Content = text, Width = 86, Height = 30, Padding = new Thickness(10, 3, 10, 3),
        Background = new SolidColorBrush(primary ? Color.FromRgb(73, 78, 83) : Color.FromRgb(229, 231, 233)),
        Foreground = primary ? Brushes.White : new SolidColorBrush(Color.FromRgb(63, 67, 71)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(186, 190, 194)), BorderThickness = primary ? new Thickness(0) : new Thickness(1),
        FontSize = 10.5, FontWeight = FontWeights.SemiBold
    };

    private static void PrepareInput(TextBox input) { input.Height = 30; input.Padding = new Thickness(8, 4, 8, 4); input.Background = Brushes.White; input.BorderBrush = new SolidColorBrush(Color.FromRgb(199, 203, 207)); input.BorderThickness = new Thickness(1); }
    private static void PrepareCombo(ComboBox input) { input.Height = 30; input.Background = Brushes.White; input.BorderBrush = new SolidColorBrush(Color.FromRgb(199, 203, 207)); input.BorderThickness = new Thickness(1); }
    private static (Grid Container, StackPanel Left, StackPanel Right) TwoColumnRow()
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel { Margin = new Thickness(0, 0, 7, 0) }; var right = new StackPanel { Margin = new Thickness(7, 0, 0, 0) }; Grid.SetColumn(right, 1); grid.Children.Add(left); grid.Children.Add(right); return (grid, left, right);
    }

    private static void AddLabel(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(92, 97, 102)), FontSize = 10.5, Margin = new Thickness(1, 0, 0, 5) });

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
