---
name: wpf-dpi-accessibility
description: DPI scaling, keyboard access, focus, contrast, and UI Automation (screen reader / test automation) rules for R3.Desktop WPF. Use when reviewing accessibility, fixing blurry or clipped UI at 125%/150% scaling, fixing focus or tab-order problems, or preparing screens for automated UI tests.
---

# R3 DPI & Accessibility (WPF)

## DPI

WPF uses device-independent units (1/96 in), so it scales automatically. What still breaks it:
- Bitmaps (PNG icons) blur, so prefer vector icons (Wpf.Ui `SymbolIcon`, Path geometry) or provide multiple sizes.
- Fixed pixel `Height`s that clip text when the font scales. Use `MinHeight` instead.
- Hosted Win32/CefSharp content (EmbeddedBrowserView) needs per-monitor DPI awareness in the app manifest. Check it when a browser pane looks blurry.
- Set `UseLayoutRounding="True"` on root windows to avoid blurry 1px borders.
- Test at 100%, 125%, and 150%, and on a monitor move.

## Keyboard & focus

- Every interactive element must be reachable by Tab in a logical order. Use `KeyboardNavigation.TabNavigation="Cycle"` inside dialogs and `IsTabStop="False"` on decorative items.
- Focus is visible. Don't style out `FocusVisualStyle` without replacing it.
- When a view opens, focus lands on the first useful input (search box or first field): `Loaded += (_, _) => search.Focus();`.
- Labels use access keys: `<Label Content="_Cari" Target="{Binding ElementName=CariBox}"/>`.
- Default/cancel buttons in dialogs: `IsDefault="True"` / `IsCancel="True"`.

## UI Automation (screen readers + automated UI tests)

- Icon-only buttons and inputs whose label is a separate TextBlock need `AutomationProperties.Name` (Turkish).
- Grids and important regions need `AutomationProperties.AutomationId` so UI tests can find them. The `winui-ui-testing` skill (winapp ui) works against WPF through UIA.
- Status changes (save succeeded, validation error) must be visible text, not just a color change.

## Contrast

- Text vs background ≥ 4.5:1 (normal), ≥ 3:1 (≥ 18px or bold 14px). `R3.Text.Secondary.Brush` (#5B6773) on white passes. Don't use lighter grays for text.
- Never convey status by color alone. Pair it with a text label or icon (e.g. "Onaylı" chip + green).
- Check High Contrast mode: hard-coded colors ignore it. Tokens can be redirected. See `wpf-theme-engine`.

For a full WCAG criterion-by-criterion audit, also run the `accessibility-auditor` skill. Its web/ARIA advice translates as: ARIA label → AutomationProperties.Name, role → control type, and tabindex → TabIndex/IsTabStop.
