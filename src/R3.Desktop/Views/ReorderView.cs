using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using R3.Desktop.ContextActions;
using R3.Desktop.Logging;
using R3.Infrastructure;

namespace R3.Desktop.Views;

/// <summary>Stok › Sipariş Önerileri (Sipariş Seviyeleri): below-minimum products → draft satınalma siparişleri.</summary>
public sealed class ReorderView : DockPanel
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static Brush Token(string key) => (Brush)System.Windows.Application.Current.FindResource(key);

    public ReorderView(StoreDatabase db, string companyId, string userName, Action<string> openProduct, Action openOrders)
    {
        var service = new LocalReorderService(db);
        Margin = new Thickness(16);
        var title = new TextBlock { Text = "Sipariş Önerileri", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Token("R3.Text.Primary.Brush") };
        SetDock(title, Dock.Top); Children.Add(title);
        var help = new TextBlock { Text = "Kullanılabilir stoğu minimumun altına düşen ürünler (depo bazlı min/max önceliklidir). Öneri = maksimum (yoksa minimum) − kullanılabilir − yoldaki sipariş; minimum sipariş miktarına yükseltilir ve sipariş katına yuvarlanır. Miktar ve fiyatı düzenleyip işaretli satırlardan tedarikçi × depo başına bir taslak satınalma siparişi oluşturabilirsiniz; onay yine Satınalma Siparişleri ekranında yapılır. Boşluk tuşu seçili satırları işaretler.", Foreground = Token("R3.Text.Secondary.Brush"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) };
        SetDock(help, Dock.Top); Children.Add(help);
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; SetDock(bar, Dock.Top); Children.Add(bar);
        var warehouse = new ComboBox { Width = 190, ItemsSource = service.WarehouseOptions(companyId), DisplayMemberPath = nameof(ReorderFilterOption.Name), SelectedValuePath = nameof(ReorderFilterOption.Id), SelectedIndex = 0 };
        var group = new ComboBox { Width = 200, ItemsSource = service.ProductGroupOptions(companyId), DisplayMemberPath = nameof(ReorderFilterOption.Name), SelectedValuePath = nameof(ReorderFilterOption.Id), SelectedIndex = 0 };
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Foreground = Token("R3.Text.Secondary.Brush") };
        var first = true;
        foreach (var (label, control) in new (string, Control)[] { ("Depo:", warehouse), ("Grup:", group) })
        {
            AutomationProperties.SetName(control, label.TrimEnd(':'));
            bar.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(first ? 0 : 12, 0, 6, 0) }); bar.Children.Add(control);
            first = false;
        }

        var grid = new DataGrid { Style = (Style)System.Windows.Application.Current.FindResource("ProfessionalDataGridStyle"), AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Extended, HeadersVisibility = DataGridHeadersVisibility.Column, FrozenColumnCount = 3 };
        AutomationProperties.SetAutomationId(grid, "ReorderGrid");
        // Template checkbox instead of DataGridCheckBoxColumn: toggles on the first click (the stock column needs select-then-click).
        var check = new FrameworkElementFactory(typeof(CheckBox));
        check.SetBinding(ToggleButton.IsCheckedProperty, new Binding("Sec") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        check.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        check.SetValue(FocusableProperty, false);
        check.SetValue(AutomationProperties.NameProperty, "Seç");
        grid.Columns.Add(new DataGridTemplateColumn { Header = "Seç", CellTemplate = new DataTemplate { VisualTree = check }, Width = 40 });

        var rightText = new Style(typeof(TextBlock), DataGridTextColumn.DefaultElementStyle);
        rightText.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        var rightEdit = new Style(typeof(TextBox), DataGridTextColumn.DefaultEditingElementStyle);
        rightEdit.Setters.Add(new Setter(TextBox.TextAlignmentProperty, TextAlignment.Right));
        DataGridTextColumn Column(string header, string path, DataGridLength width, string? format, bool editable = false) => new()
        {
            Header = header, Binding = new Binding(path) { StringFormat = format, ConverterCulture = Turkish }, Width = width, IsReadOnly = !editable,
            ElementStyle = format == null ? DataGridTextColumn.DefaultElementStyle : rightText,
            EditingElementStyle = format == null ? DataGridTextColumn.DefaultEditingElementStyle : rightEdit
        };
        grid.Columns.Add(Column("Stok Kodu", "StokKodu", 110, null));
        grid.Columns.Add(new DataGridTextColumn { Header = "Stok Adı", Binding = new Binding("StokAdi"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 200, IsReadOnly = true });
        grid.Columns.Add(Column("Depo", "Depo", 120, null));
        foreach (var (header, path, width, format) in new (string, string, double, string)[] { ("Kullanılabilir", "Kullanilabilir", 90, "N2"), ("Yolda", "Yolda", 70, "N2"), ("Min", "Min", 60, "N2"), ("Max", "Max", 60, "N2") })
            grid.Columns.Add(Column(header, path, width, format));
        grid.Columns.Add(Column("Tedarikçi", "Tedarikci", 170, null));
        grid.Columns.Add(Column("Tedarik Günü", "TedarikGunu", 80, "N0"));
        grid.Columns.Add(Column("Sipariş Miktarı ✎", "Miktar", 110, "N2", editable: true));
        grid.Columns.Add(Column("Birim Fiyat ✎", "Fiyat", 100, "N2", editable: true));
        grid.Columns.Add(Column("Tutar", "Tutar", 100, "N2"));

        var empty = new TextBlock { Text = "Minimumun altına düşen ürün yok. Depo/grup filtresini değiştirin ya da stok kartlarında minimum stok seviyesi tanımlayın.", Foreground = Token("R3.Text.Secondary.Brush"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        var loading = new LoadingOverlay();
        var gridHost = new Grid(); gridHost.Children.Add(grid); gridHost.Children.Add(empty); gridHost.Children.Add(loading);
        DataTable? table = null;
        var loadVersion = 0;

        void UpdateStatus()
        {
            if (table == null) return;
            var chosen = table.Rows.Cast<DataRow>().Where(r => r["Sec"] is true).ToList();
            status.Text = $"{table.Rows.Count} öneri • {chosen.Count} işaretli • {chosen.Sum(r => (decimal)r["Tutar"]).ToString("N2", Turkish)} ₺";
        }
        DataTable Build(IReadOnlyList<ReorderSuggestion> suggestions)
        {
            var t = new DataTable();
            foreach (var (n, type) in new[] { ("Sec", typeof(bool)), ("UrunId", typeof(string)), ("StokKodu", typeof(string)), ("StokAdi", typeof(string)), ("DepoId", typeof(string)), ("Depo", typeof(string)), ("Kullanilabilir", typeof(decimal)),
                         ("Yolda", typeof(decimal)), ("Min", typeof(decimal)), ("Max", typeof(decimal)), ("TedarikciId", typeof(string)), ("Tedarikci", typeof(string)), ("TedarikGunu", typeof(int)), ("Miktar", typeof(decimal)), ("Fiyat", typeof(decimal)), ("Tutar", typeof(decimal)) })
                t.Columns.Add(n, type);
            foreach (var s in suggestions)
                t.Rows.Add(s.SupplierId != null, s.ProductId, s.ProductCode, s.ProductName, s.WarehouseId, s.WarehouseName, s.Available, s.OnOrder, s.Minimum, s.Maximum,
                    (object?)s.SupplierId ?? DBNull.Value, s.SupplierName.Length == 0 ? "(tedarikçi tanımlı değil)" : s.SupplierName, s.LeadTimeDays, s.Suggested, s.LastCost, s.Suggested * s.LastCost);
            return t;
        }
        // Stale-result guard: a filter change while a load is running bumps loadVersion, so the older query's result is dropped.
        async Task Refresh()
        {
            var version = ++loadVersion;
            var warehouseId = warehouse.SelectedValue as string is { Length: > 0 } w ? w : null;
            var groupId = group.SelectedValue as string is { Length: > 0 } g ? g : null;
            loading.ShowLoading("Sipariş önerileri hesaplanıyor…");
            try
            {
                var loaded = await Task.Run(() => Build(service.Suggestions(companyId, warehouseId, groupId)));
                if (version != loadVersion) return;
                table = loaded;
                table.ColumnChanged += (_, e) =>
                {
                    if (e.Column?.ColumnName is "Miktar" or "Fiyat") e.Row["Tutar"] = (decimal)e.Row["Miktar"] * (decimal)e.Row["Fiyat"];
                    if (e.Column?.ColumnName != "Tutar") UpdateStatus();
                };
                grid.ItemsSource = table.DefaultView; UpdateStatus();
                empty.Visibility = table.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                if (version != loadVersion) return;
                DesktopLogging.CreateLogger<ReorderView>().LogWarning(ex, "Reorder suggestions failed to load.");
                status.Text = "Öneriler yüklenemedi — Yenile (F5) ile tekrar deneyin.";
                MessageBox.Show(Window.GetWindow(this)!, ex.Message, "Sipariş önerileri yüklenemedi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { if (version == loadVersion) loading.HideLoading(); }
        }
        void CreateOrders()
        {
            grid.CommitEdit(DataGridEditingUnit.Row, true);
            var chosen = table?.Rows.Cast<DataRow>().Where(r => r["Sec"] is true).ToList() ?? [];
            try
            {
                if (chosen.Count == 0) throw new ArgumentException("Önce sipariş verilecek satırları işaretleyin.");
                var lines = chosen.Select(r => new ReorderOrderLine(r["UrunId"].ToString()!, r["DepoId"].ToString()!, r["TedarikciId"] as string ?? "", (decimal)r["Miktar"], (decimal)r["Fiyat"])).ToList();
                var groupsCount = lines.GroupBy(l => (l.SupplierId, l.WarehouseId)).Count();
                if (MessageBox.Show(Window.GetWindow(this)!, $"{lines.Count} satırdan {groupsCount} taslak satınalma siparişi oluşturulacak. Devam edilsin mi?", "Sipariş Önerileri", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                var ids = service.CreatePurchaseOrders(companyId, lines, userName);
                _ = Refresh();
                if (MessageBox.Show(Window.GetWindow(this)!, $"{ids.Count} taslak satınalma siparişi oluşturuldu. Satınalma Siparişleri ekranı açılsın mı?", "Sipariş Önerileri", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes) openOrders();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
            { MessageBox.Show(Window.GetWindow(this)!, ex.Message, "Sipariş oluşturulamadı", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        // Keyboard bulk-mark: Space flips "Seç" on every selected row (all on unless all are already on).
        grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Space || Keyboard.FocusedElement is TextBox) return;
            var rows = grid.SelectedItems.OfType<DataRowView>().ToList();
            if (rows.Count == 0) return;
            var mark = !rows.All(r => r["Sec"] is true);
            foreach (var r in rows) r["Sec"] = mark;
            e.Handled = true;
        };
        foreach (var (text, action, primary) in new (string, Action, bool)[] { ("İşaretlilerden Sipariş Oluştur", CreateOrders, true), ("Satınalma Siparişleri", openOrders, false), ("Yenile (F5)", () => _ = Refresh(), false) })
        {
            var button = new Button { Content = text, Margin = new Thickness(8, 0, 0, 0) };
            if (primary) { button.Background = Token("R3.Accent.Brush"); button.Foreground = Token("R3.Text.OnAccent.Brush"); button.BorderThickness = new Thickness(0); }
            button.Click += (_, _) => action(); bar.Children.Add(button);
        }
        bar.Children.Add(status);
        warehouse.SelectionChanged += (_, _) => _ = Refresh(); group.SelectionChanged += (_, _) => _ = Refresh();
        ErpGridContext.Register(grid, "inventory.reorder",
            [new ContextActionDefinition("reorder.product", "Ürün Kartını Aç", "inventory.product.view", "", 10, ContextActionGroup.Primary, x => { if (x is DataRowView r) openProduct(r["UrunId"].ToString()!); return Task.FromResult(ContextActionResult.Ok()); })],
            Refresh, "ReorderSuggestion");
        KeyboardInteractionService.AttachListShortcuts(this, null, null, null, () => _ = Refresh());
        Children.Add(gridHost);
        Loaded += (_, _) => warehouse.Focus();
        _ = Refresh();
    }
}
