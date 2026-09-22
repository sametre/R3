# ASBDB ERKUR 2019 Yedek Analizi

## Kaynak ve yöntem

`ASBDB_ERKUR.rar` içindeki iki SQL Server tam yedeği kaynak dosyalara yazmadan, doğrudan MTF/MDF katalog okuyucusuyla incelendi.

| Dosya | Veritabanı | Boyut | Kullanıcı tablosu |
| --- | --- | ---: | ---: |
| `ASBDB_ERKUR011710.BAK` | `ASBDB_ERKUR01DB2008` | 2.233.785.856 bayt | 816 |
| `ASBDB_ERKUR021710.BAK` | `ASBDB_ERKUR02DB2008` | 526.356.992 bayt | 816 |

İki dosya aynı backup setinin parçaları değildir; aynı şemaya sahip iki şirket/veritabanıdır. Katalogda 883 toplam, 816 kullanıcı tablosu vardır. Bu nedenle R3 migration kaynağı `ASB:<database-name>` olarak tutulmalı ve iki kaynak ayrı şirket eşlemesine bağlanmalıdır.

## Ekran bazlı doğrulanmış tablo aileleri

### Ürün ve stok

- `STOKKARTI` (50 kolon): ürün kimliği, kod/ad, grup/tip, KDV/ÖTV, minimum-maksimum stok, minimum sipariş, sipariş katı, renk/beden, teslim süresi ve satış bayrakları.
- `STOKBARKOD` (5): barkod → stok kartı/varyant bağlantısı.
- `STOKBIRIM` (4): stok birimi ve çarpanı.
- `STOKKARTIRESIM` (6): ürün görseli bağlantıları.
- `STKSAYIM` (10) ve `STKSAYIMDTY` (8): sayım başlığı ile ürün/varyant miktar satırları.
- `STKHAR` (24), `STKBELGE` (36), `STOKYER` (8), `STOKSUBEMINMAX` (5): hareket, belge, lokasyon ve şube bazlı stok politikaları.

R3 karşılığı: `products`, `product_variants`, `product_barcodes`, `inventory_transactions`, `inventory_balances`. Sayım ekranında legacy teknik kimlikleri kullanıcıya göstermek yerine barkod/ürün kodu çözümleme kullanılmalıdır.

### Cari, müşteri ve tedarikçi

- `CARIKART` (16): ortak cari kimliği; müşteri, tedarikçi, personel, banka ve diğer rol bayrakları.
- `CARIKARTDTY` (35): unvan, adres, vergi, telefon, e-posta, web ve e-Fatura ayarları.
- `MUSTERI` (64): satış şartı, ödeme/fiyat kodu, vade, kredi/risk, temsilci, sipariş engeli, KVKK ve scoring alanları.
- `TEDARIKCI` (13): vade, ülke/il, mutabakat, grup/bölge ve kişi/tüzel bilgisi.
- `STOKTEDARIKCI` (7): ürün-tedarikçi kodu, aktiflik, termin günü ve öncelik.
- `CARIYETKILI` (15), `CARISEVKADRES` (30), `CARIBANKA` (8), `CARINOT` (9): yetkili, sevk adresi, banka ve not alt kayıtları.

R3 karşılığı ortak `accounts` agregası ve `customer_profiles` / `supplier_profiles` uzantılarıdır. Tedarikçi ekranı ayrı bir paralel cari modeli kurmamalı; `AccountType=Supplier` filtresi kullanmalıdır.

### Sipariş, sevkiyat ve teslimat

- `SIPARIS` (48) ve `SIPARISDTY` (63): sipariş başlık/satır, planlanan teslim, miktar, sevk edilmiş ve bakiye miktarları.
- `SEVKEMRI` (20): sevk tarihi, müşteri, bölge, nakliyeci, depo, durum, teslim alan ve açıklama.
- `SEVKEMRIDTY` (9): sipariş satırı, miktar, ikinci birim, depo, stok ve workflow durumu.
- `YUKLEME` (13) ve `YUKLEMEDTY` (7): plaka, sürücü, nakliyeci, yükleme sonucu ve sıra.
- `TESLIMHAR` (22): teslim alan, sonuç, koordinat, teslim edildi/son teslim/silindi bayrakları.

R3’e `shipment_orders`, `shipment_order_lines`, `shipment_deliveries` canonical modeli eklendi. Bekleyen sevkiyat ekranı artık bu tablolardan gerçek sorgu çalıştırır; `SEVKEMRI` başlığı `shipment_orders`, `SEVKEMRIDTY` satırı `shipment_order_lines`, `TESLIMHAR` sonucu `shipment_deliveries` olarak taşınmalıdır. Yükleme araç/sürücü alanları ilk aşamada sevkiyat başlığında korunur.

### Finans

- `KASA` (10), `KASAHAR` (28), `KASAHARDTY` (14): kasa kartı ve hareketleri.
- `BANKAHESABI` (12), `BANKAHAR` (26), `BANKAHARDTY` (18): banka hesabı ve hareketleri.
- `CEKSENET` (29), `CESBORDRO` (25), `CEKKARNE` (9): çek/senet yaşam döngüsü.
- `TAHSILAT` (19), `TAHSILATDTY` (7), `ODEMEEMRI` (7): tahsilat ve ödeme emri.
- `FATURA` (72), `FATURADTY` (55): fatura başlık/satır ve finansal toplamlar.

Finans genel bakışı R3 `cash_balances`, `cash_transactions`, `account_balances`, `account_transactions` ve `sales_documents` projeksiyonlarını birlikte kullanır. Banka ve çek/senet için legacy tablo ailesi doğrulandı ancak R3 canonical tabloları henüz tamamlanmadığı için mevcut kasa/cari verisine karıştırılmamalıdır.

### Kullanıcı, rol ve yetki

- `MNUSER` (17): kullanıcı, aktiflik, grup, varsayılan şirket/şube/depo ve parola hash alanları.
- `MNUSERGRUP` (3): grup kodu/adı ve şirket.
- `YETKI` (7): tip + dört seviyeli kod + kullanıcı grubu + izin bayrağı.
- `YETKITANIM` (6): aynı dört seviyeli anahtarın açıklaması.
- `KODSTOKGRUPYETKI`, `STOKGRUPKTGYETKI`, `KODFIYATYETKI`, `SATISELEMANIYETKILIGRUP`: veri kapsamı yetkileri.

R3 karşılığı `users`, `roles`, `permissions`, `user_roles`, `role_permissions` yapısıdır. Legacy anahtarlar `permissions.legacy_key` içinde tutulmalı; kapsam yetkileri yalnızca boolean ekran iznine indirgenmemelidir.

### Organizasyon ve ayarlar

- `SIRKET` (26), `SUBE` (33), `DEPO` (12): firma, şube, depo, adres/vergi/iletişim ve varsayılan sevk bilgileri.
- `PARAMETRE` (15), `PARAMETRETNM` (11), `PRMSTOK` (28), `PRMMAGAZA` (47), `PRMMAGAZASUBE` (41), `PRMASB` (127): genel ve modül bazlı parametreler.

Genel ayarlar ekranı artık R3 organizasyon ve güvenlik tablolarını gerçek veriden gösterir. Legacy parametreler tek bir serbest anahtar/değer tablosuna körlemesine aktarılmamalı; iş kuralı doğrulanan alanlar modül ayarlarına ayrılmalıdır.

## Uygulanan ekran iyileştirmeleri

- Bekleyen sevkiyatlar: gerçek canonical kuyruk, özet kartları, arama ve sevkiyat alanları.
- Finans genel bakış: kasa, müşteri alacağı, tedarikçi borcu, günlük/aylık satış ve birleşik son hareketler.
- Gönderim kuyruğu: `electronic_documents` + son outbox denemesi, durum/hata/tekrar bilgileri.
- Genel ayarlar: firma/şube/depo/kullanıcı/rol/yetki sayıları ve organizasyon listesi.
- Sayım/operasyon penceresi: teknik ürün/varyant UUID alanları kaldırıldı; barkod veya ürün kodu ile çözümleme, ürün seçici ve mevcut stok bilgisi eklendi.

## Sonraki migration sırası

1. Organizasyon ve güvenlik: `SIRKET`, `SUBE`, `DEPO`, `MNUSER*`, `YETKI*`.
2. Cari: `CARIKART`, `CARIKARTDTY`, rol profilleri ve alt kayıtlar.
3. Ürün: `STOKKARTI`, birim, barkod, varyant, görsel ve tedarikçi bağlantısı.
4. Açılış bakiyeleri ve stok hareketleri.
5. Sipariş → sevk emri → yükleme → teslim ilişkileri.
6. Finans belgeleri; mutabakat sonrası banka ve çek/senet yaşam döngüsü.

Tüm aktarım adımları `legacy_source` + `legacy_id` ile idempotent olmalı, kaynak yedekleri salt okunur tutulmalı ve bilinmeyen kısa kodların anlamı örnek veri/iş kuralı doğrulanmadan tahmin edilmemelidir.
