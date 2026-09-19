using System.Security.Cryptography;
using System.Text;

namespace R3.Infrastructure;

public sealed record UblGenerationResult(bool Success, bool AlreadyGenerated, string? PayloadId, string? ContentHash, string? ErrorMessage)
{
    public static UblGenerationResult Ok(string payloadId, string contentHash) => new(true, false, payloadId, contentHash, null);
    public static UblGenerationResult Idempotent(string payloadId, string contentHash) => new(true, true, payloadId, contentHash, null);
    public static UblGenerationResult Failed(string errorMessage) => new(false, false, null, null, errorMessage);
}

// Application-layer orchestrator (spec §47): IUblDocumentGenerator only ever reads and returns a string -
// it never opens a transaction or writes anything. This class owns that: Ready -> Generate XML -> validate
// -> persist payload + Ready->Generated + event, atomically (spec §48), with provider/network calls never
// in the picture (this phase stops at Generated - see docs/architecture/UBL-ENGINE.md's Phase 7 handoff).
public sealed class ElectronicDocumentGenerationService(StoreDatabase database, LocalElectronicDocumentService documents, IUblDocumentGenerator generator)
{
    public UblGenerationResult Generate(string electronicDocumentId, string userId)
    {
        var document = documents.Get(electronicDocumentId) ?? throw new KeyNotFoundException("Elektronik belge bulunamadı.");

        // Idempotent (spec §29): already Generated -> return the existing payload, no duplicate payload,
        // no duplicate event, status not rewritten.
        if (document.Status == ElectronicDocumentStatus.Generated)
        {
            var existing = documents.LatestSendablePayload(electronicDocumentId)
                ?? throw new InvalidOperationException("Belge Generated durumunda ama payload bulunamadı - tutarsız durum.");
            return UblGenerationResult.Idempotent(existing.Id, existing.ContentHash);
        }
        if (document.Status != ElectronicDocumentStatus.Ready)
            throw new InvalidOperationException($"{document.Status} durumundaki belge için UBL üretilemez.");

        string xml;
        try { xml = generator.Generate(electronicDocumentId); }
        catch (UblGenerationException ex)
        {
            // Illegal transition yapma (spec §28): status Ready'de kalır, sadece teknik event yazılır.
            documents.RecordEvent(electronicDocumentId, "ElectronicDocumentGenerationFailed", userId, ex.Message);
            return UblGenerationResult.Failed(ex.Message);
        }

        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        var payloadId = documents.SavePayloadWithinTransaction(c, tx, electronicDocumentId, ElectronicDocumentPayloadType.UblXml, xml, "application/xml", false, userId);
        documents.GenerateWithinTransaction(c, tx, electronicDocumentId, userId);
        tx.Commit();

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml)));
        return UblGenerationResult.Ok(payloadId, hash);
    }
}
