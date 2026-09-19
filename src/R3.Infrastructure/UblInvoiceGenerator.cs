using System.Globalization;
using System.Xml.Linq;

namespace R3.Infrastructure;

public sealed class UblGenerationException(string message) : Exception(message);

internal sealed record UblInvoice(string CompanyId, string BranchId, string AccountId, string DocumentNo, string CurrencyCode, decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal GrandTotal);
internal sealed record UblLine(int LineNo, decimal BaseQuantity, decimal UnitPrice, decimal DiscountAmount, decimal VatRate, decimal NetAmount, decimal VatAmount, string ProductCode, string ProductName, string UnitCode);
internal sealed record UblParty((string SchemeId, string Value)? PartyIdentification, string PartyName, string Alias, string Address, string City, string District, string PostalCode, string Country, string Phone, string Email, string InvoiceScenario);
internal sealed record UblInvoiceData(UblInvoice Invoice, IReadOnlyList<UblLine> Lines, UblParty Seller, UblParty Buyer);

// UN/CEFACT Recommendation 20 mapping for R3's actual unit codes - explicit and non-exhaustive
// (docs/architecture/UBL-ENGINE.md has the table and how to extend it). No silent fallback (spec §14):
// an unmapped unit is a generation error, not a guess.
internal static class UblUnitCodeMap
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ADET"] = "C62", ["KG"] = "KGM", ["GR"] = "GRM", ["LT"] = "LTR", ["LITRE"] = "LTR",
        ["MT"] = "MTR", ["METRE"] = "MTR", ["M2"] = "MTK", ["M3"] = "MTQ", ["KUTU"] = "BX", ["PAKET"] = "PK", ["ÇİFT"] = "PR", ["CIFT"] = "PR"
    };

    public static string Resolve(string unit) => Map.TryGetValue(unit, out var code) ? code
        : throw new UblGenerationException($"'{unit}' birimi için UBL UnitCode eşlemesi tanımlı değil.");
}

// Phase 6: real UBL 2.1 / UBL-TR structure built from R3's actual domain data - no placeholder/fake
// values for anything (spec §1/§6: report a gap instead of inventing data). Provider-independent
// (spec §3): this class has never heard of GİB, a specific entegratör, or an HTTP endpoint - it only
// turns R3 rows into a canonical UBL XML string, which the outbox/provider layer sends unchanged.
// Does NOT manage transactions or persist anything (spec §47) - ElectronicDocumentGenerationService does.
//
// Known, reported (not silently worked around) gaps as of this phase - see docs/architecture/UBL-ENGINE.md:
//  - Excise (ÖTV) and withholding (tevkifat) tax categories are not modeled anywhere in
//    sales_document_lines - only vat_rate exists - so this generator only ever emits a KDV TaxCategory.
//  - No cac:PartyTaxScheme/tax-office element on either party - the buyer side has no tax-office field
//    anywhere in the schema, and fabricating one for the seller only would be inconsistent.
//  - No official GİB XSD bundle is available in this repo, so validation is structural, not schema
//    validation against the real GİB UBL-TR XSDs.
public sealed class UblInvoiceGenerator(StoreDatabase database, LocalElectronicDocumentService documents) : IUblDocumentGenerator
{
    internal static readonly XNamespace Inv = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    internal static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    internal static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    // GİB's current UBL-TR customization id; not a company/document-specific value (spec §8).
    private const string CustomizationId = "TR1.2";
    private const string UblVersionId = "2.1";

    public bool CanGenerate(ElectronicDocumentRow document) =>
        document.SourceEntityType == "SalesInvoice" && document.DocumentType is ElectronicDocumentType.EInvoice or ElectronicDocumentType.EArchiveInvoice;

    public string Generate(string electronicDocumentId)
    {
        var document = documents.Get(electronicDocumentId) ?? throw new KeyNotFoundException("Elektronik belge bulunamadı.");
        if (!CanGenerate(document)) throw new InvalidOperationException($"UblInvoiceGenerator {document.DocumentType}/{document.SourceEntityType} için kullanılamaz.");

        var invoiceHeader = ReadInvoice(document.SourceEntityId);
        var data = new UblInvoiceData(invoiceHeader, ReadLines(document.SourceEntityId), ReadSeller(document), ReadBuyer(invoiceHeader.AccountId));
        UblInvoiceValidator.ValidateSource(document, data);

        var profileId = document.DocumentType == ElectronicDocumentType.EArchiveInvoice ? "EARSIVFATURA" : ScenarioToProfileId(data.Buyer.InvoiceScenario);
        var issueDate = document.IssueDate;
        var invoice = data.Invoice;

        var xml = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Inv + "Invoice",
                new XAttribute(XNamespace.Xmlns + "cac", Cac.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "cbc", Cbc.NamespaceName),
                new XElement(Cbc + "UBLVersionID", UblVersionId),
                new XElement(Cbc + "CustomizationID", CustomizationId),
                new XElement(Cbc + "ProfileID", profileId),
                new XElement(Cbc + "ID", document.DocumentNumber ?? invoice.DocumentNo),
                new XElement(Cbc + "UUID", document.Uuid),
                new XElement(Cbc + "IssueDate", issueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                new XElement(Cbc + "IssueTime", issueDate.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
                new XElement(Cbc + "InvoiceTypeCode", "SATIS"),
                new XElement(Cbc + "DocumentCurrencyCode", invoice.CurrencyCode),
                new XElement(Cbc + "LineCountNumeric", data.Lines.Count),
                AccountingParty(Cac + "AccountingSupplierParty", data.Seller),
                AccountingParty(Cac + "AccountingCustomerParty", data.Buyer),
                new XElement(Cac + "TaxTotal",
                    new XElement(Cbc + "TaxAmount", new XAttribute("currencyID", invoice.CurrencyCode), Amount(invoice.TaxTotal)),
                    new XElement(Cac + "TaxSubtotal",
                        new XElement(Cbc + "TaxableAmount", new XAttribute("currencyID", invoice.CurrencyCode), Amount(invoice.Subtotal - invoice.DiscountTotal)),
                        new XElement(Cbc + "TaxAmount", new XAttribute("currencyID", invoice.CurrencyCode), Amount(invoice.TaxTotal)),
                        new XElement(Cac + "TaxCategory",
                            new XElement(Cac + "TaxScheme", new XElement(Cbc + "Name", "KDV"), new XElement(Cbc + "TaxTypeCode", "0015"))))),
                new XElement(Cac + "LegalMonetaryTotal",
                    new XElement(Cbc + "LineExtensionAmount", new XAttribute("currencyID", invoice.CurrencyCode), Amount(invoice.Subtotal)),
                    new XElement(Cbc + "TaxExclusiveAmount", new XAttribute("currencyID", invoice.CurrencyCode), Amount(invoice.Subtotal - invoice.DiscountTotal)),
                    new XElement(Cbc + "TaxInclusiveAmount", new XAttribute("currencyID", invoice.CurrencyCode), Amount(invoice.GrandTotal)),
                    new XElement(Cbc + "AllowanceTotalAmount", new XAttribute("currencyID", invoice.CurrencyCode), Amount(invoice.DiscountTotal)),
                    new XElement(Cbc + "PayableAmount", new XAttribute("currencyID", invoice.CurrencyCode), Amount(invoice.GrandTotal))),
                data.Lines.Select(l => InvoiceLine(l, invoice.CurrencyCode))));

        var content = xml.Declaration + Environment.NewLine + xml.ToString(SaveOptions.None);
        UblInvoiceValidator.ValidateGeneratedXml(content, document, data);
        return content;
    }

    private static XElement InvoiceLine(UblLine l, string currency) => new(Cac + "InvoiceLine",
        new XElement(Cbc + "ID", l.LineNo),
        new XElement(Cbc + "InvoicedQuantity", new XAttribute("unitCode", UblUnitCodeMap.Resolve(l.UnitCode)), Quantity(l.BaseQuantity)),
        new XElement(Cbc + "LineExtensionAmount", new XAttribute("currencyID", currency), Amount(l.NetAmount)),
        l.DiscountAmount > 0 ? new XElement(Cac + "AllowanceCharge",
            new XElement(Cbc + "ChargeIndicator", "false"),
            new XElement(Cbc + "AllowanceChargeReason", "İskonto"),
            new XElement(Cbc + "Amount", new XAttribute("currencyID", currency), Amount(l.DiscountAmount))) : null,
        new XElement(Cac + "TaxTotal",
            new XElement(Cbc + "TaxAmount", new XAttribute("currencyID", currency), Amount(l.VatAmount)),
            new XElement(Cac + "TaxSubtotal",
                new XElement(Cbc + "TaxableAmount", new XAttribute("currencyID", currency), Amount(l.NetAmount)),
                new XElement(Cbc + "TaxAmount", new XAttribute("currencyID", currency), Amount(l.VatAmount)),
                new XElement(Cbc + "Percent", Percent(l.VatRate)),
                new XElement(Cac + "TaxCategory",
                    new XElement(Cac + "TaxScheme", new XElement(Cbc + "Name", "KDV"), new XElement(Cbc + "TaxTypeCode", "0015"))))),
        new XElement(Cac + "Item",
            new XElement(Cbc + "Name", l.ProductName),
            new XElement(Cac + "SellersItemIdentification", new XElement(Cbc + "ID", l.ProductCode))),
        new XElement(Cac + "Price", new XElement(Cbc + "PriceAmount", new XAttribute("currencyID", currency), Amount(l.UnitPrice))));

    private static XElement AccountingParty(XName wrapper, UblParty party) => new(wrapper,
        new XElement(Cac + "Party",
            party.PartyIdentification is { } id ? new XElement(Cac + "PartyIdentification", new XElement(Cbc + "ID", new XAttribute("schemeID", id.SchemeId), id.Value)) : null,
            new XElement(Cac + "PartyName", new XElement(Cbc + "Name", party.PartyName)),
            PostalAddress(party),
            Contact(party)));

    private static XElement? PostalAddress(UblParty p) => string.IsNullOrWhiteSpace(p.Address) && string.IsNullOrWhiteSpace(p.City) ? null : new XElement(Cac + "PostalAddress",
        new XElement(Cbc + "StreetName", p.Address), new XElement(Cbc + "CitySubdivisionName", p.District), new XElement(Cbc + "CityName", p.City),
        new XElement(Cbc + "PostalZone", p.PostalCode), new XElement(Cac + "Country", new XElement(Cbc + "Name", p.Country)));

    private static XElement? Contact(UblParty p) => string.IsNullOrWhiteSpace(p.Phone) && string.IsNullOrWhiteSpace(p.Email) ? null :
        new XElement(Cac + "Contact", string.IsNullOrWhiteSpace(p.Phone) ? null : new XElement(Cbc + "Telephone", p.Phone), string.IsNullOrWhiteSpace(p.Email) ? null : new XElement(Cbc + "ElectronicMail", p.Email));

    private static string ScenarioToProfileId(string scenario) => scenario switch
    {
        "Ticari" => "TICARIFATURA", "İhracat" => "IHRACAT", "Kamu" => "KAMU", _ => "TEMELFATURA"
    };

    internal static string Amount(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);
    internal static string Quantity(decimal value) => value.ToString("F4", CultureInfo.InvariantCulture);
    internal static string Percent(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private UblInvoice ReadInvoice(string salesDocumentId)
    {
        var t = database.Query("SELECT company_id,branch_id,account_id,document_no,currency_code,subtotal,discount_total,tax_total,grand_total FROM sales_documents WHERE id=$id", ("$id", salesDocumentId));
        if (t.Rows.Count == 0) throw new KeyNotFoundException("Kaynak satış faturası bulunamadı.");
        var r = t.Rows[0];
        return new(r["company_id"].ToString()!, r["branch_id"].ToString()!, r["account_id"].ToString()!, r["document_no"] as string ?? "",
            r["currency_code"].ToString()!, Convert.ToDecimal(r["subtotal"]), Convert.ToDecimal(r["discount_total"]), Convert.ToDecimal(r["tax_total"]), Convert.ToDecimal(r["grand_total"]));
    }

    private IReadOnlyList<UblLine> ReadLines(string salesDocumentId) => database.Query("""
        SELECT l.line_no,l.base_quantity,l.unit_price,l.discount_amount,l.vat_rate,l.net_amount,l.vat_amount,p.code AS product_code,p.name AS product_name,u.code AS unit_code
        FROM sales_document_lines l JOIN products p ON p.id=l.product_id JOIN units u ON u.id=l.unit_id
        WHERE l.sales_document_id=$id ORDER BY l.line_no
        """, ("$id", salesDocumentId)).Rows.Cast<System.Data.DataRow>().Select(r => new UblLine(
            Convert.ToInt32(r["line_no"]), Convert.ToDecimal(r["base_quantity"]), Convert.ToDecimal(r["unit_price"]), Convert.ToDecimal(r["discount_amount"]),
            Convert.ToDecimal(r["vat_rate"]), Convert.ToDecimal(r["net_amount"]), Convert.ToDecimal(r["vat_amount"]),
            r["product_code"].ToString()!, r["product_name"].ToString()!, r["unit_code"].ToString()!)).ToList();

    private UblParty ReadSeller(ElectronicDocumentRow document)
    {
        var t = database.Query("SELECT code,name,legal_name,tax_office,tax_number,phone,email,address FROM companies WHERE id=$id", ("$id", document.CompanyId));
        if (t.Rows.Count == 0) throw new KeyNotFoundException("Firma bulunamadı.");
        var r = t.Rows[0];
        var profile = documents.GetCompanyProfile(document.CompanyId);
        var legalName = string.IsNullOrWhiteSpace(profile.LegalTitle) ? r["legal_name"].ToString()! : profile.LegalTitle;
        var taxNumber = string.IsNullOrWhiteSpace(profile.TaxNumber) ? r["tax_number"].ToString()! : profile.TaxNumber;
        var address = string.IsNullOrWhiteSpace(profile.AddressLine) ? r["address"].ToString()! : profile.AddressLine;
        return new(string.IsNullOrWhiteSpace(taxNumber) ? null : ("VKN", taxNumber), string.IsNullOrWhiteSpace(legalName) ? r["name"].ToString()! : legalName, profile.DefaultEInvoiceAlias,
            address, profile.City, profile.District, profile.PostalCode, string.IsNullOrWhiteSpace(profile.Country) ? "Türkiye" : profile.Country,
            r["phone"].ToString()!, r["email"].ToString()!, profile.DefaultInvoiceScenario);
    }

    private UblParty ReadBuyer(string accountId)
    {
        var t = database.Query("""
            SELECT a.code,a.name,a.tax_number,a.identity_number,a.phone,a.mobile_phone,a.email,
                   COALESCE(tp.person_type,'LegalEntity') AS person_type, COALESCE(tp.legal_title,'') AS legal_title,
                   COALESCE(ei.einvoice_alias,'') AS einvoice_alias, COALESCE(ei.invoice_scenario,'Temel') AS invoice_scenario
            FROM accounts a
            LEFT JOIN account_tax_profiles tp ON tp.account_id=a.id
            LEFT JOIN account_einvoice_profiles ei ON ei.account_id=a.id
            WHERE a.id=$id
            """, ("$id", accountId));
        if (t.Rows.Count == 0) throw new KeyNotFoundException("Cari hesap bulunamadı.");
        var r = t.Rows[0];
        var personType = r["person_type"].ToString()!;
        var taxNumber = r["tax_number"].ToString()!; var identityNumber = r["identity_number"].ToString()!;
        (string SchemeId, string Value)? identification = personType == "Individual" && !string.IsNullOrWhiteSpace(identityNumber) ? ("TCKN", identityNumber)
            : !string.IsNullOrWhiteSpace(taxNumber) ? ("VKN", taxNumber) : null;
        var legalTitle = r["legal_title"].ToString()!; var name = r["name"].ToString()!;

        var addr = database.Query("SELECT address_line,city,district,postal_code,country FROM account_addresses WHERE account_id=$id AND is_default=1 AND is_active=1 LIMIT 1", ("$id", accountId));
        if (addr.Rows.Count == 0) addr = database.Query("SELECT address_line,city,district,postal_code,country FROM account_addresses WHERE account_id=$id AND is_active=1 LIMIT 1", ("$id", accountId));
        var ar = addr.Rows.Count > 0 ? addr.Rows[0] : null;

        return new(identification, string.IsNullOrWhiteSpace(legalTitle) ? name : legalTitle, r["einvoice_alias"].ToString()!,
            ar?["address_line"]?.ToString() ?? "", ar?["city"]?.ToString() ?? "", ar?["district"]?.ToString() ?? "", ar?["postal_code"]?.ToString() ?? "",
            string.IsNullOrWhiteSpace(ar?["country"]?.ToString()) ? "Türkiye" : ar!["country"]!.ToString()!,
            string.IsNullOrWhiteSpace(r["phone"].ToString()) ? r["mobile_phone"].ToString()! : r["phone"].ToString()!, r["email"].ToString()!, r["invoice_scenario"].ToString()!);
    }
}
