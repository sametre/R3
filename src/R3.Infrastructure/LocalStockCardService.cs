using System.Data;

namespace R3.Infrastructure;

/// <summary>"+" satır detayı: kartın aktif barkodları ve depo bazlı stoğu.</summary>
public sealed record StockCardRowDetail(DataTable Barcodes, DataTable Warehouses);

/// <summary>
/// Stok › Stok Kartları (ASB düzeni) ekranlarının okuma tarafı. ASB gibi firmanın bütün kartlarını tek listede verir
/// (ızgara sanal kaydırma yapar; kolon filtre satırı ve gruplama istemci tarafında çalışır), bu yüzden sorgu satır başına
/// alt sorgu yerine GROUP BY'lı CTE'lerle yazılmıştır. Kaydetme LocalProductService.Save üzerinden yapılır.
/// </summary>
public sealed class LocalStockCardService(StoreDatabase database)
{
    /// <summary>
    /// Liste kolonları: StokKodu, StokAdi, UrunTipi (product_type), Birim, GrupKodu, GrupAdi, KdvOrani, SevkYeri, TedarikciAdi,
    /// TedarikGun, RenkAdi, BedenTipi, Beden, Varyantlar, Marka, Kategori, Mense, BirincilBarkod, BarkodSayisi, Olculer (birim/çarpan),
    /// MevcutStok, KullanilabilirStok, Fiyat (fiyat listesi verildiyse), Aktif.
    /// </summary>
    public DataTable List(string companyId, bool? activeOnly = true, string? priceListId = null)
    {
        if (string.IsNullOrWhiteSpace(companyId)) throw new ArgumentException("Firma zorunludur.");
        var active = activeOnly switch { true => "AND p.is_active=1", false => "AND p.is_active=0", _ => "" };
        return database.Query($"""
            WITH sup AS (
                SELECT ps.product_id, a.name AS supplier_name, ps.lead_time_days + ps.extra_lead_time_days AS lead_days,
                       ROW_NUMBER() OVER (PARTITION BY ps.product_id ORDER BY ps.priority, a.name) AS rn
                FROM product_suppliers ps JOIN accounts a ON a.id = ps.supplier_account_id WHERE ps.is_active = 1),
            var AS (
                SELECT product_id,
                       GROUP_CONCAT(DISTINCT NULLIF(color_code, '')) AS colors,
                       GROUP_CONCAT(DISTINCT NULLIF(size_type, '')) AS size_types,
                       GROUP_CONCAT(DISTINCT NULLIF(size_code, '')) AS sizes,
                       GROUP_CONCAT(code, ', ') AS codes
                FROM product_variants WHERE is_active = 1 GROUP BY product_id),
            bc AS (
                SELECT product_id, MAX(CASE WHEN is_primary = 1 THEN barcode END) AS primary_barcode, COUNT(*) AS barcode_count
                FROM product_barcodes WHERE is_active = 1 GROUP BY product_id),
            un AS (
                SELECT pu.product_id, GROUP_CONCAT(uu.code || CASE WHEN pu.is_base_unit = 1 THEN '' ELSE ' ×' || pu.conversion_factor END, ', ') AS units
                FROM product_units pu JOIN units uu ON uu.id = pu.unit_id WHERE pu.is_active = 1 GROUP BY pu.product_id),
            bal AS (
                SELECT product_id, SUM(quantity_on_hand) AS on_hand, SUM(quantity_available) AS available
                FROM inventory_balances GROUP BY product_id)
            SELECT p.id AS Id, p.code AS StokKodu, p.name AS StokAdi, p.product_type AS UrunTipi, u.code AS Birim,
                   COALESCE(g.code, '') AS GrupKodu, COALESCE(g.name, '') AS GrupAdi, p.vat_rate AS KdvOrani,
                   COALESCE(pol.shipment_location_type, '') AS SevkYeri,
                   COALESCE(sup.supplier_name, '') AS TedarikciAdi, sup.lead_days AS TedarikGun,
                   COALESCE(var.colors, '') AS RenkAdi, COALESCE(var.size_types, '') AS BedenTipi, COALESCE(var.sizes, '') AS Beden, COALESCE(var.codes, '') AS Varyantlar,
                   COALESCE(b.name, '') AS Marka, COALESCE(c.name, '') AS Kategori, COALESCE(co.name, '') AS Mense,
                   COALESCE(bc.primary_barcode, '') AS BirincilBarkod, COALESCE(bc.barcode_count, 0) AS BarkodSayisi,
                   COALESCE(un.units, u.code) AS Olculer,
                   COALESCE(bal.on_hand, 0) AS MevcutStok, COALESCE(bal.available, 0) AS KullanilabilirStok,
                   {(string.IsNullOrWhiteSpace(priceListId) ? "NULL" : "pp.price")} AS Fiyat,
                   p.is_active AS Aktif
            FROM products p
            JOIN units u ON u.id = p.base_unit_id
            LEFT JOIN product_groups g ON g.id = p.product_group_id
            LEFT JOIN brands b ON b.id = p.brand_id
            LEFT JOIN categories c ON c.id = p.category_id
            LEFT JOIN countries co ON co.id = p.origin_country_id
            LEFT JOIN product_inventory_policies pol ON pol.product_id = p.id
            LEFT JOIN sup ON sup.product_id = p.id AND sup.rn = 1
            LEFT JOIN var ON var.product_id = p.id
            LEFT JOIN bc ON bc.product_id = p.id
            LEFT JOIN un ON un.product_id = p.id
            LEFT JOIN bal ON bal.product_id = p.id
            {(string.IsNullOrWhiteSpace(priceListId) ? "" : "LEFT JOIN product_prices pp ON pp.price_list_id = $priceList AND pp.product_id = p.id")}
            WHERE p.company_id = $company {active}
            ORDER BY p.code
            """, string.IsNullOrWhiteSpace(priceListId) ? [("$company", companyId)] : [("$company", companyId), ("$priceList", priceListId)]);
    }

    /// <summary>Alt bilgi satırı: "16.976 adet aktif stok kartı mevcut" için sayı.</summary>
    public int ActiveCount(string companyId) =>
        Convert.ToInt32(database.Query("SELECT COUNT(*) FROM products WHERE company_id=$c AND is_active=1", ("$c", companyId)).Rows[0][0]);

    public StockCardRowDetail RowDetail(string productId) => new(
        database.Query("""
            SELECT b.barcode AS Barkod, COALESCE(u.code, '') AS Birim, b.quantity AS Miktar, CASE WHEN b.is_primary = 1 THEN 'Evet' ELSE '' END AS Birincil
            FROM product_barcodes b LEFT JOIN units u ON u.id = b.unit_id WHERE b.product_id = $p AND b.is_active = 1 ORDER BY b.is_primary DESC, b.barcode
            """, ("$p", productId)),
        database.Query("""
            SELECT w.code AS DepoKodu, w.name AS Depo, ib.quantity_on_hand AS Mevcut, ib.quantity_reserved AS Rezerve, ib.quantity_available AS Kullanilabilir
            FROM inventory_balances ib JOIN warehouses w ON w.id = ib.warehouse_id WHERE ib.product_id = $p ORDER BY w.code
            """, ("$p", productId)));

    /// <summary>Stok Kartı "Önceki / Sonraki": stok koduna göre bir önceki / sonraki kart (aktif-pasif hepsi). Uçta null.</summary>
    public (string Id, string Code)? Adjacent(string companyId, string code, bool next)
    {
        var table = database.Query(next
            ? "SELECT id, code FROM products WHERE company_id=$c AND code > $code COLLATE NOCASE ORDER BY code COLLATE NOCASE LIMIT 1"
            : "SELECT id, code FROM products WHERE company_id=$c AND code < $code COLLATE NOCASE ORDER BY code COLLATE NOCASE DESC LIMIT 1",
            ("$c", companyId), ("$code", code.Trim()));
        return table.Rows.Count == 0 ? null : (table.Rows[0][0].ToString()!, table.Rows[0][1].ToString()!);
    }

    /// <summary>Stok Kartı "Kayıt Getir": stok kodu (büyük/küçük harf duyarsız) ya da bu aktif barkodu taşıyan tek kart.</summary>
    public string? FindIdByCodeOrBarcode(string companyId, string codeOrBarcode)
    {
        var value = codeOrBarcode.Trim();
        if (value.Length == 0) return null;
        var byCode = database.Query("SELECT id FROM products WHERE company_id=$c AND code=$v COLLATE NOCASE", ("$c", companyId), ("$v", value));
        if (byCode.Rows.Count > 0) return byCode.Rows[0][0].ToString();
        var byBarcode = database.Query("SELECT DISTINCT p.id FROM product_barcodes b JOIN products p ON p.id=b.product_id WHERE p.company_id=$c AND b.barcode=$v AND b.is_active=1", ("$c", companyId), ("$v", value));
        return byBarcode.Rows.Count == 1 ? byBarcode.Rows[0][0].ToString() : null;
    }

    /// <summary>
    /// "…" seçim pencereleri: kind = product_groups | brands | categories | products | suppliers. Id, Kod, Ad döner;
    /// en fazla 500 satır (arama ile daraltılır).
    /// </summary>
    public DataTable Lookup(string kind, string companyId, string? search = null)
    {
        var term = $"%{search?.Trim() ?? ""}%";
        var sql = kind switch
        {
            "product_groups" => "SELECT id AS Id, code AS Kod, name AS Ad FROM product_groups WHERE company_id=$c AND is_active=1 AND (code LIKE $q OR name LIKE $q) ORDER BY code LIMIT 500",
            "brands" => "SELECT id AS Id, code AS Kod, name AS Ad FROM brands WHERE company_id=$c AND is_active=1 AND (code LIKE $q OR name LIKE $q) ORDER BY code LIMIT 500",
            "categories" => "SELECT id AS Id, code AS Kod, name AS Ad FROM categories WHERE company_id=$c AND is_active=1 AND (code LIKE $q OR name LIKE $q) ORDER BY code LIMIT 500",
            "products" => "SELECT id AS Id, code AS Kod, name AS Ad FROM products WHERE company_id=$c AND (code LIKE $q OR name LIKE $q) ORDER BY code LIMIT 500",
            "suppliers" => "SELECT id AS Id, code AS Kod, name AS Ad FROM accounts WHERE company_id=$c AND is_active=1 AND account_type IN ('Supplier','CustomerAndSupplier') AND (code LIKE $q OR name LIKE $q) ORDER BY code LIMIT 500",
            _ => throw new ArgumentException("Bilinmeyen seçim listesi: " + kind)
        };
        return database.Query(sql, ("$c", companyId), ("$q", term));
    }

    /// <summary>Kod + ad of one lookup record (the gray name box next to a "…" field). ("", "") when not found.</summary>
    public (string Code, string Name) Describe(string kind, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return ("", "");
        var table = kind switch
        {
            "product_groups" => "product_groups", "brands" => "brands", "categories" => "categories", "products" => "products", "suppliers" => "accounts",
            _ => throw new ArgumentException("Bilinmeyen seçim listesi: " + kind)
        };
        var row = database.Query($"SELECT code, name FROM {table} WHERE id=$id", ("$id", id)).Rows.Cast<DataRow>().FirstOrDefault();
        return row == null ? ("", "") : (row[0].ToString()!, row[1].ToString()!);
    }

    /// <summary>İlişkili stok kodu (products.parent_code) → the related card's name, "" if no such card.</summary>
    public string NameOfCode(string companyId, string code) => string.IsNullOrWhiteSpace(code) ? "" :
        database.Query("SELECT name FROM products WHERE company_id=$c AND code=$code COLLATE NOCASE", ("$c", companyId), ("$code", code.Trim())).Rows.Cast<DataRow>().FirstOrDefault()?[0]?.ToString() ?? "";
}
