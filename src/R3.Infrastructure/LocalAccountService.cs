using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record AccountEdit(string Id, string CompanyId, string Code, string Name, string AccountType = "Customer", string TaxOffice = "", string TaxNumber = "", string Phone = "", string MobilePhone = "", string Email = "", decimal CreditLimit = 0, decimal RiskLimit = 0, bool IsActive = true, string IdentityNumber = "");
public sealed record AccountDashboardSummary(
    int ActiveAccounts,
    int Customers,
    int Suppliers,
    decimal CustomerReceivable,
    decimal SupplierPayable,
    int CreditLimitExceeded);
public sealed class LocalAccountService(StoreDatabase database)
{
    public StoreDatabase Database => database;

    public DataTable Search(string companyId, string? search = null, string? accountType = null, bool activeOnly = false)
    {
        var q = $"%{search?.Trim() ?? ""}%"; return database.Query("SELECT a.id AS Id,a.code AS Kod,a.name AS Cari,a.account_type AS Tip,a.tax_office AS VergiDairesi,a.tax_number AS VergiNo,a.phone AS Telefon,a.mobile_phone AS CepTelefonu,a.email AS Eposta,COALESCE(b.debit,0) AS Borc,COALESCE(b.credit,0) AS Alacak,COALESCE(b.balance,0) AS Bakiye,a.is_active AS Aktif,a.credit_limit AS KrediLimiti,a.risk_limit AS RiskLimiti FROM accounts a LEFT JOIN account_balances b ON b.account_id=a.id AND b.company_id=a.company_id WHERE a.company_id=$c AND ($q='' OR a.code LIKE $q OR a.name LIKE $q OR a.tax_number LIKE $q OR a.phone LIKE $q OR a.mobile_phone LIKE $q) AND ($t='' OR a.account_type=$t OR (a.account_type='CustomerAndSupplier' AND $t IN ('Customer','Supplier'))) AND ($active=0 OR a.is_active=1) ORDER BY a.code", ("$c", companyId), ("$q", q), ("$t", accountType ?? ""), ("$active", activeOnly ? 1 : 0));
    }
    public void Save(AccountEdit edit)
    {
        if (string.IsNullOrWhiteSpace(edit.CompanyId) || string.IsNullOrWhiteSpace(edit.Code) || string.IsNullOrWhiteSpace(edit.Name)) throw new ArgumentException("Firma, cari kodu ve cari adı zorunludur.");
        if (edit.AccountType is not ("Customer" or "Supplier" or "CustomerAndSupplier" or "Other")) throw new ArgumentException("Geçersiz cari tipi."); var id = string.IsNullOrWhiteSpace(edit.Id) ? Guid.NewGuid().ToString() : edit.Id; var now = DateTime.UtcNow.ToString("O"); using var c = new SqliteConnection($"Data Source={database.Path};Foreign Keys=True"); c.Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "INSERT INTO accounts(id,company_id,code,name,account_type,tax_office,tax_number,phone,mobile_phone,email,credit_limit,risk_limit,is_active,created_at,updated_at) VALUES($id,$c,$code,$name,$type,$office,$tax,$phone,$mobile,$email,$credit,$risk,$active,$now,$now) ON CONFLICT(id) DO UPDATE SET code=$code,name=$name,account_type=$type,tax_office=$office,tax_number=$tax,phone=$phone,mobile_phone=$mobile,email=$email,credit_limit=$credit,risk_limit=$risk,is_active=$active,updated_at=$now"; Add(cmd,"$id",id); Add(cmd,"$c",edit.CompanyId); Add(cmd,"$code",edit.Code.Trim().ToUpperInvariant()); Add(cmd,"$name",edit.Name.Trim()); Add(cmd,"$type",edit.AccountType); Add(cmd,"$office",edit.TaxOffice); Add(cmd,"$tax",edit.TaxNumber); Add(cmd,"$phone",edit.Phone); Add(cmd,"$mobile",edit.MobilePhone); Add(cmd,"$email",edit.Email); Add(cmd,"$credit",edit.CreditLimit); Add(cmd,"$risk",edit.RiskLimit); Add(cmd,"$active",edit.IsActive?1:0); Add(cmd,"$now",now); cmd.ExecuteNonQuery(); using var audit=c.CreateCommand(); audit.Transaction=tx; audit.CommandText="INSERT INTO audit_logs(id,company_id,entity_type,entity_id,action,new_values,created_at) VALUES($id,$c,'Account',$entity,$action,$new,$now)"; Add(audit,"$id",Guid.NewGuid().ToString()); Add(audit,"$c",edit.CompanyId); Add(audit,"$entity",id); Add(audit,"$action",string.IsNullOrWhiteSpace(edit.Id)?"AccountCreated":"AccountUpdated"); Add(audit,"$new",edit.Code); Add(audit,"$now",now); audit.ExecuteNonQuery(); tx.Commit();
    }

    public AccountDashboardSummary GetDashboardSummary(string companyId)
    {
        var row = database.Query("""
            SELECT
                SUM(CASE WHEN a.is_active=1 THEN 1 ELSE 0 END) AS active_accounts,
                SUM(CASE WHEN a.is_active=1 AND a.account_type IN ('Customer','CustomerAndSupplier') THEN 1 ELSE 0 END) AS customers,
                SUM(CASE WHEN a.is_active=1 AND a.account_type IN ('Supplier','CustomerAndSupplier') THEN 1 ELSE 0 END) AS suppliers,
                SUM(CASE WHEN a.account_type IN ('Customer','CustomerAndSupplier') AND COALESCE(b.balance,0)>0 THEN b.balance ELSE 0 END) AS customer_receivable,
                SUM(CASE WHEN a.account_type IN ('Supplier','CustomerAndSupplier') AND COALESCE(b.balance,0)<0 THEN -b.balance ELSE 0 END) AS supplier_payable,
                SUM(CASE WHEN a.credit_limit>0 AND COALESCE(b.balance,0)>a.credit_limit THEN 1 ELSE 0 END) AS limit_exceeded
            FROM accounts a
            LEFT JOIN account_balances b ON b.company_id=a.company_id AND b.account_id=a.id
            WHERE a.company_id=$company
            """, ("$company", companyId)).Rows[0];
        return new(
            Convert.ToInt32(row["active_accounts"] is DBNull ? 0 : row["active_accounts"]),
            Convert.ToInt32(row["customers"] is DBNull ? 0 : row["customers"]),
            Convert.ToInt32(row["suppliers"] is DBNull ? 0 : row["suppliers"]),
            Convert.ToDecimal(row["customer_receivable"] is DBNull ? 0 : row["customer_receivable"]),
            Convert.ToDecimal(row["supplier_payable"] is DBNull ? 0 : row["supplier_payable"]),
            Convert.ToInt32(row["limit_exceeded"] is DBNull ? 0 : row["limit_exceeded"]));
    }

    public DataTable RecentTransactions(string companyId, int limit = 25) => database.Query("""
        SELECT t.transaction_at AS Tarih,a.code AS CariKodu,a.name AS Cari,
               t.transaction_type AS IslemTipi,t.document_no AS BelgeNo,t.description AS Aciklama,
               t.debit AS Borc,t.credit AS Alacak,t.currency_code AS Doviz
        FROM account_transactions t
        JOIN accounts a ON a.id=t.account_id
        WHERE t.company_id=$company
        ORDER BY t.transaction_at DESC,t.created_at DESC
        LIMIT $limit
        """, ("$company", companyId), ("$limit", limit));

    public DataTable Statement(string companyId, string accountId) => database.Query("""
        SELECT t.transaction_at AS Tarih,t.document_no AS Belge,t.description AS Aciklama,
               t.debit AS Borc,t.credit AS Alacak,
               SUM(t.debit-t.credit) OVER (ORDER BY t.transaction_at,t.created_at,t.id) AS Bakiye,
               t.currency_code AS Doviz,t.exchange_rate AS Kur,t.transaction_type AS IslemTipi
        FROM account_transactions t
        WHERE t.company_id=$company AND t.account_id=$account
        ORDER BY t.transaction_at,t.created_at,t.id
        """, ("$company", companyId), ("$account", accountId));

    public DataTable CreditRisk(string companyId) => database.Query("""
        SELECT a.code AS CariKodu,a.name AS Cari,COALESCE(b.balance,0) AS CariBakiye,
               a.credit_limit AS KrediLimiti,a.risk_limit AS RiskLimiti,
               MAX(0,a.credit_limit-MAX(0,COALESCE(b.balance,0))) AS KullanilabilirLimit,
               CASE WHEN a.credit_limit<=0 THEN 0 ELSE ROUND(MAX(0,COALESCE(b.balance,0))*100.0/a.credit_limit,1) END AS KullanimYuzdesi,
               CASE WHEN a.is_active=0 THEN 'Bloke'
                    WHEN a.credit_limit>0 AND COALESCE(b.balance,0)>a.credit_limit THEN 'Limit Aşıldı'
                    WHEN a.credit_limit>0 AND COALESCE(b.balance,0)>=a.credit_limit*0.8 THEN 'Limite Yakın'
                    ELSE 'Normal' END AS RiskDurumu
        FROM accounts a
        LEFT JOIN account_balances b ON b.company_id=a.company_id AND b.account_id=a.id
        WHERE a.company_id=$company AND a.account_type IN ('Customer','CustomerAndSupplier')
        ORDER BY KullanimYuzdesi DESC,a.code
        """, ("$company", companyId));

    public DataTable Lookup(string companyId, string? accountType = null) => database.Query("""
        SELECT id AS Id,code AS Code,name AS Name
        FROM accounts
        WHERE company_id=$company AND is_active=1
          AND ($type='' OR account_type=$type OR account_type='CustomerAndSupplier')
        ORDER BY code
        """, ("$company", companyId), ("$type", accountType ?? ""));

    private static void Add(SqliteCommand c,string n,object v)=>c.Parameters.AddWithValue(n,v);
}
