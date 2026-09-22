using System.IO;
using Microsoft.Extensions.Logging;
using R3.Desktop.ViewModels;
using R3.Infrastructure;

namespace R3.Desktop.Tests;

/// <summary>
/// The Cari Kartlar (Account) MVVM screen is the reference implementation for
/// Desktop error handling/logging (see docs/LOGGING.md): exceptions never
/// reach the UI unhandled, the user gets a semantic Turkish message instead of
/// a raw exception string, and technical detail is only ever logged - never
/// silently dropped, never duplicated as a raw MessageBox.
/// </summary>
public sealed class AccountLoggingTests
{
    [Fact]
    public async Task RefreshAsync_OnFailure_SetsSemanticStatusMessage_AndLogsErrorWithException()
    {
        var (services, directory) = CreateServices();
        var logger = new CapturingLogger<AccountsViewModel>();
        var viewModel = new AccountsViewModel(services, "test-user", logger);

        BreakDatabaseFile(directory);

        await viewModel.RefreshCommand.ExecuteAsync(null);
        Cleanup(directory);

        Assert.Equal("Cari listesi yüklenirken bir hata oluştu.", viewModel.StatusMessage);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.NotNull(entry.Exception);
    }

    [Fact]
    public void Save_OnDuplicateCode_SetsFriendlyMessage_AndLogsErrorWithSqliteDetail()
    {
        var (services, directory) = CreateServices();
        services.Accounts.Save(new AccountAggregateEdit(new AccountEdit("", services.CompanyId, "DUP01", "Existing"), new AccountTaxProfileEdit(), new AccountEInvoiceProfileEdit(), null, null));
        var logger = new CapturingLogger<AccountEditViewModel>();
        var editViewModel = new AccountEditViewModel(services, "test-user", null, logger);
        editViewModel.Code = "DUP01";
        editViewModel.Name = "Yeni Cari";

        editViewModel.SaveCommand.Execute(null);

        Assert.Equal("Bu cari kodu zaten kullanılıyor.", editViewModel.ErrorMessage);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.NotNull(entry.Exception);

        Cleanup(directory);
    }

    [Fact]
    public void Save_OnValidationFailure_SetsMessage_ButDoesNotLogAsError()
    {
        var (services, directory) = CreateServices();
        var logger = new CapturingLogger<AccountEditViewModel>();
        // Code/Name left blank on purpose -> LocalAccountService.Save rejects with
        // ArgumentException, its own already-friendly Turkish message.
        var editViewModel = new AccountEditViewModel(services, "test-user", null, logger);

        editViewModel.SaveCommand.Execute(null);

        Assert.False(string.IsNullOrWhiteSpace(editViewModel.ErrorMessage));
        Assert.Empty(logger.Entries); // expected input rejection, not a bug - no log noise

        Cleanup(directory);
    }

    private static (AccountServices Services, string Directory) CreateServices()
    {
        var directory = Path.Combine(Path.GetTempPath(), "R3-account-log-test-" + Guid.NewGuid());
        var db = new StoreDatabase(Path.Combine(directory, "test.db"));
        var company = db.Query("SELECT id FROM companies LIMIT 1").Rows[0][0].ToString()!;
        var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!;
        var services = new AccountServices(new LocalAccountService(db), new LocalAccountAddressService(db), new LocalAccountContactService(db), new LocalAccountBankService(db),
            new LocalAccountNoteService(db), new LocalMasterDataService(db), db, company, branch);
        return (services, directory);
    }

    /// <summary>Removes the whole directory (not just the file) so the next
    /// connection open reliably fails with a real SqliteException, instead of
    /// SQLite silently recreating a missing-but-not-missing-parent-directory file.</summary>
    private static void BreakDatabaseFile(string directory)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(directory, recursive: true);
    }

    private static void Cleanup(string directory)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
