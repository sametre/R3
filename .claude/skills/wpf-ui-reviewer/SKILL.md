---
name: wpf-ui-reviewer
description: Quality gate for R3.Desktop UI code. Checks architecture, tokens, DataGrid standard, layout, DPI/a11y, performance, and ERP UX, and returns a findings report. Use after writing or changing any view, dialog, or style in src/R3.Desktop, before committing UI work, or when asked "is this screen OK", "review this view", or "UI review".
---

# R3 WPF UI Reviewer

Review the target file(s) against each R3 skill. Read the actual code; don't guess from file names.

## Checklist

**Architecture** (`wpf-ui-architect`)
- [ ] No SQL / `db.Query` in the view (legacy lookups flagged as "move to service")
- [ ] No business rules in UI; the logic lives in a service with tests
- [ ] File < ~400 lines; MainWindow.xaml.cs untouched except for navigation wiring
- [ ] Context actions via ErpGridContext

**Design system** (`wpf-design-system`)
- [ ] No `Color.FromRgb`, `Brushes.X`, or raw hex in new/changed lines
- [ ] FontSize only from the scale (10.5 / 11 / 11.5 / 13 / 18); none on inputs or grids
- [ ] Margins/padding from the scale (2/4/6/8/12/16/24)
- [ ] No explicit Height on inputs

**DataGrid** (`wpf-datagrid-expert`)
- [ ] AutoGenerateColumns=false, tr-TR formats, numbers right-aligned
- [ ] Loading / empty / error states, and a status line
- [ ] Star-sized main column, frozen columns if wide

**Layout** (`wpf-responsive-layout`)
- [ ] Works at 1366 px width; no grid inside a StackPanel/ScrollViewer
- [ ] Dialogs have an Owner, CenterOwner, and sensible MinWidth

**DPI / Accessibility** (`wpf-dpi-accessibility`)
- [ ] Full keyboard path, initial focus, IsDefault/IsCancel
- [ ] AutomationProperties.Name on icon buttons; status not shown by color alone

**Performance** (`wpf-performance`)
- [ ] Loads async; no race between filter changes; virtualization intact

**ERP UX** (`erp-desktop-ux`)
- [ ] Standard shortcuts, Turkish copy with proper characters, dirty-close confirmation, document status visible

## Output format

```
## UI Review: <View>
Verdict: ✅ OK / ⚠️ fix before merge / ❌ rework

| # | Severity | Area | file:line | Problem | Fix |
|---|---|---|---|---|---|
```

Severity: **High** means broken behavior, data risk, UI freeze, or inaccessible to keyboard users. **Medium** means a standard is violated in a way users notice. **Low** means token or spacing drift.

List at most 15 findings, highest severity first. Then list the refactors required, in order. Leave out generic advice. Every finding must cite a line.
