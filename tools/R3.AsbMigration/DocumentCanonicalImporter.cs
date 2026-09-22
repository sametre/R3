using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using R3.Infrastructure;

public sealed class DocumentCanonicalImporter
{
    private readonly string _sourceName;
    private readonly StoreDatabase _target;
    private readonly string _companyId;
    private readonly SqlConnection _source;

    public DocumentCanonicalImporter(string sourceName, StoreDatabase target, string companyId, SqlConnection source)
    { _sourceName = sourceName; _target = target; _companyId = companyId; _source = source; }

    public async Task<(int Sales, int Purchases, int InvoiceLines, int Movements)> ImportAsync()
    {
        var context = LoadContext();
        var details = await LoadInvoiceDetailsAsync();
        var sales = 0; var purchases = 0; var lines = 0;
        using (var c = _target.OpenConnection())
        using (var tx = c.BeginTransaction())
        {
            await using var h = new SqlCommand("SELECT FATREF,FATGC,FATTIP,FATTAR,FATCARREF,FATDVZ,FATKUR,FATTUT,FATISK,FATKDV,FATNET,FATACK,FATKOD FROM dbo.FATURA", _source);
            await using var r = await h.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var legacy = Long(r, 0); if (!context.Accounts.TryGetValue(Long(r, 4), out var account)) continue;
                var date = Date(r, 3) ?? DateTime.UnixEpoch.ToString("O");
                var documentNo = Text(r, 12).Trim(); if (string.IsNullOrWhiteSpace(documentNo)) documentNo = $"ASB-{legacy}";
                var isSale = Int(r, 1) == 1; var id = StableId(isSale ? "sales-document" : "purchase-document", legacy.ToString());
                var currency = string.IsNullOrWhiteSpace(Text(r, 5)) ? "TRY" : Text(r, 5).Trim().ToUpperInvariant();
                var rate = Decimal(r, 6); if (rate <= 0) rate = 1;
                var subtotal = Decimal(r, 7); var discount = Decimal(r, 8); var tax = Decimal(r, 9); var total = Decimal(r, 10);
                var rows = details.TryGetValue(legacy, out var found) ? found : [];
                if (isSale)
                {
                    InsertSalesHeader(c, tx, id, context, account, documentNo, date, currency, rate, subtotal, discount, tax, total, Text(r, 11), legacy);
                    lines += InsertSalesLines(c, tx, id, rows, context);
                    sales++;
                }
                else
                {
                    InsertPurchaseHeader(c, tx, id, context, account, documentNo, date, currency, subtotal, discount, tax, total, Text(r, 11), legacy);
                    lines += InsertPurchaseLines(c, tx, id, rows, context);
                    purchases++;
                }
            }
            tx.Commit();
        }

        var movements = await ImportMovementsAsync(context);
        return (sales, purchases, lines, movements);
    }

    private async Task<int> ImportMovementsAsync(Context context)
    {
        using var c = _target.OpenConnection(); using var tx = c.BeginTransaction();
        using (var clear = c.CreateCommand()) { clear.Transaction = tx; clear.CommandText = "DELETE FROM inventory_transactions WHERE legacy_source=$source"; Add(clear, "$source", _sourceName); clear.ExecuteNonQuery(); }
        using (var clear = c.CreateCommand()) { clear.Transaction = tx; clear.CommandText = "DELETE FROM inventory_balances"; clear.ExecuteNonQuery(); }
        var count = 0;
        await using var cmd = new SqlCommand("SELECT STHREF,STHBLGREF,STHGC,STHTAR,STHSTKREF,STHDEPOREF,STHMIKO,STHMIK,STHBRM,STHACK,STHSTKYERREF,STHKARM FROM dbo.STKHAR", _source);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var legacy = Long(r, 0); if (!context.Products.TryGetValue(Long(r, 4), out var product)) continue;
            var qty = Decimal(r, 7); if (qty <= 0) qty = Decimal(r, 6); if (qty <= 0) continue;
            var unit = context.Units.TryGetValue(Text(r, 8).Trim(), out var unitId) ? unitId : context.DefaultUnit;
            var type = Int(r, 2) == 1 ? "Inbound" : "Outbound";
            var at = Date(r, 3) ?? DateTime.UnixEpoch.ToString("O");
            var description = $"ASB STKHAR • Depo:{Text(r, 5).Trim()}";
            if (!string.IsNullOrWhiteSpace(Text(r, 9))) description += $" • {Text(r, 9).Trim()}";
            using var ins = c.CreateCommand(); ins.Transaction = tx; ins.CommandText = "INSERT INTO inventory_transactions(id,company_id,branch_id,warehouse_id,product_id,transaction_type,quantity,base_quantity,unit_id,document_type,reference_no,description,transaction_at,created_at,legacy_source,legacy_id) VALUES($id,$company,$branch,$warehouse,$product,$type,$qty,$qty,$unit,'LegacyStockMovement',$ref,$description,$at,$now,$source,$legacy)";
            Add(ins, "$id", StableId("stock-movement", legacy.ToString())); Add(ins, "$company", _companyId); Add(ins, "$branch", context.Branch); Add(ins, "$warehouse", context.Warehouse); Add(ins, "$product", product); Add(ins, "$type", type); Add(ins, "$qty", qty); Add(ins, "$unit", unit); Add(ins, "$ref", Text(r, 1)); Add(ins, "$description", description); Add(ins, "$at", at); Add(ins, "$now", DateTime.UtcNow.ToString("O")); Add(ins, "$source", _sourceName); Add(ins, "$legacy", legacy); ins.ExecuteNonQuery();
            var delta = type == "Inbound" ? qty : -qty;
            using var exists = c.CreateCommand(); exists.Transaction = tx; exists.CommandText = "SELECT COUNT(*) FROM inventory_balances WHERE warehouse_id=$warehouse AND product_id=$product AND variant_id IS NULL"; Add(exists, "$warehouse", context.Warehouse); Add(exists, "$product", product); var foundBalance = Convert.ToInt32(exists.ExecuteScalar()) > 0;
            using var bal = c.CreateCommand(); bal.Transaction = tx; bal.CommandText = foundBalance ? "UPDATE inventory_balances SET quantity_on_hand=quantity_on_hand+$delta,quantity_available=quantity_available+$delta,last_transaction_at=$at,updated_at=$now WHERE warehouse_id=$warehouse AND product_id=$product AND variant_id IS NULL" : "INSERT INTO inventory_balances(company_id,branch_id,warehouse_id,product_id,quantity_on_hand,quantity_available,last_transaction_at,updated_at) VALUES($company,$branch,$warehouse,$product,$delta,$delta,$at,$now)";
            Add(bal, "$company", _companyId); Add(bal, "$branch", context.Branch); Add(bal, "$warehouse", context.Warehouse); Add(bal, "$product", product); Add(bal, "$delta", delta); Add(bal, "$at", at); Add(bal, "$now", DateTime.UtcNow.ToString("O")); bal.ExecuteNonQuery(); count++;
        }
        tx.Commit(); return count;
    }

    private Context LoadContext()
    {
        using var c = _target.OpenConnection();
        var branch = c.CreateCommand(); branch.CommandText = "SELECT id FROM branches WHERE company_id=$company ORDER BY code LIMIT 1"; Add(branch, "$company", _companyId); var branchId = branch.ExecuteScalar()?.ToString() ?? throw new InvalidOperationException("R3 hedefinde şube bulunamadı.");
        var warehouse = c.CreateCommand(); warehouse.CommandText = "SELECT id FROM warehouses WHERE company_id=$company ORDER BY code LIMIT 1"; Add(warehouse, "$company", _companyId); var warehouseId = warehouse.ExecuteScalar()?.ToString() ?? throw new InvalidOperationException("R3 hedefinde depo bulunamadı.");
        var units = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); using var u = c.CreateCommand(); u.CommandText = "SELECT code,id FROM units WHERE company_id=$company"; Add(u, "$company", _companyId); using var ur = u.ExecuteReader(); while (ur.Read()) units[ur.GetString(0)] = ur.GetString(1);
        return new Context(_companyId, _sourceName, branchId, warehouseId, units.Values.FirstOrDefault() ?? StableId("unit", "ADET"), units, LoadLegacyMap(c, "products"), LoadLegacyMap(c, "accounts"));
    }

    private async Task<Dictionary<long,List<Detail>>> LoadInvoiceDetailsAsync()
    {
        var result = new Dictionary<long,List<Detail>>(); await using var cmd = new SqlCommand("WITH D AS (SELECT FDTFATREF,FDTMIKTAR,FDTBRM,FDTBAZFYT,FDTTUT,FDTISK,FDTKDV,FDTNET,FDTACK,FDTKDVORN,ROW_NUMBER() OVER(PARTITION BY FDTFATREF ORDER BY FDTREF) AS RN FROM dbo.FATURADTY), H AS (SELECT STHBLGREF,STHSTKREF,ROW_NUMBER() OVER(PARTITION BY STHBLGREF ORDER BY STHREF) AS RN FROM dbo.STKHAR) SELECT D.FDTFATREF,H.STHSTKREF,D.FDTMIKTAR,D.FDTBRM,D.FDTBAZFYT,D.FDTTUT,D.FDTISK,D.FDTKDV,D.FDTNET,D.FDTACK,D.FDTKDVORN FROM D LEFT JOIN H ON H.STHBLGREF=D.FDTFATREF AND H.RN=D.RN", _source); await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) { var key=Long(r,0); if(!result.TryGetValue(key,out var list)) result[key]=list=[]; list.Add(new Detail(LongNullable(r,1),Decimal(r,2),Text(r,3),Decimal(r,4),Decimal(r,5),Decimal(r,6),Decimal(r,7),Decimal(r,8),Text(r,9),Decimal(r,10))); }
        return result;
    }

    private static void InsertSalesHeader(SqliteConnection c, SqliteTransaction tx, string id, Context x, string account, string no, string date, string currency, decimal rate, decimal sub, decimal disc, decimal tax, decimal total, string desc, long legacy) { using var q=c.CreateCommand(); q.Transaction=tx; q.CommandText="INSERT OR IGNORE INTO sales_documents(id,company_id,branch_id,warehouse_id,account_id,document_type,document_no,document_date,posting_date,status,currency_code,exchange_rate,subtotal,discount_total,tax_total,grand_total,description,posted_at,created_at,updated_at,legacy_source,legacy_id) VALUES($id,$company,$branch,$warehouse,$account,'Invoice',$no,$date,$date,'Posted',$currency,$rate,$sub,$disc,$tax,$total,$desc,$date,$now,$now,$source,$legacy)"; AddHeader(q,id,x,account,no,date,currency,rate,sub,disc,tax,total,desc,legacy); q.ExecuteNonQuery(); }
    private static void InsertPurchaseHeader(SqliteConnection c, SqliteTransaction tx, string id, Context x, string account, string no, string date, string currency, decimal sub, decimal disc, decimal tax, decimal total, string desc, long legacy) { using var q=c.CreateCommand(); q.Transaction=tx; q.CommandText="INSERT OR IGNORE INTO purchase_documents(id,company_id,branch_id,warehouse_id,supplier_id,document_type,document_no,document_date,status,currency_code,subtotal,discount_total,tax_total,grand_total,description,posted_at,created_at,updated_at,legacy_source,legacy_id) VALUES($id,$company,$branch,$warehouse,$account,'Invoice',$no,$date,'Posted',$currency,$sub,$disc,$tax,$total,$desc,$date,$now,$now,$source,$legacy)"; AddHeader(q,id,x,account,no,date,currency,1,sub,disc,tax,total,desc,legacy); q.ExecuteNonQuery(); }
    private static int InsertSalesLines(SqliteConnection c, SqliteTransaction tx, string doc, List<Detail> rows, Context x) { var n=0; foreach(var d in rows) if(d.Product is long p && x.Products.TryGetValue(p,out var product)) { var qty=d.Quantity>0?d.Quantity:1; var gross=d.Gross!=0?d.Gross:d.UnitPrice*qty; var net=d.Net!=0?d.Net:gross; using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="INSERT OR IGNORE INTO sales_document_lines(id,sales_document_id,line_no,product_id,unit_id,quantity,quantity_factor,base_quantity,unit_price,discount_rate,discount_amount,vat_rate,gross_amount,net_amount,vat_amount,line_total,description) VALUES($id,$doc,$line,$product,$unit,$qty,1,$qty,$price,$dr,$disc,$vr,$gross,$net,$vat,$total,$desc)";AddLine(q,StableId("sales-line",d.Product+":"+doc+":"+n),doc,n+1,product,x.Unit(d.Unit),qty,d.UnitPrice,d.Discount,d.Vat,d.VatRate,gross,net,d.Description);q.ExecuteNonQuery();n++;}return n; }
    private static int InsertPurchaseLines(SqliteConnection c, SqliteTransaction tx, string doc, List<Detail> rows, Context x) { var n=0; foreach(var d in rows) if(d.Product is long p && x.Products.TryGetValue(p,out var product)) { var qty=d.Quantity>0?d.Quantity:1; var gross=d.Gross!=0?d.Gross:d.UnitPrice*qty; var net=d.Net!=0?d.Net:gross; using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="INSERT OR IGNORE INTO purchase_document_lines(id,purchase_document_id,line_no,product_id,unit_id,quantity,unit_price,discount_rate,discount_amount,vat_rate,gross_amount,net_amount,vat_amount,line_total,description) VALUES($id,$doc,$line,$product,$unit,$qty,$price,$dr,$disc,$vr,$gross,$net,$vat,$total,$desc)";AddLine(q,StableId("purchase-line",d.Product+":"+doc+":"+n),doc,n+1,product,x.Unit(d.Unit),qty,d.UnitPrice,d.Discount,d.Vat,d.VatRate,gross,net,d.Description);q.ExecuteNonQuery();n++;}return n; }
    private static void AddHeader(SqliteCommand q,string id,Context x,string account,string no,string date,string currency,decimal rate,decimal sub,decimal disc,decimal tax,decimal total,string desc,long legacy){Add(q,"$id",id);Add(q,"$company",x.Company);Add(q,"$branch",x.Branch);Add(q,"$warehouse",x.Warehouse);Add(q,"$account",account);Add(q,"$no",no);Add(q,"$date",date);Add(q,"$currency",currency);Add(q,"$rate",rate);Add(q,"$sub",sub);Add(q,"$disc",disc);Add(q,"$tax",tax);Add(q,"$total",total);Add(q,"$desc",desc);Add(q,"$now",DateTime.UtcNow.ToString("O"));Add(q,"$source",x.Source);Add(q,"$legacy",legacy);}
    private static void AddLine(SqliteCommand q,string id,string doc,int line,string product,string unit,decimal qty,decimal price,decimal discount,decimal vatAmount,decimal vatRate,decimal gross,decimal net,string desc){Add(q,"$id",id);Add(q,"$doc",doc);Add(q,"$line",line);Add(q,"$product",product);Add(q,"$unit",unit);Add(q,"$qty",qty);Add(q,"$price",price);Add(q,"$dr",0);Add(q,"$disc",discount);Add(q,"$vr",vatRate);Add(q,"$gross",gross);Add(q,"$net",net);Add(q,"$vat",vatAmount);Add(q,"$total",net);Add(q,"$desc",desc);}
    private Dictionary<long,string> LoadLegacyMap(SqliteConnection c,string table){var d=new Dictionary<long,string>();using var q=c.CreateCommand();q.CommandText=$"SELECT legacy_id,id FROM {table} WHERE legacy_source=$source AND legacy_id IS NOT NULL";Add(q,"$source",_sourceName);using var r=q.ExecuteReader();while(r.Read())d[r.GetInt64(0)]=r.GetString(1);return d;}
    private sealed record Context(string Company,string Source,string Branch,string Warehouse,string DefaultUnit,Dictionary<string,string> Units,Dictionary<long,string> Products,Dictionary<long,string> Accounts){public string Unit(string code)=>Units.TryGetValue(code.Trim(),out var id)?id:DefaultUnit;}
    private sealed record Detail(long? Product,decimal Quantity,string Unit,decimal UnitPrice,decimal Gross,decimal Discount,decimal Vat,decimal Net,string Description,decimal VatRate);
    private static string StableId(string kind,string value)=>GuidUtility.Create(GuidUtility.Namespace,"R3:"+kind+":"+value).ToString();
    private static string Text(SqlDataReader r,int i)=>r.IsDBNull(i)?"":Convert.ToString(r.GetValue(i))??"";private static long Long(SqlDataReader r,int i)=>r.IsDBNull(i)?0:Convert.ToInt64(r.GetValue(i));private static long? LongNullable(SqlDataReader r,int i)=>r.IsDBNull(i)?null:Convert.ToInt64(r.GetValue(i));private static int Int(SqlDataReader r,int i)=>r.IsDBNull(i)?0:Convert.ToInt32(r.GetValue(i));private static decimal Decimal(SqlDataReader r,int i)=>r.IsDBNull(i)?0:Convert.ToDecimal(r.GetValue(i));private static string? Date(SqlDataReader r,int i)=>r.IsDBNull(i)?null:Convert.ToDateTime(r.GetValue(i)).ToString("O");private static void Add(SqliteCommand q,string n,object v)=>q.Parameters.AddWithValue(n,v);
}

internal static class GuidUtility { public static readonly Guid Namespace = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8"); public static Guid Create(Guid ns,string name){using var md5=System.Security.Cryptography.MD5.Create();var b=ns.ToByteArray().Concat(System.Text.Encoding.UTF8.GetBytes(name)).ToArray();return new Guid(md5.ComputeHash(b));} }
