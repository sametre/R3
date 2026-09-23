using System.Data;
using R3.Desktop.Design;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using R3.Desktop.ContextActions;
using R3.Infrastructure;

namespace R3.Desktop.Views;

/// <summary>Stok Raporları: Ürün Ekstresi and Stok Değer Raporu (LocalInventoryReportService).</summary>
public static class InventoryReportViews
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly Brush Muted = Ui.Brush("R3.Text.Secondary.Brush");
    private static readonly Brush Accent = Ui.Brush("R3.Accent.Brush");

    public static UIElement ProductLedger(StoreDatabase db, string companyId, string? productId, Action<string> openProduct)
    {
        var reports = new LocalInventoryReportService(db); var resolver = new LocalBarcodeResolver(db);
        var root = Shell("Ürün Ekstresi", "Seçilen ürünün tarih aralığındaki tüm stok hareketleri ve yürüyen bakiyesi. Başlangıç tarihinden önceki hareketler devir olarak gösterilir.", out var bar);
        var barcode = new TextBox { Width = 150, Padding = new Thickness(6, 3, 6, 3), ToolTip = "Barkod veya ürün kodu okutun (Enter)" };
        var products = db.Query("SELECT id AS Id, code || ' — ' || name AS Display FROM products WHERE company_id=$c AND product_type<>'Service' ORDER BY code", ("$c", companyId));
        var product = new ComboBox { Width = 300, Height = 26, ItemsSource = products.DefaultView, DisplayMemberPath = "Display", SelectedValuePath = "Id", IsEditable = true, IsTextSearchEnabled = true, SelectedValue = productId ?? "" };
        var warehouse = WarehouseCombo(db, companyId);
        var from = new DatePicker { SelectedDate = new DateTime(DateTime.Today.Year, 1, 1), Width = 115 }; var to = new DatePicker { SelectedDate = DateTime.Today, Width = 115 };
        Label(bar, "Barkod:"); bar.Children.Add(barcode); Label(bar, "Ürün:"); bar.Children.Add(product); Label(bar, "Depo:"); bar.Children.Add(warehouse);
        Label(bar, "Tarih:"); bar.Children.Add(from); Label(bar, "–"); bar.Children.Add(to);
        var summary = SummaryStrip(root, out var cards);
        var grid = Grid();
        Col(grid, "Tarih", "Tarih", 125); Col(grid, "Depo", "Depo", 130); Col(grid, "İşlem", "Tur", 140); Col(grid, "Referans", "Referans", 120); Col(grid, "Açıklama", "Aciklama", 220);
        Col(grid, "Giriş", "Giris", 90, "N2"); Col(grid, "Çıkış", "Cikis", 90, "N2"); Col(grid, "Bakiye", "Bakiye", 100, "N2"); Col(grid, "Birim Maliyet", "BirimMaliyet", 100, "N2");

        void Refresh()
        {
            if (product.SelectedValue is not string id || id.Length == 0) { grid.ItemsSource = null; cards(["Devir", "—"], ["Giriş", "—"], ["Çıkış", "—"], ["Kapanış", "—"]); return; }
            var report = reports.ProductLedger(companyId, id, warehouse.SelectedValue as string is { Length: > 0 } w ? w : null, from.SelectedDate, to.SelectedDate);
            foreach (DataRow row in report.Lines.Rows) row["Tur"] = R3.Desktop.Presentation.InventoryPresentation.TransactionTypeLabel(row["Tur"].ToString()!);
            grid.ItemsSource = report.Lines.DefaultView;
            cards(["Devir", report.OpeningBalance.ToString("N2", Turkish)], ["Giriş", report.TotalIn.ToString("N2", Turkish)], ["Çıkış", report.TotalOut.ToString("N2", Turkish)], ["Kapanış bakiyesi", report.ClosingBalance.ToString("N2", Turkish)]);
        }
        barcode.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return; e.Handled = true;
            try { product.SelectedValue = resolver.ResolveForInventory(barcode.Text, companyId).ProductId; barcode.SelectAll(); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { MessageBox.Show(Window.GetWindow(root)!, ex.Message, "Ürün Ekstresi", MessageBoxButton.OK, MessageBoxImage.Information); }
        };
        product.SelectionChanged += (_, _) => Refresh(); warehouse.SelectionChanged += (_, _) => Refresh();
        from.SelectedDateChanged += (_, _) => Refresh(); to.SelectedDateChanged += (_, _) => Refresh();
        Button(bar, "Ürün Kartı", () => { if (product.SelectedValue is string id && id.Length > 0) openProduct(id); });
        Button(bar, "Yenile (F5)", Refresh, true);
        ErpGridContext.Register(grid, "inventory.productledger", [], () => { Refresh(); return Task.CompletedTask; }, "InventoryTransaction");
        KeyboardInteractionService.AttachListShortcuts(root, null, null, null, Refresh);
        root.Children.Add(grid); Refresh();
        root.Loaded += (_, _) => (productId == null ? barcode : (Control)grid).Focus();
        return root;
    }

    public static UIElement StockValuation(StoreDatabase db, string companyId, Action<string> openProduct, Action<string> openLedger)
    {
        var reports = new LocalInventoryReportService(db);
        var root = Shell("Stok Değer Raporu", "Seçilen tarih sonundaki stok miktarı × ağırlıklı ortalama maliyet. Maliyet, ürünün maliyetli giriş hareketlerinden (açılış, alış, manuel giriş, sayım fazlası; transferler hariç) hesaplanır. Hiç maliyetli girişi olmayan ürünler \"Maliyet yok\" olarak listelenir ve toplamı düşürür.", out var bar);
        var warehouse = WarehouseCombo(db, companyId);
        var groups = db.Query("SELECT '' AS Id, 'Tüm stok grupları' AS Name, '' AS SortKey UNION ALL SELECT id, code || ' — ' || name, code FROM product_groups WHERE company_id=$c AND is_active=1 ORDER BY 3", ("$c", companyId));
        var group = new ComboBox { Width = 220, Height = 26, ItemsSource = groups.DefaultView, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedIndex = 0 };
        var asOf = new DatePicker { SelectedDate = DateTime.Today, Width = 115 };
        var includeZero = new CheckBox { Content = "Sıfır bakiyeleri göster", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        Label(bar, "Depo:"); bar.Children.Add(warehouse); Label(bar, "Stok grubu:"); bar.Children.Add(group); Label(bar, "Tarih:"); bar.Children.Add(asOf); bar.Children.Add(includeZero);
        SummaryStrip(root, out var cards);
        var grid = Grid();
        Col(grid, "Stok Kodu", "StokKodu", 130); Col(grid, "Stok Adı", "StokAdi", 260); Col(grid, "Stok Grubu", "StokGrubu", 160); Col(grid, "Depo", "Depo", 130);
        Col(grid, "Miktar", "Miktar", 90, "N2"); Col(grid, "Ort. Maliyet", "OrtalamaMaliyet", 100, "N2"); Col(grid, "Tutar", "Tutar", 120, "N2"); Col(grid, "Maliyet Kaynağı", "MaliyetKaynagi", 130);

        void Refresh()
        {
            var table = reports.StockValuation(companyId, warehouse.SelectedValue as string is { Length: > 0 } w ? w : null, group.SelectedValue as string is { Length: > 0 } g ? g : null, asOf.SelectedDate, includeZero.IsChecked == true);
            grid.ItemsSource = table.DefaultView;
            var rows = table.Rows.Cast<DataRow>().ToList();
            cards(["Toplam stok değeri", rows.Sum(r => (decimal)r["Tutar"]).ToString("N2", Turkish) + " ₺"], ["Satır", rows.Count.ToString("N0", Turkish)],
                ["Toplam miktar", rows.Sum(r => (decimal)r["Miktar"]).ToString("N2", Turkish)], ["Maliyeti olmayan", rows.Count(r => r["MaliyetKaynagi"].ToString() == "Maliyet yok").ToString("N0", Turkish)]);
        }
        warehouse.SelectionChanged += (_, _) => Refresh(); group.SelectionChanged += (_, _) => Refresh(); asOf.SelectedDateChanged += (_, _) => Refresh(); includeZero.Click += (_, _) => Refresh();
        Button(bar, "Yenile (F5)", Refresh, true);
        ErpGridContext.Register(grid,
        "inventory.valuation",
        [
            new ContextActionDefinition("valuation.product", "Ürün Kartını Aç", "inventory.product.view", "", 10, ContextActionGroup.Primary, x => { if (x is DataRowView r) openProduct(r["UrunId"].ToString()!); return Task.FromResult(ContextActionResult.Ok()); }),
            new ContextActionDefinition("valuation.ledger", "Ürün Ekstresi", "inventory.transaction.view", "", 10, ContextActionGroup.Related, x => { if (x is DataRowView r) openLedger(r["UrunId"].ToString()!); return Task.FromResult(ContextActionResult.Ok()); })
        ], () => { Refresh(); return Task.CompletedTask; }, "InventoryValuation");
        KeyboardInteractionService.AttachListShortcuts(root, null, null, null, Refresh);
        root.Children.Add(grid); Refresh();
        return root;
    }

    private static ComboBox WarehouseCombo(StoreDatabase db, string companyId)
    {
        var data = db.Query("SELECT '' AS Id, 'Tüm depolar' AS Name, '' AS SortKey UNION ALL SELECT id, code || ' — ' || name, code FROM warehouses WHERE company_id=$c ORDER BY 3", ("$c", companyId));
        return new ComboBox { Width = 190, Height = 26, ItemsSource = data.DefaultView, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedIndex = 0 };
    }

    // Four small KPI cards under the toolbar; the returned setter takes (label, value) pairs.
    private static WrapPanel SummaryStrip(DockPanel root, out Action<string[], string[], string[], string[]> set)
    {
        var strip = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(strip, Dock.Top); root.Children.Add(strip);
        var labels = new TextBlock[4]; var values = new TextBlock[4];
        for (var i = 0; i < 4; i++)
        {
            var card = new Border { Background = Ui.Brush("R3.Surface.Brush"), BorderBrush = Ui.Brush("R3.Border.Brush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0), MinWidth = 150 };
            var stack = new StackPanel(); labels[i] = new TextBlock { FontSize = Ui.Font.Grid, Foreground = Muted }; values[i] = new TextBlock { FontSize = Ui.Font.Title, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Text.Primary.Brush") };
            stack.Children.Add(labels[i]); stack.Children.Add(values[i]); card.Child = stack; strip.Children.Add(card);
        }
        set = (a, b, c, d) => { var all = new[] { a, b, c, d }; for (var i = 0; i < 4; i++) { labels[i].Text = all[i][0]; values[i].Text = all[i][1]; } };
        return strip;
    }

    private static DockPanel Shell(string title, string help, out WrapPanel bar)
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var header = new TextBlock { Text = title, FontSize = Ui.Font.Title, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Text.Primary.Brush") };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var note = new TextBlock { Text = help, Foreground = Muted, FontSize = Ui.Font.Caption, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) };
        DockPanel.SetDock(note, Dock.Top); root.Children.Add(note);
        bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        return root;
    }

    private static void Label(Panel bar, string text) => bar.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 5, 0) });

    private static DataGrid Grid() => new()
    {
        Style = (Style)System.Windows.Application.Current.FindResource("ProfessionalDataGridStyle"),
        IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column
    };

    private static void Col(DataGrid grid, string header, string path, double width, string? format = null) =>
        grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path) { StringFormat = format, ConverterCulture = Turkish }, Width = width });

    private static void Button(Panel bar, string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, Height = 26, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0) };
        if (primary) { button.Background = Accent; button.Foreground = Ui.Brush("R3.Text.OnAccent.Brush"); button.BorderThickness = new Thickness(0); }
        button.Click += (_, _) => action(); bar.Children.Add(button);
    }
}
