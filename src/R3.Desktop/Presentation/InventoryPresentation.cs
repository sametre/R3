namespace R3.Desktop.Presentation;

public static class InventoryPresentation
{
    public static string GenericStatusLabel(string status) => status switch
    {
        "Draft" => "Taslak", "Pending" => "Bekliyor", "WaitingForApproval" => "Onay Bekliyor",
        "Approved" => "Onaylandı", "Posted" => "Kesinleşti", "Planned" => "Planlandı",
        "Ready" => "Hazır", "ReadyForShipment" => "Sevke Hazır", "InTransit" => "Transferde",
        "Delivered" => "Teslim Edildi", "PartiallyReceived" => "Kısmi Teslim Alındı",
        "Received" => "Teslim Alındı", "Closed" => "Kapandı", "Completed" => "Tamamlandı",
        "Cancelled" => "İptal Edildi", "Reversed" => "Tersine Çevrildi", "Active" => "Aktif",
        "Inactive" => "Pasif", "Open" => "Açık", "Failed" => "Başarısız", "Success" => "Başarılı",
        "Queued" => "Kuyrukta", "Processing" => "İşleniyor", "Sending" => "Gönderiliyor",
        "Sent" => "Gönderildi", "Accepted" => "Kabul Edildi", "Rejected" => "Reddedildi",
        "None" => "Yok", _ => status
    };

    public static string ProductTypeLabel(string type) => type switch
    {
        "Stock" => "Stok", "Service" => "Hizmet", "Bundle" => "Takım / Set",
        "RawMaterial" => "Hammadde", "FinishedGood" => "Mamul", "PRODUCT" => "Ürün", _ => type
    };

    public static string ShipmentStatusLabel(string status) => status switch
    {
        "Pending" => "Bekliyor", "Planned" => "Planlandı", "ReadyForShipment" => "Sevke Hazır",
        "InTransit" => "Yolda", "Delivered" => "Teslim Edildi", "Cancelled" => "İptal Edildi",
        _ => GenericStatusLabel(status)
    };

    public static string InvoiceStatusLabel(string status) => status switch
    {
        "Draft" => "Taslak", "Posted" => "Kesildi", "Cancelled" => "İptal Edildi",
        "Reversed" => "Tersine Çevrildi", "WaitingForApproval" => "Onay Bekliyor",
        "Approved" => "Onaylandı", _ => GenericStatusLabel(status)
    };

    public static string PurchaseStatusLabel(string status) => status switch
    {
        "Draft" => "Taslak", "Approved" => "Onaylandı", "PartiallyReceived" => "Kısmi Teslim Alındı",
        "Received" or "Closed" => "Tamamı Teslim Alındı", "Posted" => "Kesildi",
        "Cancelled" => "İptal Edildi", "Reversed" => "Tersine Çevrildi", _ => GenericStatusLabel(status)
    };

    public static string TransactionTypeLabel(string type) => type switch
    {
        "Inbound" => "Stok Girişi", "Outbound" => "Stok Çıkışı", "SaleIssue" => "Satış Çıkışı",
        "PurchaseReceipt" => "Alış Girişi", "PurchaseReturn" => "Alış İadesi", "SaleReturn" => "Satış İadesi",
        "LegacyStockMovement" => "Aktarılan Stok Hareketi", "ManualIn" => "Manuel Giriş", "ManualOut" => "Manuel Çıkış",
        "OpeningBalance" => "Açılış Bakiyesi", "TransferIn" => "Transfer Girişi", "TransferOut" => "Transfer Çıkışı",
        "CountIncrease" => "Sayım Fazlası", "CountDecrease" => "Sayım Eksiği", "Sale" => "Satış",
        "Purchase" => "Alış", "Receipt" => "Tahsilat", "Payment" => "Ödeme", "Debit" => "Borç",
        "Credit" => "Alacak", "CashIn" => "Kasa Girişi", "CashOut" => "Kasa Çıkışı", _ => DirectionLabel(type)
    };

    public static string DocumentTypeLabel(string type) => type switch
    {
        "ManualIn" => "Stok Giriş Fişi", "ManualOut" => "Stok Çıkış Fişi", "OpeningBalance" => "Açılış Bakiyesi",
        "TransferIn" => "Transfer Girişi", "TransferOut" => "Transfer Çıkışı", "SalesInvoice" => "Satış Faturası",
        "PurchaseInvoice" => "Alış Faturası", "Invoice" => "Fatura", "Despatch" => "İrsaliye",
        "PurchaseReceipt" => "Alış İrsaliyesi", "SalesDespatch" => "Satış İrsaliyesi", "Receipt" => "Tahsilat",
        "Payment" => "Ödeme", _ => type
    };

    public static string StatusLabel(string status) => status switch
    {
        "Reversed" => "Ters Hareket Oluşturuldu", "InTransit" => "Transferde", "Received" => "Teslim Alındı",
        _ => GenericStatusLabel(status)
    };

    public static string DirectionLabel(string direction) => direction switch
    {
        "Inbound" or "Received" or "In" => "Gelen", "Outbound" or "Given" or "Out" => "Giden",
        "TransferIn" => "Transfer Girişi", "TransferOut" => "Transfer Çıkışı", _ => direction
    };

    public static string OperationLabel(string operation) => operation switch
    {
        "Send" => "Gönderim", "QueryStatus" => "Durum Sorgusu", "Generate" => "Belge Oluşturma",
        "Cancel" => "İptal", "Retry" => "Tekrar Deneme", "Receive" => "Mal Kabul", "Post" => "Kesinleştirme", _ => operation
    };

    public static string ReservationStatusLabel(string status) => status switch
    {
        "Active" => "Aktif", "Released" => "Serbest", "Consumed" => "Teslim Edildi", "Expired" => "Süresi Doldu",
        _ => GenericStatusLabel(status)
    };

    public static string AuditActionLabel(string action) => action switch
    {
        "PurchaseDocumentCreated" => "Alış Belgesi Oluşturuldu", "PurchaseDocumentApproved" => "Alış Belgesi Onaylandı",
        "PurchaseDocumentCancelled" => "Alış Belgesi İptal Edildi", "PurchaseDocumentReversed" => "Alış Belgesi Tersine Çevrildi",
        "PurchaseDocumentReceived" => "Alış Belgesi Depoya Alındı", "PurchaseInvoicePosted" => "Alış Faturası Kesinleştirildi",
        "PurchaseReceiptCreated" => "Alış İrsaliyesi Oluşturuldu", "PurchaseReceiptApproved" => "Alış İrsaliyesi Onaylandı",
        "PurchaseReceiptCancelled" => "Alış İrsaliyesi İptal Edildi", "PurchaseReceiptReversed" => "Alış İrsaliyesi Tersine Çevrildi",
        "SalesInvoicePosted" => "Satış Faturası Kesinleştirildi", "SalesInvoiceCancelled" => "Satış Faturası İptal Edildi",
        "DespatchCreated" => "İrsaliye Oluşturuldu", "DespatchStatusChanged" => "İrsaliye Durumu Değiştirildi",
        "AccountCreated" => "Cari Oluşturuldu", "AccountUpdated" => "Cari Güncellendi", "AccountDeactivated" => "Cari Pasife Alındı",
        "ElectronicDocumentCreated" => "E-Belge Oluşturuldu", "ElectronicDocumentReady" => "E-Belge Hazırlandı",
        "ShipmentCancelled" => "Sevkiyat İptal Edildi", "ShipmentDelivered" => "Sevkiyat Teslim Edildi",
        "ShipmentStatusChanged" => "Sevkiyat Durumu Değiştirildi", "DespatchCancelled" => "İrsaliye İptal Edildi",
        "DespatchCreatedFromInvoice" => "Faturadan İrsaliye Oluşturuldu", "FinancialInstrumentCancelled" => "Finansal Evrak İptal Edildi",
        "CashAccountDeactivated" => "Kasa Hesabı Pasife Alındı", "DefaultGridLayoutSaved" => "Varsayılan tablo düzeni kaydedildi",
        "GridLayoutSaved" => "Tablo düzeni kaydedildi", "OrderCancelled" => "Sipariş İptal Edildi",
        "AccountActivated" => "Cari Aktif Edildi", "BankAccountCreated" => "Banka Hesabı Oluşturuldu",
        "BankAccountUpdated" => "Banka Hesabı Güncellendi", "BankAccountActivated" => "Banka Hesabı Aktif Edildi",
        "BankAccountDeactivated" => "Banka Hesabı Pasife Alındı", "AddressActivated" => "Adres Aktif Edildi",
        "AddressDeactivated" => "Adres Pasife Alındı", "AddressUpdated" => "Adres Güncellendi",
        "ContactActivated" => "Yetkili Aktif Edildi", "ContactDeactivated" => "Yetkili Pasife Alındı",
        "ContactUpdated" => "Yetkili Güncellendi", "PurchaseOrderReceived" => "Satınalma Siparişi Teslim Alındı",
        "InventoryDocumentApproved" => "Stok Fişi Onaylandı", "TransferApproved" => "Transfer Onaylandı",
        "TransferDraftCreated" => "Transfer Taslağı Oluşturuldu", "ReservationCreated" => "Rezervasyon Oluşturuldu",
        "ReturnCancelled" => "İade İptal Edildi", "ReturnPosted" => "İade Kesinleştirildi",
        "UserCreated" => "Kullanıcı Oluşturuldu", "UserUpdated" => "Kullanıcı Güncellendi",
        "UserAccessUpdated" => "Kullanıcı erişimi güncellendi", "RoleCreated" => "Rol Oluşturuldu",
        "RoleUpdated" => "Rol Güncellendi", "RolePermissionsUpdated" => "Rol yetkileri güncellendi",
        "WarehouseCreated" => "Depo Oluşturuldu", "WarehouseUpdated" => "Depo Güncellendi",
        "WarehouseLocationCreated" => "Depo lokasyonu oluşturuldu", "WarehouseLocationUpdated" => "Depo lokasyonu güncellendi",
        "PriceListCreated" => "Fiyat Listesi Oluşturuldu", "PriceListUpdated" => "Fiyat Listesi Güncellendi",
        "CustomerPriceGroupUpdated" => "Müşteri fiyat grubu güncellendi", "BankInPosted" => "Banka girişi kesinleştirildi",
        "BankOutPosted" => "Banka çıkışı kesinleştirildi", "CashInPosted" => "Kasa girişi kesinleştirildi",
        "CashOutPosted" => "Kasa çıkışı kesinleştirildi", "CashTransferPosted" => "Kasa transferi kesinleştirildi",
        "ChequeReceived" => "Çek / senet alındı", "AlışBelgesiIptalEdildi" => "Alış Belgesi İptal Edildi", _ => action
    };
}
