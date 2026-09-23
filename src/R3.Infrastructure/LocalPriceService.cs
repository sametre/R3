using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record PriceListEdit(
    string Id, string CompanyId, string Code, string Name, string PriceType = "Sales", bool VatIncluded = true, string CurrencyCode = "TRY",
    DateTime? ValidFrom = null, DateTime? ValidTo = null, int Sequence = 0, bool IsActive = true, bool IsCampaign = false);

/// <summary>Result of <see cref="LocalPriceService.ResolveSalesPrice"/>. <see cref="UnitPrice"/> is KDV hariç
/// (sales lines store net unit prices); <see cref="DiscountRate"/> is the customer-group iskonto to put on the line.</summary>
public sealed record SalesPriceResolution(decimal UnitPrice, decimal DiscountRate, string Source, string? PriceListId, decimal ListPrice, bool ListVatIncluded);

public sealed record CustomerPriceGroupEdit(string AccountGroupId, string? PriceListId, decimal DiscountRate);

public enum BulkPriceMode { SetAmount, IncreasePercent, IncreaseAmount, CopyFromList }

/// <summary>Which products a bulk price update touches. Empty filter = every active product of the company.</summary>
public sealed record BulkPriceFilter(IReadOnlyList<string>? ProductIds = null, string? ProductGroupId = null, string? BrandId = null, string? CategoryId = null, bool OnlyExistingPrices = false);

/// <summary>
/// Fiyat Yönetimi, ported from the ASB model after sampling it (2026-09-23): KODFIYAT = price list
/// (FYKGC 1 = satış / 0 = alış - every list named "ALIŞ ..." has 0; FYKKDVDH D/H = KDV dahil/hariç;
/// FYKBASTAR/FYKBITTAR validity; FYKDVZ currency), FIYAT = one price per list × product, LOGFIYAT =
/// change history. R3 keeps the same shape: price_lists (+ type, KDV, currency, validity),
/// product_prices (one row per list × product, per base unit) and product_price_history, which every
/// write here appends to - there is no way to change a price without leaving a history row.
/// </summary>
public sealed class LocalPriceService(StoreDatabase database)
{
    public DataTable PriceLists(string companyId, bool activeOnly = false) => database.Query("""
        SELECT l.id AS Id, l.code AS Kod, l.name AS Ad, l.price_type AS Tip, l.vat_included AS KdvDahil, l.currency_code AS ParaBirimi, l.is_campaign AS Kampanya,
               COALESCE(l.valid_from,'') AS Baslangic, COALESCE(l.valid_to,'') AS Bitis, l.sequence AS Sira, l.is_active AS Aktif,
               (SELECT COUNT(1) FROM product_prices pp WHERE pp.price_list_id=l.id) AS UrunSayisi
        FROM price_lists l WHERE l.company_id=$c AND ($a=0 OR l.is_active=1)
        ORDER BY l.sequence, l.code
        """, ("$c", companyId), ("$a", activeOnly ? 1 : 0));

    public PriceListEdit? GetPriceList(string id)
    {
        var t = database.Query("SELECT id,company_id,code,name,price_type,vat_included,currency_code,valid_from,valid_to,sequence,is_active,is_campaign FROM price_lists WHERE id=$id", ("$id", id));
        if (t.Rows.Count == 0) return null; var r = t.Rows[0];
        static DateTime? D(object v) => v == DBNull.Value || string.IsNullOrWhiteSpace(v?.ToString()) ? null : DateTime.Parse(v.ToString()!);
        return new PriceListEdit(r[0].ToString()!, r[1].ToString()!, r[2].ToString()!, r[3].ToString()!, r[4].ToString()!, Convert.ToInt64(r[5]) == 1, r[6].ToString()!, D(r[7]), D(r[8]), Convert.ToInt32(r[9]), Convert.ToInt64(r[10]) == 1, Convert.ToInt64(r[11]) == 1);
    }

    public string SavePriceList(PriceListEdit edit, string userName)
    {
        if (string.IsNullOrWhiteSpace(edit.CompanyId) || string.IsNullOrWhiteSpace(edit.Code) || string.IsNullOrWhiteSpace(edit.Name)) throw new ArgumentException("Firma, liste kodu ve adı zorunludur.");
        if (edit.PriceType is not ("Sales" or "Purchase")) throw new ArgumentException("Fiyat listesi tipi Satış veya Alış olmalıdır.");
        if (string.IsNullOrWhiteSpace(edit.CurrencyCode)) throw new ArgumentException("Para birimi zorunludur.");
        if (edit.ValidFrom is { } f && edit.ValidTo is { } t && t < f) throw new ArgumentException("Bitiş tarihi başlangıç tarihinden önce olamaz.");
        if (edit.IsCampaign && (edit.ValidFrom == null || edit.ValidTo == null)) throw new ArgumentException("Kampanya için başlangıç ve bitiş tarihi zorunludur.");
        if (edit.IsCampaign && edit.PriceType != "Sales") throw new ArgumentException("Kampanya yalnızca satış fiyat listesi olabilir.");
        var id = string.IsNullOrWhiteSpace(edit.Id) ? Guid.NewGuid().ToString() : edit.Id; var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        if (Scalar(c, tx, "SELECT id FROM price_lists WHERE company_id=$c AND code=$code AND id<>$id", ("$c", edit.CompanyId), ("$code", edit.Code.Trim().ToUpperInvariant()), ("$id", id)) != null)
            throw new ArgumentException($"'{edit.Code.Trim().ToUpperInvariant()}' fiyat listesi kodu zaten kullanılıyor.");
        Exec(c, tx, """
            INSERT INTO price_lists(id,company_id,code,name,is_active,price_type,vat_included,currency_code,valid_from,valid_to,sequence,is_campaign)
            VALUES($id,$c,$code,$name,$active,$type,$vat,$cur,$from,$to,$seq,$campaign)
            ON CONFLICT(id) DO UPDATE SET code=$code,name=$name,is_active=$active,price_type=$type,vat_included=$vat,currency_code=$cur,valid_from=$from,valid_to=$to,sequence=$seq,is_campaign=$campaign
            """, ("$id", id), ("$c", edit.CompanyId), ("$code", edit.Code.Trim().ToUpperInvariant()), ("$name", edit.Name.Trim()), ("$active", edit.IsActive ? 1 : 0),
            ("$type", edit.PriceType), ("$vat", edit.VatIncluded ? 1 : 0), ("$cur", edit.CurrencyCode.Trim().ToUpperInvariant()),
            ("$from", edit.ValidFrom is { } vf ? vf.ToString("yyyy-MM-dd") : DBNull.Value), ("$to", edit.ValidTo is { } vt ? vt.ToString("yyyy-MM-dd") : DBNull.Value), ("$seq", edit.Sequence), ("$campaign", edit.IsCampaign ? 1 : 0));
        Audit(c, tx, edit.CompanyId, "PriceList", id, string.IsNullOrWhiteSpace(edit.Id) ? "PriceListCreated" : "PriceListUpdated", edit.Code, userName, now);
        tx.Commit();
        return id;
    }

    /// <summary>Products with their price in <paramref name="priceListId"/> (Fiyat NULL = not priced yet).</summary>
    public DataTable ProductPrices(string companyId, string priceListId, string? search = null, string? productGroupId = null, bool onlyPriced = false, int limit = 1000)
    {
        var q = $"%{search?.Trim() ?? ""}%";
        return database.Query("""
            SELECT p.id AS UrunId, p.code AS StokKodu, p.name AS StokAdi, COALESCE(g.name,'') AS StokGrubu, COALESCE(u.code,'') AS Birim,
                   p.vat_rate AS Kdv, pp.price AS Fiyat, COALESCE(pp.updated_at,'') AS SonDegisiklik, COALESCE(pp.updated_by,'') AS Degistiren
            FROM products p
            LEFT JOIN product_prices pp ON pp.product_id=p.id AND pp.price_list_id=$l
            LEFT JOIN product_groups g ON g.id=p.product_group_id
            LEFT JOIN units u ON u.id=p.base_unit_id
            WHERE p.company_id=$c AND p.is_active=1 AND ($g='' OR p.product_group_id=$g) AND ($only=0 OR pp.price IS NOT NULL)
              AND (p.code LIKE $q OR p.name LIKE $q OR EXISTS(SELECT 1 FROM product_barcodes b WHERE b.product_id=p.id AND b.is_active=1 AND b.barcode LIKE $q))
            ORDER BY p.code LIMIT $limit
            """, ("$c", companyId), ("$l", priceListId), ("$g", productGroupId ?? ""), ("$only", onlyPriced ? 1 : 0), ("$q", q), ("$limit", limit));
    }

    /// <summary>Sets (or with null, removes) one product's price in a list, recording history.</summary>
    public void SetPrice(string companyId, string priceListId, string productId, decimal? price, string userName, string source = "Manual")
    {
        if (price is < 0) throw new ArgumentException("Fiyat negatif olamaz.");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        EnsureListAndProduct(c, tx, companyId, priceListId, productId);
        Write(c, tx, priceListId, productId, price, userName, source, DateTime.UtcNow.ToString("O"));
        tx.Commit();
    }

    /// <summary>Applies one rule to every product matching <paramref name="filter"/> in a single transaction.
    /// Returns how many prices actually changed. Rounding: result is rounded to <paramref name="roundTo"/>
    /// (e.g. 0.01, 1, 0.5); <paramref name="endWith"/> (e.g. 0.99) makes 123.40 → 123.99.</summary>
    public int BulkUpdate(string companyId, string priceListId, BulkPriceFilter filter, BulkPriceMode mode, decimal value, string userName,
        string? sourceListId = null, decimal roundTo = 0.01m, decimal? endWith = null)
    {
        if (mode == BulkPriceMode.CopyFromList && string.IsNullOrWhiteSpace(sourceListId)) throw new ArgumentException("Kopyalanacak kaynak fiyat listesi seçilmelidir.");
        if (mode == BulkPriceMode.SetAmount && value < 0) throw new ArgumentException("Fiyat negatif olamaz.");
        if (mode == BulkPriceMode.CopyFromList && value <= 0) throw new ArgumentException("Kopyalama çarpanı 0'dan büyük olmalıdır.");
        if (roundTo <= 0) throw new ArgumentException("Yuvarlama adımı 0'dan büyük olmalıdır.");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        EnsureListAndProduct(c, tx, companyId, priceListId, null);
        var where = new List<string> { "p.company_id=$c", "p.is_active=1" }; var parameters = new List<(string, object)> { ("$c", companyId), ("$l", priceListId), ("$src", sourceListId ?? "") };
        if (filter.ProductIds is { Count: > 0 } ids) { where.Add($"p.id IN ({string.Join(",", ids.Select((_, i) => "$p" + i))})"); parameters.AddRange(ids.Select((id, i) => ("$p" + i, (object)id))); }
        if (!string.IsNullOrWhiteSpace(filter.ProductGroupId)) { where.Add("p.product_group_id=$g"); parameters.Add(("$g", filter.ProductGroupId)); }
        if (!string.IsNullOrWhiteSpace(filter.BrandId)) { where.Add("p.brand_id=$b"); parameters.Add(("$b", filter.BrandId)); }
        if (!string.IsNullOrWhiteSpace(filter.CategoryId)) { where.Add("p.category_id=$cat"); parameters.Add(("$cat", filter.CategoryId)); }
        if (filter.OnlyExistingPrices) where.Add("pp.price IS NOT NULL");
        var rows = new List<(string Product, decimal? Current, decimal? Source)>();
        using (var q = c.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = $"""
                SELECT p.id, pp.price, src.price FROM products p
                LEFT JOIN product_prices pp ON pp.product_id=p.id AND pp.price_list_id=$l
                LEFT JOIN product_prices src ON src.product_id=p.id AND src.price_list_id=$src
                WHERE {string.Join(" AND ", where)}
                """;
            foreach (var (n, v) in parameters) q.Parameters.AddWithValue(n, v);
            using var r = q.ExecuteReader();
            while (r.Read()) rows.Add((r.GetString(0), r.IsDBNull(1) ? null : r.GetDecimal(1), r.IsDBNull(2) ? null : r.GetDecimal(2)));
        }
        var now = DateTime.UtcNow.ToString("O"); var changed = 0;
        foreach (var (product, current, source) in rows)
        {
            decimal? target = mode switch
            {
                BulkPriceMode.SetAmount => value,
                BulkPriceMode.IncreasePercent => current is { } cp ? cp * (1 + value / 100m) : null,
                BulkPriceMode.IncreaseAmount => current is { } ca ? ca + value : null,
                BulkPriceMode.CopyFromList => source is { } s ? s * value : null,
                _ => null
            };
            if (target is not { } t) continue;                       // nothing to base the change on
            t = Round(Math.Max(0, t), roundTo, endWith);
            if (current == t) continue;
            Write(c, tx, priceListId, product, t, userName, "Bulk", now);
            changed++;
        }
        Audit(c, tx, companyId, "PriceList", priceListId, "PriceBulkUpdate", $"{mode} {value} → {changed} ürün", userName, now);
        tx.Commit();
        return changed;
    }

    public DataTable History(string companyId, string? productId = null, string? priceListId = null, int limit = 500) => database.Query("""
        SELECT h.changed_at AS Tarih, l.code AS Liste, p.code AS StokKodu, p.name AS StokAdi, h.old_price AS EskiFiyat, h.new_price AS YeniFiyat,
               CASE WHEN h.old_price IS NULL OR h.old_price=0 OR h.new_price IS NULL THEN NULL ELSE ROUND((h.new_price-h.old_price)*100.0/h.old_price,2) END AS DegisimYuzde,
               h.source AS Kaynak, h.changed_by AS Kullanici, h.product_id AS UrunId
        FROM product_price_history h JOIN price_lists l ON l.id=h.price_list_id JOIN products p ON p.id=h.product_id
        WHERE l.company_id=$c AND ($p='' OR h.product_id=$p) AND ($l='' OR h.price_list_id=$l)
        ORDER BY h.changed_at DESC LIMIT $limit
        """, ("$c", companyId), ("$p", productId ?? ""), ("$l", priceListId ?? ""), ("$limit", limit));

    /// <summary>The price a list gives a product on <paramref name="date"/> - null if the list is inactive,
    /// outside its validity window, or has no price for the product. Intended as the single lookup for
    /// labels and (later) sales document defaults.</summary>
    public decimal? GetPrice(string priceListId, string productId, DateTime? date = null)
    {
        var day = (date ?? DateTime.Today).ToString("yyyy-MM-dd");
        var t = database.Query("""
            SELECT pp.price FROM product_prices pp JOIN price_lists l ON l.id=pp.price_list_id
            WHERE pp.price_list_id=$l AND pp.product_id=$p AND l.is_active=1
              AND (l.valid_from IS NULL OR l.valid_from<=$d) AND (l.valid_to IS NULL OR l.valid_to>=$d)
            """, ("$l", priceListId), ("$p", productId), ("$d", day));
        return t.Rows.Count == 0 ? null : Convert.ToDecimal(t.Rows[0][0]);
    }

    /// <summary>
    /// The one place that decides a sales price (satış faturası default, fiyat sorgulama). Order:
    /// 1. an active, in-date kampanya list that prices the product (lowest price if several);
    /// 2. the customer's own list (customer_profiles.price_list_id);
    /// 3. the customer's Müşteri Fiyat Grubu list (account_groups.price_list_id);
    /// 4. the default satış list (active, in-date, non-campaign, lowest sıra).
    /// A list that does not price the product is skipped, not treated as 0; null when nothing prices it.
    /// The group iskonto is a customer term: it is returned with every non-campaign price.
    /// </summary>
    public SalesPriceResolution? ResolveSalesPrice(string companyId, string productId, string? accountId = null, DateTime? date = null)
    {
        var day = (date ?? DateTime.Today).ToString("yyyy-MM-dd");
        var vatRate = database.Query("SELECT vat_rate FROM products WHERE id=$p AND company_id=$c", ("$p", productId), ("$c", companyId)).Rows.Cast<DataRow>().Select(r => Convert.ToDecimal(r[0])).FirstOrDefault();
        const string validList = "l.company_id=$c AND l.is_active=1 AND l.price_type='Sales' AND (l.valid_from IS NULL OR l.valid_from<=$d) AND (l.valid_to IS NULL OR l.valid_to>=$d)";
        DataRow? Price(string where, string order, params (string, object)[] extra)
        {
            var parameters = new List<(string, object)> { ("$p", productId), ("$c", companyId), ("$d", day) }; parameters.AddRange(extra);
            return database.Query($"""
                SELECT l.id, l.name, l.vat_included, pp.price FROM product_prices pp JOIN price_lists l ON l.id=pp.price_list_id
                WHERE pp.product_id=$p AND {validList} AND {where} ORDER BY {order} LIMIT 1
                """, parameters.ToArray()).Rows.Cast<DataRow>().FirstOrDefault();
        }
        SalesPriceResolution From(DataRow row, string source, decimal discount)
        {
            var listPrice = Convert.ToDecimal(row["price"]); var vatIncluded = Convert.ToInt64(row["vat_included"]) == 1;
            return new(Math.Round(vatIncluded ? listPrice / (1 + vatRate / 100m) : listPrice, 4), discount, source, row["id"].ToString(), listPrice, vatIncluded);
        }

        string? customerList = null, groupList = null; decimal groupDiscount = 0; var groupName = "";
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            var customer = database.Query("""
                SELECT cp.price_list_id, g.price_list_id, COALESCE(g.discount_rate,0), COALESCE(g.name,'')
                FROM accounts a LEFT JOIN customer_profiles cp ON cp.account_id=a.id LEFT JOIN account_groups g ON g.id=a.account_group_id
                WHERE a.id=$a AND a.company_id=$c
                """, ("$a", accountId), ("$c", companyId)).Rows.Cast<DataRow>().FirstOrDefault();
            if (customer != null)
            {
                customerList = customer[0] == DBNull.Value ? null : customer[0].ToString();
                groupList = customer[1] == DBNull.Value ? null : customer[1].ToString();
                groupDiscount = Convert.ToDecimal(customer[2]); groupName = customer[3].ToString()!;
            }
        }
        if (Price("l.is_campaign=1", "pp.price, l.sequence") is { } campaign) return From(campaign, $"Kampanya: {campaign["name"]}", 0);
        if (customerList != null && Price("l.id=$l", "l.sequence", ("$l", customerList)) is { } own) return From(own, $"Cari fiyat listesi: {own["name"]}", groupDiscount);
        if (groupList != null && Price("l.id=$l", "l.sequence", ("$l", groupList)) is { } group) return From(group, $"Müşteri fiyat grubu {groupName}: {group["name"]}", groupDiscount);
        return Price("l.is_campaign=0", "l.sequence, l.code") is { } fallback ? From(fallback, $"Varsayılan satış listesi: {fallback["name"]}", groupDiscount) : null;
    }

    public DataTable CustomerPriceGroups(string companyId) => database.Query("""
        SELECT g.id AS Id, g.code AS Kod, g.name AS Ad, COALESCE(g.price_list_id,'') AS FiyatListesiId, COALESCE(l.code || ' — ' || l.name,'(Liste yok)') AS FiyatListesi,
               g.discount_rate AS Iskonto, (SELECT COUNT(1) FROM accounts a WHERE a.account_group_id=g.id AND a.is_active=1) AS CariSayisi, g.is_active AS Aktif
        FROM account_groups g LEFT JOIN price_lists l ON l.id=g.price_list_id
        WHERE g.company_id=$c ORDER BY g.code
        """, ("$c", companyId));

    public void SaveCustomerPriceGroup(string companyId, CustomerPriceGroupEdit edit, string userName)
    {
        if (edit.DiscountRate is < 0 or > 100) throw new ArgumentException("İskonto 0 ile 100 arasında olmalıdır.");
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        if (Scalar(c, tx, "SELECT id FROM account_groups WHERE id=$g AND company_id=$c", ("$g", edit.AccountGroupId), ("$c", companyId)) == null) throw new ArgumentException("Cari grubu bulunamadı.");
        if (!string.IsNullOrWhiteSpace(edit.PriceListId) && Scalar(c, tx, "SELECT id FROM price_lists WHERE id=$l AND company_id=$c AND price_type='Sales' AND is_campaign=0", ("$l", edit.PriceListId), ("$c", companyId)) == null)
            throw new ArgumentException("Müşteri fiyat grubuna yalnızca kampanya olmayan bir satış fiyat listesi atanabilir.");
        Exec(c, tx, "UPDATE account_groups SET price_list_id=$l, discount_rate=$d WHERE id=$g",
            ("$l", string.IsNullOrWhiteSpace(edit.PriceListId) ? DBNull.Value : edit.PriceListId), ("$d", edit.DiscountRate), ("$g", edit.AccountGroupId));
        Audit(c, tx, companyId, "AccountGroup", edit.AccountGroupId, "CustomerPriceGroupUpdated", $"liste={edit.PriceListId}, iskonto={edit.DiscountRate}", userName, now);
        tx.Commit();
    }

    public static decimal Round(decimal value, decimal roundTo, decimal? endWith)
    {
        var rounded = Math.Round(value / roundTo, MidpointRounding.AwayFromZero) * roundTo;
        if (endWith is { } ending and >= 0 and < 1) rounded = Math.Floor(rounded) + ending;
        return Math.Round(rounded, 4);
    }

    private static void Write(SqliteConnection c, SqliteTransaction tx, string listId, string productId, decimal? price, string userName, string source, string now)
    {
        var old = Scalar(c, tx, "SELECT price FROM product_prices WHERE price_list_id=$l AND product_id=$p", ("$l", listId), ("$p", productId));
        decimal? oldPrice = old == null ? null : decimal.Parse(old, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
        if (oldPrice == price) return;
        if (price is { } value)
            Exec(c, tx, """
                INSERT INTO product_prices(id,price_list_id,product_id,price,updated_at,updated_by) VALUES($id,$l,$p,$price,$now,$user)
                ON CONFLICT(price_list_id,product_id) DO UPDATE SET price=$price,updated_at=$now,updated_by=$user
                """, ("$id", Guid.NewGuid().ToString()), ("$l", listId), ("$p", productId), ("$price", value), ("$now", now), ("$user", userName));
        else Exec(c, tx, "DELETE FROM product_prices WHERE price_list_id=$l AND product_id=$p", ("$l", listId), ("$p", productId));
        Exec(c, tx, "INSERT INTO product_price_history(id,price_list_id,product_id,old_price,new_price,changed_at,changed_by,source) VALUES($id,$l,$p,$old,$new,$now,$user,$src)",
            ("$id", Guid.NewGuid().ToString()), ("$l", listId), ("$p", productId), ("$old", (object?)oldPrice ?? DBNull.Value), ("$new", (object?)price ?? DBNull.Value), ("$now", now), ("$user", userName), ("$src", source));
    }

    private static void EnsureListAndProduct(SqliteConnection c, SqliteTransaction tx, string companyId, string listId, string? productId)
    {
        if (Scalar(c, tx, "SELECT id FROM price_lists WHERE id=$l AND company_id=$c", ("$l", listId), ("$c", companyId)) == null) throw new ArgumentException("Fiyat listesi bulunamadı.");
        if (productId != null && Scalar(c, tx, "SELECT id FROM products WHERE id=$p AND company_id=$c", ("$p", productId), ("$c", companyId)) == null) throw new ArgumentException("Ürün bulunamadı.");
    }

    private static string? Scalar(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] parameters)
    {
        using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = sql;
        foreach (var (n, v) in parameters) q.Parameters.AddWithValue(n, v);
        var value = q.ExecuteScalar();
        return value == null || value == DBNull.Value ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Exec(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] parameters)
    {
        using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = sql;
        foreach (var (n, v) in parameters) q.Parameters.AddWithValue(n, v);
        q.ExecuteNonQuery();
    }

    private static void Audit(SqliteConnection c, SqliteTransaction tx, string companyId, string type, string id, string action, string detail, string userName, string now) =>
        Exec(c, tx, "INSERT INTO audit_logs(id,user_id,company_id,entity_type,entity_id,action,new_values,created_at) VALUES($id,$user,$c,$type,$entity,$action,$new,$now)",
            ("$id", Guid.NewGuid().ToString()), ("$user", userName), ("$c", companyId), ("$type", type), ("$entity", id), ("$action", action), ("$new", detail), ("$now", now));
}
