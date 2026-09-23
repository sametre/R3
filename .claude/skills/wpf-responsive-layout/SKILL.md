---
name: wpf-responsive-layout
description: WPF layout rules for R3.Desktop so screens work from 1366x768 laptops up to large monitors and inside tabs. Use when laying out a view or dialog, fixing clipped, overlapping, or wasted-space layouts, or choosing between Grid, DockPanel, WrapPanel, and StackPanel.
---

# R3 WPF Layout

Target sizes: **1366×768 at 100%** is the minimum that must work fully. 1920×1080 at 125% is typical. Views live inside MainWindow tabs, so the available space is smaller than the window.

## Panel choice

| Panel | Use for |
|---|---|
| `DockPanel` | View skeleton: title/help/filter bar docked Top, status bar Bottom, grid fills the rest (`LastChildFill`) |
| `Grid` with `*` / `Auto` rows and columns | Forms, and anything two-dimensional |
| `WrapPanel` | Filter bars and toolbars that must wrap on narrow widths |
| `StackPanel` | Short, fixed lists of small items only. **Never** put a DataGrid or long content in it (infinite measure kills virtualization and scrolling) |
| `UniformGrid` | KPI tiles |

## Rules

- No absolute positioning (`Canvas`, fixed `Margin` offsets) for layout.
- Don't set fixed `Width`/`Height` on containers. Use `MinWidth`/`MaxWidth` for inputs and `*` sizing for regions.
- Forms use a 2-column label/value Grid (`Auto` + `*`, and `*` capped with MaxWidth ~420 so fields don't stretch across 1900px). Wide forms use 2 label/value column pairs.
- Long text: `TextWrapping="Wrap"` for help text, `TextTrimming="CharacterEllipsis"` plus a ToolTip for cells and labels.
- Dialogs: `SizeToContent="Height"`, a sensible MinWidth, `ResizeMode="CanResizeWithGrip"` when content can grow, and `WindowStartupLocation="CenterOwner"` with Owner set.
- Scrolling: a single scroll owner per region. Forms scroll inside a `ScrollViewer`. Grids scroll themselves.
- Side detail panels use a `GridSplitter` with sensible MinWidth on both sides.
- Test by resizing to 1366 px wide, and at 125%/150% scaling (see `wpf-dpi-accessibility`).
