---
name: wpf-theme-engine
description: How theming works in R3.Desktop (App.xaml tokens, Wpf.Ui ThemesDictionary, HandyControl skin, accent overrides) and how to add dark mode or high contrast without breaking screens. Use when changing colors globally, adding or renaming tokens, adding a dark theme, or when a control shows the wrong (e.g. blue Windows accent) color.
---

# R3 Theme Engine

## Current setup (App.xaml)

1. `ui:ThemesDictionary Theme="Light"` + `ui:ControlsDictionary` (Wpf.Ui Fluent styles)
2. HandyControl `SkinDefault.xaml` + `Theme.xaml`
3. R3 tokens (`R3.*` brushes, legacy `CanvasBrush`/`TextBrush`...)
4. **Accent override**: `SystemAccentColor*` and `AccentFillColor*` are redefined to graphite, so Wpf.Ui controls don't pick up the user's Windows accent (usually blue). If a control shows blue, it's reading an accent key that isn't overridden yet. Add that key here.
5. Implicit compact styles (BasedOn Wpf.Ui defaults) and the DataGrid styles.

Merge order matters. Later dictionaries win. R3 tokens and styles must come after the libraries.

## Rules

- One theme system: all colors flow from App.xaml keys. No per-view palettes.
- Token names are semantic (`R3.Danger.Brush`), never visual (`Red2`).
- The DataGrid styles still contain raw hex values (#EEEFF1, #DDE2E6, ...). When touching them, extract those into `R3.Grid.*` tokens (`R3.Grid.Header.Brush`, `R3.Grid.Line.Brush`, `R3.Grid.RowAlt.Brush`, `R3.Grid.Selection.Brush`).
- Use `StaticResource` while the theme is fixed. If runtime theme switching is added, tokens consumed by views must switch to `DynamicResource` (in code: `SetResourceReference(prop, key)` instead of `FindResource`).

## Adding dark mode (when requested)

1. Move tokens into `Themes/Light.xaml` and `Themes/Dark.xaml` with identical keys.
2. Swap the merged dictionary at runtime, and call Wpf.Ui `ApplicationThemeManager.Apply(ApplicationTheme.Dark)` to switch its dictionary as well.
3. Convert token consumers to DynamicResource / SetResourceReference.
4. Search and remove remaining hard-coded colors in .cs/.xaml, because they won't switch. (`grep -rn "Color.FromRgb\|#[0-9A-Fa-f]\{6\}" src/R3.Desktop`)
5. Check the status colors for contrast on dark surfaces.

## High Contrast

When `SystemParameters.HighContrast` is true, map tokens to `SystemColors.*Brush` keys. Hard-coded colors are the main blocker.
