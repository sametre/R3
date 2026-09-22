# UI Modernization Plan — Phase 0 (Discovery & Baseline)

Status: **Phase 0 complete. Phase 1 has not started — no package was added
and no UI file was modified as part of this plan.** Per the brief, this
document, `docs/UI-ARCHITECTURE.md`, `docs/UI-DESIGN-SYSTEM.md`, and
`THIRD-PARTY-NOTICES.md` are the Phase 0 deliverable; Phase 1 begins only
after this is reviewed.

## 0. Critical operational risk — read this first

**This working tree is currently being edited by at least one other,
uncoordinated party while this analysis was performed (2026-09-22).**
Concrete evidence gathered during this session, not inference:

- `src/R3.Desktop/MainWindow.xaml`'s `MainMenu` element's `Visibility`
  attribute was observed to change three times within a few minutes
  (absent → `Collapsed` → `Visible`) with no corresponding action taken by
  this session.
- `dotnet test tests/R3.Desktop.Tests` failed with a file-lock error:
  `R3.Infrastructure.dll` is held open by **"Microsoft Visual Studio
  (16648), R3.Desktop (7952)"** — i.e. someone has the solution open in
  Visual Studio with the Desktop app actively running/debugging right now.
- A full solution build (`dotnet build R3.slnx`) succeeded earlier in this
  session and **fails right now** with 4 compiler errors in
  `src/R3.Infrastructure/LocalInventoryDocumentService.cs` (missing methods
  `ValidateDocumentLocation`/`ValidateLocation`, a tuple-shape mismatch) —
  from in-progress, unrelated work (a "Depo ve Lokasyon Yönetimi" / warehouse
  location feature; `OpenWarehouseLocations`/`OpenWarehouseManagement`
  methods already exist in `MainWindow.xaml.cs` as stubs for it).

**Update (same day, later pass): still ongoing, not resolved.** A
re-verification run found the build green and no lock — looked resolved —
but the very next `dotnet test tests/R3.Desktop.Tests` run hit the exact
same `R3.Infrastructure.dll` lock again, this time from **different**
process IDs (`"Microsoft Visual Studio (12184), R3.Desktop (17948)"` vs the
original `(16648)/(7952)`), i.e. a new/second Visual Studio debug session
started. Per the user's explicit instruction, this session did **not**
attempt to close, kill, or force-unlock anything, and did not touch
`Service.cs`-family compile errors. Do not treat a single clean snapshot as
"done" — confirm directly with whoever is running that debug session before
Phase 1 begins.

**Recommendation: do not start Phase 1 while this is happening.** Any
sweeping change to `App.xaml`, `MainWindow.xaml`, or `MainWindow.xaml.cs` —
exactly the files Phase 1–2 needs to touch — will race with whoever is
making these edits and risks losing one side's work. Confirm with the user
whether that other work is finished/paused before any Phase 1 file change
begins. This is a process risk, not a technical one, and no plan can design
around it — it just needs to stop happening at the same time as this effort.

## 1. Baseline results (2026-09-22)

| Check | Result |
| --- | --- |
| `dotnet build R3.slnx` | **Currently failing**, 4 errors, all in `LocalInventoryDocumentService.cs` (see §0 — unrelated in-flight work, not this plan's doing) |
| `dotnet test tests/R3.Domain.Tests` | **122/122 passed**, 0 failed, 0 skipped (~12s) |
| `dotnet test tests/R3.Desktop.Tests` | **Blocked** — file lock held by a live Visual Studio debug session (§0), not a test failure |
| `dotnet test tests/R3.Server.Tests` | **Blocked** — depends on `R3.Infrastructure`, which currently fails to build (§0) |
| `tools/R3.UiSmoke` | Known broken per `memory/product_card_v2_sprint.md` — pre-existing drift, not in `R3.slnx`, out of scope here |
| Build warnings | 0, historically, whenever the build is green |
| Git status | Large pre-existing uncommitted change set (product-card-v2 sprint + an in-progress desktop-shell/ribbon pass); none of it touched by this Phase-0 pass |

**Re-run the full baseline (build + all three test projects) once §0's
concurrent edit has stopped**, before Phase 1 work begins — the numbers
above are a snapshot, not a guaranteed-clean starting line.

## 2. Solution inventory

- Solution: `R3.slnx`. Projects: `R3.Domain`, `R3.Application`,
  `R3.Contracts`, `R3.Infrastructure` (all `net10.0`), `R3.Desktop`
  (`net10.0-windows`, WPF), `R3.Server` (`net10.0`, ASP.NET Core, optional
  backend). Tests: `R3.Domain.Tests`, `R3.Desktop.Tests`, `R3.Server.Tests`.
  Tools: `R3.AsbMigration`, `R3.UiSmoke` (not in the `.slnx`).
- `Directory.Packages.props` uses Central Package Management — every
  version is pinned there, none floating. This must remain true; add new
  package versions there, not per-project.
- No formal DI container in `R3.Desktop` (see `docs/UI-ARCHITECTURE.md`
  §"MVVM boundary"). `R3.Server` uses ASP.NET Core's built-in container,
  unrelated to this effort.
- Full current-screen inventory, UI library usage, theming state, and MVVM
  boundary details are in `docs/UI-ARCHITECTURE.md` — not duplicated here.

## 3. UI kombinasyon kararı (library combination decision)

**Decision: a scoped hybrid — `ui:FluentWindow` for window chrome only +
`fluent:Ribbon` as the sole navigation surface + plain WPF controls styled
by a custom R3 `ResourceDictionary`. Remove the classic `MainMenu`. Remove
the HandyControl merge (unused).**

This is not picking blindly from the brief's three options — it is the
result of the "small integration test" the brief itself asks for when WPF
UI and Fluent.Ribbon might conflict, run using evidence already produced by
the existing codebase (documented in full, with the specific conflict
symptom, in `docs/UI-ARCHITECTURE.md` §1 "Current state" and §2 "Chosen
combination"). Short version: WPF-UI's `FluentWindow` is worth keeping only
for its Windows-11 window chrome (exactly what the brief separately asks
for); its control-level styles (`ui:Button`, `ui:TextBox`) are not — mixing
those with the Ribbon's own styling is what produced the observed
zero-height Ribbon rendering bug. Fluent.Ribbon is kept because the brief's
own reference layout explicitly asks for a ribbon at the top, and ~140
lines of working ribbon-building code already exist and are wired to 44
real `Open*` screen handlers — discarding that would be exactly the
"gereksiz yere yeniden yazma" the brief prohibits.

## 4. Phase plan

### Phase 0 — Discovery & baseline (this document) — done

Deliverables: this file, `docs/UI-ARCHITECTURE.md`,
`docs/UI-DESIGN-SYSTEM.md`, `THIRD-PARTY-NOTICES.md`. No code touched.

### Phase 1 — Design system (proposed scope)

Files: `App.xaml` (replace ad-hoc brushes with the full `R3.*.Brush` token
set from `docs/UI-DESIGN-SYSTEM.md`, as `DynamicResource`-friendly
dictionaries), new `Themes/Light.xaml` + `Themes/Dark.xaml`, remove the two
HandyControl `MergedDictionaries` entries. Component styles: Button,
TextBox, ComboBox, DatePicker, TabItem, Dialog chrome, status badge,
loading/empty states — light + dark each. No screen content changes yet.

Risk: every screen currently reading a literal hex or a `StaticResource`
brush needs to end up on `DynamicResource` + the new token names for theme
swap to work without a restart — this touches the same ~26 files the
same-session tactical recolor pass already touched once; expect to revisit
them again here, properly this time.

### Phase 2 — Main window

Files: `MainWindow.xaml`, `MainWindow.xaml.cs`. Remove `MainMenu` (classic
menu) entirely; keep and re-skin `MainRibbon`. Add theme/density switch
(persisted next to `LoginCredentialStore`'s local settings, not in SQLite).
Verify the status bar stays query-free on redraw (already true today, see
`docs/UI-ARCHITECTURE.md` §"Status bar data source" — must not regress).

**Blocked until §0's concurrent edit on this exact file stops.**

### Phase 3 — Shared `R3DataGrid`

New shared component (exact location TBD — likely `Controls/R3DataGrid.cs`
+ attached behaviors, not a new project) wrapping the built-in `DataGrid` +
`DataGridExtensions` (MIT, not yet added — see `THIRD-PARTY-NOTICES.md`).
Implements the full checklist from the brief (search, per-column filter,
multi-sort, column show/hide/resize/reorder/freeze, selection/total-row
counts, numeric column totals, context menu, row/cell copy, Excel export via
`ClosedXML` — MIT, not yet added — clear filters, reset view, per-user saved
view, active/inactive row styling, empty/loading/error states, full keyboard
support incl. the exact shortcut list in the brief). Virtualization settings
from the brief's snippet apply by default. Existing `ProfessionalDataGridStyle`
(`App.xaml`) becomes this component's base style rather than being
duplicated — do not fork it.

Performance targets (5k/20k/100k rows) measured and written to
`docs/UI-PERFORMANCE.md` (new file) before this phase is called done.

### Phase 4 — Pilot screens

Exactly the six screens the brief names, in this order (dependency order,
not arbitrary): main window/nav (Phase 2 already covers the shell) → Ürün/
Stok Kartları list → Cari Kartlar list → Ürün detay (`ProductDialog` in
`EditorDialogs.cs` already exists as an 11-tab card — re-skin, do not
rewrite) → Cari detay (`AccountEditDialog` already exists) → Stok Durumu →
Satış faturası detay (`InvoiceDetailView` already exists — **the posting
chain it calls must not change**, per the brief's explicit constraint and
`memory/product_card_v2_sprint.md`'s own note about preserving
`IsSellable`/`OrderMultiple` business-rule decisions from the last sprint).

No solution-wide style sweep before these six are done and reviewed.

### Phase 5 — Test & optimization

DPI (100/125/150/200%), resolution (1366×768, 1920×1080, 2560×1440),
keyboard-only pass, theme-swap correctness, accessibility pass, final
build+test run, license re-check of anything added in Phases 1–4.

## 5. New tests to add/update (per phase, not all at once)

Theme switch, tab open/close, duplicate-tab prevention (already exists —
add a regression test for it rather than reimplement), unsaved-changes
warning (does not exist yet — needs both the behavior and the test), grid
filter/sort, grid view save/load, corrupt-layout recovery, Excel export,
Turkish character round-trip, large-list performance, ViewModel command
tests for any new ViewModel, plus the existing sales/stock/cari test suites
must keep passing unmodified throughout.

## 6. Explicit non-goals (per brief §"Çalışma sınırları")

No database schema change, no migration, no backend business-rule rewrite,
no change to the sales posting chain or the e-document status flow, no
WinForms, no rewrite of the existing service/ViewModel layer, no DI
container introduction (see `docs/UI-ARCHITECTURE.md`), no single giant
rewrite commit — each phase lands and is verified independently.

## 7. Decisions recorded (2026-09-22, user response to Phase 0)

1. **Concurrent work / file locks:** as of this update, the build is green
   again and neither `devenv` nor `R3.Desktop.exe` is running — the
   `LocalInventoryDocumentService.cs` errors from §0 are gone and the file
   lock that blocked `R3.Desktop.Tests` is cleared. `MainWindow.xaml`,
   `MainWindow.xaml.cs`, `App.xaml`, and shared `ResourceDictionary` files
   remain untouched by this session pending the re-verified baseline in
   §1 being re-run and confirmed clean (§8 below).
2. **Grid density:** resolved with three named modes (Compact 26px/30px
   header, Normal 32px/34px, Comfortable 38px/34px header), Compact as
   default, user-configurable and persisted — full detail in
   `docs/UI-DESIGN-SYSTEM.md` §"Grid density modes". This supersedes this
   section's old open question #2 and the 21px in-place density noted in
   §"Current state".
3. **HandyControl:** researched solution-wide (namespace usage, XAML
   resource references, theme resources, style `BasedOn` links, converters,
   custom-control dependencies, transitive NuGet dependencies). Findings:
   exactly two references exist in the whole solution — the two
   `MergedDictionaries` lines in `App.xaml` and the `PackageReference` in
   `R3.Desktop.csproj`. No `xmlns:hc`, no `hc:`-prefixed control, no
   `BasedOn` reference to a HandyControl style key, anywhere. HandyControl
   itself declares **zero** package dependencies (its own `.nuspec` has no
   `<dependencies>` entries for any target framework), so nothing else in
   the solution depends on it transitively either. One caveat: HandyControl's
   `SkinDefault.xaml`/`Theme.xaml` ship *implicit* (keyless) styles for many
   stock WPF control types; `App.xaml`'s own explicit implicit-style block
   (added the same day, listed later in the same `ResourceDictionary`) wins
   precedence for the types it covers (`TextBox`, `ComboBox`, `Button`,
   `DataGrid`, etc.), but control types neither file explicitly restyles
   (e.g. `ScrollBar`, `Slider`, `Expander`) could still be silently
   reskinned by HandyControl today — this is real, if indirect, "usage,"
   not zero. **Per the user's conditional instruction, this qualifies as "no
   real usage" and removal proceeds as a separate, small, revertible change**
   (remove the `PackageReference` + the two `MergedDictionaries` lines,
   `dotnet restore`, build, test, visual smoke test, update
   `THIRD-PARTY-NOTICES.md` and `docs/DEPENDENCIES.md`) — **but this specific
   change still touches `App.xaml`, so per decision #1 it is queued behind
   the re-verified clean baseline, not executed in this update.**
4. **Main UI kombinasyonu:** confirmed as specified (FluentWindow chrome,
   Fluent.Ribbon navigation, AvalonDock workspace, WPF `DataGrid` +
   `R3DataGrid`, existing MVVM/service pattern kept, no
   `NavigationView`-vs-Ribbon double navigation). `MainMenu` removal is
   explicitly gated on a full command/permission/shortcut coverage mapping —
   see `docs/UI-MENU-COMMAND-MAPPING.md` (new). **That mapping currently
   shows two handler mismatches ("Müşteri kartları", "Depolar") and two
   missing commands ("Sevkiyat" under Satış, "Kullanıcı ve Yetkiler") — so
   `MainMenu` removal cannot happen yet even once Phase 2 starts; those three
   items must be resolved and the mapping re-verified as 100% ✅ first.**
5. **Document filenames:** verified — `docs/UI-MODERNIZATION-PLAN.md`,
   `docs/UI-ARCHITECTURE.md`, `docs/UI-DESIGN-SYSTEM.md`,
   `THIRD-PARTY-NOTICES.md` all exist under those exact names, no garbled or
   duplicate copies found anywhere in the repository.

## 8. Baseline re-verification (2026-09-22, later pass) — still not clean

Executed exactly as requested: `git status`, `dotnet restore`, solution
build, all three test projects, package vulnerability check, package
license check. **Result: not fully clean — do not start Phase 1 yet.**

| Check | Result |
| --- | --- |
| `git status` | Same file set as §1, plus this session's four new doc files and `docs/UI-MENU-COMMAND-MAPPING.md`; no other-session files reverted or altered |
| `dotnet restore R3.slnx` | Clean, no output/errors |
| `dotnet build R3.slnx` | **Green** — 0 warnings, 0 errors (the §0 `LocalInventoryDocumentService.cs` errors are gone — that part of the concurrent work finished) |
| `dotnet test tests/R3.Domain.Tests` | **123/123 passed**, 0 failed, 0 skipped (~12s) — one more test than the §1 snapshot (122), added by the concurrent work |
| `dotnet test tests/R3.Desktop.Tests` | **Still blocked** — `R3.Infrastructure.dll` locked by `"Microsoft Visual Studio (12184), R3.Desktop (17948)"`, a **different, newer** VS/debug session than §0's. Not attempted to unlock, per instruction. |
| `dotnet test tests/R3.Server.Tests` | **9/9 passed**, 0 failed (doesn't depend on the locked `R3.Desktop` build output) |
| `dotnet list R3.slnx package --vulnerable --include-transitive` | **No vulnerable packages** in any of the 10 projects in `R3.slnx` |
| `dotnet list R3.slnx package --deprecated` | Only `xunit` 2.9.3 flagged ("Legacy", `xunit.v3` suggested) across the three test projects — a test-framework note, not a UI package, out of this effort's scope |
| Package license check | See `THIRD-PARTY-NOTICES.md` — unchanged since Phase 0, all still compliant |

**Net: build + Domain/Server tests are clean; Desktop.Tests cannot be
verified while a Visual Studio debug session keeps relocking the shared
`R3.Infrastructure.dll` build output.** This isn't a code defect — it will
resolve itself the moment that VS session closes — but per the user's own
gate ("temiz baseline alındıktan sonra Phase 1'e geç"), Desktop.Tests being
unverifiable means the baseline is not yet fully clean. Recommend the user
confirm when that VS session is closed so this one remaining check can be
re-run before Phase 1 starts.
