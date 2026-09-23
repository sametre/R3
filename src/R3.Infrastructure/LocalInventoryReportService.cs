using System.Data;

namespace R3.Infrastructure;

public sealed record ProductLedgerReport(decimal OpeningBalance, decimal TotalIn, decimal TotalOut, decimal ClosingBalance, DataTable Lines);

/// <summary>
/// Stok raporları: Ürün Ekstresi (one product's movements with a running balance) and Stok Değer Raporu
/// (quantity × weighted average cost at a date). Both read inventory_transactions directly - the
/// movement log is the source of truth, inventory_balances is only a current-state projection and
/// cannot answer "as of date X". Direction comes from <see cref="LocalInventoryService.IsInbound"/>, the
/// same rule the balance rebuild uses, so a report can never disagree with Stok Durumu.
/// </summary>
public sealed class LocalInventoryReportService(StoreDatabase database)
{
    public ProductLedgerReport ProductLedger(string companyId, string productId, string? warehouseId = null, DateTime? from = null, DateTime? to = null)
    {
        var rows = database.Query("""
            SELECT t.transaction_at, COALESCE(w.name, t.warehouse_id), t.transaction_type, COALESCE(t.reference_no,''), COALESCE(t.description,''),
                   t.quantity, t.unit_cost, COALESCE(t.document_type,''), COALESCE(t.document_id,'')
            FROM inventory_transactions t LEFT JOIN warehouses w ON w.id=t.warehouse_id
            WHERE t.company_id=$c AND t.product_id=$p AND ($w='' OR t.warehouse_id=$w) AND ($to='' OR t.transaction_at<=$to)
            ORDER BY t.transaction_at, t.created_at, t.id
            """, ("$c", companyId), ("$p", productId), ("$w", warehouseId ?? ""), ("$to", to is { } t ? t.Date.AddDays(1).AddTicks(-1).ToUniversalTime().ToString("O") : ""));

        var lines = new DataTable();
        foreach (var (name, type) in new[] { ("Tarih", typeof(string)), ("Depo", typeof(string)), ("Tur", typeof(string)), ("Referans", typeof(string)), ("Aciklama", typeof(string)),
                     ("Giris", typeof(decimal)), ("Cikis", typeof(decimal)), ("Bakiye", typeof(decimal)), ("BirimMaliyet", typeof(decimal)), ("BelgeTipi", typeof(string)), ("BelgeId", typeof(string)) })
            lines.Columns.Add(name, type);
        var fromUtc = from?.Date.ToUniversalTime();
        decimal opening = 0, balance = 0, totalIn = 0, totalOut = 0;
        foreach (DataRow r in rows.Rows)
        {
            var quantity = Convert.ToDecimal(r[5]);
            var inbound = LocalInventoryService.IsInbound(r[2].ToString()!);
            var signed = inbound ? quantity : -quantity;
            var at = DateTime.Parse(r[0].ToString()!, null, System.Globalization.DateTimeStyles.RoundtripKind);
            if (fromUtc is { } f && at.ToUniversalTime() < f) { opening += signed; balance += signed; continue; }
            balance += signed;
            if (inbound) totalIn += quantity; else totalOut += quantity;
            lines.Rows.Add(at.ToLocalTime().ToString("dd.MM.yyyy HH:mm"), r[1], r[2], r[3], r[4], inbound ? quantity : 0m, inbound ? 0m : quantity, balance,
                r[6] == DBNull.Value ? 0m : Convert.ToDecimal(r[6]), r[7], r[8]);
        }
        return new ProductLedgerReport(opening, totalIn, totalOut, balance, lines);
    }

    /// <summary>
    /// Stock value per product and warehouse at <paramref name="asOf"/> (end of day; null = now).
    /// Cost = weighted average unit_cost of the product's inbound movements up to that date, across all
    /// warehouses (R3 has no per-warehouse costing). Rows whose product never had a costed inbound
    /// movement are kept with zero value and MaliyetKaynagi = "Maliyet yok" rather than silently dropped,
    /// so the total visibly understates instead of hiding the gap.
    /// </summary>
    public DataTable StockValuation(string companyId, string? warehouseId = null, string? productGroupId = null, DateTime? asOf = null, bool includeZero = false)
    {
        var cutoff = asOf is { } d ? d.Date.AddDays(1).AddTicks(-1).ToUniversalTime().ToString("O") : "";
        var movements = database.Query("""
            SELECT t.product_id, t.warehouse_id, t.transaction_type, t.quantity, t.unit_cost
            FROM inventory_transactions t JOIN products p ON p.id=t.product_id
            WHERE t.company_id=$c AND ($to='' OR t.transaction_at<=$to) AND ($g='' OR p.product_group_id=$g)
            """, ("$c", companyId), ("$to", cutoff), ("$g", productGroupId ?? ""));

        var quantities = new Dictionary<(string Product, string Warehouse), decimal>();
        var costs = new Dictionary<string, (decimal Quantity, decimal Amount)>();
        foreach (DataRow r in movements.Rows)
        {
            var product = r[0].ToString()!; var warehouse = r[1].ToString()!; var quantity = Convert.ToDecimal(r[3]);
            var inbound = LocalInventoryService.IsInbound(r[2].ToString()!);
            quantities[(product, warehouse)] = quantities.GetValueOrDefault((product, warehouse)) + (inbound ? quantity : -quantity);
            // Transfers carry the moved stock's cost (if any) but are not purchases - they must not skew the average.
            if (inbound && r[4] != DBNull.Value && Convert.ToDecimal(r[4]) > 0 && r[2].ToString() != "TransferIn")
            {
                var (q, a) = costs.GetValueOrDefault(product);
                costs[product] = (q + quantity, a + quantity * Convert.ToDecimal(r[4]));
            }
        }

        var names = database.Query("""
            SELECT p.id, p.code, p.name, COALESCE(g.name,'') FROM products p LEFT JOIN product_groups g ON g.id=p.product_group_id WHERE p.company_id=$c
            """, ("$c", companyId)).Rows.Cast<DataRow>().ToDictionary(r => r[0].ToString()!, r => (Code: r[1].ToString()!, Name: r[2].ToString()!, Group: r[3].ToString()!));
        var warehouses = database.Query("SELECT id, name FROM warehouses WHERE company_id=$c", ("$c", companyId)).Rows.Cast<DataRow>().ToDictionary(r => r[0].ToString()!, r => r[1].ToString()!);

        var result = new DataTable();
        foreach (var (name, type) in new[] { ("UrunId", typeof(string)), ("StokKodu", typeof(string)), ("StokAdi", typeof(string)), ("StokGrubu", typeof(string)), ("Depo", typeof(string)),
                     ("Miktar", typeof(decimal)), ("OrtalamaMaliyet", typeof(decimal)), ("Tutar", typeof(decimal)), ("MaliyetKaynagi", typeof(string)) })
            result.Columns.Add(name, type);
        foreach (var ((product, warehouse), quantity) in quantities.OrderBy(x => names.GetValueOrDefault(x.Key.Product).Code).ThenBy(x => warehouses.GetValueOrDefault(x.Key.Warehouse)))
        {
            if (!string.IsNullOrWhiteSpace(warehouseId) && warehouse != warehouseId) continue;
            if (quantity == 0 && !includeZero) continue;
            var info = names.GetValueOrDefault(product);
            var hasCost = costs.TryGetValue(product, out var cost) && cost.Quantity > 0;
            var unitCost = hasCost ? Math.Round(cost.Amount / cost.Quantity, 4) : 0m;
            result.Rows.Add(product, info.Code ?? product, info.Name ?? "", info.Group ?? "", warehouses.GetValueOrDefault(warehouse, warehouse), quantity, unitCost,
                Math.Round(quantity * unitCost, 2), hasCost ? "Ağırlıklı ortalama" : "Maliyet yok");
        }
        return result;
    }
}
