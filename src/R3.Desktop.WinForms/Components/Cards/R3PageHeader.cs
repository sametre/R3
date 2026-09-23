using R3.Desktop.WinForms.Design;

namespace R3.Desktop.WinForms.Components.Cards;

/// <summary>View title + one-line help, docked at the top of a view.</summary>
public sealed class R3PageHeader : TableLayoutPanel
{
    public R3PageHeader(string title, string? help = null)
    {
        Dock = DockStyle.Top; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; ColumnCount = 1;
        Padding = new Padding(0, 0, 0, AppSpacing.SM); Margin = Padding.Empty; BackColor = AppColors.Background;
        Controls.Add(new Label { Text = title, Font = AppTypography.PageTitle, ForeColor = AppColors.TextPrimary, AutoSize = true, Margin = Padding.Empty });
        if (!string.IsNullOrWhiteSpace(help))
            Controls.Add(new Label { Text = help, Font = AppTypography.Caption, ForeColor = AppColors.TextSecondary, AutoSize = true, Margin = new Padding(0, AppSpacing.XS / 2, 0, 0) });
    }
}

/// <summary>Status text with a tinted background - meaning is in the text, color only reinforces it.</summary>
public sealed class R3StatusBadge : Label
{
    public R3StatusBadge(string text, StatusKind kind)
    {
        Text = text; AutoSize = true; Font = AppTypography.Caption; Padding = new Padding(AppSpacing.XS, 1, AppSpacing.XS, 1);
        (ForeColor, BackColor) = Colors(kind);
    }

    public static (Color Fore, Color Back) Colors(StatusKind kind) => kind switch
    {
        StatusKind.Success => (AppColors.Success, AppColors.SuccessSoft),
        StatusKind.Warning => (AppColors.Warning, AppColors.WarningSoft),
        StatusKind.Danger => (AppColors.Danger, AppColors.DangerSoft),
        StatusKind.Info => (AppColors.Info, AppColors.InfoSoft),
        _ => (AppColors.TextSecondary, AppColors.SurfaceAlt)
    };
}

public enum StatusKind { Neutral, Success, Warning, Danger, Info }
