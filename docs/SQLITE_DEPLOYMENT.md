# SQLite Local Deployment

SQLite is the default local provider for development, offline and single-machine use. The default file is under the user's local application data folder: `R3/data/r3.db`. Set `R3_SQLITE_PATH` to use an explicit location. `StoreDatabase` enables WAL, foreign keys and a five-second busy timeout through the provider connection.

SQLite is not a shared multi-user network database. PostgreSQL remains available through `R3_DATABASE_PROVIDER=postgresql` and the Server connection string. Apply EF migrations with `dotnet ef database update --project src/R3.Infrastructure --startup-project src/R3.Server` when using the selected provider.

The ASB tool is read-only and currently preview-only. Run `dotnet run --project tools/R3.AsbMigration -- --dry-run`; it never writes to ASB.
