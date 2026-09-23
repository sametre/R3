using R3.Desktop.ViewModels;

namespace R3.Desktop.Presentation;

/// <summary>Stok Kartları / Stok Kartı (ASB düzeni) için sabit seçenek listeleri ve kod → Türkçe etiket eşlemeleri.</summary>
public static class StockCardPresentation
{
    public static IReadOnlyList<Option> ProductTypes { get; } =
        [new("Stock", "Stok"), new("Service", "Hizmet"), new("Bundle", "Takım / Set"), new("RawMaterial", "Hammadde"), new("FinishedGood", "Mamul")];

    /// <summary>ASB "Sevk Yeri Tipi" radyo seçenekleri (product_inventory_policies.shipment_location_type). "" = Belirsiz.</summary>
    public static IReadOnlyList<Option> ShipmentLocations { get; } =
        [new("Branch", "Şube"), new("Headquarters", "Merkez"), new("Supplier", "Tedarikçi"), new("", "Belirsiz")];

    /// <summary>
    /// ASB Satınalma / Satış Sınıfı. Şimdilik ASB ekranında görülen varsayılanlar (kod 0); diğer ASB kodları
    /// (STOKKARTI.STKSTASINIF / STKSTSSINIF) SQL Server'dan doğrulanınca buraya eklenecek.
    /// </summary>
    public static IReadOnlyList<ClassOption> PurchaseClasses { get; } = [new(0, "Stoğa satınalma yapılır. (MIP bağımlı satınalma)")];
    public static IReadOnlyList<ClassOption> SalesClasses { get; } = [new(0, "Sonsuz stoktan satış")];

    public static IReadOnlyList<Option> VariantModes { get; } = [new("None", "Varyant yok"), new("Variants", "Varyantlı")];

    public static string ShipmentLabel(string? code) =>
        string.IsNullOrEmpty(code) ? "" : ShipmentLocations.FirstOrDefault(o => o.Value == code)?.Label ?? code;

    /// <summary>"16.976 adet aktif stok kartı mevcut" (ASB alt bilgi satırı).</summary>
    public static string CountText(int count, bool? activeOnly) =>
        $"{count.ToString("N0", Design.Ui.Turkish)} adet {activeOnly switch { true => "aktif ", false => "pasif ", _ => "" }}stok kartı mevcut";
}

public sealed record ClassOption(int Value, string Label);
