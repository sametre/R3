using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record BarcodeResolution(string Barcode, string ProductId, string ProductCode, string ProductName, string ProductType, string? VariantId, string? VariantCode, string? VariantName, string UnitId, string UnitCode, string UnitName, decimal QuantityFactor, bool IsPrimary, bool ProductIsActive, bool VariantIsActive, bool BarcodeIsActive);
public sealed record ProductLookupResult(string ProductId, string Code, string Name, string Brand, string Category, string ProductType);
public sealed class LocalBarcodeResolver(StoreDatabase database)
{
    public BarcodeResolution? Resolve(string barcode, string companyId)
    {
        var value = barcode.Trim();
        if (value.Length == 0) return null;
        var t = database.Query("""
            SELECT b.barcode,b.product_id,p.code,p.name,p.product_type,b.variant_id,v.code,v.name,u.id,u.code,u.name,b.quantity,b.is_primary,p.is_active,COALESCE(v.is_active,1),b.is_active
            FROM product_barcodes b JOIN products p ON p.id=b.product_id LEFT JOIN product_variants v ON v.id=b.variant_id JOIN units u ON u.id=COALESCE(b.unit_id,p.base_unit_id)
            WHERE b.barcode=$barcode AND p.company_id=$company LIMIT 1
            """, ("$barcode", value), ("$company", companyId));
        if (t.Rows.Count == 0)
        {
            var product = database.Query("""
                SELECT p.id,p.code,p.name,p.product_type,u.id,u.code,u.name,p.is_active
                FROM products p JOIN units u ON u.id=p.base_unit_id
                WHERE p.company_id=$company AND p.code=$code COLLATE NOCASE LIMIT 1
                """, ("$company", companyId), ("$code", value));
            if (product.Rows.Count == 0) throw new KeyNotFoundException($"Barkod veya ürün kodu bulunamadı: {value}");
            var p = product.Rows[0];
            return new BarcodeResolution(value, p[0].ToString()!, p[1].ToString()!, p[2].ToString()!, p[3].ToString()!, null, null, null,
                p[4].ToString()!, p[5].ToString()!, p[6].ToString()!, 1, false, Convert.ToBoolean(p[7]), true, true);
        }
        var r = t.Rows[0]; return new BarcodeResolution(r[0].ToString()!, r[1].ToString()!, r[2].ToString()!, r[3].ToString()!, r[4].ToString()!, r.IsNull(5) ? null : r[5].ToString(), r.IsNull(6) ? null : r[6].ToString(), r.IsNull(7) ? null : r[7].ToString(), r[8].ToString()!, r[9].ToString()!, r[10].ToString()!, Convert.ToDecimal(r[11]), Convert.ToBoolean(r[12]), Convert.ToBoolean(r[13]), Convert.ToBoolean(r[14]), Convert.ToBoolean(r[15]));
    }
    public BarcodeResolution ResolveForInventory(string barcode, string companyId)
    {
        var result = Resolve(barcode, companyId) ?? throw new KeyNotFoundException($"Barkod bulunamadı: {barcode.Trim()}");
        if (!result.BarcodeIsActive) throw new InvalidOperationException("Bu barkod pasif durumda.");
        if (!result.ProductIsActive) throw new InvalidOperationException("Bu ürün pasif durumda.");
        if (result.VariantId != null && !result.VariantIsActive) throw new InvalidOperationException("Bu ürün varyantı pasif durumda.");
        return result;
    }
}

public sealed class LocalProductLookupService(StoreDatabase database)
{
    public DataTable Search(string companyId, string? search = null)
    {
        var q = $"%{search?.Trim() ?? ""}%";
        return database.Query("SELECT p.id AS ProductId,p.code AS Code,p.name AS Name,COALESCE(b.name,'') AS Brand,COALESCE(c.name,'') AS Category,p.product_type AS ProductType FROM products p LEFT JOIN brands b ON b.id=p.brand_id LEFT JOIN categories c ON c.id=p.category_id WHERE p.company_id=$company AND p.is_active=1 AND (p.code LIKE $q OR p.name LIKE $q) ORDER BY p.code", ("$company", companyId), ("$q", q));
    }
}
