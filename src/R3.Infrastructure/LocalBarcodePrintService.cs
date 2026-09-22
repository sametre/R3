using System.Data;

namespace R3.Infrastructure;

/// <summary>
/// Barkod yazdırma sağlayıcısından bağımsız etiket veri sözleşmesi. Bu katman
/// yazıcıya doğrudan erişmez; ileride ZPL, EPL veya Windows yazdırma adaptörü
/// aynı veriyi kullanabilir.
/// </summary>
public sealed record BarcodePrintLabel(string Barcode, string ProductCode, string ProductName, string UnitName, string VariantName, decimal Quantity, bool IsPrimary);

public sealed class LocalBarcodePrintService(StoreDatabase database)
{
    public IReadOnlyList<BarcodePrintLabel> GetLabels(string productId, string companyId)
    {
        var table = database.Query("""
            SELECT b.barcode,p.code,p.name,u.name,COALESCE(v.name,''),b.quantity,b.is_primary
            FROM product_barcodes b JOIN products p ON p.id=b.product_id AND p.company_id=$company
            LEFT JOIN units u ON u.id=COALESCE(b.unit_id,p.base_unit_id)
            LEFT JOIN product_variants v ON v.id=b.variant_id
            WHERE b.product_id=$product AND b.is_active=1 ORDER BY b.is_primary DESC,b.barcode
            """, ("$product", productId), ("$company", companyId));
        return table.Rows.Cast<DataRow>().Select(row => new BarcodePrintLabel(
            row[0].ToString()!, row[1].ToString()!, row[2].ToString()!, row[3].ToString()!, row[4].ToString()!, Convert.ToDecimal(row[5]), Convert.ToBoolean(row[6]))).ToArray();
    }
}
