# R3 Posting Engine (Phase 5)

## What existed before this phase

`LocalSalesService.Post()` already owned a single SQLite connection and
transaction for Invoice + Inventory ledger + Account ledger + Audit -
`SalesService opens its own transaction, AccountService opens another,
InventoryService opens a third` (the "wrong model" this phase's brief
warns against) was **not** what the codebase actually did. `Post()`
inlines the raw SQL for `inventory_transactions`/`inventory_balances` and
`account_transactions`/`account_balances` directly against its own `c`/`tx`
- it never calls `LocalInventoryService`/`LocalAccountService` as separate
services with their own connections. One `Open()` call, one
`BeginTransaction()`, one `Commit()`, for the entire method regardless of
line count.

What this phase adds: the electronic document. Before Phase 5,
`ElectronicDocument` creation had no caller at all from the sales flow.

## Transaction boundary (final)

```
BEGIN  (LocalSalesService.Post owns this - single connection, single transaction)

  ReadDoc + idempotency check (status must be Draft)
  Validate account (exists, active, Customer/CustomerAndSupplier)
  Validate + reserve each line (product active, stock sufficient)
  AllocateNumber (document_no)
  UPDATE sales_documents SET status='Posted', document_no=...
  INSERT account_transactions (Debit = grand total)
  INSERT/UPDATE account_balances
  ElectronicDocumentRoutingService.RouteOutgoingInvoice(...)          <- read-only, no write
  LocalElectronicDocumentService.CreateOrGetForSourceWithinTransaction (Draft)
  LocalElectronicDocumentService.ReadyWithinTransaction               (Draft -> Ready)
  INSERT audit_logs ('SalesInvoicePosted', includes electronicDocumentId)

COMMIT
```

Everything above runs on the transaction's `SqliteConnection`/`SqliteTransaction`
that `Post()` opened - no nested `BeginTransaction()`, no second connection.
`ElectronicDocumentRoutingService.RouteOutgoingInvoice` is a plain read
(`SELECT` against `account_einvoice_profiles`) so it doesn't need to
participate in the write transaction at all; it's called on the same
connection anyway since the read is trivial.

**Deliberately excluded from this transaction, and from this phase**:
no outbox row is created. Queuing an outbox entry requires the electronic
document to reach `Generated`, which requires a real UBL payload to already
exist (`Generate()` enforces this - see the Phase-2-review-gate fix in
`ElectronicDocumentModels.cs`/`LocalElectronicDocumentService.cs`). No UBL
generator exists yet. Forcing the document to `Generated` with a fake
payload just to satisfy "create an outbox row here" would misrepresent a
document as ready to send when it isn't - so posting stops at `Ready`.
The next phase that adds UBL generation is what will carry a `Ready`
document to `Generated` → `Queued` (+ outbox row), most likely as its own
short transaction, not inside `Post()`.

## Why no `PostingContext`/`IPostingService` abstraction

The brief allowed introducing a `PostingContext`/`LocalPostingService` "if
the current architecture isn't suitable." It was suitable: `Post()` already
was the single transaction owner for its document type, matching every
other `Local*Service.Post*`/`Save*` method in this codebase (they all
inline their own connection/transaction rather than composing injected
services with their own transactions). Adding a generic `PostingContext`
wrapper here would be new ceremony around something that already works,
for a codebase that has exactly one document type doing atomic posting so
far. Instead, `LocalElectronicDocumentService` grew two `internal
*WithinTransaction` methods (`CreateOrGetForSourceWithinTransaction`,
`ReadyWithinTransaction`) that accept an existing `SqliteConnection`/
`SqliteTransaction` - the same pattern `ElectronicDocumentOutboxService.
QueueForSendAsync` already established in Phase 3 for
`LocalElectronicDocumentService.QueueWithinTransaction`. When a second
document type needs the same treatment (Purchase invoice, a future
`SalesReturn`), promoting this into a small shared helper becomes an easy,
well-motivated refactor - not a decision to make speculatively now.

## Service responsibilities

- `LocalSalesService.Post(documentId, userId)` - the one transaction owner.
  Returns `SalesPostResult(InvoiceId, DocumentNo, ElectronicDocumentId,
  ElectronicDocumentType)` so callers (UI, tests) don't have to re-query
  for IDs the posting flow already produced.
- `ElectronicDocumentRoutingService.RouteOutgoingInvoice` - read-only
  decision, no side effects, safe to call from inside or outside a
  transaction.
- `LocalElectronicDocumentService.CreateOrGetForSourceWithinTransaction` /
  `ReadyWithinTransaction` - the composable seams; the public
  `CreateOrGetForSource`/`Ready` (used everywhere else, e.g. incoming
  document registration) still open and commit their own transaction as
  before, unchanged.

## Idempotency

Unchanged from before this phase, and it already worked: `Post()` reads
`status` inside the transaction and throws if it isn't `Draft`. Because
SQLite serializes writers to one file, a second concurrent `Post()` call on
the same row blocks on `BeginTransaction`/the first write until the first
transaction commits, then sees `status='Posted'` and throws - proven, not
assumed, by `ConcurrentDuplicatePostingOnlyOneAttemptSucceeds` in
`PostingEngineTests.cs`, which runs two real concurrent `Post()` calls
against the same document and asserts exactly one succeeds and exactly one
ledger/e-document row set exists afterward.

`CreateOrGetForSourceWithinTransaction`'s own idempotency (same
`CompanyId`+`DocumentType`+`SourceEntityType`+`SourceEntityId` returns the
existing row) means calling it twice for the same invoice - which can't
happen through `Post()` today because of the status guard above, but will
matter once a reversal/reissue flow exists - won't create a duplicate
electronic document either.

## Rollback behavior

Every step from `ReadDoc` through the audit insert runs on the same
`using var tx`; any exception before `tx.Commit()` at the very end leaves
the `using` block to dispose the transaction without committing, which
SQLite treats as a rollback. This was already true before this phase for
Invoice/Account/Inventory (see the pre-existing
`SalesPostRollsBackAllLinesWhenSecondLineInsufficient` test); Phase 5
extends the same guarantee to the electronic document by construction,
since `CreateOrGetForSourceWithinTransaction`/`ReadyWithinTransaction` run
on that identical `c`/`tx` rather than opening their own. Verified directly
by `InsufficientStockRollsBackInvoiceAccountInventoryAndElectronicDocumentTogether`
(asserts no `electronic_documents` row exists after a failed post) and, at
the unit level, by
`WithinTransactionElectronicDocumentFailureRollsBackEverythingElseUncommittedInThatTransaction`
(forces the one realistic failure `CreateOrGetForSourceWithinTransaction`
can raise - a UUID collision - mid-transaction and confirms a rollback
leaves nothing, including an otherwise-valid sibling write in that same
transaction).

## Provider/external calls stay outside every transaction

Nothing changed here from Phase 3 - `IElectronicDocumentProvider` is never
called from inside `Post()` or from `LocalElectronicDocumentService`, only
from `ElectronicDocumentDispatcher`, which runs after commits, outside any
business transaction. This phase doesn't add a call path that would
violate that; it just doesn't yet add the piece (UBL generation + outbox
queuing) that would eventually hand a document to the dispatcher at all.

## Source of truth

Unchanged, made explicit: `sales_documents`/`sales_document_lines` are the
source of truth for the invoice; `electronic_documents` is only its GİB/UBL
representation (this was already the Phase 2 invariant - see
`docs/architecture/R3-ENGINE-ARCHITECTURE.md`). Posting doesn't change that
relationship, it just makes sure the representation always exists once the
source is posted.

## Reversal (not built yet, architecture left open for it)

No `SalesReturn`/reversal flow exists. Nothing in this phase forecloses one:
a reversal would follow the exact same shape (one `Local*Service` method
owning one transaction, calling the same `*WithinTransaction` seams to
either cancel the existing electronic document or create a new one for the
reversal/return document type) rather than needing new infrastructure.

## Performance (measured, not assumed)

`Post()` opens exactly one `SqliteConnection`/`SqliteTransaction` for the
whole method - confirmed by reading the code (a single `Open()` call at the
top; the per-line loop reuses `c`/`tx`, no connection-per-line). A 20-line
invoice (20 products, 20 `inventory_transactions` inserts + balance
updates, one account transaction, one electronic document, one audit row)
posted in **86 ms** end-to-end on this machine - no regression from
`synchronous=NORMAL`/centralized connections (Phase 2).

## UI

No UI changes in this phase. Notably: **the Sales Invoice UI has no way to
call `Post()` at all today** - `SalesInvoiceDialog`
(`src/R3.Desktop/EditorDialogs.cs`) only creates/saves a Draft
(`"Taslak oluşturuldu"` in `MainWindow.xaml.cs`); there is no "Faturayı
Kes"/"Post Et" button anywhere. The Draft/Post separation the brief asked
about already exists at the service layer (`SaveDraft` vs. `Post` are
already two distinct methods with distinct semantics) - what's missing is
exposing the second one in the UI at all. That's a UI-layer follow-up, not
part of this transaction-boundary phase.

## Tests

`tests/R3.Domain.Tests/PostingEngineTests.cs` (6 tests, all new this
phase) + the two pre-existing `LocalSalesService.Post` tests in
`StoreDatabaseTests.cs` (updated to the new `Post(id, userId)` signature
and extended to also assert no `electronic_documents` row survives a
rollback).
