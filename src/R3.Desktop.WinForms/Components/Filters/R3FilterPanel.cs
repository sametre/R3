using Krypton.Toolkit;
using R3.Desktop.WinForms.Design;

namespace R3.Desktop.WinForms.Components.Filters;

/// <summary>
/// Filter row under a list toolbar: label + control pairs and toggles that wrap on narrow windows.
/// Spacing comes from tokens (label→control XS, between groups MD); screens only add fields.
/// </summary>
public sealed class R3FilterPanel : FlowLayoutPanel
{
    public R3FilterPanel()
    {
        Dock = DockStyle.Top; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; WrapContents = true;
        BackColor = AppColors.SurfaceAlt; Padding = new Padding(AppSpacing.SM, AppSpacing.XS, AppSpacing.SM, AppSpacing.XS); Margin = Padding.Empty;
    }

    public T AddField<T>(string label, T control) where T : Control
    {
        Controls.Add(new Label
        {
            Text = label, AutoSize = true, Font = AppTypography.Body, ForeColor = AppColors.TextSecondary,
            Margin = new Padding(Controls.Count == 0 ? 0 : AppSpacing.MD, AppSpacing.SM, AppSpacing.XS, 0)
        });
        control.Margin = new Padding(0, AppSpacing.XS, 0, AppSpacing.XS);
        if (string.IsNullOrEmpty(control.AccessibleName)) control.AccessibleName = label.TrimEnd(':');
        Controls.Add(control);
        return control;
    }

    public KryptonCheckBox AddToggle(string text)
    {
        var box = new KryptonCheckBox { Text = text, AutoSize = true, Margin = new Padding(AppSpacing.MD, AppSpacing.SM, 0, 0) };
        box.StateCommon.ShortText.Font = AppTypography.Body;
        box.AccessibleName = text;
        Controls.Add(box);
        return box;
    }

    /// <summary>Drop-down of (value, text) pairs; the first item is the "all" option.</summary>
    public static KryptonComboBox Choice(params (string? Value, string Text)[] items)
    {
        var combo = new KryptonComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = AppSizes.FilterComboWidth, DisplayMember = "Text", ValueMember = "Value" };
        combo.StateCommon.ComboBox.Content.Font = AppTypography.Body;
        combo.DataSource = items.Select(i => new FilterItem(i.Value, i.Text)).ToList();
        return combo;
    }

    public sealed record FilterItem(string? Value, string Text);
}
