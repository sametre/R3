using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

/// <summary>
/// Kullanıcı şube/depo erişimi inside the posting services (Kullanıcı ve Yetkiler › Şube / Depo Erişimi).
/// The operator is <see cref="StoreDatabase.OperatorUserName"/> - the login name set once at sign-in -
/// rather than a per-call user argument, because most screens pass the display name to the services.
/// Rules match LocalUserAdminService.AllowedBranchIds/AllowedWarehouseIds: no operator (tests, tools,
/// migrations), an unknown operator, an active Yönetici or a user without any access rows = unrestricted;
/// otherwise the warehouse must be allowed explicitly, or - when only şubeler are restricted - belong to an
/// allowed şube. Runs on the caller's connection/transaction so a refusal rolls the whole posting back.
/// </summary>
public static class InventoryAccessGuard
{
    public static void Ensure(StoreDatabase database, SqliteConnection c, SqliteTransaction tx, string warehouseId)
    {
        var operatorName = database.OperatorUserName;
        if (string.IsNullOrWhiteSpace(operatorName) || string.IsNullOrWhiteSpace(warehouseId)) return;
        var user = Scalar(c, tx, "SELECT id FROM users WHERE username=$u AND is_active=1", ("$u", operatorName.Trim()));
        if (user == null) return;
        if (Scalar(c, tx, "SELECT 1 FROM user_roles ur JOIN roles r ON r.id=ur.role_id AND r.is_active=1 WHERE ur.user_id=$u AND r.code='ADMIN'", ("$u", user)) != null) return;

        var restrictsWarehouses = Scalar(c, tx, "SELECT 1 FROM user_warehouse_access WHERE user_id=$u LIMIT 1", ("$u", user)) != null;
        if (restrictsWarehouses)
        {
            if (Scalar(c, tx, "SELECT 1 FROM user_warehouse_access WHERE user_id=$u AND warehouse_id=$w", ("$u", user), ("$w", warehouseId)) != null) return;
            throw Denied(c, tx, warehouseId);
        }
        if (Scalar(c, tx, "SELECT 1 FROM user_branch_access WHERE user_id=$u LIMIT 1", ("$u", user)) == null) return;
        if (Scalar(c, tx, "SELECT 1 FROM user_branch_access a JOIN warehouses w ON w.branch_id=a.branch_id WHERE a.user_id=$u AND w.id=$w", ("$u", user), ("$w", warehouseId)) != null) return;
        throw Denied(c, tx, warehouseId);
    }

    private static InvalidOperationException Denied(SqliteConnection c, SqliteTransaction tx, string warehouseId) =>
        new($"{Scalar(c, tx, "SELECT code || ' — ' || name FROM warehouses WHERE id=$w", ("$w", warehouseId)) ?? "Bu"} deposunda işlem yetkiniz yok. Yetkili depolarınızı Kullanıcı ve Yetkiler › Şube / Depo Erişimi'nden yöneticinize sorun.");

    private static string? Scalar(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] parameters)
    {
        using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = sql;
        foreach (var (n, v) in parameters) q.Parameters.AddWithValue(n, v);
        var value = q.ExecuteScalar(); return value == null || value == DBNull.Value ? null : value.ToString();
    }
}
