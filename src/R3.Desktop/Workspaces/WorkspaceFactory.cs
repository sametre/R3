using R3.Desktop.Controls;
using R3.Desktop.Navigation;

namespace R3.Desktop.Workspaces;

internal static class WorkspaceFactory
{
    public static Control Create(string key) => key switch
    {
        "dashboard" => new DashboardWorkspace(),
        "manager-dashboard" => new ManagerWorkspace(),
        "manager-approvals" => CreateManagerApprovals(),
        "manager-alerts" => CreateManagerAlerts(),
        "manager-stores" => CreateManagerStores(),
        "manager-devices" => CreateManagerDevices(),
        "manager-audit" => CreateManagerAudit(),
        "quick-sale" => new QuickSaleWorkspace(),
        "accounts" => CreateAccounts(),
        "customers" => CreateAccounts("Müşteriler", "Müşteri kartları ve ticari bilgiler"),
        "suppliers" => CreateAccounts("Tedarikçiler", "Tedarikçi kartları ve ticari bilgiler"),
        "inventory" => CreateInventory(),
        "products" => CreateInventory(),
        "variants" => CreateVariants(),
        "barcodes" => CreateVariantBarcodes(),
        "stock-movements" => CreateStockMovements(),
        "invoice" => CreateInvoices(),
        "sales-invoices" => CreateInvoices(),
        "purchase-invoice" => new PurchaseInvoiceWorkspace(),
        "purchase-orders" => CreatePurchaseOrders(),
        "purchase-requests" => CreatePurchaseRequests(),
        "goods-receipts" => CreateGoodsReceipts(),
        "exchanges" => CreateExchanges(),
        "sales-dispatches" => CreateDispatches("Satış İrsaliyeleri"),
        "e-dispatch" => CreateDispatches("e-İrsaliye Yönetimi"),
        "account-movements" => CreateAccountMovements(),
        "cash-accounts" => CreateCashMovements(),
        "retail-registers" => CreateCashMovements(),
        "shifts" => CreateCashShifts(),
        "register-closures" => CreateCashShifts("Kasa Kapanışları"),
        "banks" => CreateBankAccounts(),
        "pos-accounts" => CreatePosAccounts(),
        "cheques" => CreateCheques(),
        "promissory-notes" => CreatePromissoryNotes(),
        "cash-flow" => CreateBankMovements(),
        "campaigns" => CreateCampaigns(),
        "stock-counts" => CreateStockCounts(),
        "warehouse-transfers" => CreateWarehouseTransfers(),
        "e-invoice" or "e-archive" or "incoming-documents" => CreateEDocuments(),
        "cost-centers" => CreateExpenses(),
        "integration-pool" => CreateIntegrationPool(),
        "finance" => CreateFinance(),
        "reports" => CreateReports(),
        _ => CreateGeneric(key)
    };

    private static R3SmartTable CreateAccounts(
        string title = "Cari Yönetimi",
        string description = "Müşteri ve tedarikçi kartları") => new(
        title,
        description,
        [
            new("AccountCode", "CARİ KODU", 120),
            new("LegalName", "CARİ ÜNVANI", 260, Fill: true),
            new("AccountType", "CARİ TİPİ", 130),
            new("TaxNumber", "VERGİ / T.C. NO", 140),
            new("City", "ŞEHİR", 110),
            new("Phone", "TELEFON", 125),
            new("Balance", "BAKİYE", 125, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("CurrencyCode", "DÖVİZ", 70, DataGridViewContentAlignment.MiddleCenter),
            new("Status", "DURUM", 85, DataGridViewContentAlignment.MiddleCenter)
        ],
        [
            new("new-account", "Yeni Cari", true, 86),
            new("account-card", "Cari Kartı", false, 88),
            new("statement", "Ekstre", false, 70),
            new("collection", "Tahsilat", false, 74),
            new("reconciliation", "Mutabakat", false, 88),
            new("refresh", "Yenile", false, 68)
        ]);

    private static R3SmartTable CreateInventory() => new(
        "Stok Yönetimi",
        "Stok kartları, depo miktarları ve rezervasyonlar",
        [
            new("ProductCode", "STOK KODU", 120),
            new("ProductName", "ÜRÜN ADI", 210, Fill: true),
            new("ShortName", "KISA AD", 130),
            new("BrandName", "MARKA", 110),
            new("CategoryName", "KATEGORİ", 120),
            new("SubCategoryName", "ALT KATEGORİ", 120),
            new("ProductGroupName", "ÜRÜN GRUBU", 120),
            new("ProductType", "ÜRÜN TİPİ", 105),
            new("UnitCode", "BİRİM", 70, DataGridViewContentAlignment.MiddleCenter),
            new("Barcode", "ANA BARKOD", 130),
            new("VatRate", "KDV %", 70, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("PurchasePrice", "ALIŞ FİYATI", 110, DataGridViewContentAlignment.MiddleRight, "N4"),
            new("SalesPrice", "SATIŞ FİYATI", 110, DataGridViewContentAlignment.MiddleRight, "N4"),
            new("WholesalePrice", "TOPTAN FİYAT", 115, DataGridViewContentAlignment.MiddleRight, "N4"),
            new("CampaignPrice", "KAMPANYA", 105, DataGridViewContentAlignment.MiddleRight, "N4"),
            new("MinStockLevel", "MİN. STOK", 95, DataGridViewContentAlignment.MiddleRight, "N3"),
            new("MaxStockLevel", "MAKS. STOK", 95, DataGridViewContentAlignment.MiddleRight, "N3"),
            new("CriticalStockLevel", "KRİTİK STOK", 100, DataGridViewContentAlignment.MiddleRight, "N3"),
            new("ShelfCode", "RAF KODU", 90),
            new("AisleCode", "REYON KODU", 95),
            new("SupplierName", "TEDARİKÇİ", 160),
            new("ManufacturerCode", "ÜRETİCİ KODU", 120),
            new("CountryOfOrigin", "MENŞEİ", 80, DataGridViewContentAlignment.MiddleCenter),
            new("WarrantyMonths", "GARANTİ (AY)", 100, DataGridViewContentAlignment.MiddleRight),
            new("Status", "DURUM", 85, DataGridViewContentAlignment.MiddleCenter)
        ],
        [
            new("new-product", "Yeni Ürün", true, 88),
            new("variant", "Varyantlar", false, 82),
            new("barcode", "Barkod", false, 68),
            new("stock-card", "Stok Kartı", false, 82),
            new("movement", "Hareketler", false, 82),
            new("price", "Fiyat Güncelle", false, 98),
            new("refresh", "Yenile", false, 68)
        ]);

    private static R3SmartTable CreateInvoices() => new(
        "Fatura Yönetimi",
        "Satış, alış, iade ve e-Fatura belgeleri",
        [
            new("InvoiceNumber", "FATURA NO", 145),
            new("DocumentNumber", "BELGE NO", 120),
            new("DispatchNumber", "İRSALİYE NO", 120),
            new("InvoiceDate", "TARİH", 100, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy"),
            new("InvoiceType", "FATURA TİPİ", 120),
            new("AccountCode", "CARİ KODU", 110),
            new("AccountName", "CARİ ÜNVANI", 240, Fill: true),
            new("TaxNumber", "VERGİ / T.C.", 110),
            new("GrandTotal", "GENEL TOPLAM", 135, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("CurrencyCode", "DÖVİZ", 70, DataGridViewContentAlignment.MiddleCenter),
            new("DueDate", "VADE", 100, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy"),
            new("PaidTotal", "ÖDENEN", 120, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("RemainingTotal", "KALAN", 120, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("Status", "DURUM", 100, DataGridViewContentAlignment.MiddleCenter)
        ],
        [
            new("new-sales-invoice", "Yeni Satış", true, 88),
            new("open-invoice", "Faturayı Aç", false, 92),
            new("post-invoice", "Onayla", false, 70),
            new("cancel-invoice", "İptal Et", false, 70),
            new("reverse-invoice", "Ters Kayıt", false, 82),
            new("allocate-payment", "Ödeme Eşleştir", false, 106),
            new("invoice-movements", "Hareketler", false, 82),
            new("dispatch-to-invoice", "İrsaliyeden", false, 88),
            new("return-invoice", "İade Oluştur", false, 92),
            new("payment-plan", "Ödeme Planı", false, 92),
            new("send-einvoice", "e-Fatura Gönder", false, 108),
            new("refresh", "Yenile", false, 68)
        ]);

    private static R3SmartTable CreateVariants() => new(
        "Ürün Varyantları",
        "Renk, beden, barkod, fiyat ve depo bazlı varyant takibi",
        [
            new("ProductCode", "MODEL KODU", 115),
            new("ProductName", "ÜRÜN ADI", 210, Fill: true),
            new("VariantCode", "VARYANT KODU", 175),
            new("ColorName", "RENK", 100),
            new("SizeName", "BEDEN", 80, DataGridViewContentAlignment.MiddleCenter),
            new("MainBarcode", "BARKOD", 135),
            new("WarehouseName", "DEPO", 125),
            new("QuantityOnHand", "STOK", 90, DataGridViewContentAlignment.MiddleRight, "N3"),
            new("PurchasePrice", "ALIŞ FİYATI", 110, DataGridViewContentAlignment.MiddleRight, "N4"),
            new("SalesPrice", "SATIŞ FİYATI", 110, DataGridViewContentAlignment.MiddleRight, "N4"),
            new("ShelfCode", "RAF", 75),
            new("AisleCode", "REYON", 75),
            new("Status", "DURUM", 85, DataGridViewContentAlignment.MiddleCenter)
        ]);

    private static R3SmartTable CreateVariantBarcodes() => new(
        "Varyant Barkodları",
        "Ana ve ek barkod tanımları",
        [
            new("Barcode", "BARKOD", 150),
            new("VariantCode", "VARYANT KODU", 180),
            new("ProductCode", "MODEL KODU", 120),
            new("ProductName", "ÜRÜN ADI", 250, Fill: true),
            new("ColorName", "RENK", 110),
            new("SizeName", "BEDEN", 80, DataGridViewContentAlignment.MiddleCenter),
            new("UnitCode", "BİRİM", 75, DataGridViewContentAlignment.MiddleCenter),
            new("IsPrimary", "ANA BARKOD", 100, DataGridViewContentAlignment.MiddleCenter),
            new("Status", "DURUM", 85, DataGridViewContentAlignment.MiddleCenter)
        ]);

    private static R3SmartTable CreateStockMovements() => new(
        "Stok Hareketleri",
        "Stok miktarının değişmez hareket defteri",
        [
            new("TransactionDate", "HAREKET TARİHİ", 140, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("TransactionType", "HAREKET TÜRÜ", 145),
            new("DocumentType", "BELGE TÜRÜ", 110),
            new("DocumentNumber", "BELGE NO", 125),
            new("ProductCode", "STOK", 110),
            new("VariantCode", "VARYANT", 160),
            new("QuantityIn", "GİRİŞ", 90, DataGridViewContentAlignment.MiddleRight, "N3"),
            new("QuantityOut", "ÇIKIŞ", 90, DataGridViewContentAlignment.MiddleRight, "N3"),
            new("UnitCost", "BİRİM MALİYET", 120, DataGridViewContentAlignment.MiddleRight, "N4"),
            new("WarehouseName", "DEPO", 120),
            new("CounterWarehouseName", "KARŞI DEPO", 120),
            new("AccountName", "CARİ HESAP", 170),
            new("UserName", "KULLANICI", 110),
            new("Description", "AÇIKLAMA", 220, Fill: true)
        ]);

    private static R3SmartTable CreatePurchaseOrders() => new(
        "Satın Alma Siparişleri",
        "Tedarikçi siparişleri ve teslimat durumları",
        [
            new("OrderNumber", "SİPARİŞ NO", 135),
            new("SupplierName", "TEDARİKÇİ", 220, Fill: true),
            new("OrderDate", "SİPARİŞ TARİHİ", 110, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy"),
            new("DeliveryDate", "TESLİM TARİHİ", 110, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy"),
            new("WarehouseName", "TESLİM DEPOSU", 135),
            new("CurrencyCode", "DÖVİZ", 70, DataGridViewContentAlignment.MiddleCenter),
            new("PaymentPlanName", "ÖDEME PLANI", 125),
            new("GrandTotal", "GENEL TOPLAM", 130, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("Status", "SİPARİŞ DURUMU", 145, DataGridViewContentAlignment.MiddleCenter)
        ]);

    private static R3SmartTable CreatePurchaseRequests() => new(
        "Satın Alma Talepleri",
        "Şube ve mağazalardan gelen satın alma ihtiyaçları",
        [
            new("RequestNumber", "TALEP NO", 135),
            new("RequestDate", "TALEP TARİHİ", 110, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy"),
            new("RequiredDate", "İHTİYAÇ TARİHİ", 115, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy"),
            new("BranchName", "ŞUBE", 140),
            new("StoreName", "MAĞAZA", 140),
            new("RequestedBy", "TALEP EDEN", 140),
            new("Description", "AÇIKLAMA", 260, Fill: true),
            new("Status", "DURUM", 130, DataGridViewContentAlignment.MiddleCenter)
        ]);

    private static R3SmartTable CreateGoodsReceipts() => new(
        "Mal Kabul",
        "Sipariş ve irsaliyeye bağlı depo girişleri",
        [
            new("ReceiptNumber", "MAL KABUL NO", 140),
            new("ReceiptDate", "KABUL TARİHİ", 140, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("OrderNumber", "SİPARİŞ NO", 130),
            new("SupplierName", "TEDARİKÇİ", 220, Fill: true),
            new("DispatchNumber", "İRSALİYE NO", 130),
            new("DispatchDate", "İRSALİYE TARİHİ", 120, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy"),
            new("WarehouseName", "DEPO", 130),
            new("Status", "DURUM", 110, DataGridViewContentAlignment.MiddleCenter)
        ]);

    private static R3SmartTable CreateExchanges() => new(
        "Değişim İşlemleri",
        "Satış iadesi ve yeni satışı tek değişim kaydında yönetin",
        [
            new("ExchangeNumber", "DEĞİŞİM NO", 130),
            new("ExchangeDate", "TARİH / SAAT", 135, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("SourceInvoice", "ESKİ SATIŞ BELGESİ", 145),
            new("ReturnedProduct", "İADE EDİLEN ÜRÜN", 190),
            new("NewProduct", "YENİ VERİLEN ÜRÜN", 190, Fill: true),
            new("PriceDifference", "FİYAT FARKI", 110, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("CollectedAmount", "TAHSİL EDİLEN", 115, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("RefundedAmount", "İADE EDİLEN", 110, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("Reason", "DEĞİŞİM NEDENİ", 180),
            new("StoreName", "MAĞAZA", 120),
            new("CashName", "KASA", 100),
            new("PersonnelName", "PERSONEL", 125),
            new("Status", "DURUM", 85, DataGridViewContentAlignment.MiddleCenter)
        ]);

    private static R3SmartTable CreateDispatches(string title) => new(
        title,
        "Sevk, teslimat ve kısmi faturalaştırma takibi",
        [
            new("DispatchNumber", "İRSALİYE NO", 135),
            new("DispatchType", "İRSALİYE TÜRÜ", 145),
            new("ShipmentDate", "SEVK TARİHİ", 105, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy"),
            new("ShipmentTime", "SAAT", 70, DataGridViewContentAlignment.MiddleCenter),
            new("AccountName", "CARİ HESAP", 210, Fill: true),
            new("SourceWarehouse", "ÇIKIŞ DEPOSU", 125),
            new("DestinationWarehouse", "TESLİM DEPOSU", 125),
            new("CarrierName", "TAŞIYICI", 125),
            new("VehiclePlate", "PLAKA", 85),
            new("DriverName", "SÜRÜCÜ", 120),
            new("EDispatchUuid", "e-İRSALİYE UUID", 190),
            new("InvoicedRate", "FATURALAŞMA %", 115, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("Status", "DURUM", 110, DataGridViewContentAlignment.MiddleCenter)
        ]);

    private static R3SmartTable CreateAccountMovements() => new(
        "Cari Hareketler",
        "Fatura, tahsilat, ödeme ve diğer borç-alacak hareketleri",
        [
            new("TransactionDate", "TARİH", 125, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("AccountCode", "CARİ KODU", 110),
            new("AccountName", "CARİ ÜNVANI", 220, Fill: true),
            new("TransactionType", "HAREKET TÜRÜ", 140),
            new("DocumentNumber", "BELGE NO", 125),
            new("DueDate", "VADE", 100, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy"),
            new("Debit", "BORÇ", 115, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("Credit", "ALACAK", 115, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("Balance", "BAKİYE", 120, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("CurrencyCode", "DÖVİZ", 65, DataGridViewContentAlignment.MiddleCenter),
            new("Description", "AÇIKLAMA", 190)
        ]);

    private static R3SmartTable CreateCashMovements() => new(
        "Kasa Yönetimi",
        "Mağaza kasaları ve değiştirilemez nakit hareket defteri",
        [
            new("MovementDate", "TARİH / SAAT", 135, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("CashName", "KASA", 130),
            new("StoreName", "MAĞAZA", 125),
            new("ShiftNumber", "VARDİYA", 110),
            new("MovementType", "HAREKET TÜRÜ", 155),
            new("DocumentNumber", "BELGE NO", 120),
            new("AccountName", "CARİ HESAP", 190, Fill: true),
            new("Direction", "YÖN", 70, DataGridViewContentAlignment.MiddleCenter),
            new("Amount", "TUTAR", 120, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("CurrencyCode", "DÖVİZ", 65, DataGridViewContentAlignment.MiddleCenter),
            new("ApprovalStatus", "ONAY", 90, DataGridViewContentAlignment.MiddleCenter)
        ]);

    private static R3SmartTable CreateCashShifts(string title = "Kasa Vardiyaları") => new(
        title,
        "Açılış, fiziksel sayım, sistem bakiyesi ve kasa farkı kontrolü",
        [
            new("ShiftNumber", "VARDİYA NO", 120),
            new("StoreName", "MAĞAZA", 125),
            new("CashName", "KASA", 110),
            new("CashierName", "KASİYER", 120),
            new("OpenedAt", "AÇILIŞ", 130, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("OpeningBalance", "AÇILIŞ BAKİYESİ", 125, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("SystemCashBalance", "SİSTEM NAKİT", 115, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("PhysicalCashBalance", "FİZİKSEL NAKİT", 115, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("CashDifference", "KASA FARKI", 105, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("CardTotal", "KART", 95, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("ReturnTotal", "İADE", 95, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("GiftVoucherTotal", "HEDİYE ÇEKİ", 105, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("ApprovalStatus", "FARK ONAYI", 100, DataGridViewContentAlignment.MiddleCenter),
            new("Status", "DURUM", 85, DataGridViewContentAlignment.MiddleCenter)
        ]);

    private static R3SmartTable CreateBankAccounts() => new(
        "Banka Hesapları",
        "Banka kartları, IBAN ve kredi limitleri",
        [
            new("AccountCode", "HESAP KODU", 110),
            new("BankName", "BANKA", 145),
            new("BankBranchName", "ŞUBE", 130),
            new("AccountName", "HESAP ADI", 180, Fill: true),
            new("Iban", "IBAN", 210),
            new("AccountNumber", "HESAP NO", 120),
            new("CurrencyCode", "DÖVİZ", 65, DataGridViewContentAlignment.MiddleCenter),
            new("AccountingCode", "MUHASEBE KODU", 125),
            new("CreditLimit", "KREDİ LİMİTİ", 120, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("AvailableLimit", "KULLANILABİLİR", 120, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("Status", "DURUM", 85, DataGridViewContentAlignment.MiddleCenter)
        ]);

    private static R3SmartTable CreateBankMovements() => new(
        "Banka Hareketleri",
        "Havale, EFT, FAST, POS, kredi ve banka masrafları",
        [
            new("MovementDate", "TARİH / SAAT", 135, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("BankName", "BANKA HESABI", 160),
            new("MovementType", "HAREKET TÜRÜ", 145),
            new("DocumentNumber", "BELGE NO", 120),
            new("AccountName", "CARİ HESAP", 190, Fill: true),
            new("Direction", "YÖN", 70, DataGridViewContentAlignment.MiddleCenter),
            new("Amount", "TUTAR", 120, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("CurrencyCode", "DÖVİZ", 65, DataGridViewContentAlignment.MiddleCenter),
            new("LocalAmount", "YEREL TUTAR", 120, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("ReferenceNumber", "REFERANS", 130),
            new("ExternalTransactionId", "BANKA İŞLEM NO", 145)
        ]);

    private static R3SmartTable CreatePosAccounts() => new(
        "POS Hesapları",
        "Banka POS'ları, taksit, komisyon ve blokaj ödeme takibi",
        [
            new("PosCode", "POS KODU", 100), new("PosName", "POS ADI", 150),
            new("BankName", "BANKA", 145), new("StoreName", "MAĞAZA", 120),
            new("ReferenceNumber", "İŞLEM REFERANSI", 140),
            new("TransactionDate", "İŞLEM TARİHİ", 125, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("GrossAmount", "İŞLEM TUTARI", 120, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("InstallmentCount", "TAKSİT", 70, DataGridViewContentAlignment.MiddleCenter),
            new("CommissionRate", "KOMİSYON %", 95, DataGridViewContentAlignment.MiddleRight, "N4"),
            new("CommissionAmount", "KOMİSYON", 105, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("BlockingDays", "BLOKAJ GÜNÜ", 95, DataGridViewContentAlignment.MiddleCenter),
            new("NetAmount", "NET ÖDEME", 115, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("ExpectedPaymentDate", "BEKLENEN ÖDEME", 125, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy"),
            new("ActualPaymentDate", "GERÇEK ÖDEME", 115, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy"),
            new("Status", "DURUM", 95, DataGridViewContentAlignment.MiddleCenter)
        ],
        [
            new("new-pos", "Yeni POS", true, 78), new("pos-transaction", "POS İşlemi", false, 86),
            new("settlement", "Blokaj Çöz", false, 88), new("commission", "Komisyonlar", false, 88),
            new("refresh", "Yenile", false, 68)
        ]);

    private static R3SmartTable CreateCheques() => new(
        "Çek Portföyü", "Alınan, verilen, ciro ve tahsilat çeklerinin durum geçmişi",
        [
            new("PortfolioNumber", "PORTFÖY NO", 115), new("ChequeNumber", "ÇEK NO", 115),
            new("ChequeType", "ÇEK TÜRÜ", 125), new("BankName", "BANKA", 130),
            new("DrawerName", "KEŞİDECİ", 170, Fill: true), new("AccountName", "CARİ HESAP", 170),
            new("DueDate", "VADE", 100, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy"),
            new("Amount", "TUTAR", 120, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("CurrencyCode", "DÖVİZ", 65, DataGridViewContentAlignment.MiddleCenter), new("Status", "DURUM", 130)
        ],
        [
            new("new-cheque", "Yeni Çek", true, 78), new("cheque-history", "Durum Geçmişi", false, 102),
            new("send-bank", "Bankaya Ver", false, 94), new("endorse", "Ciro Et", false, 68),
            new("collect", "Tahsil Et", false, 72), new("return", "İade Et", false, 68)
        ]);

    private static R3SmartTable CreatePromissoryNotes() => new(
        "Senet Portföyü", "Alınan, verilen ve ciro edilen senetlerin vade takibi",
        [
            new("NoteNumber", "SENET NO", 120), new("NoteType", "SENET TÜRÜ", 125),
            new("DebtorName", "BORÇLU", 175), new("CreditorName", "ALACAKLI", 175, Fill: true),
            new("IssueDate", "DÜZENLEME", 100, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy"),
            new("DueDate", "VADE", 100, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy"),
            new("Amount", "TUTAR", 120, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("CurrencyCode", "DÖVİZ", 65, DataGridViewContentAlignment.MiddleCenter),
            new("GuarantorName", "KEFİL", 140), new("PaymentPlace", "ÖDEME YERİ", 130), new("Status", "DURUM", 120)
        ],
        [
            new("new-note", "Yeni Senet", true, 86), new("note-history", "Durum Geçmişi", false, 102),
            new("send-collection", "Tahsile Ver", false, 86), new("endorse", "Ciro Et", false, 68),
            new("collect", "Tahsil Et", false, 72)
        ]);

    private static R3SmartTable CreateCampaigns() => new(
        "Kampanya Kural Motoru", "Koşul ve sonuç tabanlı, mağaza ve müşteri odaklı kampanyalar",
        [
            new("CampaignCode", "KAMPANYA KODU", 125), new("CampaignName", "KAMPANYA ADI", 220, Fill: true),
            new("CampaignType", "TÜR", 140), new("StartAt", "BAŞLANGIÇ", 120, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("EndAt", "BİTİŞ", 120, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("Condition", "KOŞUL", 210), new("Result", "SONUÇ", 180), new("Priority", "ÖNCELİK", 70, DataGridViewContentAlignment.MiddleCenter),
            new("IsCombinable", "BİRLEŞEBİLİR", 95, DataGridViewContentAlignment.MiddleCenter), new("Status", "DURUM", 85)
        ],
        [
            new("new-campaign", "Yeni Kampanya", true, 106), new("rule", "Kural Düzenle", false, 96),
            new("simulate", "Sepette Dene", false, 94), new("activate", "Yayınla", false, 70), new("refresh", "Yenile", false, 68)
        ]);

    private static R3SmartTable CreateStockCounts() => new(
        "Stok Sayımları", "Kör sayım, ikinci sayım ve hareket ayrıştırmalı profesyonel sayım",
        [
            new("CountNumber", "SAYIM NO", 120), new("CountType", "SAYIM TÜRÜ", 125),
            new("StoreName", "MAĞAZA", 125), new("WarehouseName", "DEPO", 125),
            new("CountDate", "SAYIM TARİHİ", 105, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy"),
            new("StartedAt", "BAŞLANGIÇ", 125, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("ResponsibleName", "SORUMLU", 140, Fill: true), new("IsBlindCount", "KÖR SAYIM", 85, DataGridViewContentAlignment.MiddleCenter),
            new("DifferenceQuantity", "ADET FARKI", 95, DataGridViewContentAlignment.MiddleRight, "N3"),
            new("DifferenceCost", "MALİYET FARKI", 115, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("Status", "DURUM", 110)
        ],
        [
            new("new-count", "Sayım Emri", true, 92), new("scan", "Barkod Say", false, 84),
            new("second-count", "İkinci Sayım", false, 94), new("differences", "Farkları İncele", false, 102),
            new("approve-count", "Onayla ve İşle", false, 106)
        ]);

    private static R3SmartTable CreateWarehouseTransfers() => new(
        "Depo Transferleri", "Talep, sevk, yoldaki stok ve mağaza kabul süreci",
        [
            new("TransferNumber", "TRANSFER NO", 125), new("RequestDate", "TALEP TARİHİ", 125, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("SourceWarehouse", "KAYNAK DEPO", 140), new("DestinationWarehouse", "HEDEF DEPO", 140),
            new("RequestedQuantity", "TALEP", 85, DataGridViewContentAlignment.MiddleRight, "N3"),
            new("ShippedQuantity", "SEVK", 85, DataGridViewContentAlignment.MiddleRight, "N3"),
            new("InTransitQuantity", "YOLDAKİ", 85, DataGridViewContentAlignment.MiddleRight, "N3"),
            new("ReceivedQuantity", "TESLİM", 85, DataGridViewContentAlignment.MiddleRight, "N3"),
            new("MissingQuantity", "EKSİK", 85, DataGridViewContentAlignment.MiddleRight, "N3"),
            new("Status", "DURUM", 125, Fill: true)
        ],
        [
            new("new-transfer", "Transfer Talebi", true, 106), new("approve-transfer", "Onayla", false, 70),
            new("ship-transfer", "Sevk Et", false, 72), new("receive-transfer", "Mağaza Kabul", false, 96),
            new("difference-report", "Fark Tutanağı", false, 96)
        ]);

    private static R3SmartTable CreateEDocuments() => new(
        "E-Belge Yönetimi", "e-Fatura, e-Arşiv, e-İrsaliye ve özel entegratör durumları",
        [
            new("DocumentNumber", "BELGE NO", 130), new("DocumentType", "E-BELGE TÜRÜ", 125),
            new("DocumentUuid", "UUID", 230), new("IntegratorName", "ENTEGRATÖR", 140),
            new("CreatedAtUtc", "OLUŞTURMA", 130, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("UpdatedAtUtc", "SON GÜNCELLEME", 135, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("Status", "GİB DURUMU", 135), new("LastError", "SON HATA", 260, Fill: true)
        ],
        [
            new("prepare-edoc", "Belge Hazırla", true, 96), new("send-edoc", "Gönder", false, 68),
            new("query-edoc", "GİB Sorgula", false, 88), new("history-edoc", "Durum Geçmişi", false, 102),
            new("cancel-edoc", "İptal Talebi", false, 88)
        ]);

    private static R3SmartTable CreateExpenses() => new(
        "Masraf ve Giderler", "Şube, mağaza, personel ve masraf merkezi bazlı gider yönetimi",
        [
            new("DocumentNumber", "BELGE NO", 120), new("ExpenseDate", "TARİH", 100, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy"),
            new("ExpenseType", "GİDER TÜRÜ", 135), new("BranchName", "ŞUBE", 120), new("StoreName", "MAĞAZA", 120),
            new("CostCenterName", "MASRAF MERKEZİ", 155), new("AccountName", "CARİ", 170, Fill: true),
            new("Amount", "TUTAR", 115, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("VatAmount", "KDV", 100, DataGridViewContentAlignment.MiddleRight, "N2"), new("CurrencyCode", "DÖVİZ", 65), new("Status", "DURUM", 90)
        ],
        [
            new("new-expense", "Yeni Gider", true, 84), new("expense-card", "Gider Kartı", false, 82),
            new("approve-expense", "Onayla", false, 70), new("account-expense", "Muhasebeleştir", false, 104)
        ]);

    private static R3SmartTable CreateIntegrationPool() => new(
        "Muhasebe Entegrasyon Havuzu", "Ön modüllerden oluşan muhasebe kayıtlarının kontrollü aktarımı",
        [
            new("SourceModule", "KAYNAK MODÜL", 125), new("DocumentNumber", "BELGE NO", 125),
            new("DocumentDate", "BELGE TARİHİ", 105, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy"),
            new("DebitAccountCode", "BORÇ HESABI", 115), new("CreditAccountCode", "ALACAK HESABI", 115),
            new("VoucherNumber", "FİŞ NO", 120), new("CreatedAtUtc", "HAVUZA GELİŞ", 130, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("Status", "DURUM", 105), new("ErrorMessage", "HATA / AÇIKLAMA", 260, Fill: true)
        ],
        [
            new("connection-codes", "Bağlantı Kodları", true, 112), new("create-voucher", "Fiş Oluştur", false, 88),
            new("transfer-accounting", "Muhasebeye Aktar", false, 116), new("retry", "Tekrar Dene", false, 88)
        ]);

    private static R3SmartTable CreateFinance() => new(
        "Finans Yönetimi",
        "Kasa, banka, tahsilat ve ödeme hareketleri",
        [
            new("DocumentNumber", "BELGE NO", 135),
            new("TransactionDate", "TARİH / SAAT", 135, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("TransactionType", "İŞLEM TİPİ", 125),
            new("AccountCode", "CARİ KODU", 110),
            new("AccountName", "CARİ ÜNVANI", 220, Fill: true),
            new("FinancialAccount", "KASA / BANKA", 145),
            new("Amount", "TUTAR", 130, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("CurrencyCode", "DÖVİZ", 70, DataGridViewContentAlignment.MiddleCenter),
            new("ReferenceNumber", "REFERANS", 120),
            new("Status", "DURUM", 95, DataGridViewContentAlignment.MiddleCenter)
        ],
        [
            new("collection", "Tahsilat", true, 78),
            new("payment", "Ödeme", false, 70),
            new("open-finance", "İşlemi Aç", false, 82),
            new("post-finance", "Onayla", false, 70),
            new("cancel-finance", "İptal Et", false, 70),
            new("reverse-finance", "Ters Kayıt", false, 82),
            new("cash-transfer", "Kasa Virmanı", false, 94),
            new("bank-transfer", "Banka Virmanı", false, 102),
            new("cash-close", "Kasa Kapat", false, 88),
            new("refresh", "Yenile", false, 68)
        ]);

    private static R3SmartTable CreateReports() => new(
        "Rapor Merkezi",
        "Kaydedilmiş rapor ve analizler",
        [
            new("ReportCode", "RAPOR KODU", 130),
            new("ReportName", "RAPOR ADI", 300, Fill: true),
            new("ModuleName", "MODÜL", 120),
            new("LastRunAt", "SON ÇALIŞMA", 150, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("OwnerName", "OLUŞTURAN", 150),
            new("Status", "DURUM", 90, DataGridViewContentAlignment.MiddleCenter)
        ]);

    private static R3SmartTable CreateManagerApprovals() => new(
        "Görev ve Onaylar", "Limit aşan ve yönetici kararı bekleyen operasyonlar",
        [
            new("RequestedAt", "TALEP TARİHİ", 135, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("ApprovalType", "ONAY TÜRÜ", 150), new("DocumentNumber", "BELGE NO", 120),
            new("StoreName", "MAĞAZA", 135), new("RequestedBy", "TALEP EDEN", 135),
            new("Reason", "GEREKÇE", 230, Fill: true), new("Amount", "TUTAR / FARK", 120, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("Priority", "ÖNCELİK", 90), new("Status", "DURUM", 100)
        ],
        [new("approve", "Onayla", true, 74), new("reject", "Reddet", false, 70), new("details", "Detay", false, 66), new("delegate", "Yönlendir", false, 78)]);

    private static R3SmartTable CreateManagerAlerts() => new(
        "Kritik Uyarılar", "Zincir genelindeki stok, kasa, POS ve entegrasyon uyarıları",
        [
            new("AlertDate", "OLUŞMA", 130, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("Severity", "SEVİYE", 80), new("AlertType", "UYARI TÜRÜ", 155), new("StoreName", "MAĞAZA", 135),
            new("Description", "AÇIKLAMA", 300, Fill: true), new("OwnerName", "SORUMLU", 130),
            new("DueAt", "SON TARİH", 125, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"), new("Status", "DURUM", 95)
        ],
        [new("assign", "Sorumlu Ata", true, 92), new("resolve", "Çözüldü", false, 76), new("open-source", "Kaydı Aç", false, 78), new("refresh", "Yenile", false, 68)]);

    private static R3SmartTable CreateManagerStores() => new(
        "Mağaza İzleme", "Mağaza operasyonları ve canlı çalışma durumu",
        [
            new("StoreCode", "MAĞAZA KODU", 110), new("StoreName", "MAĞAZA", 180, Fill: true),
            new("RegionName", "BÖLGE", 120), new("CurrentSales", "BUGÜNKÜ SATIŞ", 125, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("OpenRegisters", "AÇIK KASA", 85, DataGridViewContentAlignment.MiddleCenter),
            new("CriticalStock", "KRİTİK STOK", 95, DataGridViewContentAlignment.MiddleCenter),
            new("PendingTransfers", "TRANSFER", 85, DataGridViewContentAlignment.MiddleCenter),
            new("LastSyncAt", "SON SENKRON", 135, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"), new("Status", "DURUM", 95)
        ],
        [new("store-dashboard", "Mağaza Paneli", true, 102), new("transactions", "İşlemler", false, 76), new("sync", "Senkronize Et", false, 98), new("refresh", "Yenile", false, 68)]);

    private static R3SmartTable CreateManagerDevices() => new(
        "Cihaz ve Entegrasyon Merkezi", "POS, ÖKC, terazi, yazıcı ve mağaza bridge sağlık durumu",
        [
            new("DeviceName", "CİHAZ", 150), new("DeviceType", "TÜR", 110), new("StoreName", "MAĞAZA", 135),
            new("IpAddress", "IP ADRESİ", 110), new("ConnectionStatus", "BAĞLANTI", 95),
            new("LastTransactionAt", "SON İŞLEM", 130, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("LastSyncAt", "SON SENKRON", 130, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm"),
            new("SoftwareVersion", "SÜRÜM", 85), new("PendingData", "BEKLEYEN", 85, DataGridViewContentAlignment.MiddleCenter),
            new("ErrorCode", "HATA", 100), new("Status", "DURUM", 90, Fill: true)
        ],
        [new("connection-test", "Bağlantı Testi", true, 102), new("sync-device", "Senkronize Et", false, 98), new("device-log", "Logları Aç", false, 86), new("restart", "Yeniden Başlat", false, 102)]);

    private static R3SmartTable CreateManagerAudit() => new(
        "Denetim Kayıtları", "Kritik kullanıcı ve veri değişikliklerinin değiştirilemez geçmişi",
        [
            new("EventDate", "TARİH", 135, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy HH:mm:ss"),
            new("UserName", "KULLANICI", 125), new("WindowsUser", "WINDOWS", 125), new("ModuleName", "MODÜL", 115),
            new("ActionName", "İŞLEM", 120), new("RecordKey", "KAYIT", 130),
            new("OldValue", "ÖNCEKİ DEĞER", 210), new("NewValue", "YENİ DEĞER", 210, Fill: true),
            new("IpAddress", "IP", 105), new("Result", "SONUÇ", 85)
        ],
        [new("audit-detail", "Detay", true, 68), new("filter-critical", "Kritik İşlemler", false, 102), new("export-audit", "Dışa Aktar", false, 86), new("refresh", "Yenile", false, 68)]);

    private static R3SmartTable CreateGeneric(string key)
    {
        R3NavigationItem? item = R3NavigationCatalog.Find(key);
        string title = item?.Title ?? "R3 Çalışma Alanı";
        return new R3SmartTable(
            title,
            $"{title} kayıtları",
            [
                new("Code", "KOD", 140),
                new("Name", "AÇIKLAMA", 320, Fill: true),
                new("DocumentDate", "TARİH", 110, DataGridViewContentAlignment.MiddleCenter, "dd.MM.yyyy"),
                new("Reference", "REFERANS", 150),
                new("Amount", "TUTAR", 130, DataGridViewContentAlignment.MiddleRight, "N2"),
                new("Status", "DURUM", 100, DataGridViewContentAlignment.MiddleCenter)
            ]);
    }
}
