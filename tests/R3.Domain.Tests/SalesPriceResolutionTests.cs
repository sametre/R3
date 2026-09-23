using R3.Infrastructure;

namespace R3.Domain.Tests;

/// <summary>LocalPriceService.ResolveSalesPrice: kampanya → cari listesi → müşteri fiyat grubu → varsayılan liste.</summary>
public sealed class SalesPriceResolutionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";
    private static readonly DateTime Day = new(2026, 6, 15);

    private sealed record World(StoreDatabase Db, LocalPriceService Prices, string Product, string Customer, string Group, string DefaultList);

    private World Create()
    {
        var db = new StoreDatabase(Path.Combine(_folder, "test.db"));
        var unit = db.Query("SELECT id FROM units WHERE company_id=$c LIMIT 1", ("$c", Company)).Rows[0][0].ToString()!;
        new LocalProductService(db).Save(new ProductAggregateEdit("", Company, "P1", "Ürün", "", "", unit, "Stock", 20, true, [], []));
        var product = db.Query("SELECT id FROM products WHERE code='P1'").Rows[0][0].ToString()!;
        new LocalMasterDataService(db).Save("account_groups", null, new MasterRecord("BAYI", "Bayiler"), Company);
        var group = db.Query("SELECT id FROM account_groups WHERE code='BAYI'").Rows[0][0].ToString()!;
        var customer = Guid.NewGuid().ToString();
        db.Execute("INSERT INTO accounts(id,company_id,code,name,account_type,is_active,created_at,updated_at,account_group_id) VALUES($id,$c,'C1','Müşteri','Customer',1,$n,$n,$g)",
            ("$id", customer), ("$c", Company), ("$n", DateTime.UtcNow.ToString("O")), ("$g", group));
        var prices = new LocalPriceService(db);
        var defaultList = prices.SavePriceList(new PriceListEdit("", Company, "PSN", "Peşin", Sequence: 1), "t");   // KDV dahil
        prices.SetPrice(Company, defaultList, product, 120, "t");
        return new World(db, prices, product, customer, group, defaultList);
    }

    [Fact]
    public void DefaultListPriceIsConvertedToNetUnitPrice()
    {
        var w = Create();
        var result = w.Prices.ResolveSalesPrice(Company, w.Product, date: Day)!;
        Assert.Equal(100m, result.UnitPrice);            // 120 KDV dahil, %20 KDV
        Assert.Equal(120m, result.ListPrice);
        Assert.StartsWith("Varsayılan satış listesi", result.Source);
    }

    [Fact]
    public void GroupListAndDiscountApplyThenCustomerOwnListWins()
    {
        var w = Create();
        var bayi = w.Prices.SavePriceList(new PriceListEdit("", Company, "BAYI", "Bayi Fiyatı", VatIncluded: false, Sequence: 5), "t");
        w.Prices.SetPrice(Company, bayi, w.Product, 80, "t");
        w.Prices.SaveCustomerPriceGroup(Company, new CustomerPriceGroupEdit(w.Group, bayi, 5), "t");
        var result = w.Prices.ResolveSalesPrice(Company, w.Product, w.Customer, Day)!;
        Assert.Equal((80m, 5m), (result.UnitPrice, result.DiscountRate));
        Assert.Contains("Bayiler", result.Source);

        var own = w.Prices.SavePriceList(new PriceListEdit("", Company, "OZEL", "Özel", VatIncluded: false, Sequence: 9), "t");
        w.Prices.SetPrice(Company, own, w.Product, 70, "t");
        w.Db.Execute("INSERT INTO customer_profiles(account_id,price_list_id) VALUES($a,$l)", ("$a", w.Customer), ("$l", own));
        result = w.Prices.ResolveSalesPrice(Company, w.Product, w.Customer, Day)!;
        Assert.Equal((70m, 5m), (result.UnitPrice, result.DiscountRate));   // group iskonto is still a customer term
        Assert.StartsWith("Cari fiyat listesi", result.Source);
    }

    [Fact]
    public void InDateCampaignWinsAndDoesNotStackTheGroupDiscount()
    {
        var w = Create();
        w.Prices.SaveCustomerPriceGroup(Company, new CustomerPriceGroupEdit(w.Group, null, 10), "t");
        var campaign = w.Prices.SavePriceList(new PriceListEdit("", Company, "YAZ", "Yaz Kampanyası", ValidFrom: new DateTime(2026, 6, 1), ValidTo: new DateTime(2026, 6, 30), IsCampaign: true), "t");
        w.Prices.SetPrice(Company, campaign, w.Product, 96, "t");

        var result = w.Prices.ResolveSalesPrice(Company, w.Product, w.Customer, Day)!;
        Assert.Equal((80m, 0m), (result.UnitPrice, result.DiscountRate));
        Assert.StartsWith("Kampanya", result.Source);

        var after = w.Prices.ResolveSalesPrice(Company, w.Product, w.Customer, new DateTime(2026, 7, 1))!;
        Assert.Equal((100m, 10m), (after.UnitPrice, after.DiscountRate));   // campaign over → default list + group iskonto
    }

    [Fact]
    public void ListsThatDoNotPriceTheProductAreSkippedAndNothingPricedGivesNull()
    {
        var w = Create();
        var empty = w.Prices.SavePriceList(new PriceListEdit("", Company, "BOS", "Boş"), "t");
        w.Db.Execute("INSERT INTO customer_profiles(account_id,price_list_id) VALUES($a,$l)", ("$a", w.Customer), ("$l", empty));
        Assert.StartsWith("Varsayılan", w.Prices.ResolveSalesPrice(Company, w.Product, w.Customer, Day)!.Source);

        w.Prices.SetPrice(Company, w.DefaultList, w.Product, null, "t");
        Assert.Null(w.Prices.ResolveSalesPrice(Company, w.Product, w.Customer, Day));
    }

    [Fact]
    public void CampaignsNeedDatesAndGroupsRejectCampaignOrPurchaseLists()
    {
        var w = Create();
        Assert.Throws<ArgumentException>(() => w.Prices.SavePriceList(new PriceListEdit("", Company, "K", "K", IsCampaign: true), "t"));
        var purchase = w.Prices.SavePriceList(new PriceListEdit("", Company, "ALIS", "Alış", "Purchase"), "t");
        Assert.Throws<ArgumentException>(() => w.Prices.SaveCustomerPriceGroup(Company, new CustomerPriceGroupEdit(w.Group, purchase, 0), "t"));
        Assert.Throws<ArgumentException>(() => w.Prices.SaveCustomerPriceGroup(Company, new CustomerPriceGroupEdit(w.Group, null, 120), "t"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }
}
