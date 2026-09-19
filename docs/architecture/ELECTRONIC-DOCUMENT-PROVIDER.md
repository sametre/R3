# Electronic Document Provider (Phase 10)

## BLOCKER

**No real özel entegratör (private e-Fatura/e-Arşiv integrator) is identified anywhere in this
repository.** A real HTTP/SOAP adapter was **not** built this phase, per the explicit instruction to
never invent an endpoint, SOAP action, request field, auth scheme, or error code.

### What was searched

- Case-insensitive full-repository grep (`.cs`, `.md`, `.json`, `.config`, `.xml`, `.txt`, excluding
  `bin`/`obj`/`.git`) for: `Logo`, `Uyumsoft`, `EDM`, `Foriba`, `QNB`, `DigitalPlanet`, `FIT`,
  `Izibiz`, `NES`, `Sovos`, `entegratör`/`entegrator`, `GİB`/`GIB`, `Endpoint`, `Username`,
  `Password`, `ApiKey`.
- Every `appsettings*.json` under `src/R3.Server` (`appsettings.json`,
  `appsettings.Development.json`, `appsettings.Development.example.json`).
- `README.md` in full.
- A search for any `docs/integrations` directory (none exists).
- Every `ProviderType =` assignment in `*.cs` (none exist - `ProviderType` is only ever a free-text
  column on `electronic_document_company_profiles`, Phase 6, never assigned a literal vendor name
  anywhere in code).

### What was found

Only generic GİB (Gelir İdaresi Başkanlığı - the Turkish tax authority, not a private integrator)
references: two portal links in `EmbeddedBrowserView.cs` (`gib.gov.tr`, `ebelge.gib.gov.tr`) and an
`is_gib_compliant` compliance flag on the account e-Fatura profile (`LocalAccountService.cs`). Every
other match on the vendor-name list above was a false positive (`.Fit()` - a WPF DataGrid column-sizing
helper method name; `logo` - a CSS class in an unrelated embedded-browser HTML string; both
pre-existing, unrelated code).

**Conclusion**: not verifiable from anything in this repository. Per the phase instruction, this
means: build the provider adapter *architecture* (contracts, configuration, secret handling,
provider selection, tests) without a concrete HTTP implementation for any specific vendor, and report
the missing vendor documentation/credentials as a blocker - which this document does.

## What Phase 10 built instead

Everything needed so that *selecting* a real vendor later is a configuration change plus one new
class implementing the existing `IElectronicDocumentProvider` contract - never a change to
`ElectronicDocumentDispatcher`, `ElectronicDocumentOutboxService`, `LocalElectronicDocumentService`,
`LocalSalesService`, or any UI screen.

### Architecture

```
ElectronicDocumentDispatcher
          ↓
IElectronicDocumentProvider          <- unchanged since Phase 7
          ↓
ElectronicDocumentProviderFactory.Create(ElectronicDocumentProviderConfiguration)
          ↓
   ┌──────────────┬─────────────────────────────┐
   │ Development   │ Http (kind selected,         │
   │ (unchanged)   │ real vendor NOT selected)     │
   │               │        ↓                     │
   │               │ UnconfiguredElectronicDocument│
   │               │ Provider (placeholder -       │
   │               │ never silently succeeds)      │
   └──────────────┴─────────────────────────────┘
```

Once a real vendor is selected, a new `<Vendor>ElectronicDocumentProvider : IElectronicDocumentProvider`
(and, if the vendor supports them, `IElectronicDocumentCancellationCapable`/
`IElectronicDocumentRecipientQueryCapable`/`IElectronicDocumentHealthCheckCapable`) replaces
`UnconfiguredElectronicDocumentProvider` in `ElectronicDocumentProviderFactory.Create` for the `Http`
kind. Nothing upstream of `IElectronicDocumentProvider` needs to change.

### Configuration (`ElectronicDocumentProviderConfiguration.cs`)

`ElectronicDocumentProviderKind` (`Development` | `Http`), `ElectronicDocumentProviderEnvironment`
(`Test` | `Production`), `BaseUrl`, `RequestTimeoutSeconds`, `StatusQueryDelaySeconds`,
`CompanyIdentifier`. `CompanyIdentifier` is not a new persisted field - it is the company's existing
VKN/TCKN (`ElectronicDocumentCompanyProfileEdit.TaxNumber`, Phase 6), reused rather than duplicated.
`ProviderKind`/`Environment` reuse the existing `electronic_document_company_profiles.provider_type`/
`.environment` free-text columns (Phase 6) - **no schema migration was added this phase**: `BaseUrl`
has no column yet, since its real shape (single URL? per-operation URLs? a WSDL address?) is
vendor-specific and unknown; a real adapter's configuration migration should be added alongside that
adapter, not guessed now.

`ElectronicDocumentProviderConfigurationValidator.Validate` catches a missing/inconsistent
configuration (empty `CompanyIdentifier`, non-positive timeouts, `Http` kind with no `BaseUrl`, and -
critically - `Development` kind flagged as `Production` environment) with a descriptive Turkish error,
before `ElectronicDocumentProviderFactory.Create` ever constructs a provider.

### Secret handling (`ElectronicDocumentSecretProvider.cs`)

`IElectronicDocumentSecretProvider` (`GetSecretAsync`/`SetSecretAsync`/`RemoveSecretAsync`, keyed by
`(companyId, key)`) is the one seam a real adapter's `Username`/`Password`/`ApiKey`/`ClientSecret`/
`CertificatePassword` will go through - never a plaintext column on a business table.
`DpapiElectronicDocumentSecretProvider` is a real (not a stub) implementation: Windows DPAPI
(`ProtectedData`, `CurrentUser` scope - the same OS-level protection Windows Credential Manager is
built on) encrypts a small JSON blob stored in a file next to the SQLite database, never in a
schema-migration-tracked table. `InMemoryElectronicDocumentSecretProvider` exists for
tests/CI that should not depend on DPAPI/user-profile state.

### Authentication abstraction

`IElectronicDocumentProviderAuthenticator.GetAuthenticationHeadersAsync` is declared (§11) but
**not implemented or called anywhere** - the concrete scheme (Basic/Bearer/OAuth2/SOAP
UsernameToken/API Key/Certificate) is provider-specific and unknown. Guessing one would violate the
phase's core instruction.

### Provider capabilities (`ElectronicDocumentProviderCapabilities.cs`)

`IElectronicDocumentCancellationCapable`, `IElectronicDocumentRecipientQueryCapable`,
`IElectronicDocumentHealthCheckCapable` are optional interfaces (pattern-matched, like
`IAsyncDisposable` alongside `IDisposable`) - not added to `IElectronicDocumentProvider` itself,
because not every real vendor supports Cancel/RecipientQuery/Health, and forcing every
implementation to answer for a capability it may not have would itself be "uydurma" (invented)
behavior. `DevelopmentElectronicDocumentProvider` and `UnconfiguredElectronicDocumentProvider` both
implement `IElectronicDocumentHealthCheckCapable` (trivially - "healthy" only ever means
"constructed" for Development, and "not configured" is itself a valid, honest health answer for the
placeholder).

### Provider selection / never a silent fallback

`ElectronicDocumentProviderFactory.Create` is the only place a `ElectronicDocumentProviderKind` turns
into an `IElectronicDocumentProvider` instance. `UnconfiguredElectronicDocumentProvider` (the `Http`
kind's current implementation) never silently succeeds and never silently behaves like Development:
every `SendAsync`/`QueryStatusAsync` call returns an explicit, **permanent**
(`IsTransientFailure = false`) `ProviderNotConfigured` failure, so `ElectronicDocumentOutboxService`
dead-letters it immediately instead of retrying forever against a provider that does not exist.
`DevelopmentElectronicDocumentProvider` is unchanged and still the default for local dev/tests.

### UI (`ElectronicDocumentProviderSettingsView`/`ViewModel`, menu: E-Belge → Ayarlar)

Reads/writes only the already-persisted `provider_type`/`environment` columns - no schema change.
Shows a "TEST ORTAMI" badge when `Environment = Test`. "Bağlantıyı Test Et" builds a configuration
from the current form values and calls `ElectronicDocumentProviderFactory.Create` +
`IElectronicDocumentHealthCheckCapable.CheckHealthAsync` - never a raw `HttpClient` call from the
ViewModel. Gated on `edocuments.settings.edit` (existing permission, both for editing the form and
for running the connection test). No credential fields exist on this screen yet - there is nothing to
mask with `********`, because no real provider (and therefore no real credential) has been selected;
when one is, its fields belong here, write-only, through `IElectronicDocumentSecretProvider`.

## Sections not applicable without a real vendor

Per §66 ("do not invent"), these are marked **Not verifiable from provider documentation** rather
than filled in with a guess:

- Real send/status/error/status-code mapping tables (§19/§21/§27/§28)
- Actual endpoint URLs, SOAP actions, or REST routes (§6/§10)
- The real authentication scheme (§11)
- HTTP 200-with-business-error body shape (§22) - the *principle* (never treat
  `IsSuccessStatusCode` alone as success) is documented here for whoever writes the real adapter, but
  there is no real response body to parse yet
- Cancel/RecipientQuery capability map for a specific vendor (§30-31)
- Certificate/signing requirements (§46)
- Provider-specific idempotency mechanism (§25) beyond the existing UUID + IdempotencyKey strategy
  (§15/§25/§26), which is vendor-agnostic and already fully implemented (Phase 7)

## Provider Operation → R3 Method mapping (architecture-level; vendor-agnostic)

| Provider Operation | Provider Endpoint/Method | R3 Method | Request Type | Response Type | Retry Classification |
|---|---|---|---|---|---|
| Send (E-Fatura/E-Arşiv) | *unknown - no vendor selected* | `IElectronicDocumentProvider.SendAsync` | `ElectronicDocumentSendRequest` | `ElectronicDocumentProviderResult` | Transient (timeout/5xx/429) vs Permanent (validation/rejection) - `IsTransientFailure` |
| Query Status | *unknown* | `IElectronicDocumentProvider.QueryStatusAsync` | `ElectronicDocumentStatusQueryRequest` | `ElectronicDocumentStatusQueryResult` | Same as Send |
| Cancel | *unknown - capability unconfirmed for any vendor* | `IElectronicDocumentCancellationCapable.CancelAsync` (optional) | `ElectronicDocumentCancelRequest` | `ElectronicDocumentProviderResult` | Same as Send |
| Recipient/Taxpayer Query | *unknown* | `IElectronicDocumentRecipientQueryCapable.CheckRecipientAsync` (optional) | `taxNumber: string` | `ElectronicDocumentRecipientQueryResult` | Transient vs Permanent |
| Health/Connection check | *unknown - may not exist for the real vendor* | `IElectronicDocumentHealthCheckCapable.CheckHealthAsync` (optional) | — | `ElectronicDocumentProviderHealthResult` | n/a |

## Provider Status → R3 Status mapping

Not applicable - no real vendor's status codes are known. The vendor-agnostic mapping that *is* real
today (Phase 7): `ElectronicDocumentRemoteStatus` (`Sent`/`Delivered`/`Accepted`/`Rejected`, the shape
any real vendor's status must be translated into) → `ElectronicDocumentStatus` state machine
transitions, enforced by `LocalElectronicDocumentService`'s `Allowed` transition table - see
`docs/architecture/OUTBOX-DISPATCH.md`. A real adapter's job is only to produce the correct
`ElectronicDocumentRemoteStatus` value from the vendor's real status codes; it must never skip
`Delivered` even if the vendor's own status model does (Phase 9's dispatcher already enforces this).

## Migration

**None added this phase.** `PRAGMA user_version` was read before any code was written (currently 11
in the working tree, driven by parallel Shipment/Purchasing work not owned by this phase); no new
migration number was claimed, since no schema change was made.
