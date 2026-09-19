namespace R3.Infrastructure;

// Spec §2: canonical UBL generation contract - provider-independent (§3). Future generators
// (UblDespatchAdviceGenerator, UblReceiptAdviceGenerator) implement the same shape for their own
// ElectronicDocumentType/SourceEntityType combination; this phase only implements the Invoice side.
public interface IUblDocumentGenerator
{
    bool CanGenerate(ElectronicDocumentRow document);
    string Generate(string electronicDocumentId);
}
