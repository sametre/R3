namespace R3.Desktop.Tests;

public sealed class DesktopMenuTests
{
    [Fact]
    public void MainModulesKeepTheirOriginalOrder()
    {
        Assert.Equal(new[] { "Mağaza", "Muhasebe", "Stok", "Satınalma", "Satış", "Kasa-Banka", "Çek-Senet", "Araçlar", "Asb Tools", "Kapat" },
            DesktopMenuCatalog.Create().Select(x => x.Text));
    }

    [Theory]
    [InlineData("Satış", 94)]
    [InlineData("Kasa-Banka", 32)]
    [InlineData("Çek-Senet", 39)]
    [InlineData("Araçlar", 169)]
    [InlineData("Asb Tools", 61)]
    public void ContinuationModulesContainAllSuppliedEntries(string module, int count)
    {
        var root = DesktopMenuCatalog.Create().Single(x => x.Text == module);
        Assert.Equal(count, Flatten(root.Children).Count());
    }

    [Theory]
    [InlineData("Satış > Fatura > Perakende - Fatura-Tahsilat-Transferleri > Transfer Tarihleri Değiştir")]
    [InlineData("Satış > Prim Sistemi > Tanımlar > Parametreler")]
    [InlineData("Kasa-Banka > Banka Kredileri > Kredi Ödeme İşlemleri > Ödenecek Kredi Taksitleri")]
    [InlineData("Çek-Senet > Borç Senetleri > Verilen Borç Senetleri Listesi")]
    [InlineData("Araçlar > Design Reports > Stok > Kamyon Planlama Belgesi > Ürün Toplama Belgesi")]
    [InlineData("Araçlar > Yetkilendirme > Yetkilendirme - Yetki Tanımı -> Kullanıcı")]
    [InlineData("Asb Tools > Import Xml File > Import General Asb EIhracat.xslt")]
    [InlineData("Asb Tools > Bordro > Bordro Entegrasyon Kayıtlarını Önceki Yıldan Kopyala")]
    public void DeepEntriesRemainUnderTheirCorrectParents(string path)
    {
        Assert.Empty(Find(DesktopMenuCatalog.Create(), path).Children);
    }

    [Fact]
    public void UnspecifiedSubmenusRemainGroupsWithoutInventedChildren()
    {
        var item = Find(DesktopMenuCatalog.Create(), "Araçlar > Data Import");
        Assert.True(item.IsGroup);
        Assert.Empty(item.Children);
    }

    [Fact]
    public void BindingDoesNotConfuseReportTemplatesWithOperationalScreens()
    {
        var invoked = false;
        var actions = new Dictionary<string, (Action Click, string Permission)>
        {
            ["Muhasebe > Cari Hesap Ekstresi"] = (() => invoked = true, "accounts.statement.view")
        };
        var catalog = DesktopMenuCatalog.Create();
        var bound = DesktopMenuBinding.Bind(catalog, actions, _ => true);
        Find(bound, "Muhasebe > Cari Hesap Ekstresi").Click!();
        Assert.True(invoked);
        Assert.Null(Find(bound, "Araçlar > Design Reports > Mağaza > Cari Hesap Ekstresi").Click);
        Assert.Null(Find(catalog, "Muhasebe > Cari Hesap Ekstresi").Click);
        Assert.Equal(Flatten(catalog).Count(), Flatten(bound).Count());
    }

    [Fact]
    public void DeniedPermissionDoesNotBindTheAction()
    {
        var actions = new Dictionary<string, (Action Click, string Permission)>
        {
            ["Kasa-Banka > Kasa İşlemleri > Kasa Ekstresi"] = (() => throw new InvalidOperationException(), "cash.statement.view")
        };
        var bound = DesktopMenuBinding.Bind(DesktopMenuCatalog.Create(), actions, _ => false);
        Assert.Null(Find(bound, "Kasa-Banka > Kasa İşlemleri > Kasa Ekstresi").Click);
    }

    private static DesktopMenuEntry Find(DesktopMenuEntry[] entries, string path)
    {
        DesktopMenuEntry? found = null;
        foreach (var part in path.Split(" > "))
        {
            found = entries.Single(x => x.Text == part);
            entries = found.Children;
        }
        return found!;
    }

    private static IEnumerable<DesktopMenuEntry> Flatten(DesktopMenuEntry[] entries) =>
        entries.SelectMany(x => new[] { x }.Concat(Flatten(x.Children)));
}
