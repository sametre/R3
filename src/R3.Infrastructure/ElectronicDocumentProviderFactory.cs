namespace R3.Infrastructure;

// Phase 10 (§11). The concrete authentication scheme (Basic/Bearer/OAuth2/SOAP UsernameToken/API
// Key/Certificate) is provider-specific and not yet known - no real entegratör has been identified
// (see docs/architecture/ELECTRONIC-DOCUMENT-PROVIDER.md's Blocker). This interface is only the seam
// a real HTTP/SOAP adapter will implement once that is known; it deliberately does not guess a
// scheme, and nothing in R3 calls it yet.
public interface IElectronicDocumentProviderAuthenticator
{
    Task<IReadOnlyDictionary<string, string>> GetAuthenticationHeadersAsync(CancellationToken ct = default);
}

// §18/§59/§66: the placeholder for "a real HTTP-based entegratör, selected but not yet implemented".
// Never silently succeeds and never silently falls back to Development behavior - every call is an
// explicit, permanent (IsTransientFailure=false) configuration failure, so ElectronicDocumentOutboxService
// dead-letters it immediately instead of retrying forever against a provider that does not exist.
public sealed class UnconfiguredElectronicDocumentProvider : IElectronicDocumentProvider, IElectronicDocumentHealthCheckCapable
{
    public const string ErrorCode = "ProviderNotConfigured";
    public const string ErrorMessage = "Gerçek e-belge entegratörü henüz yapılandırılmadı.";

    public Task<ElectronicDocumentProviderResult> SendAsync(ElectronicDocumentSendRequest request, CancellationToken ct = default) =>
        Task.FromResult(ElectronicDocumentProviderResult.Rejected(ErrorCode, ErrorMessage));

    public Task<ElectronicDocumentStatusQueryResult> QueryStatusAsync(ElectronicDocumentStatusQueryRequest request, CancellationToken ct = default) =>
        Task.FromResult(ElectronicDocumentStatusQueryResult.PermanentFailure(ErrorCode, ErrorMessage));

    public Task<ElectronicDocumentProviderHealthResult> CheckHealthAsync(CancellationToken ct = default) =>
        Task.FromResult(new ElectronicDocumentProviderHealthResult(false, ErrorMessage, null));
}

// §38/§70: the one place `ElectronicDocumentProviderKind` turns into an actual IElectronicDocumentProvider
// instance - so selecting Development vs a real provider is always an explicit configuration choice
// (spec §20), never something a dispatcher/ViewModel decides on its own.
public static class ElectronicDocumentProviderFactory
{
    public static IElectronicDocumentProvider Create(ElectronicDocumentProviderConfiguration configuration)
    {
        var validation = ElectronicDocumentProviderConfigurationValidator.Validate(configuration);
        if (!validation.IsValid)
            throw new InvalidOperationException("E-belge sağlayıcı yapılandırması geçersiz: " + string.Join(" ", validation.Errors));

        return configuration.Kind switch
        {
            ElectronicDocumentProviderKind.Development => new DevelopmentElectronicDocumentProvider(),
            ElectronicDocumentProviderKind.Http => new UnconfiguredElectronicDocumentProvider(),
            _ => throw new InvalidOperationException($"Bilinmeyen e-belge sağlayıcı türü: {configuration.Kind}")
        };
    }
}
