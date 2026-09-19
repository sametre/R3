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
- `CefSharp.Wpf.NETCore` 151.3.240 → 152.0.60 available (routine bump).
- `coverlet.collector`/`Microsoft.NET.Test.Sdk`/`xunit.runner.visualstudio`
  all have newer major versions available; routine bump, no urgency.

## Engine-hardening initiative (2026-09-19) — candidate dependencies evaluated, none added

A later "professionalize every engine" proposal listed ~30 candidate
packages across ORM/caching/background-jobs/reporting/observability/testing.
Per this project's own policy (top of this file) and its explicit "don't add
a dependency whose benefit isn't proven" rule, every candidate was evaluated
against the actual codebase — see
`docs/architecture/R3-ENGINE-ARCHITECTURE.md` for the full baseline this
table is based on — and **none were added**. The premise that prompted the
list (EF Core as the write engine, needing a Dapper read side to balance it)
does not match reality: the real business engine is hand-written SQL over
`Microsoft.Data.Sqlite` (`StoreDatabase`), and EF Core only ever covers the
much smaller PostgreSQL organization/catalog model. No measured performance
complaint exists anywhere in the project to justify any of these.

| Package | Purpose it would serve | Decision | Reason |
| --- | --- | --- | --- |
| Dapper | Read-side ORM to pair with EF Core writes | Rejected | No EF/Dapper split to build — business reads are already hand-written parameterized SQL returning `DataTable`, functionally equivalent. Revisit only for one specific query that's *measured* slow. |
| DapperAOT | Compile-time Dapper | Rejected | Depends on Dapper being adopted first, and even then only on a measured hot path, never applied blanket. |
| Mapster | DTO/Entity/ViewModel mapping | Rejected | No mapping layer exists to replace — records are constructed directly from `DataRow` today. |
| OpenTelemetry / prometheus-net | Distributed tracing/metrics | Rejected | Single desktop process + one API host, no multi-service call chain to trace, no reported latency issue. |
| Polly | HTTP retry/circuit breaker | Rejected for now | No real HTTP provider exists yet (`ManualElectronicDocumentProvider` is a stub); the e-document outbox already hand-rolls exponential backoff, which is enough for one stub. Reconsider when a real GİB entegratör HTTP client is wired in. |
| Quartz.NET | Background job scheduling | Rejected | No recurring/cron-style job exists; current design (e-document outbox) uses simple polling, not scheduling. Reconsider if the number of recurring jobs grows past what a couple of timers can read clearly. |
| FusionCache / StackExchange.Redis | Caching (local/distributed) | Rejected | No measured lookup latency problem; no multi-instance deployment to share cache across. |
| ClosedXML / MiniExcel | Excel export | Rejected | No Excel export feature exists yet. Decide between the two (styled-document vs. streaming-large-dataset) when a real export feature is requested, based on that feature's actual row count. |
| CsvHelper | CSV import/export | Rejected | No CSV feature exists yet. |
| PDFsharp / MigraDoc | PDF generation | Rejected | No PDF generation exists yet (the e-document `Pdf` payload type is schema-ready for when it does). QuestPDF was never considered as primary per this project's license policy (source-available, not OSS) — PDFsharp/MigraDoc remain the pre-approved open-source choice for whenever this is built. |
| ZXing.Net / QRCoder | Barcode/QR image generation | Rejected | Barcode *lookup* already exists (`product_barcodes` table); barcode *image generation* has no consumer yet. |
| LiveCharts2 / ScottPlot | Dashboard charts | Rejected | No dashboard screen exists yet. |
| BenchmarkDotNet | Micro-benchmarking | Rejected for now | Added the moment an actual "this feels slow" complaint needs measuring — not preemptively, per this project's own "benchmark, don't guess" rule. |
| Lucene.NET | Full-text search | Rejected | Every `Search()` method already uses `LIKE`-based SQLite queries at a scale where that's fine. SQLite's built-in FTS5 (zero new dependency) is the correct next step if search ever needs to get faster — not a full search engine library. |
| MemoryPack | Binary serialization | Rejected | `System.Text.Json` already covers both existing serialization needs (grid layout JSON, DPAPI-protected login JSON); no binary-format requirement exists. |
| Testcontainers | Containerized integration tests | Rejected for now | Current tests already exercise real SQLite files (not mocks) via temp-directory `IDisposable` fixtures — realistic without Docker. Reconsider only if PostgreSQL-side tests need a real Postgres instance (today's `R3.Server.Tests` only checks the no-database-configured path). |
| ArchUnitNET | Enforced layering rules | Rejected for now | 6 small projects, no observed layering violation; the layering convention is already documented in `docs/ARCHITECTURE.md`. Reconsider if a real violation is found or the team grows. |
| Scrutor | Assembly-scanning DI registration | Rejected | Desktop has no DI container by design (see "Deliberately not added" above); Server's DI surface is 2 registrations — scanning would add indirection, not clarity. |
| Microsoft.EntityFrameworkCore / .Sqlite | ORM | Already present | In use today for the PostgreSQL (and optionally SQLite) organization model — see `docs/architecture/R3-ENGINE-ARCHITECTURE.md`. No change. |
| FluentValidation, CommunityToolkit.Mvvm, Serilog | — | Already present | Already adopted and in active use — see their sections above. No change. |
| CefSharp.Wpf.NETCore | Embedded browser (login) | Already present | In use for `StartupLoginWindow`'s embedded browser flow; a newer version exists (see Known technical debt). |
