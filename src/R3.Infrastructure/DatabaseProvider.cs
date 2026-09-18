using Microsoft.EntityFrameworkCore;

namespace R3.Infrastructure;

public enum DatabaseProvider { SQLite, PostgreSQL }
public static class R3DatabaseOptions
{
    public static void Configure(DbContextOptionsBuilder options, DatabaseProvider provider, string connectionString)
    {
        if (provider == DatabaseProvider.SQLite) options.UseSqlite(connectionString);
        else options.UseNpgsql(connectionString);
    }
    public static DatabaseProvider FromEnvironment() => string.Equals(Environment.GetEnvironmentVariable("R3_DATABASE_PROVIDER"), "postgresql", StringComparison.OrdinalIgnoreCase) ? DatabaseProvider.PostgreSQL : DatabaseProvider.SQLite;
    public static string DefaultSqliteConnection() => $"Data Source={Environment.GetEnvironmentVariable("R3_SQLITE_PATH") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "R3", "data", "r3.db")};Foreign Keys=True;";
}
