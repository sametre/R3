using System.Globalization;
using Krypton.Toolkit;

namespace R3.Desktop.WinForms.Design;

/// <summary>
/// The one theme entry point. Krypton's Microsoft 365 Silver palette gives the Office-style gray look to menus,
/// toolstrips, buttons, inputs and grids; R3 components add the token colors on top where meaning is carried
/// (accent, status, grid). Screens never pick a palette or color themselves.
/// Windows are plain Forms (native Windows 11 title bar): KryptonForm 105.26 moves children into an internal
/// panel whose handle is never created, leaving the window empty (see ShellTests).
/// </summary>
public static class AppTheme
{
    public const PaletteMode Palette = PaletteMode.Microsoft365Silver;
    private static KryptonManager? _manager;

    /// <summary>Call once at startup, before the first form is created.</summary>
    public static void Apply()
    {
        _manager ??= new KryptonManager { GlobalPaletteMode = Palette, GlobalApplyToolstrips = true };
    }
}

/// <summary>Turkish display formats (tr-TR): 1.500,00 · ₺1.500,00 · 23.09.2026.</summary>
public static class AppFormats
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("tr-TR");
    public const string Quantity = "N2";
    public const string Count = "N0";
    public const string Money = "C2";
    public const string Date = "dd.MM.yyyy";
    public const string DateTime = "dd.MM.yyyy HH:mm";

    public static string Number(decimal value, string format = Quantity) => value.ToString(format, Culture);
}
