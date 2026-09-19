using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using R3.Infrastructure;

namespace R3.Domain.Tests;

public sealed class UblInvoiceGeneratorTests : IDisposable
{
    private readonly string _folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "R3-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private StoreDatabase Create() => new(System.IO.Path.Combine(_folder, "test.db"));

    private static (LocalSalesService Sales, LocalElectronicDocumentService Documents, UblInvoiceGenerator Generator, string Branch, string Warehouse, string Unit) Setup(StoreDatabase db)
    {
        var routing = new ElectronicDocumentRoutingService(db);
        var documents = new LocalElectronicDocumentService(db);
        var sales = new LocalSalesService(db, routing, documents);
        var generator = new UblInvoiceGenerator(db, documents);
        var branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!;
        var warehouse = db.Query("SELECT id FROM warehouses LIMIT 1").Rows[0][0].ToString()!;
        var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        return (sales, documents, generator, branch, warehouse, unit);
    }

    private static string SeedAccount(StoreDatabase db, string code, bool eInvoiceRegistered, bool individual = false, string identityNumber = "")
    {
        var account = new LocalAccountService(db);
        var id = Guid.NewGuid().ToString();
        account.Save(new AccountAggregateEdit(
            new AccountEdit(id, Company, code, $"Cari {code}", "Customer", IdentityNumber: identityNumber),
            new AccountTaxProfileEdit(PersonType: individual ? "Individual" : "LegalEntity", LegalTitle: $"{code} Ticaret A.Ş."),
            eInvoiceRegistered ? new AccountEInvoiceProfileEdit(IsEInvoiceEnabled: true, EInvoiceAlias: "urn:mail:test@efatura.gov.tr", InvoiceScenario: "Ticari") : new AccountEInvoiceProfileEdit(),
            new CustomerProfileEdit(), null));
        var addresses = new LocalAccountAddressService(db);
        addresses.Save(new AccountAddressEdit("", id, "Invoice", $"{code} Merkez", "Türkiye", "İstanbul", "Kadıköy", "", "Bağdat Cad. No:1", "34710", "", "", "", "", IsDefault: true));
        return id;
    }

    private static string SeedProduct(StoreDatabase db, string code, string unit)
    {
        var id = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        db.Execute("INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,is_active,created_at,updated_at) VALUES($id,$c,$code,$name,$u,'Stock',20,1,$n,$n)",
            ("$id", id), ("$c", Company), ("$code", code), ("$name", $"Ürün {code}"), ("$u", unit), ("$n", now));
        return id;
    }

    private static void SeedCompanyAddress(StoreDatabase db, LocalElectronicDocumentService documents)
    {
        documents.SaveCompanyProfile(new ElectronicDocumentCompanyProfileEdit(
            Company, "1234567890", "R3 Demo Ticaret A.Ş.", "urn:mail:r3@efatura.gov.tr", "", "", "Test", false, true, "Temel",
            "", "", "", "", "", "", "Örnek Sk. No:5", "İstanbul", "Şişli", "34394", "Türkiye"));
    }

    [Fact]
    public async Task GeneratesValidUblXmlWithRealHeaderLinesAndTotalsFromAPostedInvoice()
    {
        var db = Create(); var (sales, documents, generator, branch, warehouse, unit) = Setup(db);
        SeedCompanyAddress(db, documents);
        var account = SeedAccount(db, "C01", eInvoiceRegistered: true);
        var product = SeedProduct(db, "P01", unit);
        var inventory = new LocalInventoryService(db); await inventory.PostOpeningBalanceAsync(new(Company, branch, warehouse, product, null, 10, null, DateTime.UtcNow));
        var invoiceId = sales.CreateDraft(new("", Company, branch, warehouse, account, DateTime.Today, "", [new(product, null, unit, null, 3, 1, 100, 10, 20)]));
        var result = sales.Post(invoiceId, "test-user");

        var xml = generator.Generate(result.ElectronicDocumentId);
        var doc = XDocument.Parse(xml);
        var root = doc.Root!;

        Assert.Equal(result.DocumentNo, root.Element(Cbc + "ID")!.Value);
        Assert.Equal(documents.Get(result.ElectronicDocumentId)!.Uuid, root.Element(Cbc + "UUID")!.Value);
        Assert.Equal("TICARIFATURA", root.Element(Cbc + "ProfileID")!.Value); // EInvoice + "Ticari" scenario
        Assert.Equal("TRY", root.Element(Cbc + "DocumentCurrencyCode")!.Value);
        Assert.Single(root.Descendants(Cac + "InvoiceLine"));

        var payable = decimal.Parse(root.Descendants(Cbc + "PayableAmount").First().Value, System.Globalization.CultureInfo.InvariantCulture);
        // 3 * 100 = 300 gross, 10% discount = 270 net, 20% KDV = 54 -> 324
        Assert.Equal(324.00m, payable);

        var supplierId = root.Element(Cac + "AccountingSupplierParty")!.Descendants(Cac + "PartyIdentification").First().Element(Cbc + "ID")!;
        Assert.Equal("VKN", supplierId.Attribute("schemeID")!.Value); Assert.Equal("1234567890", supplierId.Value);
        var supplierCity = root.Element(Cac + "AccountingSupplierParty")!.Descendants(Cbc + "CityName").First().Value;
        Assert.Equal("İstanbul", supplierCity);

        var customerId = root.Element(Cac + "AccountingCustomerParty")!.Descendants(Cac + "PartyIdentification").First().Element(Cbc + "ID")!;
        Assert.Equal("VKN", customerId.Attribute("schemeID")!.Value);
        var customerCity = root.Element(Cac + "AccountingCustomerParty")!.Descendants(Cbc + "CityName").First().Value;
        Assert.Equal("İstanbul", customerCity); // from account_addresses, not fabricated

        // The generated content is what actually gets locked in as the immutable payload.
        var payloadId = documents.SavePayload(result.ElectronicDocumentId, ElectronicDocumentPayloadType.UblXml, xml, "application/xml", false, "test-user");
        Assert.NotEmpty(payloadId);
        documents.Generate(result.ElectronicDocumentId, "test-user");
        Assert.Equal(ElectronicDocumentStatus.Generated, documents.Get(result.ElectronicDocumentId)!.Status);
    }

    [Fact]
    public async Task EArchiveInvoiceAlwaysUsesEarsivfaturaProfileRegardlessOfScenario()
    {
        var db = Create(); var (sales, documents, generator, branch, warehouse, unit) = Setup(db);
        SeedCompanyAddress(db, documents);
        var account = SeedAccount(db, "C02", eInvoiceRegistered: false); // not e-Fatura registered -> routes to EArchive
        var product = SeedProduct(db, "P02", unit);
        var inventory = new LocalInventoryService(db); await inventory.PostOpeningBalanceAsync(new(Company, branch, warehouse, product, null, 5, null, DateTime.UtcNow));
        var invoiceId = sales.CreateDraft(new("", Company, branch, warehouse, account, DateTime.Today, "", [new(product, null, unit, null, 1, 1, 50, 0, 20)]));
        var result = sales.Post(invoiceId, "test-user");

        var xml = generator.Generate(result.ElectronicDocumentId);
        Assert.Equal("EARSIVFATURA", XDocument.Parse(xml).Root!.Element(Cbc + "ProfileID")!.Value);
        Assert.Equal(ElectronicDocumentType.EArchiveInvoice, result.ElectronicDocumentType);
    }

    [Fact]
    public async Task IndividualBuyerUsesTcknIdentificationInsteadOfVkn()
    {
        var db = Create(); var (sales, documents, generator, branch, warehouse, unit) = Setup(db);
        SeedCompanyAddress(db, documents);
        var account = SeedAccount(db, "C03", eInvoiceRegistered: false, individual: true, identityNumber: "12345678901");
        var product = SeedProduct(db, "P03", unit);
        var inventory = new LocalInventoryService(db); await inventory.PostOpeningBalanceAsync(new(Company, branch, warehouse, product, null, 5, null, DateTime.UtcNow));
        var invoiceId = sales.CreateDraft(new("", Company, branch, warehouse, account, DateTime.Today, "", [new(product, null, unit, null, 1, 1, 50, 0, 20)]));
        var result = sales.Post(invoiceId, "test-user");

        var xml = generator.Generate(result.ElectronicDocumentId);
        var customerId = XDocument.Parse(xml).Root!.Element(Cac + "AccountingCustomerParty")!.Descendants(Cac + "PartyIdentification").First().Element(Cbc + "ID")!;
        Assert.Equal("TCKN", customerId.Attribute("schemeID")!.Value);
        Assert.Equal("12345678901", customerId.Value);
    }

    [Fact]
    public async Task RegeneratingReturnsTheSameCanonicalUuidEveryTime()
    {
        var db = Create(); var (sales, documents, generator, branch, warehouse, unit) = Setup(db);
        SeedCompanyAddress(db, documents);
        var account = SeedAccount(db, "C04", eInvoiceRegistered: false);
        var product = SeedProduct(db, "P04", unit);
        var inventory = new LocalInventoryService(db); await inventory.PostOpeningBalanceAsync(new(Company, branch, warehouse, product, null, 5, null, DateTime.UtcNow));
        var invoiceId = sales.CreateDraft(new("", Company, branch, warehouse, account, DateTime.Today, "", [new(product, null, unit, null, 1, 1, 50, 0, 20)]));
        var result = sales.Post(invoiceId, "test-user");

        var first = XDocument.Parse(generator.Generate(result.ElectronicDocumentId)).Root!.Element(Cbc + "UUID")!.Value;
        var second = XDocument.Parse(generator.Generate(result.ElectronicDocumentId)).Root!.Element(Cbc + "UUID")!.Value;
        Assert.Equal(first, second);
        Assert.Equal(documents.Get(result.ElectronicDocumentId)!.Uuid, first);
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
}
