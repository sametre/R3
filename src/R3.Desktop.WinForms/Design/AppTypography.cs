namespace R3.Desktop.WinForms.Design;

/// <summary>
/// Type scale (Segoe UI). Pixel targets: Caption 12, Body 13, Section 16, Title 19, PageTitle 22 - expressed in
/// points (px × 0.75) because WinForms fonts are point-based. These are the only fonts in the app: shared GDI
/// objects, never created or disposed elsewhere.
/// </summary>
public static class AppTypography
{
    private const string Family = "Segoe UI";
    private const string SemiboldFamily = "Segoe UI Semibold";

    public static readonly Font Caption = new(Family, 9f);                 // 12px - help lines, status, badges
    public static readonly Font Body = new(Family, 9.75f);                 // 13px - inputs, buttons, labels, grid cells
    public static readonly Font BodyStrong = new(SemiboldFamily, 9.75f);   // 13px - grid headers, emphasis
    public static readonly Font Section = new(SemiboldFamily, 12f);        // 16px - section headings
    public static readonly Font Title = new(SemiboldFamily, 14.25f);       // 19px - dialog titles
    public static readonly Font PageTitle = new(Family, 16.5f);            // 22px - view title

    /// <summary>Segoe Fluent Icons (Windows 11) with MDL2 Assets fallback, for <see cref="AppIcons"/> glyphs.</summary>
    public static readonly Font Icon = new(IconFamily(), 10.5f);
    public static readonly Font IconSmall = new(Icon.FontFamily, 7f);    // tab close glyph

    private static string IconFamily()
    {
        using var installed = new System.Drawing.Text.InstalledFontCollection();
        return installed.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }
}
