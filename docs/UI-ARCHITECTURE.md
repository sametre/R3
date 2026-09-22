# UI Architecture (Phase 0 baseline + target)

Companion to `docs/UI-MODERNIZATION-PLAN.md` (phasing/risk) and
`docs/UI-DESIGN-SYSTEM.md` (tokens). This document describes what exists
today in `src/R3.Desktop` and the target shell architecture Phase 1–2 will
build toward.

## 1. Current state (as read 2026-09-22)

### Window shell

`MainWindow.xaml` is a `ui:FluentWindow` (WPF-UI) whose `DockPanel` stacks,
top to bottom:

1. `ui:TitleBar` (WPF-UI native Windows 11 chrome — snap layout support,
   rounded corners, custom title bar buttons)
2. A custom brand bar (`R3BrandMark` + company name)
3. **A classic WPF `Menu`** (`MainMenu`) — a condensed text menu built in
   code-behind by `BuildVisibleMenu()`
4. **A `fluent:Ribbon`** (`MainRibbon`, from Fluent.Ribbon) — the full
   module ribbon built in code-behind by `BuildMenu()`, ~140 lines, 12 tabs
5. A thin accent line
6. An `xcad:DockingManager` (Dirkster.AvalonDock, `AeroTheme`) hosting the
   tabbed document workspace
7. A status bar (`Border`, `Dock="Bottom"`) with company/branch/warehouse
   combo boxes, a connection-status text, app version, and clock

**Both (3) and (4) are live simultaneously** — `MainWindow()`'s constructor
calls `BuildMenu(); BuildVisibleMenu();` unconditionally. This is not a
deliberate two-tier "quick menu + ribbon" design as far as any doc comment
records; the two were observed to conflict directly during this Phase-0
pass: with `MainMenu.Visibility="Visible"`, the `fluent:Ribbon` rendered
with an effective bounding rectangle of zero height (confirmed via UI
Automation `BoundingRectangle`) even though its `Tabs` collection was
correctly populated — i.e. **the two navigation surfaces are currently
fighting over the same layout row and the Ribbon is losing.** `MainMenu`'s
`Visibility` was also observed to change three times within one session
(unset → `Collapsed` → `Visible`) from a concurrent, uncoordinated edit on
the same file — see the "Critical operational risk" note in the plan doc.

### Theming

`App.xaml` merges, in order: WPF-UI's `ThemesDictionary Theme="Light"` +
`ControlsDictionary`, then HandyControl's `SkinDefault.xaml` + `Theme.xaml`.
On top of that it defines a small flat set of named brushes
(`CanvasBrush`, `SurfaceBrush`, `AccentBrush`, `TextBrush`,
`MutedTextBrush`, `BorderBrush`) plus a large block of **implicit**
(keyless) `Style` elements for `TextBox`/`ComboBox`/`Button`/`DataGrid`/etc.
that apply automatically to every screen, including ones built entirely in
code-behind.

- **There is no dark theme.** No `Theme="Dark"` dictionary, no
  `ThemeManager`, no persisted user theme preference anywhere in
  `R3.Desktop`. Light is the only theme that exists.
- **There is no compact/normal view toggle.** The current density (row
  height 21px, header 24px, 11.5px control font) is hard-coded as the only
  density; it was tuned once, in-place, as part of an earlier
  "DevExpress-density" pass, not as a switchable mode.
- Hardcoded hex colors exist directly in ~26 `Views/*.xaml`/`*.cs`,
  `MainWindow.xaml(.cs)`, `LegacyAlignedViews.cs`, and
  `PurchaseModuleViews.cs` files rather than referencing the named brushes
  above — i.e. today's design system is partially token-based and partially
  ad-hoc literal colors. (A same-session tactical pass already converted
  most blue-hued literals to neutral grays; Phase 1's design-system tokens
  from `docs/UI-DESIGN-SYSTEM.md` should supersede that quick fix with the
  user's actual Navy/Orange palette and proper `R3.*.Brush` token names.)

### Navigation → screen wiring

Every ribbon/menu entry calls one of ~44 `Open*` methods on `MainWindow`
(`OpenProductList`, `OpenCanonicalAccounts`, `OpenPurchaseDocuments`, …),
each of which calls a shared `OpenTab(title, factory)` helper. `OpenTab`
already de-duplicates by tab title — calling it twice with the same title
re-activates the existing tab instead of opening a second one. This is
exactly the behavior the brief's "Sekmeli ekran sistemi" section asks for,
and **already exists** — Phase 2 should verify/harden it (e.g. de-dupe by a
stable document key, not just a display title, for tabs like "Cari Hareketler
• Ahmet Yılmaz" that embed a record name) rather than reimplement it.

Not yet present, per the brief's tab-system checklist: pinned tabs, saved/
restored user layout, layout versioning, recovery from a corrupt layout
file, "close others"/"close all" (partially present via
`DocumentContextMenu` — confirmed `CloseOtherTabs_Click`,
`CloseAllTabs_Click`, `CloseTabsToLeft/Right_Click`, `ReopenClosedTab_Click`
all exist already), unsaved-changes-on-close warning (not found — dialogs
close via `ShowDialog()` returning a bool with no dirty-check gate observed
in the sampled `EditorDialogs.cs` dialogs).

### MVVM boundary

Mixed, not uniform:

- A proper MVVM slice exists under `ViewModels/` + `Views/*.xaml` for the
  Cari/Kasa/E-Belge screens (`AccountsViewModel`, `CashAccountsViewModel`,
  `ElectronicDocumentDashboardViewModel`, etc.), each backed by a plain
  C# service class (`AccountServices.cs`, `CashServices.cs`) — **not** an
  interface + DI container; the ViewModel constructs its service
  dependencies directly.
- A second, larger slice (`MainWindow.xaml.cs`, `EditorDialogs.cs`,
  `LegacyAlignedViews.cs`, `PurchaseModuleViews.cs`) is built entirely in
  code-behind: `UIElement`-returning static factory methods that query
  `StoreDatabase` directly and construct `Grid`/`DataGrid`/`Border` trees by
  hand, with no `ViewModel` layer at all.

No `Microsoft.Extensions.DependencyInjection` (or any DI container) is
referenced anywhere in `R3.Desktop` — confirmed by search. `R3.Server` (the
optional ASP.NET Core backend) does use the built-in ASP.NET Core DI
container, but that is a separate process/project and out of scope here.

**Recommendation for Phase 1–4: do not retrofit a DI container into
R3.Desktop as part of this UI effort.** The brief explicitly says not to
redesign the backend/service layer and to avoid unnecessary abstraction;
introducing DI is an orthogonal architectural change with its own risk, and
the existing direct-construction pattern is not what is blocking the visual
modernization work. Flag it as a separate future decision, not a Phase 1–4
task.

## 2. Target shell architecture

### Chosen combination (see plan doc §"UI kombinasyon kararı" for the full rationale)

- **Keep `ui:FluentWindow`** for the outer window chrome only (title bar,
  Windows-11 rounded corners/snap layout). This is the part of WPF-UI that
  directly serves the brief's "Windows 11 görünümü" requirement and has the
  smallest integration surface (one root element).
- **Keep `fluent:Ribbon` as the single top navigation surface** — this is
  what the brief's own reference layout asks for ("Üstte modül bazlı Ribbon
  menü"). Remove the classic `MainMenu` entirely rather than running both;
  the observed zero-height rendering conflict is reason enough on its own,
  independent of the density/redundancy argument.
- **Stop merging HandyControl.** Remove the two `HandyControl` resource
  dictionaries from `App.xaml`. It is unused today (see
  `THIRD-PARTY-NOTICES.md`) and is a third competing implicit-style source.
- **For ordinary controls (`TextBox`, `ComboBox`, `DataGrid`, `Button`
  outside the ribbon), standardize on plain WPF controls styled through the
  R3 `ResourceDictionary`** (design-system tokens), not WPF-UI's `ui:*`
  control variants — the existing implicit `TargetType="{x:Type TextBox}"`
  style block in `App.xaml` already does this and should remain the pattern;
  the app should stop growing new `ui:Button`/`ui:TextBox` usage so there is
  one control-styling source of truth instead of two.

This is deliberately **not** a pure "Option 1/2/3" pick from the brief; it
is a scoped hybrid arrived at from the actual conflict observed during this
Phase-0 pass (the brief's own instruction, when WPF UI and Fluent.Ribbon
compete, is to run "a small integration test first" rather than commit
blindly — this *is* that test's result). Written down explicitly so a future
session does not re-litigate it without new evidence.

### Theming mechanism (Phase 1 target, not yet built)

- Add a `Light.xaml` / `Dark.xaml` resource dictionary pair defining every
  `R3.*.Brush` token from `docs/UI-DESIGN-SYSTEM.md`.
- Swap the active dictionary in `Application.Current.Resources.MergedDictionaries`
  at runtime (no restart) — standard WPF pattern, no extra package needed
  for this alone.
- Persist the choice (theme + density) in a small local settings file next
  to the existing `LoginCredentialStore` (same DPAPI-backed local-storage
  pattern already used for remembered login), not in the SQLite business
  database.
- Chart/report colors (once LiveCharts2/ScottPlot are evaluated in Phase 3)
  must read from the same tokens so a theme swap recolors them too.

### Status bar data source

Confirmed today: company/branch/warehouse combos are populated once from
`_db.Query(...)` on load/selection-change, not polled. The brief's
constraint ("durum çubuğu veritabanına sürekli sorgu göndermemelidir") is
already satisfied; Phase 2 just needs to keep it that way when the status
bar is rebuilt for theming.
