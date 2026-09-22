using R3.Application.Security;

namespace R3.Infrastructure;

public sealed class LocalPermissionService(StoreDatabase database, string userName) : IPermissionService
{
    private HashSet<string>? _permissions;
    private bool _isAdministrator;

    public bool HasPermission(string permissionCode)
    {
        EnsureLoaded();
        if (_isAdministrator) return true;
        // Backward compatible: until an administrator configures any role grants, existing users retain access.
        if (_permissions!.Count == 0) return true;
        return _permissions.Contains(permissionCode);
    }

    public bool HasAnyPermission(params string[] permissionCodes) => permissionCodes.Any(HasPermission);
    public bool HasAllPermissions(params string[] permissionCodes) => permissionCodes.All(HasPermission);
    public void Refresh() => _permissions = null;

    private void EnsureLoaded()
    {
        if (_permissions != null) return;
        foreach (var code in PermissionCatalog.All)
        {
            var legacy = LegacyPermissionMap.Codes.FirstOrDefault(x => string.Equals(x.Value, code, StringComparison.OrdinalIgnoreCase)).Key;
            database.Execute("INSERT OR IGNORE INTO permissions(id,permission_key,name,legacy_key,module) VALUES($id,$key,$name,$legacy,$module)", ("$id", Guid.NewGuid().ToString()), ("$key", code), ("$name", code), ("$legacy", (object?)legacy ?? DBNull.Value), ("$module", code.Split('.')[0]));
        }
        var cashierRole = database.Query("SELECT id FROM roles WHERE code='CASHIER' LIMIT 1").Rows.Cast<System.Data.DataRow>().FirstOrDefault()?[0]?.ToString();
        if (!string.IsNullOrWhiteSpace(cashierRole))
        {
            foreach (var code in PermissionCatalog.All.Where(x => x.StartsWith("cash.", StringComparison.OrdinalIgnoreCase) || x is "reports.export" or "reports.print"))
                database.Execute("INSERT OR IGNORE INTO role_permissions(role_id,permission_id,is_allowed) SELECT $role,id,1 FROM permissions WHERE permission_key=$key", ("$role", cashierRole), ("$key", code));
        }
        _permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _isAdministrator = string.Equals(userName, "admin", StringComparison.OrdinalIgnoreCase);
        var table = database.Query("""
            SELECT p.permission_key
            FROM users u
            JOIN user_roles ur ON ur.user_id=u.id
            JOIN roles r ON r.id=ur.role_id AND r.is_active=1
            JOIN role_permissions rp ON rp.role_id=r.id AND rp.is_allowed=1
            JOIN permissions p ON p.id=rp.permission_id
            WHERE u.username=$user AND u.is_active=1
            """, ("$user", (object)userName));
        foreach (System.Data.DataRow row in table.Rows) _permissions.Add(row[0].ToString()!);
    }
}
