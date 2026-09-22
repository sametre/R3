# UI Design System (target — Phase 1 will implement)

Companion to `docs/UI-ARCHITECTURE.md`. Nothing in this document is
implemented yet — Phase 0 is discovery-only. This records the token values
and rules Phase 1 will encode as resource dictionaries, superseding the
earlier same-session tactical blue→gray recolor pass (that pass stays as a
harmless interim state; these are the real target values).

## Colors

Base palette:

| Token | Value | Use |
| --- | --- | --- |
| Primary Navy | `#123047` | Ribbon/shell chrome, primary text on light surfaces where emphasis is needed |
| Primary Dark | `#0B2233` | Chrome pressed/dark-theme base |
| Accent Orange | `#F97316` | The single interactive accent — primary buttons, selected tab, focus |
| Accent Orange Hover | `#EA580C` | Hover/pressed state of the accent |
| Success | `#15803D` | Positive status, success toasts |
| Warning | `#D97706` | Warning status |
| Danger | `#DC2626` | Errors, destructive actions |
| Info | `#0369A1` | Informational status only — not used as the general chrome accent |

Light theme:

| Token | Value |
| --- | --- |
| App Background | `#F4F6F8` |
| Surface | `#FFFFFF` |
| Surface Secondary | `#EEF2F5` |
| Border | `#D6DEE5` |
| Primary Text | `#17212B` |
| Secondary Text | `#5B6773` |

Dark theme:

| Token | Value |
| --- | --- |
| App Background | `#10171D` |
| Surface | `#172129` |
| Surface Secondary | `#202C35` |
| Border | `#34434E` |
| Primary Text | `#F1F5F9` |
| Secondary Text | `#A8B3BD` |

### Semantic resource keys (Phase 1 must define exactly these names)

```
R3.Background.Brush
R3.Surface.Brush
R3.Border.Brush
R3.Text.Primary.Brush
R3.Text.Secondary.Brush
R3.Accent.Brush
R3.Success.Brush
R3.Warning.Brush
R3.Danger.Brush
```

Colors must never be written as literal hex directly in a screen; every
screen (XAML or code-behind) references one of the tokens above via
`{DynamicResource}` (not `{StaticResource}`, so a runtime theme swap takes
effect without a restart, per `docs/UI-ARCHITECTURE.md` §"Theming
mechanism"). Code-behind-built screens (`EditorDialogs.cs`,
`LegacyAlignedViews.cs`, `PurchaseModuleViews.cs`, `MainWindow.xaml.cs`)
resolve these via `Application.Current.Resources["R3.Accent.Brush"]` rather
than `new SolidColorBrush(Color.FromRgb(...))` literals — this is the same
category of change the same-session tactical recolor pass made file-by-file
by hand; Phase 1 should do it properly via lookup instead.

## Typography

- Font: `Segoe UI Variable`, fall back to `Segoe UI` if unavailable
  (`FontFamily="Segoe UI Variable Text, Segoe UI"`, matching the pattern
  already used in `StartupLoginWindow.cs`).
- Body text: 13–14px
- Grid text (compact density): 12–13px
- Page title: 22–26px
- Section title: 16–18px
- Caption/helper text: 12–13px
- Full Turkish character support (ç, ğ, ı, İ, ö, ş, ü) is required in every
  control and every export path (Excel, PDF) — verified as a Phase 5 test,
  see `docs/UI-MODERNIZATION-PLAN.md`.

## Spacing & sizing

- Base spacing scale: 4, 8, 12, 16, 24, 32 (px)
- Standard control height: 32–36px
- Primary button height: 36–40px
- Toolbar button height: 30–34px
- Corner radius: 4–6px
- No oversized empty padding in forms — density is a functional requirement
  for ERP data entry, not a stylistic preference.

### Grid density modes (decided 2026-09-22)

Three named density tokens, not two — resolves the open question this
document previously flagged about the compact floor:

| Mode | Grid row height | Grid header height |
| --- | --- | --- |
| Compact (**default**) | 26px | 30px |
| Normal | 32px | 34px |
| Comfortable | 38px | 34px |

`R3DataGrid` (Phase 3) exposes a `Density` property with these three named
values; nothing in between. The app opens in Compact. The user can change it
from settings, and the choice persists in the same local user-settings store
used for the theme preference (see `docs/UI-ARCHITECTURE.md` §"Theming
mechanism") — one settings object, two persisted fields (`Theme`,
`Density`), not two separate storage mechanisms.

Note: the current in-place density (`App.xaml`'s "Compact / keyboard-first
control defaults" block: 21px grid rows, 24px headers, 11.5px controls) is
denser than the new Compact floor above (26px/30px) and will be relaxed to
match it in Phase 1 rather than kept as an undocumented fourth density.

## Components requiring a defined style (Phase 1 checklist)

Button, TextBox, ComboBox, DatePicker, TabItem, Dialog chrome, status badge
(success/warning/danger pill), loading state, empty-result state — each
needs one light-mode and one dark-mode style sharing the same template,
differing only by which `R3.*.Brush` resources resolve.
