using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record SalesLineEdit(string ProductId, string? VariantId, string UnitId, string? BarcodeId, decimal Quantity, decimal QuantityFactor, decimal UnitPrice, decimal DiscountRate, decimal VatRate, string Description = "");
public sealed record SalesDraftEdit(string Id, string CompanyId, string BranchId, string WarehouseId, string AccountId, DateTime DocumentDate, string Description, IReadOnlyList<SalesLineEdit> Lines);
public sealed record SalesTotals(decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal GrandTotal);
public sealed record SalesPostResult(string InvoiceId, string DocumentNo, string ElectronicDocumentId, ElectronicDocumentType ElectronicDocumentType);
public sealed record SalesDocumentHeader(string Id, string CompanyId, string BranchId, string WarehouseId, string BranchName, string WarehouseName,
    string AccountId, string AccountCode, string AccountName, string? DocumentNo, DateTime DocumentDate, string Status, string Description, SalesTotals Totals);
public sealed record SalesDocumentLineRow(string Id, string ProductId, string ProductCode, string ProductName, string? VariantId, string UnitCode,
    decimal Quantity, decimal UnitPrice, decimal DiscountRate, decimal DiscountAmount, decimal VatRate, decimal NetAmount, decimal VatAmount, decimal LineTotal);

// Canonical Posting Engine (Phase 5, docs/architecture/POSTING-ENGINE.md). Post() already owned a
// single connection/transaction boundary for Invoice + Account ledger + Inventory ledger + Audit
// before this phase - that part of the "correct model" (spec §4) already existed. What Phase 5 adds
// is the electronic document: created and moved to Ready inside that SAME transaction (via
// LocalElectronicDocumentService's *WithinTransaction methods), so an invoice can never end up posted
// without its e-document counterpart, or vice versa. No outbox row is created here - Queued requires
// Generated, which requires a real UBL payload, and no UBL generator exists yet (a later phase); see
// the docs file for why posting deliberately stops at Ready.
public sealed class LocalSalesService(StoreDatabase database, ElectronicDocumentRoutingService routing, LocalElectronicDocumentService documents)
{
    // EBelgeTipi/EBelgeDurumu are the raw backend enum strings (e.g. "EInvoice"/"Sent") - Phase 8
    // deliberately does not turn them into Turkish here (spec §6): that mapping belongs to
    // R3.Desktop.Presentation.EDocumentPresentation, the one place UI labels are decided.
    public DataTable Search(string companyId, string? search = null, string? status = null) => database.Query("""
        SELECT s.id AS Id,COALESCE(s.document_no,'Taslak') AS FaturaNo,s.document_date AS Tarih,a.id AS CariId,a.code AS CariKod,a.name AS Cari,
               COALESCE(br.name,s.branch_id) AS Sube,COALESCE(w.name,s.warehouse_id) AS Depo,s.subtotal AS AraToplam,s.discount_total AS Iskonto,
               s.tax_total AS KDV,s.grand_total AS GenelToplam,s.status AS Durum,d.document_type AS EBelgeTipi,d.status AS EBelgeDurumu
        FROM sales_documents s JOIN accounts a ON a.id=s.account_id
        LEFT JOIN branches br ON br.id=s.branch_id LEFT JOIN warehouses w ON w.id=s.warehouse_id
        LEFT JOIN electronic_documents d ON d.source_entity_type='SalesInvoice' AND d.source_entity_id=s.id AND d.status<>'Cancelled'
        WHERE s.company_id=$c AND ($q='' OR s.document_no LIKE $q OR a.code LIKE $q OR a.name LIKE $q) AND ($status='' OR s.status=$status)
        ORDER BY s.document_date DESC
        """, ("$c", companyId), ("$q", $"%{search?.Trim() ?? ""}%"), ("$status", status ?? ""));
    public string CreateDraft(SalesDraftEdit draft) { var id = string.IsNullOrWhiteSpace(draft.Id) ? Guid.NewGuid().ToString() : draft.Id; SaveDraft(draft with { Id = id }); return id; }
    public void SaveDraft(SalesDraftEdit draft)
    {
        if (draft.Lines.Count == 0) throw new ArgumentException("Faturada en az bir satır olmalıdır.");
        using var c = Open(); using var tx = c.BeginTransaction();
        foreach (var line in draft.Lines)
        {
            using var product = c.CreateCommand(); product.Transaction = tx; product.CommandText = "SELECT product_type,is_active,is_sellable,company_id FROM products WHERE id=$id"; Add(product, "$id", line.ProductId);
            using var reader = product.ExecuteReader(); if (!reader.Read() || reader.GetString(3) != draft.CompanyId) throw new InvalidOperationException("Ürün firma kapsamında değil.");
            if (!reader.GetBoolean(1)) throw new InvalidOperationException("Pasif ürün satış belgesine eklenemez.");
            if (!reader.GetBoolean(2)) throw new InvalidOperationException("Satışa kapalı ürün satış belgesine eklenemez.");
            var type = reader.GetString(0); if (!type.Equals("Stock", StringComparison.OrdinalIgnoreCase) && !type.Equals("Service", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"{type} ürün tipi satış akışında henüz desteklenmiyor.");
        }
        var totals = Calculate(draft.Lines); var id = string.IsNullOrWhiteSpace(draft.Id) ? Guid.NewGuid().ToString() : draft.Id;
        using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "INSERT INTO sales_documents(id,company_id,branch_id,warehouse_id,account_id,document_date,status,subtotal,discount_total,tax_total,grand_total,description,created_at,updated_at) VALUES($id,$c,$b,$w,$a,$date,'Draft',$sub,$disc,$tax,$total,$desc,$now,$now) ON CONFLICT(id) DO UPDATE SET account_id=$a,warehouse_id=$w,document_date=$date,subtotal=$sub,discount_total=$disc,tax_total=$tax,grand_total=$total,description=$desc,updated_at=$now WHERE sales_documents.status='Draft'";
        Add(cmd,"$id",id); Add(cmd,"$c",draft.CompanyId); Add(cmd,"$b",draft.BranchId); Add(cmd,"$w",draft.WarehouseId); Add(cmd,"$a",draft.AccountId); Add(cmd,"$date",draft.DocumentDate.ToString("O")); Add(cmd,"$sub",totals.Subtotal); Add(cmd,"$disc",totals.DiscountTotal); Add(cmd,"$tax",totals.TaxTotal); Add(cmd,"$total",totals.GrandTotal); Add(cmd,"$desc",draft.Description ?? ""); Add(cmd,"$now",DateTime.UtcNow.ToString("O")); cmd.ExecuteNonQuery();
        using (var del=c.CreateCommand()) { del.Transaction=tx; del.CommandText="DELETE FROM sales_document_lines WHERE sales_document_id=$id"; Add(del,"$id",id); del.ExecuteNonQuery(); }
        var n=1; foreach(var line in draft.Lines) { var calc=CalculateLine(line); using var l=c.CreateCommand(); l.Transaction=tx; l.CommandText="INSERT INTO sales_document_lines(id,sales_document_id,line_no,product_id,variant_id,unit_id,barcode_id,quantity,quantity_factor,base_quantity,unit_price,discount_rate,discount_amount,vat_rate,gross_amount,net_amount,vat_amount,line_total,description) VALUES($id,$doc,$no,$p,$v,$u,$bar,$q,$f,$base,$price,$dr,$da,$vr,$gross,$net,$vat,$total,$desc)"; Add(l,"$id",Guid.NewGuid().ToString()); Add(l,"$doc",id); Add(l,"$no",n++); Add(l,"$p",line.ProductId); Add(l,"$v",(object?)line.VariantId??DBNull.Value); Add(l,"$u",line.UnitId); Add(l,"$bar",(object?)line.BarcodeId??DBNull.Value); Add(l,"$q",line.Quantity); Add(l,"$f",line.QuantityFactor); Add(l,"$base",line.Quantity*line.QuantityFactor); Add(l,"$price",line.UnitPrice); Add(l,"$dr",line.DiscountRate); Add(l,"$da",calc.Discount); Add(l,"$vr",line.VatRate); Add(l,"$gross",calc.Gross); Add(l,"$net",calc.Net); Add(l,"$vat",calc.Vat); Add(l,"$total",calc.Total); Add(l,"$desc",line.Description??""); l.ExecuteNonQuery(); }
        tx.Commit();
    }
    public SalesPostResult Post(string documentId, string userId)
    {
        using var c=Open(); using var tx=c.BeginTransaction();
        var doc=ReadDoc(c,tx,documentId); if(doc.Status!="Draft") throw new InvalidOperationException("Fatura daha önce işlenmiş.");
        using var account=c.CreateCommand(); account.Transaction=tx; account.CommandText="SELECT account_type,is_active,code,name,tax_number,identity_number,email FROM accounts WHERE id=$id AND company_id=$c"; Add(account,"$id",doc.AccountId); Add(account,"$c",doc.CompanyId); using var ar=account.ExecuteReader(); if(!ar.Read()) throw new InvalidOperationException("Cari hesap bulunamadı."); if(!ar.GetBoolean(1)) throw new InvalidOperationException("Cari hesap pasif."); if(ar.GetString(0) is not ("Customer" or "CustomerAndSupplier")) throw new InvalidOperationException("Satış için müşteri cari hesabı seçilmelidir.");
        var accountSnapshot = new { code = ar.GetString(2), name = ar.GetString(3), taxNumber = ar.GetString(4), identityNumber = ar.GetString(5), email = ar.GetString(6) };
        ar.Close();
        using var lines=c.CreateCommand(); lines.Transaction=tx; lines.CommandText="SELECT id,product_id,variant_id,quantity,quantity_factor FROM sales_document_lines WHERE sales_document_id=$id ORDER BY line_no"; Add(lines,"$id",documentId); using var lr=lines.ExecuteReader(); var rows=new List<(string id,string p,string? v,decimal q,decimal f)>(); while(lr.Read()) rows.Add((lr.GetString(0),lr.GetString(1),lr.IsDBNull(2)?null:lr.GetString(2),lr.GetDecimal(3),lr.GetDecimal(4))); lr.Close();
        foreach(var line in rows) { using var p=c.CreateCommand(); p.Transaction=tx; p.CommandText="SELECT product_type,is_active,company_id FROM products WHERE id=$id"; Add(p,"$id",line.p); using var pr=p.ExecuteReader(); if(!pr.Read()||pr.GetString(2)!=doc.CompanyId||!pr.GetBoolean(1)) throw new InvalidOperationException("Ürün pasif veya firma kapsamında değil."); var type=pr.GetString(0); pr.Close(); if(type.Equals("Service",StringComparison.OrdinalIgnoreCase)) continue; var qty=line.q*line.f; using var bal=c.CreateCommand(); bal.Transaction=tx; bal.CommandText="SELECT COALESCE(quantity_available,0) FROM inventory_balances WHERE warehouse_id=$w AND product_id=$p AND (variant_id=$v OR (variant_id IS NULL AND $v IS NULL))"; Add(bal,"$w",doc.WarehouseId); Add(bal,"$p",line.p); Add(bal,"$v",(object?)line.v??DBNull.Value); if(Convert.ToDecimal(bal.ExecuteScalar()??0)<qty) throw new InvalidOperationException("Yetersiz stok."); InsertIssue(c,tx,doc,line.id,qty); ApplyBalance(c,tx,doc,line.v,line.p,-qty); }
        var no=AllocateNumber(c,tx,doc.CompanyId,doc.DocumentDate.Year); using(var up=c.CreateCommand()){up.Transaction=tx;up.CommandText="UPDATE sales_documents SET status='Posted',document_no=$no,posting_date=$date,posted_at=$date,updated_at=$date WHERE id=$id";Add(up,"$no",no);Add(up,"$date",DateTime.UtcNow.ToString("O"));Add(up,"$id",documentId);up.ExecuteNonQuery();}
        using(var at=c.CreateCommand()){at.Transaction=tx;at.CommandText="INSERT INTO account_transactions(id,company_id,branch_id,account_id,transaction_type,debit,document_type,document_id,document_no,description,transaction_at,created_at) VALUES($id,$c,$b,$a,'SalesInvoice',$debit,'SalesInvoice',$doc,$no,$desc,$at,$at)";Add(at,"$id",Guid.NewGuid().ToString());Add(at,"$c",doc.CompanyId);Add(at,"$b",doc.BranchId);Add(at,"$a",doc.AccountId);Add(at,"$debit",doc.GrandTotal);Add(at,"$doc",documentId);Add(at,"$no",no);Add(at,"$desc",doc.Description);Add(at,"$at",DateTime.UtcNow.ToString("O"));at.ExecuteNonQuery();}
        using(var ab=c.CreateCommand()){ab.Transaction=tx;ab.CommandText="INSERT INTO account_balances(company_id,account_id,debit,credit,balance,updated_at) VALUES($c,$a,$d,0,$d,$now) ON CONFLICT(company_id,account_id) DO UPDATE SET debit=debit+$d,balance=balance+$d,updated_at=$now";Add(ab,"$c",doc.CompanyId);Add(ab,"$a",doc.AccountId);Add(ab,"$d",doc.GrandTotal);Add(ab,"$now",DateTime.UtcNow.ToString("O"));ab.ExecuteNonQuery();}

        // Electronic document: created and advanced to Ready in this SAME transaction (spec §15) - an
        // invoice can never end up Posted without its e-document counterpart existing, or vice versa.
        var documentType = routing.RouteOutgoingInvoice(doc.CompanyId, doc.AccountId);
        var recipientSnapshotJson = System.Text.Json.JsonSerializer.Serialize(accountSnapshot);
        var electronicDocumentId = documents.CreateOrGetForSourceWithinTransaction(c, tx, new ElectronicDocumentDraft(
            doc.CompanyId, doc.BranchId, documentType, ElectronicDocumentDirection.Outgoing, "SalesInvoice", documentId,
            doc.AccountId, no, doc.DocumentDate, "TRY", doc.GrandTotal, recipientSnapshotJson, userId));
        documents.ReadyWithinTransaction(c, tx, electronicDocumentId, userId);

        using(var audit=c.CreateCommand()){audit.Transaction=tx; audit.CommandText="INSERT INTO audit_logs(id,user_id,company_id,entity_type,entity_id,action,new_values,created_at) VALUES($id,$user,$c,'SalesDocument',$doc,'SalesInvoicePosted',$values,$now)"; Add(audit,"$id",Guid.NewGuid().ToString()); Add(audit,"$user",userId); Add(audit,"$c",doc.CompanyId); Add(audit,"$doc",doc.Id); Add(audit,"$values",$"documentNo={no};accountId={doc.AccountId};grandTotal={doc.GrandTotal};warehouseId={doc.WarehouseId};electronicDocumentId={electronicDocumentId}"); Add(audit,"$now",DateTime.UtcNow.ToString("O")); audit.ExecuteNonQuery();}
        tx.Commit();
        return new SalesPostResult(documentId, no, electronicDocumentId, documentType);
    }
    // Phase 8 (§7/§9-11): backs the invoice detail screen's Genel/Ürünler/Tutarlar tabs - read-only
    // projections of the exact rows Post()/SaveDraft() already wrote, never a second calculation.
    public SalesDocumentHeader? GetHeader(string id)
    {
        var t = database.Query("""
            SELECT s.id,s.company_id,s.branch_id,s.warehouse_id,COALESCE(br.name,s.branch_id) AS BranchName,COALESCE(w.name,s.warehouse_id) AS WarehouseName,
                   s.account_id,a.code,a.name,s.document_no,s.document_date,s.status,s.description,s.subtotal,s.discount_total,s.tax_total,s.grand_total
            FROM sales_documents s JOIN accounts a ON a.id=s.account_id
            LEFT JOIN branches br ON br.id=s.branch_id LEFT JOIN warehouses w ON w.id=s.warehouse_id
            WHERE s.id=$id
            """, ("$id", id));
        if (t.Rows.Count == 0) return null;
        var r = t.Rows[0];
        return new(r["id"].ToString()!, r["company_id"].ToString()!, r["branch_id"].ToString()!, r["warehouse_id"].ToString()!,
            r["BranchName"].ToString()!, r["WarehouseName"].ToString()!, r["account_id"].ToString()!, r["code"].ToString()!, r["name"].ToString()!,
            r["document_no"] as string, DateTime.Parse(r["document_date"].ToString()!), r["status"].ToString()!, r["description"].ToString()!,
            new SalesTotals(Convert.ToDecimal(r["subtotal"]), Convert.ToDecimal(r["discount_total"]), Convert.ToDecimal(r["tax_total"]), Convert.ToDecimal(r["grand_total"])));
    }

    public IReadOnlyList<SalesDocumentLineRow> GetLines(string documentId) => database.Query("""
        SELECT l.id,l.product_id,p.code,p.name,l.variant_id,u.code AS UnitCode,l.quantity,l.unit_price,l.discount_rate,l.discount_amount,l.vat_rate,l.net_amount,l.vat_amount,l.line_total
        FROM sales_document_lines l JOIN products p ON p.id=l.product_id JOIN units u ON u.id=l.unit_id
        WHERE l.sales_document_id=$id ORDER BY l.line_no
        """, ("$id", documentId)).Rows.Cast<DataRow>().Select(r => new SalesDocumentLineRow(
            r["id"].ToString()!, r["product_id"].ToString()!, r["code"].ToString()!, r["name"].ToString()!, r["variant_id"] as string,
            r["UnitCode"].ToString()!, Convert.ToDecimal(r["quantity"]), Convert.ToDecimal(r["unit_price"]), Convert.ToDecimal(r["discount_rate"]),
            Convert.ToDecimal(r["discount_amount"]), Convert.ToDecimal(r["vat_rate"]), Convert.ToDecimal(r["net_amount"]), Convert.ToDecimal(r["vat_amount"]),
            Convert.ToDecimal(r["line_total"]))).ToList();

    public decimal GetAccountBalance(string accountId) =>
        Convert.ToDecimal(database.Query("SELECT COALESCE(balance,0) FROM account_balances WHERE account_id=$id", ("$id", accountId)).Rows is { Count: > 0 } rows ? rows[0][0] : 0m);

    public DataTable GetAccountTransactions(string documentId) => database.Query(
        "SELECT transaction_at AS Tarih,transaction_type AS Tur,debit AS Borc,credit AS Alacak,description AS Aciklama FROM account_transactions WHERE document_type='SalesInvoice' AND document_id=$id ORDER BY transaction_at",
        ("$id", documentId));

    public DataTable GetInventoryTransactions(string documentId) => database.Query("""
        SELECT t.transaction_at AS Tarih,p.code || ' - ' || p.name AS Urun,t.transaction_type AS Tur,t.quantity AS Miktar,COALESCE(w.name,t.warehouse_id) AS Depo
        FROM inventory_transactions t JOIN products p ON p.id=t.product_id LEFT JOIN warehouses w ON w.id=t.warehouse_id
        WHERE t.document_type='SalesInvoice' AND t.document_id=$id ORDER BY t.transaction_at
        """, ("$id", documentId));

    public DataTable GetAuditHistory(string documentId) => database.Query(
        "SELECT created_at AS Tarih,action AS Islem,new_values AS Detay FROM audit_logs WHERE entity_type='SalesDocument' AND entity_id=$id ORDER BY created_at",
        ("$id", documentId));

    private static SalesTotals Calculate(IReadOnlyList<SalesLineEdit> lines){var x=lines.Select(CalculateLine).ToList();return new(x.Sum(a=>a.Gross),x.Sum(a=>a.Discount),x.Sum(a=>a.Vat),x.Sum(a=>a.Total));}
    private static (decimal Gross,decimal Discount,decimal Net,decimal Vat,decimal Total) CalculateLine(SalesLineEdit l){if(l.Quantity<=0||l.UnitPrice<0)throw new ArgumentException("Geçerli miktar ve fiyat girin.");var g=l.Quantity*l.UnitPrice;var d=g*l.DiscountRate/100;var n=g-d;var v=n*l.VatRate/100;return(g,d,n,v,n+v);}
    private sealed record Doc(string Id,string CompanyId,string BranchId,string WarehouseId,string AccountId,string Status,decimal GrandTotal,DateTime DocumentDate,string Description);
    private static Doc ReadDoc(SqliteConnection c,SqliteTransaction tx,string id){using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="SELECT id,company_id,branch_id,warehouse_id,account_id,status,grand_total,document_date,description FROM sales_documents WHERE id=$id";Add(q,"$id",id);using var r=q.ExecuteReader();if(!r.Read())throw new KeyNotFoundException("Fatura bulunamadı.");return new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetDecimal(6),DateTime.Parse(r.GetString(7)),r.GetString(8));}
    private static void InsertIssue(SqliteConnection c,SqliteTransaction tx,Doc d,string line,decimal q){using var x=c.CreateCommand();x.Transaction=tx;x.CommandText="INSERT INTO inventory_transactions(id,company_id,branch_id,warehouse_id,product_id,variant_id,transaction_type,quantity,document_type,document_id,document_line_id,reference_no,description,transaction_at,created_at) SELECT $id,$c,$b,$w,l.product_id,l.variant_id,'SaleIssue',$q,'SalesInvoice',$doc,$line,s.document_no,s.description,$at,$at FROM sales_document_lines l JOIN sales_documents s ON s.id=l.sales_document_id WHERE l.id=$line";Add(x,"$id",Guid.NewGuid().ToString());Add(x,"$c",d.CompanyId);Add(x,"$b",d.BranchId);Add(x,"$w",d.WarehouseId);Add(x,"$q",q);Add(x,"$doc",d.Id);Add(x,"$line",line);Add(x,"$at",DateTime.UtcNow.ToString("O"));x.ExecuteNonQuery();}
    private static void ApplyBalance(SqliteConnection c,SqliteTransaction tx,Doc d,string? v,string p,decimal delta){using var x=c.CreateCommand();x.Transaction=tx;x.CommandText="UPDATE inventory_balances SET quantity_on_hand=quantity_on_hand+$d,quantity_available=quantity_available+$d,updated_at=$now WHERE warehouse_id=$w AND product_id=$p AND (variant_id=$v OR (variant_id IS NULL AND $v IS NULL))";Add(x,"$d",delta);Add(x,"$now",DateTime.UtcNow.ToString("O"));Add(x,"$w",d.WarehouseId);Add(x,"$p",p);Add(x,"$v",(object?)v??DBNull.Value);x.ExecuteNonQuery();}
    private static string AllocateNumber(SqliteConnection c,SqliteTransaction tx,string company,int year){using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="INSERT INTO number_sequences(company_id,sequence_type,year,prefix,last_number) VALUES($c,'SalesInvoice',$y,'SF',$n) ON CONFLICT(company_id,sequence_type,year) DO UPDATE SET last_number=last_number+1 RETURNING prefix,last_number";Add(q,"$c",company);Add(q,"$y",year);Add(q,"$n",1);using var r=q.ExecuteReader();r.Read();return $"{r.GetString(0)}-{year}-{r.GetInt32(1):D6}";}
    private SqliteConnection Open()=>database.OpenConnection(); private static void Add(SqliteCommand c,string n,object v)=>c.Parameters.AddWithValue(n,v);
}
