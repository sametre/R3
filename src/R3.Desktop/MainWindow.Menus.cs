namespace R3.Desktop;

public partial class MainWindow
{
    private DesktopMenuEntry[] BuildRequestedMenu((string Text, Action Direct, (string Text, Action Click)[] Children)[] existing)
    {
        DesktopMenuEntry Leaf(string text, Action action) => new(text, action, []);
        DesktopMenuEntry[] Existing(string name) => existing.FirstOrDefault(x => x.Text == name).Children?
            .Select(x => Leaf(x.Text, x.Click)).ToArray() ?? [];
        var actions = new Dictionary<string, (Action Click, string Permission)>
        {
            ["Mağaza > Müşteri Cari"] = (OpenCustomerWorkspace, "accounts.view"),
            ["Mağaza > Taksit Tahsilatı (Seri)"] = (OpenCustomerWorkspace, "accounts.receipt.create"),
            ["Mağaza > İleri Teslim Siparişler"] = (OpenFutureDeliveryOrders, "invoices.view"),
            ["Mağaza > Müşteri Kartı"] = (() => OpenCanonicalAccounts("Customer", "Müşteriler"), "accounts.view"),
            ["Muhasebe > Cari Hesap Ekstresi"] = (() => OpenAccountStatement(), "accounts.view"),
            ["Mağaza > Ürün Sor"] = (OpenQuickProductLookup, "inventory.product.view"),
            ["Mağaza > Müşteri/Kefil Sor"] = (() => OpenCanonicalAccounts("Customer", "Müşteri / Kefil Ara"), "accounts.view"),
            ["Mağaza > Kefil"] = (() => OpenCanonicalAccounts("Customer", "Kefil Bilgileri"), "accounts.view"),
            ["Stok > Ürün Sor"] = (OpenQuickProductLookup, "inventory.product.view"),
            ["Stok > Tanımlar > Stok Kartları"] = (OpenProductList, "inventory.product.view"),
            ["Stok > Tanımlar > Pasif Stok Kartları"] = (() => OpenProductList("Pasif Stok Kartları", null, null, false), "inventory.product.view"),
            ["Stok > Tanımlar > Depolar"] = (OpenWarehouseManagement, "inventory.product.view"),
            ["Stok > Tanımlar > Kritik Stok Seviyeleri"] = (() => OpenProductList("Kritik Stoklar", true, null, true), "inventory.product.view"),
            ["Stok > Tanımlar > Stok Grup Kodları"] = (() => OpenMasterCrud("product_groups", "Stok Grubu Tanımları"), "inventory.product.view"),
            ["Stok > Tanımlar > Stok Birimleri"] = (() => OpenMasterCrud("units", "Birim Tanımları"), "inventory.product.view"),
            ["Stok > Tanımlar > Stok Kategori Tanımları"] = (() => OpenMasterCrud("categories", "Kategori Tanımları"), "inventory.product.view"),
            ["Stok > Raporlar > Stok Envanter Raporu"] = (OpenInventoryBalance, "inventory.transaction.view"),
            ["Stok > Raporlar > Depo Stok Hareketleri"] = (OpenInventoryMovements, "inventory.transaction.view"),
            ["Stok > Raporlar > Açık Stok Rezervasyonları"] = (OpenReservations, "inventory.transaction.view"),
            ["Stok > Stok Ekstresi"] = (() => OpenProductLedger(), "inventory.transaction.view"),
            ["Stok > Stok Etiketi"] = (() => OpenLabelPrint(), "inventory.product.view"),
            ["Stok > Fiyat > Fiyatlar"] = (() => OpenProductPrices(), "inventory.product.view"),
            ["Stok > Fiyat > Fiyat Değişiklik Raporu"] = (() => OpenPriceHistory(), "inventory.product.view"),
            ["Satınalma > Satınalma Siparişleri"] = (() => OpenPurchaseDocuments("Order"), "purchasing.document.view"),
            ["Satınalma > Satınalma İrsaliyeleri"] = (OpenPurchaseReceipts, "purchasing.document.view"),
            ["Mağaza > Bekleyen Sevkiyatlar"] = (OpenPendingShipments, "invoices.view"),
            ["Satış > Fatura > Satış Faturaları"] = (OpenSalesList, "invoices.view"),
            ["Satış > Tanımlar > Satış Fiyat Tanımları"] = (OpenPriceLists, "inventory.product.view"),
            ["Satış > Perakende Raporlar > Müşteri Listesi ve Yönetimi"] = (() => OpenCanonicalAccounts("Customer", "Müşteriler"), "accounts.view"),
            ["Kasa-Banka > Banka İşlemleri > Banka Hareketleri"] = (() => OpenBankTransactions(), "cash.transaction.view"),
            ["Kasa-Banka > Kasa İşlemleri > Kasa Hareketleri"] = (() => OpenCashTransactions(), "cash.transaction.view"),
            ["Kasa-Banka > Kasa İşlemleri > Kasa Kodları"] = (OpenCashAccounts, "cash.view"),
            ["Kasa-Banka > Kasa İşlemleri > Kasa Ekstresi"] = (() => OpenCashStatement(), "cash.statement.view"),
            ["Araçlar > Kullanıcı Grupları"] = (OpenUserRoleManagement, "ADMIN"),
            ["Araçlar > Kullanıcılar"] = (OpenUserRoleManagement, "ADMIN"),
            ["Araçlar > Parametreler"] = (OpenGeneralSettings, "ADMIN"),
            ["Araçlar > Şubeler"] = (() => OpenMasterCrud("branches", "Şube Tanımları"), "ADMIN")
        };
        var modules = DesktopMenuBinding.Bind(DesktopMenuCatalog.Create(), actions, permission =>
            permission == "ADMIN"
                ? string.Equals(_startupSession?.RoleCode, "ADMIN", StringComparison.OrdinalIgnoreCase)
                : _permissions == null || _permissions.HasPermission(permission));

        // The source inventory is broader than the screens implemented so far. Keep every
        // permitted entry active: clicking an unfinished one opens its concrete route-plan tab
        // instead of leaving the menu row disabled and unexplained.
        DesktopMenuEntry[] EnablePlannedEntries(DesktopMenuEntry[] entries, string parentPath = "") => entries.Select(entry =>
        {
            var path = parentPath.Length == 0 ? entry.Text : $"{parentPath} > {entry.Text}";
            var children = EnablePlannedEntries(entry.Children, path);
            return entry with
            {
                Children = children,
                Click = children.Length == 0 && entry.Click == null && entry.Text != "Kapat"
                    ? () => OpenModulePlan(path)
                    : entry.Click
            };
        }).ToArray();
        modules = EnablePlannedEntries(modules);
        for (var i = 0; i < modules.Length; i++)
        {
            var module = modules[i];
            // Keep the supplied hierarchy intact. Existing R3 commands remain available
            // in a separate group after the source menu entries.
            var shortcuts = module.Text switch
            {
                "Satış" => Existing("Satış"),
                "Kasa-Banka" => Existing("Finans").Concat(
                    _permissions == null || _permissions.HasAnyPermission("cash.view", "cash.transaction.view")
                        ? new[] { Leaf("Banka Hesapları", OpenBankAccounts), Leaf("Banka Hareketleri", () => OpenBankTransactions()) } : []).ToArray(),
                "Çek-Senet" => _permissions == null || _permissions.HasPermission("instruments.view")
                    ? new[] { Leaf("Çek / Senet Portföyü", OpenCheques) } : [],
                "Araçlar" => Existing("Araçlar").Concat(existing
                    .Where(x => x.Text is "Giriş" or "Cari" or "Raporlar" or "Ayarlar" or "E-Belge")
                    .Select(x => new DesktopMenuEntry(x.Text, x.Direct, Existing(x.Text)))).ToArray(),
                _ => Array.Empty<DesktopMenuEntry>()
            };
            var children = shortcuts.Length == 0 ? module.Children
                : module.Children.Concat([new DesktopMenuEntry("R3 Kısayolları", null, shortcuts, true)]).ToArray();
            modules[i] = module with { Children = children, Click = module.Text == "Kapat" ? Close : module.Click };
        }
        if (string.Equals(_startupSession?.RoleCode, "CASHIER", StringComparison.OrdinalIgnoreCase))
            return [new("Kasa-Banka", null, Existing("Finans")), Leaf("Kapat", Close)];
        var required = new Dictionary<string, string[]>
        {
            ["Mağaza"] = ["accounts.view", "invoices.view"],
            ["Stok"] = ["inventory.product.view", "inventory.transaction.view"],
            ["Satınalma"] = ["purchasing.document.view"],
            ["Satış"] = ["invoices.view", "sales.invoice.post"],
            ["Kasa-Banka"] = ["cash.view", "cash.transaction.view", "cash.statement.view"],
            ["Çek-Senet"] = ["instruments.view"]
        };
        return modules.Where(x => _permissions == null || !required.TryGetValue(x.Text, out var codes) || _permissions.HasAnyPermission(codes)).ToArray();
    }
}
