# Inventory Desktop Workflows

R3 ERP'nin stok ekranları SQLite canonical ledger üzerinden çalışır. `Stok Durumu` yalnızca `inventory_balances` projeksiyonunu okur; `Stok Hareketleri` hareket günlüğünü değiştirilemez olarak listeler.

## Giriş ve çıkış

Stok Giriş ve Stok Çıkış pencereleri şirket, şube ve depo varsayılanlarını WorkspaceContext'ten alır. Ürün kodu/ID, opsiyonel varyant, miktar, maliyet ve referans bilgileriyle `ManualIn` veya `ManualOut` hareketi oluşturulur. Çıkışta kullanılabilir miktar canlı olarak kontrol edilir; yetersiz stokta işlem transaction başlamadan reddedilir.

## Depo transferi

Transfer tek correlation ID ile `TransferOut` ve `TransferIn` çiftini üretir. Kaynak ve hedef depolar aynı olamaz. İki hareket ve iki bakiye güncellemesi aynı SQLite transaction'ında tamamlanır.

## Sayım

Sayım sonucu mevcut bakiye ile karşılaştırılır. Fark pozitifse `CountIncrease`, negatifse `CountDecrease` yazılır; fark sıfırsa hareket oluşturulmaz.

## Ürün kartı

Ürün detayı şirket kapsamında yüklenir. Varyant ve barkod kimlikleri korunarak güncellenir; karttan kaldırılan çocuk kayıtlar silinmez, pasifleştirilir. Hizmet tipindeki ürünler stok hareketine kabul edilmez.

## Rebuild

`LocalInventoryService.RebuildInventoryBalancesAsync` hareket günlüğünü tarih sırasıyla yeniden oynatıp bakiye projeksiyonunu kurar. Bu işlem bakım/administrasyon akışında kullanılmalıdır.
