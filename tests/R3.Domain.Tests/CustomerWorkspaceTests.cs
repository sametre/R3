using R3.Infrastructure;

namespace R3.Domain.Tests;

public sealed class CustomerWorkspaceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "R3-customer-workspace-tests-" + Guid.NewGuid());
    private const string Company = "00000000-0000-0000-0000-000000000001";
    private readonly StoreDatabase _db;
    private readonly string _customer = Guid.NewGuid().ToString();
    public CustomerWorkspaceTests()
    {
        _db = new StoreDatabase(Path.Combine(_folder, "test.db"));
        new LocalAccountService(_db).Save(new(new AccountEdit(_customer, Company, "M001", "Test Müşteri"), new(), new(), null, null));
    }

    [Fact]
    public void EmptyCustomerHasEmptyGridsAndZeroBalances()
    {
        var result = new LocalCustomerWorkspaceService(_db).Load(Company, _customer, "TRY", DateTime.Today);
        Assert.Empty(result.Statement.Rows);
        Assert.Empty(result.Notes.Rows);
        Assert.Empty(result.PendingProducts.Rows);
        Assert.Empty(result.DeliveredProducts.Rows);
        Assert.Empty(result.Cheques.Rows);
        Assert.Equal(0, result.Balance);
        Assert.Equal(0, result.Overdue);
    }

    [Fact]
    public void LedgerBalancesRemainSeparatedByCurrencyAndCustomer()
    {
        var service = new LocalAccountService(_db);
        service.PostManualEntry(Company, "branch", _customer, "Debit", 150, "TRY", 1, new(2026, 1, 1), "1", "Borç");
        service.PostReceipt(Company, "branch", _customer, new(2026, 1, 2), 40, "TRY", 1, "Cash", "2", "Tahsilat");
        service.PostManualEntry(Company, "branch", _customer, "Debit", 25, "USD", 30, new(2026, 1, 3), "3", "Döviz");
        var other = Guid.NewGuid().ToString();
        service.Save(new(new AccountEdit(other, Company, "M002", "Diğer Müşteri"), new(), new(), null, null));
        service.PostManualEntry(Company, "branch", other, "Debit", 999, "TRY", 1, new(2026, 1, 1), "4", "Diğer");
        var workspace = new LocalCustomerWorkspaceService(_db);
        var result = workspace.Load(Company, _customer, "TRY", DateTime.Today);
        Assert.Equal(2, result.Statement.Rows.Count);
        Assert.Equal(150, result.Debit); Assert.Equal(40, result.Credit); Assert.Equal(110, result.Balance);
        Assert.Equal(150, Convert.ToDecimal(result.Statement.Rows[0]["Bakiye"]));
        Assert.Equal(110, Convert.ToDecimal(result.Statement.Rows[1]["Bakiye"]));
        Assert.Equal(25, workspace.Load(Company, _customer, "USD", DateTime.Today).Balance);
    }

    [Fact]
    public void OverdueExcludesTodayCancelledPlansAndOtherCurrencies()
    {
        void Plan(string id, string currency, string status, string due, decimal paid)
        {
            _db.Query("""
                INSERT INTO payment_plans(id,company_id,account_id,source_document_type,source_document_id,currency_code,due_date,status,created_at)
                VALUES($id,$c,$a,'Test',$id,$currency,$due,$status,'2026-01-01')
                """, ("$id", id), ("$c", Company), ("$a", _customer), ("$currency", currency), ("$due", due), ("$status", status));
            _db.Query("""
                INSERT INTO payment_plan_lines(id,payment_plan_id,installment_no,due_date,amount,paid_amount)
                VALUES($id,$id,1,$due,100,$paid)
                """, ("$id", id), ("$due", due), ("$paid", paid));
        }
        Plan("past", "TRY", "Open", "2026-01-01", 30);
        Plan("today", "TRY", "Open", "2026-02-01", 0);
        Plan("cancelled", "TRY", "Cancelled", "2026-01-01", 0);
        Plan("usd", "USD", "Open", "2026-01-01", 0);
        var result = new LocalCustomerWorkspaceService(_db).Load(Company, _customer, "TRY", new(2026, 2, 1));
        Assert.Equal(70, result.Overdue);
        Assert.Equal(2, result.Installments.Rows.Count);
    }

    [Fact]
    public void InstallmentCollectionMarksPaidLineAndPostsReceipt()
    {
        const string plan = "collection-plan";
        const string line = "collection-line";
        _db.Query("""
            INSERT INTO payment_plans(id,company_id,account_id,source_document_type,source_document_id,currency_code,due_date,status,created_at)
            VALUES($id,$company,$account,'Test',$id,'TRY','2026-01-10','Open','2026-01-01')
            """, ("$id", plan), ("$company", Company), ("$account", _customer));
        _db.Query("""
            INSERT INTO payment_plan_lines(id,payment_plan_id,installment_no,due_date,amount,paid_amount,status)
            VALUES($id,$plan,1,'2026-01-10',100,0,'Open')
            """, ("$id", line), ("$plan", plan));

        new LocalInstallmentCollectionService(_db).Collect(Company, "branch", _customer, "TRY", new(2026, 1, 12),
            new Dictionary<string, decimal> { [line] = 100 }, "Cash", "Test tahsilat");

        var result = new LocalCustomerWorkspaceService(_db).Load(Company, _customer, "TRY", DateTime.Today);
        Assert.Equal(1, result.Statement.Rows.Count);
        Assert.Equal(100, result.Credit);
        Assert.Equal("Paid", _db.Query("SELECT status FROM payment_plan_lines WHERE id=$id", ("$id", line)).Rows[0]["status"]);
        Assert.Equal("Closed", _db.Query("SELECT status FROM payment_plans WHERE id=$id", ("$id", plan)).Rows[0]["status"]);
    }

    [Fact]
    public void OpenChequesIncludeBankCollectionButExcludeSettledInstruments()
    {
        foreach (var status in new[] { "Portfolio", "DepositedForCollection", "Bounced", "Collected", "ReturnedToDrawer" })
            _db.Query("""
                INSERT INTO cheques(id,company_id,branch_id,instrument_type,direction,status,account_id,amount,due_date,created_at,updated_at)
                VALUES($id,$c,'branch','Cheque','Received',$id,$a,100,'2026-01-01','2026-01-01','2026-01-01')
                """, ("$id", status), ("$c", Company), ("$a", _customer));
        var result = new LocalCustomerWorkspaceService(_db).Load(Company, _customer, "TRY", DateTime.Today);
        Assert.Equal(3, result.Cheques.Rows.Count);
        Assert.Contains(result.Cheques.Rows.Cast<System.Data.DataRow>(), row => row["Durum"].ToString() == "Bankada");
    }

    [Fact]
    public void ForeignCompanyAndSupplierCannotBeOpenedAsCustomer()
    {
        var workspace = new LocalCustomerWorkspaceService(_db);
        Assert.Throws<ArgumentException>(() => workspace.Load("other-company", _customer, "TRY", DateTime.Today));
        var supplier = Guid.NewGuid().ToString();
        new LocalAccountService(_db).Save(new(new AccountEdit(supplier, Company, "S001", "Tedarikçi", "Supplier"), new(), new(), null, null));
        Assert.Throws<ArgumentException>(() => workspace.Load(Company, supplier, "TRY", DateTime.Today));
        Assert.DoesNotContain(workspace.Customers(Company).Rows.Cast<System.Data.DataRow>(), x => x["Id"].ToString() == supplier);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }
}
