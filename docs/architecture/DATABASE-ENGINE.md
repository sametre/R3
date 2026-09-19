# R3 Database Engine — SQLite Performance (Phase 2)

Everything below was measured against the actual `StoreDatabase` schema and
the actual query shapes `Local*Service` classes issue — not assumed. See
`docs/architecture/R3-ENGINE-ARCHITECTURE.md` for the broader persistence
picture (SQLite is the real business-write engine; EF Core/PostgreSQL only
covers organization/catalog data).

## What changed

### 1. `synchronous=NORMAL` under WAL (measured ~6.7x faster per commit)

`journal_mode=WAL` was already set (persists in the database file itself,
so every connection benefits automatically). `synchronous` was never set
explicitly, so every connection ran at SQLite's default (`FULL`) instead.
`synchronous` is **not** persisted in the file — it must be reissued on
every connection, unlike `journal_mode`.

Benchmarked on this machine, 200 separately-committed transactions (the
shape most of R3's writes actually take — every `Local*Service` method
opens its own connection, transaction, and commits immediately):

| Setting | Time for 200 commits | Per commit |
| --- | --- | --- |
| `synchronous=FULL` (previous default) | 491 ms | 2.46 ms |
| `synchronous=NORMAL` | 73 ms | 0.37 ms |

`NORMAL` is WAL's documented pairing: still fully safe against database
*corruption*; the only risk is losing the last few committed-but-not-yet-
checkpointed transactions in a true OS/power crash, which is the standard,
widely-used tradeoff for this journal mode. This is not a correctness
regression for R3's single-desktop-process-per-file deployment.

Applied once, centrally, in `StoreDatabase.OpenConnection()` — see below.

### 2. Centralized connection opening

Before this phase, the connection string (`Data Source=...;Foreign
Keys=True;Default Timeout=5`) was copy-pasted across ~11 files and ~20 call
sites, with real drift: several sites (`LocalAccountService`,
`LocalCashService`, `LocalMasterDataService.SetActive`) omitted `Default
Timeout`, silently getting Microsoft.Data.Sqlite's 60-second default
instead of the 5 seconds used everywhere else. Since `synchronous` isn't
persisted per-file, applying it consistently required fixing this the same
way: one method, `StoreDatabase.OpenConnection()`, that every service now
calls instead of constructing its own `SqliteConnection`. Behavior is
otherwise unchanged - same file, same `Foreign Keys=True`, now a uniform
5-second busy timeout everywhere.

### 3. `busy_timeout` — measured, not assumed

`PRAGMA busy_timeout` reads back `0` even with `Default Timeout=5` set.
This looked like a bug; measuring it wasn't:

- Opened a transaction, took a write lock, did not commit.
- From a second connection, attempted a conflicting write.
- Result: **failed after 5093 ms** with `SQLite Error 5: 'database is
  locked'`.

So `Default Timeout` **is** honored — Microsoft.Data.Sqlite implements its
own busy-retry loop at the ADO.NET layer rather than setting the native
`sqlite3_busy_timeout()` PRAGMA, which is why the PRAGMA readback doesn't
reflect it. After the timeout, a genuinely contended write still fails
outright (no indefinite wait) - acceptable for R3's single-writer-per-file
desktop deployment; would need revisiting only if multiple processes ever
write the same file concurrently.

## Index audit (spec-named fields: product code, barcode, account code,
invoice/document number, transaction date, warehouse/account/product/
company/branch id, status)

Checked with `EXPLAIN QUERY PLAN` against a 5,000-row synthetic dataset,
not guessed:

| Lookup | Plan | Verdict |
| --- | --- | --- |
| `products` by `company_id`+`code` | `SEARCH ... USING INDEX sqlite_autoindex_products_2` | Already indexed - comes from the existing `UNIQUE(company_id, code)` constraint |
| `product_barcodes` by `barcode` | `SEARCH ... USING INDEX sqlite_autoindex_product_barcodes_2` | Already indexed - `UNIQUE` column |
| `accounts` by `company_id`+`code` | `SEARCH ... USING INDEX sqlite_autoindex_accounts_2` | Already indexed |
| `electronic_documents` by `uuid` | `SEARCH ... USING INDEX sqlite_autoindex_electronic_documents_2` | Already indexed |
| `inventory_transactions`, `account_transactions`, `cash_transactions` by warehouse/account/document/date | Use `IX_InventoryTransactions_*`, `IX_AccountTransactions_*`, `IX_CashTransactions_*` | Already indexed (added in earlier phases) |
| `electronic_document_outbox` claim query (`status`+`available_at`) | Uses `IX_ElectronicDocumentOutbox_Claim` | Already indexed (added in Phase 3) |
| `sales_documents.document_no`, `electronic_documents.document_number` | **No index used beyond `company_id`** | **Not fixed** - see below |

**`document_no`/`document_number` were not indexed, and adding an index for
them would not help**: the only real read usage
(`LocalSalesService.Search`, `LocalElectronicDocumentService.Search`) is a
two-sided `LIKE '%text%'` search, which a B-tree index cannot accelerate
regardless (needs FTS5 or a different structure - out of scope for this
phase per `docs/DEPENDENCIES.md`'s Lucene/FTS5 note, no measured need yet).
Adding an index here would have been dead weight with no query it actually
speeds up - exactly the "guess, don't measure" mistake this phase's
mandate warns against. No index change made for these columns.

**Duplicate/unused indexes**: none found. Every existing `CREATE INDEX`
has a distinct leading-column shape and a real query using it.

## What was deliberately not touched

- `cache_size` (SQLite default, -2000 = ~2MB): no measured page-cache-miss
  problem to justify tuning.
- `mmap_size` (0, disabled): no measured benefit demonstrated for R3's
  file sizes; revisit if a real large-database read-latency complaint
  appears.
- No new dependency was added for any of this - it's all `PRAGMA` and one
  centralized connection-opening method.
