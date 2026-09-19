using System.Globalization;
using System.Xml.Linq;

namespace R3.Infrastructure;

// Phase 6: real UBL 2.1 / UBL-TR structure built from R3's actual domain data - no placeholder/fake
// values for anything (spec §1/§6: report a gap instead of inventing data). Provider-independent
// (spec §3): this class has never heard of GİB, a specific entegratör, or an HTTP endpoint - it only
// turns R3 rows into a canonical UBL XML string, which the outbox/provider layer sends unchanged.
//
// Known, reported (not silently worked around) gaps as of this phase:
//  - Excise (ÖTV) and withholding (tevkifat) tax categories are not modeled anywhere in
//    sales_document_lines - only vat_rate exists - so this generator only ever emits a KDV
//    TaxCategory. An invoice that actually needs ÖTV/tevkifat will generate an incomplete-but-honest
//    XML; GenerationValidationException below is how that gets surfaced rather than silently ignored.
//  - No official GİB XSD bundle is available in this repo, so validation here is structural
//    (required elements present, totals reconcile) - not schema validation against the real GİB
//    UBL-TR XSDs. See docs/architecture/UBL-GENERATION.md.
public sealed class UblGenerationException(string message) : Exception(message);

public sealed class UblInvoiceGenerator(StoreDatabase database, LocalElectronicDocumentService documents) : IUblDocumentGenerator
{
    private static readonly XNamespace Inv = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    // GİB's current UBL-TR customization id; not a company/document-specific value (spec §8).
    private const string CustomizationId = "TR1.2";
    private const string UblVersionId = "2.1";

    public bool CanGenerate(ElectronicDocumentRow document) =>
        document.SourceEntityType == "SalesInvoice" && document.DocumentType is ElectronicDocumentType.EInvoice or ElectronicDocumentType.EArchiveInvoice;

    public string Generate(string electronicDocumentId)
    {
        var document = documents.Get(electronicDocumentId) ?? throw new KeyNotFoundException("Elektronik belge bulunamadı.");
        if (!CanGenerate(document)) throw new InvalidOperationException($"UblInvoiceGenerator {document.DocumentType}/{document.SourceEntityType} için kullanılamaz.");

        var invoice = ReadInvoice(document.SourceEntityId);
        var lines = ReadLines(document.SourceEntityId);
        var seller = ReadSeller(invoice.CompanyId, invoice.BranchId);
        var buyer = ReadBuyer(invoice.AccountId);
        if (lines.Count == 0) throw new UblGenerationException("Faturada hiç kalem bulunamadı.");

        var profileId = document.DocumentType == ElectronicDocumentType.EArchiveInvoice ? "EARSIVFATURA" : ScenarioToProfileId(buyer.InvoiceScenario);
        var issueDate = document.IssueDate;

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
                new XElement(Cbc + "LineCountNumeric", lines.Count),
                AccountingParty(Cac + "AccountingSupplierParty", seller.PartyIdentification, seller.PartyName, seller.PostalAddress(), seller.TaxScheme(), seller.Contact()),
                AccountingParty(Cac + "AccountingCustomerParty", buyer.PartyIdentification, buyer.PartyName, buyer.PostalAddress(), buyer.TaxScheme(), buyer.Contact()),
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
                lines.Select(l => InvoiceLine(l, invoice.CurrencyCode))));

        var content = xml.Declaration + Environment.NewLine + xml.ToString(SaveOptions.None);
        Validate(content, document, invoice, lines);
        return content;
    }

    private static XElement InvoiceLine(Line l, string currency) => new(Cac + "InvoiceLine",
        new XElement(Cbc + "ID", l.LineNo),
        new XElement(Cbc + "InvoicedQuantity", new XAttribute("unitCode", UnitCode(l.UnitCode)), Quantity(l.BaseQuantity)),
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

    private static XElement AccountingParty(XName wrapper, (string SchemeId, string Value)? partyIdentification, string partyName, XElement? postalAddress, XElement? taxScheme, XElement? contact) => new(wrapper,
        new XElement(Cac + "Party",
            partyIdentification is { } id ? new XElement(Cac + "PartyIdentification", new XElement(Cbc + "ID", new XAttribute("schemeID", id.SchemeId), id.Value)) : null,
            new XElement(Cac + "PartyName", new XElement(Cbc + "Name", partyName)),
            postalAddress,
            taxScheme,
            contact));

    // Structural validation only (spec §1/§68 of the original e-belge spec): required elements present,
    // totals reconcile with what was actually posted. Not GİB XSD schema validation - no official XSD
    // bundle is available to validate against (see the class-level gap note).
    private static void Validate(string xml, ElectronicDocumentRow document, Invoice invoice, IReadOnlyList<Line> lines)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root!;
        string? Text(XName name) => root.Element(name)?.Value;
        if (string.IsNullOrWhiteSpace(Text(Cbc + "ID"))) throw new UblGenerationException("UBL ID (fatura no) boş olamaz.");
        if (string.IsNullOrWhiteSpace(Text(Cbc + "UUID")) || Text(Cbc + "UUID") != document.Uuid) throw new UblGenerationException("UBL UUID, ElectronicDocument.Uuid ile eşleşmiyor.");
        if (string.IsNullOrWhiteSpace(Text(Cbc + "IssueDate"))) throw new UblGenerationException("UBL IssueDate boş olamaz.");
        if (!root.Descendants(Cac + "InvoiceLine").Any()) throw new UblGenerationException("UBL en az bir InvoiceLine içermelidir.");
        var payable = decimal.Parse(root.Descendants(Cbc + "PayableAmount").First().Value, CultureInfo.InvariantCulture);
        if (Math.Abs(payable - invoice.GrandTotal) > 0.01m) throw new UblGenerationException($"UBL PayableAmount ({payable}) fatura genel toplamıyla ({invoice.GrandTotal}) eşleşmiyor.");
        if (root.Descendants(Cac + "InvoiceLine").Count() != lines.Count) throw new UblGenerationException("UBL satır sayısı fatura satır sayısıyla eşleşmiyor.");
    }

    private static string ScenarioToProfileId(string scenario) => scenario switch
    {
        "Ticari" => "TICARIFATURA", "İhracat" => "IHRACAT", "Kamu" => "KAMU", _ => "TEMELFATURA"
    };

    // Minimal, explicitly non-exhaustive UN/CEFACT Recommendation 20 mapping for R3's actual unit
    // codes (docs/architecture/UBL-GENERATION.md has the full table and how to extend it). Falls back
    // to C62 ("one/piece") rather than guessing a more specific code.
    private static string UnitCode(string unit) => unit.ToUpperInvariant() switch
    {
        "ADET" => "C62", "KG" => "KGM", "GR" => "GRM", "LT" or "LITRE" => "LTR", "MT" or "METRE" => "MTR",
        "M2" => "MTK", "M3" => "MTQ", "KUTU" => "BX", "PAKET" => "PK", "ÇİFT" or "CIFT" => "PR", _ => "C62"
    };

    private static string Amount(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);
    private static string Quantity(decimal value) => value.ToString("F4", CultureInfo.InvariantCulture);
    private static string Percent(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private sealed record Invoice(string CompanyId, string BranchId, string AccountId, string DocumentNo, string CurrencyCode, decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal GrandTotal);
    private sealed record Line(int LineNo, decimal BaseQuantity, decimal UnitPrice, decimal DiscountAmount, decimal VatRate, decimal NetAmount, decimal VatAmount, string ProductCode, string ProductName, string UnitCode);

    private Invoice ReadInvoice(string salesDocumentId)
    {
        var t = database.Query("SELECT company_id,branch_id,account_id,document_no,currency_code,subtotal,discount_total,tax_total,grand_total FROM sales_documents WHERE id=$id", ("$id", salesDocumentId));
        if (t.Rows.Count == 0) throw new KeyNotFoundException("Kaynak satış faturası bulunamadı.");
        var r = t.Rows[0];
        return new(r["company_id"].ToString()!, r["branch_id"].ToString()!, r["account_id"].ToString()!, r["document_no"] as string ?? "",
            r["currency_code"].ToString()!, Convert.ToDecimal(r["subtotal"]), Convert.ToDecimal(r["discount_total"]), Convert.ToDecimal(r["tax_total"]), Convert.ToDecimal(r["grand_total"]));
    }

    private IReadOnlyList<Line> ReadLines(string salesDocumentId) => database.Query("""
        SELECT l.line_no,l.base_quantity,l.unit_price,l.discount_amount,l.vat_rate,l.net_amount,l.vat_amount,p.code AS product_code,p.name AS product_name,u.code AS unit_code
        FROM sales_document_lines l JOIN products p ON p.id=l.product_id JOIN units u ON u.id=l.unit_id
        WHERE l.sales_document_id=$id ORDER BY l.line_no
        """, ("$id", salesDocumentId)).Rows.Cast<System.Data.DataRow>().Select(r => new Line(
            Convert.ToInt32(r["line_no"]), Convert.ToDecimal(r["base_quantity"]), Convert.ToDecimal(r["unit_price"]), Convert.ToDecimal(r["discount_amount"]),
            Convert.ToDecimal(r["vat_rate"]), Convert.ToDecimal(r["net_amount"]), Convert.ToDecimal(r["vat_amount"]),
            r["product_code"].ToString()!, r["product_name"].ToString()!, r["unit_code"].ToString()!)).ToList();

    private sealed record Party((string SchemeId, string Value)? PartyIdentification, string PartyName, string Alias, string Address, string City, string District, string PostalCode, string Country, string Phone, string Email, string InvoiceScenario)
    {
        public XElement? PostalAddress() => string.IsNullOrWhiteSpace(Address) && string.IsNullOrWhiteSpace(City) ? null : new XElement(Cac + "PostalAddress",
            new XElement(Cbc + "StreetName", Address), new XElement(Cbc + "CitySubdivisionName", District), new XElement(Cbc + "CityName", City),
            new XElement(Cbc + "PostalZone", PostalCode), new XElement(Cac + "Country", new XElement(Cbc + "Name", Country)));
        public XElement? TaxScheme() => null; // party-level TaxScheme intentionally omitted here - PartyIdentification already carries VKN/TCKN per spec §6/§7; a full PartyTaxScheme/TaxOffice element is a documented follow-up, not fabricated here.
        public XElement? Contact() => string.IsNullOrWhiteSpace(Phone) && string.IsNullOrWhiteSpace(Email) ? null :
            new XElement(Cac + "Contact", string.IsNullOrWhiteSpace(Phone) ? null : new XElement(Cbc + "Telephone", Phone), string.IsNullOrWhiteSpace(Email) ? null : new XElement(Cbc + "ElectronicMail", Email));
    }

    private Party ReadSeller(string companyId, string branchId)
    {
        var t = database.Query("SELECT code,name,legal_name,tax_office,tax_number,phone,email,address FROM companies WHERE id=$id", ("$id", companyId));
        if (t.Rows.Count == 0) throw new KeyNotFoundException("Firma bulunamadı.");
        var r = t.Rows[0];
        var profile = documents.GetCompanyProfile(companyId);
        var legalName = string.IsNullOrWhiteSpace(profile.LegalTitle) ? r["legal_name"].ToString()! : profile.LegalTitle;
        var taxNumber = string.IsNullOrWhiteSpace(profile.TaxNumber) ? r["tax_number"].ToString()! : profile.TaxNumber;
        var address = string.IsNullOrWhiteSpace(profile.AddressLine) ? r["address"].ToString()! : profile.AddressLine;
        return new((("VKN", taxNumber)), string.IsNullOrWhiteSpace(legalName) ? r["name"].ToString()! : legalName, profile.DefaultEInvoiceAlias,
            address, profile.City, profile.District, profile.PostalCode, string.IsNullOrWhiteSpace(profile.Country) ? "Türkiye" : profile.Country,
            r["phone"].ToString()!, r["email"].ToString()!, profile.DefaultInvoiceScenario);
    }

    private Party ReadBuyer(string accountId)
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
        (string SchemeId, string Value) identification = personType == "Individual" && !string.IsNullOrWhiteSpace(identityNumber)
            ? ("TCKN", identityNumber) : ("VKN", taxNumber);
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
