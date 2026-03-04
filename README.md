# R3 ERP

R3; Cari, Stok, Fatura ve Finans modülleriyle başlayan, Microsoft SQL Server üzerinde çalışan masaüstü ERP uygulamasıdır.

## Teknoloji

- .NET 10 LTS
- C# / Windows Forms
- Krypton Toolkit
- Entity Framework Core 10
- Microsoft SQL Server
- Microsoft.Extensions.Hosting ile dependency injection ve configuration

## Çözüm yapısı

```text
R3.sln
├─ src
│  ├─ R3.Desktop         WinForms kullanıcı arayüzü
│  ├─ R3.Application     İş akışları ve uygulama sözleşmeleri
│  ├─ R3.Domain          Temel iş modelleri ve kurallar
│  └─ R3.Infrastructure  MSSQL, EF Core ve dış sistem uygulamaları
├─ tests
│  └─ R3.UnitTests
└─ database
   ├─ 01_ERP_Database.sql
   ├─ 02_Development_Login.sql
   ├─ 03_Organization_And_Product_Upgrade.sql
   ├─ 04_Retail_Variants_And_Trade_Workflow.sql
   ├─ 05_Exchange_Dispatch_Account_Cash_Bank.sql
   ├─ 06_Retail_Finance_Operations.sql
   ├─ 07_Inventory_Account_MasterData.sql
   ├─ 08_Operational_Defaults.sql
   ├─ 09_Document_Posting_Engine.sql
   ├─ 10_Reversal_And_Payment_Allocation.sql
   ├─ 11_Invoice_Commercial_Details.sql
   └─ 12_Agenda.sql
```

Bağımlılık yönü:

```text
Desktop -> Application
Desktop -> Infrastructure
Infrastructure -> Application -> Domain
```

`Domain` katmanı kullanıcı arayüzü veya veritabanı teknolojisine bağımlı değildir.

## Visual Studio ile açma

1. `R3.sln` dosyasını Visual Studio ile açın.
2. `R3.Desktop` projesini başlangıç projesi yapın.
3. `appsettings.json` içindeki `R3Database` bağlantısını kendi SQL Server ortamınıza göre düzenleyin.
4. `database/01_ERP_Database.sql` betiğini SQL Server Management Studio ile çalıştırın.
5. `database/03_Organization_And_Product_Upgrade.sql` betiğini çalıştırın.
6. `database/04_Retail_Variants_And_Trade_Workflow.sql` betiğini çalıştırın.
7. `database/05_Exchange_Dispatch_Account_Cash_Bank.sql` betiğini çalıştırın.
8. `database/06_Retail_Finance_Operations.sql` betiğini çalıştırın.
9. `database/07_Inventory_Account_MasterData.sql` betiğini çalıştırın.
10. `database/08_Operational_Defaults.sql` betiğini çalıştırın.
11. `database/09_Document_Posting_Engine.sql` betiğini çalıştırın.
12. `database/10_Reversal_And_Payment_Allocation.sql` betiğini çalıştırın.
13. Yalnızca geliştirme ortamında `database/02_Development_Login.sql` betiğini çalıştırın.
14. Çözümü derleyip uygulamayı başlatın.

`sqlcmd` kullanılıyorsa Türkçe karakterlerin korunması için betikler `-f 65001` parametresiyle çalıştırılmalıdır:

```powershell
sqlcmd -S localhost -E -b -f 65001 -i database/01_ERP_Database.sql
sqlcmd -S localhost -E -b -f 65001 -i database/03_Organization_And_Product_Upgrade.sql
sqlcmd -S localhost -E -b -f 65001 -i database/04_Retail_Variants_And_Trade_Workflow.sql
sqlcmd -S localhost -E -b -f 65001 -i database/05_Exchange_Dispatch_Account_Cash_Bank.sql
sqlcmd -S localhost -E -b -f 65001 -i database/06_Retail_Finance_Operations.sql
sqlcmd -S localhost -E -b -f 65001 -i database/07_Inventory_Account_MasterData.sql
sqlcmd -S localhost -E -b -f 65001 -i database/08_Operational_Defaults.sql
sqlcmd -S localhost -E -b -f 65001 -i database/09_Document_Posting_Engine.sql
sqlcmd -S localhost -E -b -f 65001 -i database/10_Reversal_And_Payment_Allocation.sql
sqlcmd -S localhost -E -b -f 65001 -i database/11_Invoice_Commercial_Details.sql
sqlcmd -S localhost -E -b -f 65001 -i database/12_Agenda.sql
sqlcmd -S localhost -E -b -f 65001 -i database/02_Development_Login.sql
```

Varsayılan bağlantı Windows Authentication ile yerel `EngineeringERP` veritabanına bağlanır. Parolalar kaynak koda veya `appsettings.json` dosyasına yazılmamalıdır.

## Geliştirme girişi

`02_Development_Login.sql` çalıştırıldıktan sonra:

```text
Şube / Mağaza No: MERKEZ
Kullanıcı Adı:     admin
PIN:               1234
```

Bu hesap yalnızca yerel geliştirme içindir. Üretim ortamında bu betik çalıştırılmamalı ve varsayılan PIN kullanılmamalıdır. PIN değerleri PBKDF2-SHA256 ile tuzlanmış hash olarak saklanır; beş hatalı denemede hesap kilitlenir.

## Komut satırı

```powershell
dotnet restore
dotnet build R3.sln
dotnet test R3.sln
dotnet run --project src/R3.Desktop
```
