using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using R3.Desktop.Controls;
using R3.Desktop.Logging;
using R3.Infrastructure;
using Microsoft.Extensions.Logging;

namespace R3.Desktop;

public sealed record StartupSession(string DatabasePath, Guid CompanyId, string CompanyName, Guid BranchId, string BranchName, string UserName, string RoleCode, string RoleName, string LoginName = "")
{
    // UserName is the display name (shown in the status bar/audit text); LoginName is users.username,
    // which is what permission lookups must key on. Older call sites only knew UserName.
    public string PermissionUserName => string.IsNullOrWhiteSpace(LoginName) ? UserName : LoginName;
}

public sealed class StartupLoginWindow : Window
{
    private readonly TextBox _databasePath = new();
    private readonly ComboBox _company = new();
    private readonly ComboBox _branch = new();
    private readonly TextBox _username = new() { Text = "admin" };
    private readonly PasswordBox _password = new();
    private readonly CheckBox _rememberMe = new() { Content = "Beni hatırla ve otomatik giriş yap", IsChecked = true };
    private readonly TextBlock _status = new();
    private readonly TextBlock _databaseName = new();
    private readonly TextBlock _roleInfo = new();
    private StoreDatabase? _database;
    private RememberedLogin? _remembered;
    private bool _loading;
    private bool _autoLoginAttempted;
    private readonly ILogger<StartupLoginWindow> _logger = DesktopLogging.CreateLogger<StartupLoginWindow>();

    public StartupSession? Session { get; private set; }

    public StartupLoginWindow()
    {
        Title = "AR3 ERP • Oturum Aç";
        Width = 760;
        Height = 430;
        MinWidth = 760;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        Background = new SolidColorBrush(Color.FromRgb(236, 238, 240));
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        ShowInTaskbar = true;
        BuildView();
        _remembered = LoginCredentialStore.Load();
        if (_remembered != null)
        {
            if (!string.IsNullOrWhiteSpace(_remembered.DatabasePath) && System.IO.File.Exists(_remembered.DatabasePath)) _databasePath.Text = _remembered.DatabasePath;
            _username.Text = _remembered.UserName;
            _password.Password = _remembered.Password;
        }
        else
        {
            // Yerel prototip ilk kurulum hesabı; ilk başarılı girişten sonra DPAPI ile korunarak saklanır.
            _password.Password = "R3Admin2026!";
        }
        Loaded += (_, _) =>
        {
            if (!LoadDatabase(_databasePath.Text) && !string.Equals(_databasePath.Text, DefaultDatabasePath(), StringComparison.OrdinalIgnoreCase))
            {
                _databasePath.Text = DefaultDatabasePath();
                LoadDatabase(_databasePath.Text);
            }
            Dispatcher.BeginInvoke(() =>
            {
                var showLogin = Environment.CommandLine.Contains("--show-login", StringComparison.OrdinalIgnoreCase);
                if (!showLogin && _database != null && _remembered != null && _rememberMe.IsChecked == true && !_autoLoginAttempted)
                {
                    _autoLoginAttempted = true;
                    _status.Text = "● Otomatik giriş yapılıyor…";
                    Login();
                }
                else _password.Focus();
            });
        };
    }

    private void BuildView()
    {
        var dark = new SolidColorBrush(Color.FromRgb(36, 42, 47));
        var medium = new SolidColorBrush(Color.FromRgb(98, 108, 115));
        var border = new SolidColorBrush(Color.FromRgb(211, 215, 219));
        var root = new Border
        {
            Width = 700,
            Height = 370,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.White,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = .12, Color = Colors.Black }
        };
        var layout = new Grid(); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(225) }); layout.ColumnDefinitions.Add(new ColumnDefinition()); root.Child = layout;

        var identity = new Border { Background = new SolidColorBrush(Color.FromRgb(244, 245, 246)), CornerRadius = new CornerRadius(9, 0, 0, 9), BorderBrush = border, BorderThickness = new Thickness(0, 0, 1, 0) };
        var identityBody = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        identityBody.Children.Add(new R3BrandMark
        {
            Width = 112, Height = 78,
            MarkBrush = dark,
            OrbitBrush = new SolidColorBrush(Color.FromRgb(66, 142, 148)),
            AccentBrush = new SolidColorBrush(Color.FromRgb(196, 139, 52))
        });
        identityBody.Children.Add(new TextBlock { Text = "AR3 ERP", Foreground = dark, FontSize = 27, FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 17, 0, 0) });
        identity.Child = identityBody; layout.Children.Add(identity);

        var formPanel = new Grid { Margin = new Thickness(34, 26, 34, 24) };
        formPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); formPanel.RowDefinitions.Add(new RowDefinition()); formPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(formPanel, 1); layout.Children.Add(formPanel);
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        heading.Children.Add(new TextBlock { Text = "Oturum Aç", Foreground = dark, FontSize = 21, FontWeight = FontWeights.SemiBold });
        _roleInfo.Text = "Rol: giriş sonrası belirlenecek";
        _roleInfo.Foreground = medium;
        _roleInfo.FontSize = 10;
        _roleInfo.Margin = new Thickness(0, 3, 0, 0);
        heading.Children.Add(_roleInfo);
        formPanel.Children.Add(heading);

        var form = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetRow(form, 1); formPanel.Children.Add(form);
        _databasePath.Text = DefaultDatabasePath();
        _databasePath.Visibility = Visibility.Collapsed; form.Children.Add(_databasePath);

        var organization = TwoColumnRow(); form.Children.Add(organization.Container);
        AddLabel(organization.Left, "Firma"); _company.DisplayMemberPath = "Name"; _company.SelectedValuePath = "Id"; PrepareCombo(_company); organization.Left.Children.Add(_company); _company.SelectionChanged += (_, _) => LoadBranches();
        AddLabel(organization.Right, "Şube"); _branch.DisplayMemberPath = "Name"; _branch.SelectedValuePath = "Id"; PrepareCombo(_branch); organization.Right.Children.Add(_branch);

        var credentials = TwoColumnRow(); credentials.Container.Margin = new Thickness(0, 13, 0, 0); form.Children.Add(credentials.Container);
        AddLabel(credentials.Left, "Kullanıcı kodu"); PrepareInput(_username); credentials.Left.Children.Add(_username);
        _username.LostFocus += (_, _) => SelectUserDefaultBranch();
        AddLabel(credentials.Right, "Şifre"); _password.Height = 31; _password.Padding = new Thickness(9, 4, 9, 4); _password.Background = Brushes.White; _password.BorderBrush = border; _password.BorderThickness = new Thickness(1); credentials.Right.Children.Add(_password);

        _rememberMe.Margin = new Thickness(1, 11, 0, 0); _rememberMe.FontSize = 10; _rememberMe.Foreground = medium; form.Children.Add(_rememberMe);

        _status.Foreground = new SolidColorBrush(Color.FromRgb(166, 60, 55)); _status.FontSize = 10; _status.TextWrapping = TextWrapping.Wrap; _status.Margin = new Thickness(1, 10, 0, 0); form.Children.Add(_status);

        var footer = new Grid { Margin = new Thickness(0, 15, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); Grid.SetRow(footer, 2); formPanel.Children.Add(footer);
        var databaseArea = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        databaseArea.Children.Add(new TextBlock { Text = "Veritabanı", Foreground = medium, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
        _databaseName.Text = System.IO.Path.GetFileName(_databasePath.Text); _databaseName.Foreground = dark; _databaseName.FontSize = 10; _databaseName.FontWeight = FontWeights.SemiBold; _databaseName.Margin = new Thickness(7, 0, 9, 0); _databaseName.VerticalAlignment = VerticalAlignment.Center; databaseArea.Children.Add(_databaseName);
        var database = new Button { Content = "Değiştir", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = new SolidColorBrush(Color.FromRgb(45, 116, 123)), FontSize = 10, FontWeight = FontWeights.SemiBold, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, Cursor = System.Windows.Input.Cursors.Hand }; database.Click += (_, _) => BrowseDatabase(); databaseArea.Children.Add(database); footer.Children.Add(databaseArea);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(buttons, 1); footer.Children.Add(buttons);
        var cancel = SmallButton("Kapat", false); cancel.Width = 72; cancel.Margin = new Thickness(0, 0, 7, 0); cancel.IsCancel = true; cancel.Click += (_, _) => Close(); buttons.Children.Add(cancel);
        var login = SmallButton("Giriş Yap", true); login.Width = 82; login.IsDefault = true; login.Click += (_, _) => Login(); buttons.Children.Add(login);
        var titleBar = new Border
        {
            Height = 38,
            Background = new SolidColorBrush(Color.FromRgb(61, 68, 74)),
            Padding = new Thickness(16, 0, 8, 0)
        };
        var titleLayout = new DockPanel { LastChildFill = false };
        titleLayout.Children.Add(new TextBlock
        {
            Text = "AR3 ERP  •  Oturum Aç",
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var close = new Button
        {
            Content = "×",
            Width = 30,
            Height = 28,
            Margin = new Thickness(8, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(220, 225, 228)),
            FontSize = 20,
            FontWeight = FontWeights.Light,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Kapat"
        };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        titleLayout.Children.Add(close);
        titleBar.Child = titleLayout;

        var shell = new DockPanel();
        DockPanel.SetDock(titleBar, Dock.Top);
        shell.Children.Add(titleBar);
        shell.Children.Add(root);
        Content = shell;
    }

    private static Button SmallButton(string text, bool primary) => new()
    {
        Content = text, Width = 82, Height = 29, Padding = new Thickness(10, 3, 10, 3),
        Background = new SolidColorBrush(primary ? Color.FromRgb(55, 66, 73) : Color.FromRgb(241, 242, 243)),
        Foreground = primary ? Brushes.White : new SolidColorBrush(Color.FromRgb(55, 62, 67)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(186, 190, 194)), BorderThickness = primary ? new Thickness(0) : new Thickness(1),
        FontSize = 11, FontWeight = FontWeights.SemiBold
    };

    private static void PrepareInput(TextBox input) { input.Height = 31; input.Padding = new Thickness(9, 4, 9, 4); input.Background = Brushes.White; input.Foreground = new SolidColorBrush(Color.FromRgb(40, 47, 52)); input.FontSize = 11; input.BorderBrush = new SolidColorBrush(Color.FromRgb(205, 211, 215)); input.BorderThickness = new Thickness(1); }
    private static void PrepareCombo(ComboBox input) { input.Height = 31; input.Background = Brushes.White; input.Foreground = new SolidColorBrush(Color.FromRgb(40, 47, 52)); input.FontSize = 11; input.BorderBrush = new SolidColorBrush(Color.FromRgb(205, 211, 215)); input.BorderThickness = new Thickness(1); }
    private static (Grid Container, StackPanel Left, StackPanel Right) TwoColumnRow()
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel { Margin = new Thickness(0, 0, 7, 0) }; var right = new StackPanel { Margin = new Thickness(7, 0, 0, 0) }; Grid.SetColumn(right, 1); grid.Children.Add(left); grid.Children.Add(right); return (grid, left, right);
    }

    private static void AddLabel(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(77, 87, 94)), FontSize = 10.5, FontWeight = FontWeights.Medium, Margin = new Thickness(1, 0, 0, 5) });

    private static string DefaultDatabasePath() => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "R3", "r3.db");

    private void BrowseDatabase()
    {
        var dialog = new OpenFileDialog { Filter = "R3 SQLite veritabanı|*.db;*.sqlite;*.sqlite3|Tüm dosyalar|*.*", CheckFileExists = false, FileName = "r3.db" };
        if (dialog.ShowDialog(this) == true) { _databasePath.Text = dialog.FileName; LoadDatabase(dialog.FileName); }
    }

    private bool LoadDatabase(string path)
    {
        try
        {
            _loading = true; _database = new StoreDatabase(path); _company.ItemsSource = _database.Query("SELECT id AS Id, code AS Code, name AS Name FROM companies WHERE is_active=1 ORDER BY code").DefaultView; _company.SelectedIndex = _company.Items.Count > 0 ? 0 : -1;
            if (_remembered?.CompanyId is { Length: > 0 }) _company.SelectedValue = _remembered.CompanyId;
            _databaseName.Text = System.IO.Path.GetFileName(_database.Path);
            _databaseName.ToolTip = _database.Path;
            _status.Foreground = new SolidColorBrush(Color.FromRgb(50, 122, 86));
            _status.Text = "● Hazır";
            return true;
        }
        catch (Exception ex) { _logger.LogError(ex, "Login database could not be opened. FileName={FileName}", System.IO.Path.GetFileName(path)); _database = null; _company.ItemsSource = null; _branch.ItemsSource = null; _databaseName.Text = string.IsNullOrWhiteSpace(path) ? "Seçilmedi" : System.IO.Path.GetFileName(path); SetError("Veritabanı açılamadı. Değiştir seçeneğiyle geçerli bir R3 veritabanı seçin."); return false; }
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
            if (_remembered?.BranchId is { Length: > 0 }) _branch.SelectedValue = _remembered.BranchId;
        }
        finally { _loading = false; }
    }

    // ASB MNUSER.UDFTSUBE equivalent: a user with a default branch (Kullanıcı ve Yetkiler) gets it
    // preselected once their user code is entered - only if it belongs to the selected company.
    private void SelectUserDefaultBranch()
    {
        if (_database == null || string.IsNullOrWhiteSpace(_username.Text)) return;
        try
        {
            var table = _database.Query("SELECT default_branch_id FROM users WHERE username=$u AND default_branch_id IS NOT NULL", ("$u", _username.Text.Trim()));
            if (table.Rows.Count == 0) return;
            var branchId = table.Rows[0][0].ToString();
            if (_branch.Items.Cast<object>().OfType<DataRowView>().Any(r => r["Id"].ToString() == branchId)) _branch.SelectedValue = branchId;
        }
        catch (Microsoft.Data.Sqlite.SqliteException) { }
    }

    private void Login()
    {
        if (_database == null) { SetError("Önce geçerli bir veritabanı seçin."); return; }
        if (_company.SelectedItem is not DataRowView company || _branch.SelectedItem is not DataRowView branch) { SetError("Firma ve şube seçimi zorunludur."); return; }
        var displayName = _database.Authenticate(_username.Text, _password.Password);
        if (displayName == null) { SetError("Kullanıcı adı veya şifre hatalı."); return; }
        // Şube erişimi (Kullanıcı ve Yetkiler › Şube / Depo Erişimi): null = unrestricted.
        if (new LocalUserAdminService(_database).AllowedBranchIds(_username.Text) is { } allowedBranches && !allowedBranches.Contains(branch["Id"].ToString()!))
        { SetError("Seçilen şubeye giriş yetkiniz yok. Yetkili olduğunuz bir şube seçin."); return; }
        var role = _database.GetUserRole(_username.Text);
        _roleInfo.Text = $"Rol: {role.Name}";
        if (!Guid.TryParse(company["Id"].ToString(), out var companyId) || !Guid.TryParse(branch["Id"].ToString(), out var branchId)) { SetError("Firma veya şube kaydı geçersiz."); return; }
        if (_rememberMe.IsChecked == true)
            LoginCredentialStore.Save(new RememberedLogin(_database.Path, _username.Text.Trim(), _password.Password, companyId.ToString(), branchId.ToString()));
        else
            LoginCredentialStore.Clear();
        Session = new StartupSession(_database.Path, companyId, company["Name"].ToString()!, branchId, branch["Name"].ToString()!, displayName, role.Code, role.Name, _username.Text.Trim());
        DialogResult = true;
    }

    private void SetError(string message)
    {
        _status.Foreground = new SolidColorBrush(Color.FromRgb(166, 60, 55));
        _status.Text = message;
    }
}
