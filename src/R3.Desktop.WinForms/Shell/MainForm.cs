using Krypton.Toolkit;
using R3.Desktop.WinForms.Components.Dialogs;
using R3.Desktop.WinForms.Design;
using R3.Desktop.WinForms.Infrastructure.Errors;
using R3.Desktop.WinForms.Infrastructure.Navigation;
using R3.Desktop.WinForms.Infrastructure.Session;
using R3.Desktop.WinForms.Shell.Workspace;

namespace R3.Desktop.WinForms.Shell;

/// <summary>
/// Application shell only: menu (built from the screen registry), tabbed workspace, status bar. It opens screens;
/// it never builds screen content, touches data, or holds business rules.
/// </summary>
public sealed class MainForm : Form, IWorkspaceNavigator
{
    private readonly AppSession _session;
    private readonly ScreenRegistry _screens;
    private readonly WorkspaceHost _workspace = new();

    public MainForm(AppSession session, ScreenRegistry screens)
    {
        _session = session; _screens = screens;
        Text = $"AR3 ERP — {session.CompanyName}";
        Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen; Size = new Size(1440, 900); MinimumSize = new Size(1024, 640);
        WindowState = FormWindowState.Maximized; Font = AppTypography.Body; KeyPreview = true; BackColor = AppColors.Background;

        var menu = BuildMenu();
        var status = new KryptonStatusStrip { SizingGrip = false, Font = AppTypography.Caption };
        status.Items.Add(new ToolStripStatusLabel($"{session.DisplayName} • {session.RoleName}"));
        status.Items.Add(new ToolStripStatusLabel($"{session.CompanyName} / {session.BranchName}") { BorderSides = ToolStripStatusLabelBorderSides.Left });
        status.Items.Add(new ToolStripStatusLabel(session.Database.Path) { Spring = true, TextAlign = ContentAlignment.MiddleLeft, BorderSides = ToolStripStatusLabelBorderSides.Left, ToolTipText = "Veritabanı dosyası" });
        status.Items.Add(new ToolStripStatusLabel(DateTime.Now.ToString("dd MMMM yyyy", AppFormats.Culture)));

        SuspendLayout();
        Controls.Add(_workspace); Controls.Add(menu); Controls.Add(status);
        MainMenuStrip = menu;
        ResumeLayout();

        FormClosing += (_, e) => { if (!_workspace.CanCloseAll()) e.Cancel = true; };
        Shown += (_, _) => { if (_screens.VisibleTo(_session).FirstOrDefault() is { } first) OpenScreen(first.Key); };
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { Font = AppTypography.Body, Padding = new Padding(AppSpacing.SM, AppSpacing.XS, 0, AppSpacing.XS) };
        foreach (var module in _screens.VisibleTo(_session).GroupBy(s => s.Module))
        {
            var top = new ToolStripMenuItem("&" + module.Key);
            foreach (var screen in module)
            {
                var item = new ToolStripMenuItem(screen.Title) { ShortcutKeys = screen.Shortcut };
                var key = screen.Key;
                item.Click += (_, _) => OpenScreen(key);
                top.DropDownItems.Add(item);
            }
            menu.Items.Add(top);
        }
        var window = new ToolStripMenuItem("&Pencere");
        var close = new ToolStripMenuItem("Sekmeyi &Kapat") { ShortcutKeys = Keys.Control | Keys.W };
        close.Click += (_, _) => _workspace.CloseSelected();
        window.DropDownItems.Add(close);
        menu.Items.Add(window);
        return menu;
    }

    public void OpenScreen(string key)
    {
        if (_screens.Find(key) is not { } screen) return;
        if (screen.Permission != null && !_session.Can(screen.Permission))
        {
            R3Dialogs.Warning(this, screen.Title, "Bu ekranı açma yetkiniz yok.");
            return;
        }
        Open(screen.Key, screen.Title, () => screen.Create(_session, this));
    }

    public void Open(string key, string title, Func<Control> create)
    {
        try { _workspace.Open(key, title, create); }
        catch (Exception ex) { ErrorHandler.Report(ex, title + " açılamadı", this); }
    }

    public void Close(Control view) { if (_workspace.TabOf(view) is { } tab) _workspace.Close(tab); }

    public void SetTitle(Control view, string title) { if (_workspace.TabOf(view) is { } tab) { tab.Text = title; _workspace.Invalidate(); } }
}
