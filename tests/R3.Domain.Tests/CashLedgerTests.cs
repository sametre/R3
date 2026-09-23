using Microsoft.Data.Sqlite;
using R3.Infrastructure;

namespace R3.Domain.Tests;

public sealed class CashLedgerTests : IDisposable
{
    private readonly string _folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "R3-cash-ledger-tests-" + Guid.NewGuid());
    private readonly string _company = "00000000-0000-0000-0000-000000000001";
    private string? _branch;

    private (StoreDatabase Db, LocalCashService Cash) Create()
    {
        var db = new StoreDatabase(System.IO.Path.Combine(_folder, "test.db"));
        _branch = db.Query("SELECT id FROM branches LIMIT 1").Rows[0][0].ToString()!;
        return (db, new LocalCashService(db));
    }

    private string SeedCashAccount(LocalCashService cash, string code = "KASA-01", bool allowNegative = false, string currency = "TRY")
    {
        cash.Save(new CashAccountEdit("", _company, _branch!, code, $"Kasa {code}", currency, "MainCash", AllowNegativeBalance: allowNegative), "test-user");
        return cash.Database.Query("SELECT id FROM cash_accounts WHERE company_id=$c AND code=$code", ("$c", _company), ("$code", code)).Rows[0]["id"].ToString()!;
    }

    private string SeedCustomerAccount(StoreDatabase db)
    {
        var id = Guid.NewGuid().ToString();
        db.Execute("INSERT INTO accounts(id,company_id,code,name,account_type,is_active,created_at,updated_at) VALUES($id,$c,'CUST01','Test Müşteri','Customer',1,$n,$n)",
            ("$id", id), ("$c", _company), ("$n", DateTime.UtcNow.ToString("O")));
        return id;
    }

    [Fact]
    public void CreateCashAccount_Persists()
    {
        var (db, cash) = Create();
        var id = SeedCashAccount(cash);
        var row = cash.Search(_company).Rows.Cast<System.Data.DataRow>().Single();
        Assert.Equal("KASA-01", row["Kod"]);
        Assert.Equal(id, row["Id"]);
    }

    [Fact]
    public void DuplicateCode_Rejected()
    {
        var (db, cash) = Create();
        SeedCashAccount(cash);
        Assert.Throws<SqliteException>(() => cash.Save(new CashAccountEdit("", _company, _branch!, "kasa-01", "Başka Kasa"), "test-user"));
    }

    [Fact]
    public void OpeningBalance_PostsAsInDirectionAndUpdatesBalance()
    {
        var (db, cash) = Create();
        var id = SeedCashAccount(cash);
        cash.PostCashIn(_company, _branch!, id, "OpeningBalance", null, DateTime.Today, 1000, "TRY", 1, null, "Açılış bakiyesi", "test-user");
        Assert.Equal(1000m, cash.GetBalance(_company, id));
    }

    [Fact]
    public void CashIn_IncreasesBalance()
    {
        var (db, cash) = Create();
        var id = SeedCashAccount(cash);
        cash.PostCashIn(_company, _branch!, id, "CashIncome", null, DateTime.Today, 500, "TRY", 1, null, "Diğer gelir", "test-user");
        Assert.Equal(500m, cash.GetBalance(_company, id));
    }

    [Fact]
    public void CashOut_DecreasesBalance()
    {
        var (db, cash) = Create();
        var id = SeedCashAccount(cash);
        cash.PostCashIn(_company, _branch!, id, "CashIncome", null, DateTime.Today, 1000, "TRY", 1, null, "Açılış", "test-user");
        cash.PostCashOut(_company, _branch!, id, "CashExpense", null, DateTime.Today, 300, "TRY", 1, null, "Masraf", "test-user");
        Assert.Equal(700m, cash.GetBalance(_company, id));
    }

    [Fact]
    public void NegativeBalance_BlockedByDefault()
    {
        var (db, cash) = Create();
        var id = SeedCashAccount(cash, allowNegative: false);
        cash.PostCashIn(_company, _branch!, id, "CashIncome", null, DateTime.Today, 500, "TRY", 1, null, "Açılış", "test-user");
        var ex = Assert.Throws<ArgumentException>(() => cash.PostCashOut(_company, _branch!, id, "CashExpense", null, DateTime.Today, 700, "TRY", 1, null, "Masraf", "test-user"));
        Assert.Contains("yetersiz", ex.Message);
        Assert.Equal(500m, cash.GetBalance(_company, id));
    }

    [Fact]
    public void NegativeBalance_AllowedWhenFlagSet()
    {
        var (db, cash) = Create();
        var id = SeedCashAccount(cash, allowNegative: true);
        cash.PostCashOut(_company, _branch!, id, "CashExpense", null, DateTime.Today, 700, "TRY", 1, null, "Masraf", "test-user");
        Assert.Equal(-700m, cash.GetBalance(_company, id));
    }

    [Fact]
    public void CustomerReceipt_PostsCashAndAccountAtomically()
    {
        var (db, cash) = Create();
        var accounts = new LocalAccountService(db);
        var cashId = SeedCashAccount(cash);
        var customerId = SeedCustomerAccount(db);
        db.Execute("INSERT INTO account_transactions(id,company_id,branch_id,account_id,transaction_type,debit,credit,currency_code,exchange_rate,description,transaction_at,created_at) VALUES($id,$c,$b,$a,'SalesInvoice',10000,0,'TRY',1,'Satış',$n,$n)",
            ("$id", Guid.NewGuid().ToString()), ("$c", _company), ("$b", _branch!), ("$a", customerId), ("$n", DateTime.UtcNow.ToString("O")));
        db.Execute("INSERT INTO account_balances(company_id,account_id,debit,credit,balance,updated_at) VALUES($c,$a,10000,0,10000,$n)", ("$c", _company), ("$a", customerId), ("$n", DateTime.UtcNow.ToString("O")));

        cash.PostCashIn(_company, _branch!, cashId, "CustomerReceipt", customerId, DateTime.Today, 3000, "TRY", 1, null, "Tahsilat", "test-user");

        Assert.Equal(3000m, cash.GetBalance(_company, cashId));
        Assert.Equal(7000m, accounts.GetBalance(_company, customerId));
    }

    // Right-click navigation on Cari Hareketler / Cari Ekstre / Kasa Hareketleri / Kasa Ekstresi / Risk
    // relies on these link columns: a cash receipt's account-ledger row must point back at its cash
    // account (via cash_transactions.account_transaction_id) and the cash row at its account.
    [Fact]
    public void CustomerReceipt_ReportRowsCarryNavigationLinks()
    {
        var (db, cash) = Create();
        var accounts = new LocalAccountService(db);
        var cashId = SeedCashAccount(cash);
        var customerId = SeedCustomerAccount(db);
        cash.PostCashIn(_company, _branch!, cashId, "CustomerReceipt", customerId, DateTime.Today, 500, "TRY", 1, null, "Tahsilat", "test-user");

        var ledger = accounts.RecentTransactions(_company, 10, customerId).Rows.Cast<System.Data.DataRow>().Single();
        Assert.Equal(customerId, ledger["CariId"]);
        Assert.Equal(cashId, ledger["KasaId"]);
        Assert.Equal("", ledger["BankaId"]);
        var statement = accounts.Statement(_company, customerId).Rows.Cast<System.Data.DataRow>().Single();
        Assert.Equal(cashId, statement["KasaId"]);

        var cashRow = cash.GetTransactions(_company, cashId).Rows.Cast<System.Data.DataRow>().Single();
        Assert.Equal(customerId, cashRow["CariId"]);
        Assert.Equal(cashId, cashRow["KasaId"]);
        Assert.Equal(customerId, cash.GetStatement(_company, cashId).Lines.Rows[0]["CariId"]);

        Assert.Equal(customerId, accounts.CreditRisk(_company).Rows.Cast<System.Data.DataRow>().Single(r => r["CariKodu"].ToString() == "CUST01")["CariId"]);
    }

    [Fact]
    public void CustomerReceipt_WithoutAccountId_Rejected()
    {
        var (db, cash) = Create();
        var id = SeedCashAccount(cash);
        Assert.Throws<ArgumentException>(() => cash.PostCashIn(_company, _branch!, id, "CustomerReceipt", null, DateTime.Today, 100, "TRY", 1, null, "Tahsilat", "test-user"));
    }

    [Fact]
    public void SupplierPayment_DebitsCashCreditsAccountCorrectly()
    {
        var (db, cash) = Create();
        var accounts = new LocalAccountService(db);
        var cashId = SeedCashAccount(cash, allowNegative: true);
        var supplierId = Guid.NewGuid().ToString();
        db.Execute("INSERT INTO accounts(id,company_id,code,name,account_type,is_active,created_at,updated_at) VALUES($id,$c,'SUP01','Test Tedarikçi','Supplier',1,$n,$n)",
            ("$id", supplierId), ("$c", _company), ("$n", DateTime.UtcNow.ToString("O")));
        db.Execute("INSERT INTO account_balances(company_id,account_id,debit,credit,balance,updated_at) VALUES($c,$a,0,5000,-5000,$n)", ("$c", _company), ("$a", supplierId), ("$n", DateTime.UtcNow.ToString("O")));

        cash.PostCashOut(_company, _branch!, cashId, "SupplierPayment", supplierId, DateTime.Today, 2000, "TRY", 1, null, "Ödeme", "test-user");

        Assert.Equal(-2000m, cash.GetBalance(_company, cashId));
        Assert.Equal(-3000m, accounts.GetBalance(_company, supplierId));
    }

    [Fact]
    public void Transfer_DecreasesSourceIncreasesTarget()
    {
        var (db, cash) = Create();
        var source = SeedCashAccount(cash, "KASA-A");
        var target = SeedCashAccount(cash, "KASA-B");
        cash.PostCashIn(_company, _branch!, source, "OpeningBalance", null, DateTime.Today, 1000, "TRY", 1, null, "Açılış", "test-user");

        cash.Transfer(_company, _branch!, source, target, DateTime.Today, 400, "Kasa transferi", "test-user");

        Assert.Equal(600m, cash.GetBalance(_company, source));
        Assert.Equal(400m, cash.GetBalance(_company, target));
    }

    [Fact]
    public void Transfer_SameSourceAndTarget_Rejected()
    {
        var (db, cash) = Create();
        var id = SeedCashAccount(cash);
        Assert.Throws<ArgumentException>(() => cash.Transfer(_company, _branch!, id, id, DateTime.Today, 100, "x", "test-user"));
    }

    [Fact]
    public void Transfer_InsufficientBalance_RollsBackBothSides()
    {
        var (db, cash) = Create();
        var source = SeedCashAccount(cash, "KASA-A");
        var target = SeedCashAccount(cash, "KASA-B");
        cash.PostCashIn(_company, _branch!, source, "OpeningBalance", null, DateTime.Today, 100, "TRY", 1, null, "Açılış", "test-user");

        Assert.Throws<ArgumentException>(() => cash.Transfer(_company, _branch!, source, target, DateTime.Today, 500, "x", "test-user"));

        Assert.Equal(100m, cash.GetBalance(_company, source));
        Assert.Equal(0m, cash.GetBalance(_company, target));
        Assert.Empty(db.Query("SELECT id FROM cash_transactions WHERE transaction_type='CashTransferOut'").Rows.Cast<System.Data.DataRow>());
    }

    [Fact]
    public void Transfer_DifferentCurrency_Rejected()
    {
        var (db, cash) = Create();
        var source = SeedCashAccount(cash, "KASA-TRY", currency: "TRY");
        var target = SeedCashAccount(cash, "KASA-USD", currency: "USD");
        var ex = Assert.Throws<ArgumentException>(() => cash.Transfer(_company, _branch!, source, target, DateTime.Today, 100, "x", "test-user"));
        Assert.Contains("desteklenmiyor", ex.Message);
    }

    [Fact]
    public void CashReversal_RestoresBalance()
    {
        var (db, cash) = Create();
        var id = SeedCashAccount(cash);
        var txId = cash.PostCashIn(_company, _branch!, id, "CashIncome", null, DateTime.Today, 500, "TRY", 1, null, "Gelir", "test-user");

        cash.Reverse(_company, txId, "test-user");

        Assert.Equal(0m, cash.GetBalance(_company, id));
        Assert.Equal("Reversed", db.Query("SELECT status FROM cash_transactions WHERE id=$id", ("$id", txId)).Rows[0]["status"]);
        Assert.Throws<ArgumentException>(() => cash.Reverse(_company, txId, "test-user"));
    }

    [Fact]
    public void ReceiptReversal_RestoresBothCashAndAccountBalance()
    {
        var (db, cash) = Create();
        var accounts = new LocalAccountService(db);
        var cashId = SeedCashAccount(cash);
        var customerId = SeedCustomerAccount(db);
        db.Execute("INSERT INTO account_balances(company_id,account_id,debit,credit,balance,updated_at) VALUES($c,$a,10000,0,10000,$n)", ("$c", _company), ("$a", customerId), ("$n", DateTime.UtcNow.ToString("O")));

        var txId = cash.PostCashIn(_company, _branch!, cashId, "CustomerReceipt", customerId, DateTime.Today, 3000, "TRY", 1, null, "Tahsilat", "test-user");
        cash.Reverse(_company, txId, "test-user");

        Assert.Equal(0m, cash.GetBalance(_company, cashId));
        Assert.Equal(10000m, accounts.GetBalance(_company, customerId));
    }

    [Fact]
    public void DuplicatePosting_ForSameDocument_Rejected()
    {
        var (db, cash) = Create();
        var id = SeedCashAccount(cash);
        var docId = Guid.NewGuid().ToString();
        cash.PostCashIn(_company, _branch!, id, "CashIncome", null, DateTime.Today, 100, "TRY", 1, "R-1", "Gelir", "test-user", documentType: "SalesReceipt", documentId: docId);

        var ex = Assert.Throws<ArgumentException>(() =>
            cash.PostCashIn(_company, _branch!, id, "CashIncome", null, DateTime.Today, 100, "TRY", 1, "R-1", "Gelir (mükerrer)", "test-user", documentType: "SalesReceipt", documentId: docId));

        Assert.Contains("zaten kaydedilmiş", ex.Message);
        Assert.Equal(100m, cash.GetBalance(_company, id));
    }

    [Fact]
    public async Task RebuildBalance_RecomputesFromTransactions()
    {
        var (db, cash) = Create();
        var id = SeedCashAccount(cash);
        cash.PostCashIn(_company, _branch!, id, "OpeningBalance", null, DateTime.Today, 1000, "TRY", 1, null, "Açılış", "test-user");
        cash.PostCashOut(_company, _branch!, id, "CashExpense", null, DateTime.Today, 200, "TRY", 1, null, "Masraf", "test-user");
        db.Execute("UPDATE cash_balances SET balance=999999 WHERE cash_account_id=$id", ("$id", id));

        await cash.RebuildBalanceAsync(_company, id);

        Assert.Equal(800m, cash.GetBalance(_company, id));
    }

    [Fact]
    public void Statement_ShowsOpeningBalanceBeforeRange()
    {
        var (db, cash) = Create();
        var id = SeedCashAccount(cash);
        cash.PostCashIn(_company, _branch!, id, "OpeningBalance", null, DateTime.Today.AddDays(-10), 1000, "TRY", 1, null, "Açılış", "test-user");
        cash.PostCashIn(_company, _branch!, id, "CashIncome", null, DateTime.Today, 200, "TRY", 1, null, "Gelir", "test-user");

        var (opening, lines) = cash.GetStatement(_company, id, from: DateTime.Today.AddDays(-1));

        Assert.Equal(1000m, opening);
        Assert.Single(lines.Rows.Cast<System.Data.DataRow>());
    }

    [Fact]
    public void Statement_RunningBalanceIncludesOpening()
    {
        var (db, cash) = Create();
        var id = SeedCashAccount(cash);
        cash.PostCashIn(_company, _branch!, id, "OpeningBalance", null, DateTime.Today.AddDays(-2), 1000, "TRY", 1, null, "Açılış", "test-user");
        cash.PostCashOut(_company, _branch!, id, "CashExpense", null, DateTime.Today.AddDays(-1), 300, "TRY", 1, null, "Masraf", "test-user");

        var (_, lines) = cash.GetStatement(_company, id);

        Assert.Equal(2, lines.Rows.Count);
        Assert.Equal(1000m, Convert.ToDecimal(lines.Rows[0]["Bakiye"]));
        Assert.Equal(700m, Convert.ToDecimal(lines.Rows[1]["Bakiye"]));
    }

    [Fact]
    public void InactiveCashAccount_PostingRejected()
    {
        var (db, cash) = Create();
        var id = SeedCashAccount(cash);
        cash.SetActive(_company, id, false, "test-user");
        Assert.Throws<ArgumentException>(() => cash.PostCashIn(_company, _branch!, id, "CashIncome", null, DateTime.Today, 100, "TRY", 1, null, "Gelir", "test-user"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }
}
