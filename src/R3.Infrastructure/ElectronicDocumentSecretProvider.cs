using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace R3.Infrastructure;

// Phase 10 (§8): no secret/config vault exists anywhere else in R3 - this is the minimal abstraction
// so a real provider adapter never has a reason to put Username/Password/ApiKey/ClientSecret/
// CertificatePassword into a normal business table, a log line, an audit entry or an exception
// message. Keyed by (companyId, key) so different companies/environments can hold independent
// credentials for the same provider kind.
public interface IElectronicDocumentSecretProvider
{
    Task<string?> GetSecretAsync(string companyId, string key, CancellationToken ct = default);
    Task SetSecretAsync(string companyId, string key, string value, CancellationToken ct = default);
    Task RemoveSecretAsync(string companyId, string key, CancellationToken ct = default);
}

// Real (not a stub) implementation: encrypts at rest with Windows DPAPI (CurrentUser scope - the
// same OS-level protection Windows Credential Manager itself is built on), storing ciphertext in a
// file next to the SQLite database - never in a business table, never in schema_migrations-tracked
// storage. Deliberately file-based rather than a new DB table: a secrets file can be excluded from
// any future DB-level export/backup/sync path without special-casing one table among many.
[SupportedOSPlatform("windows")]
public sealed class DpapiElectronicDocumentSecretProvider : IElectronicDocumentSecretProvider
{
    private readonly string _filePath;
    private static readonly byte[] Entropy = "R3.ElectronicDocument.Secrets.v1"u8.ToArray();

    public DpapiElectronicDocumentSecretProvider(string storeDirectory)
    {
        Directory.CreateDirectory(storeDirectory);
        _filePath = Path.Combine(storeDirectory, "edocument-secrets.dat");
    }

    public async Task<string?> GetSecretAsync(string companyId, string key, CancellationToken ct = default)
    {
        var all = await LoadAsync(ct);
        return all.TryGetValue(SecretKey(companyId, key), out var value) ? value : null;
    }

    public async Task SetSecretAsync(string companyId, string key, string value, CancellationToken ct = default)
    {
        var all = await LoadAsync(ct);
        all[SecretKey(companyId, key)] = value;
        await SaveAsync(all, ct);
    }

    public async Task RemoveSecretAsync(string companyId, string key, CancellationToken ct = default)
    {
        var all = await LoadAsync(ct);
        if (all.Remove(SecretKey(companyId, key))) await SaveAsync(all, ct);
    }

    private static string SecretKey(string companyId, string key) => $"{companyId}:{key}";

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath)) return [];
        var encrypted = await File.ReadAllBytesAsync(_filePath, ct);
        if (encrypted.Length == 0) return [];
        var plaintext = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext) ?? [];
    }

    private async Task SaveAsync(Dictionary<string, string> all, CancellationToken ct)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(all);
        var encrypted = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(_filePath, encrypted, ct);
    }
}

// Test/CI-friendly in-memory implementation - still never logs anything, but does not require a
// Windows-protected file, so unit tests don't depend on DPAPI/user-profile state.
public sealed class InMemoryElectronicDocumentSecretProvider : IElectronicDocumentSecretProvider
{
    private readonly Dictionary<string, string> _values = [];
    public Task<string?> GetSecretAsync(string companyId, string key, CancellationToken ct = default) =>
        Task.FromResult(_values.TryGetValue($"{companyId}:{key}", out var value) ? value : null);
    public Task SetSecretAsync(string companyId, string key, string value, CancellationToken ct = default)
    { _values[$"{companyId}:{key}"] = value; return Task.CompletedTask; }
    public Task RemoveSecretAsync(string companyId, string key, CancellationToken ct = default)
    { _values.Remove($"{companyId}:{key}"); return Task.CompletedTask; }
}
