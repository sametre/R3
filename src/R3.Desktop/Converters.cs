using System.Globalization;
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
        value is true ? new SolidColorBrush(Color.FromRgb(0xFF, 0xF6, 0xE0)) : Brushes.White;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Small badge-color lookups shared by the Cari Kartlar grid and Risk & Kredi screen.
/// Kept as plain hex brushes to match every other screen in this codebase (MainWindow, EditorDialogs) -
/// no separate theme-resource system exists here yet, so this does not invent one.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new SolidColorBrush(value?.ToString() switch
        {
            "Aktif" => Color.FromRgb(0x2A, 0x9D, 0x8F),
            "Pasif" => Color.FromRgb(0x9C, 0xA3, 0xAF),
            _ => Color.FromRgb(0x9C, 0xA3, 0xAF)
        });
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class BalanceStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new SolidColorBrush(value?.ToString() switch
        {
            "Borçlu" => Color.FromRgb(0xE7, 0x6F, 0x51),
            "Alacaklı" => Color.FromRgb(0x2A, 0x9D, 0x8F),
            _ => Color.FromRgb(0x71, 0x80, 0x96)
        });
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class RiskStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new SolidColorBrush(value?.ToString() switch
        {
            "Normal" => Color.FromRgb(0x2A, 0x9D, 0x8F),
            "Limite Yakın" => Color.FromRgb(0xC8, 0x8A, 0x21),
            "Limit Aşıldı" => Color.FromRgb(0xC0, 0x39, 0x2B),
            "Bloke" => Color.FromRgb(0x9C, 0xA3, 0xAF),
            _ => Color.FromRgb(0x71, 0x80, 0x96)
        });
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

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
