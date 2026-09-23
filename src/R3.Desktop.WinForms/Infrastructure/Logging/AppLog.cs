using System.Reflection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace R3.Desktop.WinForms.Infrastructure.Logging;

/// <summary>
/// Same logging stack as the WPF client (Serilog file sink behind Microsoft.Extensions.Logging, no DI container):
/// %LOCALAPPDATA%\R3\logs, daily files, 30 kept - with its own file prefix so the two clients' logs stay apart.
/// </summary>
public static class AppLog
{
    private const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}";
    private static ILoggerFactory? _factory;

    public static string Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "R3", "logs");

    public static void Bootstrap(string? directory = null)
    {
        var folder = directory ?? Directory;
        System.IO.Directory.CreateDirectory(folder);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.WithProperty("Client", "WinForms")
            .Enrich.WithProperty("ApplicationVersion", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0")
            .Enrich.WithProperty("ProcessId", Environment.ProcessId)
            .WriteTo.File(Path.Combine(folder, "r3-winforms-.log"), outputTemplate: OutputTemplate, rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30, fileSizeLimitBytes: 50 * 1024 * 1024, rollOnFileSizeLimit: true, shared: true)
            .CreateLogger();
        _factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace).AddSerilog(Log.Logger, dispose: false));
    }

    /// <summary>Logger for <typeparamref name="T"/>; a no-op logger before <see cref="Bootstrap"/> (tests).</summary>
    public static Microsoft.Extensions.Logging.ILogger For<T>() =>
        _factory?.CreateLogger<T>() ?? (Microsoft.Extensions.Logging.ILogger)Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    /// <summary>Logger for a static class / named area.</summary>
    public static Microsoft.Extensions.Logging.ILogger For(string category) =>
        _factory?.CreateLogger(category) ?? (Microsoft.Extensions.Logging.ILogger)Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public static void Shutdown() => Log.CloseAndFlush();
}
