using R3.Infrastructure;

namespace R3.Domain.Tests;

/// <summary>Kullanıcı ve Yetkiler engine (LocalUserAdminService + LocalPermissionService) against a
/// throwaway SQLite file per test, never the shared app database.</summary>
public sealed class UserAdminServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-tests-" + Guid.NewGuid());
    private StoreDatabase Create() => new(Path.Combine(_folder, "test.db"));
    private static string AdminId(StoreDatabase db) => db.Query("SELECT id FROM users WHERE username='admin'").Rows[0][0].ToString()!;
    private static string RoleId(StoreDatabase db, string code) => db.Query("SELECT id FROM roles WHERE code=$c", ("$c", code)).Rows[0][0].ToString()!;

    [Fact]
    public void CreatedUserCanSignInAndPasswordResetReplacesTheOldPassword()
    {
        var db = Create(); var users = new LocalUserAdminService(db);
        var id = users.SaveUser(new UserAccountEdit("", "ayse.d", "Ayşe Durgut", "ayse@example.com", "CASHIER"), "kasa123", "admin");

        Assert.Equal("Ayşe Durgut", db.Authenticate("ayse.d", "kasa123"));
        Assert.Equal("CASHIER", db.GetUserRole("ayse.d").Code);
        Assert.NotNull(db.Query("SELECT last_login_at FROM users WHERE id=$id", ("$id", id)).Rows[0][0] as string);

        users.ResetPassword(id, "yeniSifre9", "admin");
        Assert.Null(db.Authenticate("ayse.d", "kasa123"));
        Assert.Equal("Ayşe Durgut", db.Authenticate("ayse.d", "yeniSifre9"));
    }

    [Fact]
    public void DeactivatedUserCannotSignIn()
    {
        var db = Create(); var users = new LocalUserAdminService(db);
        var id = users.SaveUser(new UserAccountEdit("", "depo1", "Depo Sorumlusu", "", "USER"), "depo123", "admin");
        users.SetUserActive(id, false, "admin");
        Assert.Null(db.Authenticate("depo1", "depo123"));
    }

    [Theory]
    [InlineData("a", "Ad", "sifre12")]          // username too short
    [InlineData("ali veli", "Ad", "sifre12")]   // space in username
    [InlineData("ali", "", "sifre12")]          // display name missing
    [InlineData("ali", "Ad", "12345")]          // password too short
    public void InvalidNewUserIsRejected(string userName, string displayName, string password)
    {
        var users = new LocalUserAdminService(Create());
        Assert.Throws<ArgumentException>(() => users.SaveUser(new UserAccountEdit("", userName, displayName, "", "USER"), password, "admin"));
    }

    [Fact]
    public void DuplicateUserNameIsRejectedCaseInsensitively()
    {
        var users = new LocalUserAdminService(Create());
        Assert.Throws<ArgumentException>(() => users.SaveUser(new UserAccountEdit("", "ADMIN", "İkinci", "", "USER"), "sifre12", "admin"));
    }

    [Fact]
    public void LastActiveAdministratorCannotBeDemotedOrDeactivated()
    {
        var db = Create(); var users = new LocalUserAdminService(db); var admin = users.GetUser(AdminId(db))!;

        Assert.Throws<InvalidOperationException>(() => users.SaveUser(admin with { RoleCode = "USER" }, null, "someone"));
        Assert.Throws<InvalidOperationException>(() => users.SetUserActive(admin.Id, false, "someone"));
        Assert.Equal("ADMIN", db.GetUserRole("admin").Code); // rolled back

        // With a second administrator the first one may be demoted.
        users.SaveUser(new UserAccountEdit("", "yonetici2", "İkinci Yönetici", "", "ADMIN"), "sifre12", "admin");
        users.SaveUser(admin with { RoleCode = "USER" }, null, "yonetici2");
        Assert.Equal("USER", db.GetUserRole("admin").Code);
    }

    [Fact]
    public void SignedInUserCannotDeactivateThemselves()
    {
        var db = Create(); var users = new LocalUserAdminService(db);
        users.SaveUser(new UserAccountEdit("", "yonetici2", "İkinci Yönetici", "", "ADMIN"), "sifre12", "admin");
        Assert.Throws<InvalidOperationException>(() => users.SetUserActive(AdminId(db), false, "admin"));
    }

    [Fact]
    public void DefaultWarehouseMustBelongToDefaultBranch()
    {
        var db = Create(); var users = new LocalUserAdminService(db);
        var company = db.Query("SELECT id FROM companies LIMIT 1").Rows[0][0].ToString()!;
        var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!;
        var warehouse = db.Query("SELECT id FROM warehouses WHERE branch_id=$b LIMIT 1", ("$b", branch)).Rows[0][0].ToString()!;
        db.Execute("INSERT INTO branches(id,company_id,code,name,is_active,created_at,updated_at) VALUES('b2',$c,'SUBE2','Şube 2',1,datetime('now'),datetime('now'))", ("$c", company));

        Assert.Throws<ArgumentException>(() => users.SaveUser(new UserAccountEdit("", "kasa2", "Kasa", "", "USER", "b2", warehouse), "sifre12", "admin"));
        var id = users.SaveUser(new UserAccountEdit("", "kasa2", "Kasa", "", "USER", branch, warehouse), "sifre12", "admin");
        Assert.Equal(warehouse, users.GetUser(id)!.DefaultWarehouseId);
    }

    [Fact]
    public void RolePermissionsApplyOnlyOnceTheRoleIsConfigured()
    {
        var db = Create(); var users = new LocalUserAdminService(db);
        users.SaveUser(new UserAccountEdit("", "satis1", "Satış", "", "USER"), "sifre12", "admin");

        // Never configured -> legacy "allow everything" bootstrap.
        Assert.True(new LocalPermissionService(db, "satis1").HasPermission("cash.view"));

        users.SaveRolePermissions(RoleId(db, "USER"), ["accounts.view", "invoices.view"], "admin");
        var permissions = new LocalPermissionService(db, "satis1");
        Assert.True(permissions.HasPermission("accounts.view"));
        Assert.False(permissions.HasPermission("cash.view"));

        // Configured with nothing really means nothing (previously indistinguishable from "never configured").
        users.SaveRolePermissions(RoleId(db, "USER"), [], "admin");
        Assert.False(new LocalPermissionService(db, "satis1").HasPermission("accounts.view"));
    }

    [Fact]
    public void AdministratorRoleAlwaysHasEveryPermissionAndCannotBeEdited()
    {
        var db = Create(); var users = new LocalUserAdminService(db);
        users.SaveUser(new UserAccountEdit("", "yonetici2", "İkinci Yönetici", "", "ADMIN"), "sifre12", "admin");
        users.SaveRolePermissions(RoleId(db, "USER"), ["accounts.view"], "admin");

        Assert.True(new LocalPermissionService(db, "yonetici2").HasPermission("cash.view"));
        Assert.Throws<InvalidOperationException>(() => users.SaveRolePermissions(RoleId(db, "ADMIN"), [], "admin"));
        Assert.Throws<InvalidOperationException>(() => users.SaveRole(new RoleEdit(RoleId(db, "ADMIN"), "ADMIN", "Yönetici", "", false), "admin"));
    }

    [Fact]
    public void RemovedCashierGrantIsNotReseededOnNextLogin()
    {
        var db = Create(); var users = new LocalUserAdminService(db);
        users.SaveUser(new UserAccountEdit("", "kasa1", "Kasiyer", "", "CASHIER"), "sifre12", "admin");
        Assert.True(new LocalPermissionService(db, "kasa1").HasPermission("cash.transfer.create")); // seeded default

        var kept = users.GetRolePermissions(RoleId(db, "CASHIER")).Where(p => p.IsAllowed && p.Key != "cash.transfer.create").Select(p => p.Key).ToList();
        users.SaveRolePermissions(RoleId(db, "CASHIER"), kept, "admin");

        Assert.False(new LocalPermissionService(db, "kasa1").HasPermission("cash.transfer.create"));
        Assert.False(new LocalPermissionService(db, "kasa1").HasPermission("cash.transfer.create"));
    }

    [Fact]
    public void CustomRoleCanBeCreatedAssignedAndSystemRoleCodesAreLocked()
    {
        var db = Create(); var users = new LocalUserAdminService(db);
        var roleId = users.SaveRole(new RoleEdit("", "satis", "Satış Temsilcisi"), "admin");
        Assert.Equal("SATIS", db.Query("SELECT code FROM roles WHERE id=$id", ("$id", roleId)).Rows[0][0]);
        users.SaveUser(new UserAccountEdit("", "temsilci", "Temsilci", "", "SATIS"), "sifre12", "admin");
        Assert.Equal("SATIS", db.GetUserRole("temsilci").Code);
        // A brand-new role starts configured: no grants means no access until the administrator ticks some.
        Assert.False(new LocalPermissionService(db, "temsilci").HasPermission("accounts.view"));

        Assert.Throws<InvalidOperationException>(() => users.SaveRole(new RoleEdit(RoleId(db, "CASHIER"), "KASA", "Kasa"), "admin"));
        Assert.Throws<ArgumentException>(() => users.SaveRole(new RoleEdit("", "SATIS", "Kopya"), "admin"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }
}
