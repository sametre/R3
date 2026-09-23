using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record ReservationEdit(
    string CompanyId, string BranchId, string WarehouseId, string ProductId, string? VariantId, decimal Quantity,
    string? AccountId = null, string ReferenceNo = "", string Description = "", DateTime? ExpiresAt = null);

/// <summary>
/// Stok Rezervasyonları. inventory_balances.quantity_reserved existed but nothing ever wrote it; this is
/// the single writer. A reservation moves quantity from "kullanılabilir" to "rezerve" (on hand is
/// untouched), so every stock-out check - which reads quantity_available - automatically protects
/// reserved stock. Closing a reservation (Released / Consumed / Expired) gives it back.
/// Reservations never go below zero availability, even in a warehouse that allows negative stock:
/// promising stock that is not there is exactly what a reservation is meant to prevent.
/// </summary>
public sealed class LocalReservationService(StoreDatabase database)
{
    public static readonly string[] ClosedStatuses = ["Released", "Consumed", "Expired"];

    public DataTable Search(string companyId, string? warehouseId = null, string? status = "Active", string? search = null)
    {
        var q = $"%{search?.Trim() ?? ""}%";
        return database.Query("""
            SELECT r.id AS Id, r.created_at AS Tarih, COALESCE(w.name,'') AS Depo, r.warehouse_id AS DepoId, r.product_id AS UrunId,
                   COALESCE(p.code || ' - ' || p.name, r.product_id) AS Urun, r.quantity AS Miktar,
                   COALESCE(a.name,'') AS Cari, COALESCE(r.account_id,'') AS CariId, r.reference_no AS Referans, r.description AS Aciklama,
                   COALESCE(r.expires_at,'') AS SonTarih, r.status AS Durum, r.created_by AS Olusturan
            FROM inventory_reservations r
            LEFT JOIN warehouses w ON w.id=r.warehouse_id
            LEFT JOIN products p ON p.id=r.product_id
            LEFT JOIN accounts a ON a.id=r.account_id
            WHERE r.company_id=$c AND ($w='' OR r.warehouse_id=$w) AND ($s='' OR r.status=$s)
              AND (p.code LIKE $q OR p.name LIKE $q OR r.reference_no LIKE $q OR COALESCE(a.name,'') LIKE $q)
            ORDER BY r.created_at DESC
            """, ("$c", companyId), ("$w", warehouseId ?? ""), ("$s", status ?? ""), ("$q", q));
    }

    public string Create(ReservationEdit edit, string userName)
    {
        if (string.IsNullOrWhiteSpace(edit.CompanyId) || string.IsNullOrWhiteSpace(edit.WarehouseId) || string.IsNullOrWhiteSpace(edit.ProductId))
            throw new ArgumentException("Firma, depo ve ürün zorunludur.");
        if (edit.Quantity <= 0) throw new ArgumentException("Rezervasyon miktarı 0'dan büyük olmalıdır.");
        if (edit.ExpiresAt is { } expires && expires <= DateTime.Now) throw new ArgumentException("Son geçerlilik tarihi ileri bir tarih olmalıdır.");

        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        var product = Row(c, tx, "SELECT is_active, product_type FROM products WHERE id=$p AND company_id=$c", ("$p", edit.ProductId), ("$c", edit.CompanyId))
            ?? throw new ArgumentException("Ürün bu firma kapsamında bulunamadı.");
        if (Convert.ToInt64(product[0]) != 1) throw new InvalidOperationException("Pasif ürün için rezervasyon yapılamaz.");
        if (string.Equals(product[1]?.ToString(), "Service", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Hizmet ürünleri stok tutmaz; rezervasyon yapılamaz.");
        var balance = Row(c, tx, "SELECT quantity_available FROM inventory_balances WHERE warehouse_id=$w AND product_id=$p AND (variant_id=$v OR (variant_id IS NULL AND $v IS NULL))",
            ("$w", edit.WarehouseId), ("$p", edit.ProductId), ("$v", (object?)edit.VariantId ?? DBNull.Value));
        var available = balance == null ? 0m : Convert.ToDecimal(balance[0]);
        if (available < edit.Quantity)
            throw new InvalidOperationException($"Rezervasyon için kullanılabilir stok yetersiz. Kullanılabilir: {available:N2}; istenen: {edit.Quantity:N2}.");

        var id = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        Exec(c, tx, """
            INSERT INTO inventory_reservations(id,company_id,branch_id,warehouse_id,product_id,variant_id,quantity,account_id,reference_no,description,status,expires_at,created_by,created_at)
            VALUES($id,$c,$b,$w,$p,$v,$q,$a,$ref,$desc,'Active',$exp,$user,$now)
            """, ("$id", id), ("$c", edit.CompanyId), ("$b", edit.BranchId), ("$w", edit.WarehouseId), ("$p", edit.ProductId), ("$v", (object?)edit.VariantId ?? DBNull.Value),
            ("$q", edit.Quantity), ("$a", string.IsNullOrWhiteSpace(edit.AccountId) ? DBNull.Value : edit.AccountId), ("$ref", edit.ReferenceNo.Trim()), ("$desc", edit.Description.Trim()),
            ("$exp", edit.ExpiresAt is { } e ? e.ToUniversalTime().ToString("O") : DBNull.Value), ("$user", userName), ("$now", now));
        ShiftReserved(c, tx, edit.WarehouseId, edit.ProductId, edit.VariantId, edit.Quantity, now);
        Audit(c, tx, edit.CompanyId, id, "ReservationCreated", $"{edit.Quantity:N2} / ref={edit.ReferenceNo}", userName, now);
        tx.Commit();
        return id;
    }

    /// <summary>Closes an active reservation and returns its quantity to "kullanılabilir".
    /// <paramref name="status"/>: Released (vazgeçildi) or Consumed (teslim edildi / sevk edildi).</summary>
    public void Close(string reservationId, string status, string userName)
    {
        if (status is not ("Released" or "Consumed")) throw new ArgumentException("Geçersiz rezervasyon kapanış durumu.");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        CloseCore(c, tx, reservationId, status, userName, DateTime.UtcNow.ToString("O"));
        tx.Commit();
    }

    /// <summary>Expires every active reservation whose son tarih has passed. Returns how many.</summary>
    public int ExpireDue(string companyId, string userName, DateTime? now = null)
    {
        var cutoff = (now ?? DateTime.Now).ToUniversalTime().ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        var due = new List<string>();
        using (var q = c.CreateCommand())
        {
            q.Transaction = tx; q.CommandText = "SELECT id FROM inventory_reservations WHERE company_id=$c AND status='Active' AND expires_at IS NOT NULL AND expires_at<=$now";
            q.Parameters.AddWithValue("$c", companyId); q.Parameters.AddWithValue("$now", cutoff);
            using var r = q.ExecuteReader(); while (r.Read()) due.Add(r.GetString(0));
        }
        foreach (var id in due) CloseCore(c, tx, id, "Expired", userName, DateTime.UtcNow.ToString("O"));
        tx.Commit();
        return due.Count;
    }

    private static void CloseCore(SqliteConnection c, SqliteTransaction tx, string id, string status, string userName, string now)
    {
        var row = Row(c, tx, "SELECT company_id, warehouse_id, product_id, variant_id, quantity, status FROM inventory_reservations WHERE id=$id", ("$id", id))
            ?? throw new ArgumentException("Rezervasyon bulunamadı.");
        if (row[5]?.ToString() != "Active") throw new InvalidOperationException("Yalnızca aktif rezervasyon kapatılabilir.");
        Exec(c, tx, "UPDATE inventory_reservations SET status=$s, closed_by=$user, closed_at=$now WHERE id=$id AND status='Active'", ("$s", status), ("$user", userName), ("$now", now), ("$id", id));
        ShiftReserved(c, tx, row[1]!.ToString()!, row[2]!.ToString()!, row[3] == DBNull.Value ? null : row[3]!.ToString(), -Convert.ToDecimal(row[4]), now);
        Audit(c, tx, row[0]!.ToString()!, id, "Reservation" + status, $"{Convert.ToDecimal(row[4]):N2}", userName, now);
    }

    private static void ShiftReserved(SqliteConnection c, SqliteTransaction tx, string warehouseId, string productId, string? variantId, decimal delta, string now) =>
        Exec(c, tx, """
            UPDATE inventory_balances SET quantity_reserved=quantity_reserved+$d, quantity_available=quantity_available-$d, updated_at=$now
            WHERE warehouse_id=$w AND product_id=$p AND (variant_id=$v OR (variant_id IS NULL AND $v IS NULL))
            """, ("$d", delta), ("$now", now), ("$w", warehouseId), ("$p", productId), ("$v", (object?)variantId ?? DBNull.Value));

    private static object?[]? Row(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] parameters)
    {
        using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = sql;
        foreach (var (n, v) in parameters) q.Parameters.AddWithValue(n, v);
        using var r = q.ExecuteReader(); if (!r.Read()) return null;
        var values = new object?[r.FieldCount]; r.GetValues(values!); return values;
    }

    private static void Exec(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] parameters)
    {
        using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = sql;
        foreach (var (n, v) in parameters) q.Parameters.AddWithValue(n, v);
        q.ExecuteNonQuery();
    }

    private static void Audit(SqliteConnection c, SqliteTransaction tx, string companyId, string id, string action, string detail, string userName, string now) =>
        Exec(c, tx, "INSERT INTO audit_logs(id,user_id,company_id,entity_type,entity_id,action,new_values,created_at) VALUES($id,$user,$c,'InventoryReservation',$entity,$action,$new,$now)",
            ("$id", Guid.NewGuid().ToString()), ("$user", userName), ("$c", companyId), ("$entity", id), ("$action", action), ("$new", detail), ("$now", now));
}
