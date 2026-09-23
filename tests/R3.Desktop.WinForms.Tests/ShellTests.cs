using R3.Desktop.WinForms.Infrastructure.Session;
using R3.Desktop.WinForms.Shell;
using R3.Infrastructure;

namespace R3.Desktop.WinForms.Tests;

public sealed class ShellTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-winforms-shell-" + Guid.NewGuid());

    [Fact]
    public void MainFormHasMenuWorkspaceAndStatusAndBuildsMenuFromRegistry()
    {
        Exception? failure = null; string? report = null;
        var thread = new Thread(() =>
        {
            try
            {
                var db = new StoreDatabase(Path.Combine(_folder, "shell.db"));
                var session = new AppSession(db, "00000000-0000-0000-0000-000000000001", "Firma", "b", "Şube", "admin", "Yönetici", "Admin");
                using var form = new MainForm(session, Program.Screens());
                var menu = form.MainMenuStrip;
                report = $"controls={form.Controls.Count} [{string.Join(",", form.Controls.Cast<Control>().Select(c => c.GetType().Name))}] menu={menu?.Items.Count}";
                Assert.NotNull(menu);
                Assert.Contains(form.Controls.Cast<Control>(), c => c is Shell.Workspace.WorkspaceHost || c.Controls.OfType<Shell.Workspace.WorkspaceHost>().Any() || Descendants(c).OfType<Shell.Workspace.WorkspaceHost>().Any());
                Assert.Equal(["&Stok", "&Pencere"], menu!.Items.Cast<ToolStripItem>().Select(i => i.Text));
                form.WindowState = FormWindowState.Normal; form.Show(); System.Windows.Forms.Application.DoEvents();
                report += $" | shown: handle={form.Handle} menuParent={menu.Parent?.GetType().Name} menuHandle={menu.IsHandleCreated} menuVisible={menu.Visible} parentIsForm={ReferenceEquals(menu.Parent, form)} menuParentHandle={(menu.IsHandleCreated ? GetParent(menu.Handle) : 0)} formHandle={form.Handle}";
                Assert.True(menu.IsHandleCreated && menu.Visible, report);
                Assert.Equal(form.Handle, (IntPtr)GetParent(menu.Handle));
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new Xunit.Sdk.XunitException(report + Environment.NewLine + failure);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetParent(nint hWnd);

    private static IEnumerable<Control> Descendants(Control root) =>
        root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }
}
