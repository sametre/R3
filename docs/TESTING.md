# Testing

```powershell
dotnet restore
dotnet build R3.slnx
dotnet test R3.slnx
```

As of Phase 3: **30/30 tests pass, 0 build errors, 0 warnings.**

## Test projects

| Project | Targets | What it covers |
| --- | --- | --- |
| `tests/R3.Domain.Tests` | `R3.Domain`, `R3.Application`, `R3.Infrastructure` (SQLite path) | Canonical validators, SQLite `StoreDatabase`/master data/product/inventory/sales posting engines. 16 tests. |
| `tests/R3.Server.Tests` | `R3.Server` (via `WebApplicationFactory<Program>`, no real Postgres) | `/health`, `/api/v1/metadata`, the `503 database_not_configured` gate, and (Phase 3) `GlobalExceptionHandling`. 9 tests. |
| `tests/R3.Desktop.Tests` | `R3.Desktop` (new in Phase 3) | Desktop logging bootstrap and the Cari Kartlar ViewModels' error handling. 5 tests. |

No Postgres integration tests exist yet (Testcontainers-based canonical
PostgreSQL testing is a separate, not-yet-started phase — do not add it as a
side effect of another phase).

## Conventions

- **Real persistence over mocks.** Every SQLite-backed test uses a real,
  temporary on-disk database file (`Path.GetTempPath()`), never an in-memory
  fake or a mocked repository interface. To force a realistic failure (e.g.
  to test error-logging paths), tests delete the database file/directory out
  from under an already-constructed service rather than injecting a fake
  exception — see `tests/R3.Desktop.Tests/AccountLoggingTests.cs`.
- **No mocking framework.** `LocalAccountService`, `StoreDatabase`, etc. are
  concrete classes, used as-is. A small hand-written `CapturingLogger<T> :
  ILogger<T>` test double (duplicated once in `R3.Server.Tests` and once in
  `R3.Desktop.Tests` — deliberately not shared via a new project, to avoid a
  test-only dependency between two otherwise-unrelated test assemblies)
  records `(LogLevel, Message, Exception?)` calls for assertions; nothing
  heavier is needed.
- **Server exception-handling tests don't need a live throwing endpoint.**
  `GlobalExceptionHandling.HandleAsync` is a standalone `(HttpContext,
  Exception, ILogger) -> Task` method precisely so it can be called directly
  from a test with a `DefaultHttpContext` and a thrown exception, instead of
  needing a production-only "test endpoint" that intentionally throws (which
  this project avoids leaving in shipped code).
- **Desktop tests never touch the real log directory.**
  `DesktopLogging.BuildLoggerConfiguration(string logDirectory)` is a pure
  function; tests always pass a temp directory, never
  `DesktopLogging.DefaultLogDirectory` (`%LOCALAPPDATA%\R3\logs`).
- **Test count only grows.** No existing test is deleted or skipped without
  the person asking explicitly.

## Manual / UI smoke

`tools/R3.UiSmoke` is a small WPF host that renders real screens off-screen
(`Measure`/`Arrange`/`UpdateLayout`, no visible window needed) to catch
runtime-only failures that a successful `dotnet build` cannot — for example,
a `DataGridCheckBoxColumn`'s default `TwoWay` binding throwing against a
read-only property only surfaces when the grid actually renders, not at
compile time (this exact bug was caught and fixed this way during Phase 2).

```powershell
# Headless-safe: constructs AccountsView + round-trips a real SQLite save,
# skips the interactive login window. Exits with output, no window shown.
$env:R3_UISMOKE_ACCOUNTS_ONLY = "1"
dotnet run --project tools/R3.UiSmoke

# Full smoke, including MainWindow: requires an interactive desktop session -
# StartupLoginWindow.ShowDialog() blocks until "Giriş yap" is clicked. Not
# runnable in a headless/CI environment; this is a pre-existing constraint,
# not something introduced by any phase so far.
dotnet run --project tools/R3.UiSmoke
```

## Server manual smoke

```powershell
dotnet run --project src/R3.Server
curl http://localhost:5189/health
```

Confirms `/health` responds and `logs/r3-server-<date>.log` is created with a
structured request-logging line (method, path, status, elapsed — no body, no
headers). See `docs/LOGGING.md`.
