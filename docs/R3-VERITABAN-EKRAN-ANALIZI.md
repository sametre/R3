# R3 ERP — Veritabanı ve Ekran Eşleştirme Analizi

## 1. Mevcut mimari

R3 şu anda üç katmanlı bir masaüstü ERP yapısına sahip:

- `R3.Desktop`: WPF ekranları, menüler, gridler, sağ tık işlemleri ve diyaloglar.
- `R3.Infrastructure`: SQLite yerel veri erişimi ve stok/cari/belge servisleri.
- `R3.Domain` / `R3.Application`: alan modelleri, doğrulamalar ve sunucuya taşınabilecek iş kuralları.

Ana yerel veritabanı `StoreDatabase` tarafından oluşturuluyor. Aynı modelde eski prototip tabloları (`Stores`, `Customers`, `Movements`) ile yeni kanonik tablolar birlikte bulunuyor. Yeni geliştirmelerde kanonik tablolar kullanılmalı; eski tablolar yalnızca uyumluluk/migrasyon amacıyla tutulmalı.

## 2. Kanonik iş alanları

| Alan | Ana tablolar | Mevcut servis/ekran | Eksik veya riskli nokta |
|---|---|---|---|
| Organizasyon | `companies`, `branches`, `warehouses`, `warehouse_locations` | Firma/şube/depo/lokasyon yönetimi | Her belge ekranında bağlam zorunluluğu ve erişim yetkisi tek noktadan doğrulanmalı |
| Stok kartı | `products`, `product_variants`, `product_barcodes`, `product_units`, `product_inventory_policies` | Stok Kartları, barkod, lot/seri | Kart detayının fiyat, tedarikçi, birim, lot/seri ve depo politikası sekmeleri tamamlanmalı |
| Stok hareketi | `inventory_documents`, `inventory_document_lines`, `inventory_transactions`, `inventory_balances` | Stok giriş/çıkış, transfer, sayım | Taslak/onay/ters hareket zinciri ve lokasyon bakiyesi tek transaction içinde korunmalı |
| Cari | `accounts`, `account_addresses`, `account_contacts`, `account_transactions`, `account_balances` | Cari kartı, cari ekstre, cari hareket | Cari alt profilleri, vade ve risk alanları belge ekranlarına bağlanmalı |
| Satınalma | `purchase_documents`, `purchase_document_lines`, `purchase_receipts`, `purchase_receipt_lines` | Sipariş, alış irsaliyesi, alış faturası | Sipariş → irsaliye → fatura ilişkisi ve kısmi teslim senaryoları tamamlanmalı |
| Satış | `sales_documents`, `sales_document_lines`, `despatch_documents`, `shipment_orders` | Satış faturası, irsaliye, sevkiyat | Fatura–irsaliye–sevkiyat zincirinde ortak detay ekranı oluşturulmalı |
| Finans | `cash_accounts`, `cash_transactions`, `bank_accounts`, `bank_transactions`, `cheques` | Kasa, banka, çek/senet | Belge kesinleştiğinde finans hareketinin tekilleştirilmesi test edilmeli |
| E-belge | `electronic_documents`, `electronic_document_outbox`, `electronic_document_events` | E-belge ekranları | Belge post işlemi ile e-belge üretimi arasında idempotent ilişki korunmalı |
| Yetki/audit | `users`, `roles`, `permissions`, `audit_logs` | Kullanıcı/yetki ve audit | Her kritik sağ tık işlemi izin + audit kaydı üretmeli |

## 3. Belge yaşam döngüsü

Belgelerde ortak durum makinesi kullanılmalı:

`Taslak → Onaylandı → Kesinleşti/Sevk Edildi → Kapandı`

İzin verilen istisnalar:

- Taslak belge iptal edilebilir.
- Onaylı sipariş kısmi teslim alabilir (`PartiallyReceived`).
- Kesinleşmiş belge silinmez; ters hareket veya iade oluşturulur.
- Aynı kaynak belge ikinci kez fatura/irsaliye üretmemelidir.
- Stok ve cari etkisi yalnızca onay/kesinleştirme transaction'ında yazılmalıdır.

## 4. Ekran standardı

Her ana liste ekranı aynı sözleşmeyi uygulamalı:

1. Başlık ve kısa açıklama.
2. Özet kartları.
3. Arama, durum ve tarih filtresi.
4. Yeni / Aç-Düzenle / Onayla / İptal / Yenile araçları.
5. Türkçe kolon başlıkları ve doğru veri türü biçimi.
6. Sağ tık menüsünde yalnızca seçili satırın durumuna uygun işlemler.
7. Çift tıklama veya Enter ile detay ekranı.
8. Uzun sorgular UI thread dışında çalışmalı; yükleme katmanı görünür olmalı.

## 5. Öncelikli geliştirme sırası

### A. Ortak belge altyapısı

- Belge başlık/satır ekranı için ortak kontrol ve toplam hesaplama bileşeni.
- Ortak durum, izin, audit ve hata mesajı sözleşmesi.
- Kaynak belge ilişkilerinin (`document_relations`) her akışta gösterilmesi.

### B. Satınalma zinciri

- Sipariş satırlarından kısmi irsaliye oluşturma.
- Depoya alınmış irsaliyeden alış faturası oluşturma.
- İrsaliye satırı, fiyat, KDV, iskonto ve toplamların doğru kolon sırasıyla gösterilmesi.
- Fatura onayı ve kesinleştirme sırasında cari/stok etkisinin tek transaction olması.

### C. Satış zinciri

- Satış irsaliyesi yalnızca kesinleşmiş (`Posted`) satış faturasından oluşturulmalı.
- Satış faturası stok çıkışının tek sahibi olmalı; irsaliye oluşturma ve teslim durum geçişleri stok miktarını tekrar değiştirmemeli.
- Aynı satış faturasından ikinci kez irsaliye oluşturulmamalı; `document_relations` kaydı mevcut irsaliyeyi döndürmeli.
- Fatura → irsaliye → sevkiyat ilişkisi detay ekranında izlenebilir olmalı.

### D. Cari motoru

- Cari kartı → adresler → iletişim → vergi/e-belge → risk/vade sekmeleri.
- Cari ekstrede kaynak belgeye çift tıklama.
- Bakiye projeksiyonunun yalnızca kesinleşmiş hareketlerden hesaplanması.

### E. Stok motoru

- Stok kartı detayında birim, barkod, varyant, fiyat, tedarikçi ve depo politikası.
- Lokasyon/lot/seri zorunluluklarının ürün ve depo politikasından okunması.
- Giriş/çıkış/transfer/sayım işlemlerinde negatif stok kontrolünün merkezileştirilmesi.

## 6. İlk teknik kontrol listesi

- [x] Alış irsaliyesinden alış faturası taslak üretimi eklendi.
- [x] Fatura–irsaliye `document_relations` ilişkisi eklendi.
- [x] Alış faturası satırlarına siparişten KDV ve fiyat aktarımı yapıldı.
- [x] Yerel uygulama derlemesi başarılı.
- [x] Mevcut test seti başarılı: Server 9, Desktop 58 test.
- [ ] Satınalma belge servisleri için ayrı otomasyon testleri eklenmeli.
- [ ] Satış ve satınalma belge detayları ortaklaştırılmalı.
- [x] Ekranlarda İngilizce durum/olay kodları merkezi sunum katmanından Türkçeleştiriliyor (`InventoryPresentation`, `EDocumentPresentation`). Veritabanı kodları iş kuralları için korunuyor.
- [ ] Gerçek MDF aktarımı için legacy tablo eşleştirme ve tekrar çalıştırılabilir migrasyon raporu hazırlanmalı.

## 7. Güncel çalışma kanıtı ve tablo–ekran sözleşmesi

Çalışan yerel mağaza veritabanı:

`C:\Users\mesud\AppData\Local\R3\r3.db`

`StoreDatabase` şeması 95 tablo tanımı içeriyor; uygulama açılışında `PRAGMA user_version` değeri `15` ile kontrol ediliyor. Eski uyumluluk tabloları (`Stores`, `Customers`, `Movements`) ile kanonik tablolar aynı dosyada bulunabildiği için yeni ekranlar eski tablolara doğrudan yazmamalıdır.

| Ekran/iş akışı | Okunan tablolar | Yazılan tablolar | Durum/tekilleştirme kuralı |
|---|---|---|---|
| Stok Kartları | `products`, `brands`, `categories`, `units`, `product_variants`, `product_barcodes`, `product_units`, `product_suppliers`, `product_inventory_policies` | Aynı tablolar + `audit_logs` | Ürün kodu şirket içinde benzersiz; pasif kart hareket ekranlarında seçilemez |
| Depo/Lokasyon | `warehouses`, `warehouse_locations`, `warehouse_location_balances` | Aynı tablolar + `audit_logs` | Depo/şube/firma bağlamı zorunlu; lokasyon zorunluluğu depo politikasından gelir |
| Stok Giriş/Çıkış | `inventory_documents`, `inventory_document_lines` | `inventory_transactions`, `inventory_balances` | Stok yalnızca onay işleminde etkilenir; ters işlem yeni hareket üretir |
| Stok Transferi | `transfer_documents`, `transfer_document_lines` | `inventory_transactions`, `inventory_balances` | Kaynak ve hedef depo/lokasyon aynı işlem bağıntısında güncellenir |
| Cari Kartı/Ekstre | `accounts`, `account_addresses`, `account_contacts`, `customer_profiles`, `supplier_profiles`, `account_transactions`, `account_balances` | Aynı tablolar + `audit_logs` | Bakiye kesinleşmiş cari hareketlerden projekte edilir |
| Alış Sipariş/İrsaliye/Fatura | `purchase_documents`, `purchase_document_lines`, `purchase_receipts`, `purchase_receipt_lines` | `account_transactions`, stok tabloları, `document_relations`, `audit_logs` | İrsaliyeden fatura ilişkisi `ReceiptToInvoice`; faturadan sonra stok ikinci kez yazılmaz |
| Satış Fatura/İrsaliye/Sevkiyat | `sales_documents`, `sales_document_lines`, `despatch_documents`, `despatch_document_lines`, `shipment_orders` | `document_relations`, `shipment_deliveries`, `audit_logs` | Satış irsaliyesi yalnızca kesinleşmiş faturadan oluşur; sevkiyat durum değişimi stok miktarını değiştirmez |
| E-Belge | `electronic_documents`, `electronic_document_outbox`, `electronic_document_events`, `electronic_document_payloads` | Aynı tablolar | Kuyruk/idempotency anahtarı ile aynı işlem iki kez çalışmaz |

Bu tablo, ekranların hangi servis üzerinden veri alması gerektiğinin temel sözleşmesidir: ekran doğrudan SQL ile iş kuralı uygulamaz; sorgu ve mutasyon `Local*Service` sınıfından geçer, ekran yalnızca Türkçe sunum dönüşümünü yapar.

## 8. Güvenli geliştirme kuralı

Var olan kullanıcı verisi silinmeyecek. Şema değişiklikleri `StoreDatabase` içinde tekrar çalıştırılabilir migration olarak yapılacak; servis değişiklikleri önce test veritabanında doğrulanacak, sonra yerel `r3.db` üzerinde ekran akışı test edilecek.
