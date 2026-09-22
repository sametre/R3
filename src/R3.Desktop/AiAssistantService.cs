using System.Data;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using R3.Infrastructure;

namespace R3.Desktop;

public sealed record AiAssistantReply(string Text, bool UsedLocalReport = false);

/// <summary>
/// AR3 AI gateway. The model never receives a database connection and never writes data.
/// Local report tools collect the minimum read-only context first; the model only explains
/// that context in natural language when an API key is configured.
/// </summary>
public sealed class Ar3AiAssistant(StoreDatabase database, string companyId, string userName)
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(45) };
    private readonly string _userName = userName;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AiAssistantReply> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        var cleanQuestion = question.Trim();
        if (cleanQuestion.Length == 0) return new("Bir soru yazın. Örneğin: ‘R3 carisinin ekstresini getir.’");

        var localContext = BuildReadOnlyContext(cleanQuestion);
        var apiKey = Environment.GetEnvironmentVariable("R3_OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (localContext != null) return new(localContext + "\n\nAI açıklama katmanı henüz yapılandırılmadı. Model bağlantısı için R3_OPENAI_API_KEY ayarlanmalıdır.", true);
            return new("Yerel rapor araçlarında bu soruya uygun bir komut bulamadım. Ekstre, cari özeti veya stok sorgusu deneyin.\n\nAI model bağlantısı için R3_OPENAI_API_KEY ayarlanmalıdır.");
        }

        var model = Environment.GetEnvironmentVariable("R3_OPENAI_MODEL") ?? "gpt-4o-mini";
        var prompt = localContext ?? "Bu soru için doğrudan yerel rapor bağlamı bulunamadı. Veri uydurma; kullanıcıdan cari kodu, stok kodu veya tarih aralığı iste.";
        return new(await CompleteAsync(apiKey, model, cleanQuestion, prompt, cancellationToken), localContext != null);
    }

    private async Task<string> CompleteAsync(string apiKey, string model, string question, string context, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Environment.GetEnvironmentVariable("R3_OPENAI_ENDPOINT") ?? "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model,
            temperature = 0.2,
            messages = new object[]
            {
                new { role = "system", content = $"Sen AR3 ERP içindeki kurumsal Türkçe asistansın. Oturum kullanıcısı: {_userName}. Yalnızca verilen yerel rapor bağlamını kullan; veri uydurma. Kullanıcıya kısa, maddeli ve sayıları Türkçe biçimde açıkla. Yazma, silme, onaylama veya finansal işlem başlatma yetkin yok; böyle bir istek gelirse ilgili AR3 ekranına yönlendir." },
                new { role = "user", content = $"Soru: {question}\n\nYerel AR3 rapor bağlamı:\n{context}" }
            }
        }, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) return $"AI servisi yanıt vermedi ({(int)response.StatusCode}). Yerel rapor bağlamı:\n\n{context}";
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim() ?? context;
        }
        catch (JsonException) { return context; }
    }

    private string? BuildReadOnlyContext(string question)
    {
        try
        {
            var lower = question.ToLowerInvariant();
            if (lower.Contains("ekstre") || lower.Contains("cari hareket") || lower.Contains("cari bakiye")) return AccountStatement(question);
            if (lower.Contains("stok") || lower.Contains("ürün") || lower.Contains("urun") || lower.Contains("barkod")) return InventorySummary(question);
            if (lower.Contains("cari") || lower.Contains("müşteri") || lower.Contains("musteri") || lower.Contains("tedarikçi")) return AccountSummary();
            if (lower.Contains("satış") || lower.Contains("satis") || lower.Contains("alış") || lower.Contains("alis")) return SalesPurchaseSummary();
        }
        catch (Exception ex) { return "Yerel rapor okunamadı: " + ex.Message; }
        return null;
    }

    private string AccountSummary()
    {
        var row = database.Query("""
            SELECT COUNT(*) AS Toplam, SUM(CASE WHEN is_active=1 THEN 1 ELSE 0 END) AS Aktif,
                   SUM(CASE WHEN account_type IN ('Customer','CustomerAndSupplier') THEN 1 ELSE 0 END) AS Musteri,
                   SUM(CASE WHEN account_type IN ('Supplier','CustomerAndSupplier') THEN 1 ELSE 0 END) AS Tedarikci
            FROM accounts WHERE company_id=$company
            """, ("$company", companyId)).Rows[0];
        return $"Cari özeti ({DateTime.Now:dd.MM.yyyy})\nToplam cari: {Number(row["Toplam"])}\nAktif: {Number(row["Aktif"])}\nMüşteri hesabı: {Number(row["Musteri"])}\nTedarikçi hesabı: {Number(row["Tedarikci"])}";
    }

    private string? AccountStatement(string question)
    {
        var account = FindAccount(question);
        if (account == null) return "Ekstre için cari kodu veya cari adını belirtin.";
        var row = database.Query("""
            SELECT COUNT(*) AS Islem, COALESCE(SUM(debit),0) AS Borc, COALESCE(SUM(credit),0) AS Alacak,
                   COALESCE(SUM(debit-credit),0) AS Bakiye
            FROM account_transactions WHERE company_id=$company AND account_id=$account
            """, ("$company", companyId), ("$account", account.Value.Id)).Rows[0];
        return $"Cari ekstresi\nCari: {account.Value.Code} — {account.Value.Name}\nİşlem sayısı: {Number(row["Islem"])}\nBorç: {Money(row["Borc"])}\nAlacak: {Money(row["Alacak"])}\nBakiye: {Money(row["Bakiye"])}";
    }

    private string? InventorySummary(string question)
    {
        var term = FindTerm(question, "stok", "ürün", "urun", "barkod", "getir", "göster", "rapor", "nedir");
        if (term == null) return "Stok sorgusu için stok kodu, ürün adı veya barkod belirtin.";
        var table = database.Query("""
            SELECT p.code AS Kod, p.name AS Ad, p.product_type AS Tip,
                   COALESCE(SUM(ib.quantity_on_hand),0) AS Mevcut,
                   COALESCE(SUM(ib.quantity_reserved),0) AS Rezerve,
                   COALESCE(SUM(ib.quantity_available),0) AS Kullanilabilir
            FROM products p LEFT JOIN inventory_balances ib ON ib.product_id=p.id
            WHERE p.company_id=$company AND (p.code LIKE $q OR p.name LIKE $q OR EXISTS (SELECT 1 FROM product_barcodes pb WHERE pb.product_id=p.id AND pb.barcode LIKE $q))
            GROUP BY p.id,p.code,p.name,p.product_type ORDER BY p.code LIMIT 10
            """, ("$company", companyId), ("$q", $"%{term}%"));
        if (table.Rows.Count == 0) return $"‘{term}’ için stok kaydı bulunamadı.";
        var lines = table.Rows.Cast<DataRow>().Select(r => $"{r["Kod"]} — {r["Ad"]}: mevcut {Money(r["Mevcut"])}, rezerve {Money(r["Rezerve"])}, kullanılabilir {Money(r["Kullanilabilir"])}");
        return "Stok sorgusu\n" + string.Join("\n", lines);
    }

    private string SalesPurchaseSummary()
    {
        var account = AccountSummary();
        var sales = database.Query("SELECT COUNT(*) AS Adet, COALESCE(SUM(general_total),0) AS Toplam FROM sales_documents WHERE company_id=$company", ("$company", companyId)).Rows[0];
        var purchases = database.Query("SELECT COUNT(*) AS Adet, COALESCE(SUM(general_total),0) AS Toplam FROM purchase_documents WHERE company_id=$company", ("$company", companyId)).Rows[0];
        return $"Satış ve alış özeti\nSatış belge adedi: {Number(sales["Adet"])}\nSatış toplamı: {Money(sales["Toplam"])}\nAlış belge adedi: {Number(purchases["Adet"])}\nAlış toplamı: {Money(purchases["Toplam"])}\n\n{account}";
    }

    private (string Id, string Code, string Name)? FindAccount(string question)
    {
        var term = FindTerm(question, "cari", "ekstre", "hareket", "bakiye", "getir", "göster", "rapor", "hesap");
        if (term == null) return null;
        var row = database.Query("SELECT id,code,name FROM accounts WHERE company_id=$company AND (code LIKE $q OR name LIKE $q) ORDER BY code LIMIT 1", ("$company", companyId), ("$q", $"%{term}%")).Rows.Cast<DataRow>().FirstOrDefault();
        return row == null ? null : (row["id"].ToString()!, row["code"].ToString()!, row["name"].ToString()!);
    }

    private static string? FindTerm(string question, params string[] ignored)
        => question.Split([' ', ',', '.', ':', ';', '?', '!', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim('"', '\'')).Where(x => x.Length >= 3 && !ignored.Contains(x, StringComparer.OrdinalIgnoreCase)).OrderByDescending(x => x.Length).FirstOrDefault();
    private static string Number(object value) => value is DBNull ? "0" : Convert.ToInt32(value).ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
    private static string Money(object value) => value is DBNull ? "0,00 ₺" : Convert.ToDecimal(value).ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("tr-TR")) + " ₺";
}
