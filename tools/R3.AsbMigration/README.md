# R3 ASB Migration

Araç, ASB SQL Server veritabanından veriyi R3 SQLite veritabanına aktarır. VS Code'da yalnızca proje adını çalıştırmak yeterli değildir; kaynak bağlantısı verilmelidir.

```powershell
dotnet run --project tools/R3.AsbMigration/R3.AsbMigration.csproj -- `
  --source-connection "Server=localhost;Database=ASBDB_ERKUR02DB2008;Integrated Security=True;TrustServerCertificate=True" `
  --source-name "ASB:ASBDB_ERKUR02" `
  --target "$env:LOCALAPPDATA\R3\r3.db" `
  --canonical-core
```

`--canonical-core` müşteri, tedarikçi, stok, barkod ve birimleri aktarır. Sonraki veri aileleri için sırasıyla `--canonical-documents`, `--canonical-prices`, `--canonical-classifications` veya `--canonical-shipments` kullanılır.

Kaynak bağlantısı olmadan araç güvenli biçimde durur; R3 veritabanına hiçbir kayıt yazmaz.
