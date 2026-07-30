using Krypton.Toolkit;
using R3.Application.Abstractions;
using R3.Application.Authentication;
using R3.Desktop.Branding;
using R3.Desktop.Theme;

namespace R3.Desktop.Forms;

public sealed class LoginForm : KryptonForm
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IUserSession _userSession;
    private readonly KryptonComboBox _branch = new();
    private readonly KryptonComboBox _user = new();
    private readonly KryptonTextBox _pin = new();
    private readonly KryptonLabel _message = new();
    private readonly KryptonButton _loginButton;

    public LoginForm(
        IAuthenticationService authenticationService,
        IUserSession userSession)
    {
        _authenticationService = authenticationService;
        _userSession = userSession;
        _loginButton = R3Theme.CreatePrimaryButton("Giriş Yap");

        R3Theme.Apply();
        InitializeLogin();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await LoadBranchesAsync();
    }

    private void InitializeLogin()
    {
        Text = "R3 ERP • Güvenli Giriş";
        Icon = R3Branding.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(760, 470);
        BackColor = R3Colors.Canvas;

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 285));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var brandPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(24, 48, 75),
            Padding = new Padding(38, 62, 38, 36)
        };
        brandPanel.Controls.Add(new Label
        {
            Text = "Güvenli ve hızlı\nkurumsal yönetim",
            Dock = DockStyle.Bottom,
            Height = 90,
            ForeColor = Color.FromArgb(203, 213, 225),
            Font = new Font("Segoe UI", 12),
            TextAlign = ContentAlignment.MiddleLeft
        });
        brandPanel.Controls.Add(new Label
        {
            Text = "KOBİ ÇÖZÜMLERİ",
            Dock = DockStyle.Bottom,
            Height = 42,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 13.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        });
        brandPanel.Controls.Add(new PictureBox
        {
            Image = R3Branding.Logo,
            Dock = DockStyle.Top,
            Height = 112,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.White,
            Padding = new Padding(22)
        });

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(52, 34, 52, 24),
            ColumnCount = 1,
            RowCount = 11
        };
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        form.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        form.Controls.Add(new Label
        {
            Text = "Oturum Aç",
            Dock = DockStyle.Fill,
            ForeColor = R3Theme.Text,
            Font = new Font("Segoe UI Semibold", 20, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        form.Controls.Add(new Label
        {
            Text = "Şube ve kullanıcıyı seçin, yalnızca PIN kodunuzu girin.",
            Dock = DockStyle.Fill,
            ForeColor = R3Theme.MutedText,
            Font = new Font("Segoe UI", 9),
            TextAlign = ContentAlignment.TopLeft
        }, 0, 1);

        ConfigureCombo(_branch, "DisplayName");
        ConfigureCombo(_user, "DisplayText");
        _branch.SelectedIndexChanged += BranchSelectedIndexChanged;

        form.Controls.Add(CreateLabel("ŞUBE / MAĞAZA"), 0, 2);
        form.Controls.Add(_branch, 0, 3);
        form.Controls.Add(CreateLabel("KULLANICI"), 0, 4);
        form.Controls.Add(_user, 0, 5);
        form.Controls.Add(CreateLabel("PIN"), 0, 6);

        ConfigurePin();
        form.Controls.Add(_pin, 0, 7);

        _loginButton.Dock = DockStyle.Fill;
        _loginButton.Margin = new Padding(0, 7, 0, 7);
        _loginButton.Click += async (_, _) => await AuthenticateAsync();
        form.Controls.Add(_loginButton, 0, 8);

        _message.Dock = DockStyle.Fill;
        _message.StateCommon.ShortText.Color1 = Color.Firebrick;
        _message.StateCommon.ShortText.TextH = PaletteRelativeAlign.Center;
        _message.StateCommon.ShortText.Font = new Font("Segoe UI", 9);
        form.Controls.Add(_message, 0, 9);

        form.Controls.Add(new Label
        {
            Text = "© 2026 R3 • Güvenli Kurumsal Erişim",
            Dock = DockStyle.Bottom,
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Segoe UI", 8),
            TextAlign = ContentAlignment.BottomCenter
        }, 0, 10);

        shell.Controls.Add(brandPanel, 0, 0);
        shell.Controls.Add(form, 1, 0);
        Controls.Add(shell);
        AcceptButton = _loginButton;
    }

    private static Label CreateLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        ForeColor = R3Theme.MutedText,
        Font = new Font("Segoe UI", 8, FontStyle.Bold),
        TextAlign = ContentAlignment.BottomLeft
    };

    private static void ConfigureCombo(KryptonComboBox combo, string displayMember)
    {
        combo.Dock = DockStyle.Fill;
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.DisplayMember = displayMember;
        combo.StateCommon.ComboBox.Border.Rounding = 6;
        combo.StateCommon.ComboBox.Border.Color1 = R3Theme.Border;
        combo.StateCommon.ComboBox.Content.Font = new Font("Segoe UI", 10);
        combo.Margin = new Padding(0, 5, 0, 7);
    }

    private void ConfigurePin()
    {
        _pin.Dock = DockStyle.Fill;
        _pin.MaxLength = 12;
        _pin.UseSystemPasswordChar = true;
        _pin.CueHint.CueHintText = "PIN kodunuzu girin";
        _pin.StateCommon.Border.Rounding = 6;
        _pin.StateCommon.Border.Color1 = R3Theme.Border;
        _pin.StateCommon.Content.Font = new Font("Segoe UI", 11);
        _pin.Margin = new Padding(0, 5, 0, 7);
        _pin.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
                e.Handled = true;
        };
    }

    private async Task LoadBranchesAsync()
    {
        SetBusy(true);
        try
        {
            IReadOnlyList<LoginBranchOption> branches = await _authenticationService.GetBranchesAsync();
            _branch.DataSource = branches.ToList();
            if (branches.Count == 0)
                _message.Text = "Aktif şube veya mağaza bulunamadı.";
        }
        catch (Exception)
        {
            _message.Text = "Sunucuya bağlanılamadı.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void BranchSelectedIndexChanged(object? sender, EventArgs e)
    {
        await LoadUsersAsync();
    }

    private async Task LoadUsersAsync()
    {
        if (_branch.SelectedItem is not LoginBranchOption branch)
            return;

        try
        {
            IReadOnlyList<LoginUserOption> users = await _authenticationService.GetUsersAsync(branch.BranchId);
            _user.DataSource = users.ToList();
            _message.Text = users.Count == 0 ? "Bu şubeye tanımlı aktif kullanıcı bulunamadı." : string.Empty;
            if (users.Count > 0)
                _pin.Focus();
        }
        catch (Exception)
        {
            _message.Text = "Kullanıcı listesi alınamadı.";
        }
    }

    private async Task AuthenticateAsync()
    {
        if (_branch.SelectedItem is not LoginBranchOption branch ||
            _user.SelectedItem is not LoginUserOption user)
        {
            _message.Text = "Şube ve kullanıcı seçimi zorunludur.";
            return;
        }

        _message.Text = string.Empty;
        SetBusy(true);
        try
        {
            LoginResult result = await _authenticationService.AuthenticateAsync(
                new LoginRequest(branch.BranchCode, user.UserName, _pin.Text));

            if (!result.Succeeded || result.Session is null)
            {
                _pin.Clear();
                _pin.Focus();
                _message.Text = result.ErrorMessage ?? "Giriş yapılamadı.";
                return;
            }

            _userSession.Start(result.Session);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception)
        {
            _message.Text = "Giriş servisine ulaşılamadı.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _branch.Enabled = !busy;
        _user.Enabled = !busy;
        _pin.Enabled = !busy;
        _loginButton.Enabled = !busy;
        _loginButton.Text = busy ? "Lütfen bekleyin..." : "Giriş Yap";
        UseWaitCursor = busy;
    }
}
