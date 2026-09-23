using System.Globalization;
using Microsoft.Extensions.Logging;
using R3.Desktop.WinForms.Design;
using R3.Desktop.WinForms.Features.Products;
using R3.Desktop.WinForms.Infrastructure.Errors;
using R3.Desktop.WinForms.Infrastructure.Logging;
using R3.Desktop.WinForms.Infrastructure.Navigation;
using R3.Desktop.WinForms.Shell;

namespace R3.Desktop.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // tr-TR everywhere: 1.500,00 · ₺1.500,00 · 23.09.2026 (grid cells, NumericUpDown, ToString).
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = AppFormats.Culture;
        Thread.CurrentThread.CurrentCulture = Thread.CurrentThread.CurrentUICulture = AppFormats.Culture;

        AppLog.Bootstrap();
        ErrorHandler.Install();
        ApplicationConfiguration.Initialize();
        AppTheme.Apply();
        var log = AppLog.For<Session>();
        log.LogInformation("R3 WinForms client starting. Path={Path}", Environment.ProcessPath);
        try
        {
            using var login = new LoginForm();
            if (login.ShowDialog() != DialogResult.OK || login.Session is not { } session) return;
            System.Windows.Forms.Application.Run(new MainForm(session, Screens()));
        }
        finally
        {
            log.LogInformation("R3 WinForms client stopped.");
            AppLog.Shutdown();
        }
    }

    /// <summary>The migrated screens. Menus are built from this list, one module at a time as screens move over.</summary>
    internal static ScreenRegistry Screens() => new ScreenRegistry()
        .Register(new ScreenDefinition(ProductListView.ScreenKey, "Stok", "Stok Kartları", "inventory.product.view", Keys.Control | Keys.Shift | Keys.S,
            (session, navigator) => new ProductListView(session, navigator)));

    private sealed class Session;
}
