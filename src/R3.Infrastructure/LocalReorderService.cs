using System.Data;

namespace R3.Infrastructure;

public sealed record ReorderSuggestion(
    string ProductId, string ProductCode, string ProductName, string WarehouseId, string WarehouseName, string UnitId,
    decimal Available, decimal OnOrder, decimal Minimum, decimal Maximum, decimal Suggested,
    string? SupplierId, string SupplierName, int LeadTimeDays, decimal LastCost, decimal PurchaseVatRate);

public sealed record ReorderOrderLine(string ProductId, string WarehouseId, string SupplierId, decimal Quantity, decimal UnitPrice);

/// <summary>Filter combo entry; an empty Id means "all".</summary>
public sealed record ReorderFilterOption(string Id, string Name);

/// <summary>
/// Sipariş Seviyeleri / Sipariş Önerileri. For every active stock product and warehouse whose kullanılabilir
/// stock is below the effective minimum (depo bazlı override, else ürün geneli):
///   need = (maximum if set, else minimum) − available − on order (open satınalma siparişleri to that warehouse),
///   raised to the minimum order quantity (supplier's, else product's) and rounded up to the order multiple.
/// Supplier = the product's active tedarikçi with the best priority; price hint = last purchase receipt cost.
/// <see cref="CreatePurchaseOrders"/> turns chosen lines into draft satınalma siparişleri, one per
/// (supplier, warehouse), through the regular LocalPurchasingService - approval stays a manual step.
/// </summary>
public sealed class LocalReorderService(StoreDatabase database)
{
    public IReadOnlyList<ReorderFilterOption> WarehouseOptions(string companyId) =>
        FilterOptions("SELECT id, code || ' — ' || name FROM warehouses WHERE company_id=$c AND is_active=1 ORDER BY code", companyId, "Tüm depolar");

    public IReadOnlyList<ReorderFilterOption> ProductGroupOptions(string companyId) =>
        FilterOptions("SELECT id, code || ' — ' || name FROM product_groups WHERE company_id=$c AND is_active=1 ORDER BY code", companyId, "Tüm stok grupları");

    private List<ReorderFilterOption> FilterOptions(string sql, string companyId, string allLabel) =>
        [new("", allLabel), .. database.Query(sql, ("$c", companyId)).Rows.Cast<DataRow>().Select(r => new ReorderFilterOption(r[0].ToString()!, r[1].ToString()!))];

    public IReadOnlyList<ReorderSuggestion> Suggestions(string companyId, string? warehouseId = null, string? productGroupId = null)
    {
        var rows = database.Query("""
            SELECT p.id, p.code, p.name, w.id, w.name, p.base_unit_id,
                   COALESCE(b.quantity_available,0) AS available,
                   COALESCE((SELECT SUM(l.quantity-l.received_quantity) FROM purchase_document_lines l JOIN purchase_documents d ON d.id=l.purchase_document_id
                             WHERE l.product_id=p.id AND d.warehouse_id=w.id AND d.document_type='Order' AND d.status IN ('Draft','Approved','PartiallyReceived')),0) AS on_order,
                   COALESCE(wp.minimum_stock,p.minimum_stock,0) AS min_stock, COALESCE(wp.maximum_stock,p.maximum_stock,0) AS max_stock,
                   p.minimum_order_quantity, p.order_multiple, p.purchase_vat_rate,
                   s.supplier_account_id, COALESCE(a.name,''), COALESCE(s.lead_time_days,0)+COALESCE(s.extra_lead_time_days,0), s.minimum_order_quantity,
                   COALESCE((SELECT t.unit_cost FROM inventory_transactions t WHERE t.product_id=p.id AND t.transaction_type='PurchaseReceipt' AND t.unit_cost IS NOT NULL ORDER BY t.transaction_at DESC LIMIT 1),0)
            FROM products p
            JOIN warehouses w ON w.company_id=p.company_id AND w.is_active=1 AND ($w='' OR w.id=$w)
            LEFT JOIN product_warehouse_policies wp ON wp.product_id=p.id AND wp.warehouse_id=w.id
            LEFT JOIN inventory_balances b ON b.product_id=p.id AND b.warehouse_id=w.id AND b.variant_id IS NULL
            LEFT JOIN product_suppliers s ON s.id=(SELECT x.id FROM product_suppliers x WHERE x.product_id=p.id AND x.is_active=1 ORDER BY x.priority LIMIT 1)
            LEFT JOIN accounts a ON a.id=s.supplier_account_id
            WHERE p.company_id=$c AND p.is_active=1 AND p.product_type<>'Service' AND ($g='' OR p.product_group_id=$g)
              AND COALESCE(wp.minimum_stock,p.minimum_stock,0)>0
              AND COALESCE(b.quantity_available,0) < COALESCE(wp.minimum_stock,p.minimum_stock,0)
              -- without a warehouse filter, only warehouses the product has ever been stocked in (no 35k × depo explosion)
              AND ($w<>'' OR b.product_id IS NOT NULL OR wp.product_id IS NOT NULL)
            ORDER BY a.name, p.code
            """, ("$c", companyId), ("$w", warehouseId ?? ""), ("$g", productGroupId ?? ""));
        var result = new List<ReorderSuggestion>();
        foreach (DataRow r in rows.Rows)
        {
            // columns: 0 id 1 code 2 name 3 depo id 4 depo 5 birim 6 kullanılabilir 7 yoldaki 8 min 9 max 10 ürün min sipariş
            // 11 sipariş katı 12 alış KDV 13 tedarikçi id 14 tedarikçi 15 tedarik günü 16 tedarikçi min sipariş 17 son maliyet
            decimal D(int i) => r[i] == DBNull.Value ? 0m : Convert.ToDecimal(r[i]);
            var available = D(6); var onOrder = D(7); var min = D(8); var max = D(9);
            var suggested = Suggest(max > 0 ? max : min, available, onOrder, Math.Max(D(10), D(16)), D(11));
            if (suggested <= 0) continue;
            result.Add(new ReorderSuggestion(r[0].ToString()!, r[1].ToString()!, r[2].ToString()!, r[3].ToString()!, r[4].ToString()!, r[5].ToString()!,
                available, onOrder, min, max, suggested, r[13] == DBNull.Value ? null : r[13].ToString(), r[14].ToString()!, (int)D(15), D(17), D(12)));
        }
        return result;
    }

    /// <summary>need = target − available − on order; then ≥ minimum order quantity; then a multiple of order multiple.</summary>
    public static decimal Suggest(decimal target, decimal available, decimal onOrder, decimal minimumOrder, decimal orderMultiple)
    {
        var need = target - available - onOrder;
        if (need <= 0) return 0;
        if (minimumOrder > 0 && need < minimumOrder) need = minimumOrder;
        if (orderMultiple > 0) need = Math.Ceiling(need / orderMultiple) * orderMultiple;
        return need;
    }

    /// <summary>Creates one draft satınalma siparişi per (supplier, warehouse). Returns the new document ids.</summary>
    public IReadOnlyList<string> CreatePurchaseOrders(string companyId, IReadOnlyList<ReorderOrderLine> lines, string userName)
    {
        if (lines.Count == 0) throw new ArgumentException("Sipariş oluşturmak için en az bir satır seçin.");
        if (lines.Any(x => string.IsNullOrWhiteSpace(x.SupplierId))) throw new ArgumentException("Tedarikçisi tanımlı olmayan ürünler siparişe dönüştürülemez; ürün kartında tedarikçi tanımlayın.");
        if (lines.Any(x => x.Quantity <= 0 || x.UnitPrice < 0)) throw new ArgumentException("Sipariş miktarı 0'dan büyük, fiyat negatif olmamalıdır.");
        var purchasing = new LocalPurchasingService(database);
        var ids = new List<string>();
        foreach (var group in lines.GroupBy(x => (x.SupplierId, x.WarehouseId)))
        {
            var branch = database.Query("SELECT branch_id FROM warehouses WHERE id=$w AND company_id=$c", ("$w", group.Key.WarehouseId), ("$c", companyId)).Rows.Cast<DataRow>().FirstOrDefault()?[0]?.ToString()
                ?? throw new ArgumentException("Depo bulunamadı.");
            var orderLines = group.Select(line =>
            {
                var product = database.Query("SELECT base_unit_id, purchase_vat_rate FROM products WHERE id=$p AND company_id=$c", ("$p", line.ProductId), ("$c", companyId)).Rows.Cast<DataRow>().FirstOrDefault()
                    ?? throw new ArgumentException("Ürün bulunamadı.");
                return new PurchaseLineEdit(line.ProductId, product[0].ToString()!, line.Quantity, line.UnitPrice, 0, Convert.ToDecimal(product[1]), Description: "Sipariş önerisi");
            }).ToList();
            ids.Add(purchasing.Create(new PurchaseDocumentEdit(companyId, branch, group.Key.WarehouseId, group.Key.SupplierId!, "Order", DateTime.Today, null, "TRY", "Sipariş önerilerinden oluşturuldu", orderLines), userName));
        }
        return ids;
    }
}
