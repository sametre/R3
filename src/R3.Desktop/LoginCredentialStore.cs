using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;

namespace R3.Desktop;

internal sealed record RememberedLogin(
    string DatabasePath,
    string UserName,
    string Password,
    string? CompanyId,
    string? BranchId);

internal static class LoginCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("R3.ERP.Login.v1");
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "R3", "settings", "login.dat");

    public static RememberedLogin? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var encrypted = File.ReadAllBytes(FilePath);
            var clear = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<RememberedLogin>(clear);
        }
        catch
        {
            Clear();
            return null;
        }
    }

    public static void Save(RememberedLogin login)
    {
        var folder = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(folder);
        var clear = JsonSerializer.SerializeToUtf8Bytes(login);
        var encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, encrypted);
    }

    public static void Clear()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch { }
    }
}
