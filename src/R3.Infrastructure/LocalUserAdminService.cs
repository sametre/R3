using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using R3.Application.Security;

namespace R3.Infrastructure;

public sealed record UserAccountEdit(
    string Id, string UserName, string DisplayName, string Email, string RoleCode,
    string? DefaultBranchId = null, string? DefaultWarehouseId = null, bool IsActive = true);

public sealed record RoleEdit(string Id, string Code, string Name, string Description = "", bool IsActive = true);

/// <summary>
/// Kullanıcı ve Yetkiler engine. Ports the ASB MNUSER/MNUSERGRUP/YETKI concepts (user with a group,
/// active flag, default branch/warehouse, per-group permission grants) onto R3's canonical
/// users/roles/user_roles/role_permissions tables. Deliberately role-based only: ASB stores YETKI
/// per user (58k rows for 103 users, mostly duplicated group defaults), R3 grants per role.
/// Guards the two ways an administrator can lock everyone out: removing/deactivating the last active
/// Yönetici, and deactivating the account they are signed in with.
/// </summary>
public sealed class LocalUserAdminService(StoreDatabase database)
{
    public const string AdminRoleCode = "ADMIN";
    private static readonly string[] SystemRoleCodes = [AdminRoleCode, "CASHIER", "USER"];
    private static readonly Regex UserNamePattern = new("^[A-Za-z0-9ÇĞİÖŞÜçğıöşü._-]{2,30}$", RegexOptions.Compiled);
    private static readonly Regex RoleCodePattern = new("^[A-Z0-9_]{2,20}$", RegexOptions.Compiled);
    public const int MinimumPasswordLength = 6;

    public static bool IsSystemRole(string code) => SystemRoleCodes.Contains(code, StringComparer.OrdinalIgnoreCase);

    public DataTable SearchUsers(string? search = null, bool activeOnly = false)
    {
        var q = $"%{search?.Trim() ?? ""}%";
        return database.Query("""
            SELECT u.id AS Id, u.username AS KullaniciAdi, u.display_name AS AdSoyad, u.email AS Eposta,
                   COALESCE(r.code,'USER') AS RolKodu, COALESCE(r.name,'Standart Kullanıcı') AS Rol,
                   COALESCE(b.name,'') AS VarsayilanSube, COALESCE(w.name,'') AS VarsayilanDepo,
                   COALESCE(strftime('%d.%m.%Y %H:%M', u.last_login_at, 'localtime'),'') AS SonGiris, u.is_active AS Aktif
            FROM users u
            LEFT JOIN user_roles ur ON ur.user_id=u.id
            LEFT JOIN roles r ON r.id=ur.role_id
            LEFT JOIN branches b ON b.id=u.default_branch_id
            LEFT JOIN warehouses w ON w.id=u.default_warehouse_id
            WHERE ($q='%%' OR u.username LIKE $q OR u.display_name LIKE $q OR u.email LIKE $q OR r.name LIKE $q)
              AND ($active=0 OR u.is_active=1)
            GROUP BY u.id
            ORDER BY u.username
            """, ("$q", q), ("$active", activeOnly ? 1 : 0));
    }

    public UserAccountEdit? GetUser(string userId)
    {
        var table = database.Query("""
            SELECT u.id, u.username, u.display_name, u.email, COALESCE(r.code,'USER'), u.default_branch_id, u.default_warehouse_id, u.is_active
            FROM users u LEFT JOIN user_roles ur ON ur.user_id=u.id LEFT JOIN roles r ON r.id=ur.role_id
            WHERE u.id=$id LIMIT 1
            """, ("$id", userId));
        if (table.Rows.Count == 0) return null;
        var r = table.Rows[0];
        return new UserAccountEdit(r[0].ToString()!, r[1].ToString()!, r[2].ToString()!, r[3].ToString()!, r[4].ToString()!,
            r[5] == DBNull.Value ? null : r[5].ToString(), r[6] == DBNull.Value ? null : r[6].ToString(), Convert.ToInt64(r[7]) == 1);
    }

    /// <summary>Creates or updates a user. <paramref name="newPassword"/> is required for a new user and
    /// ignored (pass null) for an existing one - use <see cref="ResetPassword"/> for that.</summary>
    public string SaveUser(UserAccountEdit edit, string? newPassword, string actingUserName)
    {
        var isNew = string.IsNullOrWhiteSpace(edit.Id);
        var userName = edit.UserName.Trim();
        if (!UserNamePattern.IsMatch(userName)) throw new ArgumentException("Kullanıcı adı 2-30 karakter olmalı; yalnızca harf, rakam, nokta, alt çizgi ve tire içerebilir.");
        if (string.IsNullOrWhiteSpace(edit.DisplayName)) throw new ArgumentException("Ad soyad zorunludur.");
        if (!string.IsNullOrWhiteSpace(edit.Email) && !edit.Email.Contains('@')) throw new ArgumentException("E-posta adresi geçersiz.");
        if (string.IsNullOrWhiteSpace(edit.RoleCode)) throw new ArgumentException("Rol seçimi zorunludur.");
        if (isNew) ValidatePassword(newPassword);

        var id = isNew ? Guid.NewGuid().ToString() : edit.Id;
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();

        var roleId = Scalar(c, tx, "SELECT id FROM roles WHERE code=$code AND is_active=1", ("$code", edit.RoleCode))
            ?? throw new ArgumentException("Seçilen rol bulunamadı veya pasif.");
        if (Scalar(c, tx, "SELECT id FROM users WHERE username=$u AND id<>$id", ("$u", userName), ("$id", id)) != null)
            throw new ArgumentException($"'{userName}' kullanıcı adı zaten kullanılıyor.");
        if (!isNew)
        {
            var current = Scalar(c, tx, "SELECT username FROM users WHERE id=$id", ("$id", id)) ?? throw new ArgumentException("Kullanıcı bulunamadı.");
            if (!edit.IsActive && string.Equals(current, actingUserName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Oturum açtığınız kullanıcıyı pasife alamazsınız.");
        }
        if (!string.IsNullOrWhiteSpace(edit.DefaultWarehouseId) && !string.IsNullOrWhiteSpace(edit.DefaultBranchId)
            && Scalar(c, tx, "SELECT id FROM warehouses WHERE id=$w AND branch_id=$b", ("$w", edit.DefaultWarehouseId), ("$b", edit.DefaultBranchId)) == null)
            throw new ArgumentException("Varsayılan depo, seçilen varsayılan şubeye ait olmalıdır.");

        Exec(c, tx, isNew
            ? "INSERT INTO users(id,username,display_name,password_hash,is_active,created_at,updated_at,email,default_branch_id,default_warehouse_id) VALUES($id,$u,$name,$hash,$active,$now,$now,$email,$branch,$wh)"
            : "UPDATE users SET username=$u,display_name=$name,is_active=$active,updated_at=$now,email=$email,default_branch_id=$branch,default_warehouse_id=$wh WHERE id=$id",
            ("$id", id), ("$u", userName), ("$name", edit.DisplayName.Trim()), ("$hash", isNew ? StoreDatabase.HashPassword(newPassword!) : ""),
            ("$active", edit.IsActive ? 1 : 0), ("$now", now), ("$email", edit.Email.Trim()),
            ("$branch", Nullable(edit.DefaultBranchId)), ("$wh", Nullable(edit.DefaultWarehouseId)));
        Exec(c, tx, "DELETE FROM user_roles WHERE user_id=$id", ("$id", id));
        Exec(c, tx, "INSERT INTO user_roles(user_id,role_id) VALUES($id,$role)", ("$id", id), ("$role", roleId));

        EnsureAnActiveAdministratorRemains(c, tx);
        Audit(c, tx, "User", id, isNew ? "UserCreated" : "UserUpdated", $"{userName} / {edit.RoleCode} / aktif={edit.IsActive}", actingUserName, now);
        tx.Commit();
        return id;
    }

    public void ResetPassword(string userId, string newPassword, string actingUserName)
    {
        ValidatePassword(newPassword);
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        if (Exec(c, tx, "UPDATE users SET password_hash=$hash, updated_at=$now WHERE id=$id", ("$hash", StoreDatabase.HashPassword(newPassword)), ("$now", now), ("$id", userId)) == 0)
            throw new ArgumentException("Kullanıcı bulunamadı.");
        Audit(c, tx, "User", userId, "UserPasswordReset", "", actingUserName, now);
        tx.Commit();
    }

    public void SetUserActive(string userId, bool active, string actingUserName)
    {
        var user = GetUser(userId) ?? throw new ArgumentException("Kullanıcı bulunamadı.");
        SaveUser(user with { IsActive = active }, null, actingUserName);
    }

    public DataTable SearchRoles() => database.Query("""
        SELECT r.id AS Id, r.code AS Kod, r.name AS Ad, r.description AS Aciklama,
               (SELECT COUNT(1) FROM user_roles ur JOIN users u ON u.id=ur.user_id AND u.is_active=1 WHERE ur.role_id=r.id) AS KullaniciSayisi,
               CASE WHEN r.code='ADMIN' THEN 'Tümü'
                    WHEN r.permissions_configured=0 AND NOT EXISTS(SELECT 1 FROM role_permissions rp WHERE rp.role_id=r.id AND rp.is_allowed=1) THEN 'Sınırsız (tanımsız)'
                    ELSE CAST((SELECT COUNT(1) FROM role_permissions rp WHERE rp.role_id=r.id AND rp.is_allowed=1) AS TEXT) END AS YetkiSayisi,
               r.is_active AS Aktif
        FROM roles r
        ORDER BY CASE r.code WHEN 'ADMIN' THEN 0 WHEN 'CASHIER' THEN 1 WHEN 'USER' THEN 2 ELSE 3 END, r.name
        """);

    public DataTable RoleLookup() => database.Query("SELECT code AS Code, name AS Name FROM roles WHERE is_active=1 ORDER BY CASE code WHEN 'ADMIN' THEN 0 WHEN 'CASHIER' THEN 1 WHEN 'USER' THEN 2 ELSE 3 END, name");

    public string SaveRole(RoleEdit edit, string actingUserName)
    {
        var isNew = string.IsNullOrWhiteSpace(edit.Id);
        var code = edit.Code.Trim().ToUpperInvariant();
        if (!RoleCodePattern.IsMatch(code)) throw new ArgumentException("Rol kodu 2-20 karakter olmalı; yalnızca büyük harf, rakam ve alt çizgi içerebilir.");
        if (string.IsNullOrWhiteSpace(edit.Name)) throw new ArgumentException("Rol adı zorunludur.");
        var id = isNew ? Guid.NewGuid().ToString() : edit.Id;
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        if (!isNew)
        {
            var current = Scalar(c, tx, "SELECT code FROM roles WHERE id=$id", ("$id", id)) ?? throw new ArgumentException("Rol bulunamadı.");
            if (IsSystemRole(current) && !string.Equals(current, code, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Sistem rollerinin kodu değiştirilemez.");
            if (string.Equals(current, AdminRoleCode, StringComparison.OrdinalIgnoreCase) && !edit.IsActive) throw new InvalidOperationException("Yönetici rolü pasife alınamaz.");
        }
        if (Scalar(c, tx, "SELECT id FROM roles WHERE code=$code AND id<>$id", ("$code", code), ("$id", id)) != null)
            throw new ArgumentException($"'{code}' rol kodu zaten kullanılıyor.");
        Exec(c, tx, isNew
            ? "INSERT INTO roles(id,code,name,is_active,description,permissions_configured) VALUES($id,$code,$name,$active,$desc,1)"
            : "UPDATE roles SET code=$code,name=$name,is_active=$active,description=$desc WHERE id=$id",
            ("$id", id), ("$code", code), ("$name", edit.Name.Trim()), ("$active", edit.IsActive ? 1 : 0), ("$desc", edit.Description.Trim()));
        Audit(c, tx, "Role", id, isNew ? "RoleCreated" : "RoleUpdated", $"{code} / aktif={edit.IsActive}", actingUserName, now);
        tx.Commit();
        return id;
    }

    /// <summary>Every catalog permission with its grant state for the role, labelled for the matrix.</summary>
    public IReadOnlyList<RolePermissionRow> GetRolePermissions(string roleId)
    {
        var granted = database.Query("""
            SELECT p.permission_key FROM role_permissions rp JOIN permissions p ON p.id=rp.permission_id
            WHERE rp.role_id=$role AND rp.is_allowed=1
            """, ("$role", roleId)).Rows.Cast<DataRow>().Select(r => r[0].ToString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return PermissionCatalog.All
            .Select(key => new RolePermissionRow(key, PermissionLabels.Module(key), PermissionLabels.Describe(key), granted.Contains(key)))
            .ToList();
    }

    /// <summary>Replaces the role's grants with exactly <paramref name="allowedKeys"/> and marks the role
    /// as configured, so an empty set now really means "no permissions" instead of the legacy
    /// "never configured, allow everything" bootstrap.</summary>
    public void SaveRolePermissions(string roleId, IEnumerable<string> allowedKeys, string actingUserName)
    {
        var keys = allowedKeys.Where(k => PermissionCatalog.All.Contains(k, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        var code = Scalar(c, tx, "SELECT code FROM roles WHERE id=$id", ("$id", roleId)) ?? throw new ArgumentException("Rol bulunamadı.");
        if (string.Equals(code, AdminRoleCode, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Yönetici rolü her zaman tüm yetkilere sahiptir; düzenlenemez.");
        foreach (var key in keys)
            Exec(c, tx, "INSERT OR IGNORE INTO permissions(id,permission_key,name,legacy_key,module) VALUES($id,$key,$key,NULL,$module)",
                ("$id", Guid.NewGuid().ToString()), ("$key", key), ("$module", key.Split('.')[0]));
        Exec(c, tx, "DELETE FROM role_permissions WHERE role_id=$role", ("$role", roleId));
        foreach (var key in keys)
            Exec(c, tx, "INSERT INTO role_permissions(role_id,permission_id,is_allowed) SELECT $role,id,1 FROM permissions WHERE permission_key=$key", ("$role", roleId), ("$key", key));
        Exec(c, tx, "UPDATE roles SET permissions_configured=1 WHERE id=$role", ("$role", roleId));
        Audit(c, tx, "Role", roleId, "RolePermissionsUpdated", $"{code}: {keys.Count} yetki", actingUserName, now);
        tx.Commit();
    }

    private static void ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumPasswordLength) throw new ArgumentException($"Şifre en az {MinimumPasswordLength} karakter olmalıdır.");
    }

    private static void EnsureAnActiveAdministratorRemains(SqliteConnection c, SqliteTransaction tx)
    {
        var admins = Convert.ToInt64(Scalar(c, tx, """
            SELECT COUNT(1) FROM users u JOIN user_roles ur ON ur.user_id=u.id JOIN roles r ON r.id=ur.role_id
            WHERE u.is_active=1 AND r.is_active=1 AND r.code='ADMIN'
            """) ?? "0");
        if (admins == 0) throw new InvalidOperationException("Sistemde en az bir aktif Yönetici kullanıcı kalmalıdır.");
    }

    private static object Nullable(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static string? Scalar(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] parameters)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var (n, v) in parameters) cmd.Parameters.AddWithValue(n, v);
        var value = cmd.ExecuteScalar();
        return value == null || value == DBNull.Value ? null : value.ToString();
    }

    private static int Exec(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] parameters)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var (n, v) in parameters) cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteNonQuery();
    }

    private static void Audit(SqliteConnection c, SqliteTransaction tx, string entityType, string entityId, string action, string detail, string actingUserName, string now) =>
        Exec(c, tx, "INSERT INTO audit_logs(id,user_id,entity_type,entity_id,action,new_values,created_at) VALUES($id,$user,$type,$entity,$action,$new,$now)",
            ("$id", Guid.NewGuid().ToString()), ("$user", actingUserName), ("$type", entityType), ("$entity", entityId), ("$action", action), ("$new", detail), ("$now", now));
}

public sealed record RolePermissionRow(string Key, string Module, string Label, bool IsAllowed);
