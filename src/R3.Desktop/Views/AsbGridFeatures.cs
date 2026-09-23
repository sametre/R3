using System.ComponentModel;
using System.Data;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using R3.Desktop.Design;

namespace R3.Desktop.Views;

/// <summary>
/// ASB/DevExpress list look for a WPF DataGrid: a filter box under every column caption (the "▼ filtre satırı").
/// Typing filters the bound DataView with RowFilter (contains, case-insensitive, tr-TR). Only for grids bound to a
/// DataView whose table fits in memory (Stok Kartları: whole company list, ~35K rows).
/// </summary>
internal sealed class GridFilterRow
{
    private readonly DataGrid _grid;
    private readonly List<(string Path, TextBox Box)> _boxes = [];

    public event EventHandler? Changed;

    public GridFilterRow(DataGrid grid)
    {
        _grid = grid;
        foreach (var column in grid.Columns.OfType<DataGridTextColumn>())
        {
            if (column.Binding is not Binding { Path.Path: { Length: > 0 } path }) continue;
            var caption = new TextBlock { Text = column.Header as string ?? "", TextWrapping = TextWrapping.Wrap, MinHeight = 26, VerticalAlignment = VerticalAlignment.Center };
            var box = new TextBox { Margin = new Thickness(-4, 2, -4, -2), MinHeight = 20, Padding = new Thickness(2, 0, 2, 0), BorderThickness = new Thickness(0, 1, 0, 0), Background = Ui.Surface, ToolTip = "Bu kolonda ara (içerir)" };
            AutomationProperties.SetName(box, "Filtre: " + caption.Text);
            var panel = new StackPanel();
            panel.Children.Add(caption); panel.Children.Add(box);
            column.Header = panel;
            // The app-wide header style renders headers through a TextBlock template (text only): this header is a panel.
            column.HeaderTemplate = PanelHeaderTemplate;
            column.HeaderStyle = new Style(typeof(DataGridColumnHeader), column.HeaderStyle ?? grid.ColumnHeaderStyle)
            {
                Setters = { new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch), new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch) }
            };
            _boxes.Add((path, box));
            KeyboardInteractionService.AttachDebouncedSearch(box, () => { Apply(); Changed?.Invoke(this, EventArgs.Empty); }, 300);
            box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Down) { MoveToGrid(); e.Handled = true; } };
        }
    }

    private static readonly DataTemplate PanelHeaderTemplate = CreatePanelHeaderTemplate();
    private static DataTemplate CreatePanelHeaderTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding());
        var template = new DataTemplate { VisualTree = presenter };
        template.Seal();
        return template;
    }

    public bool IsActive => _boxes.Any(b => b.Box.Text.Trim().Length > 0);

    /// <summary>Re-applies the typed filters (call after the grid gets a new ItemsSource).</summary>
    public void Apply()
    {
        if (_grid.ItemsSource is not DataView view) return;
        var parts = _boxes.Where(b => b.Box.Text.Trim().Length > 0 && view.Table!.Columns.Contains(b.Path))
            .Select(b => $"CONVERT([{b.Path}], 'System.String') LIKE '%{Escape(b.Box.Text.Trim())}%'");
        view.RowFilter = string.Join(" AND ", parts);
    }

    public void Clear() { foreach (var (_, box) in _boxes) box.Clear(); Apply(); }

    public void FocusFirst() { if (_boxes.Count > 0) { _boxes[0].Box.Focus(); _boxes[0].Box.SelectAll(); } }

    /// <summary>RowFilter LIKE escaping: quotes doubled, wildcard/bracket characters wrapped in [ ].</summary>
    internal static string Escape(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length + 8);
        foreach (var c in text)
            builder.Append(c switch { '\'' => "''", '*' => "[*]", '%' => "[%]", '[' => "[[]", ']' => "[]]", _ => c.ToString() });
        return builder.ToString();
    }

    private void MoveToGrid()
    {
        if (_grid.Items.Count == 0) return;
        if (_grid.SelectedIndex < 0) _grid.SelectedIndex = 0;
        _grid.Focus();
        if (_grid.ItemContainerGenerator.ContainerFromIndex(_grid.SelectedIndex) is DataGridRow row) row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }
}

/// <summary>
/// ASB "Gruplamak için Kolon Başlığını Bu Alana Sürükleyiniz !" band: drag a column caption up onto the band (or use the
/// caption's right-click "Bu kolona göre grupla") to group the list; each grouping shows as a chip with × to remove.
/// Groups are expanders with "(n kayıt)"; virtualization stays on while grouped.
/// </summary>
internal sealed class GridGroupPanel : Border
{
    private const string Hint = "Gruplamak için Kolon Başlığını Bu Alana Sürükleyiniz !";
    private readonly DataGrid _grid;
    private readonly WrapPanel _chips = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _hint = new() { Text = Hint, VerticalAlignment = VerticalAlignment.Center, FontStyle = FontStyles.Italic };
    private readonly List<(string Path, string Caption)> _groups = [];
    private Point? _dragStart;
    private DataGridColumnHeader? _dragHeader;

    public GridGroupPanel(DataGrid grid)
    {
        _grid = grid;
        Background = Ui.Brush("R3.Surface.Alt.Brush"); BorderBrush = Ui.Border; BorderThickness = new Thickness(1, 0, 1, 1);
        Padding = new Thickness(Ui.Space.M, Ui.Space.S, Ui.Space.M, Ui.Space.S); MinHeight = 30; AllowDrop = true;
        _hint.Foreground = Ui.TextSecondary;
        var panel = new Grid(); panel.Children.Add(_hint); panel.Children.Add(_chips);
        Child = panel;
        AutomationProperties.SetName(this, "Gruplama alanı");

        VirtualizingPanel.SetIsVirtualizingWhenGrouping(grid, true);
        grid.GroupStyle.Add((GroupStyle)XamlReader.Parse("""
            <GroupStyle xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <GroupStyle.ContainerStyle>
                <Style TargetType="GroupItem">
                  <Setter Property="Template"><Setter.Value>
                    <ControlTemplate TargetType="GroupItem">
                      <Expander IsExpanded="True" Padding="0">
                        <Expander.Header>
                          <TextBlock FontWeight="SemiBold"><Run Text="{Binding Name, Mode=OneWay}"/><Run Text="  ("/><Run Text="{Binding ItemCount, Mode=OneWay}"/><Run Text=" kayıt)"/></TextBlock>
                        </Expander.Header>
                        <ItemsPresenter/>
                      </Expander>
                    </ControlTemplate>
                  </Setter.Value></Setter>
                </Style>
              </GroupStyle.ContainerStyle>
            </GroupStyle>
            """));

        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(typeof(DataGridColumn)) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) => { if (e.Data.GetData(typeof(DataGridColumn)) is DataGridColumn column) GroupBy(column); };
        grid.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragHeader = FindHeader(e.OriginalSource as DependencyObject);
            _dragStart = _dragHeader == null ? null : e.GetPosition(_dragHeader);
        };
        grid.PreviewMouseLeftButtonUp += (_, _) => { _dragHeader = null; _dragStart = null; };
        grid.PreviewMouseMove += (_, e) =>
        {
            // Sideways drag = the DataGrid's own column reorder; only a drag upward out of the header row groups.
            if (_dragHeader?.Column == null || _dragStart == null || e.LeftButton != MouseButtonState.Pressed) return;
            if (e.GetPosition(_dragHeader).Y > -8) return;
            var header = _dragHeader; _dragHeader = null; _dragStart = null;
            header.ReleaseMouseCapture();
            DragDrop.DoDragDrop(header, new DataObject(typeof(DataGridColumn), header.Column), DragDropEffects.Move);
        };
        grid.Loaded += (_, _) => AttachHeaderMenus();
    }

    /// <summary>Re-applies the chosen groupings to the grid's current view (call after a new ItemsSource).</summary>
    public void Apply()
    {
        if (_grid.ItemsSource == null) return;
        var view = CollectionViewSource.GetDefaultView(_grid.ItemsSource);
        if (view is not { CanGroup: true }) return;
        using (view.DeferRefresh())
        {
            view.GroupDescriptions.Clear();
            foreach (var (path, _) in _groups) view.GroupDescriptions.Add(new PropertyGroupDescription(path));
        }
        _hint.Visibility = _groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _chips.Children.Clear();
        foreach (var (path, caption) in _groups) _chips.Children.Add(Chip(path, caption));
    }

    public void GroupBy(DataGridColumn column)
    {
        if (column is not DataGridTextColumn { Binding: Binding { Path.Path: { Length: > 0 } path } }) return;
        if (_groups.Any(g => g.Path == path)) return;
        _groups.Add((path, GridOutput.HeaderText(column.Header)));
        Apply();
    }

    private void Remove(string path) { _groups.RemoveAll(g => g.Path == path); Apply(); }

    private Border Chip(string path, string caption)
    {
        var remove = new Button { Content = "×", Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(Ui.Space.S, 0, 0, 0), MinHeight = 0, BorderThickness = new Thickness(0), Background = Brushes.Transparent, ToolTip = "Gruplamayı kaldır" };
        AutomationProperties.SetName(remove, caption + " gruplamasını kaldır");
        remove.Click += (_, _) => Remove(path);
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = caption, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(remove);
        return new Border { Child = content, Background = Ui.Surface, BorderBrush = Ui.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(Ui.Space.SM, 0, 0, 0), Margin = new Thickness(0, 0, Ui.Space.SM, 0) };
    }

    private void AttachHeaderMenus()
    {
        foreach (var column in _grid.Columns.OfType<DataGridTextColumn>())
        {
            var item = new MenuItem { Header = "Bu kolona göre grupla" };
            var target = column;
            item.Click += (_, _) => GroupBy(target);
            var clear = new MenuItem { Header = "Gruplamayı temizle" };
            clear.Click += (_, _) => { _groups.Clear(); Apply(); };
            var menu = new ContextMenu(); menu.Items.Add(item); menu.Items.Add(clear);
            column.HeaderStyle = new Style(typeof(DataGridColumnHeader), column.HeaderStyle ?? _grid.ColumnHeaderStyle) { Setters = { new Setter(ContextMenuProperty, menu) } };
        }
    }

    private static DataGridColumnHeader? FindHeader(DependencyObject? source)
    {
        for (var node = source; node != null; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is TextBox) return null; // typing in the filter row is not a drag
            if (node is DataGridColumnHeader header) return header;
        }
        return null;
    }
}
