using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3.Application.Security;
using R3.Desktop.Logging;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

// Phase 10 (§50-53): E-Belge Ayarları. Reads/writes ONLY the already-persisted
// electronic_document_company_profiles.provider_type/environment columns (Phase 6) - no schema
// change. "Bağlantıyı Test Et" builds a configuration from the current form values and asks
// ElectronicDocumentProviderFactory/IElectronicDocumentHealthCheckCapable, never a raw HttpClient
// call from this class.
public sealed partial class ElectronicDocumentProviderSettingsViewModel : ObservableObject
{
    private readonly LocalElectronicDocumentService _documents;
    private readonly IPermissionService _permissions;
    private readonly string _companyId;
    private readonly ILogger<ElectronicDocumentProviderSettingsViewModel> _logger;

    public ElectronicDocumentProviderSettingsViewModel(StoreDatabase database, string companyId, ILogger<ElectronicDocumentProviderSettingsViewModel>? logger = null, IPermissionService? permissions = null)
    {
        _documents = new LocalElectronicDocumentService(database);
        _companyId = companyId;
        _logger = logger ?? DesktopLogging.CreateLogger<ElectronicDocumentProviderSettingsViewModel>();
        _permissions = permissions ?? new LocalPermissionService(database, "");
        Load();
    }

    [ObservableProperty] private string _taxNumber = "";
    [ObservableProperty] private string _legalTitle = "";
    [ObservableProperty] private ElectronicDocumentProviderKind _providerKind = ElectronicDocumentProviderKind.Development;
    [ObservableProperty] private ElectronicDocumentProviderEnvironment _providerEnvironment = ElectronicDocumentProviderEnvironment.Test;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _healthMessage;
    [ObservableProperty] private bool? _lastHealthy;

    public bool IsTestEnvironment => ProviderEnvironment == ElectronicDocumentProviderEnvironment.Test;
    public bool CanEdit => _permissions.HasPermission("edocuments.settings.edit");

    private ElectronicDocumentCompanyProfileEdit _profile = null!;

    private void Load()
    {
        _profile = _documents.GetCompanyProfile(_companyId);
        TaxNumber = _profile.TaxNumber; LegalTitle = _profile.LegalTitle;
        ProviderKind = Enum.TryParse<ElectronicDocumentProviderKind>(_profile.ProviderType, out var kind) ? kind : ElectronicDocumentProviderKind.Development;
        ProviderEnvironment = Enum.TryParse<ElectronicDocumentProviderEnvironment>(_profile.Environment, out var env) ? env : ElectronicDocumentProviderEnvironment.Test;
        OnPropertyChanged(nameof(IsTestEnvironment));
    }

    [RelayCommand]
    private void Save()
    {
        if (!CanEdit) { ErrorMessage = "Bu işlem için yetkiniz bulunmuyor."; return; }
        try
        {
            _documents.SaveCompanyProfile(_profile with { ProviderType = ProviderKind.ToString(), Environment = ProviderEnvironment.ToString() });
            StatusMessage = "Ayarlar kaydedildi."; ErrorMessage = null;
            Load();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Electronic document provider settings save failed. CompanyId={CompanyId}", _companyId);
            ErrorMessage = "Ayarlar kaydedilirken bir hata oluştu.";
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (!CanEdit) { ErrorMessage = "Bu işlem için yetkiniz bulunmuyor."; return; }
        IsBusy = true; HealthMessage = null; LastHealthy = null; ErrorMessage = null;
        try
        {
            var configuration = new ElectronicDocumentProviderConfiguration(ProviderKind, ProviderEnvironment, null, 30, 60, TaxNumber);
            var validation = ElectronicDocumentProviderConfigurationValidator.Validate(configuration);
            if (!validation.IsValid) { HealthMessage = string.Join(" ", validation.Errors); LastHealthy = false; return; }

            var provider = ElectronicDocumentProviderFactory.Create(configuration);
            if (provider is IElectronicDocumentHealthCheckCapable healthCheck)
            {
                var result = await healthCheck.CheckHealthAsync();
                LastHealthy = result.IsHealthy; HealthMessage = result.Message;
            }
            else
            {
                LastHealthy = null; HealthMessage = "Bu sağlayıcı için bağlantı testi desteklenmiyor.";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException)
        {
            LastHealthy = false; HealthMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Electronic document provider connection test failed. CompanyId={CompanyId}", _companyId);
            LastHealthy = false; HealthMessage = "Bağlantı testi tamamlanamadı.";
        }
        finally { IsBusy = false; }
    }
}
