using System.Text.Json;

var dryRun = args.Any(x => string.Equals(x, "--dry-run", StringComparison.OrdinalIgnoreCase));
var settingsPath = args.SkipWhile(x => x != "--settings").Skip(1).FirstOrDefault() ?? "migration.settings.json";
var report = new MigrationReport { Mode = dryRun ? "dry-run" : "import" };
Console.WriteLine($"R3 ASB Migration ({report.Mode})");
Console.WriteLine("ASB bağlantısı read-only tasarlanmıştır; bu CLI gerçek veritabanına yazmaz.");
Console.WriteLine(File.Exists(settingsPath) ? $"Mapping: {settingsPath}" : "Mapping ayarı bulunamadı; örnek mapping ile devam ediliyor.");
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(dryRun ? "Dry-run tamamlandı: R3 veritabanına yazılmadı." : "Import provider bağlantısı yapılandırılana kadar preview modunda çalışır.");

public sealed class MigrationReport
{
    public string Mode { get; init; } = "dry-run";
    public Dictionary<string, int> Created { get; } = new();
    public Dictionary<string, int> Updated { get; } = new();
    public Dictionary<string, int> Skipped { get; } = new();
    public List<MigrationError> Errors { get; } = [];
}
public sealed record MigrationError(string Entity, long? LegacyId, string Reason);
