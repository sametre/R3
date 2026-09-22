using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record AccountEdit(
    string Id, string CompanyId, string Code, string Name, string AccountType = "Customer", string TaxOffice = "", string TaxNumber = "",
    string Phone = "", string MobilePhone = "", string Email = "", decimal CreditLimit = 0, decimal RiskLimit = 0, bool IsActive = true,
    string IdentityNumber = "", string ShortName = "", string DefaultCurrencyCode = "TRY", string PreferredLanguageCode = "tr",
    string? AccountGroupId = null, string? RegionId = null);

public sealed record AccountTaxProfileEdit(string PersonType = "LegalEntity", string LegalTitle = "", string TradeRegistryNumber = "", string CountryCode = "TR");

public sealed record AccountEInvoiceProfileEdit(
    bool IsEInvoiceEnabled = false, string EInvoiceAlias = "", string InvoiceScenario = "Temel",
    bool IsEDispatchEnabled = false, string EDispatchAlias = "", bool IsGibCompliant = false, bool PrintInvoice = true);

public sealed record CustomerProfileEdit(
    string CustomerGroup = "", string Region = "", string? SalesRepresentativeId = null, string? PriceListId = null,
    int PaymentTermDays = 0, decimal DiscountRate = 0, decimal ExtraCreditLimit = 0, decimal BlockedCreditAmount = 0,
    string CreditControlType = "None", string DefaultPaymentMethod = "", bool IsOrderBlocked = false, bool IsActiveBuyer = true,
    DateTime? StartedAt = null, bool KvkkConsent = false);

public sealed record SupplierProfileEdit(
    string SupplierGroup = "", string Region = "", string DefaultCurrencyCode = "TRY", int PaymentTermDays = 0,
    int LeadTimeDays = 0, bool IsActiveSupplier = true, string Notes = "");

public sealed record AccountAggregateEdit(AccountEdit Account, AccountTaxProfileEdit Tax, AccountEInvoiceProfileEdit EInvoice, CustomerProfileEdit? Customer, SupplierProfileEdit? Supplier);

public sealed record AccountDetail(AccountEdit Account, AccountTaxProfileEdit Tax, AccountEInvoiceProfileEdit EInvoice, CustomerProfileEdit? Customer, SupplierProfileEdit? Supplier, decimal Debit, decimal Credit, decimal Balance);

public sealed record AccountDashboardSummary(
    int ActiveAccounts,
    int Customers,
    int Suppliers,
    decimal CustomerReceivable,
    decimal SupplierPayable,
    int CreditLimitExceeded);

/// <summary>
/// Account 1 -> {1 AccountTaxProfile, 1 AccountEInvoiceProfile, 0..1 CustomerProfile, 0..1 SupplierProfile}.
/// Customer/SupplierProfile rows are only written when AccountType includes that role; a role
/// dropped later keeps its historical profile row (never deleted) so re-enabling the role restores it.
/// </summary>
public sealed class LocalAccountService(StoreDatabase database)
{
    private static readonly string[] ValidPaymentMethods = ["Cash", "BankTransfer", "CreditCard", "Cheque", "PromissoryNote"];

    public StoreDatabase Database => database;

    public DataTable Search(string companyId, string? search = null, string? accountType = null, bool activeOnly = false)
    {
        var q = $"%{search?.Trim() ?? ""}%"; return database.Query("""
            SELECT a.id AS Id,a.code AS Kod,a.name AS Cari,a.account_type AS Tip,a.tax_office AS VergiDairesi,a.tax_number AS VergiNo,
                   a.phone AS Telefon,a.mobile_phone AS CepTelefonu,a.email AS Eposta,
                   COALESCE(b.debit,0) AS Borc,COALESCE(b.credit,0) AS Alacak,COALESCE(b.balance,0) AS Bakiye,
                   a.is_active AS Aktif,a.credit_limit AS KrediLimiti,a.risk_limit AS RiskLimiti,COALESCE(addr.city,'') AS Il
            FROM accounts a
            LEFT JOIN account_balances b ON b.account_id=a.id AND b.company_id=a.company_id
            LEFT JOIN (SELECT account_id, city FROM account_addresses WHERE is_default=1 AND is_active=1 GROUP BY account_id) addr ON addr.account_id=a.id
            WHERE a.company_id=$c AND ($q='' OR a.code LIKE $q OR a.name LIKE $q OR a.tax_number LIKE $q OR a.phone LIKE $q OR a.mobile_phone LIKE $q)
              AND ($t='' OR a.account_type=$t OR (a.account_type='CustomerAndSupplier' AND $t IN ('Customer','Supplier')))
              AND ($active=0 OR a.is_active=1)
            ORDER BY a.code
            """, ("$c", companyId), ("$q", q), ("$t", accountType ?? ""), ("$active", activeOnly ? 1 : 0));
    }

    public void Save(AccountAggregateEdit edit)
    {
        var a = edit.Account;
        if (string.IsNullOrWhiteSpace(a.CompanyId) || string.IsNullOrWhiteSpace(a.Code) || string.IsNullOrWhiteSpace(a.Name)) throw new ArgumentException("Firma, cari kodu ve cari adı zorunludur.");
        if (a.AccountType is not ("Customer" or "Supplier" or "CustomerAndSupplier" or "Other")) throw new ArgumentException("Geçersiz cari tipi.");
        if (edit.Tax.PersonType is not ("Individual" or "LegalEntity")) throw new ArgumentException("Geçersiz kişi/firma tipi.");
        if (a.CreditLimit < 0 || a.RiskLimit < 0) throw new ArgumentException("Kredi ve risk limitleri negatif olamaz.");
        if (edit.Customer is { } customer && (customer.PaymentTermDays < 0 || customer.DiscountRate < 0 || customer.DiscountRate > 100 || customer.ExtraCreditLimit < 0 || customer.BlockedCreditAmount < 0)) throw new ArgumentException("Müşteri vade, iskonto ve limit değerleri geçersizdir.");
        if (edit.Supplier is { } supplier && (supplier.PaymentTermDays < 0 || supplier.LeadTimeDays < 0)) throw new ArgumentException("Tedarikçi vade ve termin değerleri negatif olamaz.");
        if (edit.Customer is { CreditControlType: not ("None" or "Warning" or "Block") }) throw new ArgumentException("Geçersiz kredi kontrol tipi.");
        if (edit.Customer is { DefaultPaymentMethod: not ("" or "Cash" or "BankTransfer" or "CreditCard" or "Cheque" or "PromissoryNote") }) throw new ArgumentException("Geçersiz varsayılan ödeme yöntemi.");
        if (edit.Tax.PersonType == "LegalEntity" && !string.IsNullOrWhiteSpace(a.TaxNumber) && a.TaxNumber.Trim().Length != 10) throw new ArgumentException("Vergi numarası 10 haneli olmalıdır.");
        if (edit.Tax.PersonType == "Individual" && !string.IsNullOrWhiteSpace(a.IdentityNumber) && a.IdentityNumber.Trim().Length != 11) throw new ArgumentException("T.C. Kimlik No 11 haneli olmalıdır.");

        var id = string.IsNullOrWhiteSpace(a.Id) ? Guid.NewGuid().ToString() : a.Id;
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();

        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO accounts(id,company_id,code,name,account_type,tax_office,tax_number,phone,mobile_phone,email,credit_limit,risk_limit,is_active,identity_number,short_name,default_currency_code,preferred_language_code,account_group_id,region_id,created_at,updated_at)
                VALUES($id,$c,$code,$name,$type,$office,$tax,$phone,$mobile,$email,$credit,$risk,$active,$identity,$short,$currency,$lang,$group,$region,$now,$now)
                ON CONFLICT(id) DO UPDATE SET code=$code,name=$name,account_type=$type,tax_office=$office,tax_number=$tax,phone=$phone,mobile_phone=$mobile,email=$email,
                    credit_limit=$credit,risk_limit=$risk,is_active=$active,identity_number=$identity,short_name=$short,default_currency_code=$currency,
                    preferred_language_code=$lang,account_group_id=$group,region_id=$region,updated_at=$now
                """;
            Add(cmd, "$id", id); Add(cmd, "$c", a.CompanyId); Add(cmd, "$code", a.Code.Trim().ToUpperInvariant()); Add(cmd, "$name", a.Name.Trim()); Add(cmd, "$type", a.AccountType);
            Add(cmd, "$office", a.TaxOffice); Add(cmd, "$tax", a.TaxNumber); Add(cmd, "$phone", a.Phone); Add(cmd, "$mobile", a.MobilePhone); Add(cmd, "$email", a.Email);
            Add(cmd, "$credit", a.CreditLimit); Add(cmd, "$risk", a.RiskLimit); Add(cmd, "$active", a.IsActive ? 1 : 0); Add(cmd, "$identity", a.IdentityNumber);
            Add(cmd, "$short", a.ShortName); Add(cmd, "$currency", a.DefaultCurrencyCode); Add(cmd, "$lang", a.PreferredLanguageCode);
            AddNullable(cmd, "$group", a.AccountGroupId); AddNullable(cmd, "$region", a.RegionId); Add(cmd, "$now", now);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO account_tax_profiles(account_id,person_type,legal_title,trade_registry_number,country_code)
                VALUES($id,$person,$legal,$trade,$country)
                ON CONFLICT(account_id) DO UPDATE SET person_type=$person,legal_title=$legal,trade_registry_number=$trade,country_code=$country
                """;
            Add(cmd, "$id", id); Add(cmd, "$person", edit.Tax.PersonType); Add(cmd, "$legal", edit.Tax.LegalTitle);
            Add(cmd, "$trade", edit.Tax.TradeRegistryNumber); Add(cmd, "$country", edit.Tax.CountryCode);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO account_einvoice_profiles(account_id,is_einvoice_enabled,einvoice_alias,invoice_scenario,is_edispatch_enabled,edispatch_alias,is_gib_compliant,print_invoice)
                VALUES($id,$einv,$ealias,$scenario,$edisp,$dalias,$gib,$print)
                ON CONFLICT(account_id) DO UPDATE SET is_einvoice_enabled=$einv,einvoice_alias=$ealias,invoice_scenario=$scenario,
                    is_edispatch_enabled=$edisp,edispatch_alias=$dalias,is_gib_compliant=$gib,print_invoice=$print
                """;
            Add(cmd, "$id", id); Add(cmd, "$einv", edit.EInvoice.IsEInvoiceEnabled ? 1 : 0); Add(cmd, "$ealias", edit.EInvoice.EInvoiceAlias);
            Add(cmd, "$scenario", edit.EInvoice.InvoiceScenario); Add(cmd, "$edisp", edit.EInvoice.IsEDispatchEnabled ? 1 : 0);
            Add(cmd, "$dalias", edit.EInvoice.EDispatchAlias); Add(cmd, "$gib", edit.EInvoice.IsGibCompliant ? 1 : 0); Add(cmd, "$print", edit.EInvoice.PrintInvoice ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        if (edit.Customer is { } cust && a.AccountType is "Customer" or "CustomerAndSupplier")
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO customer_profiles(account_id,customer_group,region,payment_term_days,discount_rate,extra_credit_limit,blocked_credit,kvkk_consent,started_at,sales_representative_id,price_list_id,default_payment_method,is_order_blocked,is_active_buyer,credit_control_type)
                VALUES($id,$group,$region,$term,$discount,$extra,$blocked,$kvkk,$started,$rep,$price,$pay,$blockedorder,$activebuyer,$control)
                ON CONFLICT(account_id) DO UPDATE SET customer_group=$group,region=$region,payment_term_days=$term,discount_rate=$discount,
                    extra_credit_limit=$extra,blocked_credit=$blocked,kvkk_consent=$kvkk,started_at=$started,sales_representative_id=$rep,
                    price_list_id=$price,default_payment_method=$pay,is_order_blocked=$blockedorder,is_active_buyer=$activebuyer,credit_control_type=$control
                """;
            Add(cmd, "$id", id); Add(cmd, "$group", cust.CustomerGroup); Add(cmd, "$region", cust.Region); Add(cmd, "$term", cust.PaymentTermDays);
            Add(cmd, "$discount", cust.DiscountRate); Add(cmd, "$extra", cust.ExtraCreditLimit); Add(cmd, "$blocked", cust.BlockedCreditAmount);
            Add(cmd, "$kvkk", cust.KvkkConsent ? 1 : 0); AddNullable(cmd, "$started", cust.StartedAt?.ToString("O"));
            AddNullable(cmd, "$rep", cust.SalesRepresentativeId); AddNullable(cmd, "$price", cust.PriceListId); Add(cmd, "$pay", cust.DefaultPaymentMethod);
            Add(cmd, "$blockedorder", cust.IsOrderBlocked ? 1 : 0); Add(cmd, "$activebuyer", cust.IsActiveBuyer ? 1 : 0); Add(cmd, "$control", cust.CreditControlType);
            cmd.ExecuteNonQuery();
        }

        if (edit.Supplier is { } sup && a.AccountType is "Supplier" or "CustomerAndSupplier")
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO supplier_profiles(account_id,supplier_group,region,payment_term_days,default_currency_code,lead_time_days,is_active_supplier,notes)
                VALUES($id,$group,$region,$term,$currency,$lead,$active,$notes)
                ON CONFLICT(account_id) DO UPDATE SET supplier_group=$group,region=$region,payment_term_days=$term,default_currency_code=$currency,
                    lead_time_days=$lead,is_active_supplier=$active,notes=$notes
                """;
            Add(cmd, "$id", id); Add(cmd, "$group", sup.SupplierGroup); Add(cmd, "$region", sup.Region); Add(cmd, "$term", sup.PaymentTermDays);
            Add(cmd, "$currency", sup.DefaultCurrencyCode); Add(cmd, "$lead", sup.LeadTimeDays); Add(cmd, "$active", sup.IsActiveSupplier ? 1 : 0); Add(cmd, "$notes", sup.Notes);
            cmd.ExecuteNonQuery();
        }

        using (var audit = c.CreateCommand())
        {
            audit.Transaction = tx;
            audit.CommandText = "INSERT INTO audit_logs(id,company_id,entity_type,entity_id,action,new_values,created_at) VALUES($id,$c,'Account',$entity,$action,$new,$now)";
            Add(audit, "$id", Guid.NewGuid().ToString()); Add(audit, "$c", a.CompanyId); Add(audit, "$entity", id);
            Add(audit, "$action", string.IsNullOrWhiteSpace(a.Id) ? "AccountCreated" : "AccountUpdated"); Add(audit, "$new", a.Code); Add(audit, "$now", now);
            audit.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public AccountDetail? GetDetail(string companyId, string accountId)
    {
        var t = database.Query("""
            SELECT a.id,a.code,a.name,a.account_type,a.tax_office,a.tax_number,a.phone,a.mobile_phone,a.email,a.credit_limit,a.risk_limit,a.is_active,
                   a.identity_number,a.short_name,a.default_currency_code,a.preferred_language_code,a.account_group_id,a.region_id,
                   tp.person_type,tp.legal_title,tp.trade_registry_number,tp.country_code,
                   ei.is_einvoice_enabled,ei.einvoice_alias,ei.invoice_scenario,ei.is_edispatch_enabled,ei.edispatch_alias,ei.is_gib_compliant,ei.print_invoice,
                   cp.customer_group,cp.region AS customer_region,cp.payment_term_days AS customer_term,cp.discount_rate,cp.extra_credit_limit,cp.blocked_credit,
                   cp.kvkk_consent,cp.started_at,cp.sales_representative_id,cp.price_list_id,cp.default_payment_method,cp.is_order_blocked,cp.is_active_buyer,cp.credit_control_type,
                   sp.supplier_group,sp.region AS supplier_region,sp.payment_term_days AS supplier_term,sp.default_currency_code AS supplier_currency,
                   sp.lead_time_days,sp.is_active_supplier,sp.notes AS supplier_notes,
                   COALESCE(b.debit,0) AS bal_debit, COALESCE(b.credit,0) AS bal_credit, COALESCE(b.balance,0) AS bal_balance
            FROM accounts a
            LEFT JOIN account_tax_profiles tp ON tp.account_id=a.id
            LEFT JOIN account_einvoice_profiles ei ON ei.account_id=a.id
            LEFT JOIN customer_profiles cp ON cp.account_id=a.id
            LEFT JOIN supplier_profiles sp ON sp.account_id=a.id
            LEFT JOIN account_balances b ON b.account_id=a.id AND b.company_id=a.company_id
            WHERE a.id=$id AND a.company_id=$company
            """, ("$id", accountId), ("$company", companyId));
        if (t.Rows.Count == 0) return null;
        var r = t.Rows[0];

        string S(string col) => r[col] is DBNull ? "" : r[col].ToString()!;
        string? N(string col) => r[col] is DBNull ? null : r[col].ToString();
        bool B(string col, bool def = false) => r[col] is DBNull ? def : Convert.ToBoolean(r[col]);
        int I(string col) => r[col] is DBNull ? 0 : Convert.ToInt32(r[col]);
        decimal D(string col) => r[col] is DBNull ? 0m : Convert.ToDecimal(r[col]);
        DateTime? Dt(string col) => r[col] is DBNull ? null : DateTime.Parse(r[col].ToString()!);

        var account = new AccountEdit(
            r["id"].ToString()!, companyId, S("code"), S("name"), S("account_type"), S("tax_office"), S("tax_number"), S("phone"), S("mobile_phone"), S("email"),
            D("credit_limit"), D("risk_limit"), B("is_active", true), S("identity_number"), S("short_name"),
            string.IsNullOrEmpty(S("default_currency_code")) ? "TRY" : S("default_currency_code"),
            string.IsNullOrEmpty(S("preferred_language_code")) ? "tr" : S("preferred_language_code"), N("account_group_id"), N("region_id"));

        var tax = new AccountTaxProfileEdit(
            string.IsNullOrEmpty(S("person_type")) ? "LegalEntity" : S("person_type"), S("legal_title"), S("trade_registry_number"),
            string.IsNullOrEmpty(S("country_code")) ? "TR" : S("country_code"));

        var einvoice = new AccountEInvoiceProfileEdit(
            B("is_einvoice_enabled"), S("einvoice_alias"), string.IsNullOrEmpty(S("invoice_scenario")) ? "Temel" : S("invoice_scenario"),
            B("is_edispatch_enabled"), S("edispatch_alias"), B("is_gib_compliant"), B("print_invoice", true));

        CustomerProfileEdit? customer = r["customer_term"] is DBNull ? null : new CustomerProfileEdit(
            S("customer_group"), S("customer_region"), N("sales_representative_id"), N("price_list_id"), I("customer_term"),
            D("discount_rate"), D("extra_credit_limit"), D("blocked_credit"), string.IsNullOrEmpty(S("credit_control_type")) ? "None" : S("credit_control_type"),
            S("default_payment_method"), B("is_order_blocked"), B("is_active_buyer", true), Dt("started_at"), B("kvkk_consent"));

        SupplierProfileEdit? supplier = r["supplier_term"] is DBNull ? null : new SupplierProfileEdit(
            S("supplier_group"), S("supplier_region"), string.IsNullOrEmpty(S("supplier_currency")) ? "TRY" : S("supplier_currency"),
            I("supplier_term"), I("lead_time_days"), B("is_active_supplier", true), S("supplier_notes"));

        return new AccountDetail(account, tax, einvoice, customer, supplier, D("bal_debit"), D("bal_credit"), D("bal_balance"));
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

    public DataTable RecentTransactions(string companyId, int limit = 25, string? accountId = null) => database.Query("""
        SELECT t.transaction_at AS Tarih,a.code AS CariKodu,a.name AS Cari,
               t.transaction_type AS IslemTipi,t.document_no AS BelgeNo,t.description AS Aciklama,
               t.debit AS Borc,t.credit AS Alacak,t.currency_code AS Doviz
        FROM account_transactions t
        JOIN accounts a ON a.id=t.account_id
        WHERE t.company_id=$company AND ($account='' OR t.account_id=$account)
        ORDER BY t.transaction_at DESC,t.created_at DESC
        LIMIT $limit
        """, ("$company", companyId), ("$account", accountId ?? ""), ("$limit", limit));

    public void SetActive(string companyId, string accountId, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(accountId)) throw new ArgumentException("Firma ve cari seçimi zorunludur.");
        var now = DateTime.UtcNow.ToString("O");
        using var connection = database.OpenConnection(); using var transaction = connection.BeginTransaction();
        using var update = connection.CreateCommand(); update.Transaction = transaction;
        update.CommandText = "UPDATE accounts SET is_active=$active,updated_at=$now WHERE id=$id AND company_id=$company";
        Add(update, "$active", isActive ? 1 : 0); Add(update, "$now", now); Add(update, "$id", accountId); Add(update, "$company", companyId);
        if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException("Cari kaydı bulunamadı.");
        using var audit = connection.CreateCommand(); audit.Transaction = transaction;
        audit.CommandText = "INSERT INTO audit_logs(id,company_id,entity_type,entity_id,action,new_values,created_at) VALUES($id,$company,'Account',$entity,$action,$value,$now)";
        Add(audit, "$id", Guid.NewGuid().ToString()); Add(audit, "$company", companyId); Add(audit, "$entity", accountId); Add(audit, "$action", isActive ? "AccountActivated" : "AccountDeactivated"); Add(audit, "$value", isActive ? "Active" : "Passive"); Add(audit, "$now", now); audit.ExecuteNonQuery();
        transaction.Commit();
    }

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
               COALESCE(cp.extra_credit_limit,0) AS EkLimit, COALESCE(cp.blocked_credit,0) AS BlokeLimit,
               MAX(0,a.credit_limit-MAX(0,COALESCE(b.balance,0))) AS KullanilabilirLimit,
               CASE WHEN a.credit_limit<=0 THEN 0 ELSE ROUND(MAX(0,COALESCE(b.balance,0))*100.0/a.credit_limit,1) END AS KullanimYuzdesi,
               CASE WHEN a.is_active=0 THEN 'Bloke'
                    WHEN a.credit_limit>0 AND COALESCE(b.balance,0)>a.credit_limit THEN 'Limit Aşıldı'
                    WHEN a.credit_limit>0 AND COALESCE(b.balance,0)>=a.credit_limit*0.8 THEN 'Limite Yakın'
                    ELSE 'Normal' END AS RiskDurumu
        FROM accounts a
        LEFT JOIN account_balances b ON b.company_id=a.company_id AND b.account_id=a.id
        LEFT JOIN customer_profiles cp ON cp.account_id=a.id
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

    public decimal GetBalance(string companyId, string accountId)
    {
        var t = database.Query("SELECT balance FROM account_balances WHERE company_id=$c AND account_id=$a", ("$c", companyId), ("$a", accountId));
        return t.Rows.Count == 0 ? 0m : Convert.ToDecimal(t.Rows[0]["balance"]);
    }

    public DataTable Invoices(string companyId, string accountId) => database.Query("""
        SELECT document_date AS Tarih, document_no AS BelgeNo, document_type AS Tip, status AS Durum,
               currency_code AS Doviz, grand_total AS Tutar
        FROM sales_documents
        WHERE company_id=$company AND account_id=$account
        ORDER BY document_date DESC, created_at DESC
        """, ("$company", companyId), ("$account", accountId));

    public DataTable TransactionsByType(string companyId, string accountId, string transactionType) => database.Query("""
        SELECT transaction_at AS Tarih, document_no AS BelgeNo, description AS Aciklama, debit AS Borc, credit AS Alacak, currency_code AS Doviz
        FROM account_transactions
        WHERE company_id=$company AND account_id=$account AND transaction_type=$type
        ORDER BY transaction_at DESC, created_at DESC
        """, ("$company", companyId), ("$account", accountId), ("$type", transactionType));

    public DataTable AuditHistory(string accountId) => database.Query("""
        SELECT al.created_at AS Tarih, COALESCE(al.user_id,'') AS Kullanici, al.entity_type AS Alan, al.action AS Islem,
               al.old_values AS EskiDeger, al.new_values AS YeniDeger
        FROM audit_logs al
        WHERE al.entity_type IN ('Account','AccountAddress','AccountContact','AccountNote') AND (al.entity_id=$account OR al.new_values=$account)
        ORDER BY al.created_at DESC
        """, ("$account", accountId));

    public void PostReceipt(string companyId, string branchId, string accountId, DateTime date, decimal amount, string currencyCode, decimal exchangeRate, string paymentMethod, string? documentNo, string description)
    {
        if (amount <= 0) throw new ArgumentException("Tahsilat tutarı 0'dan büyük olmalıdır.");
        if (!ValidPaymentMethods.Contains(paymentMethod)) throw new ArgumentException("Geçersiz ödeme yöntemi.");
        Post(companyId, branchId, accountId, "Receipt", debit: 0, credit: amount, currencyCode, exchangeRate, documentNo, description, date);
    }

    public void PostPayment(string companyId, string branchId, string accountId, DateTime date, decimal amount, string currencyCode, decimal exchangeRate, string paymentMethod, string? documentNo, string description)
    {
        if (amount <= 0) throw new ArgumentException("Ödeme tutarı 0'dan büyük olmalıdır.");
        if (!ValidPaymentMethods.Contains(paymentMethod)) throw new ArgumentException("Geçersiz ödeme yöntemi.");
        Post(companyId, branchId, accountId, "Payment", debit: amount, credit: 0, currencyCode, exchangeRate, documentNo, description, date);
    }

    public void PostManualEntry(string companyId, string branchId, string accountId, string direction, decimal amount, string currencyCode, decimal exchangeRate, DateTime date, string? documentNo, string description)
    {
        if (amount <= 0) throw new ArgumentException("Tutar 0'dan büyük olmalıdır.");
        if (direction is not ("Debit" or "Credit")) throw new ArgumentException("Geçersiz işlem yönü.");
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Manuel cari hareketlerinde açıklama zorunludur.");
        Post(companyId, branchId, accountId, "ManualEntry", debit: direction == "Debit" ? amount : 0, credit: direction == "Credit" ? amount : 0, currencyCode, exchangeRate, documentNo, description, date);
    }

    private void Post(string companyId, string branchId, string accountId, string transactionType, decimal debit, decimal credit, string currencyCode, decimal exchangeRate, string? documentNo, string description, DateTime date)
    {
        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(accountId)) throw new ArgumentException("Firma ve cari seçimi zorunludur.");
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        using (var at = c.CreateCommand())
        {
            at.Transaction = tx;
            at.CommandText = "INSERT INTO account_transactions(id,company_id,branch_id,account_id,transaction_type,debit,credit,currency_code,exchange_rate,document_type,document_no,description,transaction_at,created_at) VALUES($id,$c,$b,$a,$type,$debit,$credit,$currency,$rate,$type,$doc,$desc,$at,$now)";
            Add(at, "$id", Guid.NewGuid().ToString()); Add(at, "$c", companyId); Add(at, "$b", branchId); Add(at, "$a", accountId); Add(at, "$type", transactionType);
            Add(at, "$debit", debit); Add(at, "$credit", credit); Add(at, "$currency", currencyCode); Add(at, "$rate", exchangeRate);
            Add(at, "$doc", documentNo ?? ""); Add(at, "$desc", description); Add(at, "$at", date.ToString("O")); Add(at, "$now", now);
            at.ExecuteNonQuery();
        }
        using (var ab = c.CreateCommand())
        {
            ab.Transaction = tx;
            ab.CommandText = "INSERT INTO account_balances(company_id,account_id,debit,credit,balance,updated_at) VALUES($c,$a,$d,$cr,$net,$now) ON CONFLICT(company_id,account_id) DO UPDATE SET debit=debit+$d,credit=credit+$cr,balance=balance+$net,updated_at=$now";
            Add(ab, "$c", companyId); Add(ab, "$a", accountId); Add(ab, "$d", debit); Add(ab, "$cr", credit); Add(ab, "$net", debit - credit); Add(ab, "$now", now);
            ab.ExecuteNonQuery();
        }
        using (var audit = c.CreateCommand())
        {
            audit.Transaction = tx;
            audit.CommandText = "INSERT INTO audit_logs(id,company_id,entity_type,entity_id,action,new_values,created_at) VALUES($id,$c,'Account',$entity,$action,$new,$now)";
            Add(audit, "$id", Guid.NewGuid().ToString()); Add(audit, "$c", companyId); Add(audit, "$entity", accountId); Add(audit, "$action", transactionType); Add(audit, "$new", description); Add(audit, "$now", now);
            audit.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static void Add(SqliteCommand c, string n, object v) => c.Parameters.AddWithValue(n, v);
    private static void AddNullable(SqliteCommand c, string n, string? v) => c.Parameters.AddWithValue(n, (object?)v ?? DBNull.Value);
}
