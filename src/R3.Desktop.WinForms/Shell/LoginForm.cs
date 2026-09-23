using Krypton.Toolkit;
using Microsoft.Extensions.Logging;
using R3.Desktop.WinForms.Components.Toolbars;
using R3.Desktop.WinForms.Design;
using R3.Desktop.WinForms.Infrastructure.Errors;
using R3.Desktop.WinForms.Infrastructure.Logging;
using R3.Desktop.WinForms.Infrastructure.Session;
using R3.Infrastructure;

namespace R3.Desktop.WinForms.Shell;

/// <summary>
/// Oturum aç: database file, firma, şube, user. Same behavior as the WPF login (remembered login.dat, auto sign-in
/// unless started with --show-login, şube access check, the user's default şube), rebuilt with Krypton.
/// The database is opened off the UI thread (first open of a large store can create indexes).
/// </summary>
public sealed class LoginForm : Form
{
    private readonly KryptonTextBox _databasePath = new() { Dock = DockStyle.Fill };
    private readonly KryptonComboBox _company = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly KryptonComboBox _branch = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly KryptonTextBox _user = new() { Dock = DockStyle.Fill };
    private readonly KryptonTextBox _password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly KryptonCheckBox _remember = new() { Text = "Beni hatırla ve otomatik giriş yap", Checked = true };
    private readonly Label _status = new() { AutoSize = true, Font = AppTypography.Caption, ForeColor = AppColors.Danger, MaximumSize = new Size(460, 0) };
    private readonly R3PrimaryButton _signIn = new("Giriş yap");
    private readonly RememberedLogin? _remembered = LoginStore.Load();
    private StoreDatabase? _database;
    private LocalSessionService? _sessions;
    private int _openVersion;
    /// <summary>--database &lt;path&gt; (development/QA): open that file and leave the shared login.dat untouched.</summary>
    private readonly string? _databaseOverride = ArgumentAfter("--database");

    public AppSession? Session { get; private set; }

    public LoginForm()
    {
        Text = "AR3 ERP • Oturum Aç"; StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ClientSize = new Size(520, 400); Font = AppTypography.Body; KeyPreview = true;

        var browse = new R3SecondaryButton("Gözat…", Browse, "Başka bir R3 veritabanı dosyası seçin");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(AppSpacing.XL, AppSpacing.LG, AppSpacing.XL, AppSpacing.LG), BackColor = AppColors.Surface };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new Label { Text = "AR3 ERP", Font = AppTypography.PageTitle, ForeColor = AppColors.TextPrimary, AutoSize = true, Margin = new Padding(0, 0, 0, AppSpacing.XS) };
        var subtitle = new Label { Text = "Çalışmak istediğiniz veritabanı, firma ve şubeyi seçin.", Font = AppTypography.Caption, ForeColor = AppColors.TextSecondary, AutoSize = true, Margin = new Padding(0, 0, 0, AppSpacing.MD) };
        layout.Controls.Add(title, 0, 0); layout.SetColumnSpan(title, 3);
        layout.Controls.Add(subtitle, 0, 1); layout.SetColumnSpan(subtitle, 3);
        var row = 2;
        void Field(string caption, Control control, Control? extra = null)
        {
            layout.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = AppColors.TextSecondary, Anchor = AnchorStyles.Left, Margin = new Padding(0, AppSpacing.SM, AppSpacing.SM, 0) }, 0, row);
            control.Margin = new Padding(0, AppSpacing.XS, 0, AppSpacing.XS); control.AccessibleName = caption;
            layout.Controls.Add(control, 1, row);
            if (extra != null) { extra.Margin = new Padding(AppSpacing.XS, AppSpacing.XS, 0, AppSpacing.XS); layout.Controls.Add(extra, 2, row); }
            else layout.SetColumnSpan(control, 2);
            row++;
        }
        Field("Veritabanı", _databasePath, browse);
        Field("Firma", _company);
        Field("Şube", _branch);
        Field("Kullanıcı", _user);
        Field("Şifre", _password);
        layout.Controls.Add(_remember, 1, row++); layout.SetColumnSpan(_remember, 2);
        layout.Controls.Add(_status, 1, row++); layout.SetColumnSpan(_status, 2);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, AppSpacing.SM, 0, 0) };
        var cancel = new R3SecondaryButton("Çıkış") { DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([_signIn, cancel]);
        layout.Controls.Add(buttons, 0, row); layout.SetColumnSpan(buttons, 3);
        Controls.Add(layout);
        AcceptButton = _signIn; CancelButton = cancel;

        _signIn.Click += (_, _) => SignIn();
        _company.SelectedIndexChanged += (_, _) => LoadBranches();
        _user.Leave += (_, _) => SelectDefaultBranch();
        _databasePath.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = OpenDatabaseAsync(_databasePath.Text); } };

        _databasePath.Text = _databaseOverride ?? (_remembered?.DatabasePath is { Length: > 0 } remembered && File.Exists(remembered) ? remembered : LoginStore.DefaultDatabasePath);
        if (_databaseOverride != null) { _remember.Checked = false; _remember.Enabled = false; _remember.Text = "Geliştirici veritabanı: oturum bilgisi kaydedilmez"; }
        _user.Text = _remembered?.UserName ?? "admin";
        _password.Text = _remembered?.Password ?? "";
        Shown += async (_, _) =>
        {
            await OpenDatabaseAsync(_databasePath.Text);
            var autoSignIn = _remembered != null && _database != null && !Environment.GetCommandLineArgs().Any(a => a.Equals("--show-login", StringComparison.OrdinalIgnoreCase));
            if (autoSignIn) SignIn();
            else (_password.Text.Length == 0 ? _password : _user).Focus();
        };
    }

    private void Browse()
    {
        using var dialog = new OpenFileDialog { Filter = "R3 SQLite veritabanı|*.db;*.sqlite;*.sqlite3|Tüm dosyalar|*.*", FileName = Path.GetFileName(_databasePath.Text), CheckFileExists = true };
        try { dialog.InitialDirectory = Path.GetDirectoryName(_databasePath.Text); } catch (ArgumentException) { }
        if (dialog.ShowDialog(this) == DialogResult.OK) { _databasePath.Text = dialog.FileName; _ = OpenDatabaseAsync(dialog.FileName); }
    }

    private async Task OpenDatabaseAsync(string path)
    {
        var version = ++_openVersion;
        _status.ForeColor = AppColors.TextSecondary; _status.Text = "Veritabanı açılıyor…"; _signIn.Enabled = false; UseWaitCursor = true;
        try
        {
            var (database, companies) = await Task.Run(() => { var db = new StoreDatabase(path); return (db, new LocalSessionService(db).Companies()); });
            if (version != _openVersion) return;
            _database = database; _sessions = new LocalSessionService(database);
            _company.DataSource = companies.ToList();
            if (_remembered?.CompanyId is { Length: > 0 } companyId && companies.FirstOrDefault(c => c.Id == companyId) is { } remembered) _company.SelectedItem = remembered;
            LoadBranches();
            _status.Text = companies.Count == 0 ? "Bu veritabanında aktif firma yok." : "";
            _signIn.Enabled = companies.Count > 0;
        }
        catch (Exception ex)
        {
            if (version != _openVersion) return;
            AppLog.For<LoginForm>().LogWarning(ex, "Database could not be opened: {Path}", path);
            _database = null; _company.DataSource = null; _branch.DataSource = null;
            SetError("Veritabanı açılamadı: " + ErrorHandler.UserMessage(ex));
        }
        finally { if (version == _openVersion) UseWaitCursor = false; }
    }

    private void LoadBranches()
    {
        if (_sessions == null || _company.SelectedItem is not SessionOption company) { _branch.DataSource = null; return; }
        var branches = _sessions.Branches(company.Id).ToList();
        _branch.DataSource = branches;
        var preferred = _remembered?.CompanyId == company.Id ? _remembered?.BranchId : null;
        if (branches.FirstOrDefault(b => b.Id == preferred) is { } remembered) _branch.SelectedItem = remembered;
        SelectDefaultBranch();
    }

    private void SelectDefaultBranch()
    {
        if (_sessions == null || _user.Text.Trim().Length == 0 || _branch.DataSource is not List<SessionOption> branches) return;
        try { if (_sessions.DefaultBranchId(_user.Text) is { } id && branches.FirstOrDefault(b => b.Id == id) is { } branch) _branch.SelectedItem = branch; }
        catch (Microsoft.Data.Sqlite.SqliteException ex) { AppLog.For<LoginForm>().LogWarning(ex, "Default branch lookup failed"); }
    }

    private void SignIn()
    {
        if (_database == null || _sessions == null) { SetError("Önce geçerli bir veritabanı seçin."); return; }
        if (_company.SelectedItem is not SessionOption company || _branch.SelectedItem is not SessionOption branch) { SetError("Firma ve şube seçimi zorunludur."); return; }
        var result = _sessions.SignIn(_user.Text.Trim(), _password.Text, branch.Id);
        if (!result.Success) { SetError(result.Error!); _password.Focus(); _password.SelectAll(); return; }
        if (_databaseOverride == null)
        {
            if (_remember.Checked) LoginStore.Save(new RememberedLogin(_database.Path, _user.Text.Trim(), _password.Text, company.Id, branch.Id));
            else LoginStore.Clear();
        }
        _database.OperatorUserName = _user.Text.Trim();
        Session = new AppSession(_database, company.Id, company.Name, branch.Id, branch.Name, _user.Text.Trim(), result.DisplayName, result.RoleName);
        AppLog.For<LoginForm>().LogInformation("Signed in {User} to {Company}/{Branch} on {Database}", Session.LoginName, company.Code, branch.Code, _database.Path);
        DialogResult = DialogResult.OK;
    }

    private static string? ArgumentAfter(string name)
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private void SetError(string message) { _status.ForeColor = AppColors.Danger; _status.Text = message; }
}
