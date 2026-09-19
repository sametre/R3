namespace R3.Desktop.ContextActions;

public sealed record ShipmentContextRow(string Id, string Number, string AccountName, string Status);
public sealed record InstrumentContextRow(string Id, string Number, string Status);
public sealed record OrderContextRow(string Id, string Number, string Status, bool HasReservation);
public sealed record TransferContextRow(string Id, string Number, string Status);

public static class FutureModuleContextActions
{
    public static IReadOnlyList<ContextActionDefinition> Shipments(Func<string, object?, Task<ContextActionResult>> execute) =>
    [
        A("shipment.open", "Sevkiyat Detayını Aç", "shipments.view", 10, ContextActionGroup.Primary, execute),
        A("shipment.pick", "Hazırlamaya Başla", "shipments.pick", 10, ContextActionGroup.Operational, execute, x => Is(x, "Waiting")),
        A("shipment.pack", "Paketlemeye Geç", "shipments.pack", 20, ContextActionGroup.Operational, execute, x => Is(x, "Picked")),
        A("shipment.plan", "Sevkiyatı Planla", "shipments.plan", 30, ContextActionGroup.Operational, execute, x => Is(x, "Waiting")),
        A("shipment.ship", "Sevkiyatı Gerçekleştir", "shipments.ship", 40, ContextActionGroup.Operational, execute, x => Is(x, "ReadyForShipment")),
        A("shipment.deliver", "Teslim Edildi İşaretle", "shipments.deliver", 50, ContextActionGroup.Operational, execute, x => Is(x, "Shipped", "InTransit")),
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

    private static ContextActionDefinition A(string id, string header, string permission, int order, ContextActionGroup group, Func<string, object?, Task<ContextActionResult>> execute, Func<object?, bool>? rule = null, bool critical = false, string? audit = null, string? entity = null) =>
        new(id, header, permission, "", order, group, selected => execute(id, selected), rule, rule, ContextSelectionMode.Single, true, critical, critical ? _ => $"{header} işlemi uygulanacak.\n\nBu işlem belge ve stok hareketlerini etkileyebilir." : null, audit, entity, x => x?.GetType().GetProperty("Id")?.GetValue(x)?.ToString());
    private static bool Is(object? value, params string[] states) => value is ShipmentContextRow row && states.Contains(row.Status, StringComparer.OrdinalIgnoreCase);
    private static bool IsInstrument(object? value, params string[] states) => value is InstrumentContextRow row && states.Contains(row.Status, StringComparer.OrdinalIgnoreCase);
    private static bool IsOrder(object? value, params string[] states) => value is OrderContextRow row && states.Contains(row.Status, StringComparer.OrdinalIgnoreCase);
}
