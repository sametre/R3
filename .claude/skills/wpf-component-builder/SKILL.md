---
name: wpf-component-builder
description: How to build a reusable WPF building block for R3.Desktop (filter bar, status chip, totals footer, lookup box, empty-state panel, dialog). Use when the same UI chunk appears in 2+ views, or when asked to create a new control or component for the desktop app.
---

# R3 WPF Component Builder

## Before building

1. Search first. `LoadingOverlay`, `AsyncLoading`, `ErpGridContext`, `CardStyle`, `NavigationButtonStyle`, the dialogs in `EditorDialogs.cs`/`Views/*Dialog*`, and the Wpf.Ui/HandyControl controls may already cover it.
2. It has to be needed in at least 2 places. Otherwise keep it inline.

## Choose the form

| Need | Build |
|---|---|
| Only a look change | `Style` in App.xaml (BasedOn the existing implicit style) |
| Composition of existing controls, used from code-built views | `internal sealed class X : Border/DockPanel` in C# (pattern: `LoadingOverlay`) |
| Composition used from XAML with bindable props | `UserControl` + `DependencyProperty`s |
| Behavior added to existing controls | Attached property / static helper (pattern: `ErpGridContext`) |

Put shared components in `src/R3.Desktop/Controls/` (create it if needed). Don't bury them inside one view file.

## Rules

- Take all colors, sizes, and spacing from tokens (`wpf-design-system`). No `Color.FromRgb`.
- Expose data through properties or `DependencyProperty`s. The component must not query `StoreDatabase`.
- Cover all states: normal, hover, focused (visible focus ring), disabled, and error where relevant.
- Keyboard: focusable parts are in tab order, and there are no mouse-only actions.
- Accessibility: set `AutomationProperties.Name` on anything that isn't self-labelled (icon buttons, and inputs whose label is a separate TextBlock).
- Text is Turkish, passed in by the caller where it varies. Don't hard-code screen-specific text inside the component.
- Pure logic (formatting, status mapping) goes in a testable static class that has tests in `tests/R3.Desktop.Tests`.

## Deliverable

The component, one real call site migrated to it (so it's proven), and a note in the PR/commit message listing the other call sites that could adopt it.
