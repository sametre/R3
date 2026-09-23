using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using R3.Desktop.WinForms.Components.Dialogs;
using R3.Desktop.WinForms.Infrastructure.Logging;

namespace R3.Desktop.WinForms.Infrastructure.Errors;

/// <summary>
/// Global and per-action error handling: every exception is logged with its stack trace; the user sees a short
/// Turkish message and a reference time, never the stack trace. The app does not close silently.
/// </summary>
public static class ErrorHandler
{
    public static void Install()
    {
        System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        System.Windows.Forms.Application.ThreadException += (_, e) => Report(e.Exception, "Beklenmeyen hata");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) AppLog.For("R3.Desktop.WinForms.Errors").LogCritical(ex, "Unhandled exception (terminating={Terminating})", e.IsTerminating);
            AppLog.Shutdown();
        };
        TaskScheduler.UnobservedTaskException += (_, e) => { AppLog.For("R3.Desktop.WinForms.Errors").LogError(e.Exception, "Unobserved task exception"); e.SetObserved(); };
    }

    /// <summary>Logs <paramref name="exception"/> and shows a friendly message. Safe to call from any UI handler.</summary>
    public static void Report(Exception exception, string title, IWin32Window? owner = null)
    {
        AppLog.For("R3.Desktop.WinForms.Errors").LogError(exception, "{Title}", title);
        R3Dialogs.Error(owner, title, UserMessage(exception));
    }

    /// <summary>What the user reads: validation/business messages as-is, infrastructure failures generically.</summary>
    public static string UserMessage(Exception exception) => exception switch
    {
        ArgumentException or InvalidOperationException or KeyNotFoundException => exception.Message,
        SqliteException { SqliteErrorCode: 5 or 6 } => "Veritabanı şu anda başka bir işlem tarafından kullanılıyor. Birkaç saniye sonra tekrar deneyin.",
        SqliteException { SqliteErrorCode: 19 } => "Kayıt kaydedilemedi: kod zaten kullanılıyor veya bağlı bir kayıt geçersiz.",
        SqliteException => "Veritabanı işlemi tamamlanamadı. Ayrıntılar günlük dosyasına yazıldı.",
        IOException or UnauthorizedAccessException => "Dosyaya erişilemedi. Dosyanın başka bir programda açık olmadığını ve yazma izniniz olduğunu kontrol edin.",
        _ => $"İşlem tamamlanamadı. Ayrıntılar günlük dosyasına yazıldı ({DateTime.Now:HH:mm:ss})."
    };
}
