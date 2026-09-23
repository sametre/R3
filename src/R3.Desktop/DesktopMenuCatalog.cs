namespace R3.Desktop;

internal sealed record DesktopMenuEntry(string Text, Action? Click, DesktopMenuEntry[] Children, bool IsGroup = false);

// Menu order and labels transcribed from the supplied ASB menu inventory.
internal static class DesktopMenuCatalog
{
    public static DesktopMenuEntry[] Create() =>
    [
        new("Mağaza", null, [
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
            new("Sayım", null, [], true),
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
        new("Satınalma", null, [
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
        new("Satış", null, [
            new("Fatura", null, [
                new("Satış Faturaları", null, [], false),
                new("Toptan Satış Faturası - İrsaliyeden", null, [], false),
                new("Toptan Satış Faturası - İrsaliyeli", null, [], false),
                new("Toptan Satış Faturaları", null, [], false),
                new("Satış İade Faturası - İrsaliyeden", null, [], false),
                new("Satış İade Faturaları", null, [], false),
                new("Perakende - Fatura-Tahsilat-Transferleri", null, [
                    new("Perakende Sonradan Teslim İrsaliyelerine Fatura Kaydı Oluştur", null, [], false),
                    new("Perakende Sonradan Teslim İrsaliye İadelerine Gider Pusulası Kaydı Oluştur", null, [], false),
                    new("Taksit Tahsilatları Transfer", null, [], false),
                    new("Taksit Tahsilatları Transfer (2) - Sonra Teslim Faturaların Tahsilatları", null, [], false),
                    new("Banka Hareketleri Transfer", null, [], false),
                    new("Peşinat Tahsilatları Transfer (Kredi Kartı var ise)", null, [], false),
                    new("Nakit Peşinat Tahsilatları Transfer (2) - Sonra Teslim Faturaların Tahsilatları", null, [], false),
                    new("Peşinat Tahsilatı İadeleri Transfer (Kredi Kartı var ise)", null, [], false),
                    new("Kasa Hareketleri Transfer (Banka ilişkili olanlar)", null, [], false),
                    new("Diğer Kasa Hareketleri Transfer", null, [], false),
                    new("Bakiye İadeleri Transfer (Kredi Kartı veya Banka Havalesi var ise)", null, [], false),
                    new("Bordro Muhasebe Fişi Transfer", null, [], false),
                    new("Transfer Tarihleri Değiştir", null, [], false)
                ]),
                new("Satış Fiyat Farkı Faturası - Kesilen", null, [], false),
                new("Satış Fiyat Farkı Faturaları - Kesilen", null, [], false),
                new("Satış Fiyat Farkı Faturası - Gelen", null, [], false),
                new("Satış Fiyat Farkı Faturaları - Gelen", null, [], false),
                new("HT Satış-Faturalandırma Kontrol Raporu", null, [], false),
                new("ST-Satış-İrsaliye-Fatura Raporu", null, [], false)
            ]),
            new("Raporlar", null, [
                new("Fatura Listesi", null, [], false),
                new("Fatura Kontrol Listesi - Stok Detaylı", null, [], false),
                new("Fatura Kdv Raporu", null, [], false),
                new("Fatura Kdv Listesi", null, [], false),
                new("Toptan Satış Karlılık Raporu", null, [], false),
                new("Mağaza Satış-Kalan Raporu", null, [], false)
            ]),
            new("Tanımlar", null, [
                new("Satış Fiyat Tanımları", null, [], false),
                new("Satış Şartları", null, [], false),
                new("Satış Eleman Kodları", null, [], false),
                new("Satış Eleman Grup Yetkileri", null, [], false),
                new("Kullanıcı Stok Hiyerarşi Yetkileri", null, [], false),
                new("Teslim Şekilleri - Yurtiçi", null, [], false),
                new("Teslim Şekilleri - Yurtdışı", null, [], false),
                new("Şube Nakliye Mesafeleri", null, [], false),
                new("Nakliye Kodları", null, [], false),
                new("Kasiyerler", null, [], false)
            ]),
            new("Perakende Raporlar", null, [
                new("Yönetim Raporları", null, [
                    new("Tahsilat Analiz Raporu", null, [], false),
                    new("Şubeler Karlılık Raporu", null, [], false),
                    new("Şube Nakit Kasa Raporu", null, [], false),
                    new("Banka Hesapları İcmal", null, [], false),
                    new("Faaliyet Raporu", null, [], false),
                    new("Vadeli Tahsilat Raporu", null, [], false),
                    new("Ödeme Listesi", null, [], false)
                ]),
                new("Şubeler Ciro Raporu", null, [], false),
                new("Satış Analiz Raporu", null, [], false),
                new("Satışçı(lar) Dağılım Karnesi", null, [], false),
                new("Kampanya Bazında Satış Raporu", null, [], false),
                new("Satış İskontoları Kontrol Raporu", null, [], false),
                new("Satış Elemanları Satış Raporu", null, [], false),
                new("Satış Kontrol Raporu", null, [], false),
                new("Ürün Karlılık Raporu", null, [], false),
                new("Ürün Ciro-Kar Matrisi", null, [], false),
                new("Tahsilat Raporu", null, [], false),
                new("Geciken Taksitler Raporu", null, [], false),
                new("Beklenen Taksit Tahsilatları Raporu", null, [], false),
                new("Açık Taksitler Raporu", null, [], false),
                new("Müşteri Bakiyeleri Raporu", null, [], false),
                new("Taksit İcmali", null, [], false),
                new("Taksit Tahsil İcmali", null, [], false),
                new("Teslimat Sonuç Raporu", null, [], false),
                new("İrsaliye-Fatura İşlem Kontrol Raporu", null, [], false),
                new("Borç Takip Raporu", null, [], false),
                new("Borç Takip Atama Taahhüt Tahsilat Raporu", null, [], false),
                new("Borç Takip Listesi", null, [], false),
                new("Atama Yapılan Borç Takip Kullanıcıları", null, [], false),
                new("Kredi Mektubu Raporu", null, [], false),
                new("Cari Bakiye x Taksit Bakiye Kontrol", null, [], false),
                new("Müşteri Listesi ve Yönetimi", null, [], false),
                new("Dönemsel Satış-Satınalma Analiz Raporu", null, [], false),
                new("Kredili Satış-Taksit Analiz Raporu", null, [], false),
                new("Yeni Müşteri Listesi ve Yönetimi", null, [], false)
            ]),
            new("Prim Sistemi", null, [
                new("Tanımlar", null, [
                    new("Parametreler", null, [], false),
                    new("Prim Unsurları", null, [], false),
                    new("Prim Unsur Cari Kart Eşleşmeleri", null, [], false),
                    new("Prim Anahtarları", null, [], false)
                ]),
                new("Reyon Kotaları ve Prim Oranları", null, [], false),
                new("Satış Eleman Kotaları", null, [], false),
                new("Satışa Özel Primler", null, [], false),
                new("Ürüne Özel Primler", null, [], false),
                new("Prim Dönemleri", null, [], false),
                new("Prim Analiz Raporu", null, [], false)
            ])
        ]),
        new("Kasa-Banka", null, [
            new("Banka İşlemleri", null, [
                new("Banka Hareketi Gir", null, [], false),
                new("Banka Hareketleri", null, [], false),
                new("Kredi Kartı Taksit Tablosu Düzenle", null, [], false),
                new("Kredi Kartı Geri Dönüş Tablosu", null, [], false),
                new("Kredi Kartı Geri Dönüş Tabloları", null, [], false),
                new("Kredi Kartı Günlük Kontrol Raporu", null, [], false),
                new("Kredi Kartı Taksitleri Raporu", null, [], false),
                new("Bankalar", null, [], false),
                new("Banka Şubeleri", null, [], false),
                new("Kredi / Kredi Kartı", null, [], false),
                new("Toplu Banka Hareketi (Excel Dosyasından)", null, [], false),
                new("Toplu Banka Hareketi İşlemleri", null, [], false)
            ]),
            new("Kasa İşlemleri", null, [
                new("Kasa Hareketi Gir", null, [], false),
                new("Kasa Hareketleri", null, [], false),
                new("Kasa İşlem Kodları", null, [], false),
                new("Kasa Kodları", null, [], false),
                new("Kasa Ekstresi", null, [], false)
            ]),
            new("Perakende Raporlar", null, [
                new("Kasa Bakiyeleri Raporu", null, [], false),
                new("Kasa Raporu", null, [], false)
            ]),
            new("Banka Kredileri", null, [
                new("Kredi Tipleri", null, [], false),
                new("Kredi Kayıt İşlemleri", null, [], false),
                new("Kredi Entegrasyon Sayfası", null, [], false),
                new("Kredi Ödeme İşlemleri", null, [
                    new("Kredi Taksiti Ödeme", null, [], false),
                    new("Ödenmiş Kredi Taksitleri", null, [], false),
                    new("Ödenecek Kredi Taksitleri", null, [], false)
                ])
            ]),
            new("Kredi Kartı Tahsilat", null, [], false),
            new("Kredi Kartı Tahsilatları", null, [], false)
        ]),
        new("Çek-Senet", null, [
            new("Alacak Çekleri", null, [
                new("Çek Giriş Bordrosu", null, [], false),
                new("Çek Giriş Bordroları", null, [], false),
                new("Çek Çıkış Bordrosu", null, [], false),
                new("Çek Çıkış Bordroları", null, [], false),
                new("Çek Dönüş Bordrosu", null, [], false),
                new("Çek Dönüş Bordroları", null, [], false),
                new("Çek Raporu", null, [], false)
            ]),
            new("Alacak Senetleri", null, [
                new("Senet Giriş Bordrosu", null, [], false),
                new("Senet Giriş Bordroları", null, [], false),
                new("Senet Çıkış Bordrosu", null, [], false),
                new("Senet Çıkış Bordroları", null, [], false),
                new("Senet Dönüş Bordrosu", null, [], false),
                new("Senet Dönüş Bordroları", null, [], false),
                new("Senet Raporu", null, [], false)
            ]),
            new("Borç Çekleri", null, [
                new("Çek Karnesi Giriş", null, [], false),
                new("Çek Karneleri", null, [], false),
                new("Borç Çeki Çıkış Bordrosu", null, [], false),
                new("Borç Çeki Çıkış Bordroları", null, [], false),
                new("Borç Çeki Sonuç Bordrosu", null, [], false),
                new("Borç Çeki Sonuç Bordroları", null, [], false),
                new("Verilen Borç Çekleri Listesi", null, [], false)
            ]),
            new("Borç Senetleri", null, [
                new("Borç Senedi Çıkış Bordrosu", null, [], false),
                new("Borç Senedi Çıkış Bordroları", null, [], false),
                new("Borç Senedi Sonuç Bordrosu", null, [], false),
                new("Borç Senedi Sonuç Bordroları", null, [], false),
                new("Verilen Borç Senetleri Listesi", null, [], false)
            ]),
            new("Tanımlar", null, [
                new("Çek-Senet Bordro Tipleri", null, [], false),
                new("Borç Çekleri Muhasebe Entegrasyon Tablosu", null, [], false),
                new("Borç Senetleri Muhasebe Entegrasyon Tablosu", null, [], false),
                new("Alacak Çekleri Muhasebe Entegrasyon Tablosu", null, [], false),
                new("Alacak Senetleri Muhasebe Entegrasyon Tablosu", null, [], false)
            ]),
            new("Raporlar", null, [
                new("Henüz Tahsil Olmamış Müşteri Çekleri/Senetleri", null, [], false),
                new("Çek-Senet Raporu", null, [], false)
            ])
        ]),
        new("Araçlar", null, [
            new("Design Reports", null, [
                new("Muhasebe", null, [
                    new("BaBs Mutabakat Mektubu", null, [], false),
                    new("Cari Mutabakat Mektubu", null, [], false),
                    new("Yevmiye Defteri (Xrp)", null, [], false),
                    new("Muhasebe Fişi", null, [], false)
                ]),
                new("Servis", null, [
                    new("Servis İş Emri", null, [], false),
                    new("Servis İş Emri - Fatura", null, [], false),
                    new("Servis Teklif", null, [], false)
                ]),
                new("Fatura", null, [
                    new("Satış Faturası", null, [], false),
                    new("Satınalma İade Faturası", null, [], false),
                    new("Fiyat Farkı Faturası", null, [], false),
                    new("Kur Farkı Faturası", null, [], false),
                    new("Mağaza Fatura", null, [], false)
                ]),
                new("Çek-Senet Bordro", null, [
                    new("Çek Giriş Bordrosu", null, [], false),
                    new("Çek Çıkış Bordrosu", null, [], false),
                    new("Çek Dönüş Bordrosu", null, [], false),
                    new("Borç Çeki Çıkış Bordrosu", null, [], false),
                    new("Borç Çeki Sonuç Bordrosu", null, [], false),
                    new("Senet Giriş Bordrosu", null, [], false),
                    new("Senet Çıkış Bordrosu", null, [], false),
                    new("Senet Dönüş Bordrosu", null, [], false),
                    new("Borç Senedi Çıkış Bordrosu", null, [], false),
                    new("Borç Senedi Sonuç Bordrosu", null, [], false)
                ]),
                new("Satınalma", null, [
                    new("Satınalma Mal Kabul Belgesi Print", null, [], false),
                    new("Satınalma Sipariş Formu", null, [], false),
                    new("Satınalma İade İrsaliyesi", null, [], false)
                ]),
                new("Satış", null, [
                    new("Satış Sipariş Fason Formu", null, [], false),
                    new("Satış Sipariş Föyü", null, [], false),
                    new("Satış İade Mal Kabul Belgesi", null, [], false),
                    new("Satış Sipariş Formu 08", null, [], false)
                ]),
                new("Stok", null, [
                    new("Sevk İrsaliyesi Print", null, [], false),
                    new("Sevk İrsaliyesi (Fason) Print", null, [], false),
                    new("Stok Toplama Listesi Print", null, [], false),
                    new("Stok Belgesi Print", null, [], false),
                    new("Kamyon Yükleme Belgesi", null, [], false),
                    new("Müşteriye Özel Sta Sip Etiket", null, [], false),
                    new("Stok Raf Etiketi", null, [], false),
                    new("Sevk Emri Rezervasyon Etiketi", null, [], false),
                    new("Deneme Ürün İade Giriş Belgesi", null, [], false),
                    new("Kamyon Planlama Belgesi", null, [
                        new("Ürün Toplama Belgesi", null, [], false),
                        new("Kamyon Planlama Belgesi", null, [], false)
                    ]),
                    new("Mal Kabul Belgesi (Xrp)", null, [], false)
                ]),
                new("Transfer İrsaliyesi", null, [
                    new("Transfer İrsaliyesi Print", null, [], false),
                    new("Transfer İrsaliyesi2", null, [], false),
                    new("Transfer İrsaliyesi3", null, [], false)
                ]),
                new("Mağaza", null, [
                    new("Mağaza Sözleşme", null, [], false),
                    new("Mağaza Sıra Senet", null, [], false),
                    new("Araştırma Formu Tahsilat", null, [], false),
                    new("Araştırma Formu", null, [], false),
                    new("Mağaza Gider Pusulası (Frx)", null, [], false),
                    new("Mağaza Sevkiyat Listesi", null, [], false),
                    new("Mağaza Sevkiyat Batch List", null, [], false),
                    new("Mağaza Tahsilat Makbuzu (Xrp)", null, [], false),
                    new("Mağaza Tahsilat Makbuzu (Frx)", null, [], false),
                    new("Perakende Arıza Servis Formu", null, [], false),
                    new("Kredi Mektubu", null, [], false),
                    new("Taksit Ödeme Planı (Frx)", null, [], false),
                    new("Avukata Verilecek Sözleşme Listesi", null, [], false),
                    new("Cari Hesap Ekstresi", null, [], false),
                    new("Kefil Kvkk Evragı", null, [], false),
                    new("Şehirlerarası Nakliye Sözleşmesi", null, [], false),
                    new("Oturma Grubu Sipariş Formu", null, [], false),
                    new("Senet Teslim Tutanağı", null, [], false)
                ]),
                new("Uyarı Mektubu", null, [
                    new("1.Uyarı Mektubu", null, [], false),
                    new("2.Uyarı Mektubu", null, [], false),
                    new("3.Uyarı Mektubu", null, [], false)
                ]),
                new("Raporlar", null, [
                    new("Satınalma Fiyat Kontrol Raporu", null, [], false),
                    new("Alış-Satış Fiyat Kontrol Listesi", null, [], false),
                    new("Cari Notlar Listesi", null, [], false),
                    new("Stok Sayım Belgesi", null, [], false),
                    new("Bölge Bazında Satınalma Sipariş Bakiye Raporu", null, [], false)
                ]),
                new("Bordro", null, [
                    new("Bordro - Hesap Pusulası", null, [], false),
                    new("Aylık İş Gücü Çizelgesi", null, [], false),
                    new("Bordro İcmal", null, [], false),
                    new("Kıdem Tazminatı Ekstre", null, [], false),
                    new("İhbar Tazminatı Ekstre", null, [], false),
                    new("İşten Ayrılma Bildirgesi", null, [], false),
                    new("Eksik Gün Bildirim Formu", null, [], false),
                    new("Ek-1 İşçi Bildirim Formu", null, [], false),
                    new("Ek-2 İşçi Bildirim Formu", null, [], false)
                ]),
                new("Kasa", null, [
                    new("Kasa Hareketleri - Tahsilat Cari", null, [], false),
                    new("Kasa Hareketleri - Tediye Cari", null, [], false)
                ]),
                new("Report Manager", null, [], false)
            ]),
            new("Sürüm Güncelle", null, [], false),
            new("Data Import", null, [], true),
            new("Kullanıcı Grupları", null, [], false),
            new("Kullanıcılar", null, [], false),
            new("Parametreler", null, [], false),
            new("Bakım", null, [
                new("Tahsilat-Cari Kontrol Nbm Transfer", null, [], false),
                new("Taksit Detay - Ürün Eşleştirme", null, [], false),
                new("Stok Varyant Yapısına Geçiş Yap", null, [], false),
                new("Kdv Tutarı ve Matrahı olmayan Transfer Kayıtlarına Kdv Oluştur", null, [], false),
                new("Slip Taksitleri Taksit No Düzenle", null, [], false),
                new("Default Yerden Reyona Stok Aktar", null, [], false),
                new("Sanal Pos Muhasebeleştir", null, [], false),
                new("Merkez Depo Envanterdeki Ürünleri Default Yere Çık", null, [], false),
                new("Stok kartları Toplu Pasife Alma", null, [], false),
                new("Bir stok kartının aynı isimli varyantlarını birleştir", null, [], false),
                new("Copy Database", null, [], false),
                new("Stok Varyant Yapısına Geçiş Sonrası Veritabanını Güncelle", null, [], false),
                new("Veri Transfer Sonrası StkHarIsk kayıtlarını oluştur", null, [], false)
            ]),
            new("Entegra Entegrasyon", null, [], false),
            new("Şirket Hesap Bilgileri", null, [], false),
            new("Şubeler", null, [], false),
            new("Yetkilendirme", null, [
                new("Yetkilendirme - Yetki Tanımı -> Kullanıcı", null, [], false),
                new("Yetkilendirme - Kullanıcı -> Yetki Tanımı", null, [], false),
                new("Yetki Raporu", null, [], false)
            ]),
            new("Transfer ToDb", null, [
                new("Gelen Faturaları Transfer Et", null, [], false),
                new("Kesilen Faturaları Transfer Et", null, [], false),
                new("Mağaza Satış Faturalarını Karşı DB ye Transfer Et", null, [], false),
                new("Masraf Listeleri Transfer", null, [], false),
                new("Mağaza Satış İade Faturalarını (Gider Pusulaları) Transfer", null, [], false),
                new("Borç Çeki Çıkış Bordroları Transfer", null, [], false),
                new("Borç Çeki Sonuç Bordroları Transfer", null, [], false),
                new("Alacak Çeki Giriş Bordroları Transfer", null, [], false),
                new("Alacak Çeki Çıkış Bordroları Transfer", null, [], false),
                new("Alacak Çeki Dönüş Bordroları Transfer", null, [], false),
                new("Bordro Muhasebe Fişi Transfer", null, [], false),
                new("Taksit Tahsilatları Transfer", null, [], false),
                new("Taksit Tahsilatları Transfer (2) - Sevkiyatlara Kesilen Faturaların Taksit Tahsilatları", null, [], false),
                new("Banka Hareketleri Transfer", null, [], false),
                new("Kasa Hareketleri Transfer (Banka ilişkili olanlar)", null, [], false),
                new("Diğer Kasa Hareketleri Transfer", null, [], false),
                new("Peşinat Tahsilatları Transfer (Kredi Kartı var ise)", null, [], false),
                new("Nakit Peşinat Tahsilatları Transfer (2) - Sonra Teslim Faturaların Tahsilatları", null, [], false),
                new("Peşinat Tahsilatı İadeleri Transfer (Kredi Kartı var ise)", null, [], false),
                new("Avans Tahsilatı Transfer (Kredi Kartı veya Banka Havalesi var ise)", null, [], false),
                new("Bakiye İadeleri Transfer (Kredi Kartı veya Banka Havalesi var ise)", null, [], false),
                new("Transfer Başlangıç Tarihlerini Değiştir", null, [], false)
            ]),
            new("Yeniden Muhasebeleştir", null, [], false),
            new("Müşteri Hizmetleri - Sms Tanımları", null, [
                new("Mesaj Bilgi Kaynakları", null, [], false),
                new("Mesaj Şablonları", null, [], false),
                new("Mesaj Zaman Parametreleri", null, [], false),
                new("Mesaj Analiz Raporu", null, [], false),
                new("Sms Operatörleri", null, [], false),
                new("Sms Mesaj Başlıkları (Originator)", null, [], false)
            ]),
            new("Taksit-Stok Tablosu Oluştur", null, [], false),
            new("Toplu Fatura Sil", null, [], false),
            new("GC Collect", null, [], false),
            new("Log Raporu", null, [], false),
            new("Toplu Fatura Kontrol Edildi Bilgisi Güncelle", null, [], false),
            new("Toplu Muhasebe Fişi Kontrol Edildi Bilgisi Güncelle", null, [], false),
            new("Auto Task Service Manager", null, [], false),
            new("E-Mail Tanımları", null, [
                new("Kaynak E-Mail Adresleri", null, [], false),
                new("Hedef E-Mail Adresleri", null, [], false),
                new("E-Mail Tipleri", null, [], false),
                new("E-Mail Listeleri", null, [], false)
            ]),
            new("Döküman Tipleri", null, [], false),
            new("Sayaçdan Güncelle", null, [], false),
            new("Mağaza Sözleşme Yerleri", null, [], false)
        ]),
        new("Asb Tools", null, [
            new("Parametre Tanımları Oluştur", null, [], false),
            new("Yetki Tanımları Oluştur", null, [], false),
            new("Cari Tipleri Oluştur", null, [], false),
            new("Şube Ana Kasaları Oluştur", null, [], false),
            new("Sms Operator Kodları Oluştur", null, [], false),
            new("Şube Depoları Oluştur", null, [], false),
            new("Entegrasyon Kodları Oluştur", null, [], false),
            new("Mağazacılık", null, [
                new("Arş Kodları Oluştur", null, [], false),
                new("Mağaza Şube Parametreleri Oluştur", null, [], false),
                new("Avukat Dosya Parametre Tanımları Oluştur", null, [], false),
                new("Arş.Ulaşım Kodları Oluştur", null, [], false)
            ]),
            new("Stok Kategori Kodları Oluştur", null, [], false),
            new("Hareket Kodları Oluştur", null, [], false),
            new("Belge Kodları Oluştur", null, [], false),
            new("Fiş Türleri Oluştur", null, [], false),
            new("Belge Türleri Oluştur", null, [], false),
            new("Stok Gruplarını Yetki tanımına ekle", null, [], false),
            new("Fatura Kodları Oluştur", null, [], false),
            new("Ödeme Kodları Oluştur", null, [], false),
            new("Standart Cari Kartları Oluştur", null, [], false),
            new("Kasa İşlem Kodları Oluştur", null, [], false),
            new("Arşiv Belge Yerleri Oluştur", null, [], false),
            new("Import Xml File", null, [
                new("Import General Asb e-Fatura xslt", null, [], false),
                new("Import Ubl css", null, [], false),
                new("Import GelirIdaresi.jpg", null, [], false),
                new("Import eFatura için Şirket Logo.jpg", null, [], false),
                new("Import UBLInvoiceXsd", null, [], false),
                new("Import General Asb e-ArsivFatura xslt", null, [], false),
                new("Import Nes e-ArsivFatura xslt", null, [], false),
                new("Import UBL eArsiv css", null, [], false),
                new("Import General Asb e-Irsaliye xslt", null, [], false),
                new("Import General Asb EIhracat.xslt", null, [], false),
                new("Import UblEIhracat.css", null, [], false)
            ]),
            new("Export Xml To File", null, [
                new("Export AsbGeneral xslt to a file", null, [], false),
                new("Export Ubl.css to a file", null, [], false),
                new("Export UBLInvoiceXsd to a file", null, [], false),
                new("Export AsbGeneralEArsiv xslt to a file", null, [], false),
                new("Export NesEarsiv xslt to a file", null, [], false),
                new("Export Ubl eArsiv css", null, [], false),
                new("Export AsbGeneralEIrsaliye.xslt", null, [], false),
                new("Export Asb General EIhracat.Xml", null, [], false),
                new("Export UblEIhracat.Css", null, [], false)
            ]),
            new("Döviz Kodları Oluştur", null, [], false),
            new("Çek Senet Bordro Tipleri Oluştur", null, [], false),
            new("Banka Kodları Oluştur", null, [], false),
            new("Banka Kredileri Tipleri Oluştur", null, [], false),
            new("Banka İşlem Kodları Oluştur", null, [], false),
            new("Tatil Günlerini Oluştur", null, [], false),
            new("İl-İlçe-Mahalle Oluştur", null, [], false),
            new("Ülke Kodları Oluştur", null, [], false),
            new("Çek/Senet Durum Kodları Oluştur", null, [], false),
            new("Çek Senet Durum Map Tablosu Oluştur", null, [], false),
            new("Stok Birimleri Oluştur", null, [], false),
            new("Stok Tipleri Oluştur", null, [], false),
            new("Bordro", null, [
                new("İşten Çıkış Neden Kodları Oluştur", null, [], false),
                new("Eksik Gün Neden Kodları Oluştur", null, [], false),
                new("Bordro Entegrasyon Kayıtlarını Önceki Yıldan Kopyala", null, [], false)
            ])
        ]),
        new("Kapat", null, [], false)
    ];
}
