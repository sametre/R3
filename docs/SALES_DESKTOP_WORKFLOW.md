# Sales Desktop Workflow

Sales masaüstü akışı cari seçimi, ürün/barkod çözümleme, taslak kaydı ve tek adımlı post işleminden oluşur. Draft durumunda satırlar ve toplamlar düzenlenebilir; Post çağrısı stok ürünlerinde `SaleIssue`, cari hesapta `SalesInvoice` Debit ve audit kaydı üretir. Posted belgeler tekrar düzenlenmez.

Tarihsel belge aktarımı ile operasyonel Post birbirinden ayrıdır. Tarihsel stok ve cari hareketleri ayrıca aktarılmışsa aynı belgeyi yeniden Post etmek çift kayıt oluşturur; migration aracı ileride açık bir historical import modu kullanmalıdır.
