using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using R3.Infrastructure;

namespace R3.Desktop;

public class EditorDialog : Window
{
    protected readonly StackPanel Fields = new() { Margin = new Thickness(24) };
    protected readonly TextBlock Error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    public EditorDialog(string title)
    {
        Title = title; Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brushes.White; FontFamily = new FontFamily("Segoe UI");
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/R3.Desktop;component/Assets/R3.ico"));
        Content = Fields;
        Fields.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
    }
    protected T Field<T>(string label, T control) where T : Control
    {
        Fields.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 5) });
        control.Padding = new Thickness(8); Fields.Children.Add(control); return control;
    }
    protected void Finish(Action save)
    {
        Fields.Children.Add(Error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Content = "Vazgeç", IsCancel = true, Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(0, 0, 8, 0) };
        var accept = new Button { Content = "Kaydet", IsDefault = true, Padding = new Thickness(18, 9, 18, 9), Background = new SolidColorBrush(Color.FromRgb(29, 105, 153)), Foreground = Brushes.White };
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
    private readonly TextBox _code, _name, _vat; private readonly string _defaultUnit, _companyId; private readonly string _id; private readonly List<ProductChildEdit> _variants = []; private readonly List<ProductChildEdit> _barcodes = []; private readonly string _baseline; private bool _saved;
    public ProductDialog(DataRowView? row, string defaultUnit, string companyId, ProductDetailEdit? detail = null) : base(row == null ? "Yeni Ürün" : "Ürün Kartı")
    {
        _defaultUnit = defaultUnit; _companyId = companyId; _id = detail?.Product.Id ?? row?["Id"].ToString() ?? ""; if (detail != null) { _variants.AddRange(detail.Variants); _barcodes.AddRange(detail.Barcodes); }
        _code = Field("Ürün Kodu *", new TextBox { Text = detail?.Product.Code ?? row?["Kod"].ToString() ?? "", MaxLength = 50 }); _name = Field("Ürün Adı *", new TextBox { Text = detail?.Product.Name ?? row?["Ad"].ToString() ?? "", MaxLength = 200 }); _vat = Field("KDV %", new TextBox { Text = detail?.Product.VatRate.ToString(CultureInfo.InvariantCulture) ?? row?["KDV"].ToString() ?? "20" });
        _baseline = Snapshot(); _code.TextChanged += (_, _) => { }; _name.TextChanged += (_, _) => { }; _vat.TextChanged += (_, _) => { }; var tabs = new TabControl { Height = 260, Margin = new Thickness(0, 18, 0, 0) }; var general = new TabItem { Header = "Genel" }; general.Content = new TextBlock { Text = "Ürün tipi: Stok\nMarka, kategori ve temel birim lookup alanları sonraki adımda genişletilebilir.", Margin = new Thickness(10) }; tabs.Items.Add(general);
        var barcodePanel = new StackPanel { Margin = new Thickness(10) }; var barcodeList = new ListBox { Height = 130 }; foreach (var b in _barcodes) barcodeList.Items.Add($"{b.Barcode} {(b.IsActive ? "(Aktif)" : "(Pasif)")}"); var addBarcode = new Button { Content = "+ Barkod Ekle", Padding = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left }; addBarcode.Click += (_, _) => { var value = Microsoft.VisualBasic.Interaction.InputBox("Barkod", "Yeni Barkod"); if (!string.IsNullOrWhiteSpace(value)) { _barcodes.Add(new ProductChildEdit("", "", "", Barcode: value, UnitId: _defaultUnit, Quantity: 1, IsPrimary: _barcodes.Count == 0)); barcodeList.Items.Add(value + " (Aktif)"); } }; var toggleBarcode = new Button { Content = "Aktif/Pasif", Padding = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) }; toggleBarcode.Click += (_, _) => { if (barcodeList.SelectedIndex >= 0) { var i = barcodeList.SelectedIndex; _barcodes[i] = _barcodes[i] with { IsActive = !_barcodes[i].IsActive }; barcodeList.Items[i] = $"{_barcodes[i].Barcode} {(_barcodes[i].IsActive ? "(Aktif)" : "(Pasif)")}"; } }; barcodePanel.Children.Add(addBarcode); barcodePanel.Children.Add(toggleBarcode); barcodePanel.Children.Add(barcodeList); tabs.Items.Add(new TabItem { Header = "Barkodlar", Content = barcodePanel });
        var variantPanel = new StackPanel { Margin = new Thickness(10) }; var variantList = new ListBox { Height = 130 }; foreach (var v in _variants) variantList.Items.Add($"{v.Code} — {v.Name} {(v.IsActive ? "(Aktif)" : "(Pasif)")}"); var addVariant = new Button { Content = "+ Varyant Ekle", Padding = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left }; addVariant.Click += (_, _) => { var code = Microsoft.VisualBasic.Interaction.InputBox("Varyant kodu", "Yeni Varyant"); var name = Microsoft.VisualBasic.Interaction.InputBox("Varyant adı", "Yeni Varyant"); if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(name)) { _variants.Add(new ProductChildEdit("", code, name)); variantList.Items.Add($"{code} — {name} (Aktif)"); } }; var toggleVariant = new Button { Content = "Aktif/Pasif", Padding = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) }; toggleVariant.Click += (_, _) => { if (variantList.SelectedIndex >= 0) { var i = variantList.SelectedIndex; _variants[i] = _variants[i] with { IsActive = !_variants[i].IsActive }; variantList.Items[i] = $"{_variants[i].Code} — {_variants[i].Name} {(_variants[i].IsActive ? "(Aktif)" : "(Pasif)")}"; } }; variantPanel.Children.Add(addVariant); variantPanel.Children.Add(toggleVariant); variantPanel.Children.Add(variantList); tabs.Items.Add(new TabItem { Header = "Varyantlar", Content = variantPanel }); tabs.Items.Add(new TabItem { Header = "Fiyatlar", Content = new TextBlock { Text = "Sonraki sürüm", Margin = new Thickness(12) } }); Fields.Children.Add(tabs); Finish(() => { Validate(); _saved = true; }); Closing += (_, e) => { if (_saved || !IsDirty()) return; var answer = MessageBox.Show(this, "Kaydedilmemiş değişiklikler var. Kapatılsın mı?", "Ürün kartı", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning); if (answer == MessageBoxResult.Cancel) e.Cancel = true; }; }
    public ProductAggregateEdit ToEditModel() { if (!decimal.TryParse(_vat.Text, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out var vat)) vat = 0; return new ProductAggregateEdit(_id, _companyId, _code.Text, _name.Text, "", "", _defaultUnit, "Stock", vat, true, _variants, _barcodes); }
    private void Validate() { if (string.IsNullOrWhiteSpace(_code.Text) || string.IsNullOrWhiteSpace(_name.Text)) throw new ArgumentException("Ürün kodu ve adı zorunludur."); if (!decimal.TryParse(_vat.Text, out var vat) || vat is < 0 or > 100) throw new ArgumentException("KDV 0 ile 100 arasında olmalıdır."); }
    private string Snapshot() => $"{_code.Text}|{_name.Text}|{_vat.Text}|{string.Join(';', _variants.Select(x => $"{x.Id}:{x.Code}:{x.Name}:{x.IsActive}"))}|{string.Join(';', _barcodes.Select(x => $"{x.Id}:{x.Barcode}:{x.Quantity}:{x.IsActive}"))}";
    private bool IsDirty() => Snapshot() != _baseline;
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


