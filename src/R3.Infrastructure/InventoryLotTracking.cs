using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

/// <summary>
/// Lot / Seri engine, called inside a posting transaction for one movement line. Tracking mode comes from
/// product_inventory_policies.lot_tracking_type (None / Lot / Serial, edited on the product card):
///  - Lot: a lot number is required. Inbound creates the lot (with SKT) on first use; outbound needs the lot
///    to exist and to have enough quantity in that warehouse - lots never go negative, whatever the
///    warehouse's negative-stock policy says, because a negative lot has no physical meaning.
///  - Serial: the number of serials must equal the quantity (whole units). Inbound: none of them may already
///    be in stock anywhere; outbound: each must be in stock in this warehouse. inventory_serials keeps
///    every serial's current status/warehouse.
///  - None: a lot/serial may still be given and is recorded, but nothing is enforced.
/// The caller stamps the returned lot id / serial list on its inventory_transactions row.
/// Wired into Stok Giriş/Çıkış fişleri (approve and reverse). Invoices, transfers and returns do not carry
/// lots yet - Lot / Seri Takip › Takip Uyumu shows the resulting "lot atanmamış" quantity per product.
/// </summary>
public static class InventoryLotTracking
{
    public sealed record Assignment(string? LotId, string? SerialText);

    public static string Mode(SqliteConnection c, SqliteTransaction tx, string productId)
    {
        using var q = c.CreateCommand(); q.Transaction = tx;
        q.CommandText = "SELECT COALESCE(lot_tracking_type,'None') FROM product_inventory_policies WHERE product_id=$p";
        q.Parameters.AddWithValue("$p", productId);
        return q.ExecuteScalar()?.ToString() ?? "None";
    }

    public static IReadOnlyList<string> ParseSerials(string? text) =>
        (text ?? "").Split([',', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static Assignment Apply(SqliteConnection c, SqliteTransaction tx, string companyId, string warehouseId, string productId,
        string? lotNo, string? serialText, DateTime? expiryDate, decimal quantity, bool inbound, string now)
    {
        var mode = Mode(c, tx, productId);
        var serials = ParseSerials(serialText);
        lotNo = string.IsNullOrWhiteSpace(lotNo) ? null : lotNo.Trim().ToUpperInvariant();
        if (mode == "Lot" && lotNo == null) throw new InvalidOperationException($"{Code(c, tx, productId)} lot takiplidir; lot numarası zorunludur.");
        if (mode == "Serial")
        {
            if (quantity != Math.Floor(quantity)) throw new InvalidOperationException($"{Code(c, tx, productId)} seri takiplidir; miktar tam sayı olmalıdır.");
            if (serials.Count != (int)quantity) throw new InvalidOperationException($"{Code(c, tx, productId)} seri takiplidir; {quantity:0} adet için {quantity:0} seri numarası girilmelidir (girilen: {serials.Count}).");
        }

        string? lotId = null;
        if (lotNo != null)
        {
            lotId = Scalar(c, tx, "SELECT id FROM inventory_lots WHERE company_id=$c AND product_id=$p AND lot_no=$l", ("$c", companyId), ("$p", productId), ("$l", lotNo));
            if (lotId == null)
            {
                if (!inbound) throw new InvalidOperationException($"{lotNo} numaralı lot bu ürün için bulunamadı.");
                lotId = Guid.NewGuid().ToString();
                Exec(c, tx, "INSERT INTO inventory_lots(id,company_id,product_id,lot_no,expiry_date,created_at) VALUES($id,$c,$p,$l,$e,$now)",
                    ("$id", lotId), ("$c", companyId), ("$p", productId), ("$l", lotNo), ("$e", expiryDate is { } e ? e.ToString("yyyy-MM-dd") : DBNull.Value), ("$now", now));
            }
            else if (inbound && expiryDate is { } e)
                Exec(c, tx, "UPDATE inventory_lots SET expiry_date=COALESCE(expiry_date,$e) WHERE id=$id", ("$e", e.ToString("yyyy-MM-dd")), ("$id", lotId));
            if (!inbound)
            {
                var available = LotBalance(c, tx, lotId, warehouseId);
                if (available < quantity) throw new InvalidOperationException($"{lotNo} lotunda bu depoda yeterli miktar yok. Lot bakiyesi: {available:N2}; istenen: {quantity:N2}.");
            }
        }

        foreach (var serial in serials)
        {
            var current = Row(c, tx, "SELECT status, warehouse_id FROM inventory_serials WHERE product_id=$p AND serial_no=$s", ("$p", productId), ("$s", serial));
            if (inbound)
            {
                if (current != null && current[0]?.ToString() == "InStock") throw new InvalidOperationException($"{serial} seri numarası zaten stokta ({WarehouseName(c, tx, current[1]?.ToString())}).");
                Exec(c, tx, """
                    INSERT INTO inventory_serials(product_id,serial_no,company_id,status,warehouse_id,lot_id,updated_at) VALUES($p,$s,$c,'InStock',$w,$l,$now)
                    ON CONFLICT(product_id,serial_no) DO UPDATE SET status='InStock', warehouse_id=$w, lot_id=COALESCE($l,lot_id), updated_at=$now
                    """, ("$p", productId), ("$s", serial), ("$c", companyId), ("$w", warehouseId), ("$l", (object?)lotId ?? DBNull.Value), ("$now", now));
            }
            else
            {
                if (current == null || current[0]?.ToString() != "InStock" || current[1]?.ToString() != warehouseId)
                    throw new InvalidOperationException($"{serial} seri numarası bu depoda stokta değil.");
                Exec(c, tx, "UPDATE inventory_serials SET status='Out', updated_at=$now WHERE product_id=$p AND serial_no=$s", ("$now", now), ("$p", productId), ("$s", serial));
            }
        }
        return new Assignment(lotId, serials.Count == 0 ? null : string.Join(",", serials));
    }

    /// <summary>Stamps lot/serial on the movement the caller just inserted on this connection.</summary>
    public static void StampLastMovement(SqliteConnection c, SqliteTransaction tx, Assignment assignment)
    {
        if (assignment.LotId == null && assignment.SerialText == null) return;
        Exec(c, tx, "UPDATE inventory_transactions SET lot_id=$l, serial_number=$s WHERE rowid=last_insert_rowid()",
            ("$l", (object?)assignment.LotId ?? DBNull.Value), ("$s", (object?)assignment.SerialText ?? DBNull.Value));
    }

    public static decimal LotBalance(SqliteConnection c, SqliteTransaction tx, string lotId, string warehouseId)
    {
        using var q = c.CreateCommand(); q.Transaction = tx;
        q.CommandText = "SELECT transaction_type, quantity FROM inventory_transactions WHERE lot_id=$l AND warehouse_id=$w";
        q.Parameters.AddWithValue("$l", lotId); q.Parameters.AddWithValue("$w", warehouseId);
        using var r = q.ExecuteReader(); decimal total = 0;
        while (r.Read()) total += LocalInventoryService.IsInbound(r.GetString(0)) ? r.GetDecimal(1) : -r.GetDecimal(1);
        return total;
    }

    private static string Code(SqliteConnection c, SqliteTransaction tx, string productId) => Scalar(c, tx, "SELECT code FROM products WHERE id=$p", ("$p", productId)) ?? "Ürün";
    private static string WarehouseName(SqliteConnection c, SqliteTransaction tx, string? id) => id == null ? "?" : Scalar(c, tx, "SELECT name FROM warehouses WHERE id=$w", ("$w", id)) ?? id;

    private static string? Scalar(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] p)
    {
        using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = sql; foreach (var (n, v) in p) q.Parameters.AddWithValue(n, v);
        var value = q.ExecuteScalar(); return value == null || value == DBNull.Value ? null : value.ToString();
    }

    private static object?[]? Row(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] p)
    {
        using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = sql; foreach (var (n, v) in p) q.Parameters.AddWithValue(n, v);
        using var r = q.ExecuteReader(); if (!r.Read()) return null; var values = new object?[r.FieldCount]; r.GetValues(values!); return values;
    }

    private static void Exec(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] p)
    {
        using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = sql; foreach (var (n, v) in p) q.Parameters.AddWithValue(n, v);
        q.ExecuteNonQuery();
    }
}

/// <summary>Read side of Lot / Seri Takip.</summary>
public sealed class LocalLotService(StoreDatabase database)
{
    public DataTable Lots(string companyId, string? search = null, int? expiringWithinDays = null, bool withStockOnly = true)
    {
        var rows = database.Query("""
            SELECT l.id AS LotId, p.id AS UrunId, p.code AS StokKodu, p.name AS StokAdi, l.lot_no AS LotNo, COALESCE(l.expiry_date,'') AS Skt,
                   t.warehouse_id AS DepoId, COALESCE(w.name,'') AS Depo, t.transaction_type AS Tur, t.quantity AS Miktar
            FROM inventory_lots l JOIN products p ON p.id=l.product_id
            LEFT JOIN inventory_transactions t ON t.lot_id=l.id LEFT JOIN warehouses w ON w.id=t.warehouse_id
            WHERE l.company_id=$c AND (p.code LIKE $q OR p.name LIKE $q OR l.lot_no LIKE $q)
            """, ("$c", companyId), ("$q", $"%{search?.Trim() ?? ""}%"));
        var result = new DataTable();
        foreach (var (n, t) in new[] { ("LotId", typeof(string)), ("UrunId", typeof(string)), ("StokKodu", typeof(string)), ("StokAdi", typeof(string)), ("LotNo", typeof(string)), ("Skt", typeof(string)),
                     ("KalanGun", typeof(int)), ("Depo", typeof(string)), ("Miktar", typeof(decimal)) })
            result.Columns.Add(n, t);
        result.Columns["KalanGun"]!.AllowDBNull = true;
        foreach (var group in rows.Rows.Cast<DataRow>().GroupBy(r => (Lot: r["LotId"].ToString()!, Warehouse: r["DepoId"]?.ToString() ?? "")))
        {
            var first = group.First();
            var quantity = group.Where(r => r["Tur"] != DBNull.Value).Sum(r => LocalInventoryService.IsInbound(r["Tur"].ToString()!) ? Convert.ToDecimal(r["Miktar"]) : -Convert.ToDecimal(r["Miktar"]));
            if (withStockOnly && quantity == 0) continue;
            int? days = DateTime.TryParse(first["Skt"].ToString(), out var expiry) ? (int)(expiry.Date - DateTime.Today).TotalDays : null;
            if (expiringWithinDays is { } limit && (days == null || days > limit)) continue;
            result.Rows.Add(first["LotId"], first["UrunId"], first["StokKodu"], first["StokAdi"], first["LotNo"], first["Skt"], (object?)days ?? DBNull.Value, first["Depo"], quantity);
        }
        result.DefaultView.Sort = "Skt ASC, StokKodu ASC";
        return result;
    }

    public DataTable Serials(string companyId, string? search = null, bool inStockOnly = false) => database.Query("""
        SELECT p.code AS StokKodu, p.name AS StokAdi, s.serial_no AS SeriNo, CASE s.status WHEN 'InStock' THEN 'Depoda' ELSE 'Çıkış yapıldı' END AS Durum,
               COALESCE(w.name,'') AS Depo, COALESCE(l.lot_no,'') AS LotNo, s.updated_at AS SonHareket, s.product_id AS UrunId
        FROM inventory_serials s JOIN products p ON p.id=s.product_id LEFT JOIN warehouses w ON w.id=s.warehouse_id LEFT JOIN inventory_lots l ON l.id=s.lot_id
        WHERE s.company_id=$c AND ($in=0 OR s.status='InStock') AND (s.serial_no LIKE $q OR p.code LIKE $q OR p.name LIKE $q)
        ORDER BY p.code, s.serial_no LIMIT 2000
        """, ("$c", companyId), ("$in", inStockOnly ? 1 : 0), ("$q", $"%{search?.Trim() ?? ""}%"));

    /// <summary>Every movement that carried this lot number or serial number (exact match).</summary>
    public DataTable Trace(string companyId, string lotOrSerial)
    {
        var key = lotOrSerial.Trim().ToUpperInvariant();
        return database.Query("""
            SELECT t.transaction_at AS Tarih, p.code AS StokKodu, p.name AS StokAdi, t.transaction_type AS Tur, COALESCE(w.name,'') AS Depo, t.quantity AS Miktar,
                   COALESCE(l.lot_no,'') AS LotNo, COALESCE(t.serial_number,'') AS SeriNo, COALESCE(t.reference_no,'') AS Referans, COALESCE(t.document_type,'') AS BelgeTipi
            FROM inventory_transactions t JOIN products p ON p.id=t.product_id LEFT JOIN warehouses w ON w.id=t.warehouse_id LEFT JOIN inventory_lots l ON l.id=t.lot_id
            WHERE t.company_id=$c AND (UPPER(l.lot_no)=$k OR (',' || UPPER(COALESCE(t.serial_number,'')) || ',') LIKE '%,' || $k || ',%')
            ORDER BY t.transaction_at, t.created_at
            """, ("$c", companyId), ("$k", key));
    }

    /// <summary>Tracked products whose stock is not fully covered by lots/serials (movements posted by
    /// flows that do not carry lots yet: invoices, transfers, returns, opening balances).</summary>
    public DataTable Coverage(string companyId)
    {
        var products = database.Query("""
            SELECT p.id, p.code, p.name, pol.lot_tracking_type,
                   COALESCE((SELECT SUM(b.quantity_on_hand) FROM inventory_balances b WHERE b.product_id=p.id),0) AS stock
            FROM products p JOIN product_inventory_policies pol ON pol.product_id=p.id
            WHERE p.company_id=$c AND pol.lot_tracking_type IN ('Lot','Serial')
            """, ("$c", companyId));
        var result = new DataTable();
        foreach (var (n, t) in new[] { ("UrunId", typeof(string)), ("StokKodu", typeof(string)), ("StokAdi", typeof(string)), ("Takip", typeof(string)), ("Stok", typeof(decimal)), ("Atanan", typeof(decimal)), ("Atanmamis", typeof(decimal)) })
            result.Columns.Add(n, t);
        foreach (DataRow p in products.Rows)
        {
            var id = p[0].ToString()!; var serial = p[3].ToString() == "Serial";
            decimal assigned;
            if (serial) assigned = Convert.ToDecimal(database.Query("SELECT COUNT(*) FROM inventory_serials WHERE product_id=$p AND status='InStock'", ("$p", id)).Rows[0][0]);
            else assigned = database.Query("SELECT transaction_type, quantity FROM inventory_transactions WHERE product_id=$p AND lot_id IS NOT NULL", ("$p", id)).Rows.Cast<DataRow>()
                .Sum(r => LocalInventoryService.IsInbound(r[0].ToString()!) ? Convert.ToDecimal(r[1]) : -Convert.ToDecimal(r[1]));
            var stock = Convert.ToDecimal(p[4]);
            result.Rows.Add(id, p[1], p[2], serial ? "Seri" : "Lot", stock, assigned, stock - assigned);
        }
        return result;
    }
}
