using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using R3.Application.Security;
using R3.Desktop.Logging;
using R3.Infrastructure;

namespace R3.Desktop.ContextActions;

public enum ContextActionGroup { Primary, Related, Financial, Operational, Document, Critical, Audit }
public enum ContextSelectionMode { None, Single, Multiple, Any }

public sealed record ContextActionResult(bool Success, string? Message = null, bool RefreshRequired = false)
{
    public static ContextActionResult Ok(string? message = null, bool refresh = false) => new(true, message, refresh);
    public static ContextActionResult Failed(string message) => new(false, message);
}

public sealed record ContextActionDefinition(
    string Id,
    string Header,
    string? PermissionCode,
    string Icon,
    int Order,
    ContextActionGroup Group,
    Func<object?, Task<ContextActionResult>> ExecuteAsync,
    Func<object?, bool>? IsVisible = null,
    Func<object?, bool>? IsEnabled = null,
    ContextSelectionMode SelectionMode = ContextSelectionMode.Single,
    bool HideWhenUnauthorized = true,
    bool RequiresConfirmation = false,
    Func<object?, string>? ConfirmationText = null,
    string? AuditAction = null,
    string? EntityType = null,
    Func<object?, string?>? EntityId = null);

public sealed record ContextActionAvailability(bool Visible, bool Enabled, string? DisabledReason = null);

public static class ContextActionEvaluator
{
    public static ContextActionAvailability Evaluate(ContextActionDefinition action, object? selected, int selectionCount, IPermissionService? permissions)
    {
        var authorized = string.IsNullOrWhiteSpace(action.PermissionCode) || permissions?.HasPermission(action.PermissionCode) != false;
        if (!authorized && action.HideWhenUnauthorized) return new(false, false, "Bu işlem için yetkiniz bulunmuyor.");
        var selectionMatches = action.SelectionMode switch { ContextSelectionMode.None => selectionCount == 0, ContextSelectionMode.Single => selectionCount == 1, ContextSelectionMode.Multiple => selectionCount > 1, _ => true };
        if (!selectionMatches) return new(false, false, "Uygun sayıda kayıt seçin.");
        if (action.IsVisible?.Invoke(selected) == false) return new(false, false);
        if (!authorized) return new(true, false, "Bu işlem için yetkiniz bulunmuyor.");
        if (action.IsEnabled?.Invoke(selected) == false) return new(true, false, "Seçili kaydın mevcut durumunda bu işlem kullanılamaz.");
        return new(true, true);
    }

    public static ContextActionDefinition? DefaultOpen(IEnumerable<ContextActionDefinition> actions) => actions.Where(x => x.Group == ContextActionGroup.Primary).OrderBy(x => x.Order).FirstOrDefault();
}

public static class ErpGridContext
{
    private sealed record Registration(string ViewKey, List<ContextActionDefinition> Actions, Func<Task>? Refresh, string? EntityType);
    private sealed record ColumnLayout(string Key, int DisplayIndex, double Width, string Unit, bool Visible, string? Sort, int SortIndex);
    private sealed record GridLayout(int Version, IReadOnlyList<ColumnLayout> Columns);

    private static readonly Dictionary<DataGrid, Registration> Registrations = [];
    private static readonly HashSet<DataGrid> Attached = [];
    private static readonly HashSet<string> BusyActions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ILogger Logger = DesktopLogging.CreateLogger<ErpGridContextLog>();
    private static IPermissionService? _permissions;
    private static UserGridLayoutService? _layouts;
    private static StoreDatabase? _database;
    private static string _userName = "";

    public static void Configure(StoreDatabase database, string userName)
    {
        _database = database; _userName = userName;
        _permissions = new LocalPermissionService(database, userName);
        _layouts = new UserGridLayoutService(database, userName);
    }

    public static void Register(DataGrid grid, string viewKey, IEnumerable<ContextActionDefinition>? actions = null, Func<Task>? refresh = null, string? entityType = null)
    {
        Registrations[grid] = new Registration(viewKey, actions?.ToList() ?? [], refresh, entityType);
        Attach(grid); RestoreLayout(grid, viewKey); grid.Dispatcher.BeginInvoke(() => RestoreLayout(grid, viewKey));
    }

    public static void OnGridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        Attach(grid);
        if (!Registrations.ContainsKey(grid)) Register(grid, ResolveViewKey(grid));
    }

    public static void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        var row = FindParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row != null && !row.IsSelected) { grid.SelectedItems.Clear(); row.IsSelected = true; }
    }

    private static void Attach(DataGrid grid)
    {
        if (!Attached.Add(grid)) return;
        grid.PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
        grid.ContextMenuOpening += (_, _) => grid.ContextMenu = BuildMenu(grid);
        grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Apps && !(e.Key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))) return;
            grid.ContextMenu = BuildMenu(grid); grid.ContextMenu.PlacementTarget = grid; grid.ContextMenu.IsOpen = true; e.Handled = true;
        };
        grid.MouseDoubleClick += async (_, e) =>
        {
            if (FindParent<DataGridRow>(e.OriginalSource as DependencyObject) == null) return;
            if (!Registrations.TryGetValue(grid, out var registration)) return;
            var open = ContextActionEvaluator.DefaultOpen(registration.Actions);
            if (open != null && CanShow(open, grid) && CanRun(open, grid)) await RunAsync(open, grid, registration);
        };
        grid.Unloaded += (_, _) => { Attached.Remove(grid); Registrations.Remove(grid); };
    }

    private static ContextMenu BuildMenu(DataGrid grid)
    {
        var menu = new ContextMenu { MinWidth = 245, Padding = new Thickness(2, 5, 2, 5) };
        var registration = Registrations.GetValueOrDefault(grid) ?? new Registration(ResolveViewKey(grid), [], null, null);
        ContextActionGroup? previous = null;
        foreach (var action in registration.Actions.OrderBy(x => x.Group).ThenBy(x => x.Order))
        {
            if (!CanShow(action, grid)) continue;
            if (previous != null && previous != action.Group) menu.Items.Add(new Separator());
            var item = ActionItem(action, grid, registration); menu.Items.Add(item); previous = action.Group;
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        AddCommonActions(menu, grid, registration);
        return menu;
    }

    private static MenuItem ActionItem(ContextActionDefinition action, DataGrid grid, Registration registration)
    {
        var item = new MenuItem { Header = action.Header, Icon = Icon(action.Icon), IsEnabled = CanRun(action, grid) };
        if (!item.IsEnabled) item.ToolTip = _permissions?.HasPermission(action.PermissionCode ?? "") == false ? "Bu işlem için yetkiniz bulunmuyor." : "Seçili kaydın mevcut durumunda bu işlem kullanılamaz.";
        item.Click += async (_, _) => await RunAsync(action, grid, registration); return item;
    }

    private static async Task RunAsync(ContextActionDefinition action, DataGrid grid, Registration registration)
    {
        if (!CanRun(action, grid)) return;
        var selected = Selected(grid, action.SelectionMode); var busyKey = registration.ViewKey + ":" + action.Id;
        if (!BusyActions.Add(busyKey)) return;
        try
        {
            if (action.RequiresConfirmation)
            {
                var text = action.ConfirmationText?.Invoke(selected) ?? $"{action.Header} işlemi uygulanacak.\n\nDevam etmek istiyor musunuz?";
                if (MessageBox.Show(Window.GetWindow(grid), text, action.Header, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            }
            var result = await action.ExecuteAsync(selected);
            if (!result.Success) MessageBox.Show(Window.GetWindow(grid), result.Message ?? "İşlem tamamlanamadı.", action.Header, MessageBoxButton.OK, MessageBoxImage.Warning);
            else
            {
                if (!string.IsNullOrWhiteSpace(action.AuditAction)) Audit(action, selected, registration);
                if (result.RefreshRequired && registration.Refresh != null) await registration.Refresh();
                if (!string.IsNullOrWhiteSpace(result.Message)) MessageBox.Show(Window.GetWindow(grid), result.Message, action.Header, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Context action failed. View={ViewKey} Action={ActionId}", registration.ViewKey, action.Id);
            MessageBox.Show(Window.GetWindow(grid), $"{action.Header} işlemi tamamlanamadı.\n\nLütfen kayıt durumunu kontrol edip yeniden deneyin.", "R3 ERP", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { BusyActions.Remove(busyKey); }
    }

    private static bool CanShow(ContextActionDefinition action, DataGrid grid)
        => ContextActionEvaluator.Evaluate(action, Selected(grid, action.SelectionMode), grid.SelectedItems.Count, _permissions).Visible;
    private static bool CanRun(ContextActionDefinition action, DataGrid grid)
        => ContextActionEvaluator.Evaluate(action, Selected(grid, action.SelectionMode), grid.SelectedItems.Count, _permissions).Enabled;
    private static bool SelectionMatches(DataGrid grid, ContextSelectionMode mode) => mode switch { ContextSelectionMode.None => grid.SelectedItems.Count == 0, ContextSelectionMode.Single => grid.SelectedItems.Count == 1, ContextSelectionMode.Multiple => grid.SelectedItems.Count > 1, _ => true };
    private static object? Selected(DataGrid grid, ContextSelectionMode mode) => mode == ContextSelectionMode.Multiple ? grid.SelectedItems.Cast<object>().ToArray() : grid.SelectedItem;

    private static void AddCommonActions(ContextMenu menu, DataGrid grid, Registration registration)
    {
        var copy = new MenuItem { Header = "Seçili satırı kopyala", Icon = Icon(""), IsEnabled = grid.SelectedItem != null }; copy.Click += (_, _) => CopyRows(grid, true); menu.Items.Add(copy);
        var layout = new MenuItem { Header = "Görünüm ve kolonlar", Icon = Icon("") };
        Add(layout, "Kolonları ekrana sığdır", () => Fit(grid, true)); Add(layout, "İçeriğe göre boyutlandır", () => Fit(grid, false)); layout.Items.Add(new Separator());
        var columns = new MenuItem { Header = "Kolonları seç" }; foreach (var column in grid.Columns.OrderBy(x => x.DisplayIndex)) { var c = column; var toggle = new MenuItem { Header = ColumnKey(c), IsCheckable = true, IsChecked = c.Visibility == Visibility.Visible }; toggle.Click += (_, _) => c.Visibility = toggle.IsChecked ? Visibility.Visible : Visibility.Collapsed; columns.Items.Add(toggle); } layout.Items.Add(columns);
        Add(layout, "Sıralamayı temizle", () => { foreach (var c in grid.Columns) c.SortDirection = null; CollectionViewSource.GetDefaultView(grid.ItemsSource)?.SortDescriptions.Clear(); }); layout.Items.Add(new Separator());
        Add(layout, "Filtreleri temizle", () => { var view = CollectionViewSource.GetDefaultView(grid.ItemsSource); if (view != null) { view.Filter = null; view.Refresh(); } });
        Add(layout, "Gruplamayı temizle", () => CollectionViewSource.GetDefaultView(grid.ItemsSource)?.GroupDescriptions.Clear()); layout.Items.Add(new Separator());
        var save = new MenuItem { Header = "Görünümü kaydet", IsEnabled = _permissions?.HasPermission("reports.layout.save") != false }; save.Click += (_, _) => SaveLayout(grid, registration.ViewKey, false); layout.Items.Add(save);
        var setDefault = new MenuItem { Header = "Varsayılan görünüm yap", IsEnabled = _permissions?.HasPermission("reports.layout.set_default") != false }; setDefault.Click += (_, _) => SaveLayout(grid, registration.ViewKey, true); layout.Items.Add(setDefault);
        Add(layout, "Görünümü sıfırla", () => { _layouts?.Reset(registration.ViewKey); Fit(grid, true); }, _permissions?.HasPermission("reports.layout.reset") != false); menu.Items.Add(layout);
        var export = new MenuItem { Header = "Excel/CSV dışa aktar", Icon = Icon(""), IsEnabled = grid.ItemsSource != null && _permissions?.HasPermission("reports.export") != false }; export.Click += (_, _) => ExportCsv(grid); menu.Items.Add(export);
        var print = new MenuItem { Header = "Listeyi yazdır", Icon = Icon(""), IsEnabled = grid.ItemsSource != null && _permissions?.HasPermission("reports.print") != false }; print.Click += (_, _) => Print(grid); menu.Items.Add(print);
        if (registration.Refresh != null) { menu.Items.Add(new Separator()); var refresh = new MenuItem { Header = "Listeyi yenile", Icon = Icon("") }; refresh.Click += async (_, _) => await registration.Refresh(); menu.Items.Add(refresh); }
        var clear = new MenuItem { Header = "Seçimi temizle", Icon = Icon(""), IsEnabled = grid.SelectedItems.Count > 0 }; clear.Click += (_, _) => grid.UnselectAll(); menu.Items.Add(clear);
    }

    private static void SaveLayout(DataGrid grid, string viewKey, bool isDefault)
    {
        var sort = CollectionViewSource.GetDefaultView(grid.ItemsSource)?.SortDescriptions.ToList() ?? [];
        var columns = grid.Columns.Select(c => new ColumnLayout(ColumnKey(c), c.DisplayIndex, c.Width.Value, c.Width.UnitType.ToString(), c.Visibility == Visibility.Visible, c.SortDirection?.ToString(), c.SortDirection == null ? -1 : sort.FindIndex(x => x.PropertyName == BindingPath(c)))).ToArray();
        _layouts?.Save(viewKey, JsonSerializer.Serialize(new GridLayout(1, columns)), isDefault); AuditRaw(isDefault ? "DefaultGridLayoutSaved" : "GridLayoutSaved", "GridLayout", viewKey, viewKey);
    }
    private static void RestoreLayout(DataGrid grid, string viewKey)
    {
        try
        {
            var json = _layouts?.Load(viewKey); if (string.IsNullOrWhiteSpace(json)) return; var layout = JsonSerializer.Deserialize<GridLayout>(json); if (layout?.Version != 1) return;
            foreach (var saved in layout.Columns.OrderBy(x => x.DisplayIndex)) { var column = grid.Columns.FirstOrDefault(x => ColumnKey(x) == saved.Key); if (column == null) continue; column.DisplayIndex = Math.Min(saved.DisplayIndex, grid.Columns.Count - 1); column.Visibility = saved.Visible ? Visibility.Visible : Visibility.Collapsed; column.Width = saved.Unit switch { "Star" => new DataGridLength(Math.Max(.1, saved.Width), DataGridLengthUnitType.Star), "Auto" => DataGridLength.Auto, "SizeToCells" => DataGridLength.SizeToCells, "SizeToHeader" => DataGridLength.SizeToHeader, _ => new DataGridLength(Math.Max(20, saved.Width)) }; if (Enum.TryParse<ListSortDirection>(saved.Sort, out var direction)) column.SortDirection = direction; }
            var view = CollectionViewSource.GetDefaultView(grid.ItemsSource); if (view != null) { view.SortDescriptions.Clear(); foreach (var saved in layout.Columns.Where(x => x.SortIndex >= 0).OrderBy(x => x.SortIndex)) if (Enum.TryParse<ListSortDirection>(saved.Sort, out var direction)) { var column = grid.Columns.FirstOrDefault(x => ColumnKey(x) == saved.Key); if (column != null) view.SortDescriptions.Add(new SortDescription(BindingPath(column), direction)); } }
        }
        catch (Exception ex) { Logger.LogWarning(ex, "Grid layout could not be restored. View={ViewKey}", viewKey); }
    }

    private static void Audit(ContextActionDefinition action, object? selected, Registration registration) => AuditRaw(action.AuditAction!, action.EntityType ?? registration.EntityType ?? "GridRow", action.EntityId?.Invoke(selected) ?? EntityId(selected), action.Id);
    private static void AuditRaw(string action, string entityType, string? entityId, string value)
    {
        if (_database == null) return;
        var userId = _database.Query("SELECT id FROM users WHERE username=$u LIMIT 1", ("$u", (object)_userName)).Rows.Cast<DataRow>().FirstOrDefault()?[0]?.ToString();
        _database.Execute("INSERT INTO audit_logs(id,user_id,entity_type,entity_id,action,new_values,created_at) VALUES($id,$user,$type,$entity,$action,$value,$now)", ("$id", Guid.NewGuid().ToString()), ("$user", (object?)userId ?? DBNull.Value), ("$type", entityType), ("$entity", entityId ?? ""), ("$action", action), ("$value", value), ("$now", DateTime.UtcNow.ToString("O")));
    }
    private static string? EntityId(object? selected) => selected?.GetType().GetProperty("Id")?.GetValue(selected)?.ToString() ?? (selected as DataRowView)?["Id"]?.ToString();
    private static void CopyRows(DataGrid grid, bool headers) { if (grid.SelectedItem == null) return; var values = grid.Columns.Where(c => c.Visibility == Visibility.Visible).OrderBy(c => c.DisplayIndex).Select(c => CellValue(grid.SelectedItem, c)); var line = string.Join('\t', values); if (headers) line = string.Join('\t', grid.Columns.Where(c => c.Visibility == Visibility.Visible).OrderBy(c => c.DisplayIndex).Select(ColumnKey)) + Environment.NewLine + line; Clipboard.SetText(line); }
    private static string CellValue(object row, DataGridColumn column) { var path = BindingPath(column); object? value = row is DataRowView drv && drv.Row.Table.Columns.Contains(path) ? drv[path] : row.GetType().GetProperty(path, BindingFlags.Public | BindingFlags.Instance)?.GetValue(row); return (value?.ToString() ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
    private static string BindingPath(DataGridColumn column) => (column as DataGridBoundColumn)?.Binding is Binding binding ? binding.Path.Path : ColumnKey(column);
    private static string ColumnKey(DataGridColumn column) => column.Header?.ToString() ?? BindingPath(column);
    private static void Fit(DataGrid grid, bool star) { foreach (var column in grid.Columns.Where(x => x.Visibility == Visibility.Visible)) column.Width = star ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.SizeToCells; }
    private static void ExportCsv(DataGrid grid) { var dialog = new SaveFileDialog { Filter = "CSV dosyası|*.csv", FileName = ResolveViewKey(grid).Replace('.', '-') + ".csv" }; if (dialog.ShowDialog(Window.GetWindow(grid)) != true) return; var columns = grid.Columns.Where(c => c.Visibility == Visibility.Visible).OrderBy(c => c.DisplayIndex).ToArray(); static string Q(string value) => "\"" + value.Replace("\"", "\"\"") + "\""; var lines = new List<string> { string.Join(';', columns.Select(c => Q(ColumnKey(c)))) }; foreach (var row in grid.Items.Cast<object>().Where(x => x != CollectionView.NewItemPlaceholder)) lines.Add(string.Join(';', columns.Select(c => Q(CellValue(row, c))))); File.WriteAllLines(dialog.FileName, lines, Encoding.UTF8); }
    private static void Print(DataGrid grid) { var dialog = new PrintDialog(); if (dialog.ShowDialog() == true) dialog.PrintVisual(grid, ResolveViewKey(grid)); }
    private static void Add(MenuItem parent, string header, Action action, bool enabled = true) { var item = new MenuItem { Header = header, IsEnabled = enabled }; item.Click += (_, _) => action(); parent.Items.Add(item); }
    private static TextBlock Icon(string glyph) => new() { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 13, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(55, 112, 151)) };
    private static T? FindParent<T>(DependencyObject? current) where T : DependencyObject { while (current != null) { if (current is T value) return value; current = System.Windows.Media.VisualTreeHelper.GetParent(current); } return null; }
    private static string ResolveViewKey(DataGrid grid) => !string.IsNullOrWhiteSpace(grid.Name) ? $"{grid.FindVisualParent<UserControl>()?.GetType().Name ?? "grid"}.{grid.Name}".ToLowerInvariant() : $"{grid.FindVisualParent<UserControl>()?.GetType().Name ?? "grid"}.{string.Join('-', grid.Columns.Select(ColumnKey))}".ToLowerInvariant();
    private static T? FindVisualParent<T>(this DependencyObject child) where T : DependencyObject { var current = child; while (current != null) { if (current is T typed) return typed; current = System.Windows.Media.VisualTreeHelper.GetParent(current); } return null; }
    private sealed class ErpGridContextLog;
}
