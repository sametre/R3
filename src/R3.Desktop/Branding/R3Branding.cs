using System.Reflection;

namespace R3.Desktop.Branding;

internal static class R3Branding
{
    private const string IconResource = "R3.Desktop.Assets.Branding.R3.ico";
    private const string LogoResource = "R3.Desktop.Assets.Branding.R3_Logo.png";

    public static Icon AppIcon { get; } = LoadIcon();
    public static Image Logo { get; } = LoadImage();

    private static Icon LoadIcon()
    {
        using Stream stream = GetResource(IconResource);
        return new Icon(stream);
    }

    private static Image LoadImage()
    {
        using Stream stream = GetResource(LogoResource);
        using Image source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static Stream GetResource(string resourceName) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException($"Kurumsal kaynak bulunamadı: {resourceName}");
}
