using R3.Desktop.Presentation;

namespace R3.Desktop.Tests;

public sealed class TurkishPresentationTests
{
    [Theory]
    [InlineData("Posted", "Kesinleşti")]
    [InlineData("Draft", "Taslak")]
    [InlineData("PartiallyReceived", "Kısmi Teslim Alındı")]
    [InlineData("Cancelled", "İptal Edildi")]
    [InlineData("Queued", "Kuyrukta")]
    public void Technical_status_codes_are_not_shown_raw(string code, string expected) =>
        Assert.Equal(expected, InventoryPresentation.GenericStatusLabel(code));

    [Theory]
    [InlineData("PurchaseInvoicePosted", "Alış Faturası Kesinleştirildi")]
    [InlineData("DespatchCreatedFromInvoice", "Faturadan İrsaliye Oluşturuldu")]
    [InlineData("ShipmentCancelled", "Sevkiyat İptal Edildi")]
    public void Audit_actions_are_turkish(string code, string expected) =>
        Assert.Equal(expected, InventoryPresentation.AuditActionLabel(code));

    [Fact]
    public void Electronic_document_fallback_status_is_turkish()
    {
        Assert.Equal("Gönderiliyor", EDocumentPresentation.StatusLabel(R3.Infrastructure.ElectronicDocumentStatus.Sending));
        Assert.Equal("Gönderim", InventoryPresentation.OperationLabel("Send"));
    }
}
