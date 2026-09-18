# R3 ERP

R3, Windows üzerinde çalışan modern ve lisans maliyeti olmayan bir ticari ERP uygulamasıdır.

## Teknoloji

- .NET 10 LTS
- WPF
- C#
- PostgreSQL (sonraki aşama)
- Tamamen ücretsiz/açık kaynak bağımlılıklar

## Proje yapısı

- `src/R3.Desktop`: Windows masaüstü uygulaması
- `src/R3.Domain`: İş kuralları ve temel modeller
- `src/R3.Application`: Kullanım senaryoları
- `src/R3.Infrastructure`: Veritabanı ve dış sistemler
- `src/R3.Contracts`: Katmanlar arası sözleşmeler
- `tests/R3.Domain.Tests`: Domain testleri

## Çalıştırma

```powershell
dotnet restore
dotnet build
dotnet run --project src/R3.Desktop
```


## Mağaza modülü (0.2.0)

- SQLite 3 dosyası ilk açılışta `%LOCALAPPDATA%\R3\r3.db` konumunda oluşturulur. Sunucu kurulumu gerekmez.
- Mağaza > Tanımlar > Mağaza tanımları: mağaza ekleme, düzenleme, arama ve CSV aktarımı.
- Mağaza > Müşteri kartları: müşteri ekleme ve düzenleme.
- Mağaza > Müşteri cari: müşteri seçimi, mağaza bazında borç/tahsilat kaydı, ekstre ve bakiye.
- Para değerleri tam sayı kuruş olarak saklanır. Cari borç kaydı fatura veya stok hareketi oluşturmaz.
- Diğer modüller menüde yakında olarak gösterilir. Bu sürümde kimlik doğrulama ve çok kullanıcılı sunucu bağlantısı yoktur.
- R3 mat/parlak simgeleri Assets klasöründedir; EXE ve pencereler R3.ico kullanır.

Doğrulama: `dotnet test` ve `dotnet run --project tools/R3.UiSmoke`.

## Local SQLite ve Server

Masaüstü uygulamasının varsayılan yerel sağlayıcısı SQLite 3'tür. Veritabanı `%LOCALAPPDATA%\R3\data\r3.db` altında oluşturulur; farklı bir konum için `R3_SQLITE_PATH` kullanabilirsiniz. Sistem menüsündeki “Veritabanı yedeği al” komutu `Backups` klasörüne güvenli SQLite yedeği üretir. ASB preview aracı `dotnet run --project tools/R3.AsbMigration -- --dry-run` ile çalışır.

## Server ve PostgreSQL

Canonical persistence altyapısı `R3.Server` ve `R3.Infrastructure` içindedir. Desktop PostgreSQL'e bağlanmaz. Yerel PostgreSQL için `R3_POSTGRES_CONNECTION` ortam değişkenini ayarlayın veya `src/R3.Server/appsettings.Development.example.json` dosyasını yerel, takip edilmeyen bir `appsettings.Development.json` olarak kopyalayın. Ardından:

```powershell
dotnet ef database update --project src/R3.Infrastructure --startup-project src/R3.Server
dotnet run --project src/R3.Server
```

Sunucu sağlığı `http://localhost:5000/health` adresinden kontrol edilir. Gerçek parola ve token kaynak koduna yazılmamalıdır.
