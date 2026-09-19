using System.IO;
using R3.Application.Security;
using R3.Desktop.ViewModels;
using R3.Infrastructure;

namespace R3.Desktop.Tests;

/// <summary>Phase 10 (§50-53): E-Belge Ayarları ViewModel - permission gating and the "Bağlantıyı Test
/// Et" flow, both against the real ElectronicDocumentProviderFactory (no real HTTP call is ever made,
/// since Development/Http-unconfigured are the only two kinds that exist - see
/// docs/architecture/ELECTRONIC-DOCUMENT-PROVIDER.md).</summary>
public sealed class ElectronicDocumentProviderSettingsViewModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-provider-settings-tests-" + Guid.NewGuid());
    private StoreDatabase Create() => new(Path.Combine(_folder, "test.db"));
    private const string Company = "00000000-0000-0000-0000-000000000001";

    private sealed class FakePermissions(bool allowed) : IPermissionService
    {
        public bool HasPermission(string permissionCode) => allowed;
        public bool HasAnyPermission(params string[] permissionCodes) => allowed;
        public bool HasAllPermissions(params string[] permissionCodes) => allowed;
        public void Refresh() { }
    }

    [Fact]
    public void Load_DefaultsToDevelopmentAndTest()
    {
        var db = Create();
        var vm = new ElectronicDocumentProviderSettingsViewModel(db, Company, new CapturingLogger<ElectronicDocumentProviderSettingsViewModel>(), new FakePermissions(true));
        Assert.Equal(ElectronicDocumentProviderKind.Development, vm.ProviderKind);
        Assert.Equal(ElectronicDocumentProviderEnvironment.Test, vm.ProviderEnvironment);
        Assert.True(vm.IsTestEnvironment);
    }

    [Fact]
    public void Save_PersistsProviderKindAndEnvironment_AcrossReload()
    {
        var db = Create();
        var vm = new ElectronicDocumentProviderSettingsViewModel(db, Company, new CapturingLogger<ElectronicDocumentProviderSettingsViewModel>(), new FakePermissions(true));
        vm.ProviderEnvironment = ElectronicDocumentProviderEnvironment.Production;
        vm.ProviderKind = ElectronicDocumentProviderKind.Http;
        vm.SaveCommand.Execute(null);

        var reloaded = new ElectronicDocumentProviderSettingsViewModel(db, Company, new CapturingLogger<ElectronicDocumentProviderSettingsViewModel>(), new FakePermissions(true));
        Assert.Equal(ElectronicDocumentProviderKind.Http, reloaded.ProviderKind);
        Assert.Equal(ElectronicDocumentProviderEnvironment.Production, reloaded.ProviderEnvironment);
        Assert.False(reloaded.IsTestEnvironment);
    }

    [Fact]
    public void Save_WithoutSettingsEditPermission_IsRefusedAndNotPersisted()
    {
        var db = Create();
        var vm = new ElectronicDocumentProviderSettingsViewModel(db, Company, new CapturingLogger<ElectronicDocumentProviderSettingsViewModel>(), new FakePermissions(false));
        vm.ProviderKind = ElectronicDocumentProviderKind.Http;
        vm.SaveCommand.Execute(null);

        Assert.Equal("Bu işlem için yetkiniz bulunmuyor.", vm.ErrorMessage);
        var reloaded = new ElectronicDocumentProviderSettingsViewModel(db, Company, new CapturingLogger<ElectronicDocumentProviderSettingsViewModel>(), new FakePermissions(true));
        Assert.Equal(ElectronicDocumentProviderKind.Development, reloaded.ProviderKind); // unchanged
    }

    [Fact]
    public async Task TestConnection_DevelopmentKind_ReportsHealthy()
    {
        var db = Create();
        var vm = new ElectronicDocumentProviderSettingsViewModel(db, Company, new CapturingLogger<ElectronicDocumentProviderSettingsViewModel>(), new FakePermissions(true));
        vm.TaxNumber = "1234567890";

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.True(vm.LastHealthy);
    }

    [Fact]
    public async Task TestConnection_HttpKindWithoutBaseUrl_ReportsConfigurationError_NeverThrowsToCaller()
    {
        var db = Create();
        var vm = new ElectronicDocumentProviderSettingsViewModel(db, Company, new CapturingLogger<ElectronicDocumentProviderSettingsViewModel>(), new FakePermissions(true));
        vm.ProviderKind = ElectronicDocumentProviderKind.Http;
        vm.TaxNumber = "1234567890";

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(vm.LastHealthy);
        Assert.NotNull(vm.HealthMessage);
    }

    [Fact]
    public async Task TestConnection_WithoutPermission_IsRefused()
    {
        var db = Create();
        var vm = new ElectronicDocumentProviderSettingsViewModel(db, Company, new CapturingLogger<ElectronicDocumentProviderSettingsViewModel>(), new FakePermissions(false));

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal("Bu işlem için yetkiniz bulunmuyor.", vm.ErrorMessage);
        Assert.Null(vm.LastHealthy);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
