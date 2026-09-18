# R3 ERP Architecture

This document is the source of truth for structural decisions. It complements
`R3_ARCHITECTURE.md` (layering/runtime overview) and `DATABASE_SCHEMA.md`
(persistence details) with decisions that need to stay visible over time.

## Layers

`R3.Domain` — business entities. `R3.Contracts` — transport DTOs/paging.
`R3.Application` — use cases and FluentValidation validators.
`R3.Infrastructure` — EF Core/Npgsql persistence, SQLite local store, logging
dependencies. `R3.Server` — ASP.NET Core host (Organization REST API,
`/health`). `R3.Desktop` — WPF client; talks to the server over HTTP only,
never opens a PostgreSQL connection.

Dependency direction: Desktop → Contracts; Server → Application/Infrastructure;
Infrastructure → Domain.

## Canonical domain model

**`src/R3.Domain/CanonicalEntities.cs` is the one and only canonical domain
model.** It defines `OrganizationCompany`, `Branch`, `Warehouse`, `Brand`,
`Category`, `Unit`, `Product`, `ProductVariant`, `ProductBarcode`, `Account`,
`AccountAddress`, and `AuditLog`, all deriving from `AuditableEntity`. This is
what `R3DbContext` maps to PostgreSQL, what `OrganizationService` operates on,
and what the Server API and Desktop ultimately read/write.

These entities are intentionally simple: public settable properties, no
constructor invariants. Validation for API input lives in `R3.Application`
(`CanonicalValidators.cs`, FluentValidation) and business rules that need
database lookups (duplicate codes, active/inactive company-branch-warehouse
consistency) live in `OrganizationService`. This is a deliberate choice, not
an oversight — see "Why not rich domain entities" below.

An earlier iteration of the project used a different, richer style
(`R3.Domain.Companies.Company` with private setters and constructor-enforced
invariants, backed by `R3.Domain.Common.Entity`). That model was never wired
into `R3DbContext`, `OrganizationService`, the Server API, or the Desktop —
it only existed as an orphaned class covered by its own unit test. It was
removed (see git history for the removal commit) once confirmed dead via a
solution-wide reference search. **Do not reintroduce a second `Company`
model.** If a domain invariant from that class is still relevant (e.g. the
Turkish tax number format), express it as a FluentValidation rule against the
canonical DTOs in `R3.Application`, the way `CreateCompanyValidator`/
`UpdateCompanyValidator` validate `TaxNumber` today.

### Why not rich domain entities

The canonical entities favor a simple, EF-Core-friendly shape (mutable
properties, DTO-like) over classic DDD rich entities (private setters,
constructor invariants, behavior methods) because:

- `R3DbContext` maps them directly via reflection-based `OnModelCreating`
  (snake_case conversion, no separate persistence model), which is simplest
  when the entity shape matches the table shape.
- Business rules that matter here are mostly cross-entity (duplicate codes
  scoped to a company, parent-active-before-child-active) rather than
  single-entity invariants, so they belong in `OrganizationService`
  (has database access) rather than in the entity constructor (does not).
- Input-shape validation (required fields, string length, format) is a
  different concern from persistence-shape invariants and is handled once,
  at the API boundary, via FluentValidation — see `docs/TESTING.md` and
  `CanonicalValidators.cs`.

If a genuine entity-level invariant emerges that cannot be expressed as
input validation (for example, an invariant that must hold regardless of
entry point — API, migration import, background job), add it to the
canonical entity itself rather than creating a parallel model.

## Desktop architecture direction

The current `R3.Desktop` project builds its UI programmatically in
code-behind (`MainWindow.xaml.cs`, `EditorDialogs.cs`). The target structure
introduced incrementally, module by module, is:

```text
R3.Desktop
├── Views          (XAML + minimal code-behind)
├── ViewModels      (CommunityToolkit.Mvvm-based, testable, no direct SQL)
├── Services        (thin wrappers over R3.Infrastructure local services / R3ApiClient)
├── Navigation
├── Dialogs
├── Themes
└── Resources
```

Business logic (SQL, validation, calculations) must not live in a `Window`
or a `ViewModel` directly opening a `SqliteConnection` — it stays in
`R3.Infrastructure`/`R3.Application`. See `docs/ROADMAP.md` for which module
has been migrated to this shape so far.

## Server availability contract

`R3.Server` can run without PostgreSQL configured (no `ConnectionStrings:R3`,
no `R3_POSTGRES_CONNECTION`). In that mode:

- `GET /health` returns `200` with `"database": "not_configured"`.
- `GET /api/v1/metadata` returns `200` (static info, no database needed).
- Every other `/api/v1/*` endpoint returns `503` with a `ProblemDetails` body
  (`Content-Type: application/problem+json`) carrying
  `"code": "database_not_configured"`, instead of leaking an internal DI
  resolution error as a generic `500`.

This is enforced by a pipeline gate in `Program.cs`, covered by
`tests/R3.Server.Tests/DatabaseAvailabilityTests.cs`.
