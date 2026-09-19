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
        "shipments.view", "shipments.create", "shipments.pick", "shipments.pack", "shipments.plan", "shipments.change_date", "shipments.change_address", "shipments.change_branch", "shipments.change_warehouse", "shipments.document.view", "shipments.ship", "shipments.deliver", "shipments.cancel", "shipments.audit.view",
        "invoices.view", "invoices.account_transaction.view", "invoices.inventory_transaction.view", "invoices.einvoice.view", "invoices.print", "invoices.return.create", "invoices.reverse", "invoices.audit.view",
        "edocuments.view", "edocuments.invoice.send", "edocuments.archive.send", "edocuments.despatch.send", "edocuments.status.query", "edocuments.retry", "edocuments.cancel", "edocuments.incoming.view", "edocuments.incoming.import", "edocuments.settings.view", "edocuments.settings.edit", "edocuments.audit.view",
        "reports.view", "reports.layout.save", "reports.layout.set_default", "reports.layout.reset", "reports.export", "reports.print"
    ];
}
