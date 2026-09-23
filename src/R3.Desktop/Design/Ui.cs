using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace R3.Desktop.Design;

/// <summary>
/// Design kit for code-built screens. Colors and type sizes come from Design/Tokens.xaml (same keys the
/// XAML views use), spacing from <see cref="Space"/>. Building blocks here are the standard R3 screen
/// pieces - page header, filter-bar label, primary button, empty state, grid host with loading overlay -
/// so a view composes them instead of re-inventing margins and colors.
/// </summary>
public static class Ui
{
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Spacing scale. View margin = XL, between stacked regions = M, label→control = SM, between groups = L.</summary>
    public static class Space
    {
        public const double XS = 2, S = 4, SM = 6, M = 8, L = 12, XL = 16, XXL = 24;
    }

    /// <summary>Type scale. Inputs, buttons and grids size themselves through implicit styles - only set these on text.</summary>
    public static class Font
    {
        public const double Kpi = 22, Title = 18, Section = 13, Body = 11.5, Caption = 11, Grid = 10.5;
    }

    private static ResourceDictionary? _tokens;

    /// <summary>A token brush from Design/Tokens.xaml, e.g. <c>Ui.Brush("R3.Text.Secondary.Brush")</c>. Works without a
    /// running Application too (unit tests), by loading the token dictionary directly.</summary>
    public static Brush Brush(string key)
    {
        if (System.Windows.Application.Current?.TryFindResource(key) is Brush fromApp) return fromApp;
        _tokens ??= (ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/R3.Desktop;component/Design/Tokens.xaml", UriKind.Relative));
        return _tokens[key] as Brush ?? throw new KeyNotFoundException($"Design token '{key}' is not defined in Design/Tokens.xaml.");
    }

    public static Brush TextPrimary => Brush("R3.Text.Primary.Brush");
    public static Brush TextSecondary => Brush("R3.Text.Secondary.Brush");
    public static Brush Border => Brush("R3.Border.Brush");
    public static Brush Surface => Brush("R3.Surface.Brush");

    /// <summary>Title + optional one-line help, docked at the top of a view.</summary>
    public static StackPanel PageHeader(string title, string? help = null)
    {
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, Space.M) };
        header.Children.Add(new TextBlock { Text = title, FontSize = Font.Title, FontWeight = FontWeights.SemiBold, Foreground = TextPrimary });
        if (!string.IsNullOrWhiteSpace(help))
            header.Children.Add(HelpText(help, new Thickness(0, Space.XS, 0, 0)));
        return header;
    }

    public static TextBlock SectionTitle(string text) =>
        new() { Text = text, FontSize = Font.Section, FontWeight = FontWeights.SemiBold, Foreground = TextPrimary, Margin = new Thickness(0, Space.L, 0, Space.SM) };

    public static TextBlock HelpText(string text, Thickness? margin = null) =>
        new() { Text = text, FontSize = Font.Caption, Foreground = TextSecondary, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0) };

    /// <summary>Filter-bar caption. The first label in a bar gets no left gap; later ones separate groups by L.</summary>
    public static TextBlock FieldLabel(string text, bool first = false) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Foreground = TextSecondary, Margin = new Thickness(first ? 0 : Space.L, 0, Space.SM, 0) };

    /// <summary>Status line text (counts, totals) - secondary color, vertically centered in a bar.</summary>
    public static TextBlock StatusText(string text = "") =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Foreground = TextSecondary, Margin = new Thickness(Space.L, 0, 0, 0) };

    public static Button PrimaryButton(string text, Action onClick)
    {
        var button = Button(text, onClick);
        button.Background = Brush("R3.Accent.Brush"); button.Foreground = Brush("R3.Text.OnAccent.Brush"); button.BorderThickness = new Thickness(0);
        return button;
    }

    public static Button Button(string text, Action onClick)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, Space.SM, 0) };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>White bordered card (the XAML CardStyle equivalent).</summary>
    public static Border Card(UIElement child, Thickness? padding = null) =>
        new() { Background = Surface, BorderBrush = Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = padding ?? new Thickness(Space.L), Child = child };

    /// <summary>Centered "nothing here" message laid over a grid; toggle with <see cref="UIElement.Visibility"/>.</summary>
    public static TextBlock EmptyState(string text) =>
        new() { Text = text, Foreground = TextSecondary, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Visibility = Visibility.Collapsed };

    /// <summary>Stacks a grid (or any content) with its empty-state text and a loading overlay.</summary>
    public static Grid Layered(params UIElement[] layers)
    {
        var host = new Grid();
        foreach (var layer in layers) host.Children.Add(layer);
        return host;
    }

    /// <summary>Colored text + soft background chip for a status ("Onaylı", "Taslak", "İptal").</summary>
    public static Border StatusChip(string text, StatusKind kind)
    {
        var (fg, bg) = kind switch
        {
            StatusKind.Success => ("R3.Success.Brush", "R3.Success.Soft.Brush"),
            StatusKind.Warning => ("R3.Warning.Brush", "R3.Warning.Soft.Brush"),
            StatusKind.Danger => ("R3.Danger.Brush", "R3.Danger.Soft.Brush"),
            StatusKind.Info => ("R3.Info.Brush", "R3.Info.Soft.Brush"),
            _ => ("R3.Text.Secondary.Brush", "R3.Surface.Alt.Brush")
        };
        return new Border { Background = Brush(bg), CornerRadius = new CornerRadius(4), Padding = new Thickness(Space.SM, Space.XS, Space.SM, Space.XS), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = text, Foreground = Brush(fg), FontSize = Font.Caption, FontWeight = FontWeights.SemiBold } };
    }

    private static readonly Style RightAligned = CreateRightAligned();
    private static Style CreateRightAligned()
    {
        var style = new Style(typeof(TextBlock), DataGridTextColumn.DefaultElementStyle);
        style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        style.Seal();
        return style;
    }

    /// <summary>Read-only text column.</summary>
    public static DataGridTextColumn TextColumn(string header, string path, double width) =>
        new() { Header = header, Binding = new Binding(path), Width = width, IsReadOnly = true };

    /// <summary>Right-aligned tr-TR number column (N2 amounts/quantities, N0 counts).</summary>
    public static DataGridTextColumn NumberColumn(string header, string path, double width, string format = "N2") =>
        new() { Header = header, Binding = new Binding(path) { StringFormat = format, ConverterCulture = Turkish }, Width = width, IsReadOnly = true, ElementStyle = RightAligned };

    /// <summary>dd.MM.yyyy date column.</summary>
    public static DataGridTextColumn DateColumn(string header, string path, double width = 90) =>
        new() { Header = header, Binding = new Binding(path) { StringFormat = "dd.MM.yyyy", ConverterCulture = Turkish }, Width = width, IsReadOnly = true };
}

public enum StatusKind { Neutral, Success, Warning, Danger, Info }
