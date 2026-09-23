using Krypton.Toolkit;
using R3.Desktop.WinForms.Design;
using R3.Desktop.WinForms.Features.Shared;

namespace R3.Desktop.WinForms.Components.Editors;

/// <summary>
/// A titled block of a data-entry form: two label/field column pairs that collapse naturally with the window width.
/// Tab order follows reading order (left pair, then right pair, row by row).
/// </summary>
public sealed class R3FormSection : TableLayoutPanel
{
    private int _row, _column;
    private readonly ToolTip _tips = new();

    public R3FormSection(string title)
    {
        Dock = DockStyle.Top; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; ColumnCount = 4;
        BackColor = AppColors.Surface; Padding = new Padding(AppSpacing.LG, AppSpacing.MD, AppSpacing.LG, AppSpacing.MD); Margin = Padding.Empty;
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var header = new Label { Text = title, Font = AppTypography.Section, ForeColor = AppColors.TextPrimary, AutoSize = true, Margin = new Padding(0, 0, 0, AppSpacing.SM) };
        Controls.Add(header, 0, 0); SetColumnSpan(header, 4);
        _row = 1;
    }

    /// <summary>Adds a label + field in the next free half-row. <paramref name="wide"/> spans both halves.</summary>
    public T Add<T>(string label, T field, bool required = false, string? toolTip = null, bool wide = false) where T : Control
    {
        if (wide && _column != 0) { _row++; _column = 0; }
        var caption = new Label
        {
            Text = required ? label + " *" : label, AutoSize = true, Font = AppTypography.Body,
            ForeColor = AppColors.TextSecondary, Anchor = AnchorStyles.Left, Margin = new Padding(_column == 0 ? 0 : AppSpacing.LG, AppSpacing.SM, AppSpacing.SM, 0)
        };
        field.Margin = new Padding(0, AppSpacing.XS, 0, AppSpacing.XS);
        field.Anchor = AnchorStyles.Left | AnchorStyles.Right; field.AccessibleName = label;
        if (toolTip != null) { _tips.SetToolTip(field, toolTip); _tips.SetToolTip(caption, toolTip); }
        Controls.Add(caption, _column, _row); Controls.Add(field, _column + 1, _row);
        if (wide) { SetColumnSpan(field, 3); _row++; _column = 0; }
        else if (_column == 0) _column = 2;
        else { _column = 0; _row++; }
        return field;
    }

    /// <summary>Check boxes in a wrapping row across the whole section.</summary>
    public void AddFlags(params KryptonCheckBox[] flags)
    {
        if (_column != 0) { _row++; _column = 0; }
        var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, AppSpacing.XS, 0, 0) };
        foreach (var flag in flags) { flag.Margin = new Padding(0, AppSpacing.XS, AppSpacing.LG, AppSpacing.XS); row.Controls.Add(flag); }
        Controls.Add(row, 0, _row); SetColumnSpan(row, 4); _row++;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Editor factories with token fonts and tr-TR numeric formatting.</summary>
public static class R3Editors
{
    public static KryptonTextBox Text(string value = "", int maxLength = 200)
    {
        var box = new KryptonTextBox { Text = value, MaxLength = maxLength, MinimumSize = new Size(120, 0) };
        box.StateCommon.Content.Font = AppTypography.Body;
        return box;
    }

    /// <summary>Right-aligned decimal with thousands separator (culture = tr-TR thread culture).</summary>
    public static KryptonNumericUpDown Number(decimal value, int decimals = 2, decimal max = 999_999_999m)
    {
        var box = new KryptonNumericUpDown { DecimalPlaces = decimals, ThousandsSeparator = true, Minimum = 0, Maximum = max, TextAlign = HorizontalAlignment.Right, Width = 140 };
        box.Value = Math.Clamp(value, 0, max);
        return box;
    }

    public static KryptonCheckBox Check(string text, bool value)
    {
        var box = new KryptonCheckBox { Text = text, Checked = value, AutoSize = true, AccessibleName = text };
        box.StateCommon.ShortText.Font = AppTypography.Body;
        return box;
    }

    /// <summary>Lookup combo; <paramref name="optional"/> adds a "(Seçilmedi)" entry with an empty id.</summary>
    public static KryptonComboBox Lookup(IEnumerable<LookupItem> items, string? selectedId, bool optional)
    {
        var list = (optional ? new[] { new LookupItem("", "", "(Seçilmedi)") } : []).Concat(items).ToList();
        if (!string.IsNullOrEmpty(selectedId) && list.All(i => i.Id != selectedId)) list.Add(new LookupItem(selectedId, "", "(pasif kayıt)"));
        var combo = new KryptonComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(LookupItem.Text), ValueMember = nameof(LookupItem.Id), MinimumSize = new Size(160, 0) };
        combo.StateCommon.ComboBox.Content.Font = AppTypography.Body;
        combo.DataSource = list;
        combo.SelectedValue = selectedId ?? "";
        if (combo.SelectedIndex < 0 && list.Count > 0) combo.SelectedIndex = 0;
        return combo;
    }

    public static KryptonComboBox Choice((string Value, string Text)[] items, string? selected)
    {
        var combo = Lookup(items.Select(i => new LookupItem(i.Value, "", i.Text)), selected, optional: false);
        return combo;
    }

    public static string Value(KryptonComboBox combo) => combo.SelectedValue as string ?? "";
}
