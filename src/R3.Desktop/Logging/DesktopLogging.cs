using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace R3.Desktop.Logging;

/// <summary>
/// Bootstraps structured logging for the Desktop app. Serilog is the sink
/// implementation; every consumer (ViewModels, services) depends on
/// <see cref="ILogger{T}"/> from Microsoft.Extensions.Logging, not on Serilog
/// directly, so the implementation could be swapped without touching call sites.
/// There is no DI container in R3.Desktop (by design, see Phase 2 notes), so
/// loggers are created manually via <see cref="CreateLogger{T}"/>, the same
/// way every other service in this app is constructed.
/// </summary>
public static class DesktopLogging
{
    private const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}";

    private static ILoggerFactory? _factory;

    /// <summary>Default log directory: %LOCALAPPDATA%\R3\logs.</summary>
    public static string DefaultLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "R3", "logs");

    public static void Bootstrap(string? logDirectory = null)
    {
        var directory = logDirectory ?? DefaultLogDirectory;
        Directory.CreateDirectory(directory);
        Log.Logger = BuildLoggerConfiguration(directory).CreateLogger();
        _factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace)
            .AddSerilog(Log.Logger, dispose: false));
    }

    /// <summary>
    /// Pure, side-effect-free configuration builder so tests can point it at a
    /// temp directory instead of the real per-user log folder.
    /// </summary>
    public static LoggerConfiguration BuildLoggerConfiguration(string logDirectory) => new LoggerConfiguration()
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .Enrich.WithProperty("ApplicationVersion", ApplicationVersion)
        .Enrich.WithProperty("ProcessId", Environment.ProcessId)
        .WriteTo.File(
            Path.Combine(logDirectory, "r3-desktop-.log"),
            outputTemplate: OutputTemplate,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            fileSizeLimitBytes: 50 * 1024 * 1024,
            rollOnFileSizeLimit: true,
            shared: true);

    public static ILogger<T> CreateLogger<T>() =>
        (_factory ?? throw new InvalidOperationException($"{nameof(DesktopLogging)}.{nameof(Bootstrap)} must run before any logger is created."))
        .CreateLogger<T>();

    public static void Shutdown() => Log.CloseAndFlush();

    private static string ApplicationVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
}
