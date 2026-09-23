---
name: wpf-design-system
description: R3.Desktop design tokens (colors, typography, spacing, radius, density) and how to use them from XAML and from code-built C# views. Use whenever writing or reviewing any visual code in src/R3.Desktop, such as colors, brushes, FontSize, Margin/Padding/Thickness, CornerRadius, or control heights.
---

# R3 Design System

Source of truth: `src/R3.Desktop/App.xaml`. The look is neutral gray/white, SAP Fiori-like, **dense** (DevExpress/classic ERP density), with a single teal accent. Status colors carry meaning and stay saturated.

## Color tokens (use these keys, never raw hex)

| Purpose | Key |
|---|---|
| Page background | `R3.Background.Brush` |
| Card / panel surface | `R3.Surface.Brush` |
| Borders, dividers | `R3.Border.Brush` |
| Main text | `R3.Text.Primary.Brush` |
| Secondary / help text | `R3.Text.Secondary.Brush` |
| Accent (primary action, selection highlight) | `R3.Accent.Brush` |
| Success / Warning / Danger | `R3.Success.Brush` / `R3.Warning.Brush` / `R3.Danger.Brush` |
| Sidebar | `SidebarBrush`, `SidebarHoverBrush` |

The older keys `CanvasBrush`, `SurfaceBrush`, `TextBrush`, `MutedTextBrush`, `BorderBrush`, and `AccentBrush` still exist. New code uses the `R3.*` keys.

XAML: `Foreground="{StaticResource R3.Text.Secondary.Brush}"` (use `DynamicResource` only if the theme can change at runtime; see `wpf-theme-engine`).
Code-built views: `(Brush)Application.Current.FindResource("R3.Text.Secondary.Brush")`. **Not** `Brushes.DimGray` and not `Color.FromRgb(...)`. The codebase currently has ~250 hard-coded colors in .cs files. Don't add more. When you edit a line that has one, replace it.

If a needed color has no token, add a token to App.xaml, give it a semantic name, and add a one-line comment. Don't inline the value.

## Typography scale (dense ERP)

| Role | Size | Weight |
|---|---|---|
| Page title (view header) | 18 | SemiBold |
| Section heading / group | 13 | SemiBold |
| Body, inputs, buttons (implicit style) | 11.5 | Normal |
| Help / caption / status bar | 11 | Normal, secondary brush |
| Grid cells and headers (implicit style) | 10.5 | Normal / SemiBold header |

Allowed sizes: 10.5, 11, 11.5, 13, 18 (plus 22 for dashboard KPI numbers only). Current code also uses 15, 16, 17, 19, 20, and 21 at random. Normalize to the scale whenever you touch that code. Don't set FontSize on inputs, buttons, or grids at all, because the implicit styles handle it.

## Spacing scale

`2 · 4 · 6 · 8 · 12 · 16 · 24`

- View outer margin: **16** (the codebase uses 18 and 14; converge on 16)
- Between filter bar / grid / footer: **8**
- Label → control gap: **6**. Between control groups in a bar: **12**
- Card padding: **12** (`CardStyle`)
- Grid cell padding comes from the style. Don't set it.

Values like 5, 9, 10, 14, and 18 are off-scale. Use the nearest scale value.

## Radius

`4` cards/inputs (CardStyle) · `5` small icon buttons · `8` sidebar nav items. Nothing else.

## Density / sizing

Inputs, buttons, and combos are MinHeight 24 via implicit styles. Grid RowHeight 21, header 24. Don't set explicit `Height = 26` on combos (ReorderView does; it's legacy). Width is fine for combos in filter bars.

## Controls

The app merges Wpf.Ui (`ui:` namespace) and HandyControl. Prefer standard WPF controls, which already pick up the implicit Wpf.Ui-based styles. Use `ui:` controls when you need their features (SymbolIcon, NumberBox, InfoBar). Use HandyControl only where it's already used. Don't introduce a third control library.
