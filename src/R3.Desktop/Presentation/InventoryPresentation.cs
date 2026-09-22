namespace R3.Desktop.Presentation;

public static class InventoryPresentation
{
    public static string ProductTypeLabel(string type) => type switch
    {
        "Stock" => "Stok",
        "Service" => "Hizmet",
        "Bundle" => "Takım / Set",
        "RawMaterial" => "Hammadde",
        "FinishedGood" => "Mamul",
        "PRODUCT" => "Ürün",
        _ => type
    };

    public static string ShipmentStatusLabel(string status) => status switch
    {
        "Pending" => "Bekliyor",
        "Planned" => "Planlandı",
        "ReadyForShipment" => "Sevke Hazır",
        "InTransit" => "Yolda",
        "Delivered" => "Teslim Edildi",
        "Cancelled" => "İptal Edildi",
        _ => status
    };

    public static string TransactionTypeLabel(string type) => type switch
    {
        "Inbound" => "Stok Girişi",
        "Outbound" => "Stok Çıkışı",
        "SaleIssue" => "Satış Çıkışı",
        "PurchaseReceipt" => "Alış Girişi",
        "PurchaseReturn" => "Alış İadesi",
        "SaleReturn" => "Satış İadesi",
        "LegacyStockMovement" => "Aktarılan Stok Hareketi",
        "ManualIn" => "Manuel Giriş",
        "ManualOut" => "Manuel Çıkış",
        "OpeningBalance" => "Açılış Bakiyesi",
        "TransferIn" => "Transfer Girişi",
        "TransferOut" => "Transfer Çıkışı",
        "CountIncrease" => "Sayım Fazlası",
        "CountDecrease" => "Sayım Eksiği",
        _ => type
    };

    public static string DocumentTypeLabel(string type) => type switch
    {
        "ManualIn" => "Stok Giriş Fişi",
        "ManualOut" => "Stok Çıkış Fişi",
        "OpeningBalance" => "Açılış Bakiyesi",
        "TransferIn" => "Transfer Girişi",
        "TransferOut" => "Transfer Çıkışı",
        _ => type
    };

    public static string StatusLabel(string status) => status switch
    {
        "Draft" => "Taslak",
        "Approved" => "Onaylandı",
        "Cancelled" => "İptal Edildi",
        "Reversed" => "Ters Hareket Oluşturuldu",
        "InTransit" => "Transferde",
        "Received" => "Teslim Alındı",
        _ => status
    };

}
