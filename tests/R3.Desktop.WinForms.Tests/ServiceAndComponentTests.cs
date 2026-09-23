using System.Data;
using R3.Desktop.WinForms.Components.Grids;
using R3.Desktop.WinForms.Design;
using R3.Desktop.WinForms.Features.Products;
using R3.Desktop.WinForms.Infrastructure.State;
using R3.Infrastructure;

namespace R3.Desktop.WinForms.Tests;

public sealed class ServiceAndComponentTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-winforms-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";

    [Fact]
    public void SessionServiceListsCompaniesBranchesAndChecksCredentials()
    {
        var db = new StoreDatabase(Path.Combine(_folder, "session.db"));
        var sessions = new LocalSessionService(db);
        var company = Assert.Single(sessions.Companies(), c => c.Id == Company);
        var branch = sessions.Branches(company.Id).First();
        Assert.False(sessions.SignIn("admin", "yanlış-şifre", branch.Id).Success);
        Assert.Equal("Kullanıcı adı veya şifre hatalı.", sessions.SignIn("admin", "yanlış-şifre", branch.Id).Error);
        Assert.Empty(sessions.Branches("no-such-company"));
    }

    [Fact]
    public void EditModelSavesThroughTheRealServiceWithoutLosingChildren()
    {
        var db = new StoreDatabase(Path.Combine(_folder, "save.db"));
        var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        var products = new LocalProductService(db);
        products.Save(new ProductAggregateEdit("", Company, "WF-1", "WinForms kartı", "", "", unit, "Stock", 20, true, [],
            [new ProductChildEdit("", "", "", Barcode: "8690000099991", UnitId: unit, IsPrimary: true)], MinimumStock: 2));
        var id = products.SearchPage(new ProductListQuery(Company, Code: "WF-1")).Rows.Rows[0]["Id"].ToString()!;
        var loaded = products.GetDetail(id, Company)!.Product;

        var edited = ProductEditModel.From(loaded) with { Name = "Güncellenmiş", MaximumStock = 10, PrimaryBarcode = "8690000099992" };
        Assert.Empty(edited.Validate());
        products.Save(edited.ApplyTo(loaded));

        var reloaded = products.GetDetail(id, Company)!.Product;
        Assert.Equal(("Güncellenmiş", 2m, 10m), (reloaded.Name, reloaded.MinimumStock, reloaded.MaximumStock));
        Assert.Equal("8690000099992", Assert.Single(reloaded.Barcodes, b => b.IsPrimary).Barcode);
    }

    [Fact]
    public void GridNumericColumnsAreRightAlignedTurkishFormatted() => RunSta(() =>
    {
        using var grid = new R3DataGrid();
        var amount = grid.AddNumber("Tutar", "Tutar", 90);
        var money = grid.AddMoney("Fiyat", "Fiyat", 90);
        var text = grid.AddText("Kod", "Kod", 90);
        Assert.Equal(DataGridViewContentAlignment.MiddleRight, amount.DefaultCellStyle.Alignment);
        Assert.Equal(DataGridViewContentAlignment.MiddleLeft, text.DefaultCellStyle.Alignment);
        Assert.Equal("1.500,00", 1500m.ToString(amount.DefaultCellStyle.Format, amount.DefaultCellStyle.FormatProvider));
        Assert.Equal("₺1.500,00", 1500m.ToString(money.DefaultCellStyle.Format, money.DefaultCellStyle.FormatProvider));
        Assert.Equal("-12,50", (-12.5m).ToString(amount.DefaultCellStyle.Format, amount.DefaultCellStyle.FormatProvider));
    });

    [Fact]
    public void GridPreferencesRoundTripColumnLayoutAndDensity() => RunSta(() =>
    {
        UserSettings.Folder = Path.Combine(_folder, "settings");
        using (var grid = new R3DataGrid())
        {
            grid.AddText("A", "A", 80); grid.AddText("B", "B", 80); grid.AddNumber("C", "C", 80);
            var prefs = new GridPreferences(grid, "test.grid"); prefs.Load();
            grid.Columns["B"]!.Visible = false; grid.Columns["C"]!.Width = 140; grid.Density = GridDensity.Comfortable; prefs.PageSize = 200;
            prefs.Save();
        }
        using var again = new R3DataGrid();
        again.AddText("A", "A", 80); again.AddText("B", "B", 80); again.AddNumber("C", "C", 80); again.AddText("New", "New", 80);
        var restored = new GridPreferences(again, "test.grid"); restored.Load();
        Assert.False(again.Columns["B"]!.Visible, File.ReadAllText(Path.Combine(UserSettings.Folder, "grid.test.grid.json")));
        Assert.Equal(140, again.Columns["C"]!.Width);
        Assert.True(again.Columns["New"]!.Visible);                 // a column added after saving keeps its default
        Assert.Equal((GridDensity.Comfortable, 200), (again.Density, restored.PageSize));
        Assert.Equal(AppSizes.GridRowHeight(GridDensity.Comfortable), again.RowTemplate.Height);
    });

    [Fact]
    public void ExcelExportWritesVisibleColumnsWithNumbers()
    {
        var table = new DataTable(); table.Columns.Add("Kod"); table.Columns.Add("Miktar", typeof(decimal));
        table.Rows.Add("A", 1500.5m);
        var path = Path.Combine(_folder, "export.xlsx"); Directory.CreateDirectory(_folder);
        GridExporter.ToExcel([new ExportColumn("Kod", "Stok Kodu", null), new ExportColumn("Miktar", "Miktar", "N2"), new ExportColumn("Yok", "Yok", null)], table, path, "Test");
        using var book = new ClosedXML.Excel.XLWorkbook(path);
        var sheet = book.Worksheet(1);
        Assert.Equal(("Stok Kodu", "Miktar", ""), (sheet.Cell(1, 1).GetString(), sheet.Cell(1, 2).GetString(), sheet.Cell(1, 3).GetString()));
        Assert.Equal(1500.5, sheet.Cell(2, 2).GetDouble());
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }
}
