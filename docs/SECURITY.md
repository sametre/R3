# Security Notes

## Secrets in source control

`.gitignore` excludes `appsettings.Development.json` (only the untracked
`.example.json` template is committed), `logs/`, and local SQLite
runtime/backup files (`*.db`, `Backups/`). `src/R3.Infrastructure/R3DbContextFactory.cs`
contains a fixed `Password=design_time_only` connection string — this is a
placeholder used only by `dotnet ef migrations add` at design time, never a
real credential, and never connects to anything outside a developer's own
local Postgres.

Before every commit in this project, a scan is run for `password`,
`Password=`, `token`, `Authorization`, `connectionstring`/`connection string`,
`TCKN`, `CVV`, `CardNumber` across `src/`. As of Phase 3, no real secret was
found; the only matches are the two intentional/harmless cases above plus
prose text (e.g. the `503` response's `Detail` field, which *names* the
`R3_POSTGRES_CONNECTION` environment variable but never contains its value).

## Known technical debt: hardcoded Desktop seed credential

`src/R3.Infrastructure/StoreDatabase.cs` (`EnsureDefaultUser`) seeds a fixed
local account — username `admin`, password `R3Admin2026!` — the first time the
SQLite database is created, so a fresh install has something to log in with.
The password is not stored in plain text (PBKDF2, 100k iterations, SHA-256,
random salt, constant-time comparison in `VerifyPassword`), but the **default**
value is fixed and printed as a hint in `StartupLoginWindow`'s UI.

This is acceptable for the current state of the project — a single-user,
offline, local prototype with no network exposure — but must not ship as-is
to a multi-user or network-reachable deployment. Before that happens, replace
it with one of:

- a first-run setup screen that requires the user to choose their own admin
  password before the app is usable, or
- an environment/configuration-supplied initial credential (never a fixed
  literal in source), or
- deferring authentication entirely to `R3.Server` once Desktop talks to it
  for account-sensitive operations, rather than SQLite's own local user table.

Tracked here, not silently fixed as a drive-by change, because changing
authentication behavior is out of scope for a logging/error-handling phase
and needs its own decision.

## Logging and sensitive data

See `docs/LOGGING.md` for the full policy. Summary: passwords, hashes, salts,
tokens, connection string credentials, and full PII (address, phone, TCKN/VKN)
are never written to a log message; IDs/codes are used instead when a
business event needs logging context.

## Dependency vulnerability scanning

`dotnet list package --vulnerable --include-transitive` is run after every
dependency change (see `docs/DEPENDENCIES.md` for the full package list and
license audit). As of Phase 3: no known vulnerable package, transitive or
direct, in any project. Phase 1 previously found and fixed one High-severity
transitive advisory (`System.Security.Cryptography.Xml`, pulled in by an
outdated `Microsoft.EntityFrameworkCore.Design`) by bumping that package to a
patched version.

## Server error responses

`GlobalExceptionHandling` (see `docs/LOGGING.md`) guarantees an unhandled
exception's message, type name, stack trace, and any internal detail (SQL,
file paths, connection info) never reach the HTTP response body — only a
generic Turkish message, a stable error code, and `TraceId`. The full
technical detail goes to the log file instead, where an operator (not an
external client) can see it.
