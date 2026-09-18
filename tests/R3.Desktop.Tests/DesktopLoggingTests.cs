using System.IO;
using R3.Desktop.Logging;

namespace R3.Desktop.Tests;

public sealed class DesktopLoggingTests : IDisposable
{
    // A throwaway temp directory - this must never touch the real
    // %LOCALAPPDATA%\R3\logs used by the actual running app.
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "R3-logging-test-" + Guid.NewGuid());

    [Fact]
    public void BuildLoggerConfiguration_WritesRollingFileToGivenDirectory()
    {
        Directory.CreateDirectory(_directory);

        using (var logger = DesktopLogging.BuildLoggerConfiguration(_directory).CreateLogger())
        {
            logger.Information("Test message {Value}", 42);
        }

        var files = Directory.GetFiles(_directory, "r3-desktop-*.log");
        Assert.Single(files);
        var content = File.ReadAllText(files[0]);
        Assert.Contains("Test message", content);
        Assert.Contains("42", content);
        // Structured properties are written, e.g. the enriched process id -
        // confirms the configuration (not just a bare file sink) is exercised.
        Assert.Contains("ProcessId", content);
    }

    [Fact]
    public void DefaultLogDirectory_PointsUnderLocalAppDataR3Logs()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "R3", "logs");
        Assert.Equal(expected, DesktopLogging.DefaultLogDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
