using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;

namespace R3.Desktop.Design;

/// <summary>
/// App-wide conventions applied once at startup, so every screen - XAML or code-built, old or new - gets them
/// without per-view code:
///  • tr-TR is the binding language: StringFormat N2 / dates render as 1.234,56 / 23.09.2026 (WPF defaults to en-US).
///  • Every DataGrid right-aligns numeric columns; auto-generated / unformatted decimal columns get N2.
/// </summary>
public static class DesignConventions
{
    private static readonly Style RightAligned = CreateCellText(TextAlignment.Right), LeftAligned = CreateCellText(TextAlignment.Left);

    public static void Apply()
    {
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("tr-TR")));
        EventManager.RegisterClassHandler(typeof(DataGrid), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => Attach((DataGrid)sender)));
    }

    private static readonly DependencyPropertyDescriptor ItemsSourceDescriptor = DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(DataGrid));
    private static readonly DependencyProperty AttachedProperty = DependencyProperty.RegisterAttached("ConventionsAttached", typeof(bool), typeof(DesignConventions));

    private static void Attach(DataGrid grid)
    {
        FormatNumericColumns(grid);
        if ((bool)grid.GetValue(AttachedProperty)) return;
        grid.SetValue(AttachedProperty, true);
        // Columns and ItemsSource usually arrive after Loaded (async refresh, auto-generation).
        void OnItemsSource(object? s, EventArgs e) => FormatNumericColumns(grid);
        void OnColumns(object? s, NotifyCollectionChangedEventArgs e) => FormatNumericColumns(grid);
        ItemsSourceDescriptor.AddValueChanged(grid, OnItemsSource);
        grid.Columns.CollectionChanged += OnColumns;
        grid.Unloaded += (_, _) => { ItemsSourceDescriptor.RemoveValueChanged(grid, OnItemsSource); grid.Columns.CollectionChanged -= OnColumns; grid.SetValue(AttachedProperty, false); };
    }

    /// <summary>Right-aligns numeric bound columns (unformatted decimal/double ones get N2); centers all default-styled
    /// text columns vertically. Columns with their own ElementStyle are left alone. Idempotent.</summary>
    public static void FormatNumericColumns(DataGrid grid)
    {
        var table = (grid.ItemsSource as DataView)?.Table;
        foreach (var column in grid.Columns.OfType<DataGridTextColumn>())
        {
            if (column.Binding is not Binding binding) continue;
            var path = binding.Path?.Path?.Trim('[', ']');
            // DataRowView bindings use [Column], but BindingListCollectionView sorts by Column.
            // The inferred indexer path otherwise throws when the user clicks a column header.
            if (table != null && path != null && table.Columns.Contains(path) &&
                (string.IsNullOrEmpty(column.SortMemberPath) || column.SortMemberPath == binding.Path?.Path))
                column.SortMemberPath = path;
            var type = table != null && path != null && table.Columns.Contains(path) ? table.Columns[path]!.DataType : null;
            var fractional = type == typeof(decimal) || type == typeof(double) || type == typeof(float);
            var numeric = fractional || IsNumericFormat(binding.StringFormat) || type == typeof(int) || type == typeof(long) || type == typeof(short);
            if (!numeric)
            {
                if (IsConventionStyle(column.ElementStyle)) column.ElementStyle = LeftAligned;
                continue;
            }
            if (fractional && string.IsNullOrEmpty(binding.StringFormat))
                column.Binding = new Binding(binding.Path!.Path) { StringFormat = "N2", Mode = binding.Mode, UpdateSourceTrigger = binding.UpdateSourceTrigger, Converter = binding.Converter, ConverterParameter = binding.ConverterParameter, ConverterCulture = binding.ConverterCulture };
            if (IsConventionStyle(column.ElementStyle)) column.ElementStyle = RightAligned;
        }
    }

    // A column's type is often unknown on the first pass (grid loaded before its data), so our own styles stay replaceable.
    private static bool IsConventionStyle(Style? style) =>
        style == null || ReferenceEquals(style, DataGridTextColumn.DefaultElementStyle) || ReferenceEquals(style, LeftAligned) || ReferenceEquals(style, RightAligned);

    /// <summary>"N2", "C", "F0", "P1", "#,##0.00", "{0:N2}", "{0:N2} ₺" - but not text like "Fatura {0}".</summary>
    public static bool IsNumericFormat(string? format) =>
        !string.IsNullOrEmpty(format) && System.Text.RegularExpressions.Regex.IsMatch(format, @"^(\{0:)?([NCFPncfp]\d{0,2}|[#0][#0.,]*)(\})?( ?\S{0,3})?$");

    // Vertically centered so text lines up with chips/checkboxes in taller rows (the default cell text sits at the top).
    private static Style CreateCellText(TextAlignment alignment)
    {
        var style = new Style(typeof(TextBlock), DataGridTextColumn.DefaultElementStyle);
        style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, alignment));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Seal();
        return style;
    }
}
