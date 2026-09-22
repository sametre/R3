# ASB tam veri aktarım prototipi

`R3.AsbMigration` artık SQL Server kaynağındaki tüm kullanıcı tablolarını keşfedip R3 SQLite içindeki legacy arşiv katmanına aktarabilir. Bu ilk prototipte kaynak veritabanına yalnızca okuma bağlantısı açılır; kaynak MDF değişmez.

## Çalıştırma

SQL Server MDF attach edildikten sonra:

```powershell
dotnet run --project tools/R3.AsbMigration/R3.AsbMigration.csproj -- `
  --source-connection "Server=localhost;Database=ASBDB_ERKUR02DB2008;Integrated Security=True;TrustServerCertificate=True" `
  --source-name "ASB:ASBDB_ERKUR02" `
  --target "C:\Users\mesud\AppData\Local\R3\data\r3.db"
```

## Prototipin garanti ettiği şeyler

- `sys.tables` üzerinden tüm kullanıcı tablolarını keşfeder.
- Her tablonun kolon listesini `legacy_tables` içinde saklar.
- Her satırı JSON olarak `legacy_rows` içinde saklar.
- Aynı kaynak/tablo/satır numarası için tekrar çalıştırmada `INSERT OR REPLACE` kullanır.
- Çalışma, tablo ve satır sayısını `legacy_migration_runs` içinde raporlar.
- Kaynak veritabanına INSERT/UPDATE/DELETE göndermez.

## Canonical aktarım

Legacy arşiv bütün veriyi koruyan güvenlik katmanıdır. `CARIKART`, `CARIKARTDTY`, `MUSTERI`, `TEDARIKCI`, `STOKKARTI`, `STOKBARKOD`, `STOKBIRIM`, `STOKTEDARIKCI`, depo, sipariş, sevkiyat ve finans aileleri için canonical aktarım kuralları ayrıca uygulanmalıdır. Anlamı doğrulanmamış legacy kolonları yanlış R3 alanına yazmamak için arşivde tutulur ve migration raporunda işaretlenir.
