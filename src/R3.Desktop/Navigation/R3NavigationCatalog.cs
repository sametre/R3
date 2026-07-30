using R3.Desktop.Controls;

namespace R3.Desktop.Navigation;

internal sealed record R3NavigationItem(
    string Key,
    string Title,
    R3Glyph Glyph,
    IReadOnlyList<R3NavigationItem>? Children = null)
{
    public bool HasChildren => Children is { Count: > 0 };
}

internal static class R3NavigationCatalog
{
    public static IReadOnlyList<R3NavigationItem> Items { get; } =
    [
        Item("dashboard", "Gösterge Paneli", R3Glyph.Desktop),
        Group("sales", "Satış", R3Glyph.Invoice,
            Item("quick-sale", "Hızlı Satış", R3Glyph.Invoice),
            Item("sales-invoices", "Satış Faturaları", R3Glyph.Invoice),
            Item("sales-dispatches", "Satış İrsaliyeleri", R3Glyph.Invoice),
            Item("sales-returns", "Satış İadeleri", R3Glyph.Invoice),
            Item("exchanges", "Değişim İşlemleri", R3Glyph.Invoice),
            Item("sales-quotes", "Teklifler", R3Glyph.Invoice),
            Item("sales-orders", "Siparişler", R3Glyph.Invoice)),
        Group("purchasing", "Satın Alma", R3Glyph.Inventory,
            Item("purchase-requests", "Satın Alma Talepleri", R3Glyph.Inventory),
            Item("purchase-quotes", "Teklifler", R3Glyph.Inventory),
            Item("purchase-orders", "Siparişler", R3Glyph.Inventory),
            Item("goods-receipts", "Mal Kabul", R3Glyph.Inventory),
            Item("purchase-invoice", "Alış Faturaları", R3Glyph.Invoice),
            Item("purchase-returns", "Alış İadeleri", R3Glyph.Invoice)),
        Group("inventory", "Stok", R3Glyph.Inventory,
            Item("products", "Ürünler", R3Glyph.Inventory),
            Item("variants", "Varyantlar", R3Glyph.Inventory),
            Item("barcodes", "Barkodlar", R3Glyph.Inventory),
            Item("categories", "Kategoriler", R3Glyph.Inventory),
            Item("brands", "Markalar", R3Glyph.Inventory),
            Item("units", "Birimler", R3Glyph.Inventory),
            Item("warehouses", "Depolar", R3Glyph.Inventory),
            Item("stock-movements", "Stok Hareketleri", R3Glyph.Inventory),
            Item("warehouse-transfers", "Depo Transferleri", R3Glyph.Inventory),
            Item("stock-counts", "Sayım", R3Glyph.Inventory),
            Item("waste-loss", "Fire ve Zayi", R3Glyph.Inventory),
            Item("price-lists", "Fiyat Listeleri", R3Glyph.Inventory)),
        Group("accounts", "Cari", R3Glyph.Account,
            Item("customers", "Müşteriler", R3Glyph.Account),
            Item("suppliers", "Tedarikçiler", R3Glyph.Account),
            Item("account-movements", "Cari Hareketler", R3Glyph.Account),
            Item("collections", "Tahsilatlar", R3Glyph.Finance),
            Item("payments", "Ödemeler", R3Glyph.Finance),
            Item("reconciliation", "Mutabakat", R3Glyph.Account)),
        Group("finance", "Finans", R3Glyph.Finance,
            Item("cash-accounts", "Kasalar", R3Glyph.Finance),
            Item("banks", "Bankalar", R3Glyph.Finance),
            Item("pos-accounts", "POS Hesapları", R3Glyph.Finance),
            Item("cheques", "Çekler", R3Glyph.Finance),
            Item("promissory-notes", "Senetler", R3Glyph.Finance),
            Item("loans", "Krediler", R3Glyph.Finance),
            Item("cash-flow", "Nakit Akışı", R3Glyph.Finance)),
        Group("retail", "Mağazacılık", R3Glyph.Desktop,
            Item("stores", "Mağazalar", R3Glyph.Desktop),
            Item("retail-registers", "Kasalar", R3Glyph.Finance),
            Item("shifts", "Vardiyalar", R3Glyph.Desktop),
            Item("campaigns", "Kampanyalar", R3Glyph.Invoice),
            Item("gift-vouchers", "Hediye Çekleri", R3Glyph.Invoice),
            Item("loyalty-points", "Müşteri Puanları", R3Glyph.Account),
            Item("register-closures", "Kasa Kapanışları", R3Glyph.Finance)),
        Group("e-transformation", "E-Dönüşüm", R3Glyph.Invoice,
            Item("e-invoice", "e-Fatura", R3Glyph.Invoice),
            Item("e-archive", "e-Arşiv", R3Glyph.Invoice),
            Item("e-dispatch", "e-İrsaliye", R3Glyph.Invoice),
            Item("gib-query", "GİB Sorgulama", R3Glyph.Invoice),
            Item("incoming-documents", "Gelen Belgeler", R3Glyph.Invoice)),
        Group("accounting", "Muhasebe", R3Glyph.Reports,
            Item("chart-of-accounts", "Hesap Planı", R3Glyph.Reports),
            Item("accounting-vouchers", "Muhasebe Fişleri", R3Glyph.Reports),
            Item("cost-centers", "Masraf Merkezleri", R3Glyph.Reports),
            Item("integration-pool", "Entegrasyon Havuzu", R3Glyph.Reports),
            Item("financial-reports", "Mali Raporlar", R3Glyph.Reports)),
        Item("reports", "Raporlar", R3Glyph.Reports),
        Group("manager", "Yönetici", R3Glyph.Desktop,
            Item("manager-dashboard", "Yönetici Paneli", R3Glyph.Desktop),
            Item("manager-approvals", "Görev ve Onaylar", R3Glyph.Invoice),
            Item("manager-alerts", "Kritik Uyarılar", R3Glyph.Reports),
            Item("manager-stores", "Mağaza İzleme", R3Glyph.Desktop),
            Item("manager-devices", "Cihaz ve Entegrasyon", R3Glyph.Finance),
            Item("manager-audit", "Denetim Kayıtları", R3Glyph.Reports))
    ];

    public static R3NavigationItem? Find(string key)
    {
        foreach (R3NavigationItem item in Items)
        {
            if (string.Equals(item.Key, key, StringComparison.Ordinal))
                return item;

            R3NavigationItem? child = item.Children?.FirstOrDefault(
                candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal));
            if (child is not null)
                return child;
        }
        return null;
    }

    private static R3NavigationItem Item(string key, string title, R3Glyph glyph) =>
        new(key, title, glyph);

    private static R3NavigationItem Group(
        string key,
        string title,
        R3Glyph glyph,
        params R3NavigationItem[] children) =>
        new(key, title, glyph, children);
}
