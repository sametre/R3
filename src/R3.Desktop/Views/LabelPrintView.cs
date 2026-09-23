using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using R3.Infrastructure;

namespace R3.Desktop.Views;

/// <summary>
/// Stok › Barkod Yazdırma. Replaces the old "preview in a MessageBox" with real labels: products are
/// added to a print queue (by scanning or picking), each label row has an adet and an editable price
/// (prefilled from the chosen price list via <see cref="LocalPriceService.GetPrice"/>), and the queue
/// prints either one label per page (thermal label printers) or on an A4 sheet grid. Bars come from
/// <see cref="BarcodeSymbology"/> (EAN-13 when the code is a valid EAN, otherwise Code 128).
/// </summary>
public sealed class LabelPrintView : DockPanel
{
    private const double Mm = 96 / 25.4;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private readonly ObservableCollection<LabelItem> _queue = [];
    private readonly StoreDatabase _db;
    private readonly string _companyId;
    private readonly LocalPriceService _prices;
    private readonly ComboBox _size, _layout, _priceList;
    private readonly CheckBox _showPrice;
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Foreground = Brushes.DimGray };

    private sealed record LabelSize(string Name, double WidthMm, double HeightMm);
    private static readonly LabelSize[] Sizes = [new("50 × 30 mm", 50, 30), new("40 × 25 mm", 40, 25), new("60 × 40 mm", 60, 40), new("100 × 50 mm", 100, 50), new("38 × 21 mm (A4 65'li)", 38.1, 21.2)];

    public LabelPrintView(StoreDatabase db, string companyId, string? initialProductId = null)
    {
        _db = db; _companyId = companyId; _prices = new LocalPriceService(db);
        Margin = new Thickness(18);
        var title = new TextBlock { Text = "Barkod Etiketi Yazdırma", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(47, 56, 63)) };
        SetDock(title, Dock.Top); Children.Add(title);
        var help = new TextBlock { Text = "Barkodu okutun veya ürünü seçip Ekle'ye basın; ürünün tüm aktif barkodları kuyruğa eklenir. Adet ve fiyat hücrelerini düzenleyebilirsiniz. 13 haneli geçerli barkodlar EAN-13, diğerleri Code 128 olarak basılır.", Foreground = Brushes.DimGray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) };
        SetDock(help, Dock.Top); Children.Add(help);

        var pick = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) }; SetDock(pick, Dock.Top); Children.Add(pick);
        var barcode = new TextBox { Width = 170, Padding = new Thickness(6, 3, 6, 3), ToolTip = "Barkod okutun (Enter)" };
        var products = db.Query("SELECT id AS Id, code || ' — ' || name AS Display FROM products WHERE company_id=$c AND is_active=1 ORDER BY code", ("$c", companyId));
        var product = new ComboBox { Width = 320, Height = 26, ItemsSource = products.DefaultView, DisplayMemberPath = "Display", SelectedValuePath = "Id", IsEditable = true, IsTextSearchEnabled = true };
        Label(pick, "Barkod:"); pick.Children.Add(barcode); Label(pick, "Ürün:"); pick.Children.Add(product);
        Button(pick, "Ekle", () => { if (product.SelectedValue is string id) AddProduct(id); }, true);

        var options = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; SetDock(options, Dock.Top); Children.Add(options);
        _size = new ComboBox { Width = 170, Height = 26, ItemsSource = Sizes, DisplayMemberPath = "Name", SelectedIndex = 0 };
        _layout = new ComboBox { Width = 230, Height = 26, ItemsSource = new[] { "Etiket yazıcısı (her etiket bir sayfa)", "A4 sayfaya diz" }, SelectedIndex = 0 };
        var lists = db.Query("SELECT '' AS Id, '(Fiyat listesi yok)' AS Ad, -1 AS SortKey UNION ALL SELECT id, code || ' — ' || name, sequence FROM price_lists WHERE company_id=$c AND is_active=1 AND COALESCE(price_type,'Sales')='Sales' ORDER BY 3", ("$c", companyId));
        _priceList = new ComboBox { Width = 200, Height = 26, ItemsSource = lists.DefaultView, DisplayMemberPath = "Ad", SelectedValuePath = "Id", SelectedIndex = lists.Rows.Count > 1 ? 1 : 0 };
        _showPrice = new CheckBox { Content = "Fiyatı bas", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        Label(options, "Etiket:"); options.Children.Add(_size); Label(options, "Yerleşim:"); options.Children.Add(_layout); Label(options, "Fiyat listesi:"); options.Children.Add(_priceList); options.Children.Add(_showPrice);
        Button(options, "Önizle", Preview); Button(options, "Yazdır (Ctrl+P)", Print, true); Button(options, "Kuyruğu Temizle", () => { _queue.Clear(); UpdateStatus(); });
        options.Children.Add(_status);

        var grid = new DataGrid { Style = (Style)System.Windows.Application.Current.FindResource("ProfessionalDataGridStyle"), AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = true, SelectionMode = DataGridSelectionMode.Extended, HeadersVisibility = DataGridHeadersVisibility.Column, ItemsSource = _queue };
        grid.Columns.Add(new DataGridTextColumn { Header = "Barkod", Binding = new Binding(nameof(LabelItem.Barcode)), IsReadOnly = true, Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Stok Kodu", Binding = new Binding(nameof(LabelItem.ProductCode)), IsReadOnly = true, Width = 130 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Ürün", Binding = new Binding(nameof(LabelItem.ProductName)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Birim / Varyant", Binding = new Binding(nameof(LabelItem.Detail)), IsReadOnly = true, Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Fiyat ✎", Binding = new Binding(nameof(LabelItem.Price)) { StringFormat = "N2", ConverterCulture = Turkish, TargetNullValue = "" }, Width = 100 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Adet ✎", Binding = new Binding(nameof(LabelItem.Copies)), Width = 70 });
        grid.KeyDown += (_, e) => { if (e.Key == Key.Delete) { UpdateStatus(); } };
        grid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(UpdateStatus);
        Children.Add(grid);

        barcode.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return; e.Handled = true;
            try { var r = new LocalBarcodeResolver(db).ResolveForInventory(barcode.Text, companyId); AddProduct(r.ProductId, onlyBarcode: r.Barcode); barcode.Clear(); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { _status.Text = ex.Message; }
        };
        _priceList.SelectionChanged += (_, _) => { foreach (var item in _queue) item.Price = PriceFor(item.ProductId); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control) { Print(); e.Handled = true; } };
        if (initialProductId != null) AddProduct(initialProductId);
        UpdateStatus();
        Loaded += (_, _) => barcode.Focus();
    }

    /// <summary>Adds all active barcodes of a product to the queue (used when the tab is reopened from Stok Kartları).</summary>
    public void Add(string productId) => AddProduct(productId);

    private decimal? PriceFor(string productId) => _priceList.SelectedValue is string list && list.Length > 0 ? _prices.GetPrice(list, productId) : null;

    private void AddProduct(string productId, string? onlyBarcode = null)
    {
        var labels = new LocalBarcodePrintService(_db).GetLabels(productId, _companyId);
        if (onlyBarcode != null) labels = labels.Where(x => x.Barcode == onlyBarcode).ToArray();
        if (labels.Count == 0)
        {
            // A product without any barcode can still get a label carrying its stock code.
            var p = _db.Query("SELECT code, name FROM products WHERE id=$id", ("$id", productId)).Rows;
            if (p.Count == 0) return;
            labels = [new BarcodePrintLabel(p[0][0].ToString()!, p[0][0].ToString()!, p[0][1].ToString()!, "", "", 1, true)];
        }
        var price = PriceFor(productId);
        foreach (var label in labels)
        {
            var existing = _queue.FirstOrDefault(x => x.Barcode == label.Barcode);
            if (existing != null) { existing.Copies++; continue; }
            _queue.Add(new LabelItem(productId, label.Barcode, label.ProductCode, label.ProductName,
                string.Join(" • ", new[] { label.UnitName, label.VariantName }.Where(x => !string.IsNullOrWhiteSpace(x))), price));
        }
        UpdateStatus();
    }

    private void UpdateStatus() => _status.Text = $"{_queue.Count} satır • {_queue.Sum(x => Math.Max(0, x.Copies))} etiket";

    private FixedDocument BuildDocument()
    {
        var size = (LabelSize)_size.SelectedItem;
        var labels = _queue.SelectMany(x => Enumerable.Repeat(x, Math.Clamp(x.Copies, 0, 999))).ToList();
        if (labels.Count == 0) throw new InvalidOperationException("Yazdırılacak etiket yok. Önce ürün ekleyin.");
        var document = new FixedDocument();
        if (_layout.SelectedIndex == 0)
        {
            document.DocumentPaginator.PageSize = new Size(size.WidthMm * Mm, size.HeightMm * Mm);
            foreach (var label in labels) AddPage(document, document.DocumentPaginator.PageSize, [(RenderLabel(label, size), new Point(0, 0))]);
        }
        else
        {
            var page = new Size(210 * Mm, 297 * Mm); const double margin = 8 * Mm;
            var columns = Math.Max(1, (int)((page.Width - 2 * margin) / (size.WidthMm * Mm)));
            var rows = Math.Max(1, (int)((page.Height - 2 * margin) / (size.HeightMm * Mm)));
            document.DocumentPaginator.PageSize = page;
            foreach (var chunk in labels.Chunk(columns * rows))
                AddPage(document, page, chunk.Select((label, i) => (RenderLabel(label, size), new Point(margin + i % columns * size.WidthMm * Mm, margin + i / columns * size.HeightMm * Mm))).ToList());
        }
        return document;
    }

    private static void AddPage(FixedDocument document, Size size, IReadOnlyList<(UIElement Element, Point At)> items)
    {
        var page = new FixedPage { Width = size.Width, Height = size.Height, Background = Brushes.White };
        foreach (var (element, at) in items) { FixedPage.SetLeft(element, at.X); FixedPage.SetTop(element, at.Y); page.Children.Add(element); }
        var content = new PageContent(); ((IAddChild)content).AddChild(page); document.Pages.Add(content);
    }

    private UIElement RenderLabel(LabelItem label, LabelSize size)
    {
        var width = size.WidthMm * Mm; var height = size.HeightMm * Mm; var pad = 1.5 * Mm;
        var canvas = new Canvas { Width = width, Height = height, Background = Brushes.White, ClipToBounds = true };
        var small = size.HeightMm < 26;
        var name = new TextBlock { Text = label.ProductName, FontSize = small ? 6.5 : 8, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, Width = width - 2 * pad, MaxHeight = small ? 17 : 22 };
        Canvas.SetLeft(name, pad); Canvas.SetTop(name, pad); canvas.Children.Add(name);
        var showPrice = _showPrice.IsChecked == true && label.Price is not null;
        var priceHeight = showPrice ? (small ? 12 : 17) : 0;
        var barTop = pad + (small ? 17 : 22) + 1;
        var barHeight = Math.Max(10, height - barTop - pad - priceHeight - (small ? 8 : 10));
        try
        {
            var encoded = BarcodeSymbology.Encode(label.Barcode);
            var totalModules = encoded.Modules.Sum() + 20;                  // 10-module quiet zone each side
            var module = (width - 2 * pad) / totalModules;
            var x = pad + 10 * module; var isBar = true;
            foreach (var m in encoded.Modules)
            {
                if (isBar) { var bar = new Rectangle { Width = m * module, Height = barHeight, Fill = Brushes.Black }; Canvas.SetLeft(bar, x); Canvas.SetTop(bar, barTop); canvas.Children.Add(bar); }
                x += m * module; isBar = !isBar;
            }
            var human = new TextBlock { Text = encoded.HumanReadable, FontFamily = new FontFamily("Consolas"), FontSize = small ? 6 : 7.5, Width = width - 2 * pad, TextAlignment = TextAlignment.Center };
            Canvas.SetLeft(human, pad); Canvas.SetTop(human, barTop + barHeight); canvas.Children.Add(human);
        }
        catch (ArgumentException ex)
        {
            var error = new TextBlock { Text = ex.Message, FontSize = 6, Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Width = width - 2 * pad };
            Canvas.SetLeft(error, pad); Canvas.SetTop(error, barTop); canvas.Children.Add(error);
        }
        if (showPrice)
        {
            var price = new TextBlock { Text = label.Price!.Value.ToString("N2", Turkish) + " ₺", FontSize = small ? 10 : 14, FontWeight = FontWeights.Bold, Width = width - 2 * pad, TextAlignment = TextAlignment.Right };
            Canvas.SetLeft(price, pad); Canvas.SetTop(price, height - pad - priceHeight); canvas.Children.Add(price);
            var code = new TextBlock { Text = label.ProductCode, FontSize = small ? 5.5 : 6.5, Foreground = Brushes.DimGray };
            Canvas.SetLeft(code, pad); Canvas.SetTop(code, height - pad - priceHeight + (small ? 3 : 6)); canvas.Children.Add(code);
        }
        return canvas;
    }

    private void Preview()
    {
        try
        {
            var viewer = new DocumentViewer { Document = BuildDocument() };
            new Window { Title = "Etiket Önizleme", Width = 900, Height = 700, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = viewer }.Show();
        }
        catch (InvalidOperationException ex) { _status.Text = ex.Message; }
    }

    private void Print()
    {
        try
        {
            var document = BuildDocument();
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return;
            dialog.PrintDocument(document.DocumentPaginator, "R3 barkod etiketleri");
            _status.Text = $"{document.Pages.Count} sayfa yazıcıya gönderildi.";
        }
        catch (InvalidOperationException ex) { _status.Text = ex.Message; }
    }

    private static void Label(Panel bar, string text) => bar.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 5, 0) });

    private static void Button(Panel bar, string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, Height = 26, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0) };
        if (primary) { button.Background = new SolidColorBrush(Color.FromRgb(22, 124, 130)); button.Foreground = Brushes.White; button.BorderThickness = new Thickness(0); }
        button.Click += (_, _) => action(); bar.Children.Add(button);
    }

    private sealed class LabelItem(string productId, string barcode, string productCode, string productName, string detail, decimal? price) : INotifyPropertyChanged
    {
        private int _copies = 1; private decimal? _price = price;
        public string ProductId { get; } = productId;
        public string Barcode { get; } = barcode;
        public string ProductCode { get; } = productCode;
        public string ProductName { get; } = productName;
        public string Detail { get; } = detail;
        public decimal? Price { get => _price; set { _price = value; PropertyChanged?.Invoke(this, new(nameof(Price))); } }
        public int Copies { get => _copies; set { _copies = Math.Clamp(value, 0, 999); PropertyChanged?.Invoke(this, new(nameof(Copies))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
