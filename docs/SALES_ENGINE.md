# Sales Engine V1

`LocalSalesService` Draft ve Posted yaşam döngüsünü yönetir. Draft kayıtları stok ve cari ledger'a dokunmaz. `Post` tek SQLite transaction içinde satırları yeniden hesaplar, stok ürünlerinde `SaleIssue` oluşturur, hizmet ürünlerini stoktan hariç tutar, cari hesabı borçlandırır ve belge numarası üretir.

Post sırasında stok yetersizliği, pasif ürün/cari veya ikinci kez gönderim hatası transaction'ı geri alır. Fatura numarası şirket, belge tipi ve yıl kapsamında `SF-YYYY-NNNNNN` biçimindedir.
