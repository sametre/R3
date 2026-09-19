# R3 Engine Architecture — Baseline Report (Phase 0)

Captured 2026-09-19. This is a factual snapshot of what exists today, gathered
by reading the actual code — not an aspiration. It exists to stop a later
"harden the engines" effort from starting on a false premise about what's
already there.

## Build/test baseline

```
dotnet build R3.slnx   → 0 errors, 0 warnings
dotnet test  R3.slnx   → 75/75 passed, 0 failed, 0 skipped
                          (R3.Server.Tests 9, R3.Desktop.Tests 10, R3.Domain.Tests 56)
```

## Solution structure

| Project | TFM | Role |
| --- | --- | --- |
| R3.Domain | net10.0 | Canonical entity records shared by the EF Core (PostgreSQL) side |
| R3.Application | net10.0 | Validators (FluentValidation), permission catalog |
| R3.Contracts | net10.0 | Shared DTOs |
| R3.Infrastructure | net10.0 | **Two independent persistence paths — see below** |
| R3.Server | net10.0 | Minimal API host, ASP.NET Core DI, EF Core/PostgreSQL only |
| R3.Desktop | net10.0-windows | WPF app, **no DI container**, manual `new` wiring in `MainWindow.xaml.cs` |
| R3.Domain.Tests / R3.Desktop.Tests / R3.Server.Tests | net10.0(-windows) | xUnit, real temp-SQLite round-trip tests (no mocks) |
| tools/R3.UiSmoke, tools/R3.AsbMigration | net10.0(-windows) | WPF visual smoke test; one-off legacy-data migration tool |

## The persistence reality (the single most important finding)

There are **three** persistence paths in this codebase today, not the
"EF Core write / Dapper read" hybrid a later engine-hardening effort might
assume:

1. **`StoreDatabase` (raw `Microsoft.Data.Sqlite`, hand-written SQL)** —
   `src/R3.Infrastructure/StoreDatabase.cs` + the `Local*Service` classes
   (`LocalSalesService`, `LocalInventoryService`, `LocalAccountService`,
   `LocalCashService`, `LocalElectronicDocumentService`, …). This is where
   **all real business transactions live**: sales invoices, stock ledger,
   account ledger, cash ledger, e-documents. No ORM, no repository
   abstraction — every service opens its own `SqliteConnection`, builds SQL
   by hand, and manages its own `BeginTransaction`/`Commit`.
2. **`R3DbContext` over PostgreSQL (EF Core + Npgsql)** —
   `src/R3.Infrastructure/R3DbContext.cs`, used by `OrganizationService.cs`
   and `R3.Server/Program.cs`. Covers **only** Company/Branch/Warehouse and
   the product catalog (Brand/Category/Unit/Product/ProductVariant/
   ProductBarcode) plus `Account`/`AccountAddress`/`AuditLog`. No sales,
   inventory ledger, cash, or e-document tables exist on this side at all.
3. **`R3DbContext` over SQLite (EF Core, same DbContext as #2)** —
   `src/R3.Infrastructure/DatabaseProvider.cs` lets the *same* organization
   EF model run against a **second, separate** SQLite file when
   `R3_DATABASE_PROVIDER` is not set to `postgresql` (`FromEnvironment()`
   defaults to SQLite). This is architecturally distinct from path #1's
   `StoreDatabase` file and schema, even though both are "a SQLite file on
   disk" — easy to conflate, worth flagging as a source of future confusion
   (see Findings, Medium).

`R3.Desktop` talks to `R3.Server` over plain `HttpClient` for a small slice
(`R3ApiClient.cs`, health check in `MainWindow.xaml.cs:97`) but the
day-to-day UI (Accounts, Cash, Sales) never goes through the Server or EF
Core — it calls `Local*Service` classes directly against `StoreDatabase`.

**Consequence for any future engine work**: a plan built on "EF Core is the
write engine, add Dapper for reads" does not describe this codebase. The
real system is "hand-written SQLite for business writes, a much smaller
EF/PostgreSQL model for organization data, no read/write split to harmonize
because there's no ORM on the business side to begin with." Any hybrid
persistence decision has to be made from that starting point, not assumed.

## What already exists (don't rebuild these)

- **DI**: none in Desktop by design (see `docs/ARCHITECTURE.md`); `R3.Server`
  uses standard ASP.NET Core `builder.Services`.
- **Logging**: Serilog, structured, in both apps (`DesktopLogging.cs`,
  `Program.cs`) — see `docs/LOGGING.md`. No Seq/OpenTelemetry sink.
- **Global exception handling**: `src/R3.Server/GlobalExceptionHandling.cs`
  (Server), `App.xaml.cs` dispatcher-level handling (Desktop).
- **Validation split**: FluentValidation already used in `R3.Application`
  (`CanonicalValidators.cs`) for Server API input shape; `Local*Service`
  classes enforce domain invariants themselves (e.g. "posted invoice can't
  be re-posted" throws `InvalidOperationException` from
  `LocalSalesService.Post`) — the separation this kind of initiative usually
  asks for already exists, just not documented as a named pattern.
- **Serialization**: `System.Text.Json` only (grid layouts, DPAPI-protected
  login credentials). No `Newtonsoft.Json` anywhere.
- **Ledger + projection pattern**: consistently repeated by hand across
  inventory (`inventory_transactions`/`inventory_balances`), accounts
  (`account_transactions`/`account_balances`), cash
  (`cash_transactions`/`cash_balances`), and now electronic documents
  (`electronic_documents`/`electronic_document_events`) — this *is* the
  project's existing "posting engine" convention; formalizing it as a name
  is more valuable than replacing it.
- **Async**: consistently used in newer code
  (`LocalInventoryService`, `ElectronicDocumentOutboxProcessor`); older
  `LocalSalesService.Post`/`LocalAccountService` methods are synchronous —
  not a defect (SQLite calls are effectively synchronous I/O either way),
  just an inconsistency worth a style decision, not a rewrite.

## What doesn't exist at all (greenfield, not refactor)

Dapper, an ORM read/write split, output caching (`IMemoryCache`/
FusionCache/Redis), a background-job runner (Quartz or otherwise — no
`IHostedService`/`BackgroundService` anywhere), Excel/PDF/CSV export,
barcode/QR generation, a printing abstraction, full-text search,
OpenTelemetry/metrics, BenchmarkDotNet, architecture-rule tests
(ArchUnitNET), Testcontainers, a transactional outbox for
Invoice/Stock/Account domain events (the one built for electronic documents
in `ElectronicDocumentOutboxProcessor` is scoped to e-documents only — see
its file header).

## Findings

**Medium**
- Three persistence paths sharing the word "SQLite" (see above) is a real
  confusion risk if new schema work assumes the wrong one.
- `MainWindow.xaml.cs:97` does `new HttpClient { Timeout = ... }` directly
  instead of going through `R3ApiClient`'s constructor-injected
  `HttpClient` — inconsistent with the pattern the codebase itself already
  established in `R3ApiClient.cs`. Low traffic (one health-check call), so
  socket-exhaustion risk is minimal today, but worth fixing for consistency
  if that code path is touched again.
- `xunit` 2.9.3 flagged "Legacy" by `dotnet list package --deprecated`
  (successor `xunit.v3`) — already tracked in `docs/DEPENDENCIES.md`'s
  "Known technical debt", not new.

**Low**
- Several test-only packages (`coverlet.collector`, `Microsoft.NET.Test.Sdk`,
  `xunit.runner.visualstudio`) have newer major versions available
  (`dotnet list package --outdated`); routine bump, no urgency.
- `CefSharp.Wpf.NETCore` 151.3.240 → 152.0.60 available.

**No Critical or High findings.** No data-integrity, correctness, or
security issue was found during this baseline pass. This matters because it
means any further "engine hardening" work here is preventive/performance
work on a currently-correct system, not a bug-fix effort — which is exactly
why it needs a measured reason before adding dependencies, not just a
"professional ERPs have X" argument (see `docs/DEPENDENCIES.md`'s Phase 1
policy section).

## Scale reality check

Single-file SQLite, single desktop process per user, no reported production
performance complaint, no load/concurrency test in place. Every candidate
library in this initiative's dependency list (`docs/DEPENDENCIES.md`) has
been evaluated against that reality, not against what a larger multi-tenant
SaaS ERP would need.
