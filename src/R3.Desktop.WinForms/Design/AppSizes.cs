namespace R3.Desktop.WinForms.Design;

/// <summary>Standard control sizes (96-DPI logical pixels) so bars and grids line up across screens.</summary>
public static class AppSizes
{
    public const int InputHeight = 26;
    public const int ButtonHeight = 28;
    public const int ButtonMinWidth = 72;
    public const int ToolbarHeight = 40;
    public const int SearchBoxWidth = 280;
    public const int FilterComboWidth = 160;

    public static int GridRowHeight(GridDensity density) => density switch
    {
        GridDensity.Compact => 26,
        GridDensity.Comfortable => 36,
        _ => 30
    };

    public static int GridHeaderHeight(GridDensity density) => density == GridDensity.Compact ? 28 : 32;
}

/// <summary>Grid row density; ERP default is Compact.</summary>
public enum GridDensity { Compact, Normal, Comfortable }
