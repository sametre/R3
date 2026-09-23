using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record CashAccountEdit(
    string Id, string CompanyId, string BranchId, string Code, string Name, string CurrencyCode = "TRY",
    string CashAccountType = "MainCash", string? GroupId = null, bool IsActive = true, bool AllowNegativeBalance = false,
    string Description = "");

public sealed record CashAccountDetail(CashAccountEdit Account, decimal TotalIn, decimal TotalOut, decimal Balance);

/// <summary>
/// Canonical Cash Ledger Engine. Source of truth is <c>cash_transactions</c> - <c>cash_balances</c>
/// is a projection rebuildable via <see cref="RebuildBalanceAsync"/>, never written to directly
/// outside <see cref="UpdateCashBalance"/>. CustomerReceipt/SupplierPayment also write
/// account_transactions/account_balances in the SAME connection/transaction (the codebase's
/// established atomicity pattern - see LocalSalesService, which writes account_transactions
/// directly rather than calling LocalAccountService across a second connection/transaction).
/// </summary>
public sealed class LocalCashService(StoreDatabase database)
{
    private static readonly string[] ValidCashAccountTypes = ["MainCash", "BranchCash", "StoreCash", "POSCash", "ForeignCurrencyCash", "PettyCash", "Other"];
    // ChequeCollection/ChequePayment are posted by LocalChequeService.CollectToCash/Pay when a
    // received/given çek-senet clears through a cash account instead of a bank account.
    private static readonly string[] CashInTypes = ["OpeningBalance", "CustomerReceipt", "CashIncome", "ManualIn", "ChequeCollection"];
    private static readonly string[] CashOutTypes = ["SupplierPayment", "CashExpense", "ManualOut", "ChequePayment"];

    public StoreDatabase Database => database;

    public DataTable Search(string companyId, string? branchId = null, string? search = null, bool activeOnly = false)
    {
        var q = $"%{search?.Trim() ?? ""}%";
        return database.Query("""
            SELECT ca.id AS Id, ca.code AS Kod, ca.name AS Ad, b.name AS Sube, ca.cash_account_type AS Tip, ca.currency_code AS ParaBirimi,
                   COALESCE(cb.total_in,0) AS Giris, COALESCE(cb.total_out,0) AS Cikis, COALESCE(cb.balance,0) AS Bakiye,
                   ca.is_active AS Aktif, ca.allow_negative_balance AS NegatifIzinli
            FROM cash_accounts ca
            JOIN branches b ON b.id=ca.branch_id
            LEFT JOIN cash_balances cb ON cb.company_id=ca.company_id AND cb.cash_account_id=ca.id
            WHERE ca.company_id=$company AND ($branch='' OR ca.branch_id=$branch)
              AND ($q='' OR ca.code LIKE $q OR ca.name LIKE $q) AND ($active=0 OR ca.is_active=1)
            ORDER BY ca.code
            """, ("$company", companyId), ("$branch", branchId ?? ""), ("$q", q), ("$active", activeOnly ? 1 : 0));
    }

    public DataTable Lookup(string companyId, string? branchId = null) => database.Query("""
        SELECT id AS Id, code AS Code, name AS Name, currency_code AS CurrencyCode
        FROM cash_accounts
        WHERE company_id=$company AND is_active=1 AND ($branch='' OR branch_id=$branch)
        ORDER BY code
        """, ("$company", companyId), ("$branch", branchId ?? ""));

    public void Save(CashAccountEdit edit, string userName)
    {
        if (string.IsNullOrWhiteSpace(edit.CompanyId) || string.IsNullOrWhiteSpace(edit.BranchId) || string.IsNullOrWhiteSpace(edit.Code) || string.IsNullOrWhiteSpace(edit.Name))
            throw new ArgumentException("Şirket, şube, kasa kodu ve kasa adı zorunludur.");
        if (!ValidCashAccountTypes.Contains(edit.CashAccountType)) throw new ArgumentException("Geçersiz kasa tipi.");
        if (string.IsNullOrWhiteSpace(edit.CurrencyCode)) throw new ArgumentException("Para birimi zorunludur.");

        var id = string.IsNullOrWhiteSpace(edit.Id) ? Guid.NewGuid().ToString() : edit.Id;
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();

        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO cash_accounts(id,company_id,branch_id,code,name,currency_code,cash_account_type,group_id,is_active,allow_negative_balance,description,created_at,created_by,updated_at,updated_by)
                VALUES($id,$company,$branch,$code,$name,$currency,$type,$group,$active,$negative,$description,$now,$user,$now,$user)
                ON CONFLICT(id) DO UPDATE SET branch_id=$branch,code=$code,name=$name,currency_code=$currency,cash_account_type=$type,group_id=$group,
                    is_active=$active,allow_negative_balance=$negative,description=$description,updated_at=$now,updated_by=$user
                """;
            Add(cmd, "$id", id); Add(cmd, "$company", edit.CompanyId); Add(cmd, "$branch", edit.BranchId); Add(cmd, "$code", edit.Code.Trim().ToUpperInvariant());
            Add(cmd, "$name", edit.Name.Trim()); Add(cmd, "$currency", edit.CurrencyCode); Add(cmd, "$type", edit.CashAccountType);
            AddNullable(cmd, "$group", edit.GroupId); Add(cmd, "$active", edit.IsActive ? 1 : 0); Add(cmd, "$negative", edit.AllowNegativeBalance ? 1 : 0);
            Add(cmd, "$description", edit.Description); Add(cmd, "$now", now); Add(cmd, "$user", userName);
            cmd.ExecuteNonQuery();
        }

        Audit(c, tx, edit.CompanyId, "CashAccount", id, string.IsNullOrWhiteSpace(edit.Id) ? "CashAccountCreated" : "CashAccountUpdated", edit.Code, now);
        tx.Commit();
    }

    public void SetActive(string companyId, string cashAccountId, bool isActive, string userName)
    {
        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(cashAccountId)) throw new ArgumentException("Şirket ve kasa seçimi zorunludur.");
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        using (var upd = c.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE cash_accounts SET is_active=$active,updated_at=$now,updated_by=$user WHERE id=$id AND company_id=$company";
            Add(upd, "$active", isActive ? 1 : 0); Add(upd, "$now", now); Add(upd, "$user", userName); Add(upd, "$id", cashAccountId); Add(upd, "$company", companyId);
            if (upd.ExecuteNonQuery() != 1) throw new InvalidOperationException("Kasa kaydı bulunamadı.");
        }
        Audit(c, tx, companyId, "CashAccount", cashAccountId, isActive ? "CashAccountActivated" : "CashAccountDeactivated", isActive ? "Active" : "Passive", now);
        tx.Commit();
    }

    public CashAccountDetail? GetDetail(string companyId, string cashAccountId)
    {
        var t = database.Query("""
            SELECT id, branch_id, code, name, currency_code, cash_account_type, group_id, is_active, allow_negative_balance, description
            FROM cash_accounts WHERE id=$id AND company_id=$company
            """, ("$id", cashAccountId), ("$company", companyId));
        if (t.Rows.Count == 0) return null;
        var r = t.Rows[0];
        var edit = new CashAccountEdit(
            r["id"].ToString()!, companyId, r["branch_id"].ToString()!, r["code"].ToString()!, r["name"].ToString()!,
            r["currency_code"].ToString()!, r["cash_account_type"].ToString()!, r["group_id"] is DBNull ? null : r["group_id"].ToString(),
            Convert.ToBoolean(r["is_active"]), Convert.ToBoolean(r["allow_negative_balance"]), r["description"].ToString()!);
        var balance = database.Query("SELECT total_in,total_out,balance FROM cash_balances WHERE company_id=$c AND cash_account_id=$a", ("$c", companyId), ("$a", cashAccountId));
        return balance.Rows.Count == 0
            ? new CashAccountDetail(edit, 0, 0, 0)
            : new CashAccountDetail(edit, Convert.ToDecimal(balance.Rows[0]["total_in"]), Convert.ToDecimal(balance.Rows[0]["total_out"]), Convert.ToDecimal(balance.Rows[0]["balance"]));
    }

    public decimal GetBalance(string companyId, string cashAccountId)
    {
        var t = database.Query("SELECT balance FROM cash_balances WHERE company_id=$c AND cash_account_id=$a", ("$c", companyId), ("$a", cashAccountId));
        return t.Rows.Count == 0 ? 0m : Convert.ToDecimal(t.Rows[0]["balance"]);
    }

    public string PostCashIn(string companyId, string branchId, string cashAccountId, string transactionType, string? accountId,
        DateTime date, decimal amount, string currencyCode, decimal exchangeRate, string? documentNumber, string description, string userName,
        string? documentType = null, string? documentId = null)
    {
        if (!CashInTypes.Contains(transactionType)) throw new ArgumentException("Geçersiz nakit giriş işlem tipi.");
        if (transactionType == "CustomerReceipt" && string.IsNullOrWhiteSpace(accountId)) throw new ArgumentException("Müşteri tahsilatında cari seçimi zorunludur.");
        return Post(companyId, branchId, cashAccountId, transactionType, "In", accountId, date, amount, currencyCode, exchangeRate, documentType, documentId, documentNumber, description, userName);
    }

    public string PostCashOut(string companyId, string branchId, string cashAccountId, string transactionType, string? accountId,
        DateTime date, decimal amount, string currencyCode, decimal exchangeRate, string? documentNumber, string description, string userName,
        string? documentType = null, string? documentId = null)
    {
        if (!CashOutTypes.Contains(transactionType)) throw new ArgumentException("Geçersiz nakit çıkış işlem tipi.");
        if (transactionType == "SupplierPayment" && string.IsNullOrWhiteSpace(accountId)) throw new ArgumentException("Tedarikçi ödemesinde cari seçimi zorunludur.");
        return Post(companyId, branchId, cashAccountId, transactionType, "Out", accountId, date, amount, currencyCode, exchangeRate, documentType, documentId, documentNumber, description, userName);
    }

    private string Post(string companyId, string branchId, string cashAccountId, string transactionType, string direction, string? accountId,
        DateTime date, decimal amount, string currencyCode, decimal exchangeRate, string? documentType, string? documentId, string? documentNumber,
        string description, string userName)
    {
        if (amount <= 0) throw new ArgumentException("Tutar 0'dan büyük olmalıdır.");
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Açıklama zorunludur.");
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();

        var cash = ReadCashAccount(c, tx, cashAccountId, companyId) ?? throw new ArgumentException("Kasa bulunamadı.");
        if (!cash.IsActive) throw new ArgumentException("Pasif kasaya hareket girilemez.");
        if (cash.CurrencyCode != currencyCode) throw new ArgumentException($"Kasa para birimi ({cash.CurrencyCode}) ile işlem para birimi ({currencyCode}) eşleşmiyor.");

        if (!string.IsNullOrWhiteSpace(documentId))
        {
            using var dup = c.CreateCommand(); dup.Transaction = tx;
            dup.CommandText = "SELECT COUNT(1) FROM cash_transactions WHERE document_type=$dt AND document_id=$di AND transaction_type=$tt";
            Add(dup, "$dt", documentType ?? ""); Add(dup, "$di", documentId); Add(dup, "$tt", transactionType);
            if (Convert.ToInt32(dup.ExecuteScalar()) > 0) throw new ArgumentException("Bu belge için işlem zaten kaydedilmiş.");
        }

        if (direction == "Out" && !cash.AllowNegativeBalance)
        {
            var currentBalance = ReadCashBalance(c, tx, companyId, cashAccountId);
            if (currentBalance - amount < 0) throw new ArgumentException($"Kasa bakiyesi yetersiz.\n\nMevcut bakiye: {currentBalance:N2}\nİşlem tutarı: {amount:N2}");
        }

        var id = Guid.NewGuid().ToString();
        string? accountTransactionId = null;
        if (transactionType is "CustomerReceipt" or "SupplierPayment")
            accountTransactionId = PostAccountSide(c, tx, companyId, branchId, accountId!, transactionType, amount, currencyCode, exchangeRate, documentNumber, description, date, now);

        InsertCashTransaction(c, tx, id: id, companyId: companyId, branchId: branchId, cashAccountId: cashAccountId, transactionType: transactionType,
            direction: direction, amount: amount, currencyCode: currencyCode, exchangeRate: exchangeRate, localAmount: amount * exchangeRate, date: date,
            documentType: documentType, documentId: documentId, documentNumber: documentNumber, accountId: accountId, accountTransactionId: accountTransactionId,
            referenceTransactionId: null, targetCashAccountId: null, description: description, userName: userName, now: now);
        UpdateCashBalance(c, tx, companyId, cashAccountId, currencyCode, direction == "In" ? amount : 0, direction == "Out" ? amount : 0, date, now);

        var action = transactionType == "OpeningBalance" ? "OpeningBalancePosted" : direction == "In" ? "CashInPosted" : "CashOutPosted";
        Audit(c, tx, companyId, "CashTransaction", id, action, description, now);
        tx.Commit();
        return id;
    }

    /// <summary>Writes the cari side of a CustomerReceipt/SupplierPayment in the SAME transaction as
    /// the cash side. Credit/debit direction is copied verbatim from LocalAccountService.PostReceipt
    /// (credit=amount) / PostPayment (debit=amount) - not re-derived.</summary>
    private static string PostAccountSide(SqliteConnection c, SqliteTransaction tx, string companyId, string branchId, string accountId,
        string transactionType, decimal amount, string currencyCode, decimal exchangeRate, string? documentNumber, string description, DateTime date, string now)
    {
        var accountTransactionId = Guid.NewGuid().ToString();
        var debit = transactionType == "SupplierPayment" ? amount : 0;
        var credit = transactionType == "CustomerReceipt" ? amount : 0;
        using (var at = c.CreateCommand())
        {
            at.Transaction = tx;
            at.CommandText = "INSERT INTO account_transactions(id,company_id,branch_id,account_id,transaction_type,debit,credit,currency_code,exchange_rate,document_type,document_no,description,transaction_at,created_at) VALUES($id,$c,$b,$a,$type,$debit,$credit,$currency,$rate,'CashTransaction',$doc,$desc,$at,$now)";
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

    public (string SourceTransactionId, string TargetTransactionId) Transfer(string companyId, string branchId, string sourceCashAccountId,
        string targetCashAccountId, DateTime date, decimal amount, string description, string userName)
    {
        if (sourceCashAccountId == targetCashAccountId) throw new ArgumentException("Kaynak ve hedef kasa aynı olamaz.");
        if (amount <= 0) throw new ArgumentException("Tutar 0'dan büyük olmalıdır.");
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();

        var source = ReadCashAccount(c, tx, sourceCashAccountId, companyId) ?? throw new ArgumentException("Kaynak kasa bulunamadı.");
        var target = ReadCashAccount(c, tx, targetCashAccountId, companyId) ?? throw new ArgumentException("Hedef kasa bulunamadı.");
        if (!source.IsActive || !target.IsActive) throw new ArgumentException("Pasif kasaya/kasadan transfer yapılamaz.");
        if (source.CurrencyCode != target.CurrencyCode) throw new ArgumentException("Farklı para birimli kasalar arasında transfer bu sürümde desteklenmiyor.");

        if (!source.AllowNegativeBalance)
        {
            var sourceBalance = ReadCashBalance(c, tx, companyId, sourceCashAccountId);
            if (sourceBalance - amount < 0) throw new ArgumentException($"Kasa bakiyesi yetersiz.\n\nMevcut bakiye: {sourceBalance:N2}\nİşlem tutarı: {amount:N2}");
        }

        var sourceId = Guid.NewGuid().ToString();
        var targetId = Guid.NewGuid().ToString();
        InsertCashTransaction(c, tx, id: sourceId, companyId: companyId, branchId: branchId, cashAccountId: sourceCashAccountId, transactionType: "CashTransferOut",
            direction: "Out", amount: amount, currencyCode: source.CurrencyCode, exchangeRate: 1, localAmount: amount, date: date, documentType: "CashTransfer",
            documentId: null, documentNumber: null, accountId: null, accountTransactionId: null, referenceTransactionId: targetId, targetCashAccountId: targetCashAccountId,
            description: description, userName: userName, now: now);
        InsertCashTransaction(c, tx, id: targetId, companyId: companyId, branchId: branchId, cashAccountId: targetCashAccountId, transactionType: "CashTransferIn",
            direction: "In", amount: amount, currencyCode: target.CurrencyCode, exchangeRate: 1, localAmount: amount, date: date, documentType: "CashTransfer",
            documentId: null, documentNumber: null, accountId: null, accountTransactionId: null, referenceTransactionId: sourceId, targetCashAccountId: null,
            description: description, userName: userName, now: now);

        UpdateCashBalance(c, tx, companyId, sourceCashAccountId, source.CurrencyCode, 0, amount, date, now);
        UpdateCashBalance(c, tx, companyId, targetCashAccountId, target.CurrencyCode, amount, 0, date, now);
        Audit(c, tx, companyId, "CashTransaction", sourceId, "CashTransferPosted", description, now);
        tx.Commit();
        return (sourceId, targetId);
    }

    public string Reverse(string companyId, string cashTransactionId, string userName, string? reason = null)
    {
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();

        string branchId, cashAccountId, originalType, originalDirection, currencyCode, description, status; decimal amount, exchangeRate; string? accountId;
        using (var read = c.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT branch_id,cash_account_id,transaction_type,direction,amount,currency_code,exchange_rate,account_id,description,status FROM cash_transactions WHERE id=$id AND company_id=$company";
            Add(read, "$id", cashTransactionId); Add(read, "$company", companyId);
            using var r = read.ExecuteReader();
            if (!r.Read()) throw new ArgumentException("Kasa hareketi bulunamadı.");
            branchId = r.GetString(0); cashAccountId = r.GetString(1); originalType = r.GetString(2); originalDirection = r.GetString(3);
            amount = Convert.ToDecimal(r.GetValue(4)); currencyCode = r.GetString(5); exchangeRate = Convert.ToDecimal(r.GetValue(6));
            accountId = r.IsDBNull(7) ? null : r.GetString(7); description = r.GetString(8); status = r.GetString(9);
        }
        if (status == "Reversed") throw new ArgumentException("Bu kasa hareketi zaten iptal edilmiş.");
        if (originalType == "Cancellation") throw new ArgumentException("İptal kayıtları tekrar iptal edilemez.");

        var reversalDirection = originalDirection == "In" ? "Out" : "In";
        var cash = ReadCashAccount(c, tx, cashAccountId, companyId)!;
        if (reversalDirection == "Out" && !cash.AllowNegativeBalance)
        {
            var currentBalance = ReadCashBalance(c, tx, companyId, cashAccountId);
            if (currentBalance - amount < 0) throw new ArgumentException($"Kasa bakiyesi yetersiz.\n\nMevcut bakiye: {currentBalance:N2}\nİşlem tutarı: {amount:N2}");
        }

        var reversalDescription = reason ?? $"İptal: {description}";
        var reversalId = Guid.NewGuid().ToString();
        InsertCashTransaction(c, tx, id: reversalId, companyId: companyId, branchId: branchId, cashAccountId: cashAccountId, transactionType: "Cancellation",
            direction: reversalDirection, amount: amount, currencyCode: currencyCode, exchangeRate: exchangeRate, localAmount: amount * exchangeRate, date: DateTime.UtcNow,
            documentType: "CashTransactionReversal", documentId: null, documentNumber: null, accountId: accountId, accountTransactionId: null,
            referenceTransactionId: cashTransactionId, targetCashAccountId: null, description: reversalDescription, userName: userName, now: now);
        UpdateCashBalance(c, tx, companyId, cashAccountId, currencyCode, reversalDirection == "In" ? amount : 0, reversalDirection == "Out" ? amount : 0, DateTime.UtcNow, now);

        if (accountId != null && originalType is "CustomerReceipt" or "SupplierPayment")
        {
            // Undo the original credit/debit with the opposite entry.
            var debit = originalType == "CustomerReceipt" ? amount : 0;
            var credit = originalType == "SupplierPayment" ? amount : 0;
            using (var at = c.CreateCommand())
            {
                at.Transaction = tx;
                at.CommandText = "INSERT INTO account_transactions(id,company_id,branch_id,account_id,transaction_type,debit,credit,currency_code,exchange_rate,document_type,document_no,description,transaction_at,created_at) VALUES($id,$c,$b,$a,'Cancellation',$debit,$credit,$currency,$rate,'CashTransactionReversal',$doc,$desc,$at,$now)";
                Add(at, "$id", Guid.NewGuid().ToString()); Add(at, "$c", companyId); Add(at, "$b", branchId); Add(at, "$a", accountId);
                Add(at, "$debit", debit); Add(at, "$credit", credit); Add(at, "$currency", currencyCode); Add(at, "$rate", exchangeRate);
                Add(at, "$doc", cashTransactionId); Add(at, "$desc", reversalDescription); Add(at, "$at", now); Add(at, "$now", now);
                at.ExecuteNonQuery();
            }
            using (var ab = c.CreateCommand())
            {
                ab.Transaction = tx;
                ab.CommandText = "INSERT INTO account_balances(company_id,account_id,debit,credit,balance,updated_at) VALUES($c,$a,$d,$cr,$net,$now) ON CONFLICT(company_id,account_id) DO UPDATE SET debit=debit+$d,credit=credit+$cr,balance=balance+$net,updated_at=$now";
                Add(ab, "$c", companyId); Add(ab, "$a", accountId); Add(ab, "$d", debit); Add(ab, "$cr", credit); Add(ab, "$net", debit - credit); Add(ab, "$now", now);
                ab.ExecuteNonQuery();
            }
        }

        using (var upd = c.CreateCommand()) { upd.Transaction = tx; upd.CommandText = "UPDATE cash_transactions SET status='Reversed' WHERE id=$id"; Add(upd, "$id", cashTransactionId); upd.ExecuteNonQuery(); }
        Audit(c, tx, companyId, "CashTransaction", cashTransactionId, "CashTransactionReversed", reversalDescription, now);
        tx.Commit();
        return reversalId;
    }

    public DataTable GetTransactions(string companyId, string? cashAccountId = null, DateTime? from = null, DateTime? to = null,
        string? transactionType = null, string? direction = null, string? currencyCode = null, string? status = null) => database.Query("""
        SELECT ct.transaction_date AS Tarih, ca.name AS Kasa, ct.transaction_type AS IslemTipi, COALESCE(ct.document_number,'') AS Belge,
               COALESCE(a.name,'') AS Cari, ct.description AS Aciklama,
               CASE WHEN ct.direction='In' THEN ct.amount ELSE 0 END AS Giris,
               CASE WHEN ct.direction='Out' THEN ct.amount ELSE 0 END AS Cikis,
               ct.currency_code AS Doviz, ct.exchange_rate AS Kur, ct.created_by AS Kullanici, ct.status AS Durum, ct.id AS Id,
               COALESCE(ct.account_id,'') AS CariId, COALESCE(ct.document_type,'') AS BelgeTipi, COALESCE(ct.document_id,'') AS BelgeId,
               ct.cash_account_id AS KasaId, COALESCE((SELECT bt.bank_account_id FROM bank_transactions bt WHERE bt.id=ct.bank_transaction_id),'') AS BankaId
        FROM cash_transactions ct
        JOIN cash_accounts ca ON ca.id=ct.cash_account_id
        LEFT JOIN accounts a ON a.id=ct.account_id
        WHERE ct.company_id=$company
          AND ($account='' OR ct.cash_account_id=$account)
          AND ($from='' OR ct.transaction_date>=$from)
          AND ($to='' OR ct.transaction_date<=$to)
          AND ($type='' OR ct.transaction_type=$type)
          AND ($dir='' OR ct.direction=$dir)
          AND ($currency='' OR ct.currency_code=$currency)
          AND ($status='' OR ct.status=$status)
        ORDER BY ct.transaction_date DESC, ct.created_at DESC
        """, ("$company", companyId), ("$account", cashAccountId ?? ""), ("$from", from?.ToString("O") ?? ""), ("$to", to?.ToString("O") ?? ""),
        ("$type", transactionType ?? ""), ("$dir", direction ?? ""), ("$currency", currencyCode ?? ""), ("$status", status ?? ""));

    public (decimal OpeningBalance, DataTable Lines) GetStatement(string companyId, string cashAccountId, DateTime? from = null, DateTime? to = null)
    {
        var openingBalance = from is null ? 0m : Convert.ToDecimal(database.Query("""
            SELECT COALESCE(SUM(CASE WHEN direction='In' THEN amount ELSE -amount END),0) AS Devir
            FROM cash_transactions WHERE company_id=$c AND cash_account_id=$a AND transaction_date<$from
            """, ("$c", companyId), ("$a", cashAccountId), ("$from", from.Value.ToString("O"))).Rows[0]["Devir"]);

        var lines = database.Query("""
            SELECT transaction_date AS Tarih, COALESCE(document_number,'') AS Belge, transaction_type AS IslemTipi, description AS Aciklama,
                   CASE WHEN direction='In' THEN amount ELSE 0 END AS Giris,
                   CASE WHEN direction='Out' THEN amount ELSE 0 END AS Cikis,
                   $opening + SUM(CASE WHEN direction='In' THEN amount ELSE -amount END) OVER (ORDER BY transaction_date,created_at,id) AS Bakiye,
                   COALESCE(account_id,'') AS CariId, COALESCE(document_type,'') AS BelgeTipi, COALESCE(document_id,'') AS BelgeId
            FROM cash_transactions
            WHERE company_id=$c AND cash_account_id=$a AND ($from='' OR transaction_date>=$from) AND ($to='' OR transaction_date<=$to)
            ORDER BY transaction_date,created_at,id
            """, ("$c", companyId), ("$a", cashAccountId), ("$from", from?.ToString("O") ?? ""), ("$to", to?.ToString("O") ?? ""), ("$opening", openingBalance));

        return (openingBalance, lines);
    }

    public Task RebuildBalanceAsync(string companyId, string? cashAccountId = null)
    {
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        var rows = new List<(string Id, string Currency)>();
        using (var accounts = c.CreateCommand())
        {
            accounts.Transaction = tx;
            accounts.CommandText = cashAccountId == null ? "SELECT id, currency_code FROM cash_accounts WHERE company_id=$c" : "SELECT id, currency_code FROM cash_accounts WHERE company_id=$c AND id=$a";
            Add(accounts, "$c", companyId); if (cashAccountId != null) Add(accounts, "$a", cashAccountId);
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
                sum.CommandText = "SELECT COALESCE(SUM(CASE WHEN direction='In' THEN amount ELSE 0 END),0), COALESCE(SUM(CASE WHEN direction='Out' THEN amount ELSE 0 END),0), MAX(transaction_date) FROM cash_transactions WHERE company_id=$c AND cash_account_id=$a";
                Add(sum, "$c", companyId); Add(sum, "$a", id);
                using var reader = sum.ExecuteReader();
                reader.Read();
                totalIn = Convert.ToDecimal(reader.GetValue(0)); totalOut = Convert.ToDecimal(reader.GetValue(1)); last = reader.IsDBNull(2) ? null : reader.GetString(2);
            }
            using var upsert = c.CreateCommand();
            upsert.Transaction = tx;
            upsert.CommandText = "INSERT INTO cash_balances(company_id,cash_account_id,currency_code,total_in,total_out,balance,last_transaction_at,updated_at) VALUES($c,$a,$cur,$in,$out,$bal,$last,$now) ON CONFLICT(company_id,cash_account_id) DO UPDATE SET currency_code=$cur,total_in=$in,total_out=$out,balance=$bal,last_transaction_at=$last,updated_at=$now";
            Add(upsert, "$c", companyId); Add(upsert, "$a", id); Add(upsert, "$cur", currency); Add(upsert, "$in", totalIn); Add(upsert, "$out", totalOut);
            Add(upsert, "$bal", totalIn - totalOut); AddNullable(upsert, "$last", last); Add(upsert, "$now", now);
            upsert.ExecuteNonQuery();
        }
        tx.Commit();
        return Task.CompletedTask;
    }

    private sealed record CashAccountRow(string CurrencyCode, bool IsActive, bool AllowNegativeBalance);

    private static CashAccountRow? ReadCashAccount(SqliteConnection c, SqliteTransaction tx, string cashAccountId, string companyId)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT currency_code, is_active, allow_negative_balance FROM cash_accounts WHERE id=$id AND company_id=$company";
        Add(cmd, "$id", cashAccountId); Add(cmd, "$company", companyId);
        using var r = cmd.ExecuteReader();
        return !r.Read() ? null : new CashAccountRow(r.GetString(0), Convert.ToBoolean(r.GetValue(1)), Convert.ToBoolean(r.GetValue(2)));
    }

    private static decimal ReadCashBalance(SqliteConnection c, SqliteTransaction tx, string companyId, string cashAccountId)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT balance FROM cash_balances WHERE company_id=$c AND cash_account_id=$a";
        Add(cmd, "$c", companyId); Add(cmd, "$a", cashAccountId);
        var result = cmd.ExecuteScalar();
        return result == null ? 0m : Convert.ToDecimal(result);
    }

    private static void InsertCashTransaction(SqliteConnection c, SqliteTransaction tx, string id, string companyId, string branchId, string cashAccountId,
        string transactionType, string direction, decimal amount, string currencyCode, decimal exchangeRate, decimal localAmount, DateTime date,
        string? documentType, string? documentId, string? documentNumber, string? accountId, string? accountTransactionId, string? referenceTransactionId,
        string? targetCashAccountId, string description, string userName, string now, string status = "Posted")
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO cash_transactions(id,company_id,branch_id,cash_account_id,transaction_type,direction,amount,currency_code,exchange_rate,local_amount,transaction_date,document_type,document_id,document_number,account_id,account_transaction_id,reference_transaction_id,target_cash_account_id,description,status,created_at,created_by,posted_at,posted_by)
            VALUES($id,$company,$branch,$cashAccount,$type,$direction,$amount,$currency,$rate,$local,$date,$dt,$di,$dn,$account,$accountTx,$ref,$target,$description,$status,$now,$user,$now,$user)
            """;
        Add(cmd, "$id", id); Add(cmd, "$company", companyId); Add(cmd, "$branch", branchId); Add(cmd, "$cashAccount", cashAccountId);
        Add(cmd, "$type", transactionType); Add(cmd, "$direction", direction); Add(cmd, "$amount", amount); Add(cmd, "$currency", currencyCode);
        Add(cmd, "$rate", exchangeRate); Add(cmd, "$local", localAmount); Add(cmd, "$date", date.ToString("O"));
        AddNullable(cmd, "$dt", documentType); AddNullable(cmd, "$di", documentId); AddNullable(cmd, "$dn", documentNumber);
        AddNullable(cmd, "$account", accountId); AddNullable(cmd, "$accountTx", accountTransactionId); AddNullable(cmd, "$ref", referenceTransactionId);
        AddNullable(cmd, "$target", targetCashAccountId); Add(cmd, "$description", description); Add(cmd, "$status", status); Add(cmd, "$now", now); Add(cmd, "$user", userName);
        cmd.ExecuteNonQuery();
    }

    private static void UpdateCashBalance(SqliteConnection c, SqliteTransaction tx, string companyId, string cashAccountId, string currencyCode,
        decimal inAmount, decimal outAmount, DateTime transactionDate, string now)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO cash_balances(company_id,cash_account_id,currency_code,total_in,total_out,balance,last_transaction_at,updated_at)
            VALUES($c,$a,$currency,$in,$out,$net,$txat,$now)
            ON CONFLICT(company_id,cash_account_id) DO UPDATE SET total_in=total_in+$in,total_out=total_out+$out,balance=balance+$net,last_transaction_at=$txat,updated_at=$now
            """;
        Add(cmd, "$c", companyId); Add(cmd, "$a", cashAccountId); Add(cmd, "$currency", currencyCode); Add(cmd, "$in", inAmount); Add(cmd, "$out", outAmount);
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
