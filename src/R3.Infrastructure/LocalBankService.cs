using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record BankAccountEdit(
    string Id, string CompanyId, string BranchId, string Code, string Name, string BankName = "", string BankBranchName = "",
    string BankBranchCode = "", string AccountNumber = "", string Iban = "", string CurrencyCode = "TRY",
    string AccountType = "Checking", bool IsActive = true, string Description = "");

public sealed record BankAccountDetail(BankAccountEdit Account, decimal TotalIn, decimal TotalOut, decimal Balance);

/// <summary>
/// Canonical Bank Ledger Engine - deliberately the same shape as <see cref="LocalCashService"/>
/// (source of truth is bank_transactions, bank_balances is a rebuildable projection) so the two
/// modules behave identically from a user's perspective and a bug fixed in one's pattern is easy
/// to cross-check against the other. Adds bank&lt;-&gt;cash transfers, which cash-only flows don't need.
/// </summary>
public sealed class LocalBankService(StoreDatabase database)
{
    private static readonly string[] ValidAccountTypes = ["Checking", "Savings", "ForeignCurrency", "Loan", "Other"];
    private static readonly string[] BankInTypes = ["OpeningBalance", "CustomerReceipt", "ChequeCollection", "BankIncome", "ManualIn"];
    private static readonly string[] BankOutTypes = ["SupplierPayment", "ChequePayment", "BankExpense", "ManualOut"];

    public StoreDatabase Database => database;

    public DataTable Search(string companyId, string? branchId = null, string? search = null, bool activeOnly = false)
    {
        var q = $"%{search?.Trim() ?? ""}%";
        return database.Query("""
            SELECT ba.id AS Id, ba.code AS Kod, ba.name AS Ad, b.name AS Sube, ba.bank_name AS Banka, ba.iban AS Iban,
                   ba.account_type AS HesapTipi, ba.currency_code AS ParaBirimi,
                   COALESCE(bb.total_in,0) AS Giris, COALESCE(bb.total_out,0) AS Cikis, COALESCE(bb.balance,0) AS Bakiye,
                   ba.is_active AS Aktif
            FROM bank_accounts ba
            JOIN branches b ON b.id=ba.branch_id
            LEFT JOIN bank_balances bb ON bb.company_id=ba.company_id AND bb.bank_account_id=ba.id
            WHERE ba.company_id=$company AND ($branch='' OR ba.branch_id=$branch)
              AND ($q='' OR ba.code LIKE $q OR ba.name LIKE $q OR ba.bank_name LIKE $q OR ba.iban LIKE $q) AND ($active=0 OR ba.is_active=1)
            ORDER BY ba.code
            """, ("$company", companyId), ("$branch", branchId ?? ""), ("$q", q), ("$active", activeOnly ? 1 : 0));
    }

    public DataTable Lookup(string companyId, string? branchId = null) => database.Query("""
        SELECT id AS Id, code AS Code, name AS Name, currency_code AS CurrencyCode
        FROM bank_accounts
        WHERE company_id=$company AND is_active=1 AND ($branch='' OR branch_id=$branch)
        ORDER BY code
        """, ("$company", companyId), ("$branch", branchId ?? ""));

    public void Save(BankAccountEdit edit, string userName)
    {
        if (string.IsNullOrWhiteSpace(edit.CompanyId) || string.IsNullOrWhiteSpace(edit.BranchId) || string.IsNullOrWhiteSpace(edit.Code) || string.IsNullOrWhiteSpace(edit.Name))
            throw new ArgumentException("Şirket, şube, hesap kodu ve hesap adı zorunludur.");
        if (!ValidAccountTypes.Contains(edit.AccountType)) throw new ArgumentException("Geçersiz banka hesap tipi.");
        if (string.IsNullOrWhiteSpace(edit.CurrencyCode)) throw new ArgumentException("Para birimi zorunludur.");

        var id = string.IsNullOrWhiteSpace(edit.Id) ? Guid.NewGuid().ToString() : edit.Id;
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();

        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO bank_accounts(id,company_id,branch_id,code,name,bank_name,bank_branch_name,bank_branch_code,account_number,iban,currency_code,account_type,is_active,description,created_at,created_by,updated_at,updated_by)
                VALUES($id,$company,$branch,$code,$name,$bank,$bankBranch,$bankBranchCode,$accountNo,$iban,$currency,$type,$active,$description,$now,$user,$now,$user)
                ON CONFLICT(id) DO UPDATE SET branch_id=$branch,code=$code,name=$name,bank_name=$bank,bank_branch_name=$bankBranch,bank_branch_code=$bankBranchCode,
                    account_number=$accountNo,iban=$iban,currency_code=$currency,account_type=$type,is_active=$active,description=$description,updated_at=$now,updated_by=$user
                """;
            Add(cmd, "$id", id); Add(cmd, "$company", edit.CompanyId); Add(cmd, "$branch", edit.BranchId); Add(cmd, "$code", edit.Code.Trim().ToUpperInvariant());
            Add(cmd, "$name", edit.Name.Trim()); Add(cmd, "$bank", edit.BankName.Trim()); Add(cmd, "$bankBranch", edit.BankBranchName.Trim());
            Add(cmd, "$bankBranchCode", edit.BankBranchCode.Trim()); Add(cmd, "$accountNo", edit.AccountNumber.Trim()); Add(cmd, "$iban", edit.Iban.Trim().Replace(" ", ""));
            Add(cmd, "$currency", edit.CurrencyCode); Add(cmd, "$type", edit.AccountType); Add(cmd, "$active", edit.IsActive ? 1 : 0);
            Add(cmd, "$description", edit.Description); Add(cmd, "$now", now); Add(cmd, "$user", userName);
            cmd.ExecuteNonQuery();
        }

        Audit(c, tx, edit.CompanyId, "BankAccount", id, string.IsNullOrWhiteSpace(edit.Id) ? "BankAccountCreated" : "BankAccountUpdated", edit.Code, now);
        tx.Commit();
    }

    public void SetActive(string companyId, string bankAccountId, bool isActive, string userName)
    {
        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(bankAccountId)) throw new ArgumentException("Şirket ve banka hesabı seçimi zorunludur.");
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        using (var upd = c.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE bank_accounts SET is_active=$active,updated_at=$now,updated_by=$user WHERE id=$id AND company_id=$company";
            Add(upd, "$active", isActive ? 1 : 0); Add(upd, "$now", now); Add(upd, "$user", userName); Add(upd, "$id", bankAccountId); Add(upd, "$company", companyId);
            if (upd.ExecuteNonQuery() != 1) throw new InvalidOperationException("Banka hesabı bulunamadı.");
        }
        Audit(c, tx, companyId, "BankAccount", bankAccountId, isActive ? "BankAccountActivated" : "BankAccountDeactivated", isActive ? "Active" : "Passive", now);
        tx.Commit();
    }

    public BankAccountDetail? GetDetail(string companyId, string bankAccountId)
    {
        var t = database.Query("""
            SELECT id, branch_id, code, name, bank_name, bank_branch_name, bank_branch_code, account_number, iban, currency_code, account_type, is_active, description
            FROM bank_accounts WHERE id=$id AND company_id=$company
            """, ("$id", bankAccountId), ("$company", companyId));
        if (t.Rows.Count == 0) return null;
        var r = t.Rows[0];
        var edit = new BankAccountEdit(
            r["id"].ToString()!, companyId, r["branch_id"].ToString()!, r["code"].ToString()!, r["name"].ToString()!,
            r["bank_name"].ToString()!, r["bank_branch_name"].ToString()!, r["bank_branch_code"].ToString()!, r["account_number"].ToString()!,
            r["iban"].ToString()!, r["currency_code"].ToString()!, r["account_type"].ToString()!, Convert.ToBoolean(r["is_active"]), r["description"].ToString()!);
        var balance = database.Query("SELECT total_in,total_out,balance FROM bank_balances WHERE company_id=$c AND bank_account_id=$a", ("$c", companyId), ("$a", bankAccountId));
        return balance.Rows.Count == 0
            ? new BankAccountDetail(edit, 0, 0, 0)
            : new BankAccountDetail(edit, Convert.ToDecimal(balance.Rows[0]["total_in"]), Convert.ToDecimal(balance.Rows[0]["total_out"]), Convert.ToDecimal(balance.Rows[0]["balance"]));
    }

    public decimal GetBalance(string companyId, string bankAccountId)
    {
        var t = database.Query("SELECT balance FROM bank_balances WHERE company_id=$c AND bank_account_id=$a", ("$c", companyId), ("$a", bankAccountId));
        return t.Rows.Count == 0 ? 0m : Convert.ToDecimal(t.Rows[0]["balance"]);
    }

    public string PostBankIn(string companyId, string branchId, string bankAccountId, string transactionType, string? accountId,
        DateTime date, decimal amount, string currencyCode, decimal exchangeRate, string? documentNumber, string description, string userName,
        string? chequeId = null)
    {
        if (!BankInTypes.Contains(transactionType)) throw new ArgumentException("Geçersiz banka girişi işlem tipi.");
        if (transactionType == "CustomerReceipt" && string.IsNullOrWhiteSpace(accountId)) throw new ArgumentException("Müşteri tahsilatında cari seçimi zorunludur.");
        return Post(companyId, branchId, bankAccountId, transactionType, "In", accountId, date, amount, currencyCode, exchangeRate, documentNumber, description, userName, chequeId);
    }

    public string PostBankOut(string companyId, string branchId, string bankAccountId, string transactionType, string? accountId,
        DateTime date, decimal amount, string currencyCode, decimal exchangeRate, string? documentNumber, string description, string userName,
        string? chequeId = null)
    {
        if (!BankOutTypes.Contains(transactionType)) throw new ArgumentException("Geçersiz banka çıkışı işlem tipi.");
        if (transactionType == "SupplierPayment" && string.IsNullOrWhiteSpace(accountId)) throw new ArgumentException("Tedarikçi ödemesinde cari seçimi zorunludur.");
        return Post(companyId, branchId, bankAccountId, transactionType, "Out", accountId, date, amount, currencyCode, exchangeRate, documentNumber, description, userName, chequeId);
    }

    private string Post(string companyId, string branchId, string bankAccountId, string transactionType, string direction, string? accountId,
        DateTime date, decimal amount, string currencyCode, decimal exchangeRate, string? documentNumber, string description, string userName, string? chequeId)
    {
        if (amount <= 0) throw new ArgumentException("Tutar 0'dan büyük olmalıdır.");
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Açıklama zorunludur.");
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();

        var bank = ReadBankAccount(c, tx, bankAccountId, companyId) ?? throw new ArgumentException("Banka hesabı bulunamadı.");
        if (!bank.IsActive) throw new ArgumentException("Pasif banka hesabına hareket girilemez.");
        if (bank.CurrencyCode != currencyCode) throw new ArgumentException($"Hesap para birimi ({bank.CurrencyCode}) ile işlem para birimi ({currencyCode}) eşleşmiyor.");

        var id = Guid.NewGuid().ToString();
        string? accountTransactionId = null;
        if (transactionType is "CustomerReceipt" or "SupplierPayment")
            accountTransactionId = PostAccountSide(c, tx, companyId, branchId, accountId!, transactionType, amount, currencyCode, exchangeRate, documentNumber, description, date, now);

        InsertBankTransaction(c, tx, id: id, companyId: companyId, branchId: branchId, bankAccountId: bankAccountId, transactionType: transactionType,
            direction: direction, amount: amount, currencyCode: currencyCode, exchangeRate: exchangeRate, localAmount: amount * exchangeRate, date: date,
            documentNumber: documentNumber, accountId: accountId, accountTransactionId: accountTransactionId, chequeId: chequeId,
            referenceTransactionId: null, targetBankAccountId: null, targetCashAccountId: null, description: description, userName: userName, now: now);
        UpdateBankBalance(c, tx, companyId, bankAccountId, currencyCode, direction == "In" ? amount : 0, direction == "Out" ? amount : 0, date, now);

        var action = transactionType == "OpeningBalance" ? "OpeningBalancePosted" : direction == "In" ? "BankInPosted" : "BankOutPosted";
        Audit(c, tx, companyId, "BankTransaction", id, action, description, now);
        tx.Commit();
        return id;
    }

    private static string PostAccountSide(SqliteConnection c, SqliteTransaction tx, string companyId, string branchId, string accountId,
        string transactionType, decimal amount, string currencyCode, decimal exchangeRate, string? documentNumber, string description, DateTime date, string now)
    {
        var accountTransactionId = Guid.NewGuid().ToString();
        var debit = transactionType == "SupplierPayment" ? amount : 0;
        var credit = transactionType == "CustomerReceipt" ? amount : 0;
        using (var at = c.CreateCommand())
        {
            at.Transaction = tx;
            at.CommandText = "INSERT INTO account_transactions(id,company_id,branch_id,account_id,transaction_type,debit,credit,currency_code,exchange_rate,document_type,document_no,description,transaction_at,created_at) VALUES($id,$c,$b,$a,$type,$debit,$credit,$currency,$rate,'BankTransaction',$doc,$desc,$at,$now)";
            Add(at, "$id", accountTransactionId); Add(at, "$c", companyId); Add(at, "$b", branchId); Add(at, "$a", accountId); Add(at, "$type", transactionType);
            Add(at, "$debit", debit); Add(at, "$credit", credit); Add(at, "$currency", currencyCode); Add(at, "$rate", exchangeRate);
            Add(at, "$doc", documentNumber ?? ""); Add(at, "$desc", description); Add(at, "$at", date.ToString("O")); Add(at, "$now", now);
            at.ExecuteNonQuery();
        }
        using (var ab = c.CreateCommand())
        {
            ab.Transaction = tx;
            ab.CommandText = "INSERT INTO account_balances(company_id,account_id,debit,credit,balance,updated_at) VALUES($c,$a,$d,$cr,$net,$now) ON CONFLICT(company_id,account_id) DO UPDATE SET debit=debit+$d,credit=credit+$cr,balance=balance+$net,updated_at=$now";
            Add(ab, "$c", companyId); Add(ab, "$a", accountId); Add(ab, "$d", debit); Add(ab, "$cr", credit); Add(ab, "$net", debit - credit); Add(ab, "$now", now);
            ab.ExecuteNonQuery();
        }
        return accountTransactionId;
    }

    public (string SourceTransactionId, string TargetTransactionId) Transfer(string companyId, string branchId, string sourceBankAccountId,
        string targetBankAccountId, DateTime date, decimal amount, string description, string userName)
    {
        if (sourceBankAccountId == targetBankAccountId) throw new ArgumentException("Kaynak ve hedef banka hesabı aynı olamaz.");
        if (amount <= 0) throw new ArgumentException("Tutar 0'dan büyük olmalıdır.");
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();

        var source = ReadBankAccount(c, tx, sourceBankAccountId, companyId) ?? throw new ArgumentException("Kaynak banka hesabı bulunamadı.");
        var target = ReadBankAccount(c, tx, targetBankAccountId, companyId) ?? throw new ArgumentException("Hedef banka hesabı bulunamadı.");
        if (!source.IsActive || !target.IsActive) throw new ArgumentException("Pasif hesaba/hesaptan transfer yapılamaz.");
        if (source.CurrencyCode != target.CurrencyCode) throw new ArgumentException("Farklı para birimli hesaplar arasında transfer bu sürümde desteklenmiyor.");

        var sourceId = Guid.NewGuid().ToString();
        var targetId = Guid.NewGuid().ToString();
        InsertBankTransaction(c, tx, id: sourceId, companyId: companyId, branchId: branchId, bankAccountId: sourceBankAccountId, transactionType: "BankTransferOut",
            direction: "Out", amount: amount, currencyCode: source.CurrencyCode, exchangeRate: 1, localAmount: amount, date: date, documentNumber: null,
            accountId: null, accountTransactionId: null, chequeId: null, referenceTransactionId: targetId, targetBankAccountId: targetBankAccountId, targetCashAccountId: null,
            description: description, userName: userName, now: now);
        InsertBankTransaction(c, tx, id: targetId, companyId: companyId, branchId: branchId, bankAccountId: targetBankAccountId, transactionType: "BankTransferIn",
            direction: "In", amount: amount, currencyCode: target.CurrencyCode, exchangeRate: 1, localAmount: amount, date: date, documentNumber: null,
            accountId: null, accountTransactionId: null, chequeId: null, referenceTransactionId: sourceId, targetBankAccountId: null, targetCashAccountId: null,
            description: description, userName: userName, now: now);

        UpdateBankBalance(c, tx, companyId, sourceBankAccountId, source.CurrencyCode, 0, amount, date, now);
        UpdateBankBalance(c, tx, companyId, targetBankAccountId, target.CurrencyCode, amount, 0, date, now);
        Audit(c, tx, companyId, "BankTransaction", sourceId, "BankTransferPosted", description, now);
        tx.Commit();
        return (sourceId, targetId);
    }

    /// <summary>Kasadan bankaya / bankadan kasaya - the other everyday transfer, alongside bank-to-
    /// bank above. Writes both a cash_transactions row and a bank_transactions row in one connection/
    /// transaction, same atomicity pattern as PostAccountSide.</summary>
    public (string CashTransactionId, string BankTransactionId) TransferWithCash(string companyId, string branchId, string cashAccountId,
        string bankAccountId, bool cashToBank, DateTime date, decimal amount, string description, string userName)
    {
        if (amount <= 0) throw new ArgumentException("Tutar 0'dan büyük olmalıdır.");
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();

        var bank = ReadBankAccount(c, tx, bankAccountId, companyId) ?? throw new ArgumentException("Banka hesabı bulunamadı.");
        if (!bank.IsActive) throw new ArgumentException("Pasif banka hesabına/hesabından transfer yapılamaz.");

        string cashCurrency, cashType, bankType, cashDirection, bankDirection;
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx; cmd.CommandText = "SELECT currency_code, is_active FROM cash_accounts WHERE id=$id AND company_id=$company";
            Add(cmd, "$id", cashAccountId); Add(cmd, "$company", companyId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new ArgumentException("Kasa bulunamadı.");
            cashCurrency = r.GetString(0);
            if (!Convert.ToBoolean(r.GetValue(1))) throw new ArgumentException("Pasif kasaya/kasadan transfer yapılamaz.");
        }
        if (cashCurrency != bank.CurrencyCode) throw new ArgumentException("Farklı para birimli kasa ve banka hesabı arasında transfer bu sürümde desteklenmiyor.");

        (cashType, bankType, cashDirection, bankDirection) = cashToBank ? ("CashToBankOut", "CashToBankIn", "Out", "In") : ("BankToCashIn", "BankToCashOut", "In", "Out");

        if (cashDirection == "Out")
        {
            using var bal = c.CreateCommand(); bal.Transaction = tx; bal.CommandText = "SELECT balance FROM cash_balances WHERE company_id=$c AND cash_account_id=$a";
            Add(bal, "$c", companyId); Add(bal, "$a", cashAccountId);
            var current = bal.ExecuteScalar(); var currentBalance = current == null ? 0m : Convert.ToDecimal(current);
            if (currentBalance - amount < 0) throw new ArgumentException($"Kasa bakiyesi yetersiz.\n\nMevcut bakiye: {currentBalance:N2}\nİşlem tutarı: {amount:N2}");
        }
        if (bankDirection == "Out")
        {
            var currentBalance = ReadBankBalance(c, tx, companyId, bankAccountId);
            if (currentBalance - amount < 0) throw new ArgumentException($"Banka hesabı bakiyesi yetersiz.\n\nMevcut bakiye: {currentBalance:N2}\nİşlem tutarı: {amount:N2}");
        }

        var cashTxId = Guid.NewGuid().ToString();
        var bankTxId = Guid.NewGuid().ToString();
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO cash_transactions(id,company_id,branch_id,cash_account_id,transaction_type,direction,amount,currency_code,exchange_rate,local_amount,transaction_date,description,status,created_at,created_by,posted_at,posted_by,bank_transaction_id)
                VALUES($id,$c,$b,$cash,$type,$dir,$amount,$currency,1,$amount,$date,$desc,'Posted',$now,$user,$now,$user,$bankTx)
                """;
            Add(cmd, "$id", cashTxId); Add(cmd, "$c", companyId); Add(cmd, "$b", branchId); Add(cmd, "$cash", cashAccountId); Add(cmd, "$type", cashType);
            Add(cmd, "$dir", cashDirection); Add(cmd, "$amount", amount); Add(cmd, "$currency", cashCurrency); Add(cmd, "$date", date.ToString("O"));
            Add(cmd, "$desc", description); Add(cmd, "$now", now); Add(cmd, "$user", userName); Add(cmd, "$bankTx", bankTxId);
            cmd.ExecuteNonQuery();
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO cash_balances(company_id,cash_account_id,currency_code,total_in,total_out,balance,last_transaction_at,updated_at) VALUES($c,$a,$cur,$in,$out,$net,$txat,$now) ON CONFLICT(company_id,cash_account_id) DO UPDATE SET total_in=total_in+$in,total_out=total_out+$out,balance=balance+$net,last_transaction_at=$txat,updated_at=$now";
            Add(cmd, "$c", companyId); Add(cmd, "$a", cashAccountId); Add(cmd, "$cur", cashCurrency); Add(cmd, "$in", cashDirection == "In" ? amount : 0);
            Add(cmd, "$out", cashDirection == "Out" ? amount : 0); Add(cmd, "$net", cashDirection == "In" ? amount : -amount); Add(cmd, "$txat", date.ToString("O")); Add(cmd, "$now", now);
            cmd.ExecuteNonQuery();
        }
        InsertBankTransaction(c, tx, id: bankTxId, companyId: companyId, branchId: branchId, bankAccountId: bankAccountId, transactionType: bankType,
            direction: bankDirection, amount: amount, currencyCode: bank.CurrencyCode, exchangeRate: 1, localAmount: amount, date: date, documentNumber: null,
            accountId: null, accountTransactionId: null, chequeId: null, referenceTransactionId: cashTxId, targetBankAccountId: null, targetCashAccountId: cashAccountId,
            description: description, userName: userName, now: now);
        UpdateBankBalance(c, tx, companyId, bankAccountId, bank.CurrencyCode, bankDirection == "In" ? amount : 0, bankDirection == "Out" ? amount : 0, date, now);

        Audit(c, tx, companyId, "BankTransaction", bankTxId, "CashBankTransferPosted", description, now);
        tx.Commit();
        return (cashTxId, bankTxId);
    }

    public DataTable GetTransactions(string companyId, string? bankAccountId = null, DateTime? from = null, DateTime? to = null,
        string? transactionType = null, string? direction = null, string? status = null) => database.Query("""
        SELECT bt.transaction_date AS Tarih, ba.name AS Hesap, bt.transaction_type AS IslemTipi, COALESCE(bt.document_number,'') AS Belge,
               COALESCE(a.name,'') AS Cari, bt.description AS Aciklama,
               CASE WHEN bt.direction='In' THEN bt.amount ELSE 0 END AS Giris,
               CASE WHEN bt.direction='Out' THEN bt.amount ELSE 0 END AS Cikis,
               bt.currency_code AS Doviz, bt.exchange_rate AS Kur, bt.created_by AS Kullanici, bt.status AS Durum, bt.id AS Id
        FROM bank_transactions bt
        JOIN bank_accounts ba ON ba.id=bt.bank_account_id
        LEFT JOIN accounts a ON a.id=bt.account_id
        WHERE bt.company_id=$company
          AND ($account='' OR bt.bank_account_id=$account)
          AND ($from='' OR bt.transaction_date>=$from)
          AND ($to='' OR bt.transaction_date<=$to)
          AND ($type='' OR bt.transaction_type=$type)
          AND ($dir='' OR bt.direction=$dir)
          AND ($status='' OR bt.status=$status)
        ORDER BY bt.transaction_date DESC, bt.created_at DESC
        """, ("$company", companyId), ("$account", bankAccountId ?? ""), ("$from", from?.ToString("O") ?? ""), ("$to", to?.ToString("O") ?? ""),
        ("$type", transactionType ?? ""), ("$dir", direction ?? ""), ("$status", status ?? ""));

    public (decimal OpeningBalance, DataTable Lines) GetStatement(string companyId, string bankAccountId, DateTime? from = null, DateTime? to = null)
    {
        var openingBalance = from is null ? 0m : Convert.ToDecimal(database.Query("""
            SELECT COALESCE(SUM(CASE WHEN direction='In' THEN amount ELSE -amount END),0) AS Devir
            FROM bank_transactions WHERE company_id=$c AND bank_account_id=$a AND transaction_date<$from
            """, ("$c", companyId), ("$a", bankAccountId), ("$from", from.Value.ToString("O"))).Rows[0]["Devir"]);

        var lines = database.Query("""
            SELECT transaction_date AS Tarih, COALESCE(document_number,'') AS Belge, transaction_type AS IslemTipi, description AS Aciklama,
                   CASE WHEN direction='In' THEN amount ELSE 0 END AS Giris,
                   CASE WHEN direction='Out' THEN amount ELSE 0 END AS Cikis,
                   $opening + SUM(CASE WHEN direction='In' THEN amount ELSE -amount END) OVER (ORDER BY transaction_date,created_at,id) AS Bakiye
            FROM bank_transactions
            WHERE company_id=$c AND bank_account_id=$a AND ($from='' OR transaction_date>=$from) AND ($to='' OR transaction_date<=$to)
            ORDER BY transaction_date,created_at,id
            """, ("$c", companyId), ("$a", bankAccountId), ("$from", from?.ToString("O") ?? ""), ("$to", to?.ToString("O") ?? ""), ("$opening", openingBalance));

        return (openingBalance, lines);
    }

    public Task RebuildBalanceAsync(string companyId, string? bankAccountId = null)
    {
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        var rows = new List<(string Id, string Currency)>();
        using (var accounts = c.CreateCommand())
        {
            accounts.Transaction = tx;
            accounts.CommandText = bankAccountId == null ? "SELECT id, currency_code FROM bank_accounts WHERE company_id=$c" : "SELECT id, currency_code FROM bank_accounts WHERE company_id=$c AND id=$a";
            Add(accounts, "$c", companyId); if (bankAccountId != null) Add(accounts, "$a", bankAccountId);
            using var r = accounts.ExecuteReader();
            while (r.Read()) rows.Add((r.GetString(0), r.GetString(1)));
        }
        var now = DateTime.UtcNow.ToString("O");
        foreach (var (id, currency) in rows)
        {
            decimal totalIn, totalOut; string? last;
            using (var sum = c.CreateCommand())
            {
                sum.Transaction = tx;
                sum.CommandText = "SELECT COALESCE(SUM(CASE WHEN direction='In' THEN amount ELSE 0 END),0), COALESCE(SUM(CASE WHEN direction='Out' THEN amount ELSE 0 END),0), MAX(transaction_date) FROM bank_transactions WHERE company_id=$c AND bank_account_id=$a";
                Add(sum, "$c", companyId); Add(sum, "$a", id);
                using var reader = sum.ExecuteReader();
                reader.Read();
                totalIn = Convert.ToDecimal(reader.GetValue(0)); totalOut = Convert.ToDecimal(reader.GetValue(1)); last = reader.IsDBNull(2) ? null : reader.GetString(2);
            }
            using var upsert = c.CreateCommand();
            upsert.Transaction = tx;
            upsert.CommandText = "INSERT INTO bank_balances(company_id,bank_account_id,currency_code,total_in,total_out,balance,last_transaction_at,updated_at) VALUES($c,$a,$cur,$in,$out,$bal,$last,$now) ON CONFLICT(company_id,bank_account_id) DO UPDATE SET currency_code=$cur,total_in=$in,total_out=$out,balance=$bal,last_transaction_at=$last,updated_at=$now";
            Add(upsert, "$c", companyId); Add(upsert, "$a", id); Add(upsert, "$cur", currency); Add(upsert, "$in", totalIn); Add(upsert, "$out", totalOut);
            Add(upsert, "$bal", totalIn - totalOut); AddNullable(upsert, "$last", last); Add(upsert, "$now", now);
            upsert.ExecuteNonQuery();
        }
        tx.Commit();
        return Task.CompletedTask;
    }

    private sealed record BankAccountRow(string CurrencyCode, bool IsActive);

    private static BankAccountRow? ReadBankAccount(SqliteConnection c, SqliteTransaction tx, string bankAccountId, string companyId)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT currency_code, is_active FROM bank_accounts WHERE id=$id AND company_id=$company";
        Add(cmd, "$id", bankAccountId); Add(cmd, "$company", companyId);
        using var r = cmd.ExecuteReader();
        return !r.Read() ? null : new BankAccountRow(r.GetString(0), Convert.ToBoolean(r.GetValue(1)));
    }

    private static decimal ReadBankBalance(SqliteConnection c, SqliteTransaction tx, string companyId, string bankAccountId)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT balance FROM bank_balances WHERE company_id=$c AND bank_account_id=$a";
        Add(cmd, "$c", companyId); Add(cmd, "$a", bankAccountId);
        var result = cmd.ExecuteScalar();
        return result == null ? 0m : Convert.ToDecimal(result);
    }

    private static void InsertBankTransaction(SqliteConnection c, SqliteTransaction tx, string id, string companyId, string branchId, string bankAccountId,
        string transactionType, string direction, decimal amount, string currencyCode, decimal exchangeRate, decimal localAmount, DateTime date,
        string? documentNumber, string? accountId, string? accountTransactionId, string? chequeId, string? referenceTransactionId,
        string? targetBankAccountId, string? targetCashAccountId, string description, string userName, string now, string status = "Posted")
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO bank_transactions(id,company_id,branch_id,bank_account_id,transaction_type,direction,amount,currency_code,exchange_rate,local_amount,transaction_date,document_number,account_id,account_transaction_id,cheque_id,reference_transaction_id,target_bank_account_id,target_cash_account_id,description,status,created_at,created_by,posted_at,posted_by)
            VALUES($id,$company,$branch,$bankAccount,$type,$direction,$amount,$currency,$rate,$local,$date,$dn,$account,$accountTx,$cheque,$ref,$targetBank,$targetCash,$description,$status,$now,$user,$now,$user)
            """;
        Add(cmd, "$id", id); Add(cmd, "$company", companyId); Add(cmd, "$branch", branchId); Add(cmd, "$bankAccount", bankAccountId);
        Add(cmd, "$type", transactionType); Add(cmd, "$direction", direction); Add(cmd, "$amount", amount); Add(cmd, "$currency", currencyCode);
        Add(cmd, "$rate", exchangeRate); Add(cmd, "$local", localAmount); Add(cmd, "$date", date.ToString("O"));
        AddNullable(cmd, "$dn", documentNumber); AddNullable(cmd, "$account", accountId); AddNullable(cmd, "$accountTx", accountTransactionId);
        AddNullable(cmd, "$cheque", chequeId); AddNullable(cmd, "$ref", referenceTransactionId); AddNullable(cmd, "$targetBank", targetBankAccountId);
        AddNullable(cmd, "$targetCash", targetCashAccountId); Add(cmd, "$description", description); Add(cmd, "$status", status); Add(cmd, "$now", now); Add(cmd, "$user", userName);
        cmd.ExecuteNonQuery();
    }

    private static void UpdateBankBalance(SqliteConnection c, SqliteTransaction tx, string companyId, string bankAccountId, string currencyCode,
        decimal inAmount, decimal outAmount, DateTime transactionDate, string now)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO bank_balances(company_id,bank_account_id,currency_code,total_in,total_out,balance,last_transaction_at,updated_at)
            VALUES($c,$a,$currency,$in,$out,$net,$txat,$now)
            ON CONFLICT(company_id,bank_account_id) DO UPDATE SET total_in=total_in+$in,total_out=total_out+$out,balance=balance+$net,last_transaction_at=$txat,updated_at=$now
            """;
        Add(cmd, "$c", companyId); Add(cmd, "$a", bankAccountId); Add(cmd, "$currency", currencyCode); Add(cmd, "$in", inAmount); Add(cmd, "$out", outAmount);
        Add(cmd, "$net", inAmount - outAmount); Add(cmd, "$txat", transactionDate.ToString("O")); Add(cmd, "$now", now);
        cmd.ExecuteNonQuery();
    }

    private static void Audit(SqliteConnection c, SqliteTransaction tx, string companyId, string entityType, string entityId, string action, string detail, string now)
    {
        using var audit = c.CreateCommand(); audit.Transaction = tx;
        audit.CommandText = "INSERT INTO audit_logs(id,company_id,entity_type,entity_id,action,new_values,created_at) VALUES($id,$c,$type,$entity,$action,$new,$now)";
        Add(audit, "$id", Guid.NewGuid().ToString()); Add(audit, "$c", companyId); Add(audit, "$type", entityType); Add(audit, "$entity", entityId);
        Add(audit, "$action", action); Add(audit, "$new", detail); Add(audit, "$now", now);
        audit.ExecuteNonQuery();
    }

    private static void Add(SqliteCommand c, string n, object v) => c.Parameters.AddWithValue(n, v);
    private static void AddNullable(SqliteCommand c, string n, string? v) => c.Parameters.AddWithValue(n, (object?)v ?? DBNull.Value);
}
