namespace R3.Desktop;

internal sealed record DesktopMenuEntry(string Text, Action? Click, DesktopMenuEntry[] Children, bool IsGroup = false);

// Menu order and labels transcribed from the supplied ASB menu inventory.
internal static class DesktopMenuCatalog
{
    public static DesktopMenuEntry[] Create() =>
    [
        new("Ma?aza", null, [
            new("Müşteri Cari", null, [], false),
            new("Taksit Tahsilatı (Seri)", null, [], false),
            new("İleri Teslim Siparişler", null, [], false),
            new("Ürün Sor", null, [], false),
            new("Müşteri/Kefil Sor", null, [], false),
            new("Müşteri Kartı", null, [], false),
            new("Kefil", null, [], false),
            new("Perakende Satış", null, [], true),
            new("Kasa İşlemleri", null, [], true),
            new("Stok İşlemleri", null, [], true),
            new("Satınalma", null, [], true),
            new("Arşiv", null, [], true),
            new("Toptan Satış", null, [], true),
            new("Sevkiyat", null, [], true),
            new("Avukatlık İşlemleri", null, [], true),
            new("Raporlar", null, [], true),
            new("Tanımlar", null, [], true),
            new("Borç-Takip", null, [], false),
            new("Müşteri Hizmetleri", null, [], true),
            new("Araştırma Talepleri", null, [], false),
            new("Kredi Onay Talepleri", null, [], false),
            new("Bekleyen Sevkiyatlar", null, [], false),
            new("Sms Sevk Emirleri", null, [], false),
            new("Gerçekleşen Sevkiyatlar", null, [], false),
            new("Arşivde ve Kredi Onayda Bekleyen Ürün Bazlı Satış Listesi", null, [], false),
            new("Gerçekleşen Sevkiyatlar - Ürün Bazlı", null, [], false),
            new("Şifre Değiştir", null, [], false)
        ]),
        new("Muhasebe", null, [
            new("Muhasebe Fişi", null, [], false),
            new("Muhasebe Fişleri", null, [], false),
            new("Cari Hesap Ekstresi", null, [], false),
            new("Muavin", null, [], false),
            new("E-Fatura", null, [], true),
            new("Mizan", null, [], false),
            new("Resmi Defterler", null, [], true),
            new("Açılış-Kapanış", null, [], true),
            new("Yansıtma Fişi", null, [], false),
            new("Tanımlar", null, [], true),
            new("Raporlar", null, [], true),
            new("Cari Hesap Mutabakatları", null, [], false),
            new("Fatura Matbu No Güncelle", null, [], false),
            new("Masraf-Hizmet Faturası", null, [], false),
            new("Masraf-Hizmet Faturaları", null, [], false),
            new("Masraf-Hizmet İade Faturası", null, [], false),
            new("Masraf-Hizmet İade Faturaları", null, [], false),
            new("Masraf Listesi - Giriş", null, [], false),
            new("Masraf Listeleri", null, [], false),
            new("Amortisman", null, [], true),
            new("Firma Kredi Kartları", null, [], true),
            new("Hesap Planı", null, [], false),
            new("Faturalara Matbu No Ver", null, [], false),
            new("Ön Değerler", null, [], false),
            new("Toplu Kredi Limiti Ver", null, [], false),
            new("Diğer Kesilen Fatura", null, [], false),
            new("Diğer Kesilen Faturalar", null, [], false),
            new("Gelen Diğer Kesilen Fatura İadesi", null, [], false),
            new("Gelen Diğer Kesilen Fatura İadeleri", null, [], false),
            new("Muhasebe Kontrol Kartı", null, [], false)
        ]),
        new("Stok", null, [
            new("Sevkiyat", null, [
                new("Sevkiyat - Siparişten", null, [], false),
                new("Sevkiyat Planla", null, [], false),
                new("Sevk Emirleri - (Ürün bazlı)", null, [], false),
                new("Sevk Emirleri", null, [], false),
                new("Bekleyen Sevkiyatlar - (Sevk Emirleri)", null, [], false),
                new("Kamyon Yükleme Belgeleri", null, [], false),
                new("Teslimat Takip Raporu", null, [], false),
                new("Teslimat Takip Web10 Raporu", null, [], false),
                new("Gerçekleşen Sevkiyatlar", null, [], false),
                new("Gerçekleşen Sevkiyatlar - Ürün Bazlı", null, [], false)
            ]),
            new("Tanımlar", null, [
                new("Stok Kartı", null, [], false),
                new("Depolar", null, [], false),
                new("Kritik Stok Seviyeleri", null, [], false),
                new("Renk-Beden Tanımları", null, [], true),
                new("Ürün Özellikleri", null, [], true),
                new("Stok Kartları", null, [], false),
                new("Stok Tipleri", null, [], false),
                new("Stok Grup Kodları", null, [], false),
                new("Stok İskonto Grupları", null, [], false),
                new("Stok Entegrasyon Tablosu", null, [], false),
                new("Stok Birimleri", null, [], false),
                new("KDV Oranları", null, [], false),
                new("Tevkifat Kodları", null, [], false),
                new("Stok Hareket Kodları", null, [], false),
                new("Stok Kategori Tanımları", null, [], false),
                new("İade Nedeni", null, [], false),
                new("Sevkiyat Bölge Kodları", null, [], false),
                new("Stok Kartı Yönetimi", null, [], true),
                new("Stok Parça Yönetimi", null, [], true),
                new("Kıdem, Prim Tanımlamaları", null, [], true),
                new("Kartela Tanımları", null, [], false),
                new("Stok Varyant Boyut Kodları", null, [], false),
                new("Stok Parametreleri", null, [], false),
                new("Kargo Kodları", null, [], false),
                new("Sürücü Kodları", null, [], false),
                new("Pasif Stok Kartları", null, [], false),
                new("Sevk Bölgesi - Sevk Adresi Eşleştirme", null, [], false),
                new("Route Tanımları", null, [], false),
                new("Tedarikçi Bölge x Satış Şube x Teslim Şube Tablosu", null, [], false),
                new("Toplu Stok Kartı Oluştur", null, [], true),
                new("Barkodu Olmayan Stok Kartlarına Toplu Özel Barkod Oluştur", null, [], false),
                new("Takım/Set Tanım", null, [], false),
                new("Takım/Set Tanımları", null, [], false)
            ]),
            new("Satınalma İade", null, [
                new("Satınalma İade İrsaliyesi - Doğrudan", null, [], false),
                new("Satınalma İade İrsaliyesi - İrsaliyeden", null, [], false),
                new("Satınalma İade İrsaliyeleri", null, [], false)
            ]),
            new("Stok İşlemleri", null, [
                new("Stok Hareket Belgesi", null, [], true),
                new("Depolar Arası Transfer Çıkış", null, [], false),
                new("Depolar Arası Transfer Çıkışları", null, [], false),
                new("Onay Bekleyen Transfer Girişleri", null, [], false),
                new("Onaylanmış Transfer Girişleri", null, [], false)
            ]),
            new("Ürün Sor", null, [], false),
            new("Depolararası Transfer Planı", null, [], false),
            new("Müşteri Memnuniyeti", null, [
                new("Müşteri Memnuniyeti", null, [], false),
                new("Gerçekleşen Müşteri Memnuniyetleri", null, [], false),
                new("Müşteri Memnuniyeti Listesi", null, [], false)
            ]),
            new("Maliyet", null, [
                new("Maliyet Fiyat Tanımları", null, [], false),
                new("Maliyet Fiyat Grupları", null, [], false)
            ]),
            new("Sayım", null, [
                new("Alt menüsü var ancak gönderdiğin ekran görüntülerinde açılmamış.", null, [], false)
            ]),
            new("Raporlar", null, [
                new("Stok Envanter Raporu", null, [], false),
                new("Stok Envanter Raporu - Dönemsel", null, [], false),
                new("Stok Alış-Satış Envanter Farklılık Raporu", null, [], false),
                new("Stok,Borç ve Smn Sağlaması", null, [], false),
                new("Stok Yaşlandırma Raporu", null, [], false),
                new("Dönemsel Stok Hareket Toplamları Raporu", null, [], false),
                new("Depo Stok Hareketleri", null, [], false),
                new("Açık Stok Rezervasyonları", null, [], false),
                new("Sayım Envanter Fark Raporu", null, [], false),
                new("Stok Hareketleri Analiz Raporu", null, [], false),
                new("Stok İskonto Oranları Raporu", null, [], false),
                new("Mal Kabul İrsaliyeleri-Ürün bazlı", null, [], false)
            ]),
            new("Malzeme İhtiyaç Planı (MİP)", null, [], false),
            new("Ürün Talep ve Takip Merkezi", null, [
                new("Ürün Talep - Giriş", null, [], false),
                new("Ürün Talep Takip", null, [], false)
            ]),
            new("Konsinye İşlemleri", null, [
                new("Giden Konsinye Çıkış", null, [], false),
                new("Giden Konsinye Çıkışları", null, [], false),
                new("Giden Konsinye İade Giriş", null, [], false),
                new("Giden Konsinye İade Girişleri", null, [], false),
                new("Giden Konsinye Alacaklarımız", null, [], false)
            ]),
            new("Emanetteki Ürünlerimiz", null, [
                new("Müşteriden Emanet Ürün İade Giriş", null, [], false),
                new("Müşteriden Emanet Ürün İade Girişleri", null, [], false),
                new("Müşteride Emanet Bekleyen Ürünler", null, [], false)
            ]),
            new("Fiyat", null, [
                new("Fiyatlar", null, [], false),
                new("Fiyat Değişiklik Raporu", null, [], false),
                new("Fiyatı Değişmeyen Ürünler Raporu", null, [], false),
                new("Otomatik Fiyatlandırma Tanımları", null, [], false)
            ]),
            new("Stok Ekstresi", null, [], false),
            new("Stok Etiketi", null, [], false),
            new("El Terminali", null, [
                new("El Terminali İşlemleri", null, [], false)
            ])
        ]),
        new("Sat?nalma", null, [
            new("Satınalma Siparişi - Yurtiçi", null, [], false),
            new("Satınalma Siparişleri", null, [], false),
            new("Satınalma Sipariş Bakiyeleri", null, [], false),
            new("Satınalma İrsaliyesi - Siparişten", null, [], false),
            new("Satınalma İrsaliyesi - Siparişten - Barkod ile", null, [], false),
            new("Satınalma İrsaliyesi - Doğrudan", null, [], false),
            new("Satınalma İrsaliyeleri", null, [], false),
            new("Satınalma İade İrsaliyesi - Doğrudan", null, [], false),
            new("Satınalma İade İrsaliyesi - İrsaliyeden", null, [], false),
            new("Satınalma İade İrsaliyeleri", null, [], false),
            new("Fatura", null, [], true),
            new("Tanımlar", null, [], true),
            new("Tüm İrsaliyeler", null, [], true),
            new("Raporlar", null, [], true),
            new("Malzeme Talep ve Takip Merkezi", null, [], true)
        ]),
        new("Sat??", null, [], false),
        new("Kasa-Banka", null, [], false),
        new("?ek-Senet", null, [], false),
        new("Ara?lar", null, [], false),
        new("Asb Tools", null, [], false),
        new("Kapat", null, [], false)
    ];
}
