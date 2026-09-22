using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using R3.Infrastructure;

var dryRun = args.Any(x => string.Equals(x, "--dry-run", StringComparison.OrdinalIgnoreCase));
var settingsPath = args.SkipWhile(x => x != "--settings").Skip(1).FirstOrDefault() ?? "migration.settings.json";
var sourceConnection = args.SkipWhile(x => x != "--source-connection").Skip(1).FirstOrDefault();
var targetPath = args.SkipWhile(x => x != "--target").Skip(1).FirstOrDefault();
var sourceName = args.SkipWhile(x => x != "--source-name").Skip(1).FirstOrDefault() ?? "ASB:UNKNOWN";
var canonicalCore = args.Any(x => string.Equals(x, "--canonical-core", StringComparison.OrdinalIgnoreCase));
var canonicalDocuments = args.Any(x => string.Equals(x, "--canonical-documents", StringComparison.OrdinalIgnoreCase));
var repairWal = args.Any(x => string.Equals(x, "--repair-wal", StringComparison.OrdinalIgnoreCase));
var verifyTarget = args.Any(x => string.Equals(x, "--verify-target", StringComparison.OrdinalIgnoreCase));
var canonicalShipments = args.Any(x => string.Equals(x, "--canonical-shipments", StringComparison.OrdinalIgnoreCase));
var report = new MigrationReport { Mode = dryRun ? "dry-run" : "archive-import" };
Console.WriteLine($"R3 ASB Migration ({report.Mode})");
Console.WriteLine(File.Exists(settingsPath) ? $"Mapping: {settingsPath}" : "Mapping ayarı bulunamadı; örnek mapping ile devam ediliyor.");

if (repairWal && !string.IsNullOrWhiteSpace(targetPath))
{
    var raw = new SqliteConnection($"Data Source={targetPath};Mode=ReadWriteCreate;Cache=Shared"); raw.Open();
    using var checkpoint = raw.CreateCommand(); checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);"; checkpoint.ExecuteNonQuery();
    using var integrity = raw.CreateCommand(); integrity.CommandText = "PRAGMA integrity_check;"; Console.WriteLine($"SQLite integrity: {integrity.ExecuteScalar()}");
    return;
}
if (verifyTarget && !string.IsNullOrWhiteSpace(targetPath))
{
    using var raw = new SqliteConnection($"Data Source={targetPath};Mode=ReadOnly;Cache=Shared"); raw.Open();
    foreach (var table in new[] { "accounts", "products", "sales_documents", "sales_document_lines", "purchase_documents", "purchase_document_lines", "inventory_transactions", "inventory_balances", "shipment_orders", "shipment_order_lines" })
    { using var count = raw.CreateCommand(); count.CommandText = $"SELECT COUNT(*) FROM {table}"; Console.WriteLine($"{table}: {count.ExecuteScalar()}"); }
    return;
}

if (dryRun || string.IsNullOrWhiteSpace(sourceConnection))
{
    Console.WriteLine("Kaynak bağlantısı verilmedi. Sadece plan üretildi; R3 veritabanına yazılmadı.");
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    return;
}

var database = new StoreDatabase(targetPath);
var runId = Guid.NewGuid().ToString();
database.Execute("INSERT INTO legacy_migration_runs(id,source_name,mode,status,started_at) VALUES($id,$source,'archive-import','Running',$now)", ("$id", runId), ("$source", sourceName), ("$now", DateTime.UtcNow.ToString("O")));
try
{
    var sourceBuilder = new SqlConnectionStringBuilder(sourceConnection) { MultipleActiveResultSets = true };
    await using var source = new SqlConnection(sourceBuilder.ConnectionString);
    await source.OpenAsync();
    if (canonicalCore)
    {
        var companyId = database.Query("SELECT id FROM companies ORDER BY code LIMIT 1").Rows[0][0].ToString()!;
        var result = await new CoreCanonicalImporter(sourceName, database, companyId, source).ImportAsync();
        report.Created["accounts"] = result.Accounts; report.Created["products"] = result.Products; report.Created["units"] = result.Units; report.Created["product_barcodes"] = result.Barcodes;
        report.RowCount = result.Accounts + result.Products + result.Units + result.Barcodes;
        database.Execute("UPDATE legacy_migration_runs SET status='Completed',finished_at=$now,table_count=4,row_count=$rows,error_count=0 WHERE id=$id", ("$now", DateTime.UtcNow.ToString("O")), ("$rows", report.RowCount), ("$id", runId));
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return;
    }
    if (canonicalDocuments)
    {
        var companyId = database.Query("SELECT id FROM companies ORDER BY code LIMIT 1").Rows[0][0].ToString()!;
        var result = await new DocumentCanonicalImporter(sourceName, database, companyId, source).ImportAsync();
        report.Created["sales_documents"] = result.Sales; report.Created["purchase_documents"] = result.Purchases; report.Created["document_lines"] = result.InvoiceLines; report.Created["inventory_transactions"] = result.Movements;
        report.RowCount = result.Sales + result.Purchases + result.InvoiceLines + result.Movements;
        database.Execute("UPDATE legacy_migration_runs SET status='Completed',finished_at=$now,table_count=4,row_count=$rows,error_count=0 WHERE id=$id", ("$now", DateTime.UtcNow.ToString("O")), ("$rows", report.RowCount), ("$id", runId));
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return;
    }
    if (canonicalShipments)
    {
        var companyId = database.Query("SELECT id FROM companies ORDER BY code LIMIT 1").Rows[0][0].ToString()!;
        var result = await new ShipmentCanonicalImporter(sourceName, database, companyId, source).ImportAsync();
        report.Created["shipment_orders"] = result.Orders; report.Created["shipment_order_lines"] = result.Lines; report.RowCount = result.Orders + result.Lines;
        database.Execute("UPDATE legacy_migration_runs SET status='Completed',finished_at=$now,table_count=2,row_count=$rows,error_count=0 WHERE id=$id", ("$now", DateTime.UtcNow.ToString("O")), ("$rows", report.RowCount), ("$id", runId));
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return;
    }
    var tables = new List<(string Schema, string Name)>();
    await using (var tableCommand = new SqlCommand("SELECT s.name,t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name", source))
    await using (var reader = await tableCommand.ExecuteReaderAsync())
        while (await reader.ReadAsync()) tables.Add((reader.GetString(0), reader.GetString(1)));

    foreach (var table in tables)
    {
        try
        {
            var columns = new List<string>();
            await using (var columnCommand = new SqlCommand("SELECT c.name FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE s.name=@schema AND t.name=@table ORDER BY c.column_id", source))
            { columnCommand.Parameters.AddWithValue("@schema", table.Schema); columnCommand.Parameters.AddWithValue("@table", table.Name); await using var cr = await columnCommand.ExecuteReaderAsync(); while (await cr.ReadAsync()) columns.Add(cr.GetString(0)); }
            database.Execute("INSERT OR REPLACE INTO legacy_tables(source_name,schema_name,table_name,columns_json,row_count,status,last_run_id) VALUES($source,$schema,$table,$columns,0,'Imported',$run)", ("$source", sourceName), ("$schema", table.Schema), ("$table", table.Name), ("$columns", JsonSerializer.Serialize(columns)), ("$run", runId));
            var count = 0;
            var schemaSql = table.Schema.Replace("]", "]]", StringComparison.Ordinal);
            var tableSql = table.Name.Replace("]", "]]", StringComparison.Ordinal);
            await using (var rowCommand = new SqlCommand($"SELECT * FROM [{schemaSql}].[{tableSql}]", source))
            await using (var rows = await rowCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
            {
                while (await rows.ReadAsync())
                {
                    var values = new Dictionary<string, object?>();
                    for (var i = 0; i < rows.FieldCount; i++) values[rows.GetName(i)] = rows.IsDBNull(i) ? null : rows.GetValue(i);
                    database.Execute("INSERT OR REPLACE INTO legacy_rows(source_name,schema_name,table_name,row_number,row_json,imported_at,run_id) VALUES($source,$schema,$table,$number,$json,$now,$run)", ("$source", sourceName), ("$schema", table.Schema), ("$table", table.Name), ("$number", count), ("$json", JsonSerializer.Serialize(values)), ("$now", DateTime.UtcNow.ToString("O")), ("$run", runId));
                    count++;
                }
            }
            database.Execute("UPDATE legacy_tables SET row_count=$count WHERE source_name=$source AND schema_name=$schema AND table_name=$table", ("$count", count), ("$source", sourceName), ("$schema", table.Schema), ("$table", table.Name));
            report.Created[table.Schema + "." + table.Name] = count; report.RowCount += count;
        }
        catch (Exception ex) { report.Errors.Add(new MigrationError(table.Schema + "." + table.Name, null, ex.Message)); }
    }
    database.Execute("UPDATE legacy_migration_runs SET status='Completed',finished_at=$now,table_count=$tables,row_count=$rows,error_count=$errors WHERE id=$id", ("$now", DateTime.UtcNow.ToString("O")), ("$tables", tables.Count), ("$rows", report.RowCount), ("$errors", report.Errors.Count), ("$id", runId));
}
catch (Exception ex)
{
    database.Execute("UPDATE legacy_migration_runs SET status='Failed',finished_at=$now,error_count=1,error_message=$error WHERE id=$id", ("$now", DateTime.UtcNow.ToString("O")), ("$error", ex.Message), ("$id", runId));
    report.Errors.Add(new MigrationError("Migration", null, ex.Message));
}
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

public sealed class MigrationReport
{
    public string Mode { get; init; } = "dry-run";
    public Dictionary<string, int> Created { get; } = new();
    public Dictionary<string, int> Updated { get; } = new();
    public Dictionary<string, int> Skipped { get; } = new();
    public List<MigrationError> Errors { get; } = [];
    public long RowCount { get; set; }
}
public sealed record MigrationError(string Entity, long? LegacyId, string Reason);
