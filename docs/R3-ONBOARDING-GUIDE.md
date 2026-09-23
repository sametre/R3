# R3 ERP Onboarding Guide

## Genel bakış

R3, Windows üzerinde çalışan WPF tabanlı ticari ERP uygulamasıdır. Masaüstü uygulaması yerel SQLite veritabanını kullanır; kanonik veri modeli stok, cari, satınalma, satış, depo/lokasyon, finans ve e-belge iş alanlarını kapsar.

## Teknoloji yığını

| Katman | Teknoloji |
|---|---|
| Dil | C# / .NET 10 |
| Masaüstü | WPF, WPF-UI, AvalonDock, Fluent.Ribbon, HandyControl |
| Yerel veri | Microsoft.Data.Sqlite / SQLite |
| Sunucu veri | EF Core / PostgreSQL altyapısı |
| Doğrulama | FluentValidation ve alan servisleri |
| Test | xUnit, Microsoft.NET.Test.Sdk |
| Rapor/ofis | ClosedXML, OpenXML, FastReport |

## Mimari

```text
R3.Desktop (WPF ekranı / menü / sağ tık)
        ↓
Local*Service + ViewModel (iş akışı ve sorgular)
        ↓
StoreDatabase (SQLite kanonik model)

R3.Server → R3.Infrastructure / EF Core → PostgreSQL (sunucu yolu)
```

Masaüstü ekranları çoğunlukla doğrudan `Local*Service` sınıflarını kullanır. Yeni iş kuralı önce Infrastructure servisinde transaction ve doğrulama ile uygulanmalı, ekran yalnızca bu servisi çağırmalıdır.

## Önemli giriş noktaları

- `src/R3.Desktop/App.xaml.cs`: WPF başlangıcı ve uygulama servisleri.
- `src/R3.Desktop/MainWindow.xaml.cs`: ana menü, çalışma bağlamı ve tab açma yönlendirmeleri.
- `src/R3.Infrastructure/StoreDatabase.cs`: SQLite şeması, indeksler ve yerel veritabanı başlatma.
- `src/R3.Infrastructure/LocalInventoryService.cs`: stok hareketlerinin merkezi servisi.
- `src/R3.Infrastructure/LocalAccountService.cs`: cari kart ve cari alt profilleri.
- `src/R3.Infrastructure/LocalPurchasingService.cs`: satınalma sipariş/fatura akışı.
- `src/R3.Infrastructure/LocalPurchaseReceiptService.cs`: alış irsaliyesi ve irsaliyeden fatura dönüşümü.
- `src/R3.Infrastructure/LocalSalesService.cs`: satış belgesi ve stok/cari etkileri.
- `src/R3.Infrastructure/LocalDespatchService.cs`: irsaliye/sevkiyat ilişkileri.
- `tests/R3.Domain.Tests`: servis ve veri akışı testleri.
- `tests/R3.Desktop.Tests`: sağ tık, izin ve masaüstü davranış testleri.

## Veri akışı örneği: satınalma

1. Kullanıcı satınalma siparişi ekranından taslak oluşturur.
2. Sipariş onaylanınca belge numarası üretilir.
3. Mal kabul miktarı `purchase_receipts` ve `purchase_receipt_lines` tablolarına yazılır.
4. İrsaliye onayında stok transaction ve depo bakiyesi güncellenir.
5. “İrsaliyeden Fatura” işlemi satırları `purchase_documents` / `purchase_document_lines` tablolarına taşır.
6. `document_relations` üzerinde `PurchaseReceipt → PurchaseInvoice` ilişkisi oluşturulur.
7. Fatura onayı ve kesinleştirme cari hareketi ile gerekiyorsa stok etkisini transaction içinde üretir.

## Kanonik tablo grupları

- Organizasyon: `companies`, `branches`, `warehouses`, `warehouse_locations`.
- Stok: `products`, `product_variants`, `product_barcodes`, `product_units`, `inventory_*`.
- Cari: `accounts`, `account_addresses`, `account_contacts`, `account_transactions`, `account_balances`.
- Satınalma: `purchase_documents`, `purchase_document_lines`, `purchase_receipts`, `purchase_receipt_lines`.
- Satış/irsaliye: `sales_documents`, `sales_document_lines`, `despatch_documents`, `shipment_orders`.
- Finans: `cash_*`, `bank_*`, `cheques`.
- E-belge: `electronic_documents`, `electronic_document_outbox`, `electronic_document_events`.
- Denetim/yetki: `users`, `roles`, `permissions`, `audit_logs`.

## Kodlama kuralları

- Kullanıcıya görünen tüm metinler Türkçe olmalı.
- Para ve miktar hesapları `decimal` ile yapılmalı; toplamlar satır bazında tekrar hesaplanmalı.
- Belge durumları servis katmanında doğrulanmalı; ekran yalnızca uygun aksiyonu göstermeli.
- Stok/cari/belge etkileri tek transaction içinde yazılmalı.
- Kesinleşmiş belgeler silinmemeli; ters hareket veya iade oluşturulmalı.
- Uzun sorgular WPF UI thread’i dışında çalışmalı ve `LoadingOverlay` göstermeli.
- Veritabanı değişiklikleri tekrar çalıştırılabilir migration/DDL ile yapılmalı.
- Mevcut kullanıcı değişiklikleri ezilmemeli; geliştirme öncesi `git status` kontrol edilmeli.

## Sık kullanılan komutlar

```powershell
dotnet build src/R3.Desktop/R3.Desktop.csproj --no-restore -p:NoWarn=CA1416 -p:GenerateAssemblyInfo=false -p:GenerateTargetFrameworkAttribute=false
dotnet test --no-restore --nologo
dotnet run --project src/R3.Desktop
```

## Geliştirme önceliği

1. Ortak belge detay ve toplam bileşenleri.
2. Satınalma sipariş → irsaliye → fatura testleri ve ekran detayları.
3. Cari kaynak belge navigasyonu ve risk/vade bağları.
4. Stok kartı sekmeleri: birim, barkod, fiyat, tedarikçi, lot/seri ve depo politikası.
5. Satış fatura → irsaliye → sevkiyat zinciri.
6. MDF/legacy veri eşleştirmeleri ve tekrar çalıştırılabilir aktarım raporu.
