using System.Globalization;
using System.Xml.Linq;

namespace R3.Infrastructure;

// Minimal, standalone validator (spec §20) - not an IUblDocumentValidator interface, since there is
// exactly one implementation and one document kind so far; promote to an interface when a second
// generator (despatch advice, receipt advice) needs its own validation shape.
internal static class UblInvoiceValidator
{
    // Pre-generation (spec §20/§40/§41): fail before spending effort building XML that could never be
    // valid anyway - missing VKN/TCKN, an unmapped unit, or an EInvoice-routed buyer with no alias.
    public static void ValidateSource(ElectronicDocumentRow document, UblInvoiceData data)
    {
        if (data.Lines.Count == 0) throw new UblGenerationException("Faturada hiç kalem bulunamadı.");
        foreach (var line in data.Lines)
        {
            if (line.BaseQuantity <= 0) throw new UblGenerationException($"Satır {line.LineNo}: miktar 0'dan büyük olmalıdır.");
            UblUnitCodeMap.Resolve(line.UnitCode); // throws for an unmapped unit - no silent fallback (spec §14)
        }
        if (string.IsNullOrWhiteSpace(data.Seller.PartyIdentification?.Value)) throw new UblGenerationException("Satıcı vergi numarası eksik.");
        if (string.IsNullOrWhiteSpace(data.Buyer.PartyIdentification?.Value)) throw new UblGenerationException("Alıcının VKN/TCKN bilgisi eksik.");
        // Profile aktif ama alias eksikse generation fail (spec §31). In practice ElectronicDocumentRoutingService
        // already requires a non-empty alias to route EInvoice in the first place (Phase 2 review-gate fix), so
        // this only fires if the alias was cleared between posting and generation - defense in depth, not dead code.
        if (document.DocumentType == ElectronicDocumentType.EInvoice && string.IsNullOrWhiteSpace(data.Buyer.Alias))
            throw new UblGenerationException("E-Fatura mükellefi alıcının e-Fatura alias bilgisi eksik.");
        if (string.IsNullOrWhiteSpace(data.Invoice.CurrencyCode)) throw new UblGenerationException("Para birimi eksik.");
    }

    // Post-generation structural validation only (original e-belge spec §68): required elements present,
    // totals reconcile with what was actually posted. Not GİB XSD schema validation - no official XSD
    // bundle is available in this repo to validate against (see docs/architecture/UBL-ENGINE.md).
    public static void ValidateGeneratedXml(string xml, ElectronicDocumentRow document, UblInvoiceData data)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root!;
        string? Text(XName name) => root.Element(name)?.Value;
        if (string.IsNullOrWhiteSpace(Text(UblInvoiceGenerator.Cbc + "ID"))) throw new UblGenerationException("UBL ID (fatura no) boş olamaz.");
        if (string.IsNullOrWhiteSpace(Text(UblInvoiceGenerator.Cbc + "UUID")) || Text(UblInvoiceGenerator.Cbc + "UUID") != document.Uuid)
            throw new UblGenerationException("UBL UUID, ElectronicDocument.Uuid ile eşleşmiyor.");
        if (string.IsNullOrWhiteSpace(Text(UblInvoiceGenerator.Cbc + "IssueDate"))) throw new UblGenerationException("UBL IssueDate boş olamaz.");
        var lines = root.Descendants(UblInvoiceGenerator.Cac + "InvoiceLine").ToList();
        if (lines.Count == 0) throw new UblGenerationException("UBL en az bir InvoiceLine içermelidir.");
        if (lines.Count != data.Lines.Count) throw new UblGenerationException("UBL satır sayısı fatura satır sayısıyla eşleşmiyor.");
        var payable = decimal.Parse(root.Descendants(UblInvoiceGenerator.Cbc + "PayableAmount").First().Value, CultureInfo.InvariantCulture);
        if (Math.Abs(payable - data.Invoice.GrandTotal) > 0.01m) throw new UblGenerationException($"UBL PayableAmount ({payable}) fatura genel toplamıyla ({data.Invoice.GrandTotal}) eşleşmiyor.");
    }
}
