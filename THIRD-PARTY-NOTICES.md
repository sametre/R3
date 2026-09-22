# Third-Party Notices — UI Modernization

Scope: packages relevant to the R3.Desktop UI modernization effort (see
`docs/UI-MODERNIZATION-PLAN.md`). This is a focused subset; the full
solution-wide dependency ledger lives in `docs/DEPENDENCIES.md` (currently
stale for the Desktop UI packages added 2026-09-21 — refreshing that file is
part of Phase 1).

Every license below was verified by reading the package's own `.nuspec`
license metadata and, where the license is shipped as a file rather than an
SPDX expression, the license file's actual text — not assumed from a code
comment. Verified 2026-09-22 against the exact versions pinned in
`Directory.Packages.props`.

| Package | Version | License | Verified via | Commercial/closed-source redistribution |
| --- | --- | --- | --- | --- |
| WPF-UI (`Wpf.Ui`) | 4.3.0 | MIT | nuspec `<license type="expression">MIT` | Permitted |
| Fluent.Ribbon | 11.0.2 | MIT | `license/License.txt` inside package | Permitted |
| Dirkster.AvalonDock | 4.72.1 | Ms-PL (Microsoft Public License) | `LICENSE` inside package | Permitted (explicitly on the user's allow-list) |
| Dirkster.AvalonDock.Themes.Aero | 4.72.1 | Ms-PL | `LICENSE` inside package | Permitted |
| HandyControl | 3.5.1 | MIT | `LICENSE` inside package | Permitted — **but currently unused** (see below) |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | nuspec `<license type="expression">MIT` | Permitted |
| CefSharp.Wpf.NETCore | 151.3.240 | BSD-3-Clause | `LICENSE` inside package | Permitted |
| Serilog | 4.4.0 | Apache-2.0 | nuspec expression | Permitted |
| Serilog.Extensions.Logging | 10.0.0 | Apache-2.0 | nuspec expression | Permitted |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | nuspec expression | Permitted |
| FluentValidation | 12.1.1 | Apache-2.0 | nuspec expression | Permitted |

**No package on this list is GPL/AGPL, no DevExpress/Telerik/Syncfusion/
Infragistics/Xceed commercial control, no QuestPDF, and none carries a user,
seat, device, or revenue quota.** No new package has been added by this
Phase-0 pass — this table documents what was already present in
`Directory.Packages.props` before this conversation.

## Findings requiring a decision (Phase 1)

- **HandyControl (3.5.1, MIT) is referenced in `App.xaml`'s
  `MergedDictionaries` but no `hc:`-namespaced control appears anywhere else
  in `src/R3.Desktop`.** It contributes a fourth implicit theming system
  (alongside WPF's own default styles, WPF-UI's, and Fluent.Ribbon's) for
  zero current benefit, and is a plausible contributor to the
  style-precedence issues described in `docs/UI-ARCHITECTURE.md`. Recommend
  removing the merge in Phase 1 unless a concrete HandyControl control is
  adopted deliberately.
- **CefSharp.Wpf.NETCore has a newer version available (152.0.60)** per the
  existing note in `docs/DEPENDENCIES.md`; out of scope for the UI
  modernization work, listed here only for completeness.

## Packages considered from the preferred set but not yet added

Per the user's brief, `ControlzEx`/`MahApps.Metro`, `DataGridExtensions`,
`LiveCharts2`, `ScottPlot`, `ClosedXML`, `CsvHelper`, `ZXing.Net`,
`PDFsharp`/`MigraDoc`, and `FastReport Open Source` were evaluated as
candidates against the phase plan but **none have been added** — Phase 0 is
discovery-only. `docs/UI-MODERNIZATION-PLAN.md` records which phase each
would first become relevant in (e.g. `DataGridExtensions` for the shared
`R3DataGrid` in Phase 3, `ClosedXML` for grid Excel export in Phase 3).
