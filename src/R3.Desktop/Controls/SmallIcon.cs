using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace R3.Desktop.Controls;

/// <summary>Shared 16-DIP icon for WPF toolbars and shell controls.</summary>
public sealed class SmallIcon : Image
{
    private static readonly Dictionary<string, BitmapSource> Images = new();
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(SmallIcon),
        new PropertyMetadata("Document", (d, _) => ((SmallIcon)d).UpdateSource()));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }

    public SmallIcon()
    {
        Width = Height = 16;
        Margin = new Thickness(0, 0, 6, 0);
        VerticalAlignment = VerticalAlignment.Center;
        var style = new Style(typeof(Image));
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(OpacityProperty, 0.4)); style.Triggers.Add(disabled); Style = style;
        UpdateSource();
    }

    private void UpdateSource()
    {
        lock (Images)
        {
            if (!Images.TryGetValue(Kind, out var source))
            {
                var kind = Enum.TryParse<DesktopMenuIcons.Kind>(Kind, out var parsed) ? parsed : DesktopMenuIcons.Kind.Document;
                using var bitmap = DesktopMenuIcons.Create(kind);
                using var stream = new MemoryStream(); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png); stream.Position = 0;
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
                Images[Kind] = source = image;
            }
            Source = source;
        }
    }
}
