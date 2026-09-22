namespace R3.Desktop.Presentation;

public static class InventoryPresentation
{
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

    public static string TransactionTypeLabel(string type) => type switch
    {
        "ManualIn" => "Manuel Giriş",
        "ManualOut" => "Manuel Çıkış",
        "OpeningBalance" => "Açılış Bakiyesi",
        "TransferIn" => "Transfer Girişi",
        "TransferOut" => "Transfer Çıkışı",
        "CountIncrease" => "Sayım Fazlası",
        "CountDecrease" => "Sayım Eksiği",
        _ => type
    };
}
