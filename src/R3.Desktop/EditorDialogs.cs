using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using Microsoft.Win32;
using R3.Infrastructure;

namespace R3.Desktop;

public class EditorDialog : Window
{
    protected readonly StackPanel Fields = new() { Margin = new Thickness(18) };
    protected readonly TextBlock Error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    public EditorDialog(string title)
    {
        Title = title; Width = 430; SizeToContent = SizeToContent.Height; MaxHeight = 680; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = new SolidColorBrush(Color.FromRgb(247, 248, 249)); FontFamily = new FontFamily("Segoe UI");
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/R3.Desktop;component/Assets/R3.ico"));
        Content = new ScrollViewer { Content = Fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Fields.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(54, 61, 67)), Margin = new Thickness(0, 0, 0, 8) });
    }
    protected T Field<T>(string label, T control) where T : Control
    {
        Fields.Children.Add(new TextBlock { Text = label, FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(92, 99, 105)), Margin = new Thickness(0, 8, 0, 4) });
        control.Padding = new Thickness(7, 5, 7, 5); Fields.Children.Add(control); return control;
    }
    protected void Finish(Action save)
    {
        Fields.Children.Add(Error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "Vazgeç", IsCancel = true, Width = 78, Height = 30, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 7, 0) };
        var accept = new Button { Content = "Kaydet", IsDefault = true, Width = 78, Height = 30, Padding = new Thickness(10, 4, 10, 4), Background = new SolidColorBrush(Color.FromRgb(66, 75, 82)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        accept.Click += (_, _) => { try { save(); DialogResult = true; } catch (Microsoft.Data.Sqlite.SqliteException ex) { Error.Text = ex.SqliteErrorCode == 19 ? "Bu kod zaten kullanılıyor veya seçilen kayıt geçersiz." : "Veritabanına yazılamadı: " + ex.Message; } catch (Exception ex) { Error.Text = ex.Message; } };
        buttons.Children.Add(cancel); buttons.Children.Add(accept); Fields.Children.Add(buttons);
    }
}
public sealed class RecordDialog : EditorDialog
{
    private readonly TextBox _code, _name, _phone, _address;
    public string Code => _code.Text;
    public string RecordName => _name.Text;
    public string Phone => _phone.Text;
    public string Address => _address.Text;
    public Action? SaveRecord { get; set; }
    public RecordDialog(string title, DataRowView? row) : base(title)
    {
        _code = Field("Kod *", new TextBox { Text = row?["Kod"].ToString() ?? "", MaxLength = 30 });
        _name = Field("Ad / Ünvan *", new TextBox { Text = row?["Ad"].ToString() ?? "", MaxLength = 160 });
        _phone = Field("Telefon", new TextBox { Text = row?["Telefon"].ToString() ?? "", MaxLength = 30 });
        _address = Field("Adres", new TextBox { Text = row?["Adres"].ToString() ?? "", Height = 75, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 500 });
        Finish(() => { if (SaveRecord == null) throw new InvalidOperationException("Kayıt işlemi tanımlanmadı."); SaveRecord(); });
        Loaded += (_, _) => _code.Focus();
    }
}
public sealed class MovementDialog : EditorDialog
{
    public MovementDialog(StoreDatabase db, long customer, DataTable stores) : base("Yeni cari hareket")
    {
        var store = Field("Mağaza *", new ComboBox { ItemsSource = stores.DefaultView, DisplayMemberPath = "Ad", SelectedValuePath = "Id", SelectedIndex = 0 });
        var date = Field("İşlem tarihi *", new DatePicker { SelectedDate = DateTime.Today });
        var type = Field("İşlem türü *", new ComboBox { ItemsSource = new[] { "Borç", "Tahsilat" }, SelectedIndex = 0 });
        var amount = Field("Tutar (₺) * — örnek: 1250,50", new TextBox { MaxLength = 16 });
        var note = Field("Açıklama", new TextBox { MaxLength = 500, Height = 70, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap });
        Finish(() =>
        {
            if (store.SelectedValue == null || date.SelectedDate == null) throw new ArgumentException("Mağaza ve tarih seçin.");
            if (!decimal.TryParse(amount.Text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.GetCultureInfo("tr-TR"), out var value)) throw new ArgumentException("Tutarı virgüllü ondalık sayı olarak girin (1250,50).");
            db.AddMovement(customer, Convert.ToInt64(store.SelectedValue), date.SelectedDate.Value, (string)type.SelectedItem, value, note.Text);
        });
    }
}

public sealed class MasterRecordDialog : EditorDialog
{
    private readonly TextBox _code, _name, _extra; private readonly CheckBox _active; private readonly string _kind;
    public string Code => _code.Text; public string NameValue => _name.Text; public string ParentId => ""; public string Extra => _extra.Text; public bool ActiveValue => _active.IsChecked != false;
    public MasterRecordDialog(string title, string kind, DataRowView? row) : base(title)
    {
        _kind = kind; _code = Field("Kod *", new TextBox { Text = row?["Kod"].ToString() ?? "", MaxLength = 40 }); _name = Field("Ad *", new TextBox { Text = row?["Ad"].ToString() ?? "", MaxLength = 200 });
        if (kind == "units") _extra = Field("Ondalık basamak (0-6)", new TextBox { Text = row?["Ondalik"].ToString() ?? "0", MaxLength = 1 });
        else if (kind == "warehouses") _extra = Field("Depo tipi", new TextBox { Text = row?["DepoTipi"].ToString() ?? "Main", MaxLength = 20 });
        else _extra = new TextBox { Text = "" };
        _active = new CheckBox { Content = "Aktif", IsChecked = row == null || Convert.ToBoolean(row["Aktif"]) }; Fields.Children.Add(_active); Finish(Save); Loaded += (_, _) => _code.Focus();
    }
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(NameValue)) throw new ArgumentException("Kod ve ad alanları zorunludur.");
        if (_kind == "units" && (!int.TryParse(Extra, out var places) || places is < 0 or > 6)) throw new ArgumentException("Ondalık basamak 0 ile 6 arasında olmalıdır.");
    }
}

public sealed class ProductDialog : EditorDialog
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private readonly TextBox _code, _name, _vat, _purchaseVat, _excise, _minimumStock, _maximumStock, _minimumOrder, _orderMultiple;
    private readonly ComboBox _brand, _category, _unit, _productType;
    private readonly CheckBox _active, _sellable;
    private readonly Image _image = new() { Stretch = Stretch.Uniform, Margin = new Thickness(8) };
    private readonly TextBlock _imagePlaceholder;
    private readonly string _defaultUnit, _companyId, _id;
    private readonly List<ProductChildEdit> _variants = [];
    private readonly List<ProductChildEdit> _barcodes = [];
    private string _imagePath = "", _baseline = "";
    private bool _saved;

    public ProductDialog(DataRowView? row, string defaultUnit, string companyId, StoreDatabase database, ProductDetailEdit? detail = null) : base(row == null ? "Yeni Ürün" : "Ürün Kartı")
    {
        Width = 980; Height = 720; MinWidth = 860; MinHeight = 620; MaxHeight = double.PositiveInfinity;
        SizeToContent = SizeToContent.Manual; ResizeMode = ResizeMode.CanResizeWithGrip;
        _defaultUnit = defaultUnit; _companyId = companyId; _id = detail?.Product.Id ?? row?["Id"].ToString() ?? "";
        if (detail != null) { _variants.AddRange(detail.Variants); _barcodes.AddRange(detail.Barcodes); _imagePath = detail.Product.ImagePath; }
        Fields.Children.Clear(); Fields.Margin = new Thickness(20, 16, 20, 16);

        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel(); title.Children.Add(new TextBlock { Text = row == null ? "Yeni Ürün Kartı" : "Ürün Kartı", FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = Brush("263746") });
        title.Children.Add(new TextBlock { Text = "Kimlik, vergi, stok, barkod, varyant ve görsel bilgilerini tek ekrandan yönetin.", FontSize = 11, Foreground = Brush("667785"), Margin = new Thickness(0, 3, 0, 0) });
        header.Children.Add(title); _active = new CheckBox { Content = "Aktif ürün", IsChecked = detail?.Product.IsActive ?? true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) }; Grid.SetColumn(_active, 1); header.Children.Add(_active); Fields.Children.Add(header);

        var card = new Border { Background = Brushes.White, BorderBrush = Brush("D8E1E7"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(14) };
        var top = new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) }); top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); top.ColumnDefinitions.Add(new ColumnDefinition()); card.Child = top;
        var visual = new StackPanel();
        var imageFrame = new Border { Height = 185, Background = Brush("F3F6F8"), BorderBrush = Brush("D5DFE5"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
        var imageGrid = new Grid(); _imagePlaceholder = new TextBlock { Text = "▧\nÜrün görseli", FontSize = 14, Foreground = Brush("80909A"), TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; imageGrid.Children.Add(_imagePlaceholder); imageGrid.Children.Add(_image); imageFrame.Child = imageGrid; visual.Children.Add(imageFrame);
        var imageButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 9, 0, 0) };
        var chooseImage = SmallButton("▧  Görsel Seç"); var removeImage = SmallButton("×  Kaldır"); removeImage.Margin = new Thickness(6, 0, 0, 0); chooseImage.Click += (_, _) => ChooseImage(); removeImage.Click += (_, _) => { _imagePath = ""; ShowImage(); }; imageButtons.Children.Add(chooseImage); imageButtons.Children.Add(removeImage); visual.Children.Add(imageButtons);
        visual.Children.Add(new TextBlock { Text = "PNG veya JPG • önerilen 800 × 800 px", FontSize = 9.5, Foreground = Brush("7C8992"), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 7, 0, 0) }); top.Children.Add(visual);

        var form = new Grid(); Grid.SetColumn(form, 2); top.Children.Add(form); for (var i = 0; i < 4; i++) form.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(125) : new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++) form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _code = new TextBox { Text = detail?.Product.Code ?? row?["Kod"].ToString() ?? "", MaxLength = 50 }; _name = new TextBox { Text = detail?.Product.Name ?? row?["Ad"].ToString() ?? "", MaxLength = 200 };
        _productType = new ComboBox { ItemsSource = new[] { new Choice("Stock", "Stok Ürünü"), new Choice("Service", "Hizmet"), new Choice("NonStock", "Stoksuz Ürün") }, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = detail?.Product.ProductType ?? "Stock" };
        _brand = Lookup(database, "brands", companyId, detail?.Product.BrandId); _category = Lookup(database, "categories", companyId, detail?.Product.CategoryId); _unit = Lookup(database, "units", companyId, detail?.Product.UnitId ?? defaultUnit);
        _vat = NumberBox(detail?.Product.VatRate ?? 20); _purchaseVat = NumberBox(detail?.Product.PurchaseVatRate ?? 20); _excise = NumberBox(detail?.Product.ExciseRate ?? 0);
        AddForm(form, 0, 0, "Ürün kodu *", _code); AddForm(form, 0, 2, "Ürün adı *", _name);
        AddForm(form, 1, 0, "Ürün tipi", _productType); AddForm(form, 1, 2, "Temel birim *", _unit);
        AddForm(form, 2, 0, "Marka", _brand); AddForm(form, 2, 2, "Kategori", _category);
        AddForm(form, 3, 0, "Satış KDV %", _vat); AddForm(form, 3, 2, "Alış KDV %", _purchaseVat);
        AddForm(form, 4, 0, "ÖTV %", _excise); _sellable = new CheckBox { Content = "Satışa açık", IsChecked = detail?.Product.IsSellable ?? true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 7, 0, 7) }; Grid.SetRow(_sellable, 4); Grid.SetColumn(_sellable, 3); form.Children.Add(_sellable);
        Fields.Children.Add(card);

        var tabs = new TabControl { Height = 285, Margin = new Thickness(0, 14, 0, 0) };
        _minimumStock = NumberBox(detail?.Product.MinimumStock ?? 0); _maximumStock = NumberBox(detail?.Product.MaximumStock ?? 0); _minimumOrder = NumberBox(detail?.Product.MinimumOrderQuantity ?? 0); _orderMultiple = NumberBox(detail?.Product.OrderMultiple ?? 0);
        tabs.Items.Add(new TabItem { Header = "Stok ve Sipariş", Content = StockPanel() });
        tabs.Items.Add(new TabItem { Header = $"Barkodlar ({_barcodes.Count})", Content = ChildPanel(false) });
        tabs.Items.Add(new TabItem { Header = $"Varyantlar ({_variants.Count})", Content = ChildPanel(true) });
        tabs.Items.Add(new TabItem { Header = "Fiyatlandırma", Content = InfoPanel("Fiyat listeleri", "Satış ve alış fiyatları fiyat listesi motorundan yönetilir. Ürünün vergi oranları yukarıdaki kimlik kartına kaydedilir.") });
        tabs.Items.Add(new TabItem { Header = "E-Ticaret / Görsel", Content = InfoPanel("Ürün vitrini", "Seçtiğiniz ana görsel ürünle birlikte saklanır. Pazaryeri için kare, açık zeminli ve yüksek çözünürlüklü görsel kullanın.") });
        Fields.Children.Add(tabs); ShowImage();
        Finish(() => { ValidateProduct(); _saved = true; });
        _baseline = Snapshot(); Loaded += (_, _) => _code.Focus();
        Closing += (_, e) => { if (_saved || !IsDirty()) return; var answer = MessageBox.Show(this, "Kaydedilmemiş değişiklikler var. Kapatılsın mı?", "Ürün kartı", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning); if (answer != MessageBoxResult.Yes) e.Cancel = true; };
    }

    public ProductAggregateEdit ToEditModel() => new(_id, _companyId, _code.Text.Trim(), _name.Text.Trim(), Value(_brand), Value(_category), Value(_unit, _defaultUnit), Value(_productType, "Stock"), Decimal(_vat), _active.IsChecked == true, _variants, _barcodes, Decimal(_purchaseVat), Decimal(_excise), Decimal(_minimumStock), Decimal(_maximumStock), Decimal(_minimumOrder), Decimal(_orderMultiple), _sellable.IsChecked == true, _imagePath);

    private UIElement StockPanel()
    {
        var panel = new Grid { Margin = new Thickness(18) }; for (var i = 0; i < 4; i++) panel.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(145) : new GridLength(1, GridUnitType.Star) }); for (var i = 0; i < 3; i++) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddForm(panel, 0, 0, "Minimum stok", _minimumStock); AddForm(panel, 0, 2, "Maksimum stok", _maximumStock); AddForm(panel, 1, 0, "Minimum sipariş", _minimumOrder); AddForm(panel, 1, 2, "Sipariş katı", _orderMultiple);
        var note = new Border { Background = Brush("EEF6F8"), CornerRadius = new CornerRadius(5), Padding = new Thickness(12), Margin = new Thickness(4, 14, 4, 0), Child = new TextBlock { Text = "Stok seviyeleri satınalma önerileri ve kritik stok uyarılarında kullanılır. 0 değeri sınırsız / tanımsız kabul edilir.", Foreground = Brush("416775"), TextWrapping = TextWrapping.Wrap } }; Grid.SetRow(note, 2); Grid.SetColumnSpan(note, 4); panel.Children.Add(note); return panel;
    }

    private UIElement ChildPanel(bool variants)
    {
        var source = variants ? _variants : _barcodes; var root = new DockPanel { Margin = new Thickness(12) }; var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 9) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        var list = new ListBox { BorderBrush = Brush("CAD5DC") }; void Refresh() { list.Items.Clear(); foreach (var x in source) list.Items.Add(variants ? $"{x.Code,-16}  {x.Name}   • {(x.IsActive ? "Aktif" : "Pasif")}" : $"{x.Barcode,-24}  × {x.Quantity:N2}   • {(x.IsPrimary ? "Ana barkod" : "Ek barkod")}   • {(x.IsActive ? "Aktif" : "Pasif")}"); }
        var add = SmallButton(variants ? "+  Varyant Ekle" : "+  Barkod Ekle"); var toggle = SmallButton("◐  Aktif / Pasif"); var remove = SmallButton("×  Listeden Çıkar"); toggle.Margin = remove.Margin = new Thickness(6, 0, 0, 0);
        add.Click += (_, _) => { if (variants) { var code = Microsoft.VisualBasic.Interaction.InputBox("Varyant kodu", "Yeni Varyant"); if (string.IsNullOrWhiteSpace(code)) return; var name = Microsoft.VisualBasic.Interaction.InputBox("Varyant adı", "Yeni Varyant"); if (!string.IsNullOrWhiteSpace(name)) _variants.Add(new ProductChildEdit("", code, name)); } else { var value = Microsoft.VisualBasic.Interaction.InputBox("Barkod", "Yeni Barkod"); if (!string.IsNullOrWhiteSpace(value)) _barcodes.Add(new ProductChildEdit("", "", "", Barcode: value.Trim(), UnitId: Value(_unit, _defaultUnit), Quantity: 1, IsPrimary: _barcodes.Count == 0)); } Refresh(); };
        toggle.Click += (_, _) => { if (list.SelectedIndex < 0) return; var i = list.SelectedIndex; source[i] = source[i] with { IsActive = !source[i].IsActive }; Refresh(); list.SelectedIndex = i; };
        remove.Click += (_, _) => { if (list.SelectedIndex < 0) return; source.RemoveAt(list.SelectedIndex); Refresh(); };
        bar.Children.Add(add); bar.Children.Add(toggle); bar.Children.Add(remove); root.Children.Add(list); Refresh(); return root;
    }

    private static UIElement InfoPanel(string title, string text)
    {
        var panel = new StackPanel { Margin = new Thickness(22) }; panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("2D485A") }); panel.Children.Add(new TextBlock { Text = text, Foreground = Brush("657783"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }); return panel;
    }

    private void ChooseImage()
    {
        var picker = new OpenFileDialog { Title = "Ürün görseli seçin", Filter = "Görsel dosyaları|*.png;*.jpg;*.jpeg;*.bmp|Tüm dosyalar|*.*", CheckFileExists = true };
        if (picker.ShowDialog(this) == true) { _imagePath = picker.FileName; ShowImage(); }
    }

    private void ShowImage()
    {
        _image.Source = null; _imagePlaceholder.Visibility = Visibility.Visible;
        if (string.IsNullOrWhiteSpace(_imagePath) || !File.Exists(_imagePath)) return;
        try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(_imagePath); bitmap.EndInit(); bitmap.Freeze(); _image.Source = bitmap; _imagePlaceholder.Visibility = Visibility.Collapsed; } catch { _imagePath = ""; }
    }

    private void ValidateProduct()
    {
        if (string.IsNullOrWhiteSpace(_code.Text) || string.IsNullOrWhiteSpace(_name.Text)) throw new ArgumentException("Ürün kodu ve adı zorunludur.");
        if (string.IsNullOrWhiteSpace(Value(_unit))) throw new ArgumentException("Temel birim seçin.");
        foreach (var field in new[] { _vat, _purchaseVat, _excise }) if (Decimal(field) is < 0 or > 100) throw new ArgumentException("Vergi oranları 0 ile 100 arasında olmalıdır.");
        if (Decimal(_minimumStock) < 0 || Decimal(_maximumStock) < 0 || Decimal(_minimumOrder) < 0 || Decimal(_orderMultiple) < 0) throw new ArgumentException("Stok ve sipariş değerleri negatif olamaz.");
        if (Decimal(_maximumStock) > 0 && Decimal(_maximumStock) < Decimal(_minimumStock)) throw new ArgumentException("Maksimum stok, minimum stoktan küçük olamaz.");
    }

    private static ComboBox Lookup(StoreDatabase db, string table, string companyId, string? selected)
    {
        var data = db.Query($"SELECT id AS Id, code || ' — ' || name AS Display FROM {table} WHERE company_id=$c AND is_active=1 ORDER BY code", ("$c", (object)companyId));
        var combo = new ComboBox { ItemsSource = data.DefaultView, DisplayMemberPath = "Display", SelectedValuePath = "Id", IsTextSearchEnabled = true, SelectedValue = selected ?? "" }; if (combo.SelectedIndex < 0 && table == "units") combo.SelectedIndex = 0; return combo;
    }
    private static TextBox NumberBox(decimal value) => new() { Text = value.ToString("0.##", Turkish), HorizontalContentAlignment = HorizontalAlignment.Right };
    private static decimal Decimal(TextBox box) { if (!decimal.TryParse(box.Text, NumberStyles.Number, Turkish, out var value)) throw new ArgumentException("Sayısal alanları geçerli bir değerle doldurun."); return value; }
    private static string Value(ComboBox box, string fallback = "") => box.SelectedValue?.ToString() ?? fallback;
    private static Button SmallButton(string text) => new() { Content = text, Height = 29, Padding = new Thickness(10, 3, 10, 3), Background = Brushes.White, BorderBrush = Brush("C7D3DA"), Foreground = Brush("344955") };
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString("#" + hex));
    private static void AddForm(Grid grid, int row, int column, string label, Control control) { var caption = new TextBlock { Text = label, Foreground = Brush("5D6A73"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 7, 9, 7) }; Grid.SetRow(caption, row); Grid.SetColumn(caption, column); grid.Children.Add(caption); control.MinHeight = 30; control.Margin = new Thickness(0, 4, 12, 4); control.Padding = new Thickness(7, 4, 7, 4); Grid.SetRow(control, row); Grid.SetColumn(control, column + 1); grid.Children.Add(control); }
    private string Snapshot() => $"{_code.Text}|{_name.Text}|{Value(_brand)}|{Value(_category)}|{Value(_unit)}|{Value(_productType)}|{_vat.Text}|{_purchaseVat.Text}|{_excise.Text}|{_minimumStock.Text}|{_maximumStock.Text}|{_minimumOrder.Text}|{_orderMultiple.Text}|{_active.IsChecked}|{_sellable.IsChecked}|{_imagePath}|{string.Join(';', _variants)}|{string.Join(';', _barcodes)}";
    private bool IsDirty() => Snapshot() != _baseline;
    private sealed record Choice(string Id, string Name);
}

public sealed class InventoryOperationDialog : EditorDialog
{
    private readonly string _kind; private readonly LocalInventoryService _service; private readonly LocalBarcodeResolver _resolver; private readonly string _company, _branch, _warehouse; private decimal _factor = 1;
    private readonly TextBox _product, _variant, _quantity, _cost, _reference, _description, _targetWarehouse, _counted;
    public InventoryOperationDialog(string kind, LocalInventoryService service, StoreDatabase database, string company, string branch, string warehouse) : base(kind)
    {
        _kind = kind; _service = service; _resolver = new LocalBarcodeResolver(database); _company = company; _branch = branch; _warehouse = warehouse;
        var barcode = Field("Barkod / Ürün kodu", new TextBox { MaxLength = 80 }); var resolved = new TextBlock { Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap }; Fields.Children.Add(resolved); _product = Field("Ürün ID *", new TextBox { MaxLength = 80 }); _variant = Field("Varyant ID (opsiyonel)", new TextBox { MaxLength = 80 });
        barcode.KeyDown += (_, e) => { if (e.Key != Key.Enter) return; try { var result = _resolver.ResolveForInventory(barcode.Text, _company); _product.Text = result.ProductId; _variant.Text = result.VariantId ?? ""; _factor = result.QuantityFactor; resolved.Text = $"{result.ProductCode} — {result.ProductName} • {result.UnitName} • Çarpan: {_factor:N2}"; e.Handled = true; } catch (Exception ex) { resolved.Text = ex.Message; } };
        _quantity = Field("Miktar *", new TextBox { Text = "1" }); _cost = Field("Birim maliyet", new TextBox()); _reference = Field("Referans no", new TextBox());
        if (kind == "Depo Transfer") _targetWarehouse = Field("Hedef depo ID *", new TextBox { MaxLength = 80 }); else _targetWarehouse = new TextBox();
        if (kind == "Sayım") _counted = Field("Sayım sonucu *", new TextBox { Text = "0" }); else _counted = new TextBox();
        _description = Field("Açıklama", new TextBox { Height = 60, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap }); Finish(Save);
    }
    private void Save()
    {
        if (!decimal.TryParse(_quantity.Text, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out var qty) || qty <= 0) throw new ArgumentException("Miktar 0'dan büyük olmalıdır.");
        if (!decimal.TryParse(string.IsNullOrWhiteSpace(_cost.Text) ? "0" : _cost.Text, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out var cost)) throw new ArgumentException("Maliyet sayı olmalıdır.");
        var post = new InventoryPost(_company, _branch, _warehouse, _product.Text.Trim(), string.IsNullOrWhiteSpace(_variant.Text) ? null : _variant.Text.Trim(), qty * _factor, cost, DateTime.Now, _reference.Text, _description.Text);
        if (_kind == "Stok Giriş") _service.PostManualInAsync(post).GetAwaiter().GetResult();
        else if (_kind == "Stok Çıkış") _service.PostManualOutAsync(post).GetAwaiter().GetResult();
        else if (_kind == "Depo Transfer") _service.TransferAsync(post, _targetWarehouse.Text.Trim(), _branch, default).GetAwaiter().GetResult();
        else { if (!decimal.TryParse(_counted.Text, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out var counted) || counted < 0) throw new ArgumentException("Sayım sonucu geçerli olmalıdır."); _service.PostCountAdjustmentAsync(post, counted).GetAwaiter().GetResult(); }
    }
}

public sealed class SalesInvoiceDialog : EditorDialog
{
    private readonly LocalSalesService _sales; private readonly string _company, _branch, _warehouse, _account; private readonly TextBox _product, _unit, _qty, _price;
    public string? DocumentId { get; private set; }
    public SalesInvoiceDialog(LocalSalesService sales, string company, string branch, string warehouse, string account) : base("Yeni Satış Faturası")
    {
        _sales=sales;_company=company;_branch=branch;_warehouse=warehouse;_account=account; _product=Field("Ürün ID *",new TextBox()); _unit=Field("Birim ID *",new TextBox()); _qty=Field("Miktar *",new TextBox{Text="1"}); _price=Field("Birim fiyat *",new TextBox{Text="0"});
        Fields.Children.Add(new TextBlock{Text="Bu ilk masaüstü akışında tek satır hızlı fatura formu kullanılır. Barkod/ürün lookup servisleri backend'de hazırdır.",Foreground=Brushes.SlateGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)}); Finish(Save);
    }
    private void Save(){if(!decimal.TryParse(_qty.Text,NumberStyles.Any,CultureInfo.GetCultureInfo("tr-TR"),out var q)||q<=0)throw new ArgumentException("Geçerli miktar girin.");if(!decimal.TryParse(_price.Text,NumberStyles.Any,CultureInfo.GetCultureInfo("tr-TR"),out var p)||p<0)throw new ArgumentException("Geçerli fiyat girin.");DocumentId=_sales.CreateDraft(new("",_company,_branch,_warehouse,_account,DateTime.Today,"",[new(_product.Text.Trim(),null,_unit.Text.Trim(),null,q,1,p,0,20)]));}
}


