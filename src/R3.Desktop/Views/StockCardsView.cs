using System.Data;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using R3.Desktop.ContextActions;
using R3.Desktop.Design;
using R3.Desktop.Logging;
using R3.Desktop.Presentation;
using R3.Infrastructure;
using WpfUi = Wpf.Ui.Controls;

namespace R3.Desktop.Views;

/// <summary>Actions of other screens the Stok Kartları right-click menu links to (owned by MainWindow).</summary>
public sealed record StockCardsLinks(Action Movements, Action Balances, Action Receive, Action Issue, Action Transfer, Action Count, Action<string> PrintLabels);

/// <summary>
/// Stok › Stok Kartları, ASB ekranının birebir düzeni: komut çubuğu (Kayıt Ekle · Kayıt Güncelle/Sil · Detay Gör ·
/// Excel · Print · Yenile · İşlemler · Kapat), seçenek satırı (Fiyat Tipi, Stok Kategorileri / Barkodları / Ölçüleri
/// Göster, Varyant Kolonda), "Stok Kartları" başlık bandı, gruplama alanı, kolon filtre satırı, satır başında "+"
/// detay (barkodlar, depo stokları) ve alt bilgi "N adet aktif stok kartı mevcut". Firmanın bütün kartları tek listede
/// (LocalStockCardService.List, arka planda); ızgara sanal kaydırır.
/// Klavye: Enter/F3 kartı aç · F2/Ins yeni kart · F5 yenile · Ctrl+F filtre satırı · Ctrl+P yazdır · Ctrl+E Excel.
/// </summary>
public sealed class StockCardsView : DockPanel
{
    private readonly StoreDatabase _db;
    private readonly LocalStockCardService _cards;
    private readonly string _companyId;
    private readonly bool? _activeOnly;
    private readonly StockCardsLinks _links;
    private readonly DataGrid _grid;
    private readonly GridFilterRow _filterRow;
    private readonly GridGroupPanel _groupPanel;
    private readonly LoadingOverlay _loading = new();
    private readonly TextBlock _empty = Ui.EmptyState("Bu firmada stok kartı yok. Kayıt Ekle (F2) ile ilk kartı oluşturun.");
    private readonly TextBlock _footer = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _priceOption = new() { Content = "Fiyat Tipi" }, _categoriesOption = new() { Content = "Stok Kategorileri Göster" };
    private readonly CheckBox _barcodesOption = new() { Content = "Barkodları Göster" }, _unitsOption = new() { Content = "Ölçüleri  Göster" }, _variantsOption = new() { Content = "Varyant Kolonda" };
    private readonly ComboBox _priceList = new() { Width = 200, DisplayMemberPath = "Ad", SelectedValuePath = "Id", IsEnabled = false };
    private readonly Dictionary<string, DataGridColumn> _columns = [];
    private int _loadVersion, _total;

    public StockCardsView(StoreDatabase db, string companyId, bool? activeOnly, StockCardsLinks links, Action close)
    {
        _db = db; _cards = new LocalStockCardService(db); _companyId = companyId; _activeOnly = activeOnly; _links = links;
        Background = Ui.Brush("R3.Background.Brush");

        // 1) Komut çubuğu
        var toolbar = new WrapPanel { Margin = new Thickness(Ui.Space.S, Ui.Space.S, 0, Ui.Space.S) };
        toolbar.Children.Add(Command("Kayıt Ekle", WpfUi.SymbolRegular.AddSquare24, () => OpenCard(null), "Yeni stok kartı (F2 / Ins)"));
        toolbar.Children.Add(Command("Kayıt Güncelle/Sil", WpfUi.SymbolRegular.Edit24, EditSelected, "Seçili kartı aç (Enter / F3)"));
        toolbar.Children.Add(Command("Detay Gör", WpfUi.SymbolRegular.DocumentSearch24, ToggleDetail, "Seçili satırın barkod ve depo stoklarını göster / gizle"));
        toolbar.Children.Add(new Separator { Margin = new Thickness(Ui.Space.L, Ui.Space.S, Ui.Space.L, Ui.Space.S) });
        toolbar.Children.Add(Command("Excel", WpfUi.SymbolRegular.DocumentTable24, Export, "Listede görünen satırları Excel'e aktar (Ctrl+E)"));
        toolbar.Children.Add(Command("Print", WpfUi.SymbolRegular.Print24, Print, "Listede görünen satırları yazdır (Ctrl+P)"));
        toolbar.Children.Add(Command("Yenile", WpfUi.SymbolRegular.ArrowSync24, () => _ = LoadAsync(), "Listeyi yenile (F5)"));
        toolbar.Children.Add(new Separator { Margin = new Thickness(Ui.Space.L, Ui.Space.S, Ui.Space.L, Ui.Space.S) });
        var actions = Command("İşlemler ▾", WpfUi.SymbolRegular.TaskListLtr24, () => { }, "Kopyala, Aktif/Pasif, Barkod Yazdır, Kolonlar");
        actions.ContextMenu = ActionsMenu(); actions.Click += (_, _) => { actions.ContextMenu.PlacementTarget = actions; actions.ContextMenu.Placement = PlacementMode.Bottom; actions.ContextMenu.IsOpen = true; };
        toolbar.Children.Add(actions);
        toolbar.Children.Add(Command("Kapat", WpfUi.SymbolRegular.DismissSquare24, close, "Stok Kartları sekmesini kapat"));

        // 2) Seçenek satırı
        var options = new WrapPanel { Margin = new Thickness(Ui.Space.M, Ui.Space.S, 0, Ui.Space.M) };
        options.Children.Add(_priceOption); options.Children.Add(_priceList);
        foreach (var box in new[] { _categoriesOption, _barcodesOption, _unitsOption, _variantsOption }) { box.Margin = new Thickness(Ui.Space.XXL, 0, 0, 0); options.Children.Add(box); }
        _priceOption.VerticalAlignment = VerticalAlignment.Center; _priceList.Margin = new Thickness(Ui.Space.SM, 0, 0, 0);
        AutomationProperties.SetName(_priceList, "Fiyat tipi");

        // 3) Izgara
        _grid = new DataGrid
        {
            Style = (Style)System.Windows.Application.Current.FindResource("ProfessionalDataGridStyle"), AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            SelectionMode = DataGridSelectionMode.Extended, HeadersVisibility = DataGridHeadersVisibility.All, RowHeaderWidth = 12, FrozenColumnCount = 2,
            CanUserReorderColumns = true, CanUserResizeColumns = true, CanUserSortColumns = true, RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.Collapsed,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        VirtualizingPanel.SetIsVirtualizing(_grid, true); VirtualizingPanel.SetVirtualizationMode(_grid, VirtualizationMode.Recycling);
        AutomationProperties.SetAutomationId(_grid, "StockCardsGrid"); AutomationProperties.SetName(_grid, "Stok kartları listesi");
        BuildColumns();
        _filterRow = new GridFilterRow(_grid);
        _grid.ColumnHeaderHeight = 58; // iki satırlık başlık ("Sevk Yeri" gibi) + filtre kutusu
        _filterRow.Changed += (_, _) => UpdateFooter();
        _groupPanel = new GridGroupPanel(_grid);
        _grid.RowDetailsTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
        _grid.LoadingRowDetails += (_, e) => _ = FillDetailAsync(e.Row, (ContentPresenter)e.DetailsElement);

        var titleBand = new Border
        {
            Background = Ui.Brush("R3.Surface.Alt.Brush"), BorderBrush = Ui.Border, BorderThickness = new Thickness(1), Padding = new Thickness(0, Ui.Space.SM, 0, Ui.Space.SM),
            Child = new TextBlock { Text = "Stok Kartları", FontWeight = FontWeights.SemiBold, Foreground = Ui.TextPrimary, HorizontalAlignment = HorizontalAlignment.Center }
        };
        var footerBox = new Border { BorderBrush = Ui.Border, BorderThickness = new Thickness(1), Background = Ui.Surface, Padding = new Thickness(Ui.Space.XXL * 2, Ui.Space.S, Ui.Space.L, Ui.Space.S), Margin = new Thickness(150, Ui.Space.S, 0, Ui.Space.S), HorizontalAlignment = HorizontalAlignment.Left, Child = _footer };

        var body = new DockPanel { Margin = new Thickness(Ui.Space.S, 0, Ui.Space.S, 0) };
        SetDock(titleBand, Dock.Top); SetDock(_groupPanel, Dock.Top); SetDock(footerBox, Dock.Bottom);
        body.Children.Add(titleBand); body.Children.Add(_groupPanel); body.Children.Add(footerBox);
        body.Children.Add(Ui.Layered(_grid, _empty, _loading));

        var top = new StackPanel { Background = Ui.Surface };
        top.Children.Add(toolbar); top.Children.Add(new Border { Height = 1, Background = Ui.Border }); top.Children.Add(options);
        SetDock(top, Dock.Top); Children.Add(top); Children.Add(body);

        // 4) Olaylar
        _priceOption.Checked += (_, _) => { _priceList.IsEnabled = true; Show("price", true); _ = LoadAsync(); };
        _priceOption.Unchecked += (_, _) => { _priceList.IsEnabled = false; Show("price", false); _ = LoadAsync(); };
        _priceList.SelectionChanged += (_, _) => { if (_priceOption.IsChecked == true) _ = LoadAsync(); };
        _categoriesOption.Checked += (_, _) => Show("categories", true); _categoriesOption.Unchecked += (_, _) => Show("categories", false);
        _barcodesOption.Checked += (_, _) => Show("barcodes", true); _barcodesOption.Unchecked += (_, _) => Show("barcodes", false);
        _unitsOption.Checked += (_, _) => Show("units", true); _unitsOption.Unchecked += (_, _) => Show("units", false);
        _variantsOption.Checked += (_, _) => Show("variants", true); _variantsOption.Unchecked += (_, _) => Show("variants", false);
        _grid.SelectionChanged += (_, _) => UpdateFooter();

        ErpGridContext.Register(_grid, "inventory.stock-cards", StandardContextActions.Products(
            EditSelected, EditSelected, links.Movements, links.Balances, links.Receive, links.Issue, links.Transfer, links.Count),
            () => LoadAsync(), "Product");
        KeyboardInteractionService.AttachListShortcuts(this, null, () => OpenCard(null), EditSelected, () => _ = LoadAsync());
        PreviewKeyDown += (_, e) =>
        {
            var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (e.Key == Key.Insert && !(e.OriginalSource is TextBox)) { OpenCard(null); e.Handled = true; }
            else if (ctrl && e.Key == Key.F) { _filterRow.FocusFirst(); e.Handled = true; }
            else if (ctrl && e.Key == Key.P) { Print(); e.Handled = true; }
            else if (ctrl && e.Key == Key.E) { Export(); e.Handled = true; }
        };
        Loaded += async (_, _) => { if (_grid.ItemsSource == null) { await LoadPriceListsAsync(); await LoadAsync(); } };
    }

    // ---- kolonlar -------------------------------------------------------------------------------------------

    private void BuildColumns()
    {
        var plus = new FrameworkElementFactory(typeof(ToggleButton));
        plus.SetValue(ContentControl.ContentProperty, "+"); plus.SetValue(Control.PaddingProperty, new Thickness(0)); plus.SetValue(FrameworkElement.MinHeightProperty, 0.0); plus.SetValue(FrameworkElement.MinWidthProperty, 0.0);
        plus.SetValue(FrameworkElement.WidthProperty, 14.0); plus.SetValue(FrameworkElement.HeightProperty, 14.0); plus.SetValue(Control.FontSizeProperty, 10.0); plus.SetValue(UIElement.FocusableProperty, false);
        plus.SetValue(ToolTipProperty, "Barkodlar ve depo stokları");
        plus.AddHandler(ToggleButton.ClickEvent, new RoutedEventHandler((s, _) =>
        {
            var button = (ToggleButton)s;
            if (DataGridRow.GetRowContainingElement(button) is { } row)
            {
                row.DetailsVisibility = button.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                button.Content = button.IsChecked == true ? "−" : "+";
            }
        }));
        _grid.Columns.Add(new DataGridTemplateColumn { CellTemplate = new DataTemplate { VisualTree = plus }, Width = 22, CanUserResize = false, CanUserReorder = false });

        void Text(string key, string header, string path, double width, bool visible = true) => Add(key, new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = width, SortMemberPath = path }, visible);
        void Number(string key, string header, string path, double width, string format, bool visible = true) =>
            Add(key, Ui.NumberColumn(header, path, width, format), visible);
        Text("code", "Stok Kodu", "StokKodu", 130); Text("name", "Stok Adı", "StokAdi", 235);
        Text("type", "Tip", "TipAdi", 60); Text("unit", "Birim", "Birim", 45);
        Text("groupCode", "Grup Kodu", "GrupKodu", 62); Text("groupName", "Grup Adı", "GrupAdi", 130);
        Number("vat", "Kdv%", "KdvOrani", 45, "N2"); Text("shipment", "Sevk\nYeri", "SevkYeriAdi", 60);
        Text("supplier", "Tedarikçi Adı", "TedarikciAdi", 120); Number("lead", "Tedarik\nGün", "TedarikGun", 60, "N0");
        Text("color", "Renk Adı", "RenkAdi", 75); Text("sizeType", "Beden\nTipi", "BedenTipi", 50); Text("size", "Beden", "Beden", 75);
        Number("price", "Fiyat", "Fiyat", 90, "N2", false);
        Text("brand", "Marka", "Marka", 110, false); Text("category", "Kategori", "Kategori", 110, false); Text("origin", "Menşei", "Mense", 90, false);
        Text("barcode", "Birincil Barkod", "BirincilBarkod", 120, false); Number("barcodeCount", "Barkod\nSayısı", "BarkodSayisi", 55, "N0", false);
        Text("units", "Ölçüler", "Olculer", 140, false); Text("variants", "Varyantlar", "Varyantlar", 160, false);
    }

    private void Add(string key, DataGridColumn column, bool visible)
    {
        column.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _columns[key] = column; _grid.Columns.Add(column);
    }

    private static readonly Dictionary<string, string[]> OptionColumns = new()
    {
        ["price"] = ["price"], ["categories"] = ["brand", "category", "origin"], ["barcodes"] = ["barcode", "barcodeCount"], ["units"] = ["units"], ["variants"] = ["variants"]
    };

    private void Show(string option, bool visible) { foreach (var key in OptionColumns[option]) _columns[key].Visibility = visible ? Visibility.Visible : Visibility.Collapsed; }

    // ---- veri -----------------------------------------------------------------------------------------------

    private async Task LoadPriceListsAsync()
    {
        try
        {
            var lists = await Task.Run(() => new LocalPriceService(_db).PriceLists(_companyId, activeOnly: true));
            _priceList.ItemsSource = lists.DefaultView;
            if (lists.Rows.Count > 0) _priceList.SelectedIndex = 0; else _priceOption.IsEnabled = false;
        }
        catch (Exception ex) { DesktopLogging.CreateLogger<StockCardsView>().LogWarning(ex, "Price lists failed to load."); _priceOption.IsEnabled = false; }
    }

    /// <summary>Loads every card of the company off the UI thread; an older, slower load never overwrites a newer one.</summary>
    public async Task LoadAsync()
    {
        var version = ++_loadVersion;
        var selected = SelectedId();
        var priceList = _priceOption.IsChecked == true ? _priceList.SelectedValue as string : null;
        _loading.ShowLoading("Stok kartları yükleniyor…");
        try
        {
            var (table, total) = await Task.Run(() =>
            {
                var t = _cards.List(_companyId, _activeOnly, priceList);
                t.Locale = Ui.Turkish; // filter row: i/İ, ş, ğ compare the Turkish way
                t.Columns.Add("TipAdi", typeof(string)); t.Columns.Add("SevkYeriAdi", typeof(string));
                foreach (DataRow row in t.Rows)
                {
                    row["TipAdi"] = InventoryPresentation.ProductTypeLabel(row["UrunTipi"].ToString()!);
                    row["SevkYeriAdi"] = StockCardPresentation.ShipmentLabel(row["SevkYeri"].ToString());
                }
                return (t, _activeOnly == true ? t.Rows.Count : _cards.ActiveCount(_companyId));
            });
            if (version != _loadVersion) return;
            _total = total;
            _grid.ItemsSource = table.DefaultView;
            _filterRow.Apply(); _groupPanel.Apply();
            _empty.Visibility = table.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (selected != null) Reselect(selected);
            UpdateFooter();
        }
        catch (Exception ex)
        {
            if (version != _loadVersion) return;
            DesktopLogging.CreateLogger<StockCardsView>().LogError(ex, "Stock cards failed to load.");
            _empty.Text = "Stok kartları yüklenemedi: " + ex.Message + " Yenile (F5) ile tekrar deneyin.";
            _empty.Visibility = Visibility.Visible;
        }
        finally { if (version == _loadVersion) _loading.HideLoading(); }
    }

    private void UpdateFooter()
    {
        var text = StockCardPresentation.CountText(_total, _activeOnly ?? true);
        if (_grid.ItemsSource is DataView view && _filterRow.IsActive) text += $"  •  filtre: {view.Count.ToString("N0", Ui.Turkish)}";
        if (_grid.SelectedItems.Count > 1) text += $"  •  {_grid.SelectedItems.Count} seçili";
        _footer.Text = text;
    }

    private string? SelectedId() => (_grid.SelectedItem as DataRowView)?["Id"]?.ToString();

    private void Reselect(string id)
    {
        foreach (var item in _grid.Items)
            if (item is DataRowView row && row["Id"].ToString() == id) { _grid.SelectedItem = item; _grid.ScrollIntoView(item); return; }
    }

    private List<DataRow> VisibleRows() => _grid.Items.OfType<DataRowView>().Select(r => r.Row).ToList();

    // ---- komutlar -------------------------------------------------------------------------------------------

    private void EditSelected()
    {
        if (SelectedId() is { } id) OpenCard(id);
        else MessageBox.Show(Window.GetWindow(this), "Önce listeden bir stok kartı seçin.", "Stok Kartı", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenCard(string? productId)
    {
        var window = new StockCardWindow(new ViewModels.StockCardViewModel(_db, _companyId, productId, SiblingIds()), _db) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
        if (window.Changed) _ = LoadAsync();
    }

    /// <summary>Codes in the current list order: Önceki/Sonraki in the card follow the list, like ASB.</summary>
    private IReadOnlyList<string> SiblingIds() => _grid.Items.OfType<DataRowView>().Select(r => r["Id"].ToString()!).ToList();

    private void ToggleDetail()
    {
        if (_grid.SelectedItem == null || _grid.ItemContainerGenerator.ContainerFromItem(_grid.SelectedItem) is not DataGridRow row) return;
        row.DetailsVisibility = row.DetailsVisibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task FillDetailAsync(DataGridRow row, ContentPresenter host)
    {
        if (row.Item is not DataRowView item) return;
        var id = item["Id"].ToString()!;
        host.Content = new TextBlock { Text = "Yükleniyor…", Margin = new Thickness(40, 4, 0, 4), Foreground = Ui.TextSecondary };
        try
        {
            var detail = await Task.Run(() => _cards.RowDetail(id));
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(40, 4, 0, 8) };
            panel.Children.Add(DetailGrid("Barkodlar", detail.Barcodes, ("Barkod", "Barkod", 130, null), ("Birim", "Birim", 50, null), ("Miktar", "Miktar", 60, "N2"), ("Birincil", "Birincil", 55, null)));
            panel.Children.Add(DetailGrid("Depo Stokları", detail.Warehouses, ("DepoKodu", "Depo Kodu", 80, null), ("Depo", "Depo", 140, null), ("Mevcut", "Mevcut", 75, "N2"), ("Rezerve", "Rezerve", 70, "N2"), ("Kullanilabilir", "Kullanılabilir", 85, "N2")));
            host.Content = panel;
        }
        catch (Exception ex) { host.Content = new TextBlock { Text = "Detay yüklenemedi: " + ex.Message, Margin = new Thickness(40, 4, 0, 4), Foreground = Ui.Brush("R3.Danger.Brush") }; }
    }

    private static FrameworkElement DetailGrid(string title, DataTable rows, params (string Path, string Header, double Width, string? Format)[] columns)
    {
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column, MaxHeight = 160, CanUserAddRows = false, ItemsSource = rows.DefaultView };
        foreach (var c in columns) grid.Columns.Add(c.Format == null ? Ui.TextColumn(c.Header, c.Path, c.Width) : Ui.NumberColumn(c.Header, c.Path, c.Width, c.Format));
        var panel = new StackPanel { Margin = new Thickness(0, 0, Ui.Space.XL, 0) };
        panel.Children.Add(new TextBlock { Text = $"{title} ({rows.Rows.Count})", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, Ui.Space.S) });
        panel.Children.Add(rows.Rows.Count == 0 ? new TextBlock { Text = "Kayıt yok.", Foreground = Ui.TextSecondary } : grid);
        return panel;
    }

    private ContextMenu ActionsMenu()
    {
        var menu = new ContextMenu();
        void Item(string header, Action action) { var item = new MenuItem { Header = header }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("Kopyala…", Copy);
        Item("Aktif / Pasif Yap", ToggleActive);
        Item("Barkod Yazdır", () => { if (SelectedId() is { } id) _links.PrintLabels(id); });
        menu.Items.Add(new Separator());
        Item("Filtreleri Temizle", () => _filterRow.Clear());
        Item("Kolonlar…", () => new GridColumnVisibilityDialog(_grid) { Owner = Window.GetWindow(this) }.ShowDialog());
        return menu;
    }

    /// <summary>Kopyala: seçili kart Stok Kartı penceresinde yeni kayıt olarak açılır (kod boş, barkodlar kopyalanmaz).</summary>
    private void Copy()
    {
        if (SelectedId() is not { } id) { MessageBox.Show(Window.GetWindow(this), "Önce stok kartı seçin.", "Kopyala"); return; }
        var window = new StockCardWindow(new ViewModels.StockCardViewModel(_db, _companyId, id, SiblingIds(), startAsCopy: true), _db) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
        if (window.Changed) _ = LoadAsync();
    }

    private void ToggleActive()
    {
        var rows = _grid.SelectedItems.OfType<DataRowView>().ToList();
        if (rows.Count == 0) return;
        var activate = !rows.All(r => Convert.ToBoolean(r["Aktif"]));
        if (MessageBox.Show(Window.GetWindow(this), $"{rows.Count} stok kartı {(activate ? "aktif" : "pasif")} yapılacak. Devam edilsin mi?", "Aktif / Pasif", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var products = new LocalProductService(_db);
            foreach (var row in rows) products.SetActive(row["Id"].ToString()!, _companyId, activate);
            _ = LoadAsync();
        }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "Stok kartı güncellenemedi"); }
    }

    private void Export()
    {
        var dialog = new SaveFileDialog { Filter = "Excel dosyası|*.xlsx", FileName = $"Stok Kartları {DateTime.Now:yyyy-MM-dd HHmm}.xlsx" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { GridOutput.ToExcel(GridOutput.Columns(_grid), VisibleRows(), dialog.FileName, "Stok Kartları"); MessageBox.Show(Window.GetWindow(this), $"{_grid.Items.Count.ToString("N0", Ui.Turkish)} kayıt aktarıldı.", "Excel"); }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "Excel'e aktarılamadı"); }
    }

    private void Print()
    {
        try { GridOutput.Print(GridOutput.Columns(_grid), VisibleRows(), "Stok Kartları", _footer.Text); }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "Yazdırılamadı"); }
    }

    private static Button Command(string text, WpfUi.SymbolRegular icon, Action onClick, string toolTip)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new WpfUi.SymbolIcon { Symbol = icon, FontSize = 16, Foreground = Ui.Brush("R3.Accent.Brush"), Margin = new Thickness(0, 0, Ui.Space.SM, 0) });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, ToolTip = toolTip, Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(Ui.Space.SM, Ui.Space.S, Ui.Space.SM, Ui.Space.S), Margin = new Thickness(0, 0, Ui.Space.S, 0) };
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => onClick();
        return button;
    }
}
