using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace R3.Desktop.WinForms.Infrastructure.Session;

public sealed record RememberedLogin(string DatabasePath, string UserName, string Password, string? CompanyId, string? BranchId);

/// <summary>
/// "Beni hatırla": the same DPAPI-protected file and entropy as the WPF client
/// (%LOCALAPPDATA%\R3\settings\login.dat), so both clients remember the same database and user during migration.
/// </summary>
public static class LoginStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("R3.ERP.Login.v1");

    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "R3", "settings", "login.dat");

    /// <summary>Default database when nothing is remembered - the same location StoreDatabase uses.</summary>
    public static string DefaultDatabasePath => Environment.GetEnvironmentVariable("R3_SQLITE_PATH")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "R3", "data", "r3.db");

    public static RememberedLogin? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var clear = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<RememberedLogin>(clear);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            return null; // unreadable (other user, corrupted): behave as "nothing remembered", keep the file for the WPF client
        }
    }

    public static void Save(RememberedLogin login)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllBytes(FilePath, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(login), Entropy, DataProtectionScope.CurrentUser));
    }

    public static void Clear()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch (IOException) { }
    }
}
