using System.Data;
using Microsoft.Data.SqlClient;

namespace R3.Infrastructure;

/// <summary>
/// Read-only gateway to the live ASB SQL Server database. This service never
/// attaches MDF files and never executes a data-changing command; SQL Server
/// remains the owner of the source database.
/// </summary>
public sealed class AsbLegacyReadService
{
    private const string DefaultConnection =
        "Server=localhost;Database=ASBDB_ERKUR02;Integrated Security=True;TrustServerCertificate=True;ApplicationIntent=ReadOnly;Connect Timeout=3";

    private readonly string _connectionString;

    public AsbLegacyReadService(string? connectionString = null)
    {
        _connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? Environment.GetEnvironmentVariable("R3_ASB_SQL_CONNECTION") ?? DefaultConnection
            : connectionString;
    }

    public async Task<AsbSourceStatus> ProbeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("""
                SELECT DB_NAME() AS DatabaseName,
                       (SELECT COUNT(*) FROM sys.tables WHERE name IN ('CARIKART','MUSTERI','STOKKARTI','SIPARIS','SIPARISDTY')) AS RequiredTables
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return new(false, "Kaynak veritabanı yanıt vermedi.", null, 0);

            return new(true, "ASB canlı kaynak bağlı", reader.GetString(0), reader.GetInt32(1));
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            return new(false, "ASB kaynak bağlantısı yok: " + ex.Message, null, 0);
        }
    }

    public Task<DataTable> SearchCustomersAsync(string term, CancellationToken cancellationToken = default) => QueryAsync("""
        SELECT TOP (250)
               c.CRREF AS LegacyId, c.CRKOD AS CariKodu, c.CRADI AS CariAdi,
               c.CRMUS AS Musteri, c.CRTED AS Tedarikci, c.CRAKTIF AS Aktif,
               d.CRDVERN AS VergiNo, d.CRDTEL1 AS Telefon, d.CRDGSM1 AS Gsm,
               d.CRDIL AS Il, m.MUSKREDI AS KrediLimiti, m.MUSEKKREDI AS EkKredi,
               m.MUS_KVKK_ALINDI AS KvkkOnayi
        FROM dbo.CARIKART c
        LEFT JOIN dbo.CARIKARTDTY d ON d.CRDREF = c.CRREF
        LEFT JOIN dbo.MUSTERI m ON m.MUSREF = c.CRREF
        WHERE (@term = '' OR c.CRKOD LIKE '%' + @term + '%' OR c.CRADI LIKE '%' + @term + '%'
               OR d.CRDVERN LIKE '%' + @term + '%' OR d.CRDGSM1 LIKE '%' + @term + '%')
        ORDER BY c.CRKOD
        """, term, cancellationToken);

    public Task<DataTable> SearchProductsAsync(string term, CancellationToken cancellationToken = default) => QueryAsync("""
        SELECT TOP (250)
               s.STKREF AS LegacyId, s.STKKOD AS StokKodu, s.STKADI AS UrunAdi,
               s.STKTIP AS Tip, s.STKSTS AS Aktif, s.STKKDVORN0 AS SatisKdv,
               s.STKKDVORN1 AS AlisKdv, s.STKMINM AS MinimumStok, s.STKMAXM AS MaksimumStok,
               s.STKRENK AS Renk, s.STKBEDEN AS Beden
        FROM dbo.STOKKARTI s
        WHERE (@term = '' OR s.STKKOD LIKE '%' + @term + '%' OR s.STKADI LIKE '%' + @term + '%')
        ORDER BY s.STKKOD
        """, term, cancellationToken);

    public Task<DataTable> SearchFutureDeliveryOrdersAsync(string term, CancellationToken cancellationToken = default) => QueryAsync("""
        SELECT TOP (500)
               h.SMREF AS LegacyId, h.SMMNO AS SatisNo, h.SMTAR AS SatisTarihi,
               c.CRKOD AS CariKodu, c.CRADI AS Musteri,
               d.SDREF AS SatirRef, d.SDMIK AS Miktar, d.SDIRSMIK AS SevkEdilen,
               d.SDBRM AS Birim, p.STKKOD AS UrunKodu, p.STKADI AS UrunAdi,
               h.SMNOT AS Aciklama, h.SMGC AS Yon
        FROM dbo.SIPARIS h
        INNER JOIN dbo.SIPARISDTY d ON d.SDSMREF = h.SMREF
        LEFT JOIN dbo.CARIKART c ON c.CRREF = h.SMCARREF
        LEFT JOIN dbo.STOKKARTI p ON p.STKREF = d.SDSTKREF
        WHERE (@term = '' OR h.SMMNO LIKE '%' + @term + '%' OR c.CRKOD LIKE '%' + @term + '%'
               OR c.CRADI LIKE '%' + @term + '%' OR p.STKKOD LIKE '%' + @term + '%' OR p.STKADI LIKE '%' + @term + '%')
        ORDER BY h.SMTAR DESC, h.SMREF DESC
        """, term, cancellationToken);

    private async Task<DataTable> QueryAsync(string sql, string term, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@term", SqlDbType.NVarChar, 160).Value = term.Trim();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var table = new DataTable();
        table.Load(reader);
        return table;
    }
}

public sealed record AsbSourceStatus(bool IsAvailable, string Message, string? DatabaseName, int RequiredTableCount);
