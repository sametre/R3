using CefSharp;
using CefSharp.Wpf;
using System.IO;

namespace R3.Desktop;

internal static class CefSharpBootstrapper
{
    private static readonly object Gate = new();

    public static void EnsureInitialized()
    {
        if (Cef.IsInitialized == true) return;

        lock (Gate)
        {
            if (Cef.IsInitialized == true) return;

            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "R3", "cefsharp");
            Directory.CreateDirectory(root);

            var settings = new CefSettings
            {
                CachePath = Path.Combine(root, "cache"),
                LogFile = Path.Combine(root, "cef.log"),
                LogSeverity = LogSeverity.Warning,
                PersistSessionCookies = false
            };
            settings.CefCommandLineArgs["disable-background-networking"] = "1";
            settings.CefCommandLineArgs["disable-component-update"] = "1";

            if (!Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null))
                throw new InvalidOperationException("CefSharp web motoru başlatılamadı.");
        }
    }

    public static void Shutdown()
    {
        if (Cef.IsInitialized == true) Cef.Shutdown();
    }
}
