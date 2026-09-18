# R3 ERP Architecture

`R3.Desktop` is a WPF client. It owns presentation and API contracts, and must not contain PostgreSQL credentials, `DbContext`, or a database connection. `R3ApiClient` uses the `R3_SERVER_URL` environment variable and reports server connectivity in the status bar.

`R3.Server` is an ASP.NET Core host. It exposes health/metadata and Organization REST CRUD handlers. The server resolves `R3DbContext` only when `R3_POSTGRES_CONNECTION` or the `ConnectionStrings:R3` configuration value is present.

`R3.Application` contains use cases and `R3.Contracts` contains transport DTOs and paging models. `R3.Domain` contains business entities and invariants. `R3.Infrastructure` owns EF Core/Npgsql persistence and logging dependencies. Dependency direction is Desktop → Contracts; Server → Application/Infrastructure; Infrastructure → Domain.

PostgreSQL uses the `r3` schema, snake_case names, UUID primary keys, scoped unique indexes, legacy fields, and audit timestamps. The local SQLite store remains only for the existing offline Mağaza prototype and is not used by the Server.

Run locally with `dotnet run --project src/R3.Server` after setting `R3_POSTGRES_CONNECTION` or copying `appsettings.Development.example.json` to a local, untracked configuration file. Check `/health` before opening future server-backed screens.
