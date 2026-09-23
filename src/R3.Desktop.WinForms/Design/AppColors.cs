namespace R3.Desktop.WinForms.Design;

/// <summary>
/// AR3 color tokens. The only place a color value is written: views and controls use these semantic names.
/// Neutral gray/white (SAP Fiori-like) with one teal accent; status colors carry meaning and stay saturated.
/// Same values as the legacy WPF app's Design/Tokens.xaml so both apps look alike during the migration.
/// </summary>
public static class AppColors
{
    // Surfaces
    public static readonly Color Background = Hex(0xEEF0F2);      // page canvas behind cards and grids
    public static readonly Color Surface = Hex(0xFFFFFF);         // cards, grids, inputs
    public static readonly Color SurfaceAlt = Hex(0xF7F8F9);      // toolbars, filter bars, read-only fields
    public static readonly Color SurfaceHover = Hex(0xF0F1F2);
    public static readonly Color SurfaceSelected = Hex(0xDCDFE3);

    // Borders
    public static readonly Color Border = Hex(0xD6DEE5);
    public static readonly Color BorderStrong = Hex(0xBFC8D0);

    // Text
    public static readonly Color TextPrimary = Hex(0x252A30);
    public static readonly Color TextSecondary = Hex(0x5B6773);   // help lines, labels, status (4.9:1 on white)
    public static readonly Color TextMuted = Hex(0x6B7680);       // captions, placeholders (4.5:1 on white)
    public static readonly Color TextOnAccent = Hex(0xFFFFFF);

    // Accent + status; *Soft = tinted background behind same-meaning text
    public static readonly Color Accent = Hex(0x167C82);
    public static readonly Color AccentHover = Hex(0x12686D);
    public static readonly Color AccentSoft = Hex(0xE7F3F3);
    public static readonly Color Success = Hex(0x15803D);
    public static readonly Color SuccessSoft = Hex(0xEAF5EE);
    public static readonly Color Warning = Hex(0xB45309);
    public static readonly Color WarningSoft = Hex(0xFFF4E5);
    public static readonly Color Danger = Hex(0xC0392B);
    public static readonly Color DangerSoft = Hex(0xFBEEEE);
    public static readonly Color Info = Hex(0x2B5870);
    public static readonly Color InfoSoft = Hex(0xE8F1F6);

    // Data grid
    public static readonly Color GridHeader = Hex(0xEEEFF1);
    public static readonly Color GridHeaderText = Hex(0x33383E);
    public static readonly Color GridLine = Hex(0xDDE2E6);
    public static readonly Color GridCellText = Hex(0x263746);
    public static readonly Color GridRowAlt = Hex(0xF8F9FA);

    // Shell
    public static readonly Color Chrome = Hex(0xFFFFFF);          // menu bar
    public static readonly Color ChromeAccentLine = Hex(0x167C82);
    public static readonly Color StatusBar = Hex(0x394149);
    public static readonly Color StatusBarText = Hex(0xD0D5DD);

    private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
}
