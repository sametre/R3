using Krypton.Toolkit;

namespace R3.Desktop.Theme;

internal static class R3Theme
{
    public static KryptonManager Manager { get; } = new();
    public static readonly Color Accent = R3Colors.Primary;
    public static readonly Color AccentHover = R3Colors.PrimaryHover;
    public static readonly Color Canvas = R3Colors.Canvas;
    public static readonly Color Surface = R3Colors.Surface;
    public static readonly Color Border = R3Colors.Border;
    public static readonly Color Text = R3Colors.Text;
    public static readonly Color MutedText = R3Colors.MutedText;
    public static readonly Font UiFont = new("Segoe UI", 9F);

    public static void Apply()
    {
        Manager.GlobalPaletteMode = PaletteMode.Office2013White;
    }

    public static KryptonButton CreatePrimaryButton(string text)
    {
        var button = new KryptonButton
        {
            Text = text,
            Height = 36,
            Width = 108,
            Margin = new Padding(6)
        };
        button.StateCommon.Back.Color1 = Accent;
        button.StateCommon.Back.Color2 = Accent;
        button.StateCommon.Border.Color1 = Accent;
        button.StateCommon.Border.Rounding = 6;
        button.StateCommon.Content.ShortText.Color1 = Color.White;
        button.StateTracking.Back.Color1 = AccentHover;
        button.StateTracking.Back.Color2 = AccentHover;
        return button;
    }
}
