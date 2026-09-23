using R3.Desktop.WinForms.Design;

namespace R3.Desktop.WinForms.Components.Toolbars;

/// <summary>Command row at the top of a list: buttons left-to-right, a thin separator between groups.</summary>
public sealed class R3Toolbar : FlowLayoutPanel
{
    private readonly ToolTip _tips = new();

    public R3Toolbar()
    {
        Dock = DockStyle.Top; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; WrapContents = true;
        BackColor = AppColors.Surface; Padding = new Padding(AppSpacing.SM, AppSpacing.XS, AppSpacing.SM, AppSpacing.XS); Margin = Padding.Empty;
    }

    public T Add<T>(T button, string? toolTip = null) where T : Control
    {
        button.Margin = new Padding(0, AppSpacing.XS, AppSpacing.XS, AppSpacing.XS);
        if (toolTip != null) _tips.SetToolTip(button, toolTip);
        Controls.Add(button);
        return button;
    }

    public void AddSeparator() =>
        Controls.Add(new Panel { Width = 1, Height = AppSizes.ButtonHeight - AppSpacing.SM, BackColor = AppColors.Border, Margin = new Padding(AppSpacing.XS, AppSpacing.SM, AppSpacing.SM, AppSpacing.XS) });

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }
}
