---
name: wpf-performance
description: WPF rendering and responsiveness rules for R3.Desktop, covering UI thread blocking, DataGrid virtualization, binding cost, and startup time. Use when a screen freezes, loads slowly, scrolls poorly, or when building a view that shows large data sets.
---

# R3 WPF Performance

## Never block the UI thread

- Database and service calls that can take >50 ms run via `Task.Run` (use `AsyncLoading.LoadTableAsync` with `LoadingOverlay`). Apply results back on the UI thread (the `await` continuation already does this).
- Guard against races: if the user changes a filter while a load is running, the older result must not overwrite the newer one (use a request counter or a CancellationToken).
- No `.Result` / `.Wait()` on tasks from UI code (deadlock risk).

## Virtualization

- DataGrid/ListBox virtualize only when their height is constrained. **Never** put them inside a `StackPanel` or a `ScrollViewer`, and never give them `Height="Auto"` inside an unconstrained parent. That realizes every row.
- Keep `EnableRowVirtualization=True`. Use `VirtualizingPanel.VirtualizationMode="Recycling"` for large grids.
- `VirtualizingPanel.IsVirtualizingWhenGrouping="True"` if grouping is used.
- Don't use `ScrollViewer.CanContentScroll="False"` on grids. It disables virtualization.

## Data volume

- Filter and limit in SQL (service parameters), not with client-side `RowFilter` over huge tables.
- For lists that can reach tens of thousands of rows, use a `TOP/LIMIT` with a "daha fazla yükle" option, or paging.
- Build a `DataTable` or collection fully *before* assigning `ItemsSource`. Don't add rows one by one to a bound `ObservableCollection` (every add triggers a layout pass).

## Binding & visuals

- Freeze brushes created in code (`brush.Freeze()`) or better, use shared resources.
- Avoid heavy `DataTemplate`s per cell. Use simple TextBlock columns. Use `DataGridTemplateColumn` only when needed.
- Converters in hot paths must be cheap. Cache `CultureInfo` (as views do with a `static readonly Turkish`).
- Avoid effects (`DropShadowEffect`, blur) on items that repeat. They're fine on a single card.
- Watch the Output window for binding errors. Each failed binding costs time and hides bugs.

## Startup

Keep MainWindow constructor light. Build tab content lazily when the tab first opens, and warm up CefSharp only when a browser view is actually needed.

## Measuring

Use a Stopwatch around load plus a `DesktopLogging` log line for anything >300 ms. For rendering, use Visual Studio's "Application Timeline" or PerfView. Measure before and after, and give the numbers when you claim a speedup.
