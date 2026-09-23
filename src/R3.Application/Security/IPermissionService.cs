namespace R3.Application.Security;

public interface IPermissionService
{
    bool HasPermission(string permissionCode);
    bool HasAnyPermission(params string[] permissionCodes);
    bool HasAllPermissions(params string[] permissionCodes);
    void Refresh();
}

public static class LegacyPermissionMap
{
    public static IReadOnlyDictionary<string, string> Codes { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ISLSTOKKARTIB"] = "inventory.product.view",
        ["ISLSTOKKARTII"] = "inventory.product.create",
        ["ISLSTOKKARTIU"] = "inventory.product.edit",
        ["ISLSTOKKARTID"] = "inventory.product.deactivate",
        ["ISLMGZMUSTERIKARTIB"] = "accounts.view",
        ["ISLMGZMUSTERIKARTII"] = "accounts.create",
        ["ISLMGZMUSTERIKARTIU"] = "accounts.edit",
        ["ISLMGZMUSTERIKARTID"] = "accounts.deactivate",
        ["ISLMGZCARIEKSRE"] = "accounts.statement.view",
        ["ISLMGZKASAHAREKETLERI"] = "cash.transaction.view",
        ["ISLMGZKASARAPORU"] = "cash.statement.view",
        ["ISLMGZKASABAKIYELERI"] = "cash.view",
        ["ISLMGZBEKLEYENSEVKIYATLAR"] = "shipments.view",
        ["ISLMGZGERCEKLESENSEVKIYATLAR"] = "shipments.view",
        ["ISLMGZSEVKIYATIPTAL"] = "shipments.cancel",
        ["ISLMGZSEVKIYATPLANLA"] = "shipments.plan"
    };
}

public static class PermissionCatalog
{
    public static IReadOnlyList<string> All { get; } =
    [
        "accounts.view", "accounts.create", "accounts.edit", "accounts.deactivate", "accounts.transaction.view", "accounts.statement.view", "accounts.receipt.create", "accounts.payment.create", "accounts.credit_limit.change", "accounts.risk.override", "accounts.audit.view",
        "inventory.product.view", "inventory.product.create", "inventory.product.edit", "inventory.product.deactivate", "inventory.product.clone", "inventory.product.merge", "inventory.transaction.view", "inventory.transaction.receive", "inventory.transaction.issue", "inventory.transfer.view", "inventory.transfer.create", "inventory.transfer.approve", "inventory.transfer.pick", "inventory.transfer.ship", "inventory.transfer.cancel", "inventory.count.adjust", "inventory.audit.view",
        "cash.view", "cash.edit", "cash.deactivate", "cash.transaction.view", "cash.transaction.in", "cash.transaction.out", "cash.receipt.create", "cash.payment.create", "cash.transfer.create", "cash.statement.view", "cash.audit.view",
        "instruments.view", "instruments.create", "instruments.edit", "instruments.send_to_bank", "instruments.collect", "instruments.endorse", "instruments.return", "instruments.bounce", "instruments.protest", "instruments.cancel", "instruments.audit.view",
        "orders.view", "orders.edit", "orders.inventory.view", "orders.reserve", "orders.release_reservation", "orders.shipment.create", "orders.change_shipping_address", "orders.change_shipping_date", "orders.purchase_request.create", "orders.transfer_request.create", "orders.cancel", "orders.audit.view",
        "purchasing.document.view", "purchasing.document.create", "purchasing.document.approve", "purchasing.document.receive", "purchasing.invoice.post", "purchasing.document.cancel", "purchasing.audit.view",
        "shipments.view", "shipments.create", "shipments.pick", "shipments.pack", "shipments.plan", "shipments.change_date", "shipments.change_address", "shipments.change_branch", "shipments.change_warehouse", "shipments.document.view", "shipments.ship", "shipments.deliver", "shipments.cancel", "shipments.audit.view",
        "invoices.view", "invoices.account_transaction.view", "invoices.inventory_transaction.view", "invoices.einvoice.view", "invoices.print", "invoices.return.create", "invoices.reverse", "invoices.audit.view",
        "sales.invoice.post",
        "edocuments.view", "edocuments.generate", "edocuments.send", "edocuments.payload.view", "edocuments.invoice.send", "edocuments.archive.send", "edocuments.despatch.send", "edocuments.status.query", "edocuments.retry", "edocuments.cancel", "edocuments.incoming.view", "edocuments.incoming.import", "edocuments.settings.view", "edocuments.settings.edit", "edocuments.outbox.view", "edocuments.errors.view", "edocuments.provider_response.view", "edocuments.audit.view",
        "reports.view", "reports.layout.save", "reports.layout.set_default", "reports.layout.reset", "reports.export", "reports.print"
    ];
}

/// <summary>Turkish display labels for <see cref="PermissionCatalog"/> keys, used by the Kullanıcı ve
/// Yetkiler matrix. Derived per dotted segment rather than one hand-written label per key, so a key
/// added to the catalog later still gets a readable label (falls back to the raw segment).</summary>
public static class PermissionLabels
{
    private static readonly IReadOnlyDictionary<string, string> Modules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["accounts"] = "Cari", ["inventory"] = "Stok", ["cash"] = "Kasa", ["instruments"] = "Çek / Senet", ["orders"] = "Siparişler",
        ["purchasing"] = "Satınalma", ["shipments"] = "Sevkiyat", ["invoices"] = "Faturalar", ["sales"] = "Satış",
        ["edocuments"] = "E-Belge", ["reports"] = "Raporlar"
    };

    private static readonly IReadOnlyDictionary<string, string> Segments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["view"] = "Görüntüle", ["create"] = "Oluştur", ["edit"] = "Düzenle", ["deactivate"] = "Pasife al", ["clone"] = "Kopyala", ["merge"] = "Birleştir",
        ["transaction"] = "Hareket", ["statement"] = "Ekstre", ["receipt"] = "Tahsilat", ["payment"] = "Ödeme", ["credit_limit"] = "Kredi limiti", ["change"] = "Değiştir",
        ["risk"] = "Risk", ["override"] = "Aşım onayı", ["audit"] = "Denetim kaydı", ["product"] = "Ürün kartı", ["receive"] = "Giriş", ["issue"] = "Çıkış",
        ["transfer"] = "Transfer", ["approve"] = "Onayla", ["pick"] = "Topla", ["ship"] = "Sevk et", ["cancel"] = "İptal", ["count"] = "Sayım", ["adjust"] = "Düzeltme",
        ["in"] = "Giriş", ["out"] = "Çıkış", ["send_to_bank"] = "Bankaya ver", ["collect"] = "Tahsil et", ["endorse"] = "Ciro et", ["return"] = "İade", ["bounce"] = "Karşılıksız",
        ["protest"] = "Protesto", ["inventory"] = "Stok", ["reserve"] = "Rezerve et", ["release_reservation"] = "Rezervasyonu kaldır", ["shipment"] = "Sevkiyat",
        ["change_shipping_address"] = "Sevk adresini değiştir", ["change_shipping_date"] = "Sevk tarihini değiştir", ["purchase_request"] = "Satınalma talebi",
        ["transfer_request"] = "Transfer talebi", ["document"] = "Belge", ["invoice"] = "Fatura", ["post"] = "Kesinleştir", ["pack"] = "Paketle", ["plan"] = "Planla",
        ["change_date"] = "Tarih değiştir", ["change_address"] = "Adres değiştir", ["change_branch"] = "Şube değiştir", ["change_warehouse"] = "Depo değiştir",
        ["deliver"] = "Teslim et", ["account_transaction"] = "Cari hareket", ["inventory_transaction"] = "Stok hareketi", ["einvoice"] = "E-Fatura", ["print"] = "Yazdır",
        ["reverse"] = "Ters kayıt", ["generate"] = "Oluştur", ["send"] = "Gönder", ["payload"] = "Belge içeriği", ["archive"] = "E-Arşiv", ["despatch"] = "E-İrsaliye",
        ["status"] = "Durum", ["query"] = "Sorgula", ["retry"] = "Yeniden dene", ["incoming"] = "Gelen belge", ["import"] = "İçe al", ["settings"] = "Ayarlar",
        ["outbox"] = "Gönderim kuyruğu", ["errors"] = "Hatalar", ["provider_response"] = "Entegratör yanıtı", ["layout"] = "Görünüm", ["save"] = "Kaydet",
        ["set_default"] = "Varsayılan yap", ["reset"] = "Sıfırla", ["export"] = "Dışa aktar"
    };

    public static string Module(string permissionKey)
    {
        var module = permissionKey.Split('.')[0];
        return Modules.TryGetValue(module, out var label) ? label : module;
    }

    public static string Describe(string permissionKey)
    {
        var parts = permissionKey.Split('.').Skip(1).Select(x => Segments.TryGetValue(x, out var label) ? label : x).ToArray();
        return parts.Length == 0 ? permissionKey : string.Join(" • ", parts);
    }
}
