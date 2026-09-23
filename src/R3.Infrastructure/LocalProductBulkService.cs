using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

/// <summary>Fields a Toplu Ürün İşlemleri run may set. null = leave unchanged; "" for an id = clear it.</summary>
public sealed record ProductBulkChange(
    string? ProductGroupId = null, string? BrandId = null, string? CategoryId = null, string? OriginCountryId = null,
    decimal? VatRate = null, decimal? PurchaseVatRate = null, bool? IsActive = null, bool? IsSellable = null,
    decimal? MinimumStock = null, decimal? MaximumStock = null)
{
    public bool IsEmpty => ProductGroupId == null && BrandId == null && CategoryId == null && OriginCountryId == null && VatRate == null && PurchaseVatRate == null
        && IsActive == null && IsSellable == null && MinimumStock == null && MaximumStock == null;
}

/// <summary>
/// Toplu Ürün İşlemleri: applies one <see cref="ProductBulkChange"/> to many products in a single
/// transaction - all or nothing. Every referenced master record is checked against the company once,
/// min/max is validated per product against the values it will end up with (so setting only a new
/// maximum below an existing minimum is rejected), and one audit row per product records exactly which
/// fields changed.
/// </summary>
public sealed class LocalProductBulkService(StoreDatabase database)
{
    public const int MaxProductsPerRun = 5000;

    public int Apply(string companyId, IReadOnlyCollection<string> productIds, ProductBulkChange change, string userName)
    {
        if (productIds.Count == 0) throw new ArgumentException("En az bir ürün seçilmelidir.");
        if (productIds.Count > MaxProductsPerRun) throw new ArgumentException($"Tek seferde en fazla {MaxProductsPerRun:N0} ürün güncellenebilir.");
        if (change.IsEmpty) throw new ArgumentException("Değiştirilecek en az bir alan seçilmelidir.");
        if (change.VatRate is < 0 or > 100 || change.PurchaseVatRate is < 0 or > 100) throw new ArgumentException("KDV 0 ile 100 arasında olmalıdır.");
        if (change.MinimumStock < 0 || change.MaximumStock < 0) throw new ArgumentException("Stok değerleri negatif olamaz.");

        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        EnsureMaster(c, tx, "product_groups", change.ProductGroupId, companyId, "Stok grubu");
        EnsureMaster(c, tx, "brands", change.BrandId, companyId, "Marka");
        EnsureMaster(c, tx, "categories", change.CategoryId, companyId, "Kategori");
        if (!string.IsNullOrEmpty(change.OriginCountryId) && Count(c, tx, "SELECT COUNT(*) FROM countries WHERE id=$id", ("$id", change.OriginCountryId)) == 0)
            throw new ArgumentException("Menşe ülke bulunamadı.");

        var sets = new List<string>(); var parameters = new List<(string, object)>();
        void Set(string column, object? value, string name) { if (value == null) return; sets.Add($"{column}={name}"); parameters.Add((name, value is string s && s.Length == 0 ? DBNull.Value : value)); }
        Set("product_group_id", change.ProductGroupId, "$group"); Set("brand_id", change.BrandId, "$brand"); Set("category_id", change.CategoryId, "$category");
        Set("origin_country_id", change.OriginCountryId, "$origin"); Set("vat_rate", change.VatRate, "$vat"); Set("purchase_vat_rate", change.PurchaseVatRate, "$pvat");
        Set("is_active", change.IsActive is { } a ? (a ? 1 : 0) : null, "$active"); Set("is_sellable", change.IsSellable is { } sl ? (sl ? 1 : 0) : null, "$sellable");
        Set("minimum_stock", change.MinimumStock, "$min"); Set("maximum_stock", change.MaximumStock, "$max");
        var now = DateTime.UtcNow.ToString("O");
        var summary = string.Join(", ", sets.Select(x => x.Split('=')[0]));
        var updated = 0;
        foreach (var productId in productIds.Distinct())
        {
            using (var check = c.CreateCommand())
            {
                check.Transaction = tx; check.CommandText = "SELECT code, minimum_stock, maximum_stock FROM products WHERE id=$id AND company_id=$c";
                check.Parameters.AddWithValue("$id", productId); check.Parameters.AddWithValue("$c", companyId);
                using var r = check.ExecuteReader();
                if (!r.Read()) throw new ArgumentException("Seçilen ürünlerden biri bu firmada bulunamadı.");
                var min = change.MinimumStock ?? r.GetDecimal(1); var max = change.MaximumStock ?? r.GetDecimal(2);
                if (max > 0 && max < min) throw new ArgumentException($"{r.GetString(0)}: maksimum stok ({max:N2}) minimum stoktan ({min:N2}) küçük olamaz.");
            }
            using (var update = c.CreateCommand())
            {
                update.Transaction = tx; update.CommandText = $"UPDATE products SET {string.Join(",", sets)}, updated_at=$now WHERE id=$id AND company_id=$c";
                foreach (var (n, v) in parameters) update.Parameters.AddWithValue(n, v);
                update.Parameters.AddWithValue("$now", now); update.Parameters.AddWithValue("$id", productId); update.Parameters.AddWithValue("$c", companyId);
                updated += update.ExecuteNonQuery();
            }
            // Keep the policy mirror in step (LocalProductService mirrors min/max into product_inventory_policies).
            if (change.MinimumStock != null || change.MaximumStock != null)
                using (var mirror = c.CreateCommand())
                {
                    mirror.Transaction = tx; mirror.CommandText = "UPDATE product_inventory_policies SET minimum_stock=(SELECT minimum_stock FROM products WHERE id=$id), maximum_stock=(SELECT maximum_stock FROM products WHERE id=$id) WHERE product_id=$id";
                    mirror.Parameters.AddWithValue("$id", productId); mirror.ExecuteNonQuery();
                }
            using var audit = c.CreateCommand(); audit.Transaction = tx;
            audit.CommandText = "INSERT INTO audit_logs(id,user_id,company_id,entity_type,entity_id,action,new_values,created_at) VALUES($aid,$user,$c,'Product',$id,'ProductBulkUpdated',$new,$now)";
            audit.Parameters.AddWithValue("$aid", Guid.NewGuid().ToString()); audit.Parameters.AddWithValue("$user", userName); audit.Parameters.AddWithValue("$c", companyId);
            audit.Parameters.AddWithValue("$id", productId); audit.Parameters.AddWithValue("$new", summary); audit.Parameters.AddWithValue("$now", now);
            audit.ExecuteNonQuery();
        }
        tx.Commit();
        return updated;
    }

    private static void EnsureMaster(SqliteConnection c, SqliteTransaction tx, string table, string? id, string companyId, string label)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (Count(c, tx, $"SELECT COUNT(*) FROM {table} WHERE id=$id AND company_id=$c", ("$id", id), ("$c", companyId)) == 0) throw new ArgumentException($"{label} bu firma kapsamında bulunamadı.");
    }

    private static long Count(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] parameters)
    {
        using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = sql;
        foreach (var (n, v) in parameters) q.Parameters.AddWithValue(n, v);
        return Convert.ToInt64(q.ExecuteScalar());
    }
}
