using R3.Infrastructure;

namespace R3.Domain.Tests;

public sealed class AccountAddressAndContactTests : IDisposable
{
    private readonly string _folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "R3-address-contact-tests-" + Guid.NewGuid());
    private readonly string _company = "00000000-0000-0000-0000-000000000001";
    private StoreDatabase Create() => new(System.IO.Path.Combine(_folder, "test.db"));

    private (StoreDatabase Db, string AccountId) Seed()
    {
        var db = Create();
        var accounts = new LocalAccountService(db);
        var id = Guid.NewGuid().ToString();
        accounts.Save(new AccountEdit(id, _company, "MUS0001", "Demo Müşteri"));
        return (db, id);
    }

    [Fact]
    public void CreateAccountAddress_Persists()
    {
        var (db, accountId) = Seed();
        var service = new LocalAccountAddressService(db);

        service.Save(new AccountAddressEdit("", accountId, "HeadOffice", "Merkez", "Türkiye", "İstanbul", "Kadıköy", "", "Örnek Cd. No:1", "34000", "", "", "", "", IsDefault: true));

        var rows = service.List(accountId);
        Assert.Single(rows.Rows.Cast<System.Data.DataRow>());
        Assert.Equal("Merkez", rows.Rows[0]["AdresAdi"]);
        Assert.True(Convert.ToBoolean(rows.Rows[0]["Varsayilan"]));
    }

    [Fact]
    public void MultipleShippingAddresses_OnlyOneDefaultPerType()
    {
        var (db, accountId) = Seed();
        var service = new LocalAccountAddressService(db);

        service.Save(new AccountAddressEdit("", accountId, "Shipping", "Şube 1", "Türkiye", "İstanbul", "Kadıköy", "", "Adres 1", "", "", "", "", "", IsDefault: true));
        service.Save(new AccountAddressEdit("", accountId, "Shipping", "Şube 2", "Türkiye", "Ankara", "Çankaya", "", "Adres 2", "", "", "", "", "", IsDefault: true));
        service.Save(new AccountAddressEdit("", accountId, "Shipping", "Şube 3", "Türkiye", "İzmir", "Konak", "", "Adres 3", "", "", "", "", "", IsDefault: false));
        // A different address type's default must not be affected by Shipping defaults.
        service.Save(new AccountAddressEdit("", accountId, "Invoice", "Fatura Adresi", "Türkiye", "İstanbul", "Kadıköy", "", "Fatura", "", "", "", "", "", IsDefault: true));

        var rows = service.List(accountId).Rows.Cast<System.Data.DataRow>().ToList();
        Assert.Equal(4, rows.Count);
        var shippingDefaults = rows.Where(r => r["Tip"].ToString() == "Shipping" && Convert.ToBoolean(r["Varsayilan"])).ToList();
        Assert.Single(shippingDefaults);
        Assert.Equal("Şube 2", shippingDefaults[0]["AdresAdi"]);
        Assert.Single(rows, r => r["Tip"].ToString() == "Invoice" && Convert.ToBoolean(r["Varsayilan"]));
    }

    [Fact]
    public void DeactivatingDefaultAddress_PromotesAnotherActiveAddressOfSameType()
    {
        var (db, accountId) = Seed();
        var service = new LocalAccountAddressService(db);
        var first = Guid.NewGuid().ToString();
        service.Save(new AccountAddressEdit(first, accountId, "Shipping", "Şube 1", "Türkiye", "İstanbul", "", "", "", "", "", "", "", "", IsDefault: true));
        service.Save(new AccountAddressEdit("", accountId, "Shipping", "Şube 2", "Türkiye", "Ankara", "", "", "", "", "", "", "", "", IsDefault: false));

        service.SetActive(first, active: false);

        var rows = service.List(accountId).Rows.Cast<System.Data.DataRow>().ToList();
        var shipping = rows.Where(r => r["Tip"].ToString() == "Shipping").ToList();
        var stillActiveDefault = shipping.Single(r => Convert.ToBoolean(r["Aktif"]));
        Assert.Equal("Şube 2", stillActiveDefault["AdresAdi"]);
        Assert.True(Convert.ToBoolean(stillActiveDefault["Varsayilan"]));
    }

    [Fact]
    public void InvalidAddressType_IsRejected()
    {
        var (db, accountId) = Seed();
        var service = new LocalAccountAddressService(db);
        Assert.Throws<ArgumentException>(() => service.Save(new AccountAddressEdit("", accountId, "NotAType", "X", "", "", "", "", "", "", "", "", "", "", IsDefault: false)));
    }

    [Fact]
    public void AddContact_Persists()
    {
        var (db, accountId) = Seed();
        var service = new LocalAccountContactService(db);

        service.Save(new AccountContactEdit("", accountId, "Ayşe", "Yılmaz", "Muhasebe Müdürü", "Finans", "0212", "0555", "ayse@example.com", IsPrimary: true));

        var rows = service.List(accountId);
        Assert.Single(rows.Rows.Cast<System.Data.DataRow>());
        Assert.Equal("Ayşe Yılmaz", rows.Rows[0]["AdSoyad"]);
        Assert.True(Convert.ToBoolean(rows.Rows[0]["AnaYetkili"]));
    }

    [Fact]
    public void ChangePrimaryContact_ClearsPreviousPrimary()
    {
        var (db, accountId) = Seed();
        var service = new LocalAccountContactService(db);
        var first = Guid.NewGuid().ToString();
        var second = Guid.NewGuid().ToString();
        service.Save(new AccountContactEdit(first, accountId, "Ayşe", "Yılmaz", "", "", "", "", "", IsPrimary: true));
        service.Save(new AccountContactEdit(second, accountId, "Mehmet", "Demir", "", "", "", "", "", IsPrimary: false));

        service.SetPrimary(second);

        var rows = service.List(accountId).Rows.Cast<System.Data.DataRow>().ToList();
        Assert.Single(rows, r => Convert.ToBoolean(r["AnaYetkili"]));
        Assert.Equal("Mehmet Demir", rows.Single(r => Convert.ToBoolean(r["AnaYetkili"]))["AdSoyad"]);
    }

    [Fact]
    public void DeactivatingPrimaryContact_ClearsPrimaryFlag_NoAutoPromotion()
    {
        var (db, accountId) = Seed();
        var service = new LocalAccountContactService(db);
        var first = Guid.NewGuid().ToString();
        service.Save(new AccountContactEdit(first, accountId, "Ayşe", "Yılmaz", "", "", "", "", "", IsPrimary: true));

        service.SetActive(first, active: false);

        var rows = service.List(accountId).Rows.Cast<System.Data.DataRow>().ToList();
        Assert.False(Convert.ToBoolean(rows[0]["Aktif"]));
        Assert.False(Convert.ToBoolean(rows[0]["AnaYetkili"]));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }
}
