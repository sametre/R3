using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

/// <summary>
/// Negatif Stok Politikası: warehouses.allow_negative_stock existed (and showed up in Depo Yönetimi) but
/// no posting path read it - every stock-out rejected a shortage regardless. Every stock-out check
/// (manual out, stock documents, transfers) goes through <see cref="EnsureAvailable"/> so the flag is
/// honoured in one place.
/// </summary>
public static class InventoryStockPolicy
{
    public static bool AllowsNegativeStock(SqliteConnection connection, SqliteTransaction transaction, string warehouseId)
    {
        using var cmd = connection.CreateCommand(); cmd.Transaction = transaction;
        cmd.CommandText = "SELECT COALESCE(allow_negative_stock,0) FROM warehouses WHERE id=$w";
        cmd.Parameters.AddWithValue("$w", warehouseId);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L) == 1;
    }

    /// <summary>Throws with <paramref name="message"/> when <paramref name="available"/> is below
    /// <paramref name="required"/> and the warehouse does not allow negative stock.</summary>
    public static void EnsureAvailable(SqliteConnection connection, SqliteTransaction transaction, string warehouseId, decimal available, decimal required, string message)
    {
        if (available >= required || AllowsNegativeStock(connection, transaction, warehouseId)) return;
        throw new InvalidOperationException(message);
    }
}
