namespace R3.Infrastructure;

// Phase 10 (§2/§7/§9/§37). No real özel entegratör was found anywhere in this repository, its
// config files, or its docs (see docs/architecture/ELECTRONIC-DOCUMENT-PROVIDER.md "BLOCKER") - this
// file is deliberately vendor-agnostic: it defines the *shape* a real provider's configuration will
// need, not a specific vendor's fields. `Http` is a placeholder kind for "a real HTTP-based
// entegratör, once one is selected" - not itself a working implementation (see
// ElectronicDocumentProviderFactory).
public enum ElectronicDocumentProviderKind { Development, Http }

public enum ElectronicDocumentProviderEnvironment { Test, Production }

// §7: only non-secret settings live here (secrets go through IElectronicDocumentSecretProvider,
// §8). CompanyIdentifier is deliberately not a new field to persist - it is the company's existing
// VKN/TCKN (ElectronicDocumentCompanyProfileEdit.TaxNumber, Phase 6), reused rather than duplicated.
public sealed record ElectronicDocumentProviderConfiguration(
    ElectronicDocumentProviderKind Kind,
    ElectronicDocumentProviderEnvironment Environment,
    string? BaseUrl,
    int RequestTimeoutSeconds,
    int StatusQueryDelaySeconds,
    string CompanyIdentifier)
{
    // §7: sane generic HTTP-client defaults, not a vendor-specific magic number - safe to ship even
    // though no real provider's documented SLA has been read yet.
    public static ElectronicDocumentProviderConfiguration Default(string companyIdentifier) =>
        new(ElectronicDocumentProviderKind.Development, ElectronicDocumentProviderEnvironment.Test, null, 30, 60, companyIdentifier);
}

public sealed record ElectronicDocumentProviderConfigurationValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ElectronicDocumentProviderConfigurationValidationResult Ok() => new(true, []);
    public static ElectronicDocumentProviderConfigurationValidationResult Invalid(params string[] errors) => new(false, errors);
}

// §9/§37: catches a missing/inconsistent configuration at provider-resolve time with a descriptive
// Turkish error, instead of a NullReferenceException the first time something tries to send.
public static class ElectronicDocumentProviderConfigurationValidator
{
    public static ElectronicDocumentProviderConfigurationValidationResult Validate(ElectronicDocumentProviderConfiguration configuration)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(configuration.CompanyIdentifier)) errors.Add("Firma VKN/TCKN bilgisi eksik.");
        if (configuration.RequestTimeoutSeconds <= 0) errors.Add("İstek zaman aşımı süresi (RequestTimeoutSeconds) 0'dan büyük olmalıdır.");
        if (configuration.StatusQueryDelaySeconds <= 0) errors.Add("Durum sorgu gecikmesi (StatusQueryDelaySeconds) 0'dan büyük olmalıdır.");

        // §9: a Development provider is never allowed to answer for Production - the whole point of
        // that kind is that it only ever exists locally/in tests.
        if (configuration.Kind == ElectronicDocumentProviderKind.Development && configuration.Environment == ElectronicDocumentProviderEnvironment.Production)
            errors.Add("Development sağlayıcısı Production ortamında kullanılamaz.");

        if (configuration.Kind == ElectronicDocumentProviderKind.Http && string.IsNullOrWhiteSpace(configuration.BaseUrl))
            errors.Add("Gerçek entegratör seçili ancak servis adresi (BaseUrl) tanımlanmamış.");

        return errors.Count == 0 ? ElectronicDocumentProviderConfigurationValidationResult.Ok() : ElectronicDocumentProviderConfigurationValidationResult.Invalid([.. errors]);
    }
}
