using System.Data;

namespace R3.Infrastructure;

public sealed record InventoryAvailabilityResult(string ProductId, string? VariantId, string WarehouseId, decimal QuantityOnHand, decimal QuantityReserved, decimal QuantityAvailable);
public sealed record ProductInventorySummary(decimal QuantityOnHand, decimal QuantityReserved, decimal QuantityAvailable, DataTable Balances, DataTable RecentMovements);
public sealed class LocalInventoryQueryService(StoreDatabase database)
{
    public InventoryAvailabilityResult GetAvailability(string warehouseId, string productId, string? variantId = null)
    {
        var t = database.Query("SELECT product_id,variant_id,warehouse_id,quantity_on_hand,quantity_reserved,quantity_available FROM inventory_balances WHERE warehouse_id=$w AND product_id=$p AND (variant_id=$v OR (variant_id IS NULL AND $v IS NULL))", ("$w", warehouseId), ("$p", productId), ("$v", (object?)variantId ?? DBNull.Value));
        if (t.Rows.Count == 0) return new(productId, variantId, warehouseId, 0, 0, 0);
        var r = t.Rows[0]; return new(r[0].ToString()!, r.IsNull(1) ? null : r[1].ToString(), r[2].ToString()!, Convert.ToDecimal(r[3]), Convert.ToDecimal(r[4]), Convert.ToDecimal(r[5]));
    }
    public ProductInventorySummary GetProductSummary(string productId, string? warehouseId = null)
    {
        var balances = database.Query("SELECT COALESCE(br.name,'') AS Sube,COALESCE(w.name,b.warehouse_id) AS Depo,COALESCE(v.name,'') AS Varyant,b.quantity_on_hand AS Mevcut,b.quantity_reserved AS Rezerve,b.quantity_available AS Kullanilabilir,b.last_transaction_at AS SonHareket FROM inventory_balances b LEFT JOIN branches br ON br.id=b.branch_id LEFT JOIN warehouses w ON w.id=b.warehouse_id LEFT JOIN product_variants v ON v.id=b.variant_id WHERE b.product_id=$p AND ($w='' OR b.warehouse_id=$w) ORDER BY w.name,v.name", ("$p", productId), ("$w", warehouseId ?? ""));
        var recent = database.Query("SELECT t.transaction_at AS Tarih,t.transaction_type AS Islem,COALESCE(br.name,'') AS Sube,COALESCE(w.name,t.warehouse_id) AS Depo,COALESCE(v.name,'') AS Varyant,CASE WHEN t.transaction_type IN ('OpeningBalance','PurchaseReceipt','SaleReturn','TransferIn','CountIncrease','ManualIn') THEN t.quantity ELSE 0 END AS Giris,CASE WHEN t.transaction_type IN ('PurchaseReturn','SaleIssue','TransferOut','CountDecrease','ManualOut') THEN t.quantity ELSE 0 END AS Cikis,t.reference_no AS Referans FROM inventory_transactions t LEFT JOIN branches br ON br.id=t.branch_id LEFT JOIN warehouses w ON w.id=t.warehouse_id LEFT JOIN product_variants v ON v.id=t.variant_id WHERE t.product_id=$p AND ($w='' OR t.warehouse_id=$w) ORDER BY t.transaction_at DESC LIMIT 20", ("$p", productId), ("$w", warehouseId ?? ""));
        return new(balances.AsEnumerable().Sum(x => Convert.ToDecimal(x["Mevcut"])), balances.AsEnumerable().Sum(x => Convert.ToDecimal(x["Rezerve"])), balances.AsEnumerable().Sum(x => Convert.ToDecimal(x["Kullanilabilir"])), balances, recent);
    }
}
