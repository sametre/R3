namespace R3.Infrastructure;

// Spec §11-12: the user never picks e-Fatura vs e-Arşiv by hand; the recipient's registered
// e-invoice capability (account_einvoice_profiles.is_einvoice_enabled) decides the route.
public interface IElectronicDocumentRoutingService
{
    ElectronicDocumentType RouteOutgoingInvoice(string companyId, string accountId);
}

public sealed class ElectronicDocumentRoutingService(StoreDatabase database) : IElectronicDocumentRoutingService
{
    public ElectronicDocumentType RouteOutgoingInvoice(string companyId, string accountId)
    {
        var t = database.Query(
            "SELECT ei.is_einvoice_enabled FROM account_einvoice_profiles ei JOIN accounts a ON a.id=ei.account_id WHERE ei.account_id=$id AND a.company_id=$c",
            ("$id", accountId), ("$c", companyId));
        var registered = t.Rows.Count > 0 && Convert.ToBoolean(t.Rows[0][0]);
        return registered ? ElectronicDocumentType.EInvoice : ElectronicDocumentType.EArchiveInvoice;
    }
}
