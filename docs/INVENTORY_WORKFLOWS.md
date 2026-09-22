# Inventory Desktop Workflows

R3 ERP'nin stok ekranları SQLite canonical ledger üzerinden çalışır. `Stok Durumu` yalnızca `inventory_balances` projeksiyonunu okur; `Stok Hareketleri` hareket günlüğünü değiştirilemez olarak listeler.

## Giriş ve çıkış

Stok Giriş ve Stok Çıkış pencereleri şirket, şube ve depo varsayılanlarını WorkspaceContext'ten alır. Kullanıcı kaydettiğinde önce `inventory_documents` ve `inventory_document_lines` üzerinde `Draft` fiş oluşturulur; ardından onay aynı SQLite transaction'ı içinde yapılır. Onay aşamasında `ManualIn` veya `ManualOut` hareketi, fiş numarası ve `document_id` ile değiştirilemez `inventory_transactions` günlüğüne yazılır ve `inventory_balances` projeksiyonu güncellenir. Böylece taslak stok miktarını değiştirmez ve onaylanmış fiş ikinci kez onaylanamaz.

Fiş numaraları girişte `SG-YYYY-000001`, çıkışta `SC-YYYY-000001` biçimindedir. Çıkışta kullanılabilir miktar onay transaction'ı içinde kontrol edilir; yetersiz stokta fiş `Draft` kalır, hareket ve bakiye değişikliği oluşmaz.

## Depo transferi

Transfer tek correlation ID ile `TransferOut` ve `TransferIn` çiftini üretir. Kaynak ve hedef depolar aynı olamaz. İki hareket ve iki bakiye güncellemesi aynı SQLite transaction'ında tamamlanır.

## Sayım

Sayım sonucu mevcut bakiye ile karşılaştırılır. Fark pozitifse `CountIncrease`, negatifse `CountDecrease` yazılır; fark sıfırsa hareket oluşturulmaz.

## Ürün kartı

Ürün detayı şirket kapsamında yüklenir. Varyant ve barkod kimlikleri korunarak güncellenir; karttan kaldırılan çocuk kayıtlar silinmez, pasifleştirilir. Hizmet tipindeki ürünler stok hareketine kabul edilmez.

## Rebuild

`LocalInventoryService.RebuildInventoryBalancesAsync` hareket günlüğünü tarih sırasıyla yeniden oynatıp bakiye projeksiyonunu kurar. Bu işlem bakım/administrasyon akışında kullanılmalıdır.
