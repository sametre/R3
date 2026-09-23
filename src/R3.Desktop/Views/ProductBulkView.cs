using System.Data;
using R3.Desktop.Design;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using R3.Infrastructure;

namespace R3.Desktop.Views;

/// <summary>
/// Stok › Ürün Yönetimi › Toplu Ürün İşlemleri. Left: filter + tick the products; right: tick the fields
/// to change and their new values; Uygula runs <see cref="LocalProductBulkService.Apply"/> - one
/// transaction, so a single invalid product (e.g. max below min) leaves every product untouched.
/// </summary>
public sealed class ProductBulkView : DockPanel
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private readonly StoreDatabase _db;
    private readonly string _companyId, _userName;
    private readonly DataGrid _grid;
    private readonly TextBlock _selection = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Foreground = Ui.Brush("R3.Text.Secondary.Brush") };
    private DataTable? _rows;

    public ProductBulkView(StoreDatabase db, string companyId, string userName)
    {
        _db = db; _companyId = companyId; _userName = userName;
        Margin = new Thickness(18);
        var title = new TextBlock { Text = "Toplu Ürün İşlemleri", FontSize = Ui.Font.Title, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Text.Primary.Brush") };
        SetDock(title, Dock.Top); Children.Add(title);
        var help = new TextBlock { Text = "Soldan ürünleri filtreleyip işaretleyin, sağdan değiştirilecek alanları seçin. İşlem tek seferde uygulanır: bir üründe hata varsa hiçbir ürün değişmez. Her ürün için denetim kaydı yazılır.", Foreground = Ui.Brush("R3.Text.Secondary.Brush"), FontSize = Ui.Font.Caption, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) };
        SetDock(help, Dock.Top); Children.Add(help);

        var changes = BuildChangePanel(out var apply);
        SetDock(changes, Dock.Right); Children.Add(changes);

        var left = new DockPanel { Margin = new Thickness(0, 0, 14, 0) }; Children.Add(left);
        var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) }; SetDock(filters, Dock.Top); left.Children.Add(filters);
        var search = new TextBox { Width = 180, Padding = new Thickness(6, 3, 6, 3), ToolTip = "Kod, ad veya barkod" };
        var group = Lookup("SELECT id AS Id, code || ' — ' || name AS Name, code AS SortKey FROM product_groups WHERE company_id=$c", "Tüm gruplar");
        var brand = Lookup("SELECT id AS Id, code || ' — ' || name AS Name, code AS SortKey FROM brands WHERE company_id=$c", "Tüm markalar");
        var category = Lookup("SELECT id AS Id, code || ' — ' || name AS Name, code AS SortKey FROM categories WHERE company_id=$c", "Tüm kategoriler");
        var state = new ComboBox { Width = 110, Height = 26, ItemsSource = new[] { "Aktif", "Pasif", "Tümü" }, SelectedIndex = 0 };
        foreach (var (label, control) in new (string, Control)[] { ("Ara:", search), ("Grup:", group), ("Marka:", brand), ("Kategori:", category), ("Durum:", state) })
        { filters.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) }); filters.Children.Add(control); }

        var selectBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) }; SetDock(selectBar, Dock.Top); left.Children.Add(selectBar);
        Button(selectBar, "Tümünü işaretle", () => Mark(true)); Button(selectBar, "İşaretleri kaldır", () => Mark(false)); selectBar.Children.Add(_selection);

        _grid = new DataGrid
        {
            Style = (Style)System.Windows.Application.Current.FindResource("ProfessionalDataGridStyle"), AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Extended, HeadersVisibility = DataGridHeadersVisibility.Column, IsReadOnly = false
        };
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Seç", Binding = new Binding("Sec") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 40 });
        foreach (var (header, path, width) in new[] { ("Stok Kodu", "StokKodu", 120.0), ("Stok Adı", "StokAdi", 240.0), ("Grup", "Grup", 130.0), ("Marka", "Marka", 110.0), ("Kategori", "Kategori", 110.0), ("KDV", "Kdv", 50.0), ("Min", "Min", 60.0), ("Max", "Max", 60.0), ("Durum", "Durum", 60.0) })
            _grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path) { ConverterCulture = Turkish }, Width = width, IsReadOnly = true });
        _grid.CurrentCellChanged += (_, _) => UpdateSelection();
        // Space toggles every highlighted row at once - quick for "select these 40 rows".
        _grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Space || _grid.SelectedItems.Count == 0) return;
            var target = !_grid.SelectedItems.Cast<DataRowView>().All(r => r["Sec"] is true);
            foreach (DataRowView r in _grid.SelectedItems) r["Sec"] = target;
            e.Handled = true; UpdateSelection();
        };
        left.Children.Add(_grid);

        void Refresh()
        {
            _rows = db.Query("""
                SELECT 0 AS Sec, p.id AS Id, p.code AS StokKodu, p.name AS StokAdi, COALESCE(g.name,'') AS Grup, COALESCE(b.name,'') AS Marka, COALESCE(c.name,'') AS Kategori,
                       p.vat_rate AS Kdv, p.minimum_stock AS Min, p.maximum_stock AS Max, CASE WHEN p.is_active=1 THEN 'Aktif' ELSE 'Pasif' END AS Durum
                FROM products p LEFT JOIN product_groups g ON g.id=p.product_group_id LEFT JOIN brands b ON b.id=p.brand_id LEFT JOIN categories c ON c.id=p.category_id
                WHERE p.company_id=$c AND ($g='' OR p.product_group_id=$g) AND ($b='' OR p.brand_id=$b) AND ($cat='' OR p.category_id=$cat)
                  AND ($s='Tümü' OR ($s='Aktif' AND p.is_active=1) OR ($s='Pasif' AND p.is_active=0))
                  AND (p.code LIKE $q OR p.name LIKE $q OR EXISTS(SELECT 1 FROM product_barcodes x WHERE x.product_id=p.id AND x.barcode LIKE $q))
                ORDER BY p.code LIMIT 5000
                """, ("$c", companyId), ("$g", group.SelectedValue as string ?? ""), ("$b", brand.SelectedValue as string ?? ""), ("$cat", category.SelectedValue as string ?? ""),
                ("$s", state.SelectedItem as string ?? "Aktif"), ("$q", $"%{search.Text.Trim()}%"));
            // Query yields Sec as an integer; the checkbox column needs a writable bool column.
            var typed = new DataTable(); foreach (DataColumn col in _rows.Columns) typed.Columns.Add(col.ColumnName, col.ColumnName == "Sec" ? typeof(bool) : col.DataType);
            foreach (DataRow r in _rows.Rows) { var values = r.ItemArray; values[0] = false; typed.Rows.Add(values); }
            _rows = typed; _grid.ItemsSource = _rows.DefaultView; UpdateSelection();
        }
        group.SelectionChanged += (_, _) => Refresh(); brand.SelectionChanged += (_, _) => Refresh(); category.SelectionChanged += (_, _) => Refresh(); state.SelectionChanged += (_, _) => Refresh();
        KeyboardInteractionService.AttachDebouncedSearch(search, Refresh);
        apply.Click += (_, _) => Apply(Refresh);
        Refresh();
    }

    private Func<ProductBulkChange?>? _readChange;

    private Border BuildChangePanel(out Button apply)
    {
        var panel = new StackPanel { Width = 330 };
        panel.Children.Add(new TextBlock { Text = "Değiştirilecek alanlar", FontWeight = FontWeights.SemiBold, FontSize = Ui.Font.Section, Margin = new Thickness(0, 0, 0, 8) });
        (CheckBox, T) Row<T>(string label, T control) where T : Control
        {
            var check = new CheckBox { Content = label, Margin = new Thickness(0, 6, 0, 2), FontWeight = FontWeights.SemiBold };
            control.IsEnabled = false; control.Height = 24; control.Margin = new Thickness(20, 0, 0, 0);
            check.Click += (_, _) => control.IsEnabled = check.IsChecked == true;
            panel.Children.Add(check); panel.Children.Add(control); return (check, control);
        }
        var group = Row("Stok grubu", Lookup("SELECT id AS Id, code || ' — ' || name AS Name, code AS SortKey FROM product_groups WHERE company_id=$c AND is_active=1", "(Grubu kaldır)"));
        var brand = Row("Marka", Lookup("SELECT id AS Id, code || ' — ' || name AS Name, code AS SortKey FROM brands WHERE company_id=$c AND is_active=1", "(Markayı kaldır)"));
        var category = Row("Kategori", Lookup("SELECT id AS Id, code || ' — ' || name AS Name, code AS SortKey FROM categories WHERE company_id=$c AND is_active=1", "(Kategoriyi kaldır)"));
        var origin = Row("Menşe ülke", LookupGlobal("SELECT id AS Id, code || ' — ' || name AS Name, CASE code WHEN 'TR' THEN '0' ELSE '1' || name END AS SortKey FROM countries WHERE is_active=1", "(Ülkeyi kaldır)"));
        var vat = Row("Satış KDV %", new TextBox { Text = "20" });
        var purchaseVat = Row("Alış KDV %", new TextBox { Text = "20" });
        var min = Row("Minimum stok", new TextBox { Text = "0" });
        var max = Row("Maksimum stok", new TextBox { Text = "0" });
        var active = Row("Durum", new ComboBox { ItemsSource = new[] { "Aktif", "Pasif" }, SelectedIndex = 0 });
        var sellable = Row("Satışa açık", new ComboBox { ItemsSource = new[] { "Evet", "Hayır" }, SelectedIndex = 0 });
        apply = new Button { Content = "Seçili ürünlere uygula", Height = 30, Margin = new Thickness(0, 16, 0, 0), Background = Ui.Brush("R3.Accent.Brush"), Foreground = Ui.Brush("R3.Text.OnAccent.Brush"), BorderThickness = new Thickness(0) };
        panel.Children.Add(apply);

        static decimal? Number((CheckBox Check, TextBox Box) row, string label)
        {
            if (row.Check.IsChecked != true) return null;
            if (!decimal.TryParse(row.Box.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("tr-TR"), out var value)) throw new ArgumentException($"{label} geçerli bir sayı olmalıdır.");
            return value;
        }
        static string? Id((CheckBox Check, ComboBox Box) row) => row.Check.IsChecked == true ? row.Box.SelectedValue as string ?? "" : null;
        _readChange = () => new ProductBulkChange(Id(group), Id(brand), Id(category), Id(origin), Number(vat, "Satış KDV"), Number(purchaseVat, "Alış KDV"),
            active.Item1.IsChecked == true ? active.Item2.SelectedIndex == 0 : null, sellable.Item1.IsChecked == true ? sellable.Item2.SelectedIndex == 0 : null,
            Number(min, "Minimum stok"), Number(max, "Maksimum stok"));
        return new Border { Background = Ui.Brush("R3.Surface.Brush"), BorderBrush = Ui.Brush("R3.Border.Brush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(14), Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
    }

    private void Apply(Action refresh)
    {
        var ids = _rows?.Rows.Cast<DataRow>().Where(r => r["Sec"] is true).Select(r => r["Id"].ToString()!).ToList() ?? [];
        try
        {
            var change = _readChange!() ?? throw new ArgumentException("Değişiklik okunamadı.");
            if (ids.Count == 0) throw new ArgumentException("Önce soldan ürün işaretleyin (Seç sütunu veya satırları seçip Boşluk).");
            if (change.IsEmpty) throw new ArgumentException("Sağdan en az bir alanı işaretleyin.");
            if (MessageBox.Show(Window.GetWindow(this)!, $"{ids.Count:N0} ürün güncellenecek. Devam edilsin mi?", "Toplu Ürün İşlemleri", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var updated = new LocalProductBulkService(_db).Apply(_companyId, ids, change, _userName);
            refresh();
            MessageBox.Show(Window.GetWindow(this)!, $"{updated:N0} ürün güncellendi.", "Toplu Ürün İşlemleri", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        { MessageBox.Show(Window.GetWindow(this)!, ex.Message, "Toplu Ürün İşlemleri", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void Mark(bool value) { if (_rows == null) return; foreach (DataRow r in _rows.Rows) r["Sec"] = value; UpdateSelection(); }

    private void UpdateSelection() => _selection.Text = _rows == null ? "" : $"{_rows.Rows.Count:N0} ürün listelendi • {_rows.Rows.Cast<DataRow>().Count(r => r["Sec"] is true):N0} işaretli";

    private ComboBox Lookup(string sql, string emptyLabel) => Combo(_db.Query($"SELECT '' AS Id, '{emptyLabel}' AS Name, '' AS SortKey UNION ALL SELECT * FROM ({sql}) ORDER BY 3", ("$c", _companyId)));
    private ComboBox LookupGlobal(string sql, string emptyLabel) => Combo(_db.Query($"SELECT '' AS Id, '{emptyLabel}' AS Name, '' AS SortKey UNION ALL SELECT * FROM ({sql}) ORDER BY 3"));
    private static ComboBox Combo(DataTable data) => new() { Width = 200, Height = 26, ItemsSource = data.DefaultView, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedIndex = 0, IsTextSearchEnabled = true };

    private static void Button(Panel bar, string text, Action action)
    {
        var button = new Button { Content = text, Height = 26, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        button.Click += (_, _) => action(); bar.Children.Add(button);
    }
}
