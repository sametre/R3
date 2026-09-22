using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace R3.Desktop.Controls;

/// <summary>Renkli, ölçeklenebilir AR3 marka işareti.</summary>
public sealed class R3BrandMark : Viewbox
{
    private readonly TextBlock _monogram;
    private readonly Border _badge;

    public static readonly DependencyProperty MarkBrushProperty = DependencyProperty.Register(
        nameof(MarkBrush), typeof(Brush), typeof(R3BrandMark),
        new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x3D, 0x47, 0x50)), OnBrushChanged));

    public static readonly DependencyProperty OrbitBrushProperty = DependencyProperty.Register(
        nameof(OrbitBrush), typeof(Brush), typeof(R3BrandMark),
        new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x74, 0xE0, 0xD0)), OnBrushChanged));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(Brush), typeof(R3BrandMark),
        new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0x5B)), OnBrushChanged));

    public Brush MarkBrush { get => (Brush)GetValue(MarkBrushProperty); set => SetValue(MarkBrushProperty, value); }
    public Brush OrbitBrush { get => (Brush)GetValue(OrbitBrushProperty); set => SetValue(OrbitBrushProperty, value); }
    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    public R3BrandMark()
    {
        Stretch = Stretch.Uniform;
        StretchDirection = StretchDirection.Both;

        var canvas = new Canvas { Width = 180, Height = 120, Background = Brushes.Transparent };
        _badge = new Border
        {
            Width = 116, Height = 92, CornerRadius = new CornerRadius(26),
            BorderBrush = OrbitBrush, BorderThickness = new Thickness(2),
            Background = new LinearGradientBrush(Color.FromRgb(0x26, 0x31, 0x38), Color.FromRgb(0x52, 0x60, 0x69), 35)
        };
        Canvas.SetLeft(_badge, 32); Canvas.SetTop(_badge, 14); canvas.Children.Add(_badge);

        var dot = new Ellipse { Width = 12, Height = 12, Fill = AccentBrush };
        Canvas.SetLeft(dot, 135); Canvas.SetTop(dot, 18); canvas.Children.Add(dot);

        _monogram = new TextBlock
        {
            Text = "AR3", FontFamily = new FontFamily("Segoe UI Black"), FontWeight = FontWeights.Black,
            FontStyle = FontStyles.Italic, FontSize = 49, Foreground = Brushes.White,
            RenderTransform = new SkewTransform(-4, 0), RenderTransformOrigin = new Point(.5, .5)
        };
        Canvas.SetLeft(_monogram, 42); Canvas.SetTop(_monogram, 33); canvas.Children.Add(_monogram);
        Child = canvas;
    }

    private static void OnBrushChanged(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        if (source is not R3BrandMark mark || mark._monogram == null) return;
        mark._badge.BorderBrush = mark.OrbitBrush;
    }
}
