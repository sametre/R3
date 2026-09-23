using System.Globalization;
using R3.Desktop.Design;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace R3.Desktop;

// DevExpress-style grid boolean column: a check glyph instead of the literal "True"/"False" text WPF
// would otherwise render for a raw bool/long binding. Uses System.Convert.ToBoolean rather than an
// `is true` pattern because SQLite INTEGER columns (is_active, is_sellable, ...) come back through
// Microsoft.Data.Sqlite/DataTable as boxed long, not bool.
public sealed class BoolToCheckGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value != null && System.Convert.ToBoolean(value) ? "✓" : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PinnedBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Ui.Brush("R3.Warning.Soft.Brush") : Ui.Brush("R3.Surface.Brush");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Status badge colors for the Cari Kartlar grid and Risk & Kredi screen, from the design tokens
/// (Design/Tokens.xaml): good = success, attention = warning, bad = danger, inactive/unknown = muted.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Ui.Brush(value?.ToString() == "Aktif" ? "R3.Success.Brush" : "R3.Text.Muted.Brush");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class BalanceStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Ui.Brush(value?.ToString() switch { "Borçlu" => "R3.Danger.Brush", "Alacaklı" => "R3.Success.Brush", _ => "R3.Text.Muted.Brush" });
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class RiskStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Ui.Brush(value?.ToString() switch { "Normal" => "R3.Success.Brush", "Limite Yakın" => "R3.Warning.Brush", "Limit Aşıldı" => "R3.Danger.Brush", _ => "R3.Text.Muted.Brush" });
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Cari type is a category, not a status, so it keeps its own three distinguishable hues.</summary>
public sealed class AccountTypeBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new SolidColorBrush(value?.ToString() switch
        {
            "Customer" => Color.FromRgb(0x35, 0x78, 0xB8),
            "Supplier" => Color.FromRgb(0x8E, 0x6B, 0xBE),
            "CustomerAndSupplier" => Color.FromRgb(0x2A, 0x9D, 0x8F),
            _ => Color.FromRgb(0x71, 0x80, 0x96)
        });
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
