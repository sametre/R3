# Invoice / E-Document Operation UI (Phase 8)

Connects the existing backend engines (Posting Engine, UBL Engine, Outbox Engine, Provider Engine)
to a real invoice screen. This phase adds no new business logic to those engines - it only exposes
what they already do, through one canonical read path (`InvoiceDetailViewModel`) and one screen
(`InvoiceDetailView`).

## Draft vs Posted

`sales_documents.status` is `Draft` or `Posted` (Cancel/Return are not implemented by
`LocalSalesService` yet - `EDocumentPresentation.SalesStatusLabel` also maps `Cancelled` for when
that lands, but nothing produces it today). While `Draft`, the Genel/Ürünler/Tutarlar tabs show the
saved draft; once `Posted`, `InvoiceDetailView.BuildGenel` shows an explicit "cannot edit financial
fields, use cancel/return" notice. There is no in-place financial-field edit UI for a posted
invoice in this phase - editing draft line items beyond the existing single-line `SalesInvoiceDialog`
quick-create form is also out of scope (see Deferred below).

## Posting UX

"Kaydet" (draft create/update, `SalesInvoiceDialog`/`LocalSalesService.SaveDraft`) and "Faturayı Kes"
(`LocalSalesService.Post`, financial posting) are two different actions, both already existed at the
service layer (`LocalSalesService.Post` – Phase 5). Phase 8 exposes "Faturayı Kes" as
`InvoiceDetailViewModel.PostCommand`: confirmation dialog (amount + a note that account/inventory
ledgers will be created) → `IsBusy`/`BusyText` while running → on success a summary MessageBox
(cari hareketi, stock movements only claimed if `InventoryTransactions.Count > 0` - service-only
lines never claim a stock movement) → on failure the exact `LocalSalesService.Post` exception message
(already a clean Turkish sentence, e.g. "Yetersiz stok.") is shown, never a raw stack trace.

## Electronic document states

`InvoiceDetailViewModel` never invents a status: it always reads `ElectronicDocumentStatus` off
`LocalElectronicDocumentService.GetBySource("SalesInvoice", invoiceId)`. Turkish labels and badge
colors come from `R3.Desktop.Presentation.EDocumentPresentation` - the one place backend enum values
become UI text (`StatusLabel`, `TypeLabel`, `SalesStatusLabel`, `EventLabel`).

## Action availability

`EDocumentPresentation.ActionsFor(ElectronicDocumentStatus)` is the single source of truth for which
buttons a status may show; `InvoiceDetailViewModel`'s six `[RelayCommand]`s each also have a
`CanExecute` predicate combining the same state check with `!IsBusy`. The view reads both: `actions.CanX`
decides visibility, `vm.XCommand.CanExecute(null)` decides enabled/disabled.

| Belge Durumu | Visible Actions | Enabled when | Permission (see below) |
|---|---|---|---|
| Ready | UBL Oluştur | `!IsBusy` | `edocuments.generate` |
| Generated | XML Görüntüle, Gönderim Kuyruğuna Al | `!IsBusy` | `edocuments.payload.view`, `edocuments.invoice.send`/`archive.send` |
| Queued | XML Görüntüle, Şimdi Gönder | `!IsBusy` | `edocuments.payload.view`, `edocuments.invoice.send`/`archive.send` |
| Sending | (none - in flight) | — | — |
| Sent / Delivered | XML Görüntüle, Provider Yanıtı, Durumu Sorgula | `!IsBusy` | `edocuments.payload.view`, `edocuments.status.query` |
| Accepted / Rejected | XML Görüntüle, Provider Yanıtı | — (terminal, no Send/Retry) | `edocuments.payload.view` |
| Failed | XML Görüntüle, Tekrar Dene | `!IsBusy` | `edocuments.retry` |
| CancellationRequested / Cancelled / Archived | (event timeline only) | — | `edocuments.view` |

Event timeline is always visible (not gated behind a button) once an electronic document exists.

## Outbox UX

`InvoiceDetailViewModel.SendCommand`/`QueryStatusCommand`/`RetryCommand` all call
`ElectronicDocumentDispatcher.DispatchAsync("desktop-manual", userId)` - the same manual-trigger
entry point `docs/architecture/OUTBOX-DISPATCH.md` describes (no background host exists yet).
`QueueCommand` only calls `ElectronicDocumentOutboxService.QueueForSendAsync` - it deliberately does
NOT also dispatch, keeping "kuyruğa al" and "gönder" as two distinct, separately observable actions
per spec, even though both are one click apart. A full standalone "Gönderim Kuyruğu" screen with a
detail drawer (idempotency key, correlation id, locked-by) is **deferred** - `ElectronicDocumentOutboxService.GetQueue`/`Get`
already return everything such a screen needs; only the screen itself (which would belong in
`LegacyAlignedViews.cs`, out of scope for this phase per the standing parallel-work restriction) is
not built.

## Error handling

`InvoiceDetailViewModel.RunAsync` is the one place every command's try/catch lives:
`ArgumentException`/`InvalidOperationException`/`KeyNotFoundException` (the families
`LocalSalesService`/`ElectronicDocumentGenerationService`/`ElectronicDocumentOutboxService` already
throw with ready-made Turkish messages) become `ErrorMessage` with no log entry - expected business
rejections, not bugs, matching the convention `AccountEditViewModel.Save` already established. Any
other exception is logged via `ILogger<InvoiceDetailViewModel>` and shown as a generic
"İşlem tamamlanamadı" message - never a raw exception string in the UI.

## Permission mapping

Existing `PermissionCatalog` codes are reused as-is: `edocuments.view`, `edocuments.generate`,
`edocuments.payload.view`, `edocuments.invoice.send`/`edocuments.archive.send`,
`edocuments.status.query`, `edocuments.retry`. The Phase 8 spec also names `sales.invoice.post`,
`edocuments.outbox.view` and `edocuments.provider_response.view`, which are **not yet** in
`PermissionCatalog.All` - that file was mid-edit by a parallel change (adding `purchasing.*` codes)
throughout this phase and was left untouched to avoid an unrelated merge conflict. Until it is
updated, `LocalPermissionService.HasPermission` treats any unlisted code as allowed whenever the
calling user's role has zero explicit grants (its documented backward-compatible default) - so
nothing is functionally blocked today, but an administrator cannot yet scope these three actions
down via the role editor. This is a known, disclosed gap, not a bug.

## Deferred / explicitly out of scope this phase

- Full "Giden Belgeler" (§32-34), "Gönderim Kuyruğu" (§35-36), "Hatalı Belgeler" (§37) and
  "E-Belge Dashboard" (§38) standalone screens - the backend (`Search`, `GetQueue`, `Get`) already
  supports them; only the screens themselves are not built, since they would live in
  `LegacyAlignedViews.cs` (parallel-owned, off-limits) or need a new equivalent file, which was not
  worth the scope for this pass.
- Ödeme and Sevkiyat tabs on the invoice detail screen: no real backend data source exists yet for
  invoice-linked payment allocation, and Shipment is genuinely parallel, in-flight work this phase
  must not touch or depend on - showing either tab would mean fake/empty data, which the spec
  forbids.
- Multi-line product entry with a full grid editor in `SalesInvoiceDialog` (spec §11): still a
  single-line quick-draft form, now with real product/barcode lookup, auto-filled unit/VAT and a
  real stock preview (§12-13) instead of raw ID text boxes, but not a full editable line grid.
- Cancel/Return invoice workflow (referenced in §43 as the correct route around a locked posted
  invoice) - `LocalSalesService` has no Cancel/Return operation to call yet.
