using R3.Infrastructure;

namespace R3.Domain.Tests;

/// <summary>End-to-end coverage for the Banka/Çek-Senet engines added 2026-09-23 - runs against a
/// throwaway SQLite file per test (same pattern as StoreDatabaseTests), never the shared app
/// database, so it is safe to run alongside a live, populated instance of the app.</summary>
public sealed class BankAndChequeServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";
    private StoreDatabase Create() => new(Path.Combine(_folder, "test.db"));
    private static string Branch(StoreDatabase db) => db.Query("SELECT id FROM branches WHERE company_id=$c LIMIT 1", ("$c", Company)).Rows[0][0].ToString()!;

    [Fact]
    public async Task BankAccountLifecycle_DepositTransferAndCashBridgeKeepBalancesConsistent()
    {
        var db = Create(); var branch = Branch(db); var bank = new LocalBankService(db);
        bank.Save(new BankAccountEdit("", Company, branch, "B01", "Ana Hesap", "Ziraat", "Merkez", "001", "12345", "TR330006100519786457841326"), "test");
        bank.Save(new BankAccountEdit("", Company, branch, "B02", "İkinci Hesap"), "test");
        var b01 = db.Query("SELECT id FROM bank_accounts WHERE code='B01'").Rows[0][0].ToString()!;
        var b02 = db.Query("SELECT id FROM bank_accounts WHERE code='B02'").Rows[0][0].ToString()!;

        bank.PostBankIn(Company, branch, b01, "OpeningBalance", null, DateTime.Today, 10_000m, "TRY", 1, null, "Açılış", "test");
        Assert.Equal(10_000m, bank.GetBalance(Company, b01));

        bank.Transfer(Company, branch, b01, b02, DateTime.Today, 4_000m, "Hesaplar arası", "test");
        Assert.Equal(6_000m, bank.GetBalance(Company, b01));
        Assert.Equal(4_000m, bank.GetBalance(Company, b02));

        var cash = new LocalCashService(db);
        cash.Save(new CashAccountEdit("", Company, branch, "K01", "Ana Kasa"), "test");
        var k01 = db.Query("SELECT id FROM cash_accounts WHERE code='K01'").Rows[0][0].ToString()!;

        bank.TransferWithCash(Company, branch, k01, b01, cashToBank: false, DateTime.Today, 1_500m, "Bankadan kasaya", "test");
        Assert.Equal(4_500m, bank.GetBalance(Company, b01));
        Assert.Equal(1_500m, cash.GetBalance(Company, k01));

        bank.TransferWithCash(Company, branch, k01, b01, cashToBank: true, DateTime.Today, 500m, "Kasadan bankaya", "test");
        Assert.Equal(5_000m, bank.GetBalance(Company, b01));
        Assert.Equal(1_000m, cash.GetBalance(Company, k01));

        // Balance projection is rebuildable from the transaction ledger, same guarantee as Kasa.
        await bank.RebuildBalanceAsync(Company);
        Assert.Equal(5_000m, bank.GetBalance(Company, b01));
    }

    [Fact]
    public void BankTransfer_InsufficientBalanceOrInactiveAccountIsRejected()
    {
        var db = Create(); var branch = Branch(db); var bank = new LocalBankService(db);
        bank.Save(new BankAccountEdit("", Company, branch, "B01", "Ana Hesap"), "test");
        bank.Save(new BankAccountEdit("", Company, branch, "B02", "İkinci Hesap"), "test");
        var b01 = db.Query("SELECT id FROM bank_accounts WHERE code='B01'").Rows[0][0].ToString()!;
        var b02 = db.Query("SELECT id FROM bank_accounts WHERE code='B02'").Rows[0][0].ToString()!;
        Assert.Throws<ArgumentException>(() => bank.Transfer(Company, branch, b01, b02, DateTime.Today, 100m, "Yetersiz bakiye", "test"));

        bank.PostBankIn(Company, branch, b01, "OpeningBalance", null, DateTime.Today, 100m, "TRY", 1, null, "Açılış", "test");
        bank.SetActive(Company, b02, false, "test");
        Assert.Throws<ArgumentException>(() => bank.Transfer(Company, branch, b01, b02, DateTime.Today, 50m, "Pasif hesap", "test"));
    }

    [Fact]
    public void ReceivedCheque_DepositThenCollectPostsBankIncomeAndClosesPortfolio()
    {
        var db = Create(); var branch = Branch(db);
        var bank = new LocalBankService(db); var cheques = new LocalChequeService(db);
        bank.Save(new BankAccountEdit("", Company, branch, "B01", "Ana Hesap"), "test");
        var b01 = db.Query("SELECT id FROM bank_accounts WHERE code='B01'").Rows[0][0].ToString()!;

        var chequeId = cheques.Receive(new ChequeEdit("", Company, branch, "Cheque", "Received", null, 2_500m, "TRY",
            DateTime.Today.AddDays(15), DateTime.Today, "001234", "Ahmet Yılmaz", "Garanti", "Kadıköy", "9988"), "test");

        var portfolio = cheques.Search(Company, status: "Portfolio");
        Assert.Single(portfolio.Rows.Cast<System.Data.DataRow>());

        cheques.DepositForCollection(Company, chequeId, b01, "test");
        Assert.Equal(0m, bank.GetBalance(Company, b01)); // deposit alone must not move money yet

        cheques.Collect(Company, chequeId, DateTime.Today, "test");
        Assert.Equal(2_500m, bank.GetBalance(Company, b01));

        var status = db.Query("SELECT status FROM cheques WHERE id=$id", ("$id", chequeId)).Rows[0][0].ToString();
        Assert.Equal("Collected", status);

        var history = cheques.History(Company, chequeId);
        Assert.Equal(3, history.Rows.Count); // (created)->Portfolio, Portfolio->DepositedForCollection, ->Collected
    }

    [Fact]
    public void ReceivedCheque_BounceRequiresPriorDepositAndPostsNoMoney()
    {
        var db = Create(); var branch = Branch(db);
        var bank = new LocalBankService(db); var cheques = new LocalChequeService(db);
        bank.Save(new BankAccountEdit("", Company, branch, "B01", "Ana Hesap"), "test");
        var b01 = db.Query("SELECT id FROM bank_accounts WHERE code='B01'").Rows[0][0].ToString()!;
        var chequeId = cheques.Receive(new ChequeEdit("", Company, branch, "Cheque", "Received", null, 1_000m, "TRY",
            DateTime.Today.AddDays(10), null, "001235", "Borçlu A.Ş.", "", "", ""), "test");

        Assert.Throws<ArgumentException>(() => cheques.Bounce(Company, chequeId, "test")); // must be deposited first
        cheques.DepositForCollection(Company, chequeId, b01, "test");
        cheques.Bounce(Company, chequeId, "test", "Karşılıksız çıktı");
        Assert.Equal(0m, bank.GetBalance(Company, b01));
        Assert.Equal("Bounced", db.Query("SELECT status FROM cheques WHERE id=$id", ("$id", chequeId)).Rows[0][0].ToString());
    }

    [Fact]
    public void GivenCheque_PayFromCashDebitsCashAndClosesPortfolio()
    {
        var db = Create(); var branch = Branch(db);
        var cash = new LocalCashService(db); var cheques = new LocalChequeService(db);
        cash.Save(new CashAccountEdit("", Company, branch, "K01", "Ana Kasa"), "test");
        var k01 = db.Query("SELECT id FROM cash_accounts WHERE code='K01'").Rows[0][0].ToString()!;
        cash.PostCashIn(Company, branch, k01, "OpeningBalance", null, DateTime.Today, 5_000m, "TRY", 1, null, "Açılış", "test");

        var chequeId = cheques.Give(new ChequeEdit("", Company, branch, "PromissoryNote", "Given", null, 1_200m, "TRY",
            DateTime.Today.AddDays(5), DateTime.Today, "S-001", "R3 Demo Firma", "", "", ""), "test");

        Assert.Throws<ArgumentException>(() => cheques.Pay(Company, chequeId, k01, "notNull", DateTime.Today, "test")); // both supplied
        Assert.Throws<ArgumentException>(() => cheques.Pay(Company, chequeId, null, null, DateTime.Today, "test")); // neither supplied

        cheques.Pay(Company, chequeId, k01, null, DateTime.Today, "test");
        Assert.Equal(3_800m, cash.GetBalance(Company, k01));
        Assert.Equal("Paid", db.Query("SELECT status FROM cheques WHERE id=$id", ("$id", chequeId)).Rows[0][0].ToString());
        Assert.Throws<ArgumentException>(() => cheques.Pay(Company, chequeId, k01, null, DateTime.Today, "test")); // already paid
    }

    public void Dispose() { try { Directory.Delete(_folder, recursive: true); } catch { } }
}
