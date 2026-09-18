# Legacy Field Catalog

SQL Server katalog incelemesinden doğrulanan alanlar canonical modele genişletildi. `STOKKARTI` ürün ana kartı, `STOKBARKOD` barkod, `STOKBIRIM` birim/çarpan ve `STOKTEDARIKCI` tedarikçi eşleşmesi olarak ayrılır. `CARIKART` ortak cari kimliği, `CARIKARTDTY` vergi/iletişim, `MUSTERI` müşteri kredi ve satış ayarları, `TEDARIKCI` tedarikçi ayarları olarak ele alınır.

Doğrulanmayan legacy kodlar tahmin edilmez. Özellikle `CSWRK2`, çeşitli sınıf bayrakları ve sevk/özel üretim referansları veri örneklemesi yapılmadan canonical business kuralına dönüştürülmez.

Yeni SQLite alanları:

- products: satınalma KDV'si, ÖTV, minimum/maksimum stok, minimum sipariş, sipariş katı, satılabilirlik
- accounts: vergi dairesi, vergi numarası, telefonlar, kredi/risk limitleri
- account_addresses: ülke, mahalle, faks, web
- customer_profiles / supplier_profiles: cari tipe özgü kredi, vade, iskonto ve grup alanları

2019 tam yedek ana şema kaynağı; 2024 differential yalnızca doğrulanmış ek alanlar için değişiklik kaynağı kabul edilir.
