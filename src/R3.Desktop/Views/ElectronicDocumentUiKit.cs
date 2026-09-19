using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace R3.Desktop.Views;

// Phase 9: small chrome (cards, badges, grids, toolbars) shared by the four e-document operations
// screens (Dashboard/Outgoing/Outbox/Errors) so they look and behave the same way - not a
// general-purpose framework, just the one set of primitives this phase's screens all need.
internal static class ElectronicDocumentUiKit
{
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    public static readonly Brush Muted = Brush("#667986");
    public static readonly Brush PanelBorder = Brush("#D6E0E6");

    public static Border Card(string caption, string value, string color)
    {
        var body = new StackPanel(); body.Children.Add(new TextBlock { Text = caption, Foreground = Muted, FontSize = 11 });
        body.Children.Add(new TextBlock { Text = value, Foreground = Brush(color), FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0) });
        return new Border { Child = body, MinWidth = 150, Margin = new Thickness(0, 0, 10, 8), Padding = new Thickness(13, 10, 13, 10), Background = Brushes.White, BorderBrush = PanelBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7) };
    }

    public static Border Badge(string text, string color) => new()
    {
        CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 2), Background = Brush(color),
        Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold }
    };

    public static StackPanel Toolbar(out TextBox search, string hint)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        bar.Children.Add(new TextBlock { Text = "Ara", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Foreground = Muted });
        search = new TextBox { Width = 260, Height = 30, Padding = new Thickness(8, 4, 8, 4), ToolTip = hint };
        bar.Children.Add(search);
        return bar;
    }

    public static ComboBox FilterCombo(string placeholder, params (string Value, string Label)[] options)
    {
        var items = new List<KeyValuePair<string, string>> { new("", placeholder) };
        items.AddRange(options.Select(o => new KeyValuePair<string, string>(o.Value, o.Label)));
        return new ComboBox { Width = 170, Height = 30, Margin = new Thickness(8, 0, 0, 0), ItemsSource = items, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedIndex = 0 };
    }

    // Named with a trailing underscore (matching InvoiceDetailView's Grid_ convention) so it never
    // collides with System.Windows.Controls.Grid in files that `using static` this class.
    public static DataGrid Grid_() => new()
    {
        Style = (Style)System.Windows.Application.Current.FindResource("ProfessionalDataGridStyle"), AutoGenerateColumns = false,
        IsReadOnly = true, CanUserAddRows = false, MinHeight = 220, HeadersVisibility = DataGridHeadersVisibility.Column
    };

    public static void Columns(DataGrid grid, params (string Key, string Header, double Width)[] columns)
    {
        foreach (var c in columns) grid.Columns.Add(new DataGridTextColumn { Header = c.Header, Binding = new Binding(c.Key) { ConverterCulture = Turkish }, Width = new DataGridLength(c.Width) });
    }

    public static Button ActionButton(Panel panel, string text, Action action)
    {
        var b = new Button { Content = text, Height = 30, Padding = new Thickness(11, 4, 11, 4), Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(242, 244, 246)), BorderBrush = new SolidColorBrush(Color.FromRgb(190, 199, 207)), Foreground = new SolidColorBrush(Color.FromRgb(42, 55, 65)) };
        b.Click += (_, _) => action(); panel.Children.Add(b); return b;
    }

    public static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
