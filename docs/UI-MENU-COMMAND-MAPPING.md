# Classic Menu → Ribbon Command Mapping

Read-only analysis. No file was changed to produce this table — it compares
`BuildVisibleMenu()` (the classic `MainMenu`, `MainWindow.xaml.cs:225`) against
`BuildMenu()` (the `fluent:Ribbon`, `MainWindow.xaml.cs:252`) as they exist on
disk as of 2026-09-22 (after the concurrent Depo/Lokasyon work landed).

Per the user's instruction, **`MainMenu` must not be removed until this table
shows full coverage** (or the gaps below are explicitly accepted). Right now
it does not — three real gaps and two handler mismatches were found.

**Yetki (permission) column:** no menu-level permission/authorization check
exists in either `BuildVisibleMenu()` or `BuildMenu()` today — confirmed by
searching `MainWindow.xaml.cs` for any permission/authorization check
(`grep -n "permission\|Permission\|HasAccess\|CanAccess\|IsAuthorized"`,
zero matches). Every row's Yetki value is "Yok" because there is nothing to
migrate here, not because it was omitted from this table.

**Klavye kısayolu column:** no `KeyBinding`/`InputBindings`/
`InputGestureText` exists on either menu (confirmed by search). Screen-level
shortcuts (F2/F3/F5/Ctrl+F/Esc, via `KeyboardInteractionService`) are
attached to the opened screen, not the menu item, and are unaffected by
which menu opens that screen. Every row's shortcut value is "Yok" for the
same reason.

## Mağaza

| Mevcut menü | Mevcut komut | Yetki | Yeni Ribbon sekmesi | Yeni grup | Yeni komut | Durum |
| --- | --- | --- | --- | --- | --- | --- |
| Giriş | Giriş ekranı | Yok | Mağaza | (top-level entry) | Giriş ekranı → `HomeDocument.IsActive=true` | ✅ aynı handler, farklı yer (Ribbon'da kendi sekmesi yok, Mağaza altında) |
| Mağaza | Cari Genel Bakış | Yok | Cari | (top-level entry) | `OpenAccountDashboard` | ✅ aynı handler, sekme değişti |
| Mağaza | Müşteri Kartları | Yok | Mağaza | (top-level entry) | "Müşteri kartları" → `OpenDefinitions(false)` | ⚠️ **Farklı handler.** Classic çağırıyor `OpenCanonicalAccounts("Customer","Müşteriler")`; Ribbon çağırıyor `OpenDefinitions(false)`. İki ayrı kod yolu — hangisinin doğru davranış olduğu netleşmeden Ribbon'a güvenilemez. |
| Mağaza | Yeni Satış | Yok | Mağaza | Perakende Satış | "Yeni satış" → `Planned` | ✅ ikisi de placeholder, tutarlı |
| Mağaza | Sevkiyat Takibi | Yok | Mağaza | Sevkiyat Takibi | "Bekleyen sevkiyatlar" → `OpenPendingShipments` | ✅ aynı handler |

## Cari

| Mevcut menü | Mevcut komut | Yetki | Yeni Ribbon sekmesi | Yeni grup | Yeni komut | Durum |
| --- | --- | --- | --- | --- | --- | --- |
| Cari | Cari Kartlar | Yok | Cari | (top-level) | `OpenCanonicalAccounts()` | ✅ |
| Cari | Müşteriler | Yok | Cari | (top-level) | `OpenCanonicalAccounts("Customer",…)` | ✅ |
| Cari | Tedarikçiler | Yok | Cari | (top-level) | `OpenCanonicalAccounts("Supplier",…)` | ✅ |
| Cari | Cari Hareketler | Yok | Cari | (top-level) | `OpenAccountTransactions()` | ✅ |
| Cari | Cari Ekstre | Yok | Cari | (top-level) | `OpenAccountStatement()` | ✅ |
| Cari | Risk ve Kredi | Yok | Cari | (top-level) | `OpenCreditRisk` | ✅ |

Not: Classic menüde yok ama Ribbon'da var (kayıp değil, ek): Cari Tanımları
grubu (Cari Grupları, Bölgeler, Sevk Bölgeleri, Fiyat Listeleri).

## Stok

| Mevcut menü | Mevcut komut | Yetki | Yeni Ribbon sekmesi | Yeni grup | Yeni komut | Durum |
| --- | --- | --- | --- | --- | --- | --- |
| Stok › Ürün Yönetimi | Stok Kartları | Yok | Stok | Ürün Yönetimi | `OpenProductList` | ✅ |
| Stok › Ürün Yönetimi | Yeni Stok Kartı | Yok | Stok | Ürün Yönetimi | `OpenProductList()` | ✅ |
| Stok › Ürün Yönetimi | Toplu Ürün İşlemleri | Yok | Stok | Ürün Yönetimi | `Planned` | ✅ |
| Stok › Stok Fişleri | Stok Giriş/Çıkış Fişleri, Depo Transferleri, Stok Sayımı, Stok Rezervasyonları | Yok | Stok | Stok Operasyonları | aynı 5 komut, aynı handler'lar | ✅ |
| Stok › Stok Raporları | Stok Durumu, Hareketleri, Kritik, Stoksuz, Değer Raporu, Ürün Ekstresi | Yok | Stok | Stok Raporları | aynı 6 komut, aynı handler'lar | ✅ |
| Stok › Depo ve Lokasyonlar | Depolar, Depo Lokasyonları, Raf/Göz Tanımları | Yok | Stok | Depo ve Lokasyonlar | `OpenWarehouseManagement`/`OpenWarehouseLocations`/`OpenWarehouseLocations` | ✅ tam eşleşme (her iki taraf da aynı yeni Depo/Lokasyon servisini çağırıyor) |
| Stok › İzleme ve Ayarlar | Lot/Seri Takip, Negatif Stok Politikası, Barkod Sorgulama, Barkod Yazdırma | Yok | Stok | İzleme + Barkod + Stok Ayarları (3 ayrı grup) | aynı 4 komut, aynı handler'lar | ✅ kapsam aynı, gruplama farklı |

## Satınalma

| Mevcut menü | Mevcut komut | Yetki | Yeni Ribbon sekmesi | Yeni grup | Yeni komut | Durum |
| --- | --- | --- | --- | --- | --- | --- |
| Satınalma | Satınalma Siparişleri | Yok | Satınalma | (top-level) | `OpenPurchaseDocuments("Order")` | ✅ |
| Satınalma | Alış Faturaları | Yok | Satınalma | (top-level) | `OpenPurchaseDocuments("Invoice")` | ✅ |
| Satınalma | Satınalma İadeleri | Yok | Satınalma | (top-level) | `Planned("Satınalma iadeleri")` | ✅ (başlık büyük/küçük harf farkı var, kozmetik) |

## Satış

| Mevcut menü | Mevcut komut | Yetki | Yeni Ribbon sekmesi | Yeni grup | Yeni komut | Durum |
| --- | --- | --- | --- | --- | --- | --- |
| Satış | Yeni Satış Faturası | Yok | Satış | (top-level) | `OpenNewSalesInvoice` | ✅ |
| Satış | Satış Faturaları | Yok | Satış | (top-level) | `OpenSalesList` | ✅ |
| Satış | Satış İadeleri | Yok | Satış | Satış Yönetimi | "İade işlemleri" → `Planned` | ✅ isim farklı ama aynı placeholder kavram |
| Satış | **Sevkiyat** | Yok | — | — | **yok** | ❌ **Gerçek boşluk.** Ribbon'un Satış sekmesinde Sevkiyat'a giden hiçbir komut yok; aynı ekrana (`OpenPendingShipments`) sadece Mağaza sekmesi üzerinden ulaşılabiliyor. |

## Finans

| Mevcut menü | Mevcut komut | Yetki | Yeni Ribbon sekmesi | Yeni grup | Yeni komut | Durum |
| --- | --- | --- | --- | --- | --- | --- |
| Finans | Genel Bakış | Yok | Finans | (top-level) | `OpenFinanceOverview` | ✅ |
| Finans | Kasa Kartları | Yok | Finans | Kasa Yönetimi | `OpenCashAccounts` | ✅ |
| Finans | Kasa Hareketleri | Yok | Finans | Kasa Yönetimi | `OpenCashTransactions()` | ✅ |
| Finans | Banka | Yok | Finans | Banka | `Planned` grubu | ✅ |

Not: Ribbon'da fazladan var (kayıp değil): Nakit Giriş/Çıkış, Kasalar Arası
Transfer, Kasa Ekstresi, Çek/Senet grubu.

## E-Belge

| Mevcut menü | Mevcut komut | Yetki | Yeni Ribbon sekmesi | Yeni grup | Yeni komut | Durum |
| --- | --- | --- | --- | --- | --- | --- |
| E-Belge | Genel Bakış, Giden Belgeler, Gönderim Kuyruğu, Hatalı Belgeler, Ayarlar | Yok | E-Belge | (top-level) | aynı 5 komut, aynı handler'lar | ✅ tam eşleşme + Ribbon'da fazladan "Gelen Belgeler" var |

## Ayarlar

Classic menüde kendi başına bir "Ayarlar" grubu var; Ribbon'da eşdeğer bir
üst sekme yok — komutlar "Araçlar" sekmesine dağılmış.

| Mevcut menü | Mevcut komut | Yetki | Yeni Ribbon sekmesi | Yeni grup | Yeni komut | Durum |
| --- | --- | --- | --- | --- | --- | --- |
| Ayarlar | Genel ayarlar | Yok | Araçlar | Tanımlar | `OpenGeneralSettings` | ✅ |
| Ayarlar | Firmalar | Yok | Araçlar | (top-level) | `OpenMasterCrud("companies",…)` | ✅ |
| Ayarlar | Şubeler | Yok | Araçlar | (top-level) | `OpenMasterCrud("branches",…)` | ✅ |
| Ayarlar | Depolar | Yok | Araçlar | (top-level) | `OpenMasterCrud("warehouses","Depo Tanımları")` | ⚠️ **Farklı handler.** Classic'in Ayarlar›Depolar'ı `OpenWarehouseManagement()` (yeni özel depo ekranı) çağırıyor; Araçlar›Depolar ise eski jenerik `OpenMasterCrud` grid'ini çağırıyor. Aynı isim, iki farklı ekran. |
| Ayarlar | Kullanıcı ve Yetkiler | Yok | — | — | **yok** | ❌ **Gerçek boşluk.** Ribbon'da "Kullanıcı ve Yetkiler" için hiçbir komut yok (placeholder bile yok). |

## Özet

| Bulgu türü | Adet | Ayrıntı |
| --- | --- | --- |
| ✅ Tam/kabul edilebilir eşleşme | 32 komut | Aynı handler, sadece yer/gruplama değişmiş |
| ⚠️ Aynı isim, farklı handler | 2 | "Müşteri kartları" (Mağaza) ve "Depolar" (Ayarlar→Araçlar) |
| ❌ Ribbon'da karşılığı yok | 2 | "Sevkiyat" (Satış sekmesinde), "Kullanıcı ve Yetkiler" |

**Sonuç: `MainMenu` şu an kaldırılamaz.** Üç şey netleşmeden geçiş
tamamlanmış sayılamaz:

1. "Müşteri kartları" için `OpenDefinitions(false)` mi yoksa
   `OpenCanonicalAccounts("Customer",…)` mi doğru davranış — ikisi de
   kod tabanında duruyor, hangisi kanonik olacak?
2. "Depolar" için Ribbon'un Araçlar sekmesi `OpenWarehouseManagement()`'a
   mı yönlendirilecek, yoksa `OpenMasterCrud` bilerek mi korunacak?
3. Ribbon'a Satış sekmesinde bir "Sevkiyat" girişi ve Araçlar/Ayarlar
   altında "Kullanıcı ve Yetkiler" (mevcut placeholder'ıyla) eklenmeli.

Bu üç madde çözülüp bu tablo yeniden çalıştırılıp tamamı ✅ olmadan
`MainMenu` silinmeyecek — kullanıcının talimatına göre bu, Phase 1'in değil,
Phase 2'nin (MainWindow dönüşümü) bir adımı.
