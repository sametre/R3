---
name: erp-desktop-ux
description: UX rules for Turkish commercial/ERP desktop screens in R3 (stok, cari, satış, satınalma, fatura, depo, kasa/banka, çek-senet, rapor, muhasebe). Use when designing a new ERP workflow or screen, writing Turkish UI copy, deciding keyboard shortcuts, or reviewing whether a screen works for a heavy daily operator.
---

# ERP Desktop UX (R3)

The users are operators who spend the whole day in the app, not occasional visitors. Their priorities are speed, predictability, and keyboard flow. Looking attractive comes after that.

## Principles

1. **Keyboard first.** Every document entry (fatura, irsaliye, sipariş, tahsilat) must be completable without the mouse. Tab order follows reading order. Enter moves to the next field in line-entry grids. Esc cancels or closes, and warns if there are unsaved changes.
2. **Density over whitespace.** Use the compact defaults. Don't add card padding or big headers that push the grid below the fold.
3. **Predictability.** The same action has the same place, name, and shortcut on every screen (Kaydet, Vazgeç, Yeni, Sil, Yazdır, Yenile).
4. **Show state.** Draft / onaylı / iptal / kapalı status is always visible (chip or colored text using the status tokens). Posted documents are clearly read-only.
5. **Never lose work.** Confirm before closing a dirty tab. Destructive actions show what will be affected ("3 fatura iptal edilecek").
6. **Explain numbers.** Computed values (öneri miktarı, bakiye, maliyet) have a tooltip or help line that gives the formula, like the help text in ReorderView.

## Standard shortcuts

| Key | Action |
|---|---|
| F2 | Edit selected / enter edit mode |
| F3 / Ctrl+F | Search |
| F5 | Refresh |
| F9 or Ctrl+S | Save |
| Ctrl+N / Ins | New record / new line |
| Del | Delete (with confirm) |
| Ctrl+P | Print |
| Esc | Cancel / close |
| Enter | Open (in lists) / next field (in entry) |

Check `KeyboardInteractionService` before adding bindings so you don't collide with existing ones.

## Turkish copy

- Use sentence case and short, verb-first buttons: "Kaydet", "Sipariş Oluştur", "Filtreyi Temizle".
- Keep the ERP domain terms the users already know: cari, stok kartı, irsaliye, tahsilat, tediye, çek/senet, depo, lot/seri. Don't translate them into generic words.
- Error messages say what happened and what to do next: "Depo seçilmedi. Satırı kaydetmek için bir depo seçin."
- Numbers and dates use tr-TR format (1.234,56 · 23.09.2026).
- Always use the proper Turkish characters (ı, İ, ş, ğ, ü, ö, ç). No ASCII-only substitutes in UI text.

## Screen archetypes

- **List screen**: title, help line, filter bar, grid, status line. Context menu for actions. (See `wpf-datagrid-expert`.)
- **Document entry** (fatura/sipariş): header fields on top (cari, tarih, depo, belge no), line grid in the middle, totals footer at the bottom right, and actions on the bottom bar.
- **Card screen** (stok/cari kartı): tabs for sections, with summary info always visible in the header.
- **Report**: parameters on top, then the result grid, then export and print.

## Legacy alignment

Operators are migrating from the legacy ASB system. When a screen replaces a legacy one (see `LegacyAlignedViews.cs`), keep the familiar field order and names unless there's a strong reason not to.
