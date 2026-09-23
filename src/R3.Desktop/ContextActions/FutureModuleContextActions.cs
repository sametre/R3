using System.Data;

namespace R3.Desktop.ContextActions;

public sealed record ShipmentContextRow(string Id, string Number, string AccountName, string Status);
public sealed record InstrumentContextRow(string Id, string Number, string Status);
public sealed record OrderContextRow(string Id, string Number, string Status, bool HasReservation);
public sealed record TransferContextRow(string Id, string Number, string Status);
public sealed record DespatchContextRow(string Id, string Number, string Status);

public static class FutureModuleContextActions
{
    public static IReadOnlyList<ContextActionDefinition> Shipments(Func<string, object?, Task<ContextActionResult>> execute) =>
    [
        A("shipment.open", "Sevkiyat Detayını Aç", "shipments.view", 10, ContextActionGroup.Primary, execute),
        // Legacy MDF rows can still carry these workflow values. Keep the actions for
        // compatibility, while new R3 rows use Pending -> Planned -> InTransit.
        A("shipment.pick", "Hazırlamaya Başla", "shipments.pick", 5, ContextActionGroup.Operational, execute, x => Is(x, "Waiting")),
        A("shipment.pack", "Paketlemeye Geç", "shipments.pack", 6, ContextActionGroup.Operational, execute, x => Is(x, "Picked")),
        A("shipment.plan", "Sevkiyatı Planla", "shipments.plan", 10, ContextActionGroup.Operational, execute, x => Is(x, "Pending")),
        A("shipment.ship", "Sevkiyatı Gerçekleştir", "shipments.ship", 20, ContextActionGroup.Operational, execute, x => Is(x, "Planned")),
        A("shipment.deliver", "Teslim Edildi İşaretle", "shipments.deliver", 30, ContextActionGroup.Operational, execute, x => Is(x, "InTransit")),
        A("shipment.cancel", "Sevkiyatı İptal Et", "shipments.cancel", 10, ContextActionGroup.Critical, execute, x => !Is(x, "Delivered", "Cancelled"), true, "ShipmentCancelled", "Shipment"),
        A("shipment.audit", "Geçmiş / Audit", "shipments.audit.view", 10, ContextActionGroup.Audit, execute)
    ];

    public static IReadOnlyList<ContextActionDefinition> Instruments(Func<string, object?, Task<ContextActionResult>> execute) =>
    [
        A("instrument.open", "Kartı Aç", "instruments.view", 10, ContextActionGroup.Primary, execute),
        A("instrument.bank", "Bankaya Gönder", "instruments.send_to_bank", 10, ContextActionGroup.Operational, execute, x => IsInstrument(x, "InPortfolio")),
        A("instrument.collect", "Tahsil Et", "instruments.collect", 20, ContextActionGroup.Operational, execute, x => IsInstrument(x, "InPortfolio", "AtBank")),
        A("instrument.endorse", "Ciro Et", "instruments.endorse", 30, ContextActionGroup.Operational, execute, x => IsInstrument(x, "InPortfolio")),
        A("instrument.cancel", "İptal Et", "instruments.cancel", 10, ContextActionGroup.Critical, execute, x => !IsInstrument(x, "Collected", "Cancelled"), true, "FinancialInstrumentCancelled", "FinancialInstrument"),
        A("instrument.audit", "Hareket Geçmişi / Audit", "instruments.audit.view", 10, ContextActionGroup.Audit, execute)
    ];

    public static IReadOnlyList<ContextActionDefinition> Orders(Func<string, object?, Task<ContextActionResult>> execute) =>
    [
        A("order.open", "Siparişi Aç", "orders.view", 10, ContextActionGroup.Primary, execute),
        A("order.edit", "Düzenle", "orders.edit", 20, ContextActionGroup.Primary, execute, x => !IsOrder(x, "Cancelled", "Completed")),
        A("order.reserve", "Stok Rezervasyonu Yap", "orders.reserve", 10, ContextActionGroup.Operational, execute, x => x is OrderContextRow { HasReservation: false } && !IsOrder(x, "Cancelled")),
        A("order.release", "Rezervasyonu Kaldır", "orders.release_reservation", 20, ContextActionGroup.Operational, execute, x => x is OrderContextRow { HasReservation: true }),
        A("order.shipment", "Sevk Emri Oluştur", "orders.shipment.create", 30, ContextActionGroup.Operational, execute, x => !IsOrder(x, "Cancelled", "Completed")),
        A("order.cancel", "Siparişi İptal Et", "orders.cancel", 10, ContextActionGroup.Critical, execute, x => !IsOrder(x, "Cancelled", "Completed"), true, "OrderCancelled", "Order"),
        A("order.audit", "Geçmiş / Audit", "orders.audit.view", 10, ContextActionGroup.Audit, execute)
    ];

    public static IReadOnlyList<ContextActionDefinition> Despatches(Func<string, object?, Task<ContextActionResult>> execute) =>
    [
        A("despatch.open", "İrsaliye Detayını Aç", "despatches.view", 10, ContextActionGroup.Primary, execute),
        A("despatch.plan", "Planla", "despatches.plan", 10, ContextActionGroup.Operational, execute, x => IsStatus(x, "Draft")),
        A("despatch.ready", "Sevke Hazırla", "despatches.prepare", 20, ContextActionGroup.Operational, execute, x => IsStatus(x, "Planned")),
        A("despatch.ship", "Sevki Başlat", "despatches.ship", 30, ContextActionGroup.Operational, execute, x => IsStatus(x, "ReadyForShipment")),
        A("despatch.deliver", "Teslim Edildi", "despatches.deliver", 40, ContextActionGroup.Operational, execute, x => IsStatus(x, "InTransit")),
        A("despatch.cancel", "İrsaliyeyi İptal Et", "despatches.cancel", 10, ContextActionGroup.Critical, execute, x => !IsStatus(x, "Delivered", "Cancelled"), true, "DespatchCancelled", "DespatchDocument"),
        A("despatch.audit", "Geçmiş / Audit", "despatches.audit.view", 10, ContextActionGroup.Audit, execute)
    ];

    public static IReadOnlyList<ContextActionDefinition> PurchaseReceipts(Func<string, object?, Task<ContextActionResult>> execute) =>
    [
        A("purchase.receipt.open", "İrsaliyeyi Aç", "purchases.receipt.view", 10, ContextActionGroup.Primary, execute),
        A("purchase.receipt.approve", "İrsaliyeyi Onayla ve Depoya Al", "purchases.receipt.approve", 20, ContextActionGroup.Operational, execute, x => IsStatus(x, "Draft")),
        A("purchase.receipt.cancel", "İrsaliyeyi İptal Et", "purchases.receipt.cancel", 10, ContextActionGroup.Critical, execute, x => IsStatus(x, "Draft"), true, "AlışBelgesiIptalEdildi", "PurchaseReceipt"),
        A("purchase.receipt.audit", "İşlem Geçmişi / Audit", "purchases.audit.view", 10, ContextActionGroup.Audit, execute)
    ];

    private static ContextActionDefinition A(string id, string header, string permission, int order, ContextActionGroup group, Func<string, object?, Task<ContextActionResult>> execute, Func<object?, bool>? rule = null, bool critical = false, string? audit = null, string? entity = null) =>
        new(id, header, permission, "", order, group, selected => execute(id, selected), rule, rule, ContextSelectionMode.Single, true, critical, critical ? _ => $"{header} işlemi uygulanacak.\n\nBu işlem belge ve stok hareketlerini etkileyebilir." : null, audit, entity, x => x?.GetType().GetProperty("Id")?.GetValue(x)?.ToString());
    private static bool Is(object? value, params string[] states) => Status(value) is { } status && states.Contains(status, StringComparer.OrdinalIgnoreCase);
    private static bool IsStatus(object? value, params string[] states) => Status(value) is { } status && states.Contains(status, StringComparer.OrdinalIgnoreCase);
    private static string? Status(object? value) => value switch
    {
        ShipmentContextRow row => row.Status,
        DespatchContextRow row => row.Status,
        DataRowView row when row.Row.Table.Columns.Contains("DurumKod") => row["DurumKod"]?.ToString(),
        DataRowView row when row.Row.Table.Columns.Contains("Durum") => row["Durum"]?.ToString(),
        _ => null
    };
    private static bool IsInstrument(object? value, params string[] states) => value is InstrumentContextRow row && states.Contains(row.Status, StringComparer.OrdinalIgnoreCase);
    private static bool IsOrder(object? value, params string[] states) => value is OrderContextRow row && states.Contains(row.Status, StringComparer.OrdinalIgnoreCase);
}
