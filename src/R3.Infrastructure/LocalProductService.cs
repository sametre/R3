using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record ProductChildEdit(string Id, string Code, string Name, string SizeCode = "", string ColorCode = "", string ModelCode = "", string Barcode = "", string UnitId = "", decimal Quantity = 1, bool IsPrimary = false, bool IsActive = true, string VariantId = "", string SizeType = "");
public sealed record ProductUnitEdit(string Id, string UnitId, int Sequence, decimal ConversionFactor, bool IsBaseUnit, bool IsSalesUnit = true, bool IsPurchaseUnit = true, bool IsActive = true);
public sealed record ProductSupplierEdit(string Id, string SupplierAccountId, string SupplierProductCode = "", bool IsActive = true, int LeadTimeDays = 0, int ExtraLeadTimeDays = 0, int Priority = 1, decimal? MinimumOrderQuantity = null);
public sealed record ProductInventoryPolicyEdit(int DeliveryLeadTimeDays = 0, int MaximumDeliveryLeadTimeDays = 0, string LotTrackingType = "None", int PieceCount = 0, string ShipmentLocationType = "");
public sealed record ProductAggregateEdit(string Id, string CompanyId, string Code, string Name, string BrandId, string CategoryId, string UnitId, string ProductType, decimal VatRate, bool IsActive, IReadOnlyList<ProductChildEdit> Variants, IReadOnlyList<ProductChildEdit> Barcodes, decimal PurchaseVatRate = 0, decimal ExciseRate = 0, decimal MinimumStock = 0, decimal MaximumStock = 0, decimal MinimumOrderQuantity = 0, decimal OrderMultiple = 0, bool IsSellable = true, string ImagePath = "", string ParentCode = "", bool IsDefinitionComplete = false, bool CanQuote = true, bool AllowFreeIssue = false, bool IsBundle = false, decimal ExciseUnitPrice = 0, IReadOnlyList<ProductUnitEdit>? Units = null, IReadOnlyList<ProductSupplierEdit>? Suppliers = null, ProductInventoryPolicyEdit? Policy = null)
{
    public IReadOnlyList<ProductUnitEdit> Units { get; init; } = Units ?? [];
    public IReadOnlyList<ProductSupplierEdit> Suppliers { get; init; } = Suppliers ?? [];
    public ProductInventoryPolicyEdit Policy { get; init; } = Policy ?? new ProductInventoryPolicyEdit();
    // Sınıflandırma + depo bazlı min/max. null = "leave what is stored" so callers that predate these
    // fields (imports, bulk tools, older tests) never wipe them; "" / [] clears them explicitly.
    public string? ProductGroupId { get; init; }
    public string? OriginCountryId { get; init; }
    public IReadOnlyList<ProductWarehousePolicyEdit>? WarehousePolicies { get; init; }
}
public sealed record ProductWarehousePolicyEdit(string WarehouseId, decimal MinimumStock, decimal MaximumStock);
public sealed record ProductDetailEdit(ProductAggregateEdit Product, IReadOnlyList<ProductChildEdit> Variants, IReadOnlyList<ProductChildEdit> Barcodes);
public sealed record ProductListQuery(
    string CompanyId,
    string? Search = null,
    string? Code = null,
    string? Name = null,
    string? Barcode = null,
    string? BrandId = null,
    string? CategoryId = null,
    string? ProductType = null,
    bool? ActiveOnly = null,
    bool NegativeStockOnly = false,
    bool OutOfStockOnly = false,
    bool BelowMinimumOnly = false,
    int Page = 1,
    int PageSize = 50,
    string SortColumn = "code",
    bool SortDescending = false,
    string? ProductGroupId = null);
public sealed record ProductListPage(DataTable Rows, int Page, int PageSize, int TotalCount);
public sealed class LocalProductService(StoreDatabase database)
{
    public ProductListPage SearchPage(ProductListQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.CompanyId)) throw new ArgumentException("Firma zorunludur.");
        var page = Math.Max(1, query.Page); var pageSize = Math.Clamp(query.PageSize, 1, 500);
        var where = new List<string> { "p.company_id=$company" }; var parameters = new List<(string Name, object Value)> { ("$company", query.CompanyId) };
        AddLike(where, parameters, "p.code", "$code", query.Code);
        AddLike(where, parameters, "p.name", "$name", query.Name);
        AddLike(where, parameters, "barcodeRows.barcode", "$barcode", query.Barcode, "EXISTS(SELECT 1 FROM product_barcodes barcodeRows WHERE barcodeRows.product_id=p.id AND barcodeRows.is_active=1 AND ");
        if (!string.IsNullOrWhiteSpace(query.Search)) { where.Add("(p.code LIKE $search OR p.name LIKE $search OR EXISTS(SELECT 1 FROM product_barcodes sb WHERE sb.product_id=p.id AND sb.is_active=1 AND sb.barcode LIKE $search))"); parameters.Add(("$search", $"%{query.Search.Trim()}%")); }
        if (!string.IsNullOrWhiteSpace(query.BrandId)) { where.Add("p.brand_id=$brand"); parameters.Add(("$brand", query.BrandId)); }
        if (!string.IsNullOrWhiteSpace(query.CategoryId)) { where.Add("p.category_id=$category"); parameters.Add(("$category", query.CategoryId)); }
        if (!string.IsNullOrWhiteSpace(query.ProductType)) { where.Add("p.product_type=$type"); parameters.Add(("$type", query.ProductType)); }
        if (!string.IsNullOrWhiteSpace(query.ProductGroupId)) { where.Add("p.product_group_id=$group"); parameters.Add(("$group", query.ProductGroupId)); }
        if (query.ActiveOnly.HasValue) where.Add(query.ActiveOnly.Value ? "p.is_active=1" : "p.is_active=0");
        var available = "COALESCE((SELECT SUM(ib.quantity_available) FROM inventory_balances ib WHERE ib.product_id=p.id),0)";
        if (query.NegativeStockOnly) where.Add($"{available}<0");
        if (query.OutOfStockOnly) where.Add($"{available}<=0");
        if (query.BelowMinimumOnly) where.Add($"p.minimum_stock>0 AND {available}<p.minimum_stock");
        var sort = query.SortColumn.ToLowerInvariant() switch { "name" => "p.name", "updated_at" => "p.updated_at", "available" => available, "brand" => "b.name", "category" => "c.name", "group" => "(SELECT g.name FROM product_groups g WHERE g.id=p.product_group_id)", _ => "p.code" };
        var direction = query.SortDescending ? "DESC" : "ASC";
        var sql = $"""
            SELECT p.id AS Id,p.code AS StokKodu,p.name AS StokAdi,p.product_type AS UrunTipi,u.name AS AnaBirim,
            COALESCE((SELECT barcode FROM product_barcodes pb WHERE pb.product_id=p.id AND pb.is_active=1 AND pb.is_primary=1 LIMIT 1),'') AS BirincilBarkod,
            COALESCE(b.name,'') AS Marka,COALESCE(c.name,'') AS Kategori,
            COALESCE((SELECT g.name FROM product_groups g WHERE g.id=p.product_group_id),'') AS StokGrubu,
            COALESCE((SELECT co.name FROM countries co WHERE co.id=p.origin_country_id),'') AS Mense,
            COALESCE((SELECT SUM(ib.quantity_on_hand) FROM inventory_balances ib WHERE ib.product_id=p.id),0) AS MevcutStok,
            COALESCE((SELECT SUM(ib.quantity_reserved) FROM inventory_balances ib WHERE ib.product_id=p.id),0) AS RezerveStok,
            {available} AS KullanilabilirStok,p.is_active AS Aktif,p.is_sellable AS SatisaAcik,
            p.is_definition_complete AS TanimTamam,p.updated_at AS SonGuncelleme,p.minimum_stock AS MinimumStok
            FROM products p JOIN units u ON u.id=p.base_unit_id LEFT JOIN brands b ON b.id=p.brand_id LEFT JOIN categories c ON c.id=p.category_id
            WHERE {string.Join(" AND ", where)} ORDER BY {sort} {direction} LIMIT $limit OFFSET $offset
            """;
        parameters.Add(("$limit", pageSize)); parameters.Add(("$offset", (page - 1) * pageSize));
        var rows = QueryWithParameters(sql, parameters);
        var countSql = $"SELECT COUNT(*) FROM products p LEFT JOIN brands b ON b.id=p.brand_id LEFT JOIN categories c ON c.id=p.category_id WHERE {string.Join(" AND ", where)}";
        var countParameters = parameters.Where(x => x.Name is not "$limit" and not "$offset").ToArray();
        var count = Convert.ToInt32(database.Query(countSql, countParameters).Rows[0][0]);
        return new(rows, page, pageSize, count);
    }

    public void SetActive(string productId, string companyId, bool isActive)
    {
        using var connection = database.OpenConnection(); using var tx = connection.BeginTransaction();
        using var update = connection.CreateCommand(); update.Transaction = tx; update.CommandText = "UPDATE products SET is_active=$active,updated_at=$now WHERE id=$id AND company_id=$company"; update.Parameters.AddWithValue("$active", isActive ? 1 : 0); update.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O")); update.Parameters.AddWithValue("$id", productId); update.Parameters.AddWithValue("$company", companyId);
        if (update.ExecuteNonQuery() == 0) throw new KeyNotFoundException("Stok kartı bulunamadı.");
        using var audit = connection.CreateCommand(); audit.Transaction = tx; audit.CommandText = "INSERT INTO audit_logs(id,entity_type,entity_id,company_id,action,new_values,created_at) VALUES($log,'Product',$id,$company,$action,$new,$now)"; audit.Parameters.AddWithValue("$log", Guid.NewGuid().ToString()); audit.Parameters.AddWithValue("$id", productId); audit.Parameters.AddWithValue("$company", companyId); audit.Parameters.AddWithValue("$action", isActive ? "ACTIVATE" : "DEACTIVATE"); audit.Parameters.AddWithValue("$new", $"Active={isActive}"); audit.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O")); audit.ExecuteNonQuery(); tx.Commit();
    }

    public string Copy(ProductAggregateEdit source, string newCode, string newName)
    {
        var variants = source.Variants.Select(x => x with { Id = "" }).ToArray();
        var units = source.Units.Select(x => x with { Id = "" }).ToArray();
        var suppliers = source.Suppliers.Select(x => x with { Id = "" }).ToArray();
        // Barcodes are intentionally not copied: they are globally unique and must be assigned explicitly.
        var copy = source with { Id = "", Code = newCode, Name = newName, Variants = variants, Barcodes = [], Units = units, Suppliers = suppliers };
        Save(copy);
        return database.Query("SELECT id FROM products WHERE company_id=$c AND code=$code COLLATE NOCASE", ("$c", source.CompanyId), ("$code", newCode.Trim())).Rows[0][0].ToString()!;
    }

    private DataTable QueryWithParameters(string sql, IEnumerable<(string Name, object Value)> parameters)
    {
        using var connection = database.OpenConnection(); using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        using var reader = command.ExecuteReader(); var table = new DataTable(); StoreDatabase.LoadSafely(table, reader); return table;
    }

    private static void AddLike(List<string> where, List<(string Name, object Value)> parameters, string column, string parameter, string? value, string? existsPrefix = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var expression = $"{column} LIKE {parameter}";
        if (existsPrefix != null) expression = existsPrefix + expression + ")";
        where.Add(expression); parameters.Add((parameter, $"%{value.Trim()}%"));
    }

    public DataTable Search(string? search = null, bool? belowMinimumStock = null, bool? outOfStock = null, bool? activeOnly = null, string? companyId = null)
    {
        var q = search?.Trim() ?? "";
        var predicates = new List<string> { "(p.code LIKE $q OR p.name LIKE $q OR EXISTS(SELECT 1 FROM product_barcodes z WHERE z.product_id=p.id AND z.barcode LIKE $q))" };
        if (!string.IsNullOrWhiteSpace(companyId)) predicates.Add("p.company_id=$company");
        if (belowMinimumStock == true) predicates.Add("p.minimum_stock > 0 AND COALESCE((SELECT SUM(b.quantity_available) FROM inventory_balances b WHERE b.product_id=p.id),0) < p.minimum_stock");
        if (outOfStock == true) predicates.Add("COALESCE((SELECT SUM(b.quantity_available) FROM inventory_balances b WHERE b.product_id=p.id),0) <= 0");
        if (activeOnly == false) predicates.Add("p.is_active=0"); else if (activeOnly == true) predicates.Add("p.is_active=1");
        return database.Query($"""
            SELECT p.id AS Id,p.code AS StokKodu,p.code AS Kod,p.name AS StokAdi,p.name AS Ad,COALESCE(b.name,'') AS Marka,COALESCE(c.name,'') AS Kategori,
            u.name AS AnaBirim,p.product_type AS UrunTipi,COALESCE((SELECT barcode FROM product_barcodes x WHERE x.product_id=p.id AND x.is_active=1 AND x.is_primary=1 LIMIT 1),'') AS BirincilBarkod,
            COALESCE((SELECT SUM(x.quantity_on_hand) FROM inventory_balances x WHERE x.product_id=p.id),0) AS MevcutStok,
            COALESCE((SELECT SUM(x.quantity_reserved) FROM inventory_balances x WHERE x.product_id=p.id),0) AS RezerveStok,
            COALESCE((SELECT SUM(x.quantity_available) FROM inventory_balances x WHERE x.product_id=p.id),0) AS KullanilabilirStok,
            p.is_active AS Aktif,p.is_sellable AS SatisaAcik,p.is_definition_complete AS TanimTamam,
            p.updated_at AS SonGuncelleme,p.minimum_stock AS MinimumStok,
            (SELECT COUNT(*) FROM product_variants v WHERE v.product_id=p.id AND v.is_active=1) AS Varyant,
            (SELECT COUNT(*) FROM product_barcodes x WHERE x.product_id=p.id AND x.is_active=1) AS Barkod
            FROM products p JOIN units u ON u.id=p.base_unit_id LEFT JOIN brands b ON b.id=p.brand_id LEFT JOIN categories c ON c.id=p.category_id
            WHERE {string.Join(" AND ", predicates)} ORDER BY p.code
            """, companyId is null ? new[] { ("$q", (object)$"%{q}%") } : new[] { ("$q", (object)$"%{q}%"), ("$company", companyId) });
    }
    public ProductDetailEdit? GetDetail(string productId, string companyId)
    {
        using var c = database.OpenConnection();
        using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT id,company_id,code,name,COALESCE(brand_id,''),COALESCE(category_id,''),base_unit_id,product_type,vat_rate,is_active,purchase_vat_rate,excise_rate,minimum_stock,maximum_stock,minimum_order_quantity,order_multiple,is_sellable,COALESCE(image_path,''),parent_code,is_definition_complete,can_quote,allow_free_issue,is_bundle,excise_unit_price FROM products WHERE id=$id AND company_id=$company"; cmd.Parameters.AddWithValue("$id", productId); cmd.Parameters.AddWithValue("$company", companyId);
        using var r = cmd.ExecuteReader(); if (!r.Read()) return null;
        var policy = new ProductInventoryPolicyEdit();
        using (var pc = c.CreateCommand()) { pc.CommandText = "SELECT delivery_lead_time_days,maximum_delivery_lead_time_days,lot_tracking_type,piece_count,shipment_location_type FROM product_inventory_policies WHERE product_id=$p"; pc.Parameters.AddWithValue("$p", productId); using var pr = pc.ExecuteReader(); if (pr.Read()) policy = new ProductInventoryPolicyEdit(pr.GetInt32(0), pr.GetInt32(1), pr.GetString(2), pr.GetInt32(3), pr.GetString(4)); }
        var product = new ProductAggregateEdit(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetDecimal(8), r.GetBoolean(9), [], [], r.GetDecimal(10), r.GetDecimal(11), r.GetDecimal(12), r.GetDecimal(13), r.GetDecimal(14), r.GetDecimal(15), r.GetBoolean(16), r.GetString(17), r.GetString(18), r.GetBoolean(19), r.GetBoolean(20), r.GetBoolean(21), r.GetBoolean(22), r.GetDecimal(23), Policy: policy);
        var variants = new List<ProductChildEdit>(); var barcodes = new List<ProductChildEdit>(); var units = new List<ProductUnitEdit>(); var suppliers = new List<ProductSupplierEdit>();
        using var v = c.CreateCommand(); v.CommandText = "SELECT id,code,name,COALESCE(size_code,''),COALESCE(color_code,''),COALESCE(model_code,''),is_active,COALESCE(size_type,'') FROM product_variants WHERE product_id=$p ORDER BY code"; v.Parameters.AddWithValue("$p", productId); using var vr = v.ExecuteReader(); while (vr.Read()) variants.Add(new ProductChildEdit(vr.GetString(0), vr.GetString(1), vr.GetString(2), vr.GetString(3), vr.GetString(4), vr.GetString(5), IsActive: vr.GetBoolean(6), SizeType: vr.GetString(7)));
        using var b = c.CreateCommand(); b.CommandText = "SELECT id,COALESCE(variant_id,''),COALESCE(unit_id,''),barcode,quantity,is_primary,is_active FROM product_barcodes WHERE product_id=$p ORDER BY barcode"; b.Parameters.AddWithValue("$p", productId); using var br = b.ExecuteReader(); while (br.Read()) barcodes.Add(new ProductChildEdit(br.GetString(0), "", "", Barcode: br.GetString(3), UnitId: br.GetString(2), Quantity: br.GetDecimal(4), IsPrimary: br.GetBoolean(5), IsActive: br.GetBoolean(6), VariantId: br.GetString(1)));
        using var u = c.CreateCommand(); u.CommandText = "SELECT id,unit_id,sequence,conversion_factor,is_base_unit,is_sales_unit,is_purchase_unit,is_active FROM product_units WHERE product_id=$p ORDER BY sequence"; u.Parameters.AddWithValue("$p", productId); using var ur = u.ExecuteReader(); while (ur.Read()) units.Add(new ProductUnitEdit(ur.GetString(0), ur.GetString(1), ur.GetInt32(2), ur.GetDecimal(3), ur.GetBoolean(4), ur.GetBoolean(5), ur.GetBoolean(6), ur.GetBoolean(7)));
        using var s = c.CreateCommand(); s.CommandText = "SELECT id,supplier_account_id,supplier_product_code,is_active,lead_time_days,extra_lead_time_days,priority,minimum_order_quantity FROM product_suppliers WHERE product_id=$p ORDER BY priority"; s.Parameters.AddWithValue("$p", productId); using var sr = s.ExecuteReader(); while (sr.Read()) suppliers.Add(new ProductSupplierEdit(sr.GetString(0), sr.GetString(1), sr.GetString(2), sr.GetBoolean(3), sr.GetInt32(4), sr.GetInt32(5), sr.GetInt32(6), sr.IsDBNull(7) ? null : sr.GetDecimal(7)));
        var classification = new List<string>();
        using (var cl = c.CreateCommand()) { cl.CommandText = "SELECT COALESCE(product_group_id,''),COALESCE(origin_country_id,'') FROM products WHERE id=$p"; cl.Parameters.AddWithValue("$p", productId); using var clr = cl.ExecuteReader(); if (clr.Read()) { classification.Add(clr.GetString(0)); classification.Add(clr.GetString(1)); } }
        var warehousePolicies = new List<ProductWarehousePolicyEdit>();
        using (var wp = c.CreateCommand()) { wp.CommandText = "SELECT warehouse_id,minimum_stock,maximum_stock FROM product_warehouse_policies WHERE product_id=$p"; wp.Parameters.AddWithValue("$p", productId); using var wpr = wp.ExecuteReader(); while (wpr.Read()) warehousePolicies.Add(new ProductWarehousePolicyEdit(wpr.GetString(0), wpr.GetDecimal(1), wpr.GetDecimal(2))); }
        return new ProductDetailEdit(product with
        {
            Variants = variants, Barcodes = barcodes, Units = units, Suppliers = suppliers,
            ProductGroupId = classification.ElementAtOrDefault(0) ?? "", OriginCountryId = classification.ElementAtOrDefault(1) ?? "", WarehousePolicies = warehousePolicies
        }, variants, barcodes);
    }
    public void Save(ProductAggregateEdit edit)
    {
        if (string.IsNullOrWhiteSpace(edit.CompanyId) || string.IsNullOrWhiteSpace(edit.Code) || string.IsNullOrWhiteSpace(edit.Name) || string.IsNullOrWhiteSpace(edit.UnitId)) throw new ArgumentException("Firma, kod, ad ve temel birim zorunludur.");
        if (edit.VatRate is < 0 or > 100 || edit.PurchaseVatRate is < 0 or > 100) throw new ArgumentException("KDV 0 ile 100 arasında olmalıdır.");
        if (edit.ExciseRate is < 0 or > 100 || edit.ExciseUnitPrice < 0) throw new ArgumentException("ÖTV oranı 0 ile 100 arasında olmalı, tutar negatif olamaz.");
        if (edit.MinimumStock < 0 || edit.MaximumStock < 0 || edit.MinimumOrderQuantity < 0 || edit.OrderMultiple < 0) throw new ArgumentException("Stok ve sipariş değerleri negatif olamaz.");
        if (edit.MaximumStock > 0 && edit.MaximumStock < edit.MinimumStock) throw new ArgumentException("Maksimum stok, minimum stoktan küçük olamaz.");
        if (edit.Policy.DeliveryLeadTimeDays < 0 || edit.Policy.MaximumDeliveryLeadTimeDays < 0 || edit.Policy.PieceCount < 0) throw new ArgumentException("Teslim süresi ve parça sayısı negatif olamaz.");
        if (edit.Policy.MaximumDeliveryLeadTimeDays > 0 && edit.Policy.MaximumDeliveryLeadTimeDays < edit.Policy.DeliveryLeadTimeDays) throw new ArgumentException("Maksimum teslim süresi, satış teslim süresinden küçük olamaz.");
        // The product's base unit is also a ProductUnit. Older callers only supplied
        // Product.BaseUnitId, so materialize that canonical child for backward compatibility.
        IReadOnlyList<ProductUnitEdit> units = edit.Units.Count == 0
            ? new[] { new ProductUnitEdit("", edit.UnitId, 1, 1, true) }
            : edit.Units;
        // Barkod ↔ Ürün Birimi (§15 cutover): for a barcode of a non-base unit, ProductUnit.ConversionFactor is the
        // single authority - a missing unit is created from the barcode's factor, an existing one overrides the
        // barcode's factor. Base-unit barcodes keep their own quantity (see LocalBarcodeResolver).
        var unitList = units.ToList(); var alignedBarcodes = new List<ProductChildEdit>();
        foreach (var barcode in edit.Barcodes)
        {
            if (string.IsNullOrWhiteSpace(barcode.UnitId) || string.Equals(barcode.UnitId, edit.UnitId, StringComparison.OrdinalIgnoreCase)) { alignedBarcodes.Add(barcode); continue; }
            var unit = unitList.FirstOrDefault(u => string.Equals(u.UnitId, barcode.UnitId, StringComparison.OrdinalIgnoreCase));
            if (unit == null)
            {
                if (barcode.Quantity <= 0) throw new ArgumentException("Barkod katsayısı 0'dan büyük olmalıdır.");
                unit = new ProductUnitEdit("", barcode.UnitId, unitList.Count == 0 ? 1 : unitList.Max(u => u.Sequence) + 1, barcode.Quantity, false);
                unitList.Add(unit);
            }
            alignedBarcodes.Add(barcode with { Quantity = unit.ConversionFactor });
        }
        units = unitList; edit = edit with { Barcodes = alignedBarcodes };
        ValidateUnits(units);
        ValidateSuppliers(edit.Suppliers);
        ValidateVariants(edit.Variants);
        ValidateBarcodes(edit.Barcodes);
        ValidateWarehousePolicies(edit.WarehousePolicies);
        var isNew = string.IsNullOrWhiteSpace(edit.Id);
        var id = isNew ? Guid.NewGuid().ToString() : edit.Id; var now = DateTime.UtcNow.ToString("O");
        using var connection = database.OpenConnection(); using var tx = connection.BeginTransaction();
        if (edit.Units.Count == 0 && !isNew)
        {
            // A caller that sends no unit list (imports, bulk tools) means "keep the stored units": reuse the stored
            // rows' ids (re-inserting hit UNIQUE(product_id,unit_id)) and keep stored units the list does not mention,
            // instead of deactivating them.
            var stored = new List<ProductUnitEdit>();
            using (var existingUnits = connection.CreateCommand())
            {
                existingUnits.Transaction = tx; existingUnits.CommandText = "SELECT id,unit_id,sequence,conversion_factor,is_base_unit,is_sales_unit,is_purchase_unit,is_active FROM product_units WHERE product_id=$p";
                existingUnits.Parameters.AddWithValue("$p", id);
                using var r = existingUnits.ExecuteReader();
                while (r.Read()) stored.Add(new ProductUnitEdit(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetDecimal(3), r.GetBoolean(4), r.GetBoolean(5), r.GetBoolean(6), r.GetBoolean(7)));
            }
            var merged = units.Select(u => stored.FirstOrDefault(x => string.Equals(x.UnitId, u.UnitId, StringComparison.OrdinalIgnoreCase)) is { } row ? u with { Id = row.Id, ConversionFactor = u.IsBaseUnit ? 1 : row.ConversionFactor } : u).ToList();
            merged.AddRange(stored.Where(x => !merged.Any(m => string.Equals(m.UnitId, x.UnitId, StringComparison.OrdinalIgnoreCase)) && !x.IsBaseUnit));
            units = merged;
            edit = edit with { Barcodes = edit.Barcodes.Select(b => !string.IsNullOrWhiteSpace(b.UnitId) && merged.FirstOrDefault(u => !u.IsBaseUnit && string.Equals(u.UnitId, b.UnitId, StringComparison.OrdinalIgnoreCase)) is { } m ? b with { Quantity = m.ConversionFactor } : b).ToList() };
        }
        using (var master = connection.CreateCommand())
        {
            master.Transaction = tx; master.CommandText = "SELECT COUNT(*) FROM units WHERE id=$unit AND company_id=$company"; master.Parameters.AddWithValue("$unit", edit.UnitId); master.Parameters.AddWithValue("$company", edit.CompanyId);
            if (Convert.ToInt64(master.ExecuteScalar()) == 0) throw new ArgumentException("Temel birim seçilen firma kapsamında bulunamadı.");
        }
        string? oldSnapshot = null;
        if (!isNew)
        {
            using var old = connection.CreateCommand(); old.Transaction = tx; old.CommandText = "SELECT code,name,is_active,is_sellable,minimum_stock,maximum_stock FROM products WHERE id=$id AND company_id=$company"; old.Parameters.AddWithValue("$id", id); old.Parameters.AddWithValue("$company", edit.CompanyId);
            using (var oldReader = old.ExecuteReader()) if (oldReader.Read()) oldSnapshot = $"Code={oldReader.GetString(0)};Name={oldReader.GetString(1)};Active={oldReader.GetBoolean(2)};Sellable={oldReader.GetBoolean(3)};MinStock={oldReader.GetDecimal(4)};MaxStock={oldReader.GetDecimal(5)}";
        }
        using var cmd = connection.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "SELECT COUNT(*) FROM products WHERE company_id=$company AND code=$code AND id<>$id"; cmd.Parameters.AddWithValue("$company", edit.CompanyId); cmd.Parameters.AddWithValue("$code", edit.Code.Trim().ToUpperInvariant()); cmd.Parameters.AddWithValue("$id", id); if (Convert.ToInt64(cmd.ExecuteScalar()) > 0) throw new ArgumentException("Bu ürün kodu firma içinde zaten kullanılıyor.");
        cmd.Parameters.Clear(); cmd.CommandText = "INSERT INTO products(id,company_id,code,name,brand_id,category_id,base_unit_id,product_type,vat_rate,purchase_vat_rate,excise_rate,excise_unit_price,minimum_stock,maximum_stock,minimum_order_quantity,order_multiple,is_sellable,is_active,image_path,parent_code,is_definition_complete,can_quote,allow_free_issue,is_bundle,created_at,updated_at) VALUES($id,$company,$code,$name,$brand,$category,$unit,$type,$vat,$purchaseVat,$excise,$exciseUnit,$minimumStock,$maximumStock,$minimumOrder,$orderMultiple,$sellable,$active,$image,$parent,$defOk,$quote,$free,$bundle,$now,$now) ON CONFLICT(id) DO UPDATE SET code=$code,name=$name,brand_id=$brand,category_id=$category,base_unit_id=$unit,product_type=$type,vat_rate=$vat,purchase_vat_rate=$purchaseVat,excise_rate=$excise,excise_unit_price=$exciseUnit,minimum_stock=$minimumStock,maximum_stock=$maximumStock,minimum_order_quantity=$minimumOrder,order_multiple=$orderMultiple,is_sellable=$sellable,is_active=$active,image_path=$image,parent_code=$parent,is_definition_complete=$defOk,can_quote=$quote,allow_free_issue=$free,is_bundle=$bundle,updated_at=$now";
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$company", edit.CompanyId); cmd.Parameters.AddWithValue("$code", edit.Code.Trim().ToUpperInvariant()); cmd.Parameters.AddWithValue("$name", edit.Name.Trim()); cmd.Parameters.AddWithValue("$brand", (object?)NullIf(edit.BrandId) ?? DBNull.Value); cmd.Parameters.AddWithValue("$category", (object?)NullIf(edit.CategoryId) ?? DBNull.Value); cmd.Parameters.AddWithValue("$unit", edit.UnitId); cmd.Parameters.AddWithValue("$type", edit.ProductType); cmd.Parameters.AddWithValue("$vat", edit.VatRate); cmd.Parameters.AddWithValue("$purchaseVat", edit.PurchaseVatRate); cmd.Parameters.AddWithValue("$excise", edit.ExciseRate); cmd.Parameters.AddWithValue("$exciseUnit", edit.ExciseUnitPrice); cmd.Parameters.AddWithValue("$minimumStock", edit.MinimumStock); cmd.Parameters.AddWithValue("$maximumStock", edit.MaximumStock); cmd.Parameters.AddWithValue("$minimumOrder", edit.MinimumOrderQuantity); cmd.Parameters.AddWithValue("$orderMultiple", edit.OrderMultiple); cmd.Parameters.AddWithValue("$sellable", edit.IsSellable ? 1 : 0); cmd.Parameters.AddWithValue("$active", edit.IsActive ? 1 : 0); cmd.Parameters.AddWithValue("$image", edit.ImagePath?.Trim() ?? ""); cmd.Parameters.AddWithValue("$parent", edit.ParentCode?.Trim() ?? ""); cmd.Parameters.AddWithValue("$defOk", edit.IsDefinitionComplete ? 1 : 0); cmd.Parameters.AddWithValue("$quote", edit.CanQuote ? 1 : 0); cmd.Parameters.AddWithValue("$free", edit.AllowFreeIssue ? 1 : 0); cmd.Parameters.AddWithValue("$bundle", edit.IsBundle ? 1 : 0); cmd.Parameters.AddWithValue("$now", now); cmd.ExecuteNonQuery();

        using (var policyCmd = connection.CreateCommand()) { policyCmd.Transaction = tx; policyCmd.CommandText = "INSERT INTO product_inventory_policies(product_id,minimum_stock,maximum_stock,minimum_order_quantity,order_multiple,delivery_lead_time_days,maximum_delivery_lead_time_days,lot_tracking_type,piece_count,shipment_location_type,updated_at) VALUES($p,$min,$max,$minOrder,$mult,$lead,$maxLead,$lot,$piece,$ship,$now) ON CONFLICT(product_id) DO UPDATE SET minimum_stock=$min,maximum_stock=$max,minimum_order_quantity=$minOrder,order_multiple=$mult,delivery_lead_time_days=$lead,maximum_delivery_lead_time_days=$maxLead,lot_tracking_type=$lot,piece_count=$piece,shipment_location_type=$ship,updated_at=$now"; policyCmd.Parameters.AddWithValue("$p", id); policyCmd.Parameters.AddWithValue("$min", edit.MinimumStock); policyCmd.Parameters.AddWithValue("$max", edit.MaximumStock); policyCmd.Parameters.AddWithValue("$minOrder", edit.MinimumOrderQuantity); policyCmd.Parameters.AddWithValue("$mult", edit.OrderMultiple); policyCmd.Parameters.AddWithValue("$lead", edit.Policy.DeliveryLeadTimeDays); policyCmd.Parameters.AddWithValue("$maxLead", edit.Policy.MaximumDeliveryLeadTimeDays); policyCmd.Parameters.AddWithValue("$lot", edit.Policy.LotTrackingType); policyCmd.Parameters.AddWithValue("$piece", edit.Policy.PieceCount); policyCmd.Parameters.AddWithValue("$ship", edit.Policy.ShipmentLocationType ?? ""); policyCmd.Parameters.AddWithValue("$now", now); policyCmd.ExecuteNonQuery(); }

        SaveClassificationAndWarehousePolicies(connection, tx, id, edit, now);

        // Retire omitted children before upserting new rows. New UI rows have an empty
        // client-side id; doing this after the insert would immediately deactivate them.
        DeactivateRemoved(connection, tx, "product_variants", id, edit.Variants.Select(x => x.Id));
        DeactivateRemoved(connection, tx, "product_barcodes", id, edit.Barcodes.Select(x => x.Id));
        DeactivateRemoved(connection, tx, "product_units", id, units.Select(x => x.Id));
        DeactivateRemoved(connection, tx, "product_suppliers", id, edit.Suppliers.Select(x => x.Id));

        foreach (var variant in edit.Variants) { if (string.IsNullOrWhiteSpace(variant.Code) || string.IsNullOrWhiteSpace(variant.Name)) throw new ArgumentException("Varyant kodu ve adı zorunludur."); var vid = string.IsNullOrWhiteSpace(variant.Id) ? Guid.NewGuid().ToString() : variant.Id; using var child = connection.CreateCommand(); child.Transaction = tx; child.CommandText = "INSERT INTO product_variants(id,product_id,code,name,size_code,color_code,model_code,is_active,size_type) VALUES($id,$product,$code,$name,$size,$color,$model,$active,$sizeType) ON CONFLICT(id) DO UPDATE SET code=$code,name=$name,size_code=$size,color_code=$color,model_code=$model,is_active=$active,size_type=$sizeType"; child.Parameters.AddWithValue("$id", vid); child.Parameters.AddWithValue("$product", id); child.Parameters.AddWithValue("$code", variant.Code.Trim().ToUpperInvariant()); child.Parameters.AddWithValue("$name", variant.Name.Trim()); child.Parameters.AddWithValue("$size", (object?)NullIf(variant.SizeCode) ?? DBNull.Value); child.Parameters.AddWithValue("$color", (object?)NullIf(variant.ColorCode) ?? DBNull.Value); child.Parameters.AddWithValue("$model", (object?)NullIf(variant.ModelCode) ?? DBNull.Value); child.Parameters.AddWithValue("$active", variant.IsActive ? 1 : 0); child.Parameters.AddWithValue("$sizeType", variant.SizeType ?? ""); child.ExecuteNonQuery(); }
        foreach (var barcode in edit.Barcodes) { if (string.IsNullOrWhiteSpace(barcode.Barcode) || barcode.Quantity <= 0) throw new ArgumentException("Barkod boş olamaz ve miktar 0'dan büyük olmalıdır."); using var check = connection.CreateCommand(); check.Transaction = tx; check.CommandText = "SELECT COUNT(*) FROM product_barcodes WHERE barcode=$barcode AND product_id<>$product"; check.Parameters.AddWithValue("$barcode", barcode.Barcode.Trim()); check.Parameters.AddWithValue("$product", id); if (Convert.ToInt64(check.ExecuteScalar()) > 0) throw new ArgumentException($"{barcode.Barcode.Trim()} barkodu başka bir üründe kayıtlı."); var bid = string.IsNullOrWhiteSpace(barcode.Id) ? Guid.NewGuid().ToString() : barcode.Id; using var child = connection.CreateCommand(); child.Transaction = tx; child.CommandText = "INSERT INTO product_barcodes(id,product_id,variant_id,unit_id,barcode,quantity,is_primary,is_active) VALUES($id,$product,$variant,$unit,$barcode,$quantity,$primary,$active) ON CONFLICT(id) DO UPDATE SET variant_id=$variant,unit_id=$unit,barcode=$barcode,quantity=$quantity,is_primary=$primary,is_active=$active"; child.Parameters.AddWithValue("$id", bid); child.Parameters.AddWithValue("$product", id); child.Parameters.AddWithValue("$variant", (object?)NullIf(barcode.VariantId) ?? DBNull.Value); child.Parameters.AddWithValue("$unit", (object?)NullIf(barcode.UnitId) ?? DBNull.Value); child.Parameters.AddWithValue("$barcode", barcode.Barcode.Trim()); child.Parameters.AddWithValue("$quantity", barcode.Quantity); child.Parameters.AddWithValue("$primary", barcode.IsPrimary ? 1 : 0); child.Parameters.AddWithValue("$active", barcode.IsActive ? 1 : 0); if (barcode.IsPrimary) { using var clear = connection.CreateCommand(); clear.Transaction = tx; clear.CommandText = "UPDATE product_barcodes SET is_primary=0 WHERE product_id=$product"; clear.Parameters.AddWithValue("$product", id); clear.ExecuteNonQuery(); } child.ExecuteNonQuery(); }
        foreach (var unit in units) { var uid = string.IsNullOrWhiteSpace(unit.Id) ? Guid.NewGuid().ToString() : unit.Id; using var child = connection.CreateCommand(); child.Transaction = tx; child.CommandText = "INSERT INTO product_units(id,product_id,unit_id,sequence,conversion_factor,is_base_unit,is_sales_unit,is_purchase_unit,is_active) VALUES($id,$product,$unit,$seq,$factor,$base,$sales,$purchase,$active) ON CONFLICT(id) DO UPDATE SET unit_id=$unit,sequence=$seq,conversion_factor=$factor,is_base_unit=$base,is_sales_unit=$sales,is_purchase_unit=$purchase,is_active=$active"; child.Parameters.AddWithValue("$id", uid); child.Parameters.AddWithValue("$product", id); child.Parameters.AddWithValue("$unit", unit.UnitId); child.Parameters.AddWithValue("$seq", unit.Sequence); child.Parameters.AddWithValue("$factor", unit.ConversionFactor); child.Parameters.AddWithValue("$base", unit.IsBaseUnit ? 1 : 0); child.Parameters.AddWithValue("$sales", unit.IsSalesUnit ? 1 : 0); child.Parameters.AddWithValue("$purchase", unit.IsPurchaseUnit ? 1 : 0); child.Parameters.AddWithValue("$active", unit.IsActive ? 1 : 0); child.ExecuteNonQuery(); }
        foreach (var supplier in edit.Suppliers) { if (string.IsNullOrWhiteSpace(supplier.SupplierAccountId)) throw new ArgumentException("Tedarikçi seçimi zorunludur."); using (var accountCheck = connection.CreateCommand()) { accountCheck.Transaction = tx; accountCheck.CommandText = "SELECT COUNT(*) FROM accounts WHERE id=$id AND company_id=$company AND is_active=1 AND account_type IN ('Supplier','CustomerAndSupplier')"; accountCheck.Parameters.AddWithValue("$id", supplier.SupplierAccountId); accountCheck.Parameters.AddWithValue("$company", edit.CompanyId); if (Convert.ToInt64(accountCheck.ExecuteScalar()) == 0) throw new ArgumentException("Seçilen cari tedarikçi olarak kullanılamaz."); } var sid = string.IsNullOrWhiteSpace(supplier.Id) ? Guid.NewGuid().ToString() : supplier.Id; using var child = connection.CreateCommand(); child.Transaction = tx; child.CommandText = "INSERT INTO product_suppliers(id,product_id,supplier_account_id,supplier_product_code,is_active,lead_time_days,extra_lead_time_days,priority,minimum_order_quantity) VALUES($id,$product,$supplier,$code,$active,$lead,$extra,$priority,$minOrder) ON CONFLICT(id) DO UPDATE SET supplier_account_id=$supplier,supplier_product_code=$code,is_active=$active,lead_time_days=$lead,extra_lead_time_days=$extra,priority=$priority,minimum_order_quantity=$minOrder"; child.Parameters.AddWithValue("$id", sid); child.Parameters.AddWithValue("$product", id); child.Parameters.AddWithValue("$supplier", supplier.SupplierAccountId); child.Parameters.AddWithValue("$code", supplier.SupplierProductCode?.Trim() ?? ""); child.Parameters.AddWithValue("$active", supplier.IsActive ? 1 : 0); child.Parameters.AddWithValue("$lead", supplier.LeadTimeDays); child.Parameters.AddWithValue("$extra", supplier.ExtraLeadTimeDays); child.Parameters.AddWithValue("$priority", supplier.Priority); child.Parameters.AddWithValue("$minOrder", (object?)supplier.MinimumOrderQuantity ?? DBNull.Value); child.ExecuteNonQuery(); }

        using var audit = connection.CreateCommand(); audit.Transaction = tx; audit.CommandText = "INSERT INTO audit_logs(id,entity_type,entity_id,company_id,action,old_values,new_values,created_at) VALUES($id,'Product',$entity,$company,$action,$old,$new,$now)"; audit.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); audit.Parameters.AddWithValue("$entity", id); audit.Parameters.AddWithValue("$company", edit.CompanyId); audit.Parameters.AddWithValue("$action", isNew ? "CREATE" : "UPDATE"); audit.Parameters.AddWithValue("$old", (object?)oldSnapshot ?? DBNull.Value); audit.Parameters.AddWithValue("$new", $"Code={edit.Code.Trim()};Name={edit.Name.Trim()};Active={edit.IsActive};Sellable={edit.IsSellable};MinStock={edit.MinimumStock};MaxStock={edit.MaximumStock}"); audit.Parameters.AddWithValue("$now", now); audit.ExecuteNonQuery(); tx.Commit();
    }
    private static void DeactivateRemoved(SqliteConnection connection, SqliteTransaction tx, string table, string productId, IEnumerable<string> keptIds)
    {
        var kept = keptIds.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var existing = connection.CreateCommand(); existing.Transaction = tx; existing.CommandText = $"SELECT id FROM {table} WHERE product_id=$p"; existing.Parameters.AddWithValue("$p", productId);
        var ids = new List<string>(); using (var rr = existing.ExecuteReader()) while (rr.Read()) ids.Add(rr.GetString(0));
        foreach (var childId in ids.Where(x => !kept.Contains(x))) { using var off = connection.CreateCommand(); off.Transaction = tx; off.CommandText = $"UPDATE {table} SET is_active=0 WHERE id=$id"; off.Parameters.AddWithValue("$id", childId); off.ExecuteNonQuery(); }
    }
    private static void ValidateUnits(IReadOnlyList<ProductUnitEdit> units)
    {
        if (units.Count == 0) throw new ArgumentException("En az bir temel birim tanımlanmalıdır.");
        foreach (var unit in units) { if (string.IsNullOrWhiteSpace(unit.UnitId)) throw new ArgumentException("Ürün birimi seçilmelidir."); if (unit.ConversionFactor <= 0) throw new ArgumentException("Birim çarpanı 0'dan büyük olmalıdır."); }
        if (units.Select(x => x.UnitId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != units.Count) throw new ArgumentException("Aynı birim üründe birden fazla kez tanımlanamaz.");
        var baseUnits = units.Where(x => x.IsBaseUnit).ToList();
        if (baseUnits.Count > 1) throw new ArgumentException("Bir üründe yalnızca bir temel birim olabilir.");
        if (baseUnits.Count == 1 && baseUnits[0].ConversionFactor != 1) throw new ArgumentException("Temel birimin çarpanı 1 olmalıdır.");
        if (baseUnits.Count == 0) throw new ArgumentException("Üründe bir temel birim bulunmalıdır.");
    }

    private static void ValidateVariants(IReadOnlyList<ProductChildEdit> variants)
    {
        if (variants.Any(x => string.IsNullOrWhiteSpace(x.Code) || string.IsNullOrWhiteSpace(x.Name)))
            throw new ArgumentException("Varyant kodu ve adı zorunludur.");
        if (variants.Select(x => x.Code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != variants.Count)
            throw new ArgumentException("Aynı ürün içinde varyant kodu tekrarlanamaz.");
    }

    private static void ValidateBarcodes(IReadOnlyList<ProductChildEdit> barcodes)
    {
        if (barcodes.Any(x => string.IsNullOrWhiteSpace(x.Barcode) || x.Quantity <= 0))
            throw new ArgumentException("Barkod boş olamaz ve miktar 0'dan büyük olmalıdır.");
        var activePrimary = barcodes.Count(x => x.IsActive && x.IsPrimary);
        if (activePrimary > 1) throw new ArgumentException("Bir ürün için yalnızca bir aktif birincil barkod olabilir.");
        if (barcodes.Select(x => x.Barcode.Trim()).Distinct(StringComparer.Ordinal).Count() != barcodes.Count)
            throw new ArgumentException("Aynı barkod bir ürün içinde birden fazla kez tanımlanamaz.");
    }
    private static void ValidateSuppliers(IReadOnlyList<ProductSupplierEdit> suppliers)
    {
        foreach (var supplier in suppliers)
        {
            if (string.IsNullOrWhiteSpace(supplier.SupplierAccountId)) throw new ArgumentException("Tedarikçi seçimi zorunludur.");
            if (supplier.LeadTimeDays < 0 || supplier.ExtraLeadTimeDays < 0) throw new ArgumentException("Teslim günü negatif olamaz.");
            if (supplier.Priority < 1) throw new ArgumentException("Öncelik 1 veya üzeri olmalıdır.");
            if (supplier.MinimumOrderQuantity is < 0) throw new ArgumentException("Minimum sipariş miktarı negatif olamaz.");
        }
        if (suppliers.Select(x => x.SupplierAccountId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != suppliers.Count) throw new ArgumentException("Aynı tedarikçi üründe birden fazla kez tanımlanamaz.");
    }
    private static void ValidateWarehousePolicies(IReadOnlyList<ProductWarehousePolicyEdit>? policies)
    {
        if (policies == null) return;
        foreach (var policy in policies)
        {
            if (string.IsNullOrWhiteSpace(policy.WarehouseId)) throw new ArgumentException("Depo bazlı stok politikasında depo seçilmelidir.");
            if (policy.MinimumStock < 0 || policy.MaximumStock < 0) throw new ArgumentException("Depo bazlı minimum/maksimum stok negatif olamaz.");
            if (policy.MaximumStock > 0 && policy.MaximumStock < policy.MinimumStock) throw new ArgumentException("Depo bazlı maksimum stok, minimum stoktan küçük olamaz.");
        }
        if (policies.Select(x => x.WarehouseId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != policies.Count) throw new ArgumentException("Aynı depo için birden fazla stok politikası tanımlanamaz.");
    }

    private static void SaveClassificationAndWarehousePolicies(SqliteConnection connection, SqliteTransaction tx, string productId, ProductAggregateEdit edit, string now)
    {
        long Count(string sql, params (string, object)[] parameters)
        {
            using var q = connection.CreateCommand(); q.Transaction = tx; q.CommandText = sql;
            foreach (var (n, v) in parameters) q.Parameters.AddWithValue(n, v);
            return Convert.ToInt64(q.ExecuteScalar());
        }
        void Exec(string sql, params (string, object)[] parameters)
        {
            using var q = connection.CreateCommand(); q.Transaction = tx; q.CommandText = sql;
            foreach (var (n, v) in parameters) q.Parameters.AddWithValue(n, v);
            q.ExecuteNonQuery();
        }
        if (edit.ProductGroupId != null)
        {
            if (edit.ProductGroupId.Length > 0 && Count("SELECT COUNT(*) FROM product_groups WHERE id=$g AND company_id=$c", ("$g", edit.ProductGroupId), ("$c", edit.CompanyId)) == 0)
                throw new ArgumentException("Seçilen stok grubu bu firma kapsamında bulunamadı.");
            Exec("UPDATE products SET product_group_id=$g WHERE id=$id", ("$g", (object?)NullIf(edit.ProductGroupId) ?? DBNull.Value), ("$id", productId));
        }
        if (edit.OriginCountryId != null)
        {
            if (edit.OriginCountryId.Length > 0 && Count("SELECT COUNT(*) FROM countries WHERE id=$o", ("$o", edit.OriginCountryId)) == 0)
                throw new ArgumentException("Seçilen menşe ülke bulunamadı.");
            Exec("UPDATE products SET origin_country_id=$o WHERE id=$id", ("$o", (object?)NullIf(edit.OriginCountryId) ?? DBNull.Value), ("$id", productId));
        }
        if (edit.WarehousePolicies != null)
        {
            Exec("DELETE FROM product_warehouse_policies WHERE product_id=$p", ("$p", productId));
            foreach (var policy in edit.WarehousePolicies)
            {
                if (Count("SELECT COUNT(*) FROM warehouses WHERE id=$w AND company_id=$c", ("$w", policy.WarehouseId), ("$c", edit.CompanyId)) == 0)
                    throw new ArgumentException("Depo bazlı stok politikasındaki depo bu firma kapsamında bulunamadı.");
                Exec("INSERT INTO product_warehouse_policies(product_id,warehouse_id,minimum_stock,maximum_stock,updated_at) VALUES($p,$w,$min,$max,$now)",
                    ("$p", productId), ("$w", policy.WarehouseId), ("$min", policy.MinimumStock), ("$max", policy.MaximumStock), ("$now", now));
            }
        }
    }

    private static string? NullIf(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
