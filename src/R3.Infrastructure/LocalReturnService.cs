using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public enum ReturnDirection { Sales, Purchase }

public sealed record ReturnLineEdit(string SourceLineId, decimal Quantity);

public sealed record ReturnDraft(ReturnDirection Direction, string CompanyId, string SourceDocumentId, DateTime DocumentDate, string Description, IReadOnlyList<ReturnLineEdit> Lines);

public sealed record ReturnPostResult(string ReturnId, string DocumentNo, decimal GrandTotal);

/// <summary>
/// Satış İadeleri and Satınalma İadeleri. A return always points at a posted invoice and undoes exactly
/// what posting that invoice did, line by line, at the invoice's own price, iskonto and KDV:
///  - satış iadesi: cari ALACAK (the customer owes less), stock back in with SaleReturn;
///  - alış iadesi: tedarikçi BORÇ (we owe less), stock out with PurchaseReturn at the purchase cost -
///    this stock-out obeys the warehouse's negative-stock policy like every other one.
/// Per source line, returned quantity can never exceed invoiced minus already returned (cancelled returns
/// do not count). Service products post money only. Cancelling a return writes reversing movements and
/// ledger rows; nothing is deleted. Posting and cancelling are each one transaction.
/// Not done here: the e-Fatura/e-Arşiv "İADE" document for sales returns (the e-document pipeline belongs to
/// the invoice flow and is outside this service).
/// </summary>
public sealed class LocalReturnService(StoreDatabase database)
{
    private sealed record SourceLine(string Id, string ProductId, string? VariantId, string UnitId, decimal Quantity, decimal Factor, decimal UnitPrice, decimal DiscountRate, decimal VatRate, string ProductType, decimal Returned);
    private sealed record SourceDocument(string Id, string CompanyId, string BranchId, string WarehouseId, string AccountId, string DocumentNo, string Status);

    public DataTable SourceDocuments(string companyId, ReturnDirection direction, string? search = null)
    {
        var q = $"%{search?.Trim() ?? ""}%";
        return direction == ReturnDirection.Sales
            ? database.Query("""
                SELECT s.id AS Id, COALESCE(s.document_no,'') AS BelgeNo, s.document_date AS Tarih, a.code AS CariKodu, a.name AS Cari, s.grand_total AS Tutar,
                       (SELECT COALESCE(SUM(r.grand_total),0) FROM return_documents r WHERE r.source_document_id=s.id AND r.status='Posted') AS IadeEdilen
                FROM sales_documents s JOIN accounts a ON a.id=s.account_id
                WHERE s.company_id=$c AND s.status='Posted' AND (COALESCE(s.document_no,'') LIKE $q OR a.name LIKE $q OR a.code LIKE $q)
                ORDER BY s.document_date DESC LIMIT 500
                """, ("$c", companyId), ("$q", q))
            : database.Query("""
                SELECT d.id AS Id, COALESCE(d.document_no,'') AS BelgeNo, d.document_date AS Tarih, a.code AS CariKodu, a.name AS Cari, d.grand_total AS Tutar,
                       (SELECT COALESCE(SUM(r.grand_total),0) FROM return_documents r WHERE r.source_document_id=d.id AND r.status='Posted') AS IadeEdilen
                FROM purchase_documents d JOIN accounts a ON a.id=d.supplier_id
                WHERE d.company_id=$c AND d.document_type='Invoice' AND d.status='Posted' AND (COALESCE(d.document_no,'') LIKE $q OR a.name LIKE $q OR a.code LIKE $q)
                ORDER BY d.document_date DESC LIMIT 500
                """, ("$c", companyId), ("$q", q));
    }

    /// <summary>Lines of a posted invoice with how much is still returnable (in the line's own unit).</summary>
    public DataTable SourceLines(ReturnDirection direction, string sourceDocumentId)
    {
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        var table = new DataTable();
        foreach (var (name, type) in new[] { ("LineId", typeof(string)), ("UrunId", typeof(string)), ("StokKodu", typeof(string)), ("StokAdi", typeof(string)), ("Birim", typeof(string)),
                     ("Miktar", typeof(decimal)), ("IadeEdilen", typeof(decimal)), ("Kalan", typeof(decimal)), ("BirimFiyat", typeof(decimal)), ("Iskonto", typeof(decimal)), ("Kdv", typeof(decimal)) })
            table.Columns.Add(name, type);
        foreach (var line in ReadLines(c, tx, direction, sourceDocumentId))
        {
            var product = Row(c, tx, "SELECT p.code, p.name, COALESCE(u.code,'') FROM products p LEFT JOIN units u ON u.id=$u WHERE p.id=$p", ("$p", line.ProductId), ("$u", line.UnitId))!;
            table.Rows.Add(line.Id, line.ProductId, product[0], product[1], product[2], line.Quantity, line.Returned, line.Quantity - line.Returned, line.UnitPrice, line.DiscountRate, line.VatRate);
        }
        return table;
    }

    public ReturnPostResult Post(ReturnDraft draft, string userName)
    {
        if (draft.Lines.Count == 0 || draft.Lines.All(x => x.Quantity == 0)) throw new ArgumentException("İade edilecek en az bir satır ve miktar girilmelidir.");
        if (draft.Lines.Any(x => x.Quantity < 0)) throw new ArgumentException("İade miktarı negatif olamaz.");
        if (draft.Lines.Select(x => x.SourceLineId).Distinct().Count() != draft.Lines.Count) throw new ArgumentException("Aynı fatura satırı iadede birden fazla kez yer alamaz.");

        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        var source = ReadSource(c, tx, draft.Direction, draft.SourceDocumentId);
        if (source.CompanyId != draft.CompanyId) throw new ArgumentException("Fatura bu firmaya ait değil.");
        if (source.Status != "Posted") throw new InvalidOperationException("Yalnızca kesinleşmiş faturalar iade edilebilir.");
        var lines = ReadLines(c, tx, draft.Direction, draft.SourceDocumentId).ToDictionary(x => x.Id);

        var id = Guid.NewGuid().ToString(); var now = DateTime.UtcNow.ToString("O");
        var number = AllocateNumber(c, tx, draft.CompanyId, draft.Direction, draft.DocumentDate.Year);
        decimal netTotal = 0, vatTotal = 0; var lineNo = 0;
        var posted = new List<(SourceLine Line, decimal Quantity, decimal Net, decimal Vat)>();
        foreach (var edit in draft.Lines.Where(x => x.Quantity > 0))
        {
            if (!lines.TryGetValue(edit.SourceLineId, out var line)) throw new ArgumentException("İade satırı bu faturaya ait değil.");
            var remaining = line.Quantity - line.Returned;
            if (edit.Quantity > remaining) throw new InvalidOperationException($"İade miktarı faturada kalan miktarı aşıyor. Faturalanan: {line.Quantity:N2}; önceden iade: {line.Returned:N2}; iade edilebilir: {remaining:N2}.");
            var gross = edit.Quantity * line.UnitPrice; var net = Math.Round(gross - gross * line.DiscountRate / 100m, 2); var vat = Math.Round(net * line.VatRate / 100m, 2);
            netTotal += net; vatTotal += vat; posted.Add((line, edit.Quantity, net, vat));
        }
        var grandTotal = netTotal + vatTotal;
        Exec(c, tx, """
            INSERT INTO return_documents(id,company_id,branch_id,warehouse_id,direction,source_document_id,account_id,document_no,document_date,status,description,net_total,vat_total,grand_total,created_by,created_at)
            VALUES($id,$c,$b,$w,$dir,$src,$a,$no,$date,'Posted',$desc,$net,$vat,$total,$user,$now)
            """, ("$id", id), ("$c", source.CompanyId), ("$b", source.BranchId), ("$w", source.WarehouseId), ("$dir", draft.Direction.ToString()), ("$src", source.Id), ("$a", source.AccountId),
            ("$no", number), ("$date", draft.DocumentDate.ToString("yyyy-MM-dd")), ("$desc", draft.Description.Trim()), ("$net", netTotal), ("$vat", vatTotal), ("$total", grandTotal), ("$user", userName), ("$now", now));

        var inbound = draft.Direction == ReturnDirection.Sales;
        foreach (var (line, quantity, net, vat) in posted)
        {
            var lineId = Guid.NewGuid().ToString();
            Exec(c, tx, """
                INSERT INTO return_document_lines(id,return_document_id,line_no,source_line_id,product_id,variant_id,unit_id,quantity,quantity_factor,unit_price,discount_rate,vat_rate,net_amount,vat_amount,line_total)
                VALUES($id,$r,$no,$src,$p,$v,$u,$q,$f,$price,$disc,$vatRate,$net,$vat,$total)
                """, ("$id", lineId), ("$r", id), ("$no", ++lineNo), ("$src", line.Id), ("$p", line.ProductId), ("$v", (object?)line.VariantId ?? DBNull.Value), ("$u", line.UnitId),
                ("$q", quantity), ("$f", line.Factor), ("$price", line.UnitPrice), ("$disc", line.DiscountRate), ("$vatRate", line.VatRate), ("$net", net), ("$vat", vat), ("$total", net + vat));
            if (line.ProductType.Equals("Service", StringComparison.OrdinalIgnoreCase)) continue;
            var baseQuantity = quantity * line.Factor;
            Move(c, tx, source, line, baseQuantity, inbound, inbound ? "SaleReturn" : "PurchaseReturn", draft.Direction + "Return", id, lineId, number, now,
                inbound ? null : line.UnitPrice / (line.Factor == 0 ? 1 : line.Factor));
        }
        Ledger(c, tx, source, draft.Direction == ReturnDirection.Sales ? -grandTotal : grandTotal, draft.Direction + "Return", id, number, $"{number} iade — {source.DocumentNo}", now);
        Audit(c, tx, source.CompanyId, id, draft.Direction + "ReturnPosted", $"{number} kaynak={source.DocumentNo} toplam={grandTotal:N2}", userName, now);
        tx.Commit();
        return new ReturnPostResult(id, number, grandTotal);
    }

    public void Cancel(string returnId, string reason, string userName)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("İptal nedeni zorunludur.");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        var doc = Row(c, tx, "SELECT direction,source_document_id,status,document_no,grand_total FROM return_documents WHERE id=$id", ("$id", returnId)) ?? throw new ArgumentException("İade belgesi bulunamadı.");
        if (doc[2]!.ToString() != "Posted") throw new InvalidOperationException("Yalnızca kesinleşmiş iade iptal edilebilir.");
        var direction = Enum.Parse<ReturnDirection>(doc[0]!.ToString()!);
        var source = ReadSource(c, tx, direction, doc[1]!.ToString()!);
        var number = doc[3]!.ToString()!; var total = Convert.ToDecimal(doc[4]); var now = DateTime.UtcNow.ToString("O");
        var lines = new List<(string Id, string Product, string? Variant, decimal Quantity, decimal Factor, decimal Price, string Type)>();
        using (var q = c.CreateCommand())
        {
            q.Transaction = tx; q.CommandText = "SELECT l.id,l.product_id,l.variant_id,l.quantity,l.quantity_factor,l.unit_price,p.product_type FROM return_document_lines l JOIN products p ON p.id=l.product_id WHERE l.return_document_id=$id";
            q.Parameters.AddWithValue("$id", returnId);
            using var r = q.ExecuteReader();
            while (r.Read()) lines.Add((r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetDecimal(3), r.GetDecimal(4), r.GetDecimal(5), r.GetString(6)));
        }
        // Undo: a cancelled sales return takes the goods back out, a cancelled purchase return brings them back in.
        var inbound = direction == ReturnDirection.Purchase;
        foreach (var line in lines.Where(x => !x.Type.Equals("Service", StringComparison.OrdinalIgnoreCase)))
            Move(c, tx, source, new SourceLine(line.Id, line.Product, line.Variant, "", line.Quantity, line.Factor, line.Price, 0, 0, line.Type, 0), line.Quantity * line.Factor, inbound,
                inbound ? "PurchaseReceipt" : "SaleIssue", direction + "ReturnCancel", returnId, line.Id, number, now, inbound ? line.Price / (line.Factor == 0 ? 1 : line.Factor) : null);
        Ledger(c, tx, source, direction == ReturnDirection.Sales ? total : -total, "Cancellation", returnId, number, $"{number} iade iptali: {reason.Trim()}", now);
        Exec(c, tx, "UPDATE return_documents SET status='Cancelled', cancelled_by=$u, cancelled_at=$now, cancel_reason=$r WHERE id=$id AND status='Posted'", ("$u", userName), ("$now", now), ("$r", reason.Trim()), ("$id", returnId));
        Audit(c, tx, source.CompanyId, returnId, direction + "ReturnCancelled", reason.Trim(), userName, now);
        tx.Commit();
    }

    public DataTable Search(string companyId, ReturnDirection direction, string? search = null)
    {
        var q = $"%{search?.Trim() ?? ""}%";
        return database.Query("""
            SELECT r.id AS Id, r.document_no AS IadeNo, r.document_date AS Tarih, a.code AS CariKodu, a.name AS Cari,
                   COALESCE(s.document_no, p.document_no, '') AS FaturaNo, r.source_document_id AS FaturaId, r.account_id AS CariId,
                   r.net_total AS Net, r.vat_total AS Kdv, r.grand_total AS Toplam, CASE r.status WHEN 'Posted' THEN 'Kesinleşti' ELSE 'İptal' END AS Durum, r.description AS Aciklama
            FROM return_documents r JOIN accounts a ON a.id=r.account_id
            LEFT JOIN sales_documents s ON s.id=r.source_document_id LEFT JOIN purchase_documents p ON p.id=r.source_document_id
            WHERE r.company_id=$c AND r.direction=$d AND (r.document_no LIKE $q OR a.name LIKE $q OR COALESCE(s.document_no,p.document_no,'') LIKE $q)
            ORDER BY r.document_date DESC, r.created_at DESC
            """, ("$c", companyId), ("$d", direction.ToString()), ("$q", q));
    }

    public DataTable Lines(string returnId) => database.Query("""
        SELECT l.line_no AS Sira, p.code AS StokKodu, p.name AS StokAdi, l.quantity AS Miktar, l.unit_price AS BirimFiyat, l.discount_rate AS Iskonto, l.vat_rate AS Kdv, l.net_amount AS Net, l.line_total AS Toplam
        FROM return_document_lines l JOIN products p ON p.id=l.product_id WHERE l.return_document_id=$id ORDER BY l.line_no
        """, ("$id", returnId));

    private static SourceDocument ReadSource(SqliteConnection c, SqliteTransaction tx, ReturnDirection direction, string id)
    {
        var r = direction == ReturnDirection.Sales
            ? Row(c, tx, "SELECT id,company_id,branch_id,warehouse_id,account_id,COALESCE(document_no,''),status FROM sales_documents WHERE id=$id", ("$id", id))
            : Row(c, tx, "SELECT id,company_id,branch_id,warehouse_id,supplier_id,COALESCE(document_no,''),status FROM purchase_documents WHERE id=$id AND document_type='Invoice'", ("$id", id));
        if (r == null) throw new ArgumentException("Kaynak fatura bulunamadı.");
        return new SourceDocument(r[0]!.ToString()!, r[1]!.ToString()!, r[2]!.ToString()!, r[3]!.ToString()!, r[4]!.ToString()!, r[5]!.ToString()!, r[6]!.ToString()!);
    }

    private static List<SourceLine> ReadLines(SqliteConnection c, SqliteTransaction tx, ReturnDirection direction, string documentId)
    {
        var sql = direction == ReturnDirection.Sales
            ? "SELECT l.id,l.product_id,l.variant_id,l.unit_id,l.quantity,l.quantity_factor,l.unit_price,l.discount_rate,l.vat_rate,p.product_type FROM sales_document_lines l JOIN products p ON p.id=l.product_id WHERE l.sales_document_id=$id ORDER BY l.line_no"
            : "SELECT l.id,l.product_id,l.variant_id,l.unit_id,l.quantity,1,l.unit_price,l.discount_rate,l.vat_rate,p.product_type FROM purchase_document_lines l JOIN products p ON p.id=l.product_id WHERE l.purchase_document_id=$id ORDER BY l.line_no";
        var result = new List<SourceLine>();
        using (var q = c.CreateCommand())
        {
            q.Transaction = tx; q.CommandText = sql; q.Parameters.AddWithValue("$id", documentId);
            using var r = q.ExecuteReader();
            while (r.Read()) result.Add(new SourceLine(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetDecimal(4), r.GetDecimal(5), r.GetDecimal(6), r.GetDecimal(7), r.GetDecimal(8), r.GetString(9), 0));
        }
        return result.Select(line => line with
        {
            Returned = Convert.ToDecimal(Row(c, tx, "SELECT COALESCE(SUM(l.quantity),0) FROM return_document_lines l JOIN return_documents r ON r.id=l.return_document_id WHERE l.source_line_id=$l AND r.status='Posted'", ("$l", line.Id))![0])
        }).ToList();
    }

    private static void Move(SqliteConnection c, SqliteTransaction tx, SourceDocument source, SourceLine line, decimal baseQuantity, bool inbound, string type, string documentType,
        string documentId, string documentLineId, string reference, string now, decimal? unitCost)
    {
        var balance = Row(c, tx, "SELECT quantity_available FROM inventory_balances WHERE warehouse_id=$w AND product_id=$p AND (variant_id=$v OR (variant_id IS NULL AND $v IS NULL))",
            ("$w", source.WarehouseId), ("$p", line.ProductId), ("$v", (object?)line.VariantId ?? DBNull.Value));
        if (!inbound) InventoryStockPolicy.EnsureAvailable(c, tx, source.WarehouseId, balance == null ? 0 : Convert.ToDecimal(balance[0]), baseQuantity,
            $"İade için depoda yeterli stok yok. Ürün stoğu: {(balance == null ? 0 : Convert.ToDecimal(balance[0])):N2}; iade: {baseQuantity:N2}.");
        Exec(c, tx, """
            INSERT INTO inventory_transactions(id,company_id,branch_id,warehouse_id,product_id,variant_id,transaction_type,quantity,unit_cost,total_cost,document_type,document_id,document_line_id,reference_no,description,transaction_at,created_at)
            VALUES($id,$c,$b,$w,$p,$v,$type,$q,$cost,$total,$dt,$doc,$line,$ref,$desc,$now,$now)
            """, ("$id", Guid.NewGuid().ToString()), ("$c", source.CompanyId), ("$b", source.BranchId), ("$w", source.WarehouseId), ("$p", line.ProductId), ("$v", (object?)line.VariantId ?? DBNull.Value),
            ("$type", type), ("$q", baseQuantity), ("$cost", (object?)unitCost ?? DBNull.Value), ("$total", unitCost is { } u ? u * baseQuantity : DBNull.Value), ("$dt", documentType), ("$doc", documentId),
            ("$line", documentLineId), ("$ref", reference), ("$desc", $"{reference} ({source.DocumentNo})"), ("$now", now));
        var delta = inbound ? baseQuantity : -baseQuantity;
        if (balance == null)
            Exec(c, tx, "INSERT INTO inventory_balances(company_id,branch_id,warehouse_id,product_id,variant_id,quantity_on_hand,quantity_available,last_transaction_at,updated_at) VALUES($c,$b,$w,$p,$v,$d,$d,$now,$now)",
                ("$c", source.CompanyId), ("$b", source.BranchId), ("$w", source.WarehouseId), ("$p", line.ProductId), ("$v", (object?)line.VariantId ?? DBNull.Value), ("$d", delta), ("$now", now));
        else
            Exec(c, tx, "UPDATE inventory_balances SET quantity_on_hand=quantity_on_hand+$d, quantity_available=quantity_available+$d, last_transaction_at=$now, updated_at=$now WHERE warehouse_id=$w AND product_id=$p AND (variant_id=$v OR (variant_id IS NULL AND $v IS NULL))",
                ("$d", delta), ("$now", now), ("$w", source.WarehouseId), ("$p", line.ProductId), ("$v", (object?)line.VariantId ?? DBNull.Value));
    }

    // signedAmount > 0 = cari borç (debit), < 0 = cari alacak (credit); same convention as account_balances.balance.
    private static void Ledger(SqliteConnection c, SqliteTransaction tx, SourceDocument source, decimal signedAmount, string type, string documentId, string number, string description, string now)
    {
        var debit = Math.Max(0, signedAmount); var credit = Math.Max(0, -signedAmount);
        Exec(c, tx, """
            INSERT INTO account_transactions(id,company_id,branch_id,account_id,transaction_type,debit,credit,currency_code,exchange_rate,document_type,document_id,document_no,description,transaction_at,created_at)
            VALUES($id,$c,$b,$a,$type,$debit,$credit,'TRY',1,$type,$doc,$no,$desc,$now,$now)
            """, ("$id", Guid.NewGuid().ToString()), ("$c", source.CompanyId), ("$b", source.BranchId), ("$a", source.AccountId), ("$type", type), ("$debit", debit), ("$credit", credit),
            ("$doc", documentId), ("$no", number), ("$desc", description), ("$now", now));
        Exec(c, tx, """
            INSERT INTO account_balances(company_id,account_id,debit,credit,balance,updated_at) VALUES($c,$a,$debit,$credit,$debit-$credit,$now)
            ON CONFLICT(company_id,account_id) DO UPDATE SET debit=debit+$debit, credit=credit+$credit, balance=balance+$debit-$credit, updated_at=$now
            """, ("$c", source.CompanyId), ("$a", source.AccountId), ("$debit", debit), ("$credit", credit), ("$now", now));
    }

    private static string AllocateNumber(SqliteConnection c, SqliteTransaction tx, string companyId, ReturnDirection direction, int year)
    {
        var prefix = direction == ReturnDirection.Sales ? "SIA" : "AIA";
        var next = Convert.ToInt64(Row(c, tx, "SELECT COUNT(*) FROM return_documents WHERE company_id=$c AND direction=$d AND document_no LIKE $p", ("$c", companyId), ("$d", direction.ToString()), ("$p", $"{prefix}{year}%"))![0]) + 1;
        return $"{prefix}{year}{next:000000}";
    }

    private static object?[]? Row(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] parameters)
    {
        using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = sql;
        foreach (var (n, v) in parameters) q.Parameters.AddWithValue(n, v);
        using var r = q.ExecuteReader(); if (!r.Read()) return null;
        var values = new object?[r.FieldCount]; r.GetValues(values!); return values;
    }

    private static void Exec(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] parameters)
    {
        using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = sql;
        foreach (var (n, v) in parameters) q.Parameters.AddWithValue(n, v);
        q.ExecuteNonQuery();
    }

    private static void Audit(SqliteConnection c, SqliteTransaction tx, string companyId, string id, string action, string detail, string userName, string now) =>
        Exec(c, tx, "INSERT INTO audit_logs(id,user_id,company_id,entity_type,entity_id,action,new_values,created_at) VALUES($id,$u,$c,'ReturnDocument',$e,$a,$n,$now)",
            ("$id", Guid.NewGuid().ToString()), ("$u", userName), ("$c", companyId), ("$e", id), ("$a", action), ("$n", detail), ("$now", now));
}
