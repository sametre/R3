using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record InventoryDocumentLineEdit(
    string ProductId,
    string UnitId,
    decimal Quantity,
    decimal BaseQuantity,
    string? VariantId = null,
    decimal? UnitCost = null,
    decimal DiscountRate = 0,
    decimal VatRate = 0,
    string? LotNo = null,
    string? SerialNo = null,
    DateTime? ExpiryDate = null,
    string Description = "",
    string? LocationId = null);

public sealed record InventoryDocumentEdit(
    string CompanyId,
    string BranchId,
    string WarehouseId,
    string DocumentType,
    DateTime DocumentDate,
    IReadOnlyList<InventoryDocumentLineEdit> Lines,
    string? ReferenceNo = null,
    string Description = "");

public sealed record InventoryDocumentSummary(string Id, string DocumentNo, string DocumentType, string Status, DateTime DocumentDate, int LineCount, decimal TotalQuantity);

/// <summary>
/// Stok fişinin yaşam döngüsünü yönetir. Taslak yalnızca belge ve satırları saklar;
/// stok defteri ve bakiye yalnızca ApproveAsync içinde, aynı transaction'da güncellenir.
/// </summary>
public sealed class LocalInventoryDocumentService(StoreDatabase database)
{
    public void UpdateDraft(string documentId, InventoryDocumentEdit edit, string userId)
    {
        ValidateEdit(edit);
        using var connection = database.OpenConnection(); using var transaction = connection.BeginTransaction();
        using var state = connection.CreateCommand(); state.Transaction = transaction; state.CommandText = "SELECT status,company_id,branch_id,warehouse_id,document_no FROM inventory_documents WHERE id=$id"; Add(state, "$id", documentId);
        using var reader = state.ExecuteReader(); if (!reader.Read()) throw new InvalidOperationException("Stok fişi bulunamadı."); var status = reader.GetString(0); var company = reader.GetString(1); var branch = reader.GetString(2); var warehouse = reader.GetString(3); var number = reader.GetString(4); reader.Close();
        if (status != "Draft") throw new InvalidOperationException("Sadece taslak fiş düzenlenebilir."); if (company != edit.CompanyId || branch != edit.BranchId || warehouse != edit.WarehouseId) throw new InvalidOperationException("Taslak fişin şirket, şube ve depo bilgileri değiştirilemez.");
        foreach (var line in edit.Lines) ValidateLine(connection, transaction, edit.CompanyId, edit.BranchId, edit.WarehouseId, line);
        using (var delete = connection.CreateCommand()) { delete.Transaction = transaction; delete.CommandText = "DELETE FROM inventory_document_lines WHERE inventory_document_id=$id"; Add(delete, "$id", documentId); delete.ExecuteNonQuery(); }
        var lineNo = 1; foreach (var line in edit.Lines) InsertLine(connection, transaction, documentId, lineNo++, line);
        var now = DateTime.UtcNow.ToString("O"); using (var update = connection.CreateCommand()) { update.Transaction = transaction; update.CommandText = "UPDATE inventory_documents SET document_date=$date,document_type=$type,reference_no=$reference,description=$description,updated_at=$now WHERE id=$id AND status='Draft'"; Add(update, "$date", edit.DocumentDate.ToUniversalTime().ToString("O")); Add(update, "$type", edit.DocumentType); Add(update, "$reference", (object?)edit.ReferenceNo ?? DBNull.Value); Add(update, "$description", edit.Description); Add(update, "$now", now); Add(update, "$id", documentId); update.ExecuteNonQuery(); }
        Audit(connection, transaction, edit.CompanyId, documentId, userId, "DraftUpdated", $"documentNo={number};lineCount={edit.Lines.Count}", CancellationToken.None).GetAwaiter().GetResult(); transaction.Commit();
    }

    public string CreateDraft(InventoryDocumentEdit edit, string userId)
    {
        ValidateEdit(edit);
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTime.UtcNow.ToString("O");
        var id = Guid.NewGuid().ToString();
        var number = NextNumber(connection, transaction, edit.CompanyId, edit.DocumentType, edit.DocumentDate.Year);
        InsertDocument(connection, transaction, id, number, edit, userId, now);
        var lineNo = 1;
        foreach (var line in edit.Lines)
        {
            ValidateLine(connection, transaction, edit.CompanyId, edit.BranchId, edit.WarehouseId, line);
            InsertLine(connection, transaction, id, lineNo++, line);
        }
        transaction.Commit();
        return id;
    }

    public async Task<string> ApproveAsync(string documentId, string userId, CancellationToken ct = default)
    {
        await using var connection = database.OpenConnection();
        await using var transaction = connection.BeginTransaction();
        var document = await ReadDocument(connection, transaction, documentId, ct) ?? throw new InvalidOperationException("Stok fişi bulunamadı.");
        if (document.Status != "Draft") throw new InvalidOperationException($"Sadece taslak fiş onaylanabilir. Mevcut durum: {document.Status}.");
        InventoryAccessGuard.Ensure(database, connection, transaction, document.WarehouseId);
        var lines = await ReadLines(connection, transaction, documentId, ct);
        if (lines.Count == 0) throw new InvalidOperationException("Stok fişinde en az bir satır olmalıdır.");

        foreach (var line in lines)
        {
            await ValidateProduct(connection, transaction, document.CompanyId, line.ProductId, ct);
            await ValidateDocumentLocation(connection, transaction, document, line.LocationId, ct);
            if (document.DocumentType == "ManualOut")
            {
                var available = await BalanceValue(connection, transaction, document.WarehouseId, line.LocationId, line.ProductId, line.VariantId, ct);
                InventoryStockPolicy.EnsureAvailable(connection, transaction, document.WarehouseId, available, line.BaseQuantity,
                    $"Yetersiz stok. Ürün: {line.ProductId}; Mevcut kullanılabilir: {available:N2}; Talep edilen: {line.BaseQuantity:N2}; Eksik: {line.BaseQuantity - available:N2}.");
            }
        }

        var transactionType = document.DocumentType == "ManualIn" ? InventoryTransactionType.ManualIn : InventoryTransactionType.ManualOut;
        var inbound = document.DocumentType == "ManualIn";
        var lots = ReadLotInfo(connection, transaction, documentId); var stamp = DateTime.UtcNow.ToString("O");
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            // Lot / Seri: validated and recorded before the movement; lot-tracked products need a lot, serial-tracked ones one serial per unit.
            var assignment = InventoryLotTracking.Apply(connection, transaction, document.CompanyId, document.WarehouseId, line.ProductId, lots[i].LotNo, lots[i].SerialNo, lots[i].Expiry, line.BaseQuantity, inbound, stamp);
            await InsertMovement(connection, transaction, document, line, transactionType, document.Id, userId, ct);
            InventoryLotTracking.StampLastMovement(connection, transaction, assignment);
            await ApplyBalance(connection, transaction, document, line, inbound ? line.BaseQuantity : -line.BaseQuantity, ct);
        }
        var now = DateTime.UtcNow.ToString("O");
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE inventory_documents SET status='Approved',approved_by=$user,approved_at=$now,updated_at=$now WHERE id=$id AND status='Draft'";
            Add(update, "$user", userId); Add(update, "$now", now); Add(update, "$id", documentId);
            if (await update.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Stok fişi başka bir işlem tarafından onaylandı.");
        }
        await Audit(connection, transaction, document.CompanyId, document.Id, userId, "InventoryDocumentApproved", $"documentNo={document.DocumentNo};type={document.DocumentType}", ct);
        await transaction.CommitAsync(ct);
        return document.DocumentNo;
    }

    public async Task ReverseAsync(string documentId, string userId, CancellationToken ct = default)
    {
        await using var connection = database.OpenConnection();
        await using var transaction = connection.BeginTransaction();
        var document = await ReadDocument(connection, transaction, documentId, ct) ?? throw new InvalidOperationException("Stok fişi bulunamadı.");
        if (document.Status != "Approved") throw new InvalidOperationException($"Sadece onaylı fiş ters çevrilebilir. Mevcut durum: {document.Status}.");
        InventoryAccessGuard.Ensure(database, connection, transaction, document.WarehouseId);
        var lines = await ReadLines(connection, transaction, documentId, ct);
        var reverseInbound = document.DocumentType == "ManualOut";
        var reverseType = reverseInbound ? InventoryTransactionType.ManualIn : InventoryTransactionType.ManualOut;
        var reverseLots = ReadLotInfo(connection, transaction, documentId); var reverseStamp = DateTime.UtcNow.ToString("O"); var index = 0;
        foreach (var line in lines)
        {
            var lot = reverseLots[index++];
            if (!reverseInbound)
            {
                var available = await BalanceValue(connection, transaction, document.WarehouseId, line.LocationId, line.ProductId, line.VariantId, ct);
                InventoryStockPolicy.EnsureAvailable(connection, transaction, document.WarehouseId, available, line.BaseQuantity, $"Fiş ters çevrilemez; kullanılabilir stok yetersiz. Ürün: {line.ProductId}; Mevcut: {available:N2}; Gereken: {line.BaseQuantity:N2}.");
            }
            var reverseAssignment = InventoryLotTracking.Apply(connection, transaction, document.CompanyId, document.WarehouseId, line.ProductId, lot.LotNo, lot.SerialNo, lot.Expiry, line.BaseQuantity, reverseInbound, reverseStamp);
            await InsertMovement(connection, transaction, document, line, reverseType, document.Id + ":reverse", userId, ct);
            InventoryLotTracking.StampLastMovement(connection, transaction, reverseAssignment);
            await ApplyBalance(connection, transaction, document, line, reverseInbound ? line.BaseQuantity : -line.BaseQuantity, ct);
        }
        var now = DateTime.UtcNow.ToString("O");
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction; update.CommandText = "UPDATE inventory_documents SET status='Reversed',cancelled_at=$now,updated_at=$now WHERE id=$id AND status='Approved'"; Add(update, "$now", now); Add(update, "$id", documentId);
            if (await update.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Stok fişi ters çevrilemedi.");
        }
        await Audit(connection, transaction, document.CompanyId, document.Id, userId, "InventoryDocumentReversed", $"documentNo={document.DocumentNo};reverseCorrelation={document.Id}:reverse", ct);
        await transaction.CommitAsync(ct);
    }

    public DataTable Search(string companyId, string? documentType = null, string? status = null)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.id AS Id,d.document_no AS FisNo,d.document_type AS FisTuru,d.document_date AS Tarih,
                   d.status AS Durum,COALESCE(br.name,d.branch_id) AS Sube,COALESCE(w.name,d.warehouse_id) AS Depo,
                   COUNT(l.id) AS SatirSayisi,COALESCE(SUM(l.base_quantity),0) AS TemelMiktar,
                   COUNT(DISTINCT l.location_id) AS LokasyonSayisi,d.created_by AS Olusturan,d.approved_by AS Onaylayan,d.description AS Aciklama
            FROM inventory_documents d LEFT JOIN inventory_document_lines l ON l.inventory_document_id=d.id
            LEFT JOIN warehouses w ON w.id=d.warehouse_id LEFT JOIN branches br ON br.id=d.branch_id
            WHERE d.company_id=$company AND ($type='' OR d.document_type=$type) AND ($status='' OR d.status=$status)
            GROUP BY d.id ORDER BY d.document_date DESC,d.created_at DESC
            """;
        Add(command, "$company", companyId); Add(command, "$type", documentType ?? ""); Add(command, "$status", status ?? "");
        using var reader = command.ExecuteReader(); var result = new DataTable(); StoreDatabase.LoadSafely(result, reader); return result;
    }

    public DataTable Details(string documentId)
    {
        using var connection = database.OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT l.line_no AS Satır,p.code AS StokKodu,p.name AS StokAdi,COALESCE(v.code,'') AS Varyant,u.code AS Birim,l.quantity AS Miktar,l.base_quantity AS TemelMiktar,COALESCE(wl.code,'') AS Lokasyon,l.unit_cost AS BirimMaliyet,l.lot_no AS Lot,l.serial_no AS Seri,l.expiry_date AS SonKullanma,l.description AS Açıklama FROM inventory_document_lines l JOIN products p ON p.id=l.product_id JOIN units u ON u.id=l.unit_id LEFT JOIN product_variants v ON v.id=l.variant_id LEFT JOIN warehouse_locations wl ON wl.id=l.location_id WHERE l.inventory_document_id=$id ORDER BY l.line_no"; Add(command, "$id", documentId); using var reader = command.ExecuteReader(); var result = new DataTable(); StoreDatabase.LoadSafely(result, reader); return result;
    }

    private static void ValidateEdit(InventoryDocumentEdit edit)
    {
        if (string.IsNullOrWhiteSpace(edit.CompanyId) || string.IsNullOrWhiteSpace(edit.BranchId) || string.IsNullOrWhiteSpace(edit.WarehouseId)) throw new ArgumentException("Firma, şube ve depo zorunludur.");
        if (edit.DocumentType is not ("ManualIn" or "ManualOut")) throw new ArgumentException("Stok fişi türü ManualIn veya ManualOut olmalıdır.");
        if (edit.Lines is null || edit.Lines.Count == 0) throw new ArgumentException("Stok fişinde en az bir satır olmalıdır.");
    }

    private static void ValidateLine(SqliteConnection c, SqliteTransaction tx, string companyId, string branchId, string warehouseId, InventoryDocumentLineEdit line)
    {
        if (string.IsNullOrWhiteSpace(line.ProductId) || string.IsNullOrWhiteSpace(line.UnitId) || line.Quantity <= 0 || line.BaseQuantity <= 0) throw new ArgumentException("Stok fişi satırında ürün, birim ve pozitif miktar zorunludur.");
        if (line.UnitCost is < 0 || line.DiscountRate is < 0 or > 100 || line.VatRate is < 0 or > 100) throw new ArgumentException("Maliyet, iskonto veya KDV oranı geçersiz.");
        using (var warehouse = c.CreateCommand()) { warehouse.Transaction = tx; warehouse.CommandText = "SELECT COUNT(*) FROM warehouses WHERE id=$warehouse AND company_id=$company AND branch_id=$branch AND is_active=1"; Add(warehouse, "$warehouse", warehouseId); Add(warehouse, "$company", companyId); Add(warehouse, "$branch", branchId); if (Convert.ToInt32(warehouse.ExecuteScalar()) != 1) throw new ArgumentException("Seçilen depo aktif değil veya şirket/şube ile eşleşmiyor."); }
        using var command = c.CreateCommand(); command.Transaction = tx; command.CommandText = "SELECT COUNT(*) FROM products WHERE id=$product AND company_id=$company AND is_active=1"; Add(command, "$product", line.ProductId); Add(command, "$company", companyId);
        if (Convert.ToInt32(command.ExecuteScalar()) != 1) throw new ArgumentException("Fiş satırındaki ürün bulunamadı, pasif veya farklı firmaya ait.");
        ValidateLocation(c, tx, warehouseId, line.LocationId, false);
    }

    private static string NextNumber(SqliteConnection c, SqliteTransaction tx, string companyId, string type, int year)
    {
        var prefix = (type == "ManualIn" ? "SG" : "SC") + $"-{year}-";
        using var command = c.CreateCommand(); command.Transaction = tx; command.CommandText = "SELECT COALESCE(MAX(CAST(SUBSTR(document_no,$offset) AS INTEGER)),0)+1 FROM inventory_documents WHERE company_id=$company AND document_no LIKE $prefix || '%'"; Add(command, "$offset", prefix.Length + 1); Add(command, "$company", companyId); Add(command, "$prefix", prefix); var number = Convert.ToInt32(command.ExecuteScalar()); return prefix + number.ToString("D6");
    }

    private static void InsertDocument(SqliteConnection c, SqliteTransaction tx, string id, string number, InventoryDocumentEdit edit, string userId, string now)
    { using var command = c.CreateCommand(); command.Transaction = tx; command.CommandText = "INSERT INTO inventory_documents(id,company_id,branch_id,warehouse_id,document_type,document_no,document_date,status,reference_no,description,created_by,created_at,updated_at) VALUES($id,$company,$branch,$warehouse,$type,$number,$date,'Draft',$reference,$description,$user,$now,$now)"; Add(command,"$id",id); Add(command,"$company",edit.CompanyId); Add(command,"$branch",edit.BranchId); Add(command,"$warehouse",edit.WarehouseId); Add(command,"$type",edit.DocumentType); Add(command,"$number",number); Add(command,"$date",edit.DocumentDate.ToUniversalTime().ToString("O")); Add(command,"$reference",(object?)edit.ReferenceNo??DBNull.Value); Add(command,"$description",edit.Description); Add(command,"$user",userId); Add(command,"$now",now); command.ExecuteNonQuery(); }
    private static void InsertLine(SqliteConnection c, SqliteTransaction tx, string documentId, int lineNo, InventoryDocumentLineEdit line)
    { using var command = c.CreateCommand(); command.Transaction = tx; command.CommandText = "INSERT INTO inventory_document_lines(id,inventory_document_id,line_no,product_id,variant_id,unit_id,quantity,base_quantity,unit_cost,discount_rate,vat_rate,location_id,lot_no,serial_no,expiry_date,description,created_at) VALUES($id,$document,$line,$product,$variant,$unit,$quantity,$base,$cost,$discount,$vat,$location,$lot,$serial,$expiry,$description,$now)"; Add(command,"$id",Guid.NewGuid().ToString()); Add(command,"$document",documentId); Add(command,"$line",lineNo); Add(command,"$product",line.ProductId); Add(command,"$variant",(object?)line.VariantId??DBNull.Value); Add(command,"$unit",line.UnitId); Add(command,"$quantity",line.Quantity); Add(command,"$base",line.BaseQuantity); Add(command,"$cost",(object?)line.UnitCost??DBNull.Value); Add(command,"$discount",line.DiscountRate); Add(command,"$vat",line.VatRate); Add(command,"$location",(object?)line.LocationId??DBNull.Value); Add(command,"$lot",(object?)line.LotNo??DBNull.Value); Add(command,"$serial",(object?)line.SerialNo??DBNull.Value); Add(command,"$expiry",line.ExpiryDate?.ToUniversalTime().ToString("O")??(object)DBNull.Value); Add(command,"$description",line.Description); Add(command,"$now",DateTime.UtcNow.ToString("O")); command.ExecuteNonQuery(); }

    private static async Task<(string Id,string CompanyId,string BranchId,string WarehouseId,string DocumentType,string DocumentNo,string Status)?> ReadDocument(SqliteConnection c, SqliteTransaction tx, string id, CancellationToken ct)
    { await using var command = c.CreateCommand(); command.Transaction = tx; command.CommandText = "SELECT id,company_id,branch_id,warehouse_id,document_type,document_no,status FROM inventory_documents WHERE id=$id"; Add(command,"$id",id); await using var reader = await command.ExecuteReaderAsync(ct); if (!await reader.ReadAsync(ct)) return null; return (reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetString(6)); }
    // Same order as ReadLines (line_no), so index i matches.
    private static List<(string? LotNo, string? SerialNo, DateTime? Expiry)> ReadLotInfo(SqliteConnection c, SqliteTransaction tx, string documentId)
    {
        var list = new List<(string?, string?, DateTime?)>();
        using var command = c.CreateCommand(); command.Transaction = tx;
        command.CommandText = "SELECT lot_no, serial_no, expiry_date FROM inventory_document_lines WHERE inventory_document_id=$id ORDER BY line_no"; Add(command, "$id", documentId);
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add((reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) || !DateTime.TryParse(reader.GetValue(2).ToString(), out var e) ? null : e));
        return list;
    }
    private static async Task<List<(string ProductId,string? VariantId,decimal BaseQuantity,decimal? UnitCost,string? LocationId)>> ReadLines(SqliteConnection c, SqliteTransaction tx, string documentId, CancellationToken ct)
    { var list = new List<(string,string?,decimal,decimal?,string?)>(); await using var command = c.CreateCommand(); command.Transaction = tx; command.CommandText = "SELECT product_id,variant_id,base_quantity,unit_cost,location_id FROM inventory_document_lines WHERE inventory_document_id=$id ORDER BY line_no"; Add(command,"$id",documentId); await using var reader = await command.ExecuteReaderAsync(ct); while(await reader.ReadAsync(ct)) list.Add((reader.GetString(0),reader.IsDBNull(1)?null:reader.GetString(1),reader.GetDecimal(2),reader.IsDBNull(3)?null:reader.GetDecimal(3),reader.IsDBNull(4)?null:reader.GetString(4))); return list; }
    private static async Task ValidateProduct(SqliteConnection c, SqliteTransaction tx, string companyId, string productId, CancellationToken ct)
    { await using var command = c.CreateCommand(); command.Transaction = tx; command.CommandText = "SELECT product_type,is_active FROM products WHERE id=$product AND company_id=$company"; Add(command,"$product",productId); Add(command,"$company",companyId); await using var reader = await command.ExecuteReaderAsync(ct); if(!await reader.ReadAsync(ct)) throw new InvalidOperationException("Ürün firma kapsamında bulunamadı."); if(!reader.GetBoolean(1)) throw new InvalidOperationException("Pasif ürünle stok fişi onaylanamaz."); if(reader.GetString(0) != "Stock") throw new InvalidOperationException("Hizmet veya desteklenmeyen ürün tipiyle stok fişi onaylanamaz."); }
    private static async Task<decimal> BalanceValue(SqliteConnection c, SqliteTransaction tx, string warehouseId, string? locationId, string productId, string? variantId, CancellationToken ct)
    { await using var command = c.CreateCommand(); command.Transaction = tx; command.CommandText = string.IsNullOrWhiteSpace(locationId) ? "SELECT COALESCE(quantity_available,0) FROM inventory_balances WHERE warehouse_id=$warehouse AND product_id=$product AND (variant_id=$variant OR (variant_id IS NULL AND $variant IS NULL))" : "SELECT COALESCE(quantity_available,0) FROM warehouse_location_balances WHERE warehouse_id=$warehouse AND location_id=$location AND product_id=$product AND (variant_id=$variant OR (variant_id IS NULL AND $variant IS NULL))"; Add(command,"$warehouse",warehouseId); Add(command,"$location",(object?)locationId??DBNull.Value); Add(command,"$product",productId); Add(command,"$variant",(object?)variantId??DBNull.Value); return Convert.ToDecimal(await command.ExecuteScalarAsync(ct)??0); }
    private static async Task InsertMovement(SqliteConnection c, SqliteTransaction tx, (string Id,string CompanyId,string BranchId,string WarehouseId,string DocumentType,string DocumentNo,string Status) document, (string ProductId,string? VariantId,decimal BaseQuantity,decimal? UnitCost,string? LocationId) line, InventoryTransactionType type, string correlation, string userId, CancellationToken ct)
    { await using var command = c.CreateCommand(); command.Transaction = tx; command.CommandText = "INSERT INTO inventory_transactions(id,company_id,branch_id,warehouse_id,location_id,product_id,variant_id,transaction_type,quantity,base_quantity,unit_cost,total_cost,document_type,document_id,reference_no,description,transaction_at,created_by,created_at,correlation_id) VALUES($id,$company,$branch,$warehouse,$location,$product,$variant,$type,$quantity,$base,$cost,CASE WHEN $cost IS NULL THEN NULL ELSE $cost*$quantity END,'InventoryDocument',$document,$number,$description,$date,$user,$now,$correlation)"; Add(command,"$id",Guid.NewGuid().ToString()); Add(command,"$company",document.CompanyId); Add(command,"$branch",document.BranchId); Add(command,"$warehouse",document.WarehouseId); Add(command,"$location",(object?)line.LocationId??DBNull.Value); Add(command,"$product",line.ProductId); Add(command,"$variant",(object?)line.VariantId??DBNull.Value); Add(command,"$type",type.ToString()); Add(command,"$quantity",line.BaseQuantity); Add(command,"$base",line.BaseQuantity); Add(command,"$cost",(object?)line.UnitCost??DBNull.Value); Add(command,"$document",document.Id); Add(command,"$number",document.DocumentNo); Add(command,"$description",document.DocumentType); Add(command,"$date",DateTime.UtcNow.ToString("O")); Add(command,"$user",userId); Add(command,"$now",DateTime.UtcNow.ToString("O")); Add(command,"$correlation",correlation); await command.ExecuteNonQueryAsync(ct); }
    private static async Task ApplyBalance(SqliteConnection c, SqliteTransaction tx, (string Id,string CompanyId,string BranchId,string WarehouseId,string DocumentType,string DocumentNo,string Status) document, (string ProductId,string? VariantId,decimal BaseQuantity,decimal? UnitCost,string? LocationId) line, decimal delta, CancellationToken ct)
    { await using var exists=c.CreateCommand(); exists.Transaction=tx; exists.CommandText="SELECT COUNT(*) FROM inventory_balances WHERE warehouse_id=$warehouse AND product_id=$product AND (variant_id=$variant OR (variant_id IS NULL AND $variant IS NULL))"; Add(exists,"$warehouse",document.WarehouseId); Add(exists,"$product",line.ProductId); Add(exists,"$variant",(object?)line.VariantId??DBNull.Value); var found=Convert.ToInt32(await exists.ExecuteScalarAsync(ct))>0; await using var command=c.CreateCommand(); command.Transaction=tx; command.CommandText=found?"UPDATE inventory_balances SET quantity_on_hand=quantity_on_hand+$delta,quantity_available=quantity_available+$delta,last_transaction_at=$now,updated_at=$now WHERE warehouse_id=$warehouse AND product_id=$product AND (variant_id=$variant OR (variant_id IS NULL AND $variant IS NULL))":"INSERT INTO inventory_balances(company_id,branch_id,warehouse_id,product_id,variant_id,quantity_on_hand,quantity_available,last_transaction_at,updated_at) VALUES($company,$branch,$warehouse,$product,$variant,$delta,$delta,$now,$now)"; Add(command,"$company",document.CompanyId); Add(command,"$branch",document.BranchId); Add(command,"$warehouse",document.WarehouseId); Add(command,"$product",line.ProductId); Add(command,"$variant",(object?)line.VariantId??DBNull.Value); Add(command,"$delta",delta); Add(command,"$now",DateTime.UtcNow.ToString("O")); await command.ExecuteNonQueryAsync(ct);
        if (!string.IsNullOrWhiteSpace(line.LocationId)) { await using var locExists=c.CreateCommand(); locExists.Transaction=tx; locExists.CommandText="SELECT COUNT(*) FROM warehouse_location_balances WHERE warehouse_id=$warehouse AND location_id=$location AND product_id=$product AND (variant_id=$variant OR (variant_id IS NULL AND $variant IS NULL))"; Add(locExists,"$warehouse",document.WarehouseId); Add(locExists,"$location",line.LocationId); Add(locExists,"$product",line.ProductId); Add(locExists,"$variant",(object?)line.VariantId??DBNull.Value); var locFound=Convert.ToInt32(await locExists.ExecuteScalarAsync(ct))>0; await using var loc=c.CreateCommand(); loc.Transaction=tx; loc.CommandText=locFound?"UPDATE warehouse_location_balances SET quantity_on_hand=quantity_on_hand+$delta,quantity_available=quantity_available+$delta,updated_at=$now WHERE warehouse_id=$warehouse AND location_id=$location AND product_id=$product AND (variant_id=$variant OR (variant_id IS NULL AND $variant IS NULL))":"INSERT INTO warehouse_location_balances(id,warehouse_id,location_id,product_id,variant_id,quantity_on_hand,quantity_available,updated_at) VALUES($id,$warehouse,$location,$product,$variant,$delta,$delta,$now)"; Add(loc,"$id",Guid.NewGuid().ToString()); Add(loc,"$warehouse",document.WarehouseId); Add(loc,"$location",line.LocationId); Add(loc,"$product",line.ProductId); Add(loc,"$variant",(object?)line.VariantId??DBNull.Value); Add(loc,"$delta",delta); Add(loc,"$now",DateTime.UtcNow.ToString("O")); await loc.ExecuteNonQueryAsync(ct); }
    }
    private static void ValidateLocation(SqliteConnection c, SqliteTransaction tx, string warehouseId, string? locationId, bool require)
    { using var cmd=c.CreateCommand(); cmd.Transaction=tx; cmd.CommandText="SELECT requires_location FROM warehouses WHERE id=$warehouse"; Add(cmd,"$warehouse",warehouseId); var requires=Convert.ToInt32(cmd.ExecuteScalar()??0)==1; if (require || requires) { if(string.IsNullOrWhiteSpace(locationId)) throw new ArgumentException("Bu depo için lokasyon seçilmesi zorunludur."); } if(string.IsNullOrWhiteSpace(locationId)) return; using var loc=c.CreateCommand(); loc.Transaction=tx; loc.CommandText="SELECT COUNT(*) FROM warehouse_locations WHERE id=$location AND warehouse_id=$warehouse AND is_active=1"; Add(loc,"$location",locationId); Add(loc,"$warehouse",warehouseId); if(Convert.ToInt32(loc.ExecuteScalar())!=1) throw new ArgumentException("Seçilen lokasyon depoya ait değil veya pasif."); }
    private static async Task ValidateDocumentLocation(SqliteConnection c, SqliteTransaction tx, (string Id,string CompanyId,string BranchId,string WarehouseId,string DocumentType,string DocumentNo,string Status) document, string? locationId, CancellationToken ct)
    { await using var cmd=c.CreateCommand(); cmd.Transaction=tx; cmd.CommandText="SELECT requires_location FROM warehouses WHERE id=$warehouse AND company_id=$company AND branch_id=$branch"; Add(cmd,"$warehouse",document.WarehouseId); Add(cmd,"$company",document.CompanyId); Add(cmd,"$branch",document.BranchId); var requires=Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)??0)==1; if(requires && string.IsNullOrWhiteSpace(locationId)) throw new InvalidOperationException("Bu depo için lokasyon seçilmesi zorunludur."); if(string.IsNullOrWhiteSpace(locationId)) return; await using var loc=c.CreateCommand(); loc.Transaction=tx; loc.CommandText="SELECT COUNT(*) FROM warehouse_locations WHERE id=$location AND warehouse_id=$warehouse AND is_active=1"; Add(loc,"$location",locationId); Add(loc,"$warehouse",document.WarehouseId); if(Convert.ToInt32(await loc.ExecuteScalarAsync(ct)??0)!=1) throw new InvalidOperationException("Seçilen lokasyon depoya ait değil veya pasif."); }
    private static void Add(SqliteCommand command, string name, object value) => command.Parameters.AddWithValue(name, value);
    private static async Task Audit(SqliteConnection c, SqliteTransaction tx, string companyId, string entityId, string userId, string action, string values, CancellationToken ct)
    { await using var command = c.CreateCommand(); command.Transaction = tx; command.CommandText = "INSERT INTO audit_logs(id,user_id,company_id,entity_type,entity_id,action,new_values,created_at) VALUES($id,$user,$company,'InventoryDocument',$entity,$action,$values,$now)"; Add(command,"$id",Guid.NewGuid().ToString()); Add(command,"$user",userId); Add(command,"$company",companyId); Add(command,"$entity",entityId); Add(command,"$action",action); Add(command,"$values",values); Add(command,"$now",DateTime.UtcNow.ToString("O")); await command.ExecuteNonQueryAsync(ct); }
}
