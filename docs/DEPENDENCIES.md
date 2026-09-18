# Third-Party Dependencies

Central Package Management (`Directory.Packages.props`) pins every version
used in the solution. All licenses below were verified against nuget.org
package metadata (`licenseExpression`), not assumed. Every package here is a
stable release — no `-preview`/`-rc`/`-alpha` version is used anywhere.

## Validation

| Package | Version | Project(s) | License | Repository |
| --- | --- | --- | --- | --- |
| FluentValidation | 12.1.1 | R3.Application | Apache-2.0 | github.com/FluentValidation/FluentValidation |
| FluentValidation.DependencyInjectionExtensions | 12.1.1 | R3.Server | Apache-2.0 | github.com/FluentValidation/FluentValidation |

Why: input-shape validation (required fields, formats, ranges) for the
Organization API, separate from persistence-layer business rules — see
`docs/ARCHITECTURE.md`.

## Data access

| Package | Version | Project(s) | License | Repository |
| --- | --- | --- | --- | --- |
| Microsoft.Data.Sqlite | 10.0.12 | R3.Infrastructure | MIT | github.com/dotnet/efcore |
| Microsoft.EntityFrameworkCore | 10.0.12 | R3.Infrastructure | MIT | github.com/dotnet/efcore |
| Microsoft.EntityFrameworkCore.Design | 10.0.12 | R3.Infrastructure, R3.Server | MIT | github.com/dotnet/efcore |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.12 | R3.Infrastructure | MIT | github.com/dotnet/efcore |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 | R3.Infrastructure | PostgreSQL License (OSI-approved, MIT-style) | github.com/npgsql/efcore.pg |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.5 | R3.Infrastructure | Apache-2.0 | github.com/ericsink/SQLitePCL.raw |

Why: SQLite is the offline/local Desktop store (`StoreDatabase`); EF Core +
Npgsql is the canonical PostgreSQL path for `R3.Server`
(`R3DbContext`/`OrganizationService`). `Microsoft.EntityFrameworkCore.Design`
is design-time-only (`IncludeAssets`/`PrivateAssets` restrict it to tooling,
e.g. `dotnet ef migrations add`) and is not part of the shipped runtime
behavior of either app.

## Logging (Phase 3)

| Package | Version | Project(s) | License | Repository |
| --- | --- | --- | --- | --- |
| Serilog | 4.4.0 | R3.Server, R3.Desktop | Apache-2.0 | github.com/serilog/serilog |
| Serilog.AspNetCore | 10.0.0 | R3.Server | Apache-2.0 | github.com/serilog/serilog-aspnetcore |
| Serilog.Extensions.Hosting | 10.0.0 | R3.Server | Apache-2.0 | github.com/serilog/serilog-extensions-hosting |
| Serilog.Extensions.Logging | 10.0.0 | R3.Desktop | Apache-2.0 | github.com/serilog/serilog-extensions-logging |
| Serilog.Sinks.File | 7.0.0 | R3.Desktop | Apache-2.0 | github.com/serilog/serilog-sinks-file |

Why: `Serilog` is the sink implementation behind `ILogger<T>` for both apps —
see `docs/LOGGING.md`. `Serilog.AspNetCore` gives `R3.Server` `UseSerilog()`
on `IHostBuilder` and `UseSerilogRequestLogging()`.
`Serilog.Extensions.Logging` gives `R3.Desktop` the `AddSerilog()` bridge onto
a plain `Microsoft.Extensions.Logging.LoggerFactory` (Desktop has no
`IHostBuilder`/DI container, so it can't use `Serilog.Extensions.Hosting`
directly the way the Server does). No Seq/Elasticsearch/OpenTelemetry sink is
used — local rolling files only, see `docs/LOGGING.md`.

**Note on `Serilog.Sinks.Console`**: not added. `R3.Server`'s `WriteTo.Console()`
call in `Program.cs` works because `Serilog.AspNetCore` already depends on
`Serilog.Sinks.Console` transitively — an explicit direct reference would be
redundant. `R3.Desktop` (a GUI app with no console) does not use a console
sink at all.

## Desktop MVVM / UI (Phase 2 POC)

| Package | Version | Project(s) | License | Repository |
| --- | --- | --- | --- | --- |
| CommunityToolkit.Mvvm | 8.4.2 | R3.Desktop | MIT | github.com/CommunityToolkit/dotnet |
| WPF-UI | 4.3.0 | R3.Desktop | MIT | github.com/lepoco/wpfui |

Why: `ObservableObject`/`[ObservableProperty]`/`[RelayCommand]`/
`IAsyncRelayCommand` for the Cari Kartlar reference screen
(`AccountsViewModel`, `AccountEditViewModel`); no DI container or messenger
feature is used. WPF-UI provides Fluent-styled `Button`/`TextBox` controls,
scoped locally to `AccountsView`/`AccountEditDialog`'s own `Resources` (not
`App.xaml`), so the rest of the app's look is untouched — see
`docs/ARCHITECTURE.md`.

## Testing

| Package | Version | Project(s) | License | Repository |
| --- | --- | --- | --- | --- |
| xunit | 2.9.3 | R3.Domain.Tests, R3.Server.Tests, R3.Desktop.Tests | Apache-2.0 | github.com/xunit/xunit |
| xunit.runner.visualstudio | 3.1.4 | (same) | Apache-2.0 | github.com/xunit/visualstudio.xunit |
| Microsoft.NET.Test.Sdk | 17.14.1 | (same) | MIT | github.com/microsoft/vstest |
| coverlet.collector | 6.0.4 | (same) | MIT | github.com/coverlet-coverage/coverlet |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.12 | R3.Server.Tests | MIT | github.com/dotnet/aspnetcore |

`xunit` 2.9.3 is flagged "Legacy" by `dotnet list package --deprecated`
(successor: `xunit.v3`). Left as-is for now — v2→v3 is an API migration, not a
version bump, and out of scope for the logging phase; tracked as a known
technical debt item (see the bottom of this file).

## Deliberately not added

Per the project's package policy (avoid unnecessary framework dependencies):
Prism, MediatR, AutoMapper, ReactiveUI, a second ORM, an event bus/message
broker, a CQRS framework, Seq/Elasticsearch/OpenTelemetry log sinks,
`Serilog.Enrichers.Process` (process id is added as a static enriched
property at bootstrap instead — see `DesktopLogging.cs` — since it never
changes within a process, a per-event enricher package isn't needed).

## Known technical debt

- `xunit` v2 → v3 migration (see Testing table above).
- `Microsoft.EntityFrameworkCore*`/`Npgsql.EntityFrameworkCore.PostgreSQL`
  patch releases newer than what's pinned here may exist by the time you read
  this — re-run `dotnet list package --outdated` periodically.
