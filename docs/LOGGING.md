# Logging & Global Error Handling

Serilog is the implementation. Application code (ViewModels, services, Server
endpoints) never calls `Serilog.Log` directly — it depends on
`Microsoft.Extensions.Logging.ILogger<T>`, resolved manually (Desktop has no
DI container, see `docs/ARCHITECTURE.md`) or from ASP.NET Core's DI (Server).
`Serilog.Log` (the static instance) is only touched at bootstrap/shutdown.

## Log locations

| App | Path | File pattern |
| --- | --- | --- |
| Desktop | `%LOCALAPPDATA%\R3\logs\` | `r3-desktop-<date>.log` |
| Server | `logs/` (relative to the server's working directory) | `r3-server-<date>.log` |

Desktop and Server never write to the same file. Both roll daily
(`RollingInterval.Day`), keep at most 30 files (`RetainedFileCountLimit`), and
additionally roll early if a single day's file exceeds 50 MB
(`FileSizeLimitBytes` + `RollOnFileSizeLimit`), so logs cannot grow without
bound. Configuration lives in one place per app —
`R3.Desktop/Logging/DesktopLogging.cs` and `R3.Server/Program.cs` — not
scattered per class.

Format is human-readable structured text:

```text
2026-09-18 18:32:04.281 +03:00 [ERR] Account creation failed. CompanyId=... {"ErrorId":"...","SourceContext":"..."}
```

No external sink (Seq, Elasticsearch, OpenTelemetry) is configured. Local
rolling files are the whole story for now.

## Levels

- **Trace/Debug** — developer diagnostics, off by default (`MinimumLevel.Information`).
- **Information** — lifecycle events: app started/stopped, request completed. Not
  every successful query — `LocalAccountService.Search`/`OrganizationService`
  query methods are not logged per call.
- **Warning** — expected-but-worth-noting: `OrganizationRuleException` (business
  rule rejections like duplicate codes), the `503 database_not_configured`
  response.
- **Error** — an operation failed but the process keeps running: account
  save/load failures, unhandled Server exceptions, unobserved Task exceptions.
- **Critical** — Desktop only: unhandled UI-thread or AppDomain exceptions,
  where the app can no longer guarantee a safe state.

## Sensitive data

Never logged: passwords/hashes, connection string credentials, tokens,
Authorization headers, card data. Prefer IDs/codes over free-text PII (full
address, phone, TCKN/VKN) when a business event needs logging — e.g.
`AccountId=42 AccountCode=CARI-00125`, not the customer's address or phone
number. `AccountsViewModel.RefreshAsync` deliberately does not log the raw
search text a user typed, since it could contain a phone number or name.

Request logging (`UseSerilogRequestLogging` on the Server) only records
method, path, status code and elapsed time — request/response bodies, cookies
and the `Authorization` header are never included, matching Serilog's
default behavior for this middleware.

**Known technical debt** (tracked in `docs/SECURITY.md`): the Desktop SQLite
seed creates a fixed default `admin`/`R3Admin2026!` account for local,
single-user use. It is not a logging concern, but it's exactly the kind of
credential this policy says must never appear in a log message — verified: it
does not.

## Desktop global exception handling

`App.xaml.cs` wires three channels, each logged before any user-facing action:

| Channel | Meaning | Response |
| --- | --- | --- |
| `DispatcherUnhandledException` | Unhandled exception on the UI thread | Log `Critical` with a fresh `ErrorId` (Guid), show a friendly Turkish message containing that ErrorId (no stack trace), then `Shutdown(-1)` — a controlled shutdown, not `e.Handled = true` and pretending nothing happened. |
| `AppDomain.CurrentDomain.UnhandledException` | Unhandled exception off the UI thread | Log `Critical`, flush, let the runtime terminate (it's already terminating regardless). |
| `TaskScheduler.UnobservedTaskException` | A background `Task` faulted and nothing observed it | Log `Error` (not `Critical` — doesn't corrupt UI state by itself), mark observed. |

The `ErrorId` shown to the user is a plain `Guid.NewGuid()`, logged as a
structured `ErrorId` property alongside the exception. Support can grep the
log files for it — no distributed tracing infrastructure involved.

## Server global exception handling

`GlobalExceptionHandling.HandleAsync` (`src/R3.Server/GlobalExceptionHandling.cs`)
is the single place an unhandled exception becomes an HTTP response, wired via
`app.UseExceptionHandler(...)`. It is a plain static method
(`HttpContext, Exception, ILogger` in) so it's unit-tested directly
(`tests/R3.Server.Tests/GlobalExceptionHandlingTests.cs`) without needing a
live HTTP pipeline:

- `OrganizationRuleException` → `409 Conflict`, `ApiError` with the rule's own
  message, logged at **Warning** (expected business outcome, not a bug).
- Anything else → `500`, generic `ApiError("server.error", "Beklenmeyen bir
  sunucu hatası oluştu.", ...)`, logged at **Error** with the real exception
  and a fresh `ErrorId`. The response body never contains the exception
  message, type name, stack trace, or any internal path/SQL/connection
  detail — only `TraceId` (ASP.NET Core's own `HttpContext.TraceIdentifier`).

This is unchanged in shape from before Phase 3 (still `ApiError`, not
`ProblemDetails`, for 409/500) — Phase 3 added logging around the existing
contract, it did not redesign it. The `503 database_not_configured` response
(a separate middleware, see `docs/ARCHITECTURE.md`) is untouched; that
behavior is Phase 5's, not Phase 3's.

## Audit log vs. diagnostic log

These are two different things and this phase does not merge them:

- **Audit log** (`audit_logs` table, `LocalAccountService`/`LocalProductService`/
  `OrganizationService` writes) — business record: who did what, to which
  document, when. Persisted, queried by the app itself, part of the domain.
- **Diagnostic log** (Serilog files described above) — developer/operator
  concern: exceptions, request timing, startup/shutdown. Not queried by the
  app; read by a human or a log tool when something goes wrong.

## Exception boundaries ("log once")

- **Domain** (`R3.Domain`) — no logger dependency; raises exceptions.
- **Application** (`R3.Application`, FluentValidation) — no new logging in this
  phase; validation failures are just `400`s, not something to log.
- **Infrastructure** (`LocalAccountService`, `OrganizationService`) —
  deliberately **not** given a logger in this phase. The immediate caller
  (Desktop ViewModel, Server exception handler) already has to catch the
  exception to decide the user-facing outcome, so it is the one responsible
  boundary that logs it. Adding a second log call in Infrastructure would
  duplicate every entry.
- **Desktop** — `AccountsViewModel`/`AccountEditViewModel` (see below) log
  once, at the point they turn an exception into a `StatusMessage`/`ErrorMessage`.
- **Server** — `GlobalExceptionHandling.HandleAsync` logs once, at the HTTP
  boundary.

## Reference implementation: Cari Kartlar

`AccountsViewModel` and `AccountEditViewModel` (`src/R3.Desktop/ViewModels/`)
are the pattern to copy for any other screen:

- Known, already-friendly validation errors (`ArgumentException` from
  `LocalAccountService.Save`, e.g. "Cari kodu zorunludur.") are shown as-is,
  **not** logged as Error — they're expected input rejection.
- `SqliteException` is translated: error code 19 (unique constraint) becomes
  "Bu cari kodu zaten kullanılıyor.", anything else becomes a generic Turkish
  message — and both are logged at Error with the real `SqliteErrorCode` and
  exception attached.
- Any other unexpected exception gets a generic Turkish message and an Error
  log with the full exception.
- A normal successful list refresh is not logged at all — see "Levels" above.

## Performance

Do not log once per row/item in a loop. A future bulk operation (e.g. product
import) should log one summary line on completion:

```text
Product import completed. Imported=9824 Skipped=123 Failed=53 DurationMs=4213
```

not one line per imported row.
