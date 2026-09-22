using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record PurchaseLineEdit(string ProductId, string UnitId, decimal Quantity, decimal UnitPrice, decimal DiscountRate, decimal VatRate, string? VariantId = null, string Description = "");
public sealed record PurchaseDocumentEdit(string CompanyId, string BranchId, string WarehouseId, string SupplierId, string DocumentType, DateTime DocumentDate, DateTime? ExpectedDate, string CurrencyCode, string Description, IReadOnlyList<PurchaseLineEdit> Lines);
public sealed record PurchaseSummary(int Draft, int Approved, int OpenOrders, decimal OpenOrderTotal);
public sealed record PurchaseReceiptLine(string LineId, decimal Quantity);

public sealed class LocalPurchasingService(StoreDatabase database)
{
    public StoreDatabase Database { get; } = database;
    public DataTable Search(string companyId, string documentType, string? search = null, string? status = null) => Database.Query(
        """
        SELECT d.id AS Id, COALESCE(d.document_no,'Taslak') AS BelgeNo,
               d.document_date AS Tarih, d.expected_date AS BeklenenTarih,
               a.code AS TedarikciKodu, a.name AS Tedarikci,
               COALESCE(b.name,d.branch_id) AS Sube, COALESCE(w.name,d.warehouse_id) AS Depo,
               COUNT(l.id) AS Satir, COALESCE(SUM(l.quantity),0) AS Miktar,
               d.subtotal AS AraToplam, d.discount_total AS Iskonto,
               d.tax_total AS KDV, d.grand_total AS GenelToplam,
               d.currency_code AS ParaBirimi, d.status AS Durum, d.description AS Aciklama
        FROM purchase_documents d
        JOIN accounts a ON a.id=d.supplier_id
        LEFT JOIN branches b ON b.id=d.branch_id
        LEFT JOIN warehouses w ON w.id=d.warehouse_id
        LEFT JOIN purchase_document_lines l ON l.purchase_document_id=d.id
        WHERE d.company_id=$company AND d.document_type=$type
          AND ($status='' OR d.status=$status)
          AND ($search='' OR d.document_no LIKE $like OR a.code LIKE $like OR a.name LIKE $like OR d.description LIKE $like)
        GROUP BY d.id
        ORDER BY d.document_date DESC, d.created_at DESC
        """, ("$company", companyId), ("$type", documentType), ("$status", status ?? ""),
        ("$search", search?.Trim() ?? ""), ("$like", $"%{search?.Trim() ?? ""}%"));

    public PurchaseSummary Summary(string companyId)
    {
        var table = Database.Query(
            """
            SELECT SUM(CASE WHEN status='Draft' THEN 1 ELSE 0 END) AS Draft,
                   SUM(CASE WHEN status='Approved' THEN 1 ELSE 0 END) AS Approved,
                   SUM(CASE WHEN document_type='Order' AND status IN ('Draft','Approved','PartiallyReceived') THEN 1 ELSE 0 END) AS OpenOrders,
                   SUM(CASE WHEN document_type='Order' AND status IN ('Draft','Approved','PartiallyReceived') THEN grand_total ELSE 0 END) AS OpenOrderTotal
            FROM purchase_documents WHERE company_id=$company
            """, ("$company", companyId));
        var row = table.Rows[0];
        return new(Convert.ToInt32(row["Draft"] is DBNull ? 0 : row["Draft"]), Convert.ToInt32(row["Approved"] is DBNull ? 0 : row["Approved"]), Convert.ToInt32(row["OpenOrders"] is DBNull ? 0 : row["OpenOrders"]), Convert.ToDecimal(row["OpenOrderTotal"] is DBNull ? 0 : row["OpenOrderTotal"]));
    }

    public string Create(PurchaseDocumentEdit edit, string userName)
    {
        Validate(edit);
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        EnsureSupplier(connection, transaction, edit.CompanyId, edit.SupplierId);
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("O");
        var totals = Calculate(edit.Lines);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO purchase_documents(id,company_id,branch_id,warehouse_id,supplier_id,document_type,document_date,expected_date,status,currency_code,subtotal,discount_total,tax_total,grand_total,description,created_at,updated_at) VALUES($id,$company,$branch,$warehouse,$supplier,$type,$date,$expected,'Draft',$currency,$subtotal,$discount,$tax,$total,$description,$now,$now)";
            Add(command, "$id", id); Add(command, "$company", edit.CompanyId); Add(command, "$branch", edit.BranchId); Add(command, "$warehouse", edit.WarehouseId); Add(command, "$supplier", edit.SupplierId); Add(command, "$type", edit.DocumentType); Add(command, "$date", edit.DocumentDate.ToString("O")); Add(command, "$expected", (object?)edit.ExpectedDate?.ToString("O") ?? DBNull.Value); Add(command, "$currency", edit.CurrencyCode); Add(command, "$subtotal", totals.subtotal); Add(command, "$discount", totals.discount); Add(command, "$tax", totals.tax); Add(command, "$total", totals.total); Add(command, "$description", edit.Description.Trim()); Add(command, "$now", now);
            command.ExecuteNonQuery();
        }
        var lineNo = 1;
        foreach (var line in edit.Lines)
        {
            var calculated = Calculate(line);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO purchase_document_lines(id,purchase_document_id,line_no,product_id,variant_id,unit_id,quantity,unit_price,discount_rate,discount_amount,vat_rate,gross_amount,net_amount,vat_amount,line_total,description) VALUES($id,$document,$line,$product,$variant,$unit,$quantity,$price,$discountRate,$discount,$vatRate,$gross,$net,$vat,$total,$description)";
            Add(command, "$id", Guid.NewGuid().ToString()); Add(command, "$document", id); Add(command, "$line", lineNo++); Add(command, "$product", line.ProductId); Add(command, "$variant", (object?)line.VariantId ?? DBNull.Value); Add(command, "$unit", line.UnitId); Add(command, "$quantity", line.Quantity); Add(command, "$price", line.UnitPrice); Add(command, "$discountRate", line.DiscountRate); Add(command, "$discount", calculated.discount); Add(command, "$vatRate", line.VatRate); Add(command, "$gross", calculated.gross); Add(command, "$net", calculated.net); Add(command, "$vat", calculated.vat); Add(command, "$total", calculated.total); Add(command, "$description", line.Description.Trim());
            command.ExecuteNonQuery();
        }
        Audit(connection, transaction, userName, edit.CompanyId, id, "PurchaseDocumentCreated", $"type={edit.DocumentType};supplierId={edit.SupplierId};total={totals.total}");
        transaction.Commit();
        return id;
    }

    public string Approve(string id, string userName)
    {
        using var connection = Database.OpenConnection(); using var transaction = connection.BeginTransaction();
        var document = Read(connection, transaction, id);
        if (document.status != "Draft") throw new InvalidOperationException("Yalnızca taslak belgeler onaylanabilir.");
        var number = AllocateNumber(connection, transaction, document.company, document.type, document.date.Year);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE purchase_documents SET document_no=$number,status='Approved',approved_at=$now,approved_by=$user,updated_at=$now WHERE id=$id AND status='Draft'";
        Add(command, "$number", number); Add(command, "$now", DateTime.UtcNow.ToString("O")); Add(command, "$user", userName); Add(command, "$id", id); command.ExecuteNonQuery();
        Audit(connection, transaction, userName, document.company, id, "PurchaseDocumentApproved", $"documentNo={number};type={document.type}");
        transaction.Commit(); return number;
    }

    public void Cancel(string id, string userName)
    {
        using var connection = Database.OpenConnection(); using var transaction = connection.BeginTransaction();
        var document = Read(connection, transaction, id);
        if (document.status is "Cancelled" or "Closed") throw new InvalidOperationException("Bu belge mevcut durumunda iptal edilemez.");
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE purchase_documents SET status='Cancelled',updated_at=$now WHERE id=$id"; Add(command, "$now", DateTime.UtcNow.ToString("O")); Add(command, "$id", id); command.ExecuteNonQuery();
        Audit(connection, transaction, userName, document.company, id, "PurchaseDocumentCancelled", $"type={document.type};previousStatus={document.status}");
        transaction.Commit();
    }

    public DataTable Lines(string documentId) => Database.Query(
        """
        SELECT l.id AS Id,p.code AS UrunKodu,p.name AS Urun,l.quantity AS SiparisMiktari,
               l.received_quantity AS TeslimAlinan,(l.quantity-l.received_quantity) AS Kalan,
               l.unit_price AS BirimFiyat,l.line_total AS SatirToplami,u.code AS Birim
        FROM purchase_document_lines l
        JOIN products p ON p.id=l.product_id LEFT JOIN units u ON u.id=l.unit_id
        WHERE l.purchase_document_id=$id ORDER BY l.line_no
        """, ("$id", documentId));

    public void Receive(string documentId, IReadOnlyList<PurchaseReceiptLine> receipts, string userName)
    {
        if (receipts.Count == 0 || receipts.All(x => x.Quantity == 0)) throw new ArgumentException("Teslim alınacak en az bir miktar girin.");
        using var connection = Database.OpenConnection(); using var transaction = connection.BeginTransaction();
        var document = ReadDetail(connection, transaction, documentId);
        if (document.Type != "Order") throw new InvalidOperationException("Mal kabul yalnızca satınalma siparişleri için yapılabilir.");
        if (document.Status is not ("Approved" or "PartiallyReceived")) throw new InvalidOperationException("Mal kabul için sipariş onaylı veya kısmi teslim durumunda olmalıdır.");
        var receivedTotal = 0m;
        foreach (var receipt in receipts.Where(x => x.Quantity > 0))
        {
            using var read = connection.CreateCommand(); read.Transaction = transaction;
            read.CommandText = "SELECT l.product_id,l.variant_id,l.quantity,l.received_quantity,l.unit_price,p.product_type,p.is_active FROM purchase_document_lines l JOIN products p ON p.id=l.product_id WHERE l.id=$line AND l.purchase_document_id=$document";
            Add(read, "$line", receipt.LineId); Add(read, "$document", documentId); using var reader = read.ExecuteReader();
            if (!reader.Read()) throw new KeyNotFoundException("Sipariş satırı bulunamadı.");
            var productId = reader.GetString(0); var variantId = reader.IsDBNull(1) ? null : reader.GetString(1); var ordered = reader.GetDecimal(2); var alreadyReceived = reader.GetDecimal(3); var unitCost = reader.GetDecimal(4); var productType = reader.GetString(5); var active = reader.GetBoolean(6); reader.Close();
            if (!active) throw new InvalidOperationException("Pasif ürün teslim alınamaz.");
            if (receipt.Quantity > ordered - alreadyReceived) throw new InvalidOperationException("Teslim miktarı kalan sipariş miktarını aşamaz.");
            if (!productType.Equals("Service", StringComparison.OrdinalIgnoreCase)) InsertInventoryReceipt(connection, transaction, document, receipt.LineId, productId, variantId, receipt.Quantity, unitCost);
            using var update = connection.CreateCommand(); update.Transaction = transaction; update.CommandText = "UPDATE purchase_document_lines SET received_quantity=received_quantity+$quantity WHERE id=$id"; Add(update, "$quantity", receipt.Quantity); Add(update, "$id", receipt.LineId); update.ExecuteNonQuery();
            receivedTotal += receipt.Quantity;
        }
        var remaining = Convert.ToDecimal(Scalar(connection, transaction, "SELECT COALESCE(SUM(quantity-received_quantity),0) FROM purchase_document_lines WHERE purchase_document_id=$id", ("$id", documentId)));
        var status = remaining == 0 ? "Closed" : "PartiallyReceived";
        using (var update = connection.CreateCommand()) { update.Transaction = transaction; update.CommandText = "UPDATE purchase_documents SET status=$status,updated_at=$now WHERE id=$id"; Add(update, "$status", status); Add(update, "$now", DateTime.UtcNow.ToString("O")); Add(update, "$id", documentId); update.ExecuteNonQuery(); }
        Audit(connection, transaction, userName, document.Company, documentId, "PurchaseOrderReceived", $"quantity={receivedTotal};remaining={remaining};status={status}");
        transaction.Commit();
    }

    public void PostInvoice(string documentId, string userName)
    {
        using var connection = Database.OpenConnection(); using var transaction = connection.BeginTransaction();
        var document = ReadDetail(connection, transaction, documentId);
        if (document.Type != "Invoice") throw new InvalidOperationException("Yalnızca alış faturaları kesinleştirilebilir.");
        if (document.Status != "Approved") throw new InvalidOperationException("Kesinleştirme için alış faturası onaylı olmalıdır.");
        using var lines = connection.CreateCommand(); lines.Transaction = transaction;
        lines.CommandText = "SELECT l.id,l.product_id,l.variant_id,l.quantity,l.received_quantity,l.unit_price,p.product_type,p.is_active FROM purchase_document_lines l JOIN products p ON p.id=l.product_id WHERE l.purchase_document_id=$id ORDER BY l.line_no"; Add(lines, "$id", documentId);
        using var reader = lines.ExecuteReader(); var rows = new List<(string Id,string Product,string? Variant,decimal Quantity,decimal Received,decimal Cost,string Type,bool Active)>();
        while (reader.Read()) rows.Add((reader.GetString(0),reader.GetString(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.GetDecimal(3),reader.GetDecimal(4),reader.GetDecimal(5),reader.GetString(6),reader.GetBoolean(7))); reader.Close();
        foreach (var line in rows)
        {
            if (!line.Active) throw new InvalidOperationException("Pasif ürün içeren fatura kesinleştirilemez.");
            var quantity = line.Quantity - line.Received; if (quantity <= 0) continue;
            if (!line.Type.Equals("Service", StringComparison.OrdinalIgnoreCase)) InsertInventoryReceipt(connection, transaction, document, line.Id, line.Product, line.Variant, quantity, line.Cost);
            using var update = connection.CreateCommand(); update.Transaction = transaction; update.CommandText = "UPDATE purchase_document_lines SET received_quantity=quantity WHERE id=$id"; Add(update, "$id", line.Id); update.ExecuteNonQuery();
        }
        var now = DateTime.UtcNow.ToString("O");
        using (var ledger = connection.CreateCommand()) { ledger.Transaction = transaction; ledger.CommandText = "INSERT INTO account_transactions(id,company_id,branch_id,account_id,transaction_type,debit,credit,currency_code,exchange_rate,document_type,document_id,document_no,description,transaction_at,created_at) VALUES($id,$company,$branch,$supplier,'PurchaseInvoice',0,$credit,$currency,1,'PurchaseInvoice',$document,$number,$description,$now,$now)"; Add(ledger, "$id", Guid.NewGuid().ToString()); Add(ledger, "$company", document.Company); Add(ledger, "$branch", document.Branch); Add(ledger, "$supplier", document.Supplier); Add(ledger, "$credit", document.Total); Add(ledger, "$currency", document.Currency); Add(ledger, "$document", documentId); Add(ledger, "$number", document.Number); Add(ledger, "$description", document.Description); Add(ledger, "$now", now); ledger.ExecuteNonQuery(); }
        using (var balance = connection.CreateCommand()) { balance.Transaction = transaction; balance.CommandText = "INSERT INTO account_balances(company_id,account_id,debit,credit,balance,updated_at) VALUES($company,$supplier,0,$credit,-$credit,$now) ON CONFLICT(company_id,account_id) DO UPDATE SET credit=credit+$credit,balance=balance-$credit,updated_at=$now"; Add(balance, "$company", document.Company); Add(balance, "$supplier", document.Supplier); Add(balance, "$credit", document.Total); Add(balance, "$now", now); balance.ExecuteNonQuery(); }
        using (var update = connection.CreateCommand()) { update.Transaction = transaction; update.CommandText = "UPDATE purchase_documents SET status='Posted',posted_at=$now,posted_by=$user,updated_at=$now WHERE id=$id"; Add(update, "$now", now); Add(update, "$user", userName); Add(update, "$id", documentId); update.ExecuteNonQuery(); }
        Audit(connection, transaction, userName, document.Company, documentId, "PurchaseInvoicePosted", $"documentNo={document.Number};supplierId={document.Supplier};grandTotal={document.Total};warehouseId={document.Warehouse}");
        transaction.Commit();
    }

    private static void Validate(PurchaseDocumentEdit edit)
    {
        if (edit.DocumentType is not ("Order" or "Invoice")) throw new ArgumentException("Belge tipi geçersiz.");
        if (string.IsNullOrWhiteSpace(edit.SupplierId)) throw new ArgumentException("Tedarikçi seçin.");
        if (edit.Lines.Count == 0) throw new ArgumentException("En az bir ürün satırı ekleyin.");
        if (edit.ExpectedDate < edit.DocumentDate.Date) throw new ArgumentException("Beklenen tarih belge tarihinden önce olamaz.");
        foreach (var line in edit.Lines) Calculate(line);
    }

    private static (decimal gross, decimal discount, decimal net, decimal vat, decimal total) Calculate(PurchaseLineEdit line)
    {
        if (line.Quantity <= 0 || line.UnitPrice < 0) throw new ArgumentException("Miktar sıfırdan büyük, fiyat negatif olmayan bir değer olmalıdır.");
        if (line.DiscountRate is < 0 or > 100 || line.VatRate is < 0 or > 100) throw new ArgumentException("İskonto ve KDV oranları 0–100 arasında olmalıdır.");
        var gross = line.Quantity * line.UnitPrice; var discount = gross * line.DiscountRate / 100; var net = gross - discount; var vat = net * line.VatRate / 100;
        return (gross, discount, net, vat, net + vat);
    }
    private static (decimal subtotal, decimal discount, decimal tax, decimal total) Calculate(IReadOnlyList<PurchaseLineEdit> lines)
    {
        var values = lines.Select(Calculate).ToArray(); return (values.Sum(x => x.gross), values.Sum(x => x.discount), values.Sum(x => x.vat), values.Sum(x => x.total));
    }
    private static void EnsureSupplier(SqliteConnection connection, SqliteTransaction transaction, string companyId, string supplierId)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT account_type,is_active FROM accounts WHERE id=$id AND company_id=$company"; Add(command, "$id", supplierId); Add(command, "$company", companyId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("Tedarikçi cari kartı bulunamadı.");
        if (!reader.GetBoolean(1)) throw new InvalidOperationException("Pasif tedarikçiyle işlem yapılamaz.");
        if (reader.GetString(0) is not ("Supplier" or "CustomerAndSupplier")) throw new InvalidOperationException("Satınalma belgesi için tedarikçi cari kartı seçilmelidir.");
    }
    private static (string company, string type, string status, DateTime date) Read(SqliteConnection connection, SqliteTransaction transaction, string id)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT company_id,document_type,status,document_date FROM purchase_documents WHERE id=$id"; Add(command, "$id", id);
        using var reader = command.ExecuteReader(); if (!reader.Read()) throw new KeyNotFoundException("Satınalma belgesi bulunamadı.");
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2), DateTime.Parse(reader.GetString(3)));
    }
    private sealed record DocumentDetail(string Id, string Company, string Branch, string Warehouse, string Supplier, string Type, string Status, string Number, string Currency, decimal Total, string Description);
    private static DocumentDetail ReadDetail(SqliteConnection connection, SqliteTransaction transaction, string id)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT id,company_id,branch_id,warehouse_id,supplier_id,document_type,status,COALESCE(document_no,''),currency_code,grand_total,description FROM purchase_documents WHERE id=$id"; Add(command, "$id", id);
        using var reader = command.ExecuteReader(); if (!reader.Read()) throw new KeyNotFoundException("Satınalma belgesi bulunamadı.");
        return new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetString(7),reader.GetString(8),reader.GetDecimal(9),reader.GetString(10));
    }
    private static void InsertInventoryReceipt(SqliteConnection connection, SqliteTransaction transaction, DocumentDetail document, string lineId, string productId, string? variantId, decimal quantity, decimal unitCost)
    {
        var now = DateTime.UtcNow.ToString("O");
        using (var movement = connection.CreateCommand()) { movement.Transaction = transaction; movement.CommandText = "INSERT INTO inventory_transactions(id,company_id,branch_id,warehouse_id,product_id,variant_id,transaction_type,quantity,unit_cost,total_cost,document_type,document_id,document_line_id,reference_no,description,transaction_at,created_at) VALUES($id,$company,$branch,$warehouse,$product,$variant,'PurchaseReceipt',$quantity,$cost,$total,$documentType,$document,$line,$number,$description,$now,$now)"; Add(movement, "$id", Guid.NewGuid().ToString()); Add(movement, "$company", document.Company); Add(movement, "$branch", document.Branch); Add(movement, "$warehouse", document.Warehouse); Add(movement, "$product", productId); Add(movement, "$variant", (object?)variantId ?? DBNull.Value); Add(movement, "$quantity", quantity); Add(movement, "$cost", unitCost); Add(movement, "$total", unitCost * quantity); Add(movement, "$documentType", document.Type == "Order" ? "PurchaseOrderReceipt" : "PurchaseInvoice"); Add(movement, "$document", document.Id); Add(movement, "$line", lineId); Add(movement, "$number", document.Number); Add(movement, "$description", document.Description); Add(movement, "$now", now); movement.ExecuteNonQuery(); }
        var exists = Convert.ToInt64(Scalar(connection, transaction, "SELECT COUNT(*) FROM inventory_balances WHERE warehouse_id=$warehouse AND product_id=$product AND (variant_id=$variant OR (variant_id IS NULL AND $variant IS NULL))", ("$warehouse", document.Warehouse), ("$product", productId), ("$variant", (object?)variantId ?? DBNull.Value))) > 0;
        using var balance = connection.CreateCommand(); balance.Transaction = transaction;
        balance.CommandText = exists ? "UPDATE inventory_balances SET quantity_on_hand=quantity_on_hand+$quantity,quantity_available=quantity_available+$quantity,last_transaction_at=$now,updated_at=$now WHERE warehouse_id=$warehouse AND product_id=$product AND (variant_id=$variant OR (variant_id IS NULL AND $variant IS NULL))" : "INSERT INTO inventory_balances(company_id,branch_id,warehouse_id,product_id,variant_id,quantity_on_hand,quantity_available,last_transaction_at,updated_at) VALUES($company,$branch,$warehouse,$product,$variant,$quantity,$quantity,$now,$now)";
        Add(balance, "$company", document.Company); Add(balance, "$branch", document.Branch); Add(balance, "$warehouse", document.Warehouse); Add(balance, "$product", productId); Add(balance, "$variant", (object?)variantId ?? DBNull.Value); Add(balance, "$quantity", quantity); Add(balance, "$now", now); balance.ExecuteNonQuery();
    }
    private static object Scalar(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters) { using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; foreach (var parameter in parameters) Add(command, parameter.Name, parameter.Value); return command.ExecuteScalar() ?? 0; }
    private static string AllocateNumber(SqliteConnection connection, SqliteTransaction transaction, string companyId, string type, int year)
    {
        var sequence = type == "Order" ? "PurchaseOrder" : "PurchaseInvoice"; var prefix = type == "Order" ? "SS" : "AF";
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO number_sequences(company_id,sequence_type,year,prefix,last_number) VALUES($company,$type,$year,$prefix,1) ON CONFLICT(company_id,sequence_type,year) DO UPDATE SET last_number=last_number+1 RETURNING prefix,last_number";
        Add(command, "$company", companyId); Add(command, "$type", sequence); Add(command, "$year", year); Add(command, "$prefix", prefix);
        using var reader = command.ExecuteReader(); reader.Read(); return $"{reader.GetString(0)}-{year}-{reader.GetInt32(1):D6}";
    }
    private static void Audit(SqliteConnection connection, SqliteTransaction transaction, string userName, string companyId, string entityId, string action, string values)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO audit_logs(id,user_id,company_id,entity_type,entity_id,action,new_values,created_at) VALUES($id,$user,$company,'PurchaseDocument',$entity,$action,$values,$now)";
        Add(command, "$id", Guid.NewGuid().ToString()); Add(command, "$user", userName); Add(command, "$company", companyId); Add(command, "$entity", entityId); Add(command, "$action", action); Add(command, "$values", values); Add(command, "$now", DateTime.UtcNow.ToString("O")); command.ExecuteNonQuery();
    }
    private static void Add(SqliteCommand command, string name, object value) => command.Parameters.AddWithValue(name, value);
}
