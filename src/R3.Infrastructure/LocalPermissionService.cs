using R3.Application.Security;

namespace R3.Infrastructure;

public sealed class LocalPermissionService(StoreDatabase database, string userName) : IPermissionService
{
    private HashSet<string>? _permissions;
    private bool _isAdministrator;
    private bool _configured;

    public bool HasPermission(string permissionCode)
    {
        EnsureLoaded();
        if (_isAdministrator) return true;
        // Backward compatible: until an administrator configures the user's role (Kullanıcı ve
        // Yetkiler > Roller ve Yetkiler > Kaydet sets roles.permissions_configured), existing users
        // retain access. Once configured, an empty grant set really means "nothing".
        if (!_configured && _permissions!.Count == 0) return true;
        return _permissions!.Contains(permissionCode);
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
        // Seed the Kasa Kullanıcısı defaults only while nobody has edited that role - otherwise a grant an
        // administrator removed would silently come back on the next login.
        var cashierRole = database.Query("SELECT id FROM roles WHERE code='CASHIER' AND permissions_configured=0 LIMIT 1").Rows.Cast<System.Data.DataRow>().FirstOrDefault()?[0]?.ToString();
        if (!string.IsNullOrWhiteSpace(cashierRole))
        {
            foreach (var code in PermissionCatalog.All.Where(x => x.StartsWith("cash.", StringComparison.OrdinalIgnoreCase) || x is "reports.export" or "reports.print"))
                database.Execute("INSERT OR IGNORE INTO role_permissions(role_id,permission_id,is_allowed) SELECT $role,id,1 FROM permissions WHERE permission_key=$key", ("$role", cashierRole), ("$key", code));
        }
        _permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roles = database.Query("""
            SELECT r.code, r.permissions_configured
            FROM users u
            JOIN user_roles ur ON ur.user_id=u.id
            JOIN roles r ON r.id=ur.role_id AND r.is_active=1
            WHERE u.username=$user AND u.is_active=1
            """, ("$user", (object)userName)).Rows.Cast<System.Data.DataRow>().ToList();
        _isAdministrator = string.Equals(userName, "admin", StringComparison.OrdinalIgnoreCase)
            || roles.Any(r => string.Equals(r[0].ToString(), "ADMIN", StringComparison.OrdinalIgnoreCase));
        _configured = roles.Any(r => Convert.ToInt64(r[1]) == 1);
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
