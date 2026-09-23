# R3 Project Instructions

## Stack

- C# / .NET 10, WPF masaüstü uygulaması.
- Yerel SQLite için `StoreDatabase`; sunucu yolu EF Core/PostgreSQL.
- UI ekranları `src/R3.Desktop`, iş servisleri `src/R3.Infrastructure` altındadır.

## Kurallar

- İş kurallarını ekran koduna değil `Local*Service` sınıflarına koy.
- Stok, cari ve belge etkilerini aynı transaction içinde uygula.
- Belge durum geçişlerini servis katmanında doğrula.
- Türkçe kullanıcı metinleri ve Türkçe kolon başlıkları kullan.
- Var olan kullanıcı değişikliklerini koru; geniş kapsamlı reset/checkout kullanma.
- Uzun SQLite sorgularını UI thread’inden çıkar.

## Doğrulama

```powershell
dotnet test --no-restore --nologo
dotnet build src/R3.Desktop/R3.Desktop.csproj --no-restore -p:NoWarn=CA1416 -p:GenerateAssemblyInfo=false -p:GenerateTargetFrameworkAttribute=false
```

Detaylı mimari ve veri akışı için `docs/R3-ONBOARDING-GUIDE.md` ve `docs/R3-VERITABAN-EKRAN-ANALIZI.md` dosyalarını kullan.
