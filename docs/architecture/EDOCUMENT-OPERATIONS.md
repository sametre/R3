# E-Belge Operasyon Merkezi (Phase 9)

Makes the entire existing electronic-document engine (`ElectronicDocument` + `Payload` + `Outbox` +
`Dispatcher` + `Provider` + event log - Phases 2/3/6/7) visible and operable from four screens. No
new business logic: every screen here reads through `ElectronicDocumentOperationsService` (a
read-only projection layer) and every mutating action calls the exact same canonical services
`InvoiceDetailViewModel` already called in Phase 8 (`ElectronicDocumentGenerationService`,
`ElectronicDocumentOutboxService`, `ElectronicDocumentDispatcher`). Nothing here ever runs
`UPDATE electronic_documents SET status=...` or calls `IElectronicDocumentProvider` directly.

## Dashboard (`ElectronicDocumentDashboardView`/`ViewModel`)

KPI cards (Bugün Oluşturulan / UBL Hazır / Kuyrukta / Gönderilen / Kabul Edilen / Reddedilen / Retry
Bekleyen / Dead Letter) and a document-type distribution (E-Fatura / E-Arşiv / E-İrsaliye) both come
from one call each: `ElectronicDocumentOperationsService.GetDashboardSummary` and `.GetTypeDistribution`
- two aggregate SQL queries, never a client-side count of loaded rows. `RetryPending` means an
outbox `Send` row that is `Pending` with `attempt_count>0` (a genuine reschedule, not a first
attempt); `DeadLetter` counts outbox rows in that terminal state. E-İrsaliye legitimately reads 0
until a real e-İrsaliye flow exists - `GetTypeDistribution` zero-fills every `ElectronicDocumentType`
rather than omitting types with no rows, so "0" is a real answer, not a missing one. The "Son Hatalı
Belgeler" panel is `GetRecentErrors` (the same query the Error Center uses), capped to 15 rows.

## Outgoing documents (`OutgoingElectronicDocumentsView`/`OutgoingElectronicDocumentsViewModel`)

`ElectronicDocumentOperationsService.SearchOutgoing`: every filter (date range, document type,
status, free-text search across document number/UUID/account name/VKN-TCKN) becomes a SQL `WHERE`
clause, capped at `ElectronicDocumentFilter.Limit` (default 100) - never an in-memory re-filter of an
already-loaded table. No "Profil" column: the UBL scenario (Temel/Ticari/İhracat/Kamu) is a
company-wide setting read at generation time, never stored per document, so a later profile change
could make a stored per-row value wrong; omitted rather than risk showing a stale answer.

## Outbox (`ElectronicDocumentOutboxView`/`ElectronicDocumentOutboxViewModel`)

`SearchOutbox` joins the outbox row with its document's own status/provider (one query, not one
document lookup per row - spec §35). "Şimdi Çalıştır" and per-row "Tekrar Dene" both call
`ElectronicDocumentDispatcher.DispatchAsync`/`ElectronicDocumentOutboxService.ManualRetry` - the same
public API `InvoiceDetailViewModel.SendCommand`/`RetryCommand` already use. `ProcessOneAsync` is
private by design (see `docs/architecture/OUTBOX-DISPATCH.md`), so "Şimdi Çalıştır" is always a full
due-batch dispatch pass, not scoped to one row - this is disclosed, not hidden, in the UI copy.

The row detail drawer (`ElectronicDocumentDialogs.ShowOutboxDetail`) is read-only end to end: there
is no control anywhere in that dialog bound to `IdempotencyKey` except a copy button - no code path
can edit it.

## Error Center (`FailedElectronicDocumentsView`/`FailedElectronicDocumentsViewModel`)

`SearchErrors` unions Failed, Rejected, DeadLetter and retry-pending documents in one query. Its KPI
cards reuse `GetDashboardSummary` (the untruncated aggregate) rather than counting the grid's
(LIMIT-capped) rows, so they stay correct past the 100-row page size. A dead-lettered document's own
`electronic_documents.status` stays `Failed` forever (DeadLetter is an outbox-row terminal state, not
a document status) until `ManualRetry` moves it back to `Queued` - both screens show this
consistently rather than inventing a fourth pseudo-status.

## Retry operations

Every "Tekrar Dene" in the app (Invoice detail, Giden Belgeler context menu, Gönderim Kuyruğu context
menu, Error Center) calls `ElectronicDocumentOutboxService.ManualRetry(electronicDocumentId, userId)`
then one `ElectronicDocumentDispatcher.DispatchAsync` pass - never a second implementation. `ManualRetry`
opens a brand-new outbox row with the *same* deterministic `IdempotencyKey` (see
`docs/architecture/OUTBOX-DISPATCH.md`); the old (DeadLetter) row's attempt history is left untouched,
not overwritten.

## Reusable dialogs (`ElectronicDocumentDialogs`)

`ShowXmlViewer`, `ShowProviderResponse`, `ShowEventTimeline` and `BuildEventTimeline` are the single
implementations `InvoiceDetailView` (Phase 8), `OutgoingElectronicDocumentsView`,
`FailedElectronicDocumentsView` and `ElectronicDocumentGenericDetailView` all call - no screen has its
own copy. `ShowXmlViewer` lists every UBL/SignedXml payload version when more than one exists, with
the sendable one (SignedXml if present, else the latest UblXml) marked and selected by default.
`ShowProviderResponse`'s structured fields (Provider/Provider Belge No/Envelope ID) come from the
`ElectronicDocumentRow` itself, never parsed out of the raw response blob - the raw
`ProviderResponse` payload, if one was ever saved, is shown underneath, read-only.

## Generic document detail (`ElectronicDocumentGenericDetailView`)

A document whose source is not (yet) a `SalesInvoice` - e.g. a future `EDespatch` - gets a generic
Belge No/Belge Tipi/Kaynak Belge/Cari/UUID/Status screen with the same shared dialogs, instead of the
invoice-shaped `InvoiceDetailView` layout being forced onto it. `MainWindow.OpenElectronicDocumentById`
picks between the two based on `SourceEntityType` - the invoice screen for `SalesInvoice`, the generic
one for anything else - so a future source type is never a dead click.

## Permissions

`edocuments.view/generate/send/payload.view/status.query/retry/outbox.view/errors.view/
provider_response.view/audit.view` plus `sales.invoice.post` are now all in `PermissionCatalog.All`
(`src/R3.Application/Security/IPermissionService.cs`) - most already existed from Phase 6/8; this
phase adds `sales.invoice.post`, `edocuments.send`, `edocuments.outbox.view`, `edocuments.errors.view`
and `edocuments.provider_response.view`. Every context-menu action in
`ElectronicDocumentContextActions` carries a `PermissionCode` evaluated by the existing
`ContextActionEvaluator` (`ErpContextActionFramework.cs`, unchanged) - denying a permission hides the
action (`HideWhenUnauthorized: true`), it does not just disable it.

`InvoiceDetailViewModel`'s own commands (`PostCommand`/`GenerateCommand`/`QueueCommand`/`SendCommand`/
`QueryStatusCommand`/`RetryCommand`) now also check `IPermissionService.HasPermission` inside their
`CanExecute` predicate **and** inside the command body itself (`RequirePermission`, throwing if
denied) - so a direct `ExecuteAsync` call that bypasses the UI's `CanExecute` gate (spec §30 "UI
permission gizlese bile ... korunmalı") is still refused, not merely hidden.

**Disclosed limitation**: R3 has no service-layer/backend permission interceptor anywhere in the
codebase - not for accounts, not for cash, not here. `RequirePermission` inside
`InvoiceDetailViewModel` is the closest analogue this phase adds, but `LocalSalesService.Post`,
`ElectronicDocumentGenerationService.Generate`, etc. themselves still have no permission check if
called directly (e.g. from a test or a future API surface) - the enforcement boundary today is the
ViewModel/command layer, not the domain service layer. Building a real service-layer interceptor
would be a cross-cutting architectural change affecting every existing screen, out of scope for this
phase.

## Zero-grant bootstrap (§29)

`LocalPermissionService.HasPermission`: an administrator user, or any user whose role has zero
explicit permission grants, is allowed everything (`_permissions.Count == 0 -> true`). This is a
deliberate backward-compatibility bootstrap documented in the class itself
(`src/R3.Infrastructure/LocalPermissionService.cs`): before any administrator has configured role
grants, existing users must not be locked out of screens they could already use. It predates this
phase and is unchanged by it.

For production hardening, two modes are worth distinguishing going forward:
- **PermissiveBootstrap** (today's behavior): zero grants -> allow. Safe for a fresh install / a
  migrated legacy install where roles have not been configured yet.
  **Enforced**: zero grants -> deny everything except an explicit "Administrator" role bypass.
Migration path: an administrator populates `role_permissions` for at least one role (the UI to do
this already exists per `PermissionCatalog.All`), then a future release flips the default for
non-administrator users with zero grants from allow to deny. This phase does not implement that flip
- doing so today would silently lock out every current desktop user, since no deployment of this app
has ever populated `role_permissions` - but records the exact mechanism and a safe order of
operations for whoever does.

## Provider response security

Raw provider responses are only ever written by `ElectronicDocumentDispatcher` (Phase 7) after a
`Send`/`QueryStatus` call, saved as an `ElectronicDocumentPayloadType.ProviderResponse` payload row -
never logged (see `docs/architecture/OUTBOX-DISPATCH.md`'s logging section). Viewing that raw content
requires `edocuments.provider_response.view`; without it, `ShowProviderResponse` is not offered by
any context menu or the E-Belge tab. No additional runtime redaction is applied to the stored
content itself - the write-time discipline (never persisting secrets into that payload in the first
place) is what has protected it since Phase 7, and remains unchanged here.

## Query/read-model architecture

`ElectronicDocumentOperationsService` (new, Infrastructure) is the one place read-shaped
`DataTable`s for these four screens are produced - a single join per method, filters applied in SQL,
`LIMIT` applied in SQL. It is deliberately separate from `LocalElectronicDocumentService` (owns the
status state machine + event log) and `ElectronicDocumentOutboxService` (owns claim/retry/dead-letter):
neither of those write-path classes gained a new read method for this phase, keeping their surface
exactly as Phases 2/3 left it.

## Index audit (§37)

Checked against the query shapes above; no new index was added.
- `electronic_documents(company_id,status,document_type)` (`IX_ElectronicDocuments_Status`, Phase 2)
  already serves every per-status count in `GetDashboardSummary` and every status filter in
  `SearchOutgoing`/`SearchErrors`.
- `electronic_document_outbox(status,available_at)` (`IX_ElectronicDocumentOutbox_Claim`, Phase 3) is
  exactly the shape `ClaimDue` and this phase's `SearchOutbox` status filter both use.
- `electronic_document_outbox(electronic_document_id,operation_type)` is covered by the existing
  partial unique index `UX_ElectronicDocumentOutbox_ActiveOperation` (Phase 3), which
  `GetLatestForDocument`'s lookup benefits from too.
No query here filters or orders on a column combination those three don't already cover; adding one
would be exactly the "unused index" Phase 2's DB-performance principle warns against.

## Operational workflow

```
Fatura Kesildi -> ElectronicDocument Ready
    -> UBL Oluştur -> Generated
    -> Gönderim Kuyruğuna Al -> Queued (Outbox Send: Pending)
    -> Şimdi Gönder / Şimdi Çalıştır -> Sending -> Sent (Outbox Send: Completed, QueryStatus: Pending chained)
    -> Durumu Sorgula -> Delivered -> Accepted | Rejected
Herhangi bir adımda transient hata -> Failed, Outbox: Pending (RetryPending) veya DeadLetter (max deneme)
DeadLetter -> Tekrar Dene (ManualRetry) -> Outbox: Pending, aynı IdempotencyKey -> döngü tekrar başlar
```

Every arrow above is a call into `LocalSalesService`, `ElectronicDocumentGenerationService`,
`ElectronicDocumentOutboxService` or `ElectronicDocumentDispatcher` - the operations screens this
phase adds only make that existing graph visible and clickable; none of the arrows themselves are new.

## Deferred (see also `docs/architecture/INVOICE-EDOCUMENT-UI.md`'s Deferred section)

- Gelen Belgeler (incoming e-document processing) - no real inbox engine exists in any phase so far;
  the menu entry opens an honest empty state, not a fake populated list.
- E-Belge Ayarları (company profile UI) - `LocalElectronicDocumentService.GetCompanyProfile`/
  `SaveCompanyProfile` exist (Phase 6) but have no screen; menu entry is a placeholder.
- Real dark-mode/theme-resource brushes for `EDocumentPresentation.SemanticState` - `App.xaml`
  currently pins `Theme="Light"` with no dynamic switching anywhere in the app, and is parallel-owned
  this phase; `SemanticColor` centralizes the *decision* (one function, five states) but still
  resolves to a fixed hex value rather than a `DynamicResource`.
- Auto-refresh (§31 "30-60 saniye configurable") - manual "Yenile" only; a timer-driven refresh was
  judged unnecessary complexity for an MVP operations screen and risks masking a stale-tab bug behind
  an auto-reload.
