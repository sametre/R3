using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using R3.Desktop;
using R3.Desktop.Logging;
using R3.Desktop.ViewModels;
using R3.Desktop.Views;
using R3.Infrastructure;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Bootstrap logging into a throwaway temp directory - never the real
        // %LOCALAPPDATA%\R3\logs used by the actual app - since MainWindow and
        // the Account ViewModels now require a logger to have been created.
        var logDirectory = Path.Combine(Path.GetTempPath(), "R3-uismoke-logs-" + Guid.NewGuid());
        DesktopLogging.Bootstrap(logDirectory);

        var app = new App();
        app.InitializeComponent();

        try
        {
            SmokeAccountsMvvmPoc();
            if (Environment.GetEnvironmentVariable("R3_UISMOKE_ACCOUNTS_ONLY") == "1") { app.Shutdown(); return; }
            RunMainWindowSmoke(app);
        }
        finally
        {
            DesktopLogging.Shutdown();
            try { Directory.Delete(logDirectory, recursive: true); } catch { /* best effort cleanup */ }
        }
    }

    private static void RunMainWindowSmoke(App app)
    {

        // NOTE: the checks below open MainWindow, which shows a blocking modal
        // login dialog (StartupLoginWindow) before any content loads. That
        // requires an interactive desktop session (click "Giriş yap"); it is
        // not runnable headlessly and is unrelated to the Accounts MVVM POC
        // verified above.
        var window = new MainWindow();
        window.Measure(new Size(1440, 900)); window.Arrange(new Rect(0, 0, 1440, 900)); window.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1440, 900, 96, 96, PixelFormats.Pbgra32);
        var surface = (FrameworkElement)window.Content; surface.Measure(new Size(1440, 900)); surface.Arrange(new Rect(0, 0, 1440, 900)); surface.UpdateLayout(); bitmap.Render(surface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory("artifacts");
        using (var file = File.Create("artifacts/R3-preview.png")) encoder.Save(file);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetMethod("OpenDefinitions", flags)!.Invoke(window, new object[] { true });
        typeof(MainWindow).GetMethod("OpenDefinitions", flags)!.Invoke(window, new object[] { false });
        typeof(MainWindow).GetMethod("OpenLedger", flags)!.Invoke(window, null);
        window.UpdateLayout();
        var tabs = (TabControl)window.FindName("Workspace");
        if (tabs.Items.Count != 4) throw new Exception("Expected home + three module tabs.");
        var dialog = new RecordDialog("Mağaza", null); dialog.Measure(new Size(480, 700)); dialog.Close();
        Console.WriteLine("PASS: Main window, icons, three module tabs and record dialog loaded.");
        window.Close(); app.Shutdown();
    }

    /// <summary>
    /// Verifies the Cari Kartlar MVVM POC without going through the interactive
    /// login window. Two independent checks, kept deliberately simple so this
    /// smoke tool has no dispatcher/async timing dependency of its own:
    /// 1) WPF-UI resource dictionaries resolve and the view lays out, by
    ///    constructing and measuring the real AccountsView + ViewModel.
    /// 2) The real persistence round-trip (LocalAccountService -> StoreDatabase,
    ///    a temp on-disk SQLite file) works, called directly and synchronously -
    ///    this is the same service call the ViewModel's RefreshCommand/
    ///    AccountEditViewModel.SaveCommand invoke.
    /// </summary>
    private static void SmokeAccountsMvvmPoc()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "R3-uismoke-" + Guid.NewGuid() + ".db");
        try
        {
            var db = new StoreDatabase(dbPath);
            var company = db.Query("SELECT id FROM companies LIMIT 1").Rows[0][0].ToString()!;
            var accounts = new LocalAccountService(db);

            var viewModel = new AccountsViewModel(accounts, company, DesktopLogging.CreateLogger<AccountsViewModel>());
            var view = new AccountsView(viewModel);
            view.Measure(new Size(1440, 900));
            view.Arrange(new Rect(0, 0, 1440, 900));
            view.UpdateLayout();
            Console.WriteLine("PASS: AccountsView constructed and laid out; WPF-UI ThemesDictionary/ControlsDictionary resolved without error.");

            accounts.Save(new AccountEdit("", company, "SMOKE01", "Smoke Test Cari"));
            var rows = accounts.Search(company, "SMOKE01");
            if (rows.Rows.Count != 1) throw new Exception($"Expected 1 account after save, got {rows.Rows.Count}.");
            Console.WriteLine("PASS: LocalAccountService.Save/Search round-tripped a real account through SQLite (same call path as AccountEditViewModel.SaveCommand / AccountsViewModel.RefreshCommand).");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
