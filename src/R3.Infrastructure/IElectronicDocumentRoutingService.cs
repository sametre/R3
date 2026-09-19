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
            "SELECT ei.is_einvoice_enabled,ei.einvoice_alias FROM account_einvoice_profiles ei JOIN accounts a ON a.id=ei.account_id WHERE ei.account_id=$id AND a.company_id=$c",
            ("$id", accountId), ("$c", companyId));
        // No profile row at all -> EArchive fallback (review §4). An enabled flag with no alias is a
        // data-quality state, not a real e-Fatura registration - there is nowhere to actually deliver
        // an EInvoice to, so it must not be routed as one (review §4 "EInvoice enabled ama alias yok").
        var registered = t.Rows.Count > 0 && Convert.ToBoolean(t.Rows[0]["is_einvoice_enabled"]) && !string.IsNullOrWhiteSpace(t.Rows[0]["einvoice_alias"] as string);
        return registered ? ElectronicDocumentType.EInvoice : ElectronicDocumentType.EArchiveInvoice;
    }
}
