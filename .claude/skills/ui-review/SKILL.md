---
name: ui-review
description: Full UI audit-then-fix pipeline for one R3.Desktop screen, e.g. "/ui-review ReorderView". Chains architect → design system → datagrid → layout → DPI/a11y → performance → ERP UX → reviewer, reports findings, then fixes them. Use when the user runs /ui-review or asks for a full UI audit and cleanup of a specific view.
argument-hint: <ViewName or path>
---

# /ui-review <target>

1. **Locate** the target in `src/R3.Desktop` (`Views/<target>.cs`, `.xaml`, `.xaml.cs`, its ViewModel, and the service it calls). Read all of them fully.
2. **Load the standards**: read the SKILL.md for `wpf-ui-architect`, `wpf-design-system`, `wpf-datagrid-expert` (if the view has a grid), `wpf-responsive-layout`, `wpf-dpi-accessibility`, `wpf-performance`, `erp-desktop-ux`. If the view is a key workflow, also apply `ux-heuristics` (Nielsen) to the flow.
3. **Audit** using the `wpf-ui-reviewer` checklist and output format. Group findings under: Architecture · Design tokens · Typography/Spacing · DataGrid · Layout · DPI/Accessibility · Performance · ERP UX. Every finding cites file:line.
4. **Plan the fixes**: the High and Medium findings, ordered. Call out anything that changes behavior (e.g. moving SQL into a service) separately from purely visual changes.
5. **Fix** unless the user asked for audit-only. Keep the diff focused on the target view (and a new service method or token if needed). Don't reformat unrelated code. Note that the working tree may contain another agent's uncommitted changes, so touch only the files you need.
6. **Verify**: `dotnet build src/R3.Desktop` and run the relevant tests (`dotnet test tests/R3.Domain.Tests` / `R3.Desktop.Tests`). If possible, launch the app and open the screen (use the `run` skill). Report which checks you actually ran and their results.
7. **Summarize**: fixed findings, deferred findings with the reason, and any follow-ups.
