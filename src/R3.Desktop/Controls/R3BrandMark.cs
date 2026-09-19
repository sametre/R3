using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace R3.Desktop.Controls;

/// <summary>Şeffaf, ölçeklenebilir R3 + atom yörüngesi marka işareti.</summary>
public sealed class R3BrandMark : Viewbox
{
    private readonly TextBlock _monogram;
    private readonly Ellipse[] _orbits;
    private readonly Ellipse _nucleus;

    public static readonly DependencyProperty MarkBrushProperty = DependencyProperty.Register(
        nameof(MarkBrush), typeof(Brush), typeof(R3BrandMark),
        new PropertyMetadata(new SolidColorBrush(Color.FromRgb(23, 61, 86)), OnBrushChanged));

    public static readonly DependencyProperty OrbitBrushProperty = DependencyProperty.Register(
        nameof(OrbitBrush), typeof(Brush), typeof(R3BrandMark),
        new PropertyMetadata(new SolidColorBrush(Color.FromRgb(42, 143, 146)), OnBrushChanged));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(Brush), typeof(R3BrandMark),
        new PropertyMetadata(new SolidColorBrush(Color.FromRgb(214, 146, 46)), OnBrushChanged));

    public Brush MarkBrush { get => (Brush)GetValue(MarkBrushProperty); set => SetValue(MarkBrushProperty, value); }
    public Brush OrbitBrush { get => (Brush)GetValue(OrbitBrushProperty); set => SetValue(OrbitBrushProperty, value); }
    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    public R3BrandMark()
    {
        Stretch = Stretch.Uniform;
        StretchDirection = StretchDirection.Both;

        var canvas = new Canvas { Width = 180, Height = 120, Background = Brushes.Transparent };
        _orbits =
        [
            CreateOrbit(canvas, 0),
            CreateOrbit(canvas, 60),
            CreateOrbit(canvas, -60)
        ];

        _nucleus = new Ellipse { Width = 9, Height = 9, Fill = AccentBrush };
        Canvas.SetLeft(_nucleus, 85.5); Canvas.SetTop(_nucleus, 55.5); canvas.Children.Add(_nucleus);

        _monogram = new TextBlock
        {
            Text = "R3", FontFamily = new FontFamily("Segoe UI Black"), FontWeight = FontWeights.Black,
            FontStyle = FontStyles.Italic, FontSize = 67, Foreground = MarkBrush,
            RenderTransform = new SkewTransform(-4, 0), RenderTransformOrigin = new Point(.5, .5)
        };
        Canvas.SetLeft(_monogram, 39); Canvas.SetTop(_monogram, 21); canvas.Children.Add(_monogram);
        Child = canvas;
    }

    private Ellipse CreateOrbit(Canvas canvas, double angle)
    {
        var orbit = new Ellipse
        {
            Width = 132, Height = 43, Stroke = OrbitBrush, StrokeThickness = 3,
            Opacity = .82, Fill = Brushes.Transparent,
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(angle)
        };
        Canvas.SetLeft(orbit, 24); Canvas.SetTop(orbit, 38.5); canvas.Children.Add(orbit);
        return orbit;
    }

    private static void OnBrushChanged(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        if (source is not R3BrandMark mark || mark._monogram == null) return;
        mark._monogram.Foreground = mark.MarkBrush;
        foreach (var orbit in mark._orbits) orbit.Stroke = mark.OrbitBrush;
        mark._nucleus.Fill = mark.AccentBrush;
    }
}
