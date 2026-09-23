using R3.Desktop.WinForms.Features.Products;
using R3.Infrastructure;

namespace R3.Desktop.WinForms.Tests;

public sealed class ProductEditModelTests
{
    private static ProductAggregateEdit Loaded() => new(
        "p1", "c1", "A-1", "Ürün", "b1", "k1", "u1", "Stock", 20, true,
        [new ProductChildEdit("v1", "V1", "Varyant")],
        [new ProductChildEdit("bc1", "", "", Barcode: "111", UnitId: "u1", IsPrimary: true), new ProductChildEdit("bc2", "", "", Barcode: "222", UnitId: "u1")],
        MinimumStock: 5, ImagePath: "img.png",
        Units: [new ProductUnitEdit("pu1", "u1", 1, 1, true)],
        Suppliers: [new ProductSupplierEdit("s1", "acc1", "SUP-1")],
        Policy: new ProductInventoryPolicyEdit(2, 5, "Lot", 3, "Raf"))
    { ProductGroupId = "g1", OriginCountryId = "tr", WarehousePolicies = [new ProductWarehousePolicyEdit("w1", 1, 9)] };

    [Fact]
    public void RoundTripWithoutChangesKeepsEverything()
    {
        var loaded = Loaded();
        var saved = ProductEditModel.From(loaded).ApplyTo(loaded);
        Assert.Equal(loaded.Code, saved.Code);
        Assert.Same(loaded.Variants, saved.Variants);
        Assert.Same(loaded.Units, saved.Units);
        Assert.Same(loaded.Suppliers, saved.Suppliers);
        Assert.Same(loaded.WarehousePolicies, saved.WarehousePolicies);
        Assert.Equal(loaded.ImagePath, saved.ImagePath);
        Assert.Equal(loaded.Policy, saved.Policy);
        Assert.Equal(loaded.Barcodes, saved.Barcodes);
        Assert.Equal(("g1", "tr"), (saved.ProductGroupId, saved.OriginCountryId));
    }

    [Fact]
    public void EditedFieldsAreAppliedAndOthersPreserved()
    {
        var loaded = Loaded();
        var model = ProductEditModel.From(loaded) with { Name = "  Yeni ad ", MinimumStock = 7, MaximumStock = 20, LotTrackingType = "Serial", BrandId = "" };
        var saved = model.ApplyTo(loaded);
        Assert.Equal("Yeni ad", saved.Name);
        Assert.Equal((7m, 20m, ""), (saved.MinimumStock, saved.MaximumStock, saved.BrandId));
        Assert.Equal(new ProductInventoryPolicyEdit(2, 5, "Serial", 3, "Raf"), saved.Policy);
        Assert.Same(loaded.Suppliers, saved.Suppliers);
    }

    [Fact]
    public void PrimaryBarcodeExistingValueBecomesPrimary()
    {
        var result = ProductEditModel.WithPrimaryBarcode(Loaded().Barcodes, "222", "u1");
        Assert.Equal([false, true], result.Select(b => b.IsPrimary));
        Assert.Equal(["111", "222"], result.Select(b => b.Barcode));
    }

    [Fact]
    public void PrimaryBarcodeNewValueReplacesCurrentPrimary()
    {
        var result = ProductEditModel.WithPrimaryBarcode(Loaded().Barcodes, "333", "u2");
        Assert.Equal(("bc1", "333", "u2", true), (result[0].Id, result[0].Barcode, result[0].UnitId, result[0].IsPrimary));
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void PrimaryBarcodeInsertedFirstWhenNoPrimary()
    {
        var result = ProductEditModel.WithPrimaryBarcode([new ProductChildEdit("bc2", "", "", Barcode: "222")], "444", "u1");
        Assert.Equal(("", "444", true), (result[0].Id, result[0].Barcode, result[0].IsPrimary));
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void BlankPrimaryBarcodeLeavesBarcodesUnchanged() =>
        Assert.Equal(Loaded().Barcodes, ProductEditModel.WithPrimaryBarcode(Loaded().Barcodes, "  ", "u1"));

    [Fact]
    public void ValidationMatchesWpfRules()
    {
        var valid = ProductEditModel.From(Loaded());
        Assert.Empty(valid.Validate());
        Assert.Contains("Ürün kodu ve adı zorunludur.", (valid with { Code = " " }).Validate());
        Assert.Contains("Temel birim seçin.", (valid with { UnitId = "" }).Validate());
        Assert.Contains("Maksimum stok, minimum stoktan küçük olamaz.", (valid with { MinimumStock = 10, MaximumStock = 5 }).Validate());
        Assert.Contains("Maksimum teslim süresi, satış teslim süresinden küçük olamaz.", (valid with { DeliveryLeadTimeDays = 10, MaximumDeliveryLeadTimeDays = 3 }).Validate());
        Assert.Contains("Vergi oranları 0 ile 100 arasında olmalıdır.", (valid with { VatRate = 120 }).Validate());
        Assert.Empty((valid with { MaximumStock = 0, MinimumStock = 10 }).Validate()); // 0 = no maximum
    }

    [Fact]
    public void NewAggregateHasWpfDefaults()
    {
        var blank = ProductEditModel.NewAggregate("c1", "u1");
        Assert.Equal(("", "c1", "u1", "Stock", 20m, 20m), (blank.Id, blank.CompanyId, blank.UnitId, blank.ProductType, blank.VatRate, blank.PurchaseVatRate));
        Assert.True(blank.IsActive && blank.IsSellable && blank.CanQuote);
    }
}
