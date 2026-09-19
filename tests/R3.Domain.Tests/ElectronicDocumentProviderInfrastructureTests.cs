using System.Text;
using R3.Infrastructure;

namespace R3.Domain.Tests;

/// <summary>
/// Phase 10: no real özel entegratör was identified anywhere in the repository (see
/// docs/architecture/ELECTRONIC-DOCUMENT-PROVIDER.md "BLOCKER"), so there is no vendor-specific
/// request/response/status mapping to test. What Phase 10 actually built - configuration validation,
/// provider selection, the never-silent "not configured" placeholder, and secret-at-rest protection -
/// is fully real and fully testable without one, and is covered here.
/// </summary>
public sealed class ElectronicDocumentProviderInfrastructureTests
{
    // --- §9/§37: configuration validation ---

    [Fact]
    public void Validate_DevelopmentInTest_IsValid()
    {
        var config = new ElectronicDocumentProviderConfiguration(ElectronicDocumentProviderKind.Development, ElectronicDocumentProviderEnvironment.Test, null, 30, 60, "1234567890");
        Assert.True(ElectronicDocumentProviderConfigurationValidator.Validate(config).IsValid);
    }

    [Fact]
    public void Validate_MissingCompanyIdentifier_IsInvalid()
    {
        var config = new ElectronicDocumentProviderConfiguration(ElectronicDocumentProviderKind.Development, ElectronicDocumentProviderEnvironment.Test, null, 30, 60, "");
        var result = ElectronicDocumentProviderConfigurationValidator.Validate(config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("VKN/TCKN"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveTimeout_IsInvalid(int timeout)
    {
        var config = new ElectronicDocumentProviderConfiguration(ElectronicDocumentProviderKind.Development, ElectronicDocumentProviderEnvironment.Test, null, timeout, 60, "1234567890");
        Assert.False(ElectronicDocumentProviderConfigurationValidator.Validate(config).IsValid);
    }

    [Fact]
    public void Validate_DevelopmentInProduction_IsInvalid()
    {
        // §9: a Development provider must never be reachable from a Production-flagged environment.
        var config = new ElectronicDocumentProviderConfiguration(ElectronicDocumentProviderKind.Development, ElectronicDocumentProviderEnvironment.Production, null, 30, 60, "1234567890");
        var result = ElectronicDocumentProviderConfigurationValidator.Validate(config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Development") && e.Contains("Production"));
    }

    [Fact]
    public void Validate_HttpKindWithoutBaseUrl_IsInvalid()
    {
        var config = new ElectronicDocumentProviderConfiguration(ElectronicDocumentProviderKind.Http, ElectronicDocumentProviderEnvironment.Test, null, 30, 60, "1234567890");
        var result = ElectronicDocumentProviderConfigurationValidator.Validate(config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("BaseUrl") || e.Contains("servis adresi"));
    }

    // --- §38/§70: provider selection never silently defaults ---

    [Fact]
    public void Factory_DevelopmentKind_ReturnsDevelopmentProvider() =>
        Assert.IsType<DevelopmentElectronicDocumentProvider>(
            ElectronicDocumentProviderFactory.Create(new(ElectronicDocumentProviderKind.Development, ElectronicDocumentProviderEnvironment.Test, null, 30, 60, "1234567890")));

    [Fact]
    public void Factory_HttpKind_ReturnsUnconfiguredPlaceholder_NotDevelopment() =>
        Assert.IsType<UnconfiguredElectronicDocumentProvider>(
            ElectronicDocumentProviderFactory.Create(new(ElectronicDocumentProviderKind.Http, ElectronicDocumentProviderEnvironment.Test, "https://example.invalid", 30, 60, "1234567890")));

    [Fact]
    public void Factory_InvalidConfiguration_ThrowsRatherThanConstructingProvider() =>
        Assert.Throws<InvalidOperationException>(() =>
            ElectronicDocumentProviderFactory.Create(new(ElectronicDocumentProviderKind.Http, ElectronicDocumentProviderEnvironment.Test, null, 30, 60, "1234567890")));

    // --- §18/§59/§66: never a silent fallback ---

    [Fact]
    public async Task UnconfiguredProvider_Send_IsPermanentFailure_NeverSilentSuccess()
    {
        var provider = new UnconfiguredElectronicDocumentProvider();
        var result = await provider.SendAsync(new("doc-1", ElectronicDocumentType.EInvoice, "uuid-1", "SF-1", "<xml/>", "hash", null, "acc-1", "key-1", null));
        Assert.False(result.Success);
        Assert.False(result.IsTransientFailure); // permanent - dead-letters immediately, never retries forever
        Assert.Equal(UnconfiguredElectronicDocumentProvider.ErrorCode, result.ProviderCode);
    }

    [Fact]
    public async Task UnconfiguredProvider_QueryStatus_IsPermanentFailure()
    {
        var provider = new UnconfiguredElectronicDocumentProvider();
        var result = await provider.QueryStatusAsync(new("doc-1", ElectronicDocumentType.EInvoice, "uuid-1", null, "key-1", null));
        Assert.False(result.Success);
        Assert.False(result.IsTransientFailure);
    }

    [Fact]
    public async Task UnconfiguredProvider_Health_ReportsUnhealthy() =>
        Assert.False((await new UnconfiguredElectronicDocumentProvider().CheckHealthAsync()).IsHealthy);

    [Fact]
    public async Task DevelopmentProvider_Health_ReportsHealthy_NoNetworkCall() =>
        Assert.True((await new DevelopmentElectronicDocumentProvider().CheckHealthAsync()).IsHealthy);

    // --- §8/§55: secret at-rest protection ---

    [Fact]
    public async Task InMemorySecretProvider_RoundTripsAndRemoves()
    {
        var secrets = new InMemoryElectronicDocumentSecretProvider();
        await secrets.SetSecretAsync("company-1", "ApiKey", "super-secret-value");
        Assert.Equal("super-secret-value", await secrets.GetSecretAsync("company-1", "ApiKey"));
        await secrets.RemoveSecretAsync("company-1", "ApiKey");
        Assert.Null(await secrets.GetSecretAsync("company-1", "ApiKey"));
    }

    [Fact]
    public async Task InMemorySecretProvider_DifferentCompanies_DoNotShareSecrets()
    {
        var secrets = new InMemoryElectronicDocumentSecretProvider();
        await secrets.SetSecretAsync("company-1", "ApiKey", "secret-for-1");
        await secrets.SetSecretAsync("company-2", "ApiKey", "secret-for-2");
        Assert.Equal("secret-for-1", await secrets.GetSecretAsync("company-1", "ApiKey"));
        Assert.Equal("secret-for-2", await secrets.GetSecretAsync("company-2", "ApiKey"));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task DpapiSecretProvider_RoundTrips_AndStoresCiphertextNotPlaintext()
    {
        var folder = Path.Combine(Path.GetTempPath(), "R3-secret-tests-" + Guid.NewGuid());
        try
        {
            var secrets = new DpapiElectronicDocumentSecretProvider(folder);
            const string secretValue = "correct-horse-battery-staple";
            await secrets.SetSecretAsync("company-1", "Password", secretValue);

            Assert.Equal(secretValue, await secrets.GetSecretAsync("company-1", "Password"));

            var fileBytes = await File.ReadAllBytesAsync(Path.Combine(folder, "edocument-secrets.dat"));
            var rawText = Encoding.UTF8.GetString(fileBytes);
            Assert.DoesNotContain(secretValue, rawText); // §55: never plaintext at rest
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task DpapiSecretProvider_MissingKey_ReturnsNull()
    {
        var folder = Path.Combine(Path.GetTempPath(), "R3-secret-tests-" + Guid.NewGuid());
        try
        {
            var secrets = new DpapiElectronicDocumentSecretProvider(folder);
            Assert.Null(await secrets.GetSecretAsync("company-1", "NeverSet"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }
}
