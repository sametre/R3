using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record AccountEdit(string Id, string CompanyId, string Code, string Name, string AccountType = "Customer", string TaxOffice = "", string TaxNumber = "", string Phone = "", string MobilePhone = "", string Email = "", decimal CreditLimit = 0, decimal RiskLimit = 0, bool IsActive = true, string IdentityNumber = "");
public sealed class LocalAccountService(StoreDatabase database)
{
    public DataTable Search(string companyId, string? search = null, string? accountType = null, bool activeOnly = false)
    {
        var q = $"%{search?.Trim() ?? ""}%"; return database.Query("SELECT a.id AS Id,a.code AS Kod,a.name AS Cari,a.account_type AS Tip,a.tax_office AS VergiDairesi,a.tax_number AS VergiNo,a.phone AS Telefon,a.mobile_phone AS CepTelefonu,a.email AS Eposta,COALESCE(b.debit,0) AS Borc,COALESCE(b.credit,0) AS Alacak,COALESCE(b.balance,0) AS Bakiye,a.is_active AS Aktif,a.credit_limit AS KrediLimiti,a.risk_limit AS RiskLimiti FROM accounts a LEFT JOIN account_balances b ON b.account_id=a.id AND b.company_id=a.company_id WHERE a.company_id=$c AND ($q='' OR a.code LIKE $q OR a.name LIKE $q OR a.tax_number LIKE $q OR a.phone LIKE $q OR a.mobile_phone LIKE $q) AND ($t='' OR a.account_type=$t) AND ($active=0 OR a.is_active=1) ORDER BY a.code", ("$c", companyId), ("$q", q), ("$t", accountType ?? ""), ("$active", activeOnly ? 1 : 0));
    }
    public void Save(AccountEdit edit)
    {
        if (string.IsNullOrWhiteSpace(edit.CompanyId) || string.IsNullOrWhiteSpace(edit.Code) || string.IsNullOrWhiteSpace(edit.Name)) throw new ArgumentException("Firma, cari kodu ve cari adı zorunludur.");
        if (edit.AccountType is not ("Customer" or "Supplier" or "CustomerAndSupplier" or "Other")) throw new ArgumentException("Geçersiz cari tipi."); var id = string.IsNullOrWhiteSpace(edit.Id) ? Guid.NewGuid().ToString() : edit.Id; var now = DateTime.UtcNow.ToString("O"); using var c = new SqliteConnection($"Data Source={database.Path};Foreign Keys=True"); c.Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "INSERT INTO accounts(id,company_id,code,name,account_type,tax_office,tax_number,phone,mobile_phone,email,credit_limit,risk_limit,is_active,created_at,updated_at) VALUES($id,$c,$code,$name,$type,$office,$tax,$phone,$mobile,$email,$credit,$risk,$active,$now,$now) ON CONFLICT(id) DO UPDATE SET code=$code,name=$name,account_type=$type,tax_office=$office,tax_number=$tax,phone=$phone,mobile_phone=$mobile,email=$email,credit_limit=$credit,risk_limit=$risk,is_active=$active,updated_at=$now"; Add(cmd,"$id",id); Add(cmd,"$c",edit.CompanyId); Add(cmd,"$code",edit.Code.Trim().ToUpperInvariant()); Add(cmd,"$name",edit.Name.Trim()); Add(cmd,"$type",edit.AccountType); Add(cmd,"$office",edit.TaxOffice); Add(cmd,"$tax",edit.TaxNumber); Add(cmd,"$phone",edit.Phone); Add(cmd,"$mobile",edit.MobilePhone); Add(cmd,"$email",edit.Email); Add(cmd,"$credit",edit.CreditLimit); Add(cmd,"$risk",edit.RiskLimit); Add(cmd,"$active",edit.IsActive?1:0); Add(cmd,"$now",now); cmd.ExecuteNonQuery(); using var audit=c.CreateCommand(); audit.Transaction=tx; audit.CommandText="INSERT INTO audit_logs(id,company_id,entity_type,entity_id,action,new_values,created_at) VALUES($id,$c,'Account',$entity,$action,$new,$now)"; Add(audit,"$id",Guid.NewGuid().ToString()); Add(audit,"$c",edit.CompanyId); Add(audit,"$entity",id); Add(audit,"$action",string.IsNullOrWhiteSpace(edit.Id)?"AccountCreated":"AccountUpdated"); Add(audit,"$new",edit.Code); Add(audit,"$now",now); audit.ExecuteNonQuery(); tx.Commit();
    }
    private static void Add(SqliteCommand c,string n,object v)=>c.Parameters.AddWithValue(n,v);
}
