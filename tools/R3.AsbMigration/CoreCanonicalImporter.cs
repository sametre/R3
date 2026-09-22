using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using R3.Infrastructure;

public sealed class CoreCanonicalImporter
{
    private readonly string _sourceName;
    private readonly StoreDatabase _target;
    private readonly string _companyId;
    private readonly SqlConnection _source;
    public CoreCanonicalImporter(string sourceName, StoreDatabase target, string companyId, SqlConnection source) { _sourceName=sourceName; _target=target; _companyId=companyId; _source=source; }

    public async Task<(int Accounts, int Products, int Units, int Barcodes)> ImportAsync()
    {
        var units = await ImportUnitsAsync();
        var accounts = await ImportAccountsAsync();
        var products = await ImportProductsAsync(units);
        var barcodes = await ImportBarcodesAsync(products);
        return (accounts, products.Count, units.Count, barcodes);
    }

    private async Task<Dictionary<string,string>> ImportUnitsAsync()
    {
        var map = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = new SqlCommand("SELECT DISTINCT LTRIM(RTRIM(SBRBRM)) FROM dbo.STOKBIRIM WHERE ISNULL(SBRBRM,'')<>''", _source);
        await using var reader = await cmd.ExecuteReaderAsync();
        using var c = _target.OpenConnection(); using var tx = c.BeginTransaction();
        while (await reader.ReadAsync())
        {
            var code = reader.GetString(0).Trim().ToUpperInvariant(); var id = StableId("unit", code); map[code]=id;
            using var insert = c.CreateCommand(); insert.Transaction=tx; insert.CommandText="INSERT OR IGNORE INTO units(id,company_id,code,name,decimal_places,is_active) VALUES($id,$company,$code,$name,2,1)";
            Add(insert,"$id",id); Add(insert,"$company",_companyId); Add(insert,"$code",code); Add(insert,"$name",code); insert.ExecuteNonQuery();
        }
        tx.Commit(); return map;
    }

    private async Task<int> ImportAccountsAsync()
    {
        const string sql = """
            SELECT c.CRREF,c.CRKOD,c.CRADI,c.CRMUS,c.CRTED,c.CRAKTIF,c.CRDVZ,
                   d.CRDUNV,d.CRDVERD,d.CRDVERN,d.CRDTEL1,d.CRDGSM1,d.CRDEMAIL,d.CRDADR1,d.CRDADR2,d.CRDIL,d.CRDPKOD,
                   m.MUSVADEGUN,m.MUSKREDI,m.MUSEKKREDI,m.MUSKREDIBLOKE,m.MUSKREDIKONTROL,m.MUSSCORINGPUANI,m.MUSKARTAKTIF,m.MUSSTKK,m.MUS_KVKK_ALINDI,m.MUSSTARTDATE,
                   t.TEDVADEGUN,t.TED_SAHIS
            FROM dbo.CARIKART c LEFT JOIN dbo.CARIKARTDTY d ON d.CRDREF=c.CRREF LEFT JOIN dbo.MUSTERI m ON m.MUSREF=c.CRREF LEFT JOIN dbo.TEDARIKCI t ON t.TEDREF=c.CRREF
            """;
        await using var cmd = new SqlCommand(sql,_source); await using var reader=await cmd.ExecuteReaderAsync();
        using var c=_target.OpenConnection(); using var tx=c.BeginTransaction(); var count=0;
        while(await reader.ReadAsync())
        {
            var legacyId=reader.GetInt64(0); var code=Text(reader,1).Trim(); if(string.IsNullOrWhiteSpace(code)) code=$"LEG-{legacyId}"; code=code.ToUpperInvariant(); var name=Text(reader,2).Trim();
            var type=Bool(reader,3)&&Bool(reader,4)?"CustomerAndSupplier":Bool(reader,3)?"Customer":Bool(reader,4)?"Supplier":"Other"; var id=FindId(c,tx,"accounts",_sourceName,legacyId) ?? StableId("account",legacyId.ToString());
            using var up=c.CreateCommand(); up.Transaction=tx; up.CommandText="""
                INSERT INTO accounts(id,company_id,code,name,account_type,tax_office,tax_number,phone,mobile_phone,email,is_active,legacy_source,legacy_id,created_at,updated_at)
                VALUES($id,$company,$code,$name,$type,$office,$tax,$phone,$mobile,$email,$active,$source,$legacy,$now,$now)
                ON CONFLICT(id) DO UPDATE SET code=$code,name=$name,account_type=$type,tax_office=$office,tax_number=$tax,phone=$phone,mobile_phone=$mobile,email=$email,is_active=$active,updated_at=$now
                """;
            Add(up,"$id",id);Add(up,"$company",_companyId);Add(up,"$code",code);Add(up,"$name",name);Add(up,"$type",type);Add(up,"$office",Text(reader,8));Add(up,"$tax",Text(reader,9));Add(up,"$phone",Text(reader,10));Add(up,"$mobile",Text(reader,11));Add(up,"$email",Text(reader,12));Add(up,"$active",Bool(reader,5)?1:0);Add(up,"$source",_sourceName);Add(up,"$legacy",legacyId);Add(up,"$now",DateTime.UtcNow.ToString("O"));up.ExecuteNonQuery();
            using var profile=c.CreateCommand();profile.Transaction=tx;profile.CommandText="INSERT INTO customer_profiles(account_id,payment_term_days,extra_credit_limit,blocked_credit,scoring_score,started_at) VALUES($id,$term,$extra,$blocked,$score,$started) ON CONFLICT(account_id) DO UPDATE SET payment_term_days=$term,extra_credit_limit=$extra,blocked_credit=$blocked,scoring_score=$score,started_at=$started"; Add(profile,"$id",id);Add(profile,"$term",Int(reader,17));Add(profile,"$extra",Decimal(reader,19));Add(profile,"$blocked",Decimal(reader,20));Add(profile,"$score",Int(reader,22));AddNullable(profile,"$started",Date(reader,26)); if(type is "Customer" or "CustomerAndSupplier") profile.ExecuteNonQuery();
            using var sup=c.CreateCommand();sup.Transaction=tx;sup.CommandText="INSERT INTO supplier_profiles(account_id,payment_term_days,is_active_supplier) VALUES($id,$term,$active) ON CONFLICT(account_id) DO UPDATE SET payment_term_days=$term,is_active_supplier=$active";Add(sup,"$id",id);Add(sup,"$term",Int(reader,27));Add(sup,"$active",Bool(reader,5)?1:0);if(type is "Supplier" or "CustomerAndSupplier")sup.ExecuteNonQuery();
            AddAddress(c,tx,id,legacyId,Text(reader,7),Text(reader,13)+" "+Text(reader,14),Text(reader,15),Text(reader,16),Text(reader,10)); AddContacts(c,tx,id,legacyId); AddBanks(c,tx,id,legacyId); AddNotes(c,tx,id,legacyId);
            count++;
        }
        tx.Commit(); return count;
    }

    private async Task<Dictionary<long,string>> ImportProductsAsync(Dictionary<string,string> units)
    {
        const string sql="SELECT STKREF,STKKOD,STKADI,STKTIP,STKSTS,STKKDVORN0,STKKDVORN1,STKMINM,STKMAXM,STKMINSIPMIK,STKSIPKAT,STKHKSATILAMAZ,STKFORTEKLIF,STKBDLSZGRS,STKTAKIM,STKTANIMOK,STKANAKOD,STKRENK,STKBEDENTIPI,STKBEDEN,STKOTVBRMFYT,STKKISAADI FROM dbo.STOKKARTI";
        await using var cmd=new SqlCommand(sql,_source);await using var reader=await cmd.ExecuteReaderAsync();using var c=_target.OpenConnection();using var tx=c.BeginTransaction();var map=new Dictionary<long,string>();
        var defaultUnit=units.Values.FirstOrDefault() ?? StableId("unit","ADET");
        while(await reader.ReadAsync()) { var legacy=reader.GetInt64(0);var code=Text(reader,1).Trim().ToUpperInvariant();if(string.IsNullOrWhiteSpace(code))code=$"LEG-{legacy}";var id=FindId(c,tx,"products",_sourceName,legacy)??StableId("product",legacy.ToString());map[legacy]=id;using var up=c.CreateCommand();up.Transaction=tx;up.CommandText="""INSERT INTO products(id,company_id,code,name,base_unit_id,product_type,vat_rate,purchase_vat_rate,excise_unit_price,minimum_stock,maximum_stock,minimum_order_quantity,order_multiple,is_sellable,is_active,parent_code,legacy_source,legacy_id,created_at,updated_at) VALUES($id,$company,$code,$name,$unit,$type,$vat,$pvat,$excise,$min,$max,$minorder,$multiple,$sell,$active,$parent,$source,$legacy,$now,$now) ON CONFLICT(id) DO UPDATE SET code=$code,name=$name,base_unit_id=$unit,product_type=$type,vat_rate=$vat,purchase_vat_rate=$pvat,excise_unit_price=$excise,minimum_stock=$min,maximum_stock=$max,minimum_order_quantity=$minorder,order_multiple=$multiple,is_sellable=$sell,is_active=$active,parent_code=$parent,updated_at=$now""";Add(up,"$id",id);Add(up,"$company",_companyId);Add(up,"$code",code);Add(up,"$name",Text(reader,2).Trim());Add(up,"$unit",defaultUnit);Add(up,"$type",Text(reader,3));Add(up,"$vat",Decimal(reader,5));Add(up,"$pvat",Decimal(reader,6));Add(up,"$excise",Decimal(reader,20));Add(up,"$min",Decimal(reader,7));Add(up,"$max",Decimal(reader,8));Add(up,"$minorder",Decimal(reader,9));Add(up,"$multiple",Decimal(reader,10));Add(up,"$sell",Bool(reader,11)?0:1);Add(up,"$active",Bool(reader,4)?1:0);Add(up,"$parent",Text(reader,16));Add(up,"$source",_sourceName);Add(up,"$legacy",legacy);Add(up,"$now",DateTime.UtcNow.ToString("O"));up.ExecuteNonQuery();}
        tx.Commit();return map;
    }

    private async Task<int> ImportBarcodesAsync(Dictionary<long,string> products){const string sql="SELECT SBRKBARKOD,SBRKSTKREF FROM dbo.STOKBARKOD WHERE ISNULL(SBRKBARKOD,'')<>''";await using var cmd=new SqlCommand(sql,_source);await using var reader=await cmd.ExecuteReaderAsync();using var c=_target.OpenConnection();using var tx=c.BeginTransaction();var n=0;while(await reader.ReadAsync()){if(!products.TryGetValue(reader.GetInt64(1),out var product))continue;using var up=c.CreateCommand();up.Transaction=tx;up.CommandText="INSERT OR IGNORE INTO product_barcodes(id,product_id,barcode,quantity,is_primary,is_active) VALUES($id,$product,$barcode,1,0,1)";Add(up,"$id",StableId("barcode",Text(reader,0)));Add(up,"$product",product);Add(up,"$barcode",Text(reader,0).Trim());try{up.ExecuteNonQuery();n++;}catch(SqliteException){}}tx.Commit();return n;}

    private void AddAddress(SqliteConnection c,SqliteTransaction tx,string account,long legacy,string title,string line,string city,string postal,string phone){if(string.IsNullOrWhiteSpace(line.Trim())&&string.IsNullOrWhiteSpace(city))return;using var x=c.CreateCommand();x.Transaction=tx;x.CommandText="INSERT OR REPLACE INTO account_addresses(id,account_id,address_type,title,country,city,address_line,postal_code,phone,is_default,is_active) VALUES($id,$account,'HeadOffice',$title,'Türkiye',$city,$line,$postal,$phone,1,1)";Add(x,"$id",StableId("address",legacy.ToString()));Add(x,"$account",account);Add(x,"$title",string.IsNullOrWhiteSpace(title)?"Merkez":title);Add(x,"$city",city);Add(x,"$line",line.Trim());Add(x,"$postal",postal);Add(x,"$phone",phone);x.ExecuteNonQuery();}
    private void AddContacts(SqliteConnection c,SqliteTransaction tx,string account,long legacy){using var cmd=new SqlCommand("SELECT TOP 1 CYTADI,CYTEMAIL,CYTTEL,CYTGSM,CYTGOREVI,CYTACK FROM dbo.CARIYETKILI WHERE CYTCRREF=@id ORDER BY CYTSIRA",_source);cmd.Parameters.AddWithValue("@id",legacy);using var r=cmd.ExecuteReader();if(!r.Read())return;var parts=Text(r,0).Split(' ',2,StringSplitOptions.RemoveEmptyEntries);using var x=c.CreateCommand();x.Transaction=tx;x.CommandText="INSERT OR REPLACE INTO account_contacts(id,account_id,first_name,last_name,title,phone,mobile_phone,email,is_primary,is_active,notes,created_at,updated_at) VALUES($id,$account,$first,$last,$title,$phone,$mobile,$email,1,1,$notes,$now,$now)";Add(x,"$id",StableId("contact",legacy.ToString()));Add(x,"$account",account);Add(x,"$first",parts.FirstOrDefault()??"");Add(x,"$last",parts.Skip(1).FirstOrDefault()??"");Add(x,"$title",Text(r,4));Add(x,"$phone",Text(r,2));Add(x,"$mobile",Text(r,3));Add(x,"$email",Text(r,1));Add(x,"$notes",Text(r,5));Add(x,"$now",DateTime.UtcNow.ToString("O"));x.ExecuteNonQuery();}
    private void AddBanks(SqliteConnection c,SqliteTransaction tx,string account,long legacy){using var cmd=new SqlCommand("SELECT TOP 1 CRBNBANKAADI,CRBNSUBEADI,CRBNSUBENO,CRBNHESAPNO,CRBNIBAN FROM dbo.CARIBANKA WHERE CRBNCRREF=@id ORDER BY CRBNSIRANO",_source);cmd.Parameters.AddWithValue("@id",legacy);using var r=cmd.ExecuteReader();if(!r.Read())return;using var x=c.CreateCommand();x.Transaction=tx;x.CommandText="INSERT OR REPLACE INTO account_banks(id,account_id,bank_name,branch_name,branch_code,account_number,iban,currency_code,is_default,is_active,created_at,updated_at) VALUES($id,$account,$bank,$branch,$code,$number,$iban,'TRY',1,1,$now,$now)";Add(x,"$id",StableId("bank",legacy.ToString()));Add(x,"$account",account);Add(x,"$bank",Text(r,0));Add(x,"$branch",Text(r,1));Add(x,"$code",Text(r,2));Add(x,"$number",Text(r,3));Add(x,"$iban",Text(r,4));Add(x,"$now",DateTime.UtcNow.ToString("O"));x.ExecuteNonQuery();}
    private void AddNotes(SqliteConnection c,SqliteTransaction tx,string account,long legacy){using var cmd=new SqlCommand("SELECT TOP 1 CNOTACK,CNOTUSR,CNOTTAR,CNOTTIP FROM dbo.CARINOT WHERE CNOTCRREF=@id ORDER BY CNOTTAR DESC",_source);cmd.Parameters.AddWithValue("@id",legacy);using var r=cmd.ExecuteReader();if(!r.Read())return;using var x=c.CreateCommand();x.Transaction=tx;x.CommandText="INSERT OR REPLACE INTO account_notes(id,account_id,note_type,title,content,is_pinned,created_by,created_at,updated_at) VALUES($id,$account,$type,'Legacy ASB Notu',$content,0,$user,$now,$now)";Add(x,"$id",StableId("note",legacy.ToString()));Add(x,"$account",account);Add(x,"$type",Text(r,3));Add(x,"$content",Text(r,0));Add(x,"$user",Text(r,1));Add(x,"$now",DateTime.UtcNow.ToString("O"));x.ExecuteNonQuery();}

    private string? FindId(SqliteConnection c,SqliteTransaction tx,string table,string source,long legacy){using var q=c.CreateCommand();q.Transaction=tx;q.CommandText=$"SELECT id FROM {table} WHERE legacy_source=$source AND legacy_id=$legacy LIMIT 1";Add(q,"$source",source);Add(q,"$legacy",legacy);return q.ExecuteScalar()?.ToString();}
    private static string StableId(string kind,string value){var bytes=MD5.HashData(Encoding.UTF8.GetBytes("R3:"+kind+":"+value));return new Guid(bytes).ToString();}
    private static string Text(SqlDataReader r,int i)=>r.IsDBNull(i)?"":Convert.ToString(r.GetValue(i))??"";
    private static bool Bool(SqlDataReader r,int i)=>!r.IsDBNull(i)&&Convert.ToBoolean(r.GetValue(i));
    private static int Int(SqlDataReader r,int i)=>r.IsDBNull(i)?0:Convert.ToInt32(r.GetValue(i));
    private static decimal Decimal(SqlDataReader r,int i)=>r.IsDBNull(i)?0m:Convert.ToDecimal(r.GetValue(i));
    private static string? Date(SqlDataReader r,int i)=>r.IsDBNull(i)?null:Convert.ToDateTime(r.GetValue(i)).ToString("O");
    private static void Add(SqliteCommand c,string n,object v)=>c.Parameters.AddWithValue(n,v);
    private static void AddNullable(SqliteCommand c,string n,object? v)=>c.Parameters.AddWithValue(n,v??DBNull.Value);
}
