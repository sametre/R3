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

