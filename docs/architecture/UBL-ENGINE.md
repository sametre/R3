# R3 UBL Document Generation Engine (Phase 6)

`UblInvoiceGenerator` turns a posted `SalesInvoice` + its already-routed
`ElectronicDocument` into a real UBL 2.1 / UBL-TR invoice XML string - no
placeholder or fabricated field is ever emitted; anything R3 doesn't
actually model is either omitted (and documented below) or throws
`UblGenerationException` rather than guessing.

## Architecture

```
ElectronicDocument (Ready)
        │
        ▼
ElectronicDocumentGenerationService.Generate(electronicDocumentId, userId)
        │
        ├─ already Generated? ──▶ return existing payload (idempotent, spec §29) - no rework
        │
        ├─ not Ready? ──▶ throw (illegal state)
        │
        ▼
   UblInvoiceGenerator.Generate(electronicDocumentId)          [IUblDocumentGenerator]
        │  reads: sales_documents, sales_document_lines, products, units,
        │         companies, electronic_document_company_profiles,
        │         accounts, account_tax_profiles, account_addresses,
        │         account_einvoice_profiles
        │  UblInvoiceValidator.ValidateSource  (pre: VKN/TCKN, unit mapping, alias, currency)
        │  UblInvoiceValidator.ValidateGeneratedXml (post: structural + totals reconcile)
        ▼
   UBL XML string
        │
        ├─ validation failed ──▶ RecordEvent("ElectronicDocumentGenerationFailed") - status stays Ready
        │
        ▼ success: ONE SQLite transaction (spec §48)
   BEGIN
     SavePayloadWithinTransaction (PayloadType=UblXml, SHA-256 hash, version)
     GenerateWithinTransaction    (Ready -> Generated, event DocumentGenerated)
   COMMIT
```

`IUblDocumentGenerator` is provider-independent by construction (spec §3):
it has no knowledge of GİB, a specific entegratör, an HTTP endpoint, or a
signing certificate - it only ever sees R3 domain rows and returns a
canonical XML string. It also never touches a transaction or persists
anything (spec §47) - `ElectronicDocumentGenerationService` is the only
place that opens the database transaction and drives the status/payload
writes together. `ElectronicDocumentDispatcher`/`IElectronicDocumentProvider`
(Phase 3) are what eventually send the result, unchanged, to a real
provider - not part of this phase (spec §17/final rule: no network calls
here at all).

## Field mapping

| UBL Field | R3 Domain | Database Source | Required? | Validation |
| --- | --- | --- | --- | --- |
| `Invoice.UBLVersionID` | constant | — | yes | fixed `"2.1"` |
| `Invoice.CustomizationID` | constant | — | yes | fixed `"TR1.2"` |
| `Invoice.ProfileID` | routing scenario | `account_einvoice_profiles.invoice_scenario` (EInvoice) or fixed (EArchive) | yes | `TEMELFATURA`/`TICARIFATURA`/`IHRACAT`/`KAMU`/`EARSIVFATURA` only |
| `Invoice.ID` | ElectronicDocument.DocumentNumber | `sales_documents.document_no` | yes | non-empty (post-generation) |
| `Invoice.UUID` | ElectronicDocument.Uuid | `electronic_documents.uuid` | yes | must equal `ElectronicDocument.Uuid` exactly; never regenerated |
| `Invoice.IssueDate`/`IssueTime` | ElectronicDocument.IssueDate | `electronic_documents.issue_date` | yes | non-empty, `yyyy-MM-dd`/`HH:mm:ss`, `CultureInfo.InvariantCulture` |
| `Invoice.DocumentCurrencyCode` | SalesInvoice.CurrencyCode | `sales_documents.currency_code` | yes | non-empty (pre-generation) |
| `AccountingSupplierParty PartyIdentification` | Company/CompanyProfile tax number | `companies.tax_number` or `electronic_document_company_profiles.tax_number` | yes | non-empty (pre-generation) |
| `AccountingSupplierParty PostalAddress` | Company address | `electronic_document_company_profiles.address_line/city/district/postal_code/country` (added this phase), falls back to `companies.address` | no | omitted if both empty, never fabricated |
| `AccountingCustomerParty PartyIdentification` | Account tax id | `accounts.tax_number` (VKN) or `accounts.identity_number` (TCKN, when `account_tax_profiles.person_type='Individual'`) | yes | non-empty (pre-generation) - "Missing VKN/TCKN" test |
| `AccountingCustomerParty PostalAddress` | Account default address | `account_addresses` (`is_default=1`, else any active) | no | omitted if none exists |
| `InvoiceLine.ID` | SalesInvoiceLine.LineNo | `sales_document_lines.line_no` | yes | sequential per invoice |
| `InvoiceLine.InvoicedQuantity` + `unitCode` | SalesInvoiceLine.BaseQuantity (already unit-converted) | `sales_document_lines.base_quantity`, `units.code` via `UblUnitCodeMap` | yes | unmapped unit → generation rejected, no fallback |
| `InvoiceLine.LineExtensionAmount` | SalesInvoiceLine.NetAmount | `sales_document_lines.net_amount` | yes | domain-calculated at posting, never recalculated here |
| `InvoiceLine AllowanceCharge` | SalesInvoiceLine.DiscountAmount | `sales_document_lines.discount_amount` | no | omitted when zero |
| `InvoiceLine TaxTotal/TaxSubtotal` | SalesInvoiceLine VAT | `sales_document_lines.vat_rate/vat_amount` | yes | `TaxScheme.Name="KDV"`, `TaxTypeCode="0015"` only (no ÖTV/tevkifat - see Gaps) |
| `LegalMonetaryTotal.*` | SalesInvoice totals | `sales_documents.subtotal/discount_total/tax_total/grand_total` | yes | `PayableAmount` must reconcile with `grand_total` within 0.01 (post-generation) |

## Snapshot semantics

The generator reads **live** `Account`/`Company`/`Product` data at
generate time, not the minimal `ElectronicDocument.RecipientSnapshotJson`
captured at posting (Phase 5) - spec §33 explicitly says not to build a
second, duplicate snapshot system, so that JSON field stays exactly what
it was: a lightweight, posting-time-only summary for UI/audit convenience.
Reading live data at generate time is not a contradiction of spec §5's
"don't stay live-linked forever": immutability is enforced at the
*payload* level (`SavePayload`/`SavePayloadWithinTransaction` refuse to
overwrite a UblXml/SignedXml payload once the document is past `Generated`
- the Phase 2 review-gate fix), not by restricting what the generator is
allowed to read. Once the XML is generated and saved, it is frozen; a
later `Account.Name` or `Product.Name` edit cannot touch it - verified
directly by `GeneratedPayloadStaysUnchangedAfterAccountAndProductNamesChangeLater`.

## Idempotency

`ElectronicDocumentGenerationService.Generate` checks the document's
current status first: if already `Generated`, it returns the existing
payload's id/hash without calling the generator again, without inserting
a second payload row, and without writing a second `DocumentGenerated`
event (spec §29) - verified by `OrchestratorIsIdempotentOnAnAlreadyGeneratedDocument`.

## Failure behavior

A `UblGenerationException` (missing VKN/TCKN, unmapped unit, missing
EInvoice alias, structural/total-reconciliation failure) does **not**
transition the document - it stays `Ready` (spec §28, illegal-transition-safe
by construction: `GenerateWithinTransaction` is simply never called). A
technical `ElectronicDocumentGenerationFailed` event is recorded via
`LocalElectronicDocumentService.RecordEvent` (the same append-only
`electronic_document_events` table every other transition writes to - not
`audit_logs`, which is business audit, per spec §52's explicit separation).
No payload is created. Verified by
`MissingBuyerTaxNumberRejectsGenerationWithoutCreatingAPayloadOrChangingStatus`.

## Hashing

`SavePayloadCore` computes `SHA256` over the exact UTF-8 bytes of the
generated XML string - no normalization, no re-encoding. Verified by
`StoredPayloadHashMatchesSha256OfExactContentBytes` (independently
recomputes the hash from the stored content and compares).

## Known gaps (reported, not silently worked around)

- **Excise (ÖTV) and withholding (tevkifat) tax are not modeled anywhere**
  in `sales_document_lines` - only `vat_rate` exists. This generator only
  ever emits a `KDV` `TaxCategory`/`TaxScheme` (`Name="KDV"`,
  `TaxTypeCode="0015"`). An invoice that actually needs ÖTV/tevkifat
  produces an incomplete XML; there is no silent substitution.
- **No `cac:PartyTaxScheme`/tax-office element on either party.** The
  seller has `companies.tax_office`; the buyer has no equivalent field
  anywhere in `account_tax_profiles`. Rather than emit a real VKN/TCKN with
  a fabricated or missing office name, the whole element is omitted for
  both parties. `PartyIdentification` (the VKN/TCKN itself) is always
  present and validated.
- **No official GİB UBL-TR XSD bundle is available in this repo**, so
  `UblInvoiceValidator.ValidateGeneratedXml` is structural only - it does
  **not** validate against the real GİB schema.
- **Unit code mapping (UN/CEFACT Rec. 20) is a small, explicit table**
  (`UblUnitCodeMap`): `ADET→C62, KG→KGM, GR→GRM, LT→LTR, MT→MTR, M2→MTK,
  M3→MTQ, KUTU→BX, PAKET→PK, ÇİFT→PR`. An unmapped unit **rejects
  generation** rather than silently defaulting (spec §14) - verified by
  `UnmappedUnitCodeRejectsGenerationRatherThanSilentlyFallingBack`.
- **`cbc:InvoiceTypeCode` is always `"SATIS"`** - R3 has no return/credit
  invoice document type yet (see `docs/architecture/POSTING-ENGINE.md`'s
  Sales Return note).
- **Payment information (`cac:PaymentMeans`/`PaymentTerms`) is not
  emitted** - `sales_documents` has no payment-method/terms fields to
  source it from; fabricating one was not an option.

## Performance

No N+1: `ReadLines` is a single query returning every line (not one query
per line); `ReadSeller`/`ReadBuyer` are 2 queries each regardless of line
count. Total query count for generating one invoice is constant
(≈6 queries) whether it has 1 line or 100.

## Tests

`tests/R3.Domain.Tests/UblInvoiceGeneratorTests.cs` (12 tests): header/
line/total mapping from a real posted invoice including actual discount+VAT
arithmetic reconciling with `PayableAmount`; EInvoice vs EArchiveInvoice →
correct `ProfileID`; Individual buyer → TCKN; UUID stability across
repeated `Generate()` calls; SHA-256 hash correctness; payload immutability
after Account/Product names change; orchestrator idempotency; missing VKN/
TCKN rejection; unmapped-unit rejection; service line handling; Turkish
(`tr-TR`) current-culture invariance; and a full acceptance scenario (2 ×
1000 @ 20% VAT → line 2000 / VAT 400 / payable 2400, generated end-to-end
through `ElectronicDocumentGenerationService`).

## Phase 7 handoff

This phase ends at `Generated` with a stored, hashed, immutable UBL
payload. It does **not** touch the outbox, does not queue anything, and
makes no network call. Phase 7's job: `Generated → Queued` (which requires
a real `ElectronicDocumentOutboxService.QueueForSendAsync` call - the
outbox/dispatcher/provider machinery already exists from Phase 3, it has
simply never had a real payload to work with until now) and wiring a real
`IElectronicDocumentProvider` behind `DevelopmentElectronicDocumentProvider`.
