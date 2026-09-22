# ASB Legacy Database Analysis — Status

## What this covers

A follow-on to `ASB_ERKUR_2019_SCHEMA_ANALYSIS.md` / `ASB_MIGRATION_MAPPING.md`: an attempt to produce the
full 816-table data dictionary (all columns, PKs, FKs, indexes, views, procedures, triggers, row counts,
module classification, R3 mapping) for the legacy ASBDB_ERKUR 2019 backups. Recorded here so the next
session doesn't have to rediscover the constraints below from scratch.

## Verified this session

- Re-ran the existing `BAK-SHELL` tool (a custom .NET 8 MTF/MDF parser that reads SQL Server `.BAK` files
  directly into DuckDB, no SQL Server install required) against `ASBDB_ERKUR011710.BAK`.
- Independently reconfirmed: database `ASBDB_ERKUR01DB2008`, **883 total schema objects, 816 user tables**.
  This matches the count already recorded in `ASB_ERKUR_2019_SCHEMA_ANALYSIS.md` — it is not a hardcoded
  or assumed number, it was read from the backup's system catalog (`SysSchObj`) twice.

## Blocker: source files are gone

Both `.bak` files (`ASBDB_ERKUR011710.BAK`, `ASBDB_ERKUR021710.BAK`) and the `BAK-SHELL` tool were sitting
in `%TEMP%\r3-asb-analysis\` from a prior session. Mid-way through this session that entire directory
disappeared (confirmed via `Test-Path` going from present to `False` between two consecutive commands) —
almost certainly a scheduled Temp-cleanup or another session's scratch-directory teardown, since `%TEMP%`
is volatile by design. Extraction work stopped at that point rather than continuing on files that could
vanish again mid-run.

**To resume**: put `ASBDB_ERKUR011710.BAK`, `ASBDB_ERKUR021710.BAK`, and the `BAK-SHELL` tool (or its
source, so it can be rebuilt with `dotnet build -c Release`) somewhere persistent — outside `%TEMP%`,
e.g. a local folder or a network share — and give the path.

## Known capability gap (independent of the file-location issue)

`BAK-SHELL` only parses `SysSchObj` / `SysColPar` / `SysScalarType` / `SysRowSet` from the backup's system
catalog. That's enough for:
- Table and column inventory (names, types, nullability)
- Row data → row counts, data profiling, inferred logical relationships (via naming/value overlap)

It does **not** parse `sys.foreign_keys`, `sys.indexes`, or `sys.sql_modules`, so it cannot currently
extract:
- Physical foreign keys, primary keys, indexes, check constraints
- Stored procedure / view / trigger definitions

Getting those requires either extending `BAK-SHELL` to parse those additional system-table pages (a real
binary-format reverse-engineering task, not a quick add), or restoring for real with SQL Server Express /
SQL Server in Docker — neither of which is installed in this dev environment currently (`sqlcmd`,
`sqlservr`, and `docker` were all absent when checked).

## Recommended next step

Once the files are back at a stable path: re-run `BAK-SHELL` in batch mode (`-o <file>.duckdb`) for both
databases to get full column dictionary + row counts for all 816 tables, export to CSV/JSON per the
original spec's Part I/II, and generate the per-table/per-module markdown catalog from that data
programmatically (per the spec's own Part XIV — don't hand-write 816 tables). Explicitly mark FK/index/
procedure/view/trigger sections as "NOT AVAILABLE — requires SQL Server restore" rather than guessing them,
consistent with the spec's own golden rule against inventing schema.
