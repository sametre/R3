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
