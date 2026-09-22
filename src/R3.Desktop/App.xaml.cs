using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using R3.Desktop.Logging;
using R3.Desktop.ContextActions;

namespace R3.Desktop;

/// <summary>
/// R3 masaüstü uygulamasının başlangıç noktası.
/// </summary>
public partial class App : System.Windows.Application
{
    private ILogger<App>? _logger;

    private void ProfessionalGrid_Loaded(object sender, RoutedEventArgs e) => ErpGridContext.OnGridLoaded(sender, e);

    protected override void OnStartup(StartupEventArgs e)
    {
        KeyboardInteractionService.Initialize();
        DesktopLogging.Bootstrap();
        _logger = DesktopLogging.CreateLogger<App>();
        _logger.LogInformation(
            "R3 Desktop starting. ProcessId={ProcessId} OsVersion={OsVersion}",
            Environment.ProcessId, Environment.OSVersion.VersionString);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);
        _logger.LogInformation("R3 Desktop started.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("R3 Desktop shutting down. ExitCode={ExitCode}", e.ApplicationExitCode);
        CefSharpBootstrapper.Shutdown();
        base.OnExit(e);
        DesktopLogging.Shutdown();
    }

    /// <summary>
    /// Fatal: an exception reached the top of the UI thread's dispatcher
    /// without being handled anywhere. Log it as Critical with a short ErrorId
    /// the user can quote to support, show a friendly message with no stack
    /// trace, then shut down in a controlled way rather than let the app keep
    /// running with unknown/corrupted state.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var errorId = Guid.NewGuid();
        _logger?.LogCritical(e.Exception, "Unhandled UI thread exception. ErrorId={ErrorId}", errorId);
        MessageBox.Show(
            $"Beklenmeyen bir hata oluştu.\n\nHata kayıt altına alındı.\nHata Kodu: {errorId:N}\n\nUygulamayı yeniden başlatmanız önerilir.",
            "AR3 ERP", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(-1);
    }

    /// <summary>
    /// Fatal: an exception reached a non-UI thread unhandled. The runtime is
    /// terminating the process regardless (<see cref="UnhandledExceptionEventArgs.IsTerminating"/>
    /// is almost always true here) - best effort is to log Critical and flush
    /// before the process goes down.
    /// </summary>
    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var errorId = Guid.NewGuid();
        _logger?.LogCritical(
            e.ExceptionObject as Exception,
            "Unhandled non-UI-thread exception. ErrorId={ErrorId} IsTerminating={IsTerminating}",
            errorId, e.IsTerminating);
        DesktopLogging.Shutdown();
    }

    /// <summary>
    /// Recoverable: a background <see cref="System.Threading.Tasks.Task"/>
    /// faulted and nothing observed its exception. This does not corrupt UI
    /// state by itself, so log at Error (not Critical) and mark it observed
    /// so it does not additionally crash the process.
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var errorId = Guid.NewGuid();
        _logger?.LogError(e.Exception, "Unobserved task exception. ErrorId={ErrorId}", errorId);
        e.SetObserved();
    }
}
