using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using R3.Desktop.ContextActions;
using R3.Infrastructure;

namespace R3.Desktop.Views;

/// <summary>Stok › Sipariş Önerileri (Sipariş Seviyeleri): below-minimum products → draft satınalma siparişleri.</summary>
public sealed class ReorderView : DockPanel
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public ReorderView(StoreDatabase db, string companyId, string userName, Action<string> openProduct, Action openOrders)
    {
        var service = new LocalReorderService(db);
        Margin = new Thickness(18);
        var title = new TextBlock { Text = "Sipariş Önerileri", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(47, 56, 63)) };
        SetDock(title, Dock.Top); Children.Add(title);
        var help = new TextBlock { Text = "Kullanılabilir stoğu minimumun altına düşen ürünler (depo bazlı min/max önceliklidir). Öneri = maksimum (yoksa minimum) − kullanılabilir − yoldaki sipariş; minimum sipariş miktarına yükseltilir ve sipariş katına yuvarlanır. Miktar ve fiyatı düzenleyip işaretli satırlardan tedarikçi × depo başına bir taslak satınalma siparişi oluşturabilirsiniz; onay yine Satınalma Siparişleri ekranında yapılır.", Foreground = Brushes.DimGray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) };
        SetDock(help, Dock.Top); Children.Add(help);
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; SetDock(bar, Dock.Top); Children.Add(bar);
        var warehouses = db.Query("SELECT '' AS Id, 'Tüm depolar' AS Name, '' AS SortKey UNION ALL SELECT id, code || ' — ' || name, code FROM warehouses WHERE company_id=$c AND is_active=1 ORDER BY 3", ("$c", companyId));
        var warehouse = new ComboBox { Width = 190, Height = 26, ItemsSource = warehouses.DefaultView, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedIndex = 0 };
        var groups = db.Query("SELECT '' AS Id, 'Tüm stok grupları' AS Name, '' AS SortKey UNION ALL SELECT id, code || ' — ' || name, code FROM product_groups WHERE company_id=$c AND is_active=1 ORDER BY 3", ("$c", companyId));
        var group = new ComboBox { Width = 200, Height = 26, ItemsSource = groups.DefaultView, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedIndex = 0 };
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Foreground = Brushes.DimGray };
        foreach (var (label, control) in new (string, Control)[] { ("Depo:", warehouse), ("Grup:", group) })
        { bar.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 5, 0) }); bar.Children.Add(control); }

        var grid = new DataGrid { Style = (Style)System.Windows.Application.Current.FindResource("ProfessionalDataGridStyle"), AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Extended, HeadersVisibility = DataGridHeadersVisibility.Column };
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Seç", Binding = new Binding("Sec") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 40 });
        foreach (var (header, path, width, format) in new (string, string, double, string?)[] { ("Stok Kodu", "StokKodu", 110, null), ("Stok Adı", "StokAdi", 220, null), ("Depo", "Depo", 120, null), ("Kullanılabilir", "Kullanilabilir", 90, "N2"),
                     ("Yolda", "Yolda", 70, "N2"), ("Min", "Min", 60, "N2"), ("Max", "Max", 60, "N2"), ("Tedarikçi", "Tedarikci", 170, null), ("Tedarik Günü", "TedarikGunu", 80, "N0") })
            grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path) { StringFormat = format, ConverterCulture = Turkish }, Width = width, IsReadOnly = true });
        grid.Columns.Add(new DataGridTextColumn { Header = "Sipariş Miktarı ✎", Binding = new Binding("Miktar") { StringFormat = "N2", ConverterCulture = Turkish }, Width = 110 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Birim Fiyat ✎", Binding = new Binding("Fiyat") { StringFormat = "N2", ConverterCulture = Turkish }, Width = 100 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Tutar", Binding = new Binding("Tutar") { StringFormat = "N2", ConverterCulture = Turkish }, Width = 100, IsReadOnly = true });
        DataTable? table = null;

        void UpdateStatus()
        {
            if (table == null) return;
            var chosen = table.Rows.Cast<DataRow>().Where(r => r["Sec"] is true).ToList();
            status.Text = $"{table.Rows.Count} öneri • {chosen.Count} işaretli • {chosen.Sum(r => (decimal)r["Tutar"]).ToString("N2", Turkish)} ₺";
        }
        void Refresh()
        {
            table = new DataTable();
            foreach (var (n, t) in new[] { ("Sec", typeof(bool)), ("UrunId", typeof(string)), ("StokKodu", typeof(string)), ("StokAdi", typeof(string)), ("DepoId", typeof(string)), ("Depo", typeof(string)), ("Kullanilabilir", typeof(decimal)),
                         ("Yolda", typeof(decimal)), ("Min", typeof(decimal)), ("Max", typeof(decimal)), ("TedarikciId", typeof(string)), ("Tedarikci", typeof(string)), ("TedarikGunu", typeof(int)), ("Miktar", typeof(decimal)), ("Fiyat", typeof(decimal)), ("Tutar", typeof(decimal)) })
                table.Columns.Add(n, t);
            foreach (var s in service.Suggestions(companyId, warehouse.SelectedValue as string is { Length: > 0 } w ? w : null, group.SelectedValue as string is { Length: > 0 } g ? g : null))
                table.Rows.Add(s.SupplierId != null, s.ProductId, s.ProductCode, s.ProductName, s.WarehouseId, s.WarehouseName, s.Available, s.OnOrder, s.Minimum, s.Maximum,
                    (object?)s.SupplierId ?? DBNull.Value, s.SupplierName.Length == 0 ? "(tedarikçi tanımlı değil)" : s.SupplierName, s.LeadTimeDays, s.Suggested, s.LastCost, s.Suggested * s.LastCost);
            table.ColumnChanged += (_, e) =>
            {
                if (e.Column?.ColumnName is "Miktar" or "Fiyat") e.Row["Tutar"] = (decimal)e.Row["Miktar"] * (decimal)e.Row["Fiyat"];
                if (e.Column?.ColumnName != "Tutar") UpdateStatus();
            };
            grid.ItemsSource = table.DefaultView; UpdateStatus();
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
                Refresh();
                if (MessageBox.Show(Window.GetWindow(this)!, $"{ids.Count} taslak satınalma siparişi oluşturuldu. Satınalma Siparişleri ekranı açılsın mı?", "Sipariş Önerileri", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes) openOrders();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
            { MessageBox.Show(Window.GetWindow(this)!, ex.Message, "Sipariş oluşturulamadı", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        foreach (var (text, action, primary) in new (string, Action, bool)[] { ("İşaretlilerden Sipariş Oluştur", CreateOrders, true), ("Satınalma Siparişleri", openOrders, false), ("Yenile (F5)", Refresh, false) })
        {
            var button = new Button { Content = text, Height = 26, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0) };
            if (primary) { button.Background = new SolidColorBrush(Color.FromRgb(22, 124, 130)); button.Foreground = Brushes.White; button.BorderThickness = new Thickness(0); }
            button.Click += (_, _) => action(); bar.Children.Add(button);
        }
        bar.Children.Add(status);
        warehouse.SelectionChanged += (_, _) => Refresh(); group.SelectionChanged += (_, _) => Refresh();
        ErpGridContext.Register(grid, "inventory.reorder",
            [new ContextActionDefinition("reorder.product", "Ürün Kartını Aç", "inventory.product.view", "", 10, ContextActionGroup.Primary, x => { if (x is DataRowView r) openProduct(r["UrunId"].ToString()!); return Task.FromResult(ContextActionResult.Ok()); })],
            () => { Refresh(); return Task.CompletedTask; }, "ReorderSuggestion");
        KeyboardInteractionService.AttachListShortcuts(this, null, null, null, Refresh);
        Children.Add(grid); Refresh();
    }
}
