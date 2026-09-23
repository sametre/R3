---
name: wpf-ui-architect
description: Structure rules for R3.Desktop (WPF, .NET 10, Wpf.Ui + HandyControl). Use before adding or restructuring any screen, view, dialog, or window in src/R3.Desktop, when a view file grows past ~400 lines, when SQL or business logic is showing up in UI code, or when deciding between a XAML+ViewModel view and a code-built view.
---

# R3 WPF UI Architect

R3.Desktop is **WPF**, not WinForms. Rules from WinForms guides (Form1.cs, UserControl, DataGridView, Krypton) don't apply here. Map them to WPF: Window/UserControl/DockPanel, DataGrid, ResourceDictionary styles.

## Layering (top → bottom, dependencies point down only)

```
MainWindow (shell: sidebar, tab host, status bar)       MainWindow.xaml(.cs)
  └ Navigation / tab opening                            MainWindow.xaml.cs
      └ Feature views                                   Views/*.xaml | Views/*View.cs
          └ Reusable pieces                             LoadingOverlay, ErpGridContext, dialogs
              └ Design tokens + implicit styles         App.xaml resources
                  └ ViewModels / Presentation mappers   ViewModels/, Presentation/
                      └ Services                        R3.Infrastructure Local*Service
                          └ StoreDatabase / domain      R3.Infrastructure, R3.Domain
```

## Two view styles exist; pick deliberately

| Style | When | Example |
|---|---|---|
| XAML + ViewModel (MVVM) | Editing forms, dialogs with validation, anything with >1 state | `Views/AccountEditDialog.xaml` + `ViewModels/AccountEditViewModel.cs` |
| Code-built `sealed class XView : DockPanel` | Read-mostly lists and reports with a filter bar + grid | `Views/ReorderView.cs`, `Views/LotTrackingView.cs` |

A new editing screen goes XAML + ViewModel. Don't start new code-built editors in `EditorDialogs.cs` (already ~1000 lines).

## Hard rules

- **No SQL in views.** A view calls a `Local*Service` in R3.Infrastructure. Existing inline `db.Query(...)` calls for combo lookups (e.g. warehouse/group lists) are tolerated legacy; when you touch them, move them into the service.
- **No business rules in views.** Pricing, stock math, posting, and validation that must hold for the server belong in R3.Domain / services, with tests in `tests/R3.Domain.Tests`.
- **MainWindow.xaml.cs is the shell only.** It opens tabs and wires navigation. It shouldn't build screen content inline. New screens get their own file under `Views/`.
- **File size.** Past ~400 lines, split by section (filter bar, grid, detail panel) or move logic into a ViewModel/Presentation mapper.
- **Presentation mapping** (status → Turkish label/brush, enum → display text) goes in `Presentation/*Presentation.cs` static classes. Don't use switch statements scattered across views.
- **Async loading.** Queries run off the UI thread through `AsyncLoading.LoadTableAsync` with a `LoadingOverlay`. Don't use a synchronous `service.X()` in a click handler for anything that can touch many rows.
- **Grid context menus** go through `ErpGridContext.Register(grid, viewKey, actions, refresh)` with `ContextActionDefinition`s in `ContextActions/`. Don't hand-build a ContextMenu.
- **Styling** comes from App.xaml tokens (see `wpf-design-system`). Don't create `new SolidColorBrush(Color.FromRgb(...))` in new code.
- **Permissions**: gate actions via `IPermissionService` / context-action availability, not by hiding buttons ad hoc.

## Checklist before writing a new screen

1. Which service owns the data? Does it exist? If not, create `Local<Feature>Service` with tests first.
2. MVVM or code-built? (Table above.)
3. Which parts are reusable (filter bar, totals footer, status chip)? Reuse existing helpers before writing new ones.
4. Loading, empty, and error states: see `wpf-datagrid-expert`.
5. Keyboard path: can the whole task be done without a mouse? See `erp-desktop-ux`.
