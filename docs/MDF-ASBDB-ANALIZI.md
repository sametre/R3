# ASBDB_ERKUR02 MDF → R3 Aktarım Analizi

## 1. Dosya ve SQL Server kanıtı

- MDF: `C:\Users\mesud\Desktop\SqlData\ASBDB_ERKUR02.mdf`
- Log: `C:\Users\mesud\Desktop\SqlData\ASBDB_ERKUR02_log.ldf`
- SQL Server örneği: `localhost` / `MSSQLSERVER`
- SQL Server veritabanı adı: `ASBDB_ERKUR02`
- Durum: `ONLINE`, erişim: `MULTI_USER`
- Windows bağlantısı: `SAMET\muhasebe3`

Dosyanın fiziksel adı ile SQL Server içindeki veritabanı adı aynıdır. Önceki `ASBDB_VIMAR01` adı varsayımı doğru değildir; aktarım bağlantılarında kullanılacak gerçek ad `ASBDB_ERKUR02` olmalıdır.

## 2. Hacim özeti

SQL Server katalog sorgusundan alınan başlıca kayıt sayıları:

| Legacy tablo | Kayıt | Ana referans | İş anlamı |
|---|---:|---|---|
| `STOKKARTI` | 35.095 | `STKREF` / `STKKOD` | Stok kartı |
| `STOKBIRIM` | 35.095 | `SBRSTKREF` | Stok temel birimi |
| `STOKBARKOD` | 8 | `SBRKSTKREF` / `SBRKBARKOD` | Barkod |
| `CARIKART` | 21.388 | `CRREF` / `CRKOD` | Cari kartı |
| `FATURA` | 55.678 | `FATREF` | Fatura başlığı |
| `FATURADTY` | 166.389 | `FDTREF` / `FDTFATREF` | Fatura satırı |
| `STKBELGE` | 47.287 | `STBREF` / `STBNO` | Stok fişi/irsaliye başlığı |
| `STKHAR` | 156.317 | `STHREF` / `STHBLGREF` | Stok hareketi |
| `SIPARIS` | 23.487 | `SMREF` / `SMMNO` | Sipariş başlığı |
| `SIPARISDTY` | 36.101 | `SDREF` / `SDSMREF` | Sipariş satırı |
| `KASA` | 48 | `KASAREF` / `KASAKOD` | Kasa kartı |
| `KASAHAR` | 131.665 | `KHREF` / `KHKASAREF` | Kasa hareketi |
| `BANKAHESABI` | 99 | `BHSREF` | Banka hesabı |
| `BANKAHAR` | 98.212 | `BHREF` / `BHCARREF` | Banka hareketi |
| `DEPO` | 30 | `DEPOREF` / `DEPOKOD` | Depo |
| `SUBE` | 10 | `SUBEKOD` | Şube |

Veritabanında çok sayıda boş modül tablosu da bulunduğu için yalnızca tablo varlığına göre aktarım yapılmamalıdır. Aktarım önceliği, kayıt sayısı ve R3 ekran/servis karşılığı birlikte değerlendirilerek belirlenmelidir.

## 3. R3 kanonik eşleştirme

| Legacy kaynak | R3 hedef | Anahtar eşleştirme | Risk / doğrulama |
|---|---|---|---|
| `STOKKARTI` | `products` | `legacy_source='ASBDB_ERKUR02:STOKKARTI'`, `legacy_id=STKREF` | `STKKOD` şirket içinde benzersiz olmalı; `STKTIP`, KDV ve lot tipi dönüştürülmeli |
| `STOKBIRIM` | `product_units`, `units` | `SBRSTKREF=STKREF`, `SBRBRM` | Birim kodu R3 `units` tablosunda yoksa önce oluşturulmalı |
| `STOKBARKOD` | `product_barcodes` | `SBRKSTKREF=STKREF`, barkod | Sadece 8 kayıt bulundu; eksik barkodlar veri yokluğu olarak raporlanmalı |
| `CARIKART` | `accounts` | `CRREF`, `CRKOD` | `CRMUS/CRTED` müşteri/tedarikçi tipine çevrilmeli |
| `CARIKARTDTY` | `account_addresses`, `account_tax_profiles`, `account_contacts` | `CRDREF` ile cari bağlantısı | Adres, vergi ve e-belge alanları ayrıştırılmalı |
| `DEPO` | `warehouses` | `DEPOREF`, `DEPOKOD` | `DEPOSIRKET` ve `DEPOSUBE` firma/şube bağlamına bağlanmalı |
| `SUBE` | `branches` | `SUBESIRKET`, `SUBEKOD` | Firma önce oluşturulmalı |
| `FATURA` | `sales_documents` veya `purchase_documents` | `FATREF`, `FATGC`, `FATTIP` | Alış/satış yönü kod tablosundan kesinleştirilmeli; varsayımla aktarılmamalı |
| `FATURADTY` | `sales_document_lines` veya `purchase_document_lines` | `FDTFATREF=FATREF`, `FDTREF` | `FDTMIKTAR`, `FDTFYTO`, KDV/iskonto ve birim dönüşümü doğrulanmalı |
| `STKBELGE` | `inventory_documents` / `despatch_documents` | `STBREF`, `STBFATREF`, `STBDEPOREF` | Fiş, irsaliye ve fatura ilişkisi belge türü kodlarından ayrılmalı |
| `STKHAR` | `inventory_transactions` | `STHBLGREF=STBREF`, `STHSTKREF=STKREF` | `STHGC`, `STHMIK`, depo ve lot/seri alanları stok hareketine çevrilmeli |
| `SIPARIS` | `purchase_documents` veya satış siparişi hedefi | `SMREF`, `SMCARREF` | `SMGC` alış/satış yönü için kod sözlüğü çıkarılmalı |
| `SIPARISDTY` | ilgili belge satırları | `SDSMREF=SMREF`, `SDSTKREF=STKREF` | Sipariş, teslim ve fatura ilişkisi `document_relations` ile kurulmalı |
| `KASA` / `KASAHAR` | `cash_accounts` / `cash_transactions` | `KASAREF`, `KHREF` | `KHBA` giriş/çıkış, tutar ve cari bağlantısı doğrulanmalı |
| `BANKAHESABI` / `BANKAHAR` | `bank_accounts` / `bank_transactions` | `BHSREF`, `BHREF` | `BHBA`, kur ve döviz tutarları doğrulanmalı |

## 4. Kritik ilişkiler

Aktarım motoru aşağıdaki sırayı izlemelidir:

1. Firma (`SIRKET`) ve şube (`SUBE`) oluşturulur.
2. Birimler, gruplar, kategoriler ve depolar oluşturulur.
3. Stok kartları ve cari kartları, `legacy_source + legacy_id` ile tekrar çalıştırılabilir biçimde aktarılır.
4. Stok birimleri, barkodlar, cari adres/vergi bilgileri ve depo lokasyonları bağlanır.
5. Sipariş başlık/satırları aktarılır.
6. Fatura başlık/satırları aktarılır.
7. Stok fişi ve hareketleri aktarılır; aynı stok etkisi ikinci kez üretilmez.
8. Kasa/banka hareketleri ve cari hareketleri aktarılır.
9. Kaynak–hedef ilişkileri ve aktarım sonucu `legacy_migration_runs`, `legacy_tables`, `legacy_rows` tablolarına yazılır.

## 5. Şimdilik aktarılmaması gerekenler

`LOG*`, `P1*`, `BI*`, web oturumu, geçici (`TEMP`), rapor tasarım, mesaj kuyruğu, bordro, servis ve üretim tabloları ilk stok/cari prototipine doğrudan aktarılmamalıdır. Bunlar ayrı modül kapsamı ve ayrı eşleştirme gerektirir. Kaynakta kayıt bulunması, R3'e aktarılması gerektiği anlamına gelmez.

## 6. Aktarım güvenlik kuralları

- MDF üzerinde yazma yapılmayacak; yalnızca SQL Server `SELECT` ile okunacak.
- İlk faz yalnızca keşif ve doğrulama raporu üretmeli, R3 veritabanına yazmamalıdır.
- Her hedef satır `legacy_source` ve `legacy_id` ile idempotent olmalıdır.
- Anahtar çakışması, eksik cari/stok/depo ve bilinmeyen kodlar hata raporuna yazılmalı; sessizce atlanmamalıdır.
- Başlık/satır toplamları kaynakla karşılaştırılmadan belge kesinleşmiş kabul edilmemelidir.
- Aktarım tamamlanmadan `inventory_transactions`, `account_transactions`, kasa ve banka hareketleri canlı bakiyeye dahil edilmemelidir.

Bu belge, gerçek MDF'nin SQL Server üzerinden doğrulanmış yapısına göre hazırlanmıştır. Kodlama için sonraki güvenli adım, önce yalnızca `STOKKARTI`, `CARIKART`, `DEPO`, `SUBE` için “keşif raporu + dry-run” servisidir; doğrudan toplu yazma yapılmamalıdır.

## 7. Kopya veritabanında canonical-core doğrulaması

Canlı `C:\Users\mesud\AppData\Local\R3\r3.db` yerine `artifacts\migration-probe\r3-probe.db` kopyasında gerçek SQL Server bağlantısı ile `--canonical-core` çalıştırıldı. Sonuç:

```text
accounts: 21388
products: 35095
units: 4
product_barcodes: 8
Errors: 0
RowCount: 56495
```

Kopya hedefteki mevcut kanonik veri doğrulaması:

```text
accounts: 21388
products: 35095
sales_documents: 25131
sales_document_lines: 21177
purchase_documents: 30547
purchase_document_lines: 27497
inventory_transactions: 156316
inventory_balances: 32041
shipment_orders: 23487
shipment_order_lines: 36101
```

Bu test, R3 aktarım aracının gerçek MDF'ye bağlanabildiğini ve çekirdek aktarımın hatasız tamamlandığını kanıtlar; ancak canlı veritabanına aktarım izni anlamına gelmez. Importer'ın cari kartı başına ek iletişim/banka/not sorguları çalıştırması nedeniyle 56.495 satırlık bu fazın tamamlanması birkaç dakika sürmüştür. Canlı aktarım öncesinde bu satır-bazlı yardımcı sorgular toplu sorguya çevrilerek süre ölçülmelidir.

## 8. 23.09.2026 canonical belge doğrulaması

Temiz bir probe kopyasında (`artifacts\migration-probe\r3-probe3.db`) gerçek SQL Server kaynağına bağlanılarak belge aktarımı çalıştırıldı. Kaynak kimliği ile eşleşen çekirdek kayıtlar:

```text
accounts (ASBDB_ERKUR02): 21388
products (ASBDB_ERKUR02): 35095
```

Belge fazının tek çalıştırmadaki sonucu:

```text
sales_documents: 25131
purchase_documents: 30547
document_lines: 48674
inventory_transactions: 156316
Errors: 0
```

Kimlik çakışmasını önlemek için fatura başlığı, fatura satırı ve stok hareketi kimlikleri `kaynak_veritabanı + legacy_id` bileşimiyle üretilir. Canlı veritabanında çalıştırmadan önce yedek, dry-run ve ayrı hedef doğrulaması zorunludur.

`FATURADTY` ile `STKHAR` arasında doğrudan satır anahtarı olmayan kayıtlarda satır numarası (`ROW_NUMBER`) eşleştirmesi kontrollü bir heuristiktir. Canlı kesin aktarım öncesi örnek faturalar ürün, miktar, fiyat, iskonto ve KDV bazında karşılaştırılmalıdır.

## 9. Canlı R3 store çekirdek bağlama doğrulaması

23.09.2026 tarihinde canlı store üzerinde yalnızca `--canonical-core` çalıştırıldı. Fatura ve stok hareketi belge fazı tekrar çalıştırılmadı; mevcut kanonik belgeler iki kez üretilmedi.

Yedek:

```text
artifacts\migration-probe\r3-live-before-core-20260923.db
```

Canlı hedef doğrulaması:

```text
accounts: 21388
products: 35095
sales_documents: 25131
purchase_documents: 30547
inventory_transactions: 156316
inventory_balances: 32041
accounts (ASBDB_ERKUR02): 21388
products (ASBDB_ERKUR02): 35095
Errors: 0
```

Böylece mevcut cari ve stok kartları artık MDF kaynağındaki `CRREF`/`STKREF` kimlikleriyle yeniden eşleştirilebilir durumdadır. Belge tablolarının mevcut satırlarında legacy metadata bulunmadığı için belge fazının canlı store üzerinde çalıştırılması ayrı bir uyumluluk/backfill adımı gerektirir; probe doğrulaması tamamlanmadan canlıda çalıştırılmamalıdır.
