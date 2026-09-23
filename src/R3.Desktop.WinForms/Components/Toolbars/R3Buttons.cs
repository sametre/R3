using Krypton.Toolkit;
using R3.Desktop.WinForms.Design;

namespace R3.Desktop.WinForms.Components.Toolbars;

/// <summary>Secondary (default) command button: Krypton standalone look, token font and height.</summary>
public class R3SecondaryButton : KryptonButton
{
    private readonly ToolTip? _tip;

    public R3SecondaryButton(string text, Action? onClick = null, string? toolTip = null)
    {
        Values.Text = text;
        // KryptonButton's AutoSize under-reports its width inside FlowLayoutPanels (labels overlap): size from the text.
        AutoSize = false;
        StateCommon.Content.ShortText.Font = AppTypography.Body;
        Size = new Size(Math.Max(AppSizes.ButtonMinWidth, TextRenderer.MeasureText(text.Replace("&", ""), AppTypography.BodyStrong).Width + 2 * AppSpacing.MD + AppSpacing.SM), AppSizes.ButtonHeight);
        StateCommon.Content.Padding = new Padding(AppSpacing.SM, 0, AppSpacing.SM, 0);
        AccessibleName = text.Replace("&", "");
        if (toolTip != null) { _tip = new ToolTip(); _tip.SetToolTip(this, toolTip); AccessibleDescription = toolTip; }
        if (onClick != null) Click += (_, _) => onClick();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip?.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>The one primary action on a screen/dialog: accent fill, white text.</summary>
public sealed class R3PrimaryButton : R3SecondaryButton
{
    public R3PrimaryButton(string text, Action? onClick = null, string? toolTip = null) : base(text, onClick, toolTip)
    {
        foreach (var state in new[] { StateNormal, StateTracking, StatePressed })
        {
            state.Back.Color1 = state == StateNormal ? AppColors.Accent : AppColors.AccentHover;
            state.Back.Color2 = state.Back.Color1;
            state.Border.Color1 = state.Back.Color1; state.Border.Color2 = state.Back.Color1;
            state.Content.ShortText.Color1 = AppColors.TextOnAccent; state.Content.ShortText.Color2 = AppColors.TextOnAccent;
        }
        StateCommon.Content.ShortText.Font = AppTypography.BodyStrong;
    }
}
