using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using R3.Infrastructure;

public sealed class ShipmentCanonicalImporter
{
    private readonly string _sourceName; private readonly StoreDatabase _target; private readonly string _company; private readonly SqlConnection _source;
    public ShipmentCanonicalImporter(string sourceName, StoreDatabase target, string company, SqlConnection source) { _sourceName=sourceName; _target=target; _company=company; _source=source; }

    public async Task<(int Orders,int Lines)> ImportAsync()
    {
        using var c=_target.OpenConnection(); using var tx=c.BeginTransaction();
        using(var clear=c.CreateCommand()){clear.Transaction=tx;clear.CommandText="DELETE FROM shipment_order_lines WHERE shipment_order_id IN (SELECT id FROM shipment_orders WHERE legacy_source=$source); DELETE FROM shipment_orders WHERE legacy_source=$source;";Add(clear,"$source",_sourceName);clear.ExecuteNonQuery();}
        var branch=Scalar(c,tx,"SELECT id FROM branches WHERE company_id=$company ORDER BY code LIMIT 1",_company); var warehouse=Scalar(c,tx,"SELECT id FROM warehouses WHERE company_id=$company ORDER BY code LIMIT 1",_company);
        var accounts=LegacyMap(c,tx,"accounts"); var products=LegacyMap(c,tx,"products"); var units=UnitMap(c,tx); var orderLines=await LoadLinesAsync(); var orders=0;var lines=0;
        await using var cmd=new SqlCommand("SELECT SMREF,SMMNO,SMTAR,SMCARREF,SMNOT,SMGC FROM dbo.SIPARIS",_source); await using var r=await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync())
        {
            var legacy=Long(r,0); if(!accounts.TryGetValue(Long(r,3),out var account))continue; var id=Stable("shipment-order",legacy.ToString()); var date=Date(r,2)??DateTime.UnixEpoch.ToString("O"); var number=Text(r,1).Trim(); if(string.IsNullOrWhiteSpace(number))number=$"ASB-SIP-{legacy}";
            using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="INSERT OR IGNORE INTO shipment_orders(id,company_id,branch_id,warehouse_id,account_id,shipment_no,order_date,planned_shipment_date,status,description,legacy_source,legacy_id,created_at,updated_at) VALUES($id,$company,$branch,$warehouse,$account,$no,$date,$date,'Pending',$description,$source,$legacy,$now,$now)";Add(q,"$id",id);Add(q,"$company",_company);Add(q,"$branch",branch);Add(q,"$warehouse",warehouse);Add(q,"$account",account);Add(q,"$no",number);Add(q,"$date",date);Add(q,"$description",Text(r,4));Add(q,"$source",_sourceName);Add(q,"$legacy",legacy);Add(q,"$now",DateTime.UtcNow.ToString("O"));q.ExecuteNonQuery();orders++;
            if(!orderLines.TryGetValue(legacy,out var list))continue;var lineNo=0;foreach(var d in list)if(products.TryGetValue(d.Product,out var product)&&d.Quantity>0){using var x=c.CreateCommand();x.Transaction=tx;x.CommandText="INSERT OR IGNORE INTO shipment_order_lines(id,shipment_order_id,product_id,planned_quantity,shipped_quantity,unit_code,stock_status,workflow_status,legacy_id) VALUES($id,$order,$product,$planned,$shipped,$unit,'Unknown','Pending',$legacy)";Add(x,"$id",Stable("shipment-line",d.Legacy.ToString()));Add(x,"$order",id);Add(x,"$product",product);Add(x,"$planned",d.Quantity);Add(x,"$shipped",d.Shipped);Add(x,"$unit",d.Unit);Add(x,"$legacy",d.Legacy);x.ExecuteNonQuery();lines++;lineNo++;}
        }
        tx.Commit();return(orders,lines);
    }
    private async Task<Dictionary<long,List<Detail>>> LoadLinesAsync(){var d=new Dictionary<long,List<Detail>>();await using var cmd=new SqlCommand("SELECT SDREF,SDSMREF,SDSTKREF,SDMIK,SDIRSMIK,SDBRM FROM dbo.SIPARISDTY",_source);await using var r=await cmd.ExecuteReaderAsync();while(await r.ReadAsync()){var order=Long(r,1);if(!d.TryGetValue(order,out var list))d[order]=list=[];list.Add(new Detail(Long(r,0),Long(r,2),Decimal(r,3),Decimal(r,4),Text(r,5)));}return d;}
    private Dictionary<long,string> LegacyMap(SqliteConnection c,SqliteTransaction tx,string table){var d=new Dictionary<long,string>();using var q=c.CreateCommand();q.Transaction=tx;q.CommandText=$"SELECT legacy_id,id FROM {table} WHERE company_id=$company AND legacy_source=$source AND legacy_id IS NOT NULL";Add(q,"$company",_company);Add(q,"$source",_sourceName);using var r=q.ExecuteReader();while(r.Read())d[r.GetInt64(0)]=r.GetString(1);return d;}
    private Dictionary<string,string> UnitMap(SqliteConnection c,SqliteTransaction tx){var d=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="SELECT code,id FROM units WHERE company_id=$company";Add(q,"$company",_company);using var r=q.ExecuteReader();while(r.Read())d[r.GetString(0)]=r.GetString(1);return d;}
    private static string Scalar(SqliteConnection c,SqliteTransaction tx,string sql,string company){using var q=c.CreateCommand();q.Transaction=tx;q.CommandText=sql;Add(q,"$company",company);return q.ExecuteScalar()?.ToString()??throw new InvalidOperationException("R3 şube/depo bulunamadı.");}
    private static string Stable(string k,string v)=>GuidUtility.Create(GuidUtility.Namespace,"R3:"+k+":"+v).ToString();private static string Text(SqlDataReader r,int i)=>r.IsDBNull(i)?"":Convert.ToString(r.GetValue(i))??"";private static long Long(SqlDataReader r,int i)=>r.IsDBNull(i)?0:Convert.ToInt64(r.GetValue(i));private static decimal Decimal(SqlDataReader r,int i)=>r.IsDBNull(i)?0:Convert.ToDecimal(r.GetValue(i));private static string? Date(SqlDataReader r,int i)=>r.IsDBNull(i)?null:Convert.ToDateTime(r.GetValue(i)).ToString("O");private static void Add(SqliteCommand q,string n,object v)=>q.Parameters.AddWithValue(n,v);
    private sealed record Detail(long Legacy,long Product,decimal Quantity,decimal Shipped,string Unit);
}
