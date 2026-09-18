# Barcode Resolution Contract

`LocalBarcodeResolver` tek projection sorgusuyla barkodu, ürünü, varyantı ve birimi birlikte çözer. Barkod metni `Trim` edilir; leading zero karakterleri korunur. Barkod SQLite genelinde unique olsa da sorgu şirket filtresiyle güvenlik sınırını korur.

`QuantityFactor`, `product_barcodes.quantity` alanından gelir. Kullanıcı barkod biriminde 3 girerse stok hareketine temel birim miktarı `3 * QuantityFactor` olarak yazılır. Resolver hizmet ürünü çözebilir; stok yazma servisi hizmet ürününü ayrıca reddeder.

Pasif barkod, ürün veya varyant için envanter akışında kullanıcıya açık hata döndürülür. Barkod bulunamadığında mesaj barkodu içerir ve spam log üretmek için otomatik tekrar yapılmaz.

Sales modülü aynı sözleşmeyi şu zincirde kullanabilir:

`BarcodeResolver → ProductLookup → QuantityFactor → InventoryAvailability → InventoryPosting`

`QuantityAvailable` her yerde `QuantityOnHand - QuantityReserved` olarak tutulur. Rezervasyon motoru bu sprintte eklenmez; `QuantityReserved` sonraki Sales Order rezervasyonları için ayrılmış alandır.
