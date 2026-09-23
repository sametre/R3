using R3.Infrastructure;

namespace R3.Desktop.WinForms.Features.Products;

/// <summary>
/// Editable header fields of a stok kartı. The view edits this; <see cref="ApplyTo"/> writes it onto the loaded
/// aggregate with <c>with</c>, so everything the screen does not show (variants, units, suppliers, warehouse
/// policies, image…) is saved back unchanged. Validation and the primary-barcode rule mirror the WPF ProductDialog.
/// </summary>
public sealed record ProductEditModel(
    string Code, string Name, string ParentCode, string ProductType, string UnitId, string PrimaryBarcode,
    bool IsActive, bool IsSellable, bool CanQuote, bool IsDefinitionComplete, bool AllowFreeIssue, bool IsBundle,
    string BrandId, string CategoryId, string ProductGroupId, string OriginCountryId,
    decimal VatRate, decimal PurchaseVatRate, decimal ExciseRate, decimal ExciseUnitPrice,
    decimal MinimumStock, decimal MaximumStock, decimal MinimumOrderQuantity, decimal OrderMultiple,
    int DeliveryLeadTimeDays, int MaximumDeliveryLeadTimeDays, string LotTrackingType, int PieceCount, string ShipmentLocationType)
{
    public static readonly (string Value, string Text)[] LotTrackingTypes = [("None", "Yok"), ("Lot", "Lot"), ("Serial", "Seri No")];

    public static ProductEditModel From(ProductAggregateEdit p) => new(
        p.Code, p.Name, p.ParentCode, p.ProductType, p.UnitId,
        (p.Barcodes.FirstOrDefault(b => b.IsPrimary && b.IsActive) ?? p.Barcodes.FirstOrDefault(b => b.IsActive))?.Barcode ?? "",
        p.IsActive, p.IsSellable, p.CanQuote, p.IsDefinitionComplete, p.AllowFreeIssue, p.IsBundle,
        p.BrandId, p.CategoryId, p.ProductGroupId ?? "", p.OriginCountryId ?? "",
        p.VatRate, p.PurchaseVatRate, p.ExciseRate, p.ExciseUnitPrice,
        p.MinimumStock, p.MaximumStock, p.MinimumOrderQuantity, p.OrderMultiple,
        p.Policy.DeliveryLeadTimeDays, p.Policy.MaximumDeliveryLeadTimeDays, p.Policy.LotTrackingType, p.Policy.PieceCount, p.Policy.ShipmentLocationType);

    /// <summary>A blank card: Stok, 20% VAT, active and sellable - the WPF defaults.</summary>
    public static ProductAggregateEdit NewAggregate(string companyId, string defaultUnitId) =>
        new("", companyId, "", "", "", "", defaultUnitId, "Stock", 20, true, [], [], PurchaseVatRate: 20, IsSellable: true, CanQuote: true);

    /// <summary>Same checks as ProductDialog.ValidateProduct; the service validates again on save.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Name)) errors.Add("Ürün kodu ve adı zorunludur.");
        if (string.IsNullOrWhiteSpace(UnitId)) errors.Add("Temel birim seçin.");
        if (VatRate is < 0 or > 100 || PurchaseVatRate is < 0 or > 100 || ExciseRate is < 0 or > 100) errors.Add("Vergi oranları 0 ile 100 arasında olmalıdır.");
        if (ExciseUnitPrice < 0) errors.Add("ÖTV birim fiyatı negatif olamaz.");
        if (MinimumStock < 0 || MaximumStock < 0 || MinimumOrderQuantity < 0 || OrderMultiple < 0) errors.Add("Stok ve sipariş değerleri negatif olamaz.");
        if (MaximumStock > 0 && MaximumStock < MinimumStock) errors.Add("Maksimum stok, minimum stoktan küçük olamaz.");
        if (DeliveryLeadTimeDays < 0 || MaximumDeliveryLeadTimeDays < 0 || PieceCount < 0) errors.Add("Teslim süresi ve parça sayısı negatif olamaz.");
        if (MaximumDeliveryLeadTimeDays > 0 && MaximumDeliveryLeadTimeDays < DeliveryLeadTimeDays) errors.Add("Maksimum teslim süresi, satış teslim süresinden küçük olamaz.");
        return errors;
    }

    public ProductAggregateEdit ApplyTo(ProductAggregateEdit loaded) => loaded with
    {
        Code = Code.Trim(), Name = Name.Trim(), ParentCode = ParentCode.Trim(), ProductType = ProductType, UnitId = UnitId,
        IsActive = IsActive, IsSellable = IsSellable, CanQuote = CanQuote, IsDefinitionComplete = IsDefinitionComplete, AllowFreeIssue = AllowFreeIssue, IsBundle = IsBundle,
        BrandId = BrandId, CategoryId = CategoryId, ProductGroupId = ProductGroupId, OriginCountryId = OriginCountryId,
        VatRate = VatRate, PurchaseVatRate = PurchaseVatRate, ExciseRate = ExciseRate, ExciseUnitPrice = ExciseUnitPrice,
        MinimumStock = MinimumStock, MaximumStock = MaximumStock, MinimumOrderQuantity = MinimumOrderQuantity, OrderMultiple = OrderMultiple,
        Policy = loaded.Policy with { DeliveryLeadTimeDays = DeliveryLeadTimeDays, MaximumDeliveryLeadTimeDays = MaximumDeliveryLeadTimeDays, LotTrackingType = LotTrackingType, PieceCount = PieceCount, ShipmentLocationType = ShipmentLocationType.Trim() },
        Barcodes = WithPrimaryBarcode(loaded.Barcodes, PrimaryBarcode, UnitId)
    };

    /// <summary>
    /// "Birincil barkod" quick field (WPF ProductDialog.ToEditModel): an existing barcode becomes primary; otherwise the
    /// current primary is replaced; with no primary a new one is inserted first. Blank = barcodes unchanged.
    /// </summary>
    public static IReadOnlyList<ProductChildEdit> WithPrimaryBarcode(IReadOnlyList<ProductChildEdit> barcodes, string value, string unitId)
    {
        var quick = value.Trim();
        var list = barcodes.ToList();
        if (quick.Length == 0) return list;
        var existing = list.FindIndex(x => string.Equals(x.Barcode, quick, StringComparison.OrdinalIgnoreCase));
        var primary = list.FindIndex(x => x.IsPrimary);
        if (existing >= 0) for (var i = 0; i < list.Count; i++) list[i] = list[i] with { IsPrimary = i == existing };
        else if (primary >= 0) list[primary] = list[primary] with { Barcode = quick, IsPrimary = true, IsActive = true, UnitId = unitId, Quantity = 1 };
        else list.Insert(0, new ProductChildEdit("", "", "", Barcode: quick, UnitId: unitId, Quantity: 1, IsPrimary: true, IsActive: true));
        return list;
    }
}
