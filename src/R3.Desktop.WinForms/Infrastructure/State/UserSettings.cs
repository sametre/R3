using System.Text.Json;

namespace R3.Desktop.WinForms.Infrastructure.State;

/// <summary>
/// Per-user UI preferences (grid columns, density, page size) as small JSON files under
/// %LOCALAPPDATA%\R3\settings\winforms\. Failures never break a screen: a missing or unreadable file = defaults.
/// </summary>
public static class UserSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "R3", "settings", "winforms");

    public static T? Load<T>(string key) where T : class
    {
        try
        {
            var path = PathFor(key);
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public static void Save<T>(string key, T value)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(PathFor(key), JsonSerializer.Serialize(value, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* preferences are best-effort */ }
    }

    private static string PathFor(string key) => Path.Combine(Folder, string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_')) + ".json");
}
