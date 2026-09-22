using System.Data;
using R3.Application.Security;
using R3.Desktop.ContextActions;
using R3.Desktop.ViewModels;

namespace R3.Desktop.Tests;

public sealed class ContextActionFrameworkTests
{
    private static readonly Func<string, object?, Task<ContextActionResult>> Noop = (_, _) => Task.FromResult(ContextActionResult.Ok());

    [Fact]
    public void ActionIsVisibleWithPermissionAndHiddenWithoutPermission()
    {
        var action = FutureModuleContextActions.Shipments(Noop).Single(x => x.Id == "shipment.cancel"); var row = new ShipmentContextRow("1", "S1", "Cari", "Waiting");
        Assert.True(ContextActionEvaluator.Evaluate(action, row, 1, new Permissions("shipments.cancel")).Visible);
        Assert.False(ContextActionEvaluator.Evaluate(action, row, 1, new Permissions()).Visible);
    }

    [Fact]
    public void AccountRoleAndActiveStateControlFinancialActions()
    {
        var actions = StandardContextActions.Accounts(Nop, Nop, Nop, Nop, Nop, Nop, _ => Task.CompletedTask, Nop); var all = new Permissions("*");
        var customer = Account("Customer", true); var supplier = Account("Supplier", true); var both = Account("CustomerAndSupplier", true); var inactive = Account("CustomerAndSupplier", false);
        Assert.True(Available(actions, "accounts.receipt", customer, all)); Assert.False(Visible(actions, "accounts.payment", customer, all));
        Assert.True(Available(actions, "accounts.payment", supplier, all)); Assert.False(Visible(actions, "accounts.receipt", supplier, all));
        Assert.True(Available(actions, "accounts.receipt", both, all) && Available(actions, "accounts.payment", both, all));
        Assert.False(Available(actions, "accounts.receipt", inactive, all));
    }

    [Fact]
    public void ServiceProductHidesInventoryPostingAndInactiveCashDisablesPosting()
    {
        // Column names here must match LocalProductService's list query (product_type AS UrunTipi) -
        // this test previously used the wrong "ProductType" name, which matched the bug in
        // StandardContextActions.Products instead of catching it (both were wrong the same way).
        var products = StandardContextActions.Products(Nop, Nop, Nop, Nop, Nop, Nop, Nop, Nop); var table = new DataTable(); table.Columns.Add("Id"); table.Columns.Add("Kod"); table.Columns.Add("Ad"); table.Columns.Add("UrunTipi"); table.Columns.Add("Aktif", typeof(bool)); table.Rows.Add("1", "H1", "Hizmet", "Service", true);
        Assert.False(Visible(products, "inventory.receive", table.DefaultView[0], new Permissions("*")));
        var cash = StandardContextActions.Cash(Nop, Nop, Nop, Nop, Nop, Nop, Nop, _ => Task.CompletedTask);
        Assert.False(Available(cash, "cash.in", Cash(false), new Permissions("*")));
    }

    [Fact]
    public void DeliveredAndCancelledShipmentRulesAreSafe()
    {
        var actions = FutureModuleContextActions.Shipments(Noop); var permissions = new Permissions("*");
        var delivered = new ShipmentContextRow("1", "S1", "Cari", "Delivered"); var cancelled = delivered with { Status = "Cancelled" };
        Assert.False(Visible(actions, "shipment.cancel", delivered, permissions));
        Assert.False(Visible(actions, "shipment.pick", cancelled, permissions));
        Assert.True(Visible(actions, "shipment.open", cancelled, permissions)); Assert.True(Visible(actions, "shipment.audit", cancelled, permissions));
    }

    [Fact]
    public void CollectedInstrumentCannotBeCollectedTwiceAndDoubleClickUsesPrimaryOpen()
    {
        var actions = FutureModuleContextActions.Instruments(Noop); var row = new InstrumentContextRow("1", "C1", "Collected");
        Assert.False(Visible(actions, "instrument.collect", row, new Permissions("*")));
        Assert.Equal("instrument.open", ContextActionEvaluator.DefaultOpen(actions)!.Id);
    }

    private static bool Visible(IEnumerable<ContextActionDefinition> actions, string id, object row, IPermissionService p) => ContextActionEvaluator.Evaluate(actions.Single(x => x.Id == id), row, 1, p).Visible;
    private static bool Available(IEnumerable<ContextActionDefinition> actions, string id, object row, IPermissionService p) => ContextActionEvaluator.Evaluate(actions.Single(x => x.Id == id), row, 1, p).Enabled;
    private static void Nop() { }
    private static AccountRowViewModel Account(string type, bool active) => new("1", "C1", "Cari", type, "", "", "", "", "", 0, 0, 0, active, 0, 0, "");
    private static CashAccountRowViewModel Cash(bool active) => new("1", "K1", "Kasa", "Merkez", "MainCash", "TRY", 0, 0, 0, active, false);
    private sealed class Permissions(params string[] values) : IPermissionService
    {
        private readonly HashSet<string> _values = new(values, StringComparer.OrdinalIgnoreCase);
        public bool HasPermission(string code) => _values.Contains("*") || _values.Contains(code);
        public bool HasAnyPermission(params string[] codes) => codes.Any(HasPermission);
        public bool HasAllPermissions(params string[] codes) => codes.All(HasPermission);
        public void Refresh() { }
    }
}
