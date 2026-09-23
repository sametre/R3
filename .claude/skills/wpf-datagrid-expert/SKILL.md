---
name: wpf-datagrid-expert
description: Standards for every list/table screen in R3.Desktop (WPF DataGrid). Use when building or reviewing any DataGrid, list view, report grid, search/filter bar, bulk selection, grid export, or when a list screen is slow, cluttered, or missing loading/empty/error states.
---

# R3 DataGrid Standard

Most of R3's work happens in grids. Every grid follows this standard.

## Baseline

- Style: implicit DataGrid style applies automatically. Set `Style = FindResource("ProfessionalDataGridStyle")` only when you need the `Loaded` hook that registers the context menu (ErpGridContext).
- `AutoGenerateColumns = false`, always. Declare the columns yourself.
- Headers are Turkish and short. Editable columns get the ` ✎` suffix (as in ReorderView).
- Numbers: `StringFormat = "N2"` (amounts/qty) or `"N0"` (counts/days), `ConverterCulture = tr-TR`, **right-aligned** (use an ElementStyle with `TextAlignment.Right`). Dates: `dd.MM.yyyy`.
- Money columns show ₺ only in totals and footers, not in every cell.
- Widths: code columns ~90–110, name columns `*` or 200–240, qty 70–90, amounts 90–110. The main text column should absorb the extra width (`DataGridLength(1, Star)`) so the grid doesn't leave dead space on the right.
- Freeze identifying columns (`FrozenColumnCount`) when the grid has more than ~10 columns.
- Read-only columns must set `IsReadOnly = true`. Editable grids have to make it obvious which columns can be edited.

## Required states

| State | Implementation |
|---|---|
| Loading | `LoadingOverlay` over the grid + `AsyncLoading.LoadTableAsync`. The shell stays usable. A stale result must never overwrite a newer one. |
| Empty | A centered secondary-text message that says *why* and what to do next ("Filtreye uyan kayıt yok — tarih aralığını genişletin"). Never show a blank white grid. |
| Error | Message with a retry option. Don't swallow exceptions. Log through DesktopLogging. |
| Status line | Under or above the grid: `"{n} kayıt • {selected} seçili • {total:N2} ₺"` (pattern from ReorderView.UpdateStatus). |

## Filter / search bar

- Sits in a WrapPanel above the grid, with a label+control pair for each filter and 12px between groups.
- Search box first. Enter runs the search. Search is debounced (~300 ms) if it runs live.
- Date range, then lookups (depo, grup, cari), then toggles. The Refresh button is last.
- Filtering over large sets happens in SQL (service parameter), not by `DefaultView.RowFilter` on 100k rows.

## Selection & actions

- Single selection by default. Use `Extended` only when there's a bulk action.
- A bulk-select column (`Seç` checkbox) updates the status line live.
- Row actions go in the context menu via `ErpGridContext.Register` + `ContextActionDefinition` (Primary action = double-click/Enter).
- Delete / irreversible actions go in the `Critical` group and need confirmation.

## Keyboard

Enter = open primary action · F5 = refresh · Ctrl+F = focus search · Del = critical delete (with confirm) · Ctrl+C copies cells. Tab must not get trapped inside the grid.

## Performance

Keep `EnableRowVirtualization` and `EnableColumnVirtualization` true (the default). Never wrap a DataGrid in a ScrollViewer or an auto-height StackPanel, because that disables virtualization. Page or limit very large result sets in the service. See `wpf-performance`.

## Export & preferences

When a list is a report, offer Excel/CSV export of the *visible, filtered* rows with Turkish headers. Column order and width can be reordered by the user. If persistence is added, key it by `viewKey`.
