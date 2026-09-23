using R3.Infrastructure;

namespace R3.Domain.Tests;

/// <summary>Şube/depo access enforced inside the posting services via StoreDatabase.OperatorUserName.</summary>
public sealed class InventoryAccessGuardTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";

    private sealed record World(StoreDatabase Db, string Branch, string Main, string Other, string Unit, string Product, string User);

    private World Create()
    {
        var db = new StoreDatabase(Path.Combine(_folder, "test.db"));
        var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!;
        var main = db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!;
        db.Execute("INSERT INTO warehouses(id,company_id,branch_id,code,name,warehouse_type,is_active,created_at,updated_at) VALUES('w2',$c,$b,'D2','Depo 2','Main',1,datetime('now'),datetime('now'))", ("$c", Company), ("$b", branch));
        var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        new LocalProductService(db).Save(new ProductAggregateEdit("", Company, "P1", "Ürün", "", "", unit, "Stock", 20, true, [], []));
        var product = db.Query("SELECT id FROM products WHERE code='P1'").Rows[0][0].ToString()!;
        var users = new LocalUserAdminService(db);
        var user = users.SaveUser(new UserAccountEdit("", "depo1", "Depo Görevlisi", "", "USER"), "sifre12", "admin");
        users.SaveAccess(user, [branch], [main], "admin");   // only the main warehouse
        return new World(db, branch, main, "w2", unit, product, user);
    }

    private static Task Receive(World w, string warehouse)
    {
        var docs = new LocalInventoryDocumentService(w.Db);
        var id = docs.CreateDraft(new InventoryDocumentEdit(Company, w.Branch, warehouse, "ManualIn", DateTime.Today, [new InventoryDocumentLineEdit(w.Product, w.Unit, 5, 5)]), "t");
        return docs.ApproveAsync(id, "t");
    }

    [Fact]
    public async Task RestrictedOperatorCanPostOnlyInAllowedWarehouses()
    {
        var w = Create();
        w.Db.OperatorUserName = "depo1";
        await Receive(w, w.Main);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Receive(w, w.Other));
        Assert.Contains("D2", ex.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LocalInventoryService(w.Db).PostManualInAsync(new(Company, w.Branch, w.Other, w.Product, null, 1, null, DateTime.UtcNow)));
        Assert.Throws<InvalidOperationException>(() => new LocalReservationService(w.Db).Create(new ReservationEdit(Company, w.Branch, w.Other, w.Product, null, 1), "t"));
        Assert.Equal(0L, Convert.ToInt64(w.Db.Query("SELECT COUNT(*) FROM inventory_transactions WHERE warehouse_id='w2'").Rows[0][0]));   // nothing leaked
    }

    [Fact]
    public async Task NoOperatorAdministratorsAndUnrestrictedUsersAreNotAffected()
    {
        var w = Create();
        w.Db.OperatorUserName = null; await Receive(w, w.Other);           // tools, tests, migrations
        w.Db.OperatorUserName = "admin"; await Receive(w, w.Other);        // Yönetici
        new LocalUserAdminService(w.Db).SaveAccess(w.User, [], [], "admin");
        w.Db.OperatorUserName = "depo1"; await Receive(w, w.Other);        // access rows removed → unrestricted
    }

    [Fact]
    public async Task BranchOnlyRestrictionAllowsEveryWarehouseOfThatBranch()
    {
        var w = Create();
        new LocalUserAdminService(w.Db).SaveAccess(w.User, [w.Branch], [], "admin");
        w.Db.OperatorUserName = "depo1";
        await Receive(w, w.Other);   // w2 belongs to the allowed şube
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }
}
