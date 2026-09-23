using System.Data;
using R3.Desktop.Design;
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

// Base dialog shell for every code-behind editor window (Movement, MasterRecord, Product, Inventory
// Operation, Sales Invoice...). Kept intentionally small/dense — old-Windows-ERP proportions, not
// spacious "modern form" proportions — so a keyboard-only user can tab through a whole card without
// the window feeling like it needs scrolling for six fields.
public class EditorDialog : Window
{
    protected readonly StackPanel Fields = new() { Margin = new Thickness(18, 14, 18, 14) };
    protected readonly TextBlock Error = new() { Foreground = Ui.Brush("R3.Danger.Brush"), TextWrapping = TextWrapping.Wrap, FontSize = 10.5, Margin = new Thickness(0, 8, 0, 0) };
    protected StackPanel? ButtonsPanel;
    protected Button? AcceptButton;
    protected Button? CancelButton;
    public EditorDialog(string title)
    {
        Title = title; Width = 440; MinWidth = 380; SizeToContent = SizeToContent.Height; MaxHeight = 720; ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Ui.Brush("R3.Background.Brush");
        BorderBrush = Ui.Brush("R3.Border.Strong.Brush"); BorderThickness = new Thickness(1);
        FontFamily = new FontFamily("Segoe UI"); ShowInTaskbar = false;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/R3.Desktop;component/Assets/AR3.ico"));
        var titleBar = new Border
        {
            Background = Ui.Brush("R3.Surface.Alt.Brush"),
            BorderBrush = Ui.Brush("R3.Border.Brush"), BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(18, 12, 18, 11),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "AR3 ERP", FontSize = Ui.Font.Grid, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Text.Secondary.Brush") },
                    new TextBlock { Text = title, FontSize = Ui.Font.Title, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Text.Primary.Brush"), Margin = new Thickness(0, 2, 0, 0) }
                }
            }
        };
        var body = new StackPanel(); body.Children.Add(titleBar); body.Children.Add(new ScrollViewer { Content = Fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = new Border { Background = Ui.Brush("R3.Surface.Alt.Brush"), BorderBrush = Ui.Brush("R3.Border.Brush"), BorderThickness = new Thickness(1), Child = body };
        PreviewKeyDown += HandleKeyboardNavigation;
    }

    private void HandleKeyboardNavigation(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (AcceptButton != null && AcceptButton.IsEnabled) AcceptButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true; return;
        }
        if (e.Key != Key.Enter || Keyboard.Modifiers is not (ModifierKeys.None or ModifierKeys.Shift)) return;
        if (Keyboard.FocusedElement is TextBox { AcceptsReturn: true } && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        MoveFocus(new TraversalRequest(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next));
        e.Handled = true;
    }
    protected T Field<T>(string label, T control) where T : Control
    {
        Fields.Children.Add(new TextBlock { Text = label, FontSize = Ui.Font.Grid, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Text.Secondary.Brush"), Margin = new Thickness(0, 7, 0, 3) });
        control.Padding = new Thickness(5, 3, 5, 3); Fields.Children.Add(control); return control;
    }
    protected void Finish(Action save)
    {
        Fields.Children.Add(Error);
        Fields.Children.Add(new TextBlock { Text = "Enter: sonraki alan   •   Shift+Enter: önceki alan   •   Ctrl+Enter: kaydet   •   Esc: vazgeç", FontSize = Ui.Font.Grid, Foreground = Ui.Brush("R3.Text.Secondary.Brush"), Margin = new Thickness(0, 6, 0, 0) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0) };
        var cancel = new Button { Content = "Vazgeç", IsCancel = true, Width = 78, Height = 28, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 7, 0), Background = Ui.Brush("R3.Surface.Alt.Brush"), BorderBrush = Ui.Brush("R3.Border.Strong.Brush") };
        var accept = new Button { Content = "Kaydet", IsDefault = true, Width = 82, Height = 28, Padding = new Thickness(8, 2, 8, 2), Background = Ui.Brush("R3.Sidebar.Brush"), Foreground = Ui.Brush("R3.Text.OnAccent.Brush"), BorderBrush = new SolidColorBrush(Color.FromRgb(68, 78, 86)) };
        accept.Click += (_, _) => { try { save(); DialogResult = true; } catch (Microsoft.Data.Sqlite.SqliteException ex) { Error.Text = ex.SqliteErrorCode == 19 ? "Bu kod zaten kullanılıyor veya seçilen kayıt geçersiz." : "Veritabanına yazılamadı: " + ex.Message; } catch (Exception ex) { Error.Text = ex.Message; } };
        buttons.Children.Add(cancel); buttons.Children.Add(accept);
        Fields.Children.Add(new Border { Background = Ui.Brush("R3.Background.Brush"), BorderBrush = Ui.Brush("R3.Border.Brush"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 10, 0, 0), Margin = new Thickness(0, 11, 0, 0), Child = buttons });
        ButtonsPanel = buttons; AcceptButton = accept; CancelButton = cancel;
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
        var amount = Field("Tutar (₺) * — örnek: 1250,50", new TextBox { MaxLength = 16, Tag = "Numeric" });
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
    private readonly TextBox _code, _name; private readonly TextBox? _extraText; private readonly ComboBox? _extraCombo; private readonly CheckBox _active; private readonly string _kind;
    public string Code => _code.Text; public string NameValue => _name.Text; public string ParentId => ""; public string Extra => _extraCombo != null ? (_extraCombo.SelectedItem?.ToString() ?? "") : (_extraText?.Text ?? ""); public bool ActiveValue => _active.IsChecked != false;
    public MasterRecordDialog(string title, string kind, DataRowView? row) : base(title)
    {
        _kind = kind; _code = Field("Kod *", new TextBox { Text = row?["Kod"].ToString() ?? "", MaxLength = 40 }); _name = Field("Ad *", new TextBox { Text = row?["Ad"].ToString() ?? "", MaxLength = 200 });
        if (kind == "units") _extraText = Field("Ondalık basamak (0-6)", new TextBox { Text = row?["Ondalik"].ToString() ?? "0", MaxLength = 1 });
        else if (kind == "warehouses") _extraText = Field("Depo tipi", new TextBox { Text = row?["DepoTipi"].ToString() ?? "Main", MaxLength = 20 });
        else if (kind == "product_groups") _extraText = Field("GTİP kodu (gümrük tarife pozisyonu)", new TextBox { Text = row?["GTIP"].ToString() ?? "", MaxLength = 20 });
        else if (kind == "variant_definitions") _extraCombo = Field("Tanım tipi *", new ComboBox { ItemsSource = new[] { "Renk", "Beden", "BedenTipi", "Model" }, SelectedItem = row?["Tip"].ToString() ?? "Renk" });
        _active = new CheckBox { Content = "Aktif", IsChecked = row == null || Convert.ToBoolean(row["Aktif"]) }; Fields.Children.Add(_active); Finish(Save); Loaded += (_, _) => _code.Focus();
    }
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(NameValue)) throw new ArgumentException("Kod ve ad alanları zorunludur.");
        if (_kind == "units" && (!int.TryParse(Extra, out var places) || places is < 0 or > 6)) throw new ArgumentException("Ondalık basamak 0 ile 6 arasında olmalıdır.");
        if (_kind == "variant_definitions" && string.IsNullOrWhiteSpace(Extra)) throw new ArgumentException("Tanım tipi seçilmelidir.");
    }
}

public sealed class BankAccountDialog : EditorDialog
{
    private readonly TextBox _code, _name, _bankName, _branchName, _branchCode, _accountNumber, _iban;
    private readonly ComboBox _currency, _type; private readonly CheckBox _active;
    public BankAccountDialog(BankAccountEdit? existing) : base(existing == null ? "Yeni Banka Hesabı" : "Banka Hesabını Düzenle")
    {
        Width = 420;
        _code = Field("Hesap kodu *", new TextBox { Text = existing?.Code ?? "", MaxLength = 20 });
        _name = Field("Hesap adı *", new TextBox { Text = existing?.Name ?? "", MaxLength = 120 });
        _bankName = Field("Banka", new TextBox { Text = existing?.BankName ?? "", MaxLength = 80 });
        _branchName = Field("Şube adı", new TextBox { Text = existing?.BankBranchName ?? "", MaxLength = 80 });
        _branchCode = Field("Şube kodu", new TextBox { Text = existing?.BankBranchCode ?? "", MaxLength = 10 });
        _accountNumber = Field("Hesap no", new TextBox { Text = existing?.AccountNumber ?? "", MaxLength = 30 });
        _iban = Field("IBAN", new TextBox { Text = existing?.Iban ?? "", MaxLength = 34 });
        _currency = Field("Para birimi *", new ComboBox { ItemsSource = new[] { "TRY", "USD", "EUR", "GBP" }, SelectedItem = existing?.CurrencyCode ?? "TRY" });
        _type = Field("Hesap tipi *", new ComboBox
        {
            ItemsSource = new[] { new Choice("Checking", "Vadesiz"), new Choice("Savings", "Vadeli"), new Choice("ForeignCurrency", "Döviz"), new Choice("Loan", "Kredi"), new Choice("Other", "Diğer") },
            DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = existing?.AccountType ?? "Checking"
        });
        _active = new CheckBox { Content = "Aktif", IsChecked = existing?.IsActive ?? true, Margin = new Thickness(0, 10, 0, 0) }; Fields.Children.Add(_active);
        Finish(() => { if (string.IsNullOrWhiteSpace(_code.Text) || string.IsNullOrWhiteSpace(_name.Text)) throw new ArgumentException("Hesap kodu ve adı zorunludur."); });
        Loaded += (_, _) => _code.Focus();
    }
    private sealed record Choice(string Id, string Name);
    public BankAccountEdit ToEditModel(string companyId, string branchId, string id) => new(
        id, companyId, branchId, _code.Text.Trim(), _name.Text.Trim(), _bankName.Text.Trim(), _branchName.Text.Trim(), _branchCode.Text.Trim(),
        _accountNumber.Text.Trim(), _iban.Text.Trim(), _currency.SelectedItem?.ToString() ?? "TRY", _type.SelectedValue?.ToString() ?? "Checking",
        _active.IsChecked == true);
}

public sealed class BankMovementDialog : EditorDialog
{
    private readonly ComboBox? _account; private readonly DatePicker _date; private readonly TextBox _amount, _description;
    public string? AccountId => _account?.SelectedValue?.ToString();
    public DateTime Date => _date.SelectedDate ?? DateTime.Today;
    public decimal Amount => decimal.Parse(_amount.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("tr-TR"));
    public string Description => _description.Text.Trim();
    public BankMovementDialog(string title, DataTable? accountLookup = null) : base(title)
    {
        Width = 380;
        if (accountLookup != null) _account = Field("Hesap *", new ComboBox { ItemsSource = accountLookup.DefaultView, DisplayMemberPath = "Ad", SelectedValuePath = "Id", IsTextSearchEnabled = true });
        _date = Field("Tarih *", new DatePicker { SelectedDate = DateTime.Today });
        _amount = Field("Tutar *", new TextBox { Tag = "Numeric", Text = "0" });
        _description = Field("Açıklama *", new TextBox { MaxLength = 200, Height = 60, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap });
        Finish(() =>
        {
            if (accountLookup != null && _account!.SelectedValue == null) throw new ArgumentException("Hesap seçin.");
            if (_date.SelectedDate == null) throw new ArgumentException("Tarih seçin.");
            if (!decimal.TryParse(_amount.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("tr-TR"), out var value) || value <= 0) throw new ArgumentException("Tutar 0'dan büyük olmalıdır.");
            if (string.IsNullOrWhiteSpace(_description.Text)) throw new ArgumentException("Açıklama zorunludur.");
        });
        Loaded += (_, _) => (_account as Control ?? _amount).Focus();
    }
}

/// <summary>Shared "pick an account and a date" prompt for the çek/senet lifecycle actions that
/// need one (tahsile ver / tahsil et / öde) - one dialog instead of three near-identical ones.</summary>
public sealed class AccountPickerDialog : EditorDialog
{
    private readonly ComboBox _account; private readonly DatePicker _date;
    public string? AccountId => _account.SelectedValue?.ToString();
    public DateTime Date => _date.SelectedDate ?? DateTime.Today;
    public AccountPickerDialog(string title, string fieldLabel, DataTable accountLookup) : base(title)
    {
        Width = 360;
        _account = Field(fieldLabel, new ComboBox { ItemsSource = accountLookup.DefaultView, DisplayMemberPath = "Ad", SelectedValuePath = "Id", IsTextSearchEnabled = true });
        _date = Field("Tarih *", new DatePicker { SelectedDate = DateTime.Today });
        Finish(() => { if (_account.SelectedValue == null) throw new ArgumentException("Hesap seçin."); if (_date.SelectedDate == null) throw new ArgumentException("Tarih seçin."); });
        Loaded += (_, _) => _account.Focus();
    }
}

/// <summary>Single required text field - "ciro edilen kişi/kurum" (Endorse) or an optional note
/// (Bounce/ReturnToDrawer). Kept separate from AccountPickerDialog since those two prompts don't
/// need a date or an account lookup.</summary>
public sealed class TextPromptDialog : EditorDialog
{
    private readonly TextBox _value; private readonly bool _required;
    public string Value => _value.Text.Trim();
    public TextPromptDialog(string title, string fieldLabel, bool required = true) : base(title)
    {
        Width = 360; _required = required;
        _value = Field(fieldLabel, new TextBox { MaxLength = 200 });
        Finish(() => { if (_required && string.IsNullOrWhiteSpace(_value.Text)) throw new ArgumentException("Bu alan zorunludur."); });
        Loaded += (_, _) => _value.Focus();
    }
}

/// <summary>Verilen çek/senedin ödemesi: tam olarak biri seçilmeli (kasa veya banka), ikisi birden
/// değil - LocalChequeService.Pay enforces the same rule server-side.</summary>
public sealed class ChequePaymentDialog : EditorDialog
{
    private readonly ComboBox _cash, _bank; private readonly DatePicker _date;
    public string? CashAccountId => _cash.SelectedValue?.ToString();
    public string? BankAccountId => _bank.SelectedValue?.ToString();
    public DateTime Date => _date.SelectedDate ?? DateTime.Today;
    public ChequePaymentDialog(DataTable cashLookup, DataTable bankLookup) : base("Çek/Senet Öde")
    {
        Width = 380;
        Fields.Children.Add(new TextBlock { Text = "Ödemeyi kasadan veya bankadan yapın - sadece birini seçin.", Foreground = Ui.Brush("R3.Text.Secondary.Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        _cash = Field("Kasa hesabı", new ComboBox { ItemsSource = cashLookup.DefaultView, DisplayMemberPath = "Ad", SelectedValuePath = "Id", IsTextSearchEnabled = true });
        _bank = Field("Banka hesabı", new ComboBox { ItemsSource = bankLookup.DefaultView, DisplayMemberPath = "Ad", SelectedValuePath = "Id", IsTextSearchEnabled = true });
        _date = Field("Tarih *", new DatePicker { SelectedDate = DateTime.Today });
        _cash.SelectionChanged += (_, _) => { if (_cash.SelectedValue != null) _bank.SelectedIndex = -1; };
        _bank.SelectionChanged += (_, _) => { if (_bank.SelectedValue != null) _cash.SelectedIndex = -1; };
        Finish(() =>
        {
            if (string.IsNullOrWhiteSpace(CashAccountId) == string.IsNullOrWhiteSpace(BankAccountId)) throw new ArgumentException("Ödeme kasadan veya bankadan yapılmalı (ikisi birden değil).");
            if (_date.SelectedDate == null) throw new ArgumentException("Tarih seçin.");
        });
    }
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString("#" + hex));
}

public sealed class ChequeDialog : EditorDialog
{
    private readonly ComboBox _instrumentType, _account; private readonly TextBox _amount, _chequeNumber, _drawerName, _bankName, _branchName, _accountNumber, _description;
    private readonly DatePicker _dueDate, _issueDate; private readonly string _currency;
    public ChequeDialog(string direction, DataTable accountLookup, string currency) : base(direction == "Received" ? "Yeni Alınan Çek/Senet" : "Yeni Verilen Çek/Senet")
    {
        Width = 420; _currency = currency;
        _instrumentType = Field("Tür *", new ComboBox { ItemsSource = new[] { new Choice("Cheque", "Çek"), new Choice("PromissoryNote", "Senet") }, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedIndex = 0 });
        _account = Field(direction == "Received" ? "Cari (kimden alındı)" : "Cari (kime verildi)", new ComboBox { ItemsSource = accountLookup.DefaultView, DisplayMemberPath = "Ad", SelectedValuePath = "Id", IsTextSearchEnabled = true });
        _amount = Field("Tutar *", new TextBox { Tag = "Numeric", Text = "0" });
        _dueDate = Field("Vade tarihi *", new DatePicker { SelectedDate = DateTime.Today.AddDays(30) });
        _issueDate = Field("Düzenleme tarihi", new DatePicker { SelectedDate = DateTime.Today });
        _chequeNumber = Field("Belge no", new TextBox { MaxLength = 20 });
        _drawerName = Field("Keşideci / borçlu adı *", new TextBox { MaxLength = 120 });
        _bankName = Field("Banka", new TextBox { MaxLength = 80 });
        _branchName = Field("Şube", new TextBox { MaxLength = 80 });
        _accountNumber = Field("Banka hesap no", new TextBox { MaxLength = 30 });
        _description = Field("Açıklama", new TextBox { MaxLength = 200, Height = 55, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap });
        Finish(() =>
        {
            if (_instrumentType.SelectedValue == null) throw new ArgumentException("Tür seçin.");
            if (!decimal.TryParse(_amount.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("tr-TR"), out var value) || value <= 0) throw new ArgumentException("Tutar 0'dan büyük olmalıdır.");
            if (_dueDate.SelectedDate == null) throw new ArgumentException("Vade tarihi seçin.");
            if (string.IsNullOrWhiteSpace(_drawerName.Text)) throw new ArgumentException("Keşideci/borçlu adı zorunludur.");
        });
        Loaded += (_, _) => _amount.Focus();
    }
    private sealed record Choice(string Id, string Name);
    public ChequeEdit ToEditModel(string companyId, string branchId, string direction) => new(
        "", companyId, branchId, _instrumentType.SelectedValue?.ToString() ?? "Cheque", direction, _account.SelectedValue?.ToString(),
        decimal.Parse(_amount.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("tr-TR")), _currency, _dueDate.SelectedDate!.Value, _issueDate.SelectedDate,
        _chequeNumber.Text.Trim(), _drawerName.Text.Trim(), _bankName.Text.Trim(), _branchName.Text.Trim(), _accountNumber.Text.Trim(), _description.Text.Trim());
}

// Product Card v2 (§9-§29): a persistent identity header plus a full-height tab strip, one tab per
// business area, replacing the old fixed identity-card + short tab-strip layout. ASB's STOKKARTI/
// STOKBIRIM/STOKTEDARIKCI are never reconstructed as-is here - each becomes its own canonical
// grid/section (Units, Suppliers, Inventory policy) per docs/PRODUCT_CARD_V2.md.
public sealed class ProductDialog : EditorDialog
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private readonly TextBox _code, _name, _parentCode, _quickBarcode, _vat, _purchaseVat, _excise, _exciseUnitPrice, _minimumStock, _maximumStock, _minimumOrder, _orderMultiple, _deliveryLead, _maxDeliveryLead, _pieceCount, _shipmentLocation;
    private readonly ComboBox _brand, _category, _unit, _productType, _lotTracking, _productGroup, _originCountry;
    private readonly List<ProductWarehousePolicyEdit> _warehousePolicies = [];
    private readonly System.Data.DataView _warehouses;
    private readonly CheckBox _active, _sellable, _definitionComplete, _canQuote, _allowFreeIssue, _isBundle;
    private readonly Image _image = new() { Stretch = Stretch.Uniform, Margin = new Thickness(8) };
    private TextBlock _imagePlaceholder = new();
    private readonly string _defaultUnit, _companyId, _id;
    private readonly List<ProductChildEdit> _variants = [];
    private readonly List<ProductChildEdit> _barcodes = [];
    private readonly List<ProductUnitEdit> _units = [];
    private readonly List<ProductSupplierEdit> _suppliers = [];
    private string _imagePath = "", _baseline = "";
    private bool _saved;
    public bool SaveAndNew { get; private set; }

    public ProductDialog(DataRowView? row, string defaultUnit, string companyId, StoreDatabase database, ProductDetailEdit? detail = null) : base(row == null ? "Yeni Ürün" : "Ürün Kartı")
    {
        // DevExpress/SAP-style dense card (2026-09-23): a light gray canvas (matches the app's own
        // R3.Background.Brush, #EEF0F2) instead of EditorDialog's near-white default, small-caps-ish
        // gray labels tight against white inputs, and 22px rows instead of the previous 30px - see
        // AddForm below. Width/height stay generous since eleven tabs of fields still need room, but
        // every row inside is now noticeably denser than a "modern spacious form".
        Width = 1000; Height = 740; MinWidth = 880; MinHeight = 620; MaxHeight = double.PositiveInfinity;
        SizeToContent = SizeToContent.Manual; ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = Ui.Brush("R3.Background.Brush");
        _defaultUnit = defaultUnit; _companyId = companyId; _id = detail?.Product.Id ?? row?["Id"].ToString() ?? "";
        if (detail != null) { _variants.AddRange(detail.Variants); _barcodes.AddRange(detail.Barcodes); _units.AddRange(detail.Product.Units); _suppliers.AddRange(detail.Product.Suppliers); _imagePath = detail.Product.ImagePath; _warehousePolicies.AddRange(detail.Product.WarehousePolicies ?? []); }
        _warehouses = database.Query("SELECT id AS Id, code || ' — ' || name AS Display FROM warehouses WHERE company_id=$c AND is_active=1 ORDER BY code", ("$c", companyId)).DefaultView;
        Fields.Children.Clear(); Fields.Margin = new Thickness(16, 12, 16, 12);

        // §27 header: identity + a static point-in-time summary, not a live-bound dashboard.
        // A white title strip against the gray canvas (vs. floating text) - the SAP/DevExpress
        // "transaction header bar" cue - and a smaller title so it reads as a dense form, not a
        // hero heading.
        var headerBar = new Border { Background = Ui.Brush("R3.Surface.Brush"), BorderBrush = Ui.Brush("R3.Border.Brush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 10) };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerBar.Child = header;
        var title = new StackPanel();
        var code0 = detail?.Product.Code ?? row?["Kod"].ToString() ?? ""; var name0 = detail?.Product.Name ?? row?["Ad"].ToString() ?? "";
        title.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(code0) ? "Yeni Ürün Kartı" : $"{code0}   {name0}", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Text.Primary.Brush") });
        var primaryBarcode = _barcodes.FirstOrDefault(x => x.IsPrimary) ?? _barcodes.FirstOrDefault();
        var summary = string.IsNullOrWhiteSpace(_id) ? null : database.Query("SELECT COALESCE(SUM(quantity_on_hand),0),COALESCE(SUM(quantity_reserved),0),COALESCE(SUM(quantity_available),0) FROM inventory_balances WHERE product_id=$p", ("$p", _id)).Rows[0];
        var unitDisplay = TryDisplay(database, "units", detail?.Product.UnitId ?? defaultUnit);
        title.Children.Add(new TextBlock { Text = $"Ana Barkod: {(primaryBarcode?.Barcode ?? "—")}   •   Temel Birim: {unitDisplay}   •   Mevcut: {(summary == null ? "—" : Convert.ToDecimal(summary[0]).ToString("N2", Turkish))}   •   Kullanılabilir: {(summary == null ? "—" : Convert.ToDecimal(summary[2]).ToString("N2", Turkish))}", FontSize = 10.5, Foreground = Ui.Brush("R3.Text.Secondary.Brush"), Margin = new Thickness(0, 2, 0, 0) });
        header.Children.Add(title); _active = new CheckBox { Content = "Aktif ürün", IsChecked = detail?.Product.IsActive ?? true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) }; Grid.SetColumn(_active, 1); header.Children.Add(_active); Fields.Children.Add(headerBar);

        _code = new TextBox { Text = code0, MaxLength = 50 }; _name = new TextBox { Text = name0, MaxLength = 200 };
        _parentCode = new TextBox { Text = detail?.Product.ParentCode ?? "", MaxLength = 50 };
        _productType = new ComboBox { ItemsSource = new[] { new Choice("Stock", "Stok"), new Choice("Service", "Hizmet"), new Choice("Bundle", "Takım / Set"), new Choice("RawMaterial", "Hammadde"), new Choice("FinishedGood", "Mamul") }, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = detail?.Product.ProductType ?? "Stock", ToolTip = "Hammadde, mamul ve takım/set davranışları henüz desteklenmiyor." };
        _productGroup = OptionalLookup(database, "SELECT id AS Id, code || ' — ' || name AS Display, code AS SortKey FROM product_groups WHERE company_id=$c AND is_active=1", companyId, detail?.Product.ProductGroupId);
        _originCountry = OptionalLookup(database, "SELECT id AS Id, code || ' — ' || name AS Display, CASE code WHEN 'TR' THEN '0' ELSE '1' || name END AS SortKey FROM countries WHERE is_active=1", companyId, detail?.Product.OriginCountryId);
        _brand = Lookup(database, "brands", companyId, detail?.Product.BrandId); _category = Lookup(database, "categories", companyId, detail?.Product.CategoryId); _unit = Lookup(database, "units", companyId, detail?.Product.UnitId ?? defaultUnit);
        _quickBarcode = new TextBox { Text = (_barcodes.FirstOrDefault(x => x.IsPrimary)?.Barcode ?? _barcodes.FirstOrDefault()?.Barcode ?? ""), MaxLength = 80, ToolTip = "Yeni kartta doğrudan ana barkod olarak kaydedilir. F4 ile bu alana geçebilirsiniz." };
        _definitionComplete = new CheckBox { Content = "Tanım tamamlandı / onaylandı", IsChecked = detail?.Product.IsDefinitionComplete ?? false, Margin = new Thickness(6, 10, 0, 0) };
        _vat = NumberBox(detail?.Product.VatRate ?? 20); _purchaseVat = NumberBox(detail?.Product.PurchaseVatRate ?? 20); _excise = NumberBox(detail?.Product.ExciseRate ?? 0); _exciseUnitPrice = NumberBox(detail?.Product.ExciseUnitPrice ?? 0);
        _sellable = new CheckBox { Content = "Satışa açık", IsChecked = detail?.Product.IsSellable ?? true, Margin = new Thickness(6, 10, 0, 0) };
        _canQuote = new CheckBox { Content = "Teklifte kullanılabilir", IsChecked = detail?.Product.CanQuote ?? true, Margin = new Thickness(6, 10, 0, 0) };
        _allowFreeIssue = new CheckBox { Content = "Bedelsiz girişe izin", IsChecked = detail?.Product.AllowFreeIssue ?? false, Margin = new Thickness(6, 10, 0, 0) };
        _isBundle = new CheckBox { Content = "Takım / set ürün", IsChecked = detail?.Product.IsBundle ?? false, Margin = new Thickness(6, 10, 0, 0) };
        _minimumStock = NumberBox(detail?.Product.MinimumStock ?? 0); _maximumStock = NumberBox(detail?.Product.MaximumStock ?? 0); _minimumOrder = NumberBox(detail?.Product.MinimumOrderQuantity ?? 0); _orderMultiple = NumberBox(detail?.Product.OrderMultiple ?? 0);
        _deliveryLead = IntBox(detail?.Product.Policy.DeliveryLeadTimeDays ?? 0); _maxDeliveryLead = IntBox(detail?.Product.Policy.MaximumDeliveryLeadTimeDays ?? 0); _pieceCount = IntBox(detail?.Product.Policy.PieceCount ?? 0); _shipmentLocation = new TextBox { Text = detail?.Product.Policy.ShipmentLocationType ?? "", MaxLength = 80 };
        _lotTracking = new ComboBox { ItemsSource = new[] { new Choice("None", "Yok"), new Choice("Lot", "Lot"), new Choice("Serial", "Seri No") }, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = detail?.Product.Policy.LotTrackingType ?? "None" };

        var tabs = new TabControl();
        // Only "Genel" is built up front; every other tab builds its content (and runs its queries - supplier
        // list, stock status, movements, audit history) the first time it is opened. All editable state lives
        // in fields/lists created above, so an unopened tab never changes what Save reads.
        TabItem Lazy(string header, Func<UIElement> build) => new() { Header = header, Tag = build };
        tabs.Items.Add(new TabItem { Header = "Genel", Content = GeneralPanel() });
        tabs.Items.Add(Lazy("Sınıflandırma", ClassificationPanel));
        tabs.Items.Add(Lazy($"Barkod & Birimler ({_barcodes.Count}/{_units.Count})", () => BarcodeUnitsPanel(database, companyId)));
        tabs.Items.Add(Lazy($"Varyant & Özellikler ({_variants.Count})", () => ChildPanel(true, database)));
        tabs.Items.Add(Lazy("Ticari Bilgiler", CommercialPanel));
        tabs.Items.Add(Lazy("Stok & Sipariş", StockPanel));
        tabs.Items.Add(Lazy($"Tedarikçiler ({_suppliers.Count})", () => SuppliersPanel(database, companyId)));
        tabs.Items.Add(Lazy("E-Ticaret", () => ChannelPanel(database)));
        tabs.Items.Add(Lazy("Stok Durumu", () => StockStatusPanel(database)));
        tabs.Items.Add(Lazy("Hareketler", () => MovementsPanel(database)));
        tabs.Items.Add(Lazy("Geçmiş", () => HistoryPanel(database)));
        // SelectionChanged bubbles up from grids/combos inside the tabs too - only react to the tab strip itself.
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource == tabs && tabs.SelectedItem is TabItem { Content: null, Tag: Func<UIElement> build } item) item.Content = build();
        };
        // White "document" card holding the tab strip, framed by the gray canvas around it - the
        // other half of the SAP/DevExpress cue started by headerBar above.
        var tabCard = new Border { Background = Ui.Brush("R3.Surface.Brush"), BorderBrush = Ui.Brush("R3.Border.Brush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Child = tabs };
        Fields.Children.Add(tabCard); ShowImage();
        Finish(() => { ValidateProduct(); _saved = true; });
        AcceptButton!.Content = "Kaydet (Ctrl+S)";
        // §28/§40 quick action: "Kaydet ve Yeni" closes this card and signals the caller (which owns
        // LocalProductService.Save/opens the next dialog) to immediately start a fresh one - kept as a
        // caller-side loop rather than this dialog re-opening itself, since persistence already lives
        // in the caller (see MainWindow.OpenProductList/Edit).
        var saveAndNew = new Button { Content = "Kaydet ve Yeni (Ctrl+Shift+S)", Height = 24, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
        saveAndNew.Click += (_, _) => { try { ValidateProduct(); _saved = true; SaveAndNew = true; DialogResult = true; } catch (Microsoft.Data.Sqlite.SqliteException ex) { Error.Text = ex.SqliteErrorCode == 19 ? "Bu kod zaten kullanılıyor veya seçilen kayıt geçersiz." : "Veritabanına yazılamadı: " + ex.Message; } catch (Exception ex) { Error.Text = ex.Message; } };
        ButtonsPanel!.Children.Insert(1, saveAndNew);
        // SAP-style numbered tab jumps: Ctrl+1..Ctrl+9 then Ctrl+0 walk the eleven tabs left to
        // right without leaving the keyboard - faster than clicking a header once you know the
        // card layout. F6/F7 stay as shortcuts to the two tabs used most while typing a new card
        // (barcodes/units, stock status).
        var tabKeys = new[] { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9, Key.D0 };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { saveAndNew.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { AcceptButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
            else if (e.Key == Key.F6) { tabs.SelectedIndex = 2; e.Handled = true; }
            else if (e.Key == Key.F7) { tabs.SelectedIndex = 8; e.Handled = true; }
            else if (e.Key == Key.F4) { _quickBarcode.Focus(); _quickBarcode.SelectAll(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && Array.IndexOf(tabKeys, e.Key) is var index and >= 0 && index < tabs.Items.Count) { tabs.SelectedIndex = index; e.Handled = true; }
        };
        _baseline = Snapshot(); Loaded += (_, _) => _code.Focus();
        Closing += (_, e) => { if (_saved || !IsDirty()) return; var answer = MessageBox.Show(this, "Kaydedilmemiş değişiklikler var. Kapatılsın mı?", "Ürün kartı", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning); if (answer != MessageBoxResult.Yes) e.Cancel = true; };
    }

    public ProductAggregateEdit ToEditModel()
    {
        var barcodes = _barcodes.ToList();
        var quickBarcode = _quickBarcode.Text.Trim();
        if (quickBarcode.Length > 0)
        {
            var primaryIndex = barcodes.FindIndex(x => x.IsPrimary);
            var existingIndex = barcodes.FindIndex(x => string.Equals(x.Barcode, quickBarcode, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                for (var i = 0; i < barcodes.Count; i++) barcodes[i] = barcodes[i] with { IsPrimary = i == existingIndex };
            }
            else if (primaryIndex >= 0)
            {
                barcodes[primaryIndex] = barcodes[primaryIndex] with { Barcode = quickBarcode, IsPrimary = true, IsActive = true, UnitId = Value(_unit, _defaultUnit), Quantity = 1 };
            }
            else
            {
                barcodes.Insert(0, new ProductChildEdit("", "", "", Barcode: quickBarcode, UnitId: Value(_unit, _defaultUnit), Quantity: 1, IsPrimary: true, IsActive: true));
            }
        }

        return new ProductAggregateEdit(_id, _companyId, _code.Text.Trim(), _name.Text.Trim(), Value(_brand), Value(_category), Value(_unit, _defaultUnit), Value(_productType, "Stock"), Decimal(_vat), _active.IsChecked == true, _variants, barcodes,
            Decimal(_purchaseVat), Decimal(_excise), Decimal(_minimumStock), Decimal(_maximumStock), Decimal(_minimumOrder), Decimal(_orderMultiple), _sellable.IsChecked == true, _imagePath,
            _parentCode.Text.Trim(), _definitionComplete.IsChecked == true, _canQuote.IsChecked == true, _allowFreeIssue.IsChecked == true, _isBundle.IsChecked == true, Decimal(_exciseUnitPrice),
            _units, _suppliers, new ProductInventoryPolicyEdit(Int(_deliveryLead), Int(_maxDeliveryLead), Value(_lotTracking, "None"), Int(_pieceCount), _shipmentLocation.Text.Trim()))
            { ProductGroupId = Value(_productGroup), OriginCountryId = Value(_originCountry), WarehousePolicies = _warehousePolicies.ToList() };
    }

    private UIElement GeneralPanel()
    {
        var root = new Grid { Margin = new Thickness(14) }; root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(185) }); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); root.ColumnDefinitions.Add(new ColumnDefinition());
        var visual = new StackPanel();
        var imageFrame = new Border { Height = 160, Background = Ui.Brush("R3.Surface.Brush"), BorderBrush = Ui.Brush("R3.Border.Brush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3) };
        var imageGrid = new Grid(); _imagePlaceholder = new TextBlock { Text = "▧\nÜrün görseli", FontSize = Ui.Font.Section, Foreground = Ui.Brush("R3.Text.Muted.Brush"), TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; imageGrid.Children.Add(_imagePlaceholder); imageGrid.Children.Add(_image); imageFrame.Child = imageGrid; visual.Children.Add(imageFrame);
        var imageButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 9, 0, 0) };
        var chooseImage = SmallButton("▧  Görsel Seç"); var removeImage = SmallButton("×  Kaldır"); removeImage.Margin = new Thickness(6, 0, 0, 0); chooseImage.Click += (_, _) => ChooseImage(); removeImage.Click += (_, _) => { _imagePath = ""; ShowImage(); }; imageButtons.Children.Add(chooseImage); imageButtons.Children.Add(removeImage); visual.Children.Add(imageButtons);
        visual.Children.Add(new TextBlock { Text = "PNG veya JPG • önerilen 800 × 800 px", FontSize = Ui.Font.Grid, Foreground = Ui.Brush("R3.Text.Muted.Brush"), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 7, 0, 0) }); root.Children.Add(visual);

        var form = new Grid(); Grid.SetColumn(form, 2); root.Children.Add(form); for (var i = 0; i < 4; i++) form.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(115) : new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++) form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddForm(form, 0, 0, "Ürün kodu *", _code); AddForm(form, 0, 2, "Ürün adı *", _name);
        AddForm(form, 1, 0, "Ana ürün kodu", _parentCode); AddForm(form, 1, 2, "Temel birim *", _unit);
        AddForm(form, 2, 0, "Ürün tipi", _productType); AddForm(form, 2, 2, "Ana barkod", _quickBarcode);
        form.Children.Add(_definitionComplete); Grid.SetRow(_definitionComplete, 3); Grid.SetColumn(_definitionComplete, 0); Grid.SetColumnSpan(_definitionComplete, 4);
        var typeInfo = InfoPanel("Ürün tipi desteği", "Stok ve Hizmet tipleri mevcut satış/stok akışlarında desteklenir. Takım/Set, Hammadde ve Mamul tipleri modele hazırdır; ilgili operasyon davranışları henüz desteklenmemektedir."); Grid.SetRow(typeInfo, 4); Grid.SetColumn(typeInfo, 0); Grid.SetColumnSpan(typeInfo, 4); form.Children.Add(typeInfo);
        return root;
    }

    private UIElement ClassificationPanel()
    {
        var root = new StackPanel { Margin = new Thickness(14) };
        var form = new Grid(); for (var i = 0; i < 4; i++) form.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(115) : new GridLength(1, GridUnitType.Star) }); form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddForm(form, 0, 0, "Marka", _brand); AddForm(form, 0, 2, "Kategori", _category);
        AddForm(form, 1, 0, "Stok grubu", _productGroup); AddForm(form, 1, 2, "Menşe ülke", _originCountry); root.Children.Add(form);
        root.Children.Add(InfoPanel("Doğrulama bekleyen alanlar", "Stok sınıfı / satış sınıfı (ASB STKSTASINIF / STKSTSSINIF yalnızca 0/1 bayrak, anlamı doğrulanmadı), ürün özellik grubu (ASB'de 35 bin üründen yalnızca 151'inde dolu) ve departman henüz eklenmedi. Stok grupları: Stok › Tanımlar › Stok Grupları; ülkeler: Stok › Tanımlar › Menşe Ülkeler."));
        return root;
    }

    private UIElement BarcodeUnitsPanel(StoreDatabase database, string companyId)
    {
        var inner = new TabControl { Margin = new Thickness(4) };
        inner.Items.Add(new TabItem { Header = "Barkodlar", Content = ChildPanel(false, database) });
        inner.Items.Add(new TabItem { Header = "Birimler", Content = UnitsPanel(database, companyId) });
        return inner;
    }

    private UIElement UnitsPanel(StoreDatabase database, string companyId)
    {
        var root = new DockPanel { Margin = new Thickness(12) }; var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 9) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        var picker = Lookup(database, "units", companyId, null); var factor = NumberBox(1); factor.Width = 70;
        var baseCheck = new CheckBox { Content = "Temel", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var salesCheck = new CheckBox { Content = "Satış", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var purchaseCheck = new CheckBox { Content = "Alış", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var list = new DataGrid { BorderBrush = Ui.Brush("R3.Border.Strong.Brush"), AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, MinHeight = 190 };
        foreach (var column in new[] { ("Birim", "Birim"), ("Sira", "Sıra"), ("Carpan", "Dönüşüm Katsayısı"), ("Ana", "Ana Birim"), ("Satis", "Satış Birimi"), ("Alis", "Alış Birimi"), ("Aktif", "Aktif") }) list.Columns.Add(new DataGridTextColumn { Header = column.Item2, Binding = new System.Windows.Data.Binding(column.Item1) });
        void Refresh()
        {
            var table = new DataTable(); foreach (var column in new[] { "Birim", "Sira", "Carpan", "Ana", "Satis", "Alis", "Aktif" }) table.Columns.Add(column);
            foreach (var x in _units) table.Rows.Add(TryDisplay(database, "units", x.UnitId), x.Sequence, x.ConversionFactor, x.IsBaseUnit ? "Evet" : "Hayır", x.IsSalesUnit ? "Evet" : "Hayır", x.IsPurchaseUnit ? "Evet" : "Hayır", x.IsActive ? "Aktif" : "Pasif");
            list.ItemsSource = table.DefaultView;
        }
        var add = SmallButton("+  Birim Ekle"); var toggle = SmallButton("◐  Aktif / Pasif"); var remove = SmallButton("×  Listeden Çıkar"); toggle.Margin = remove.Margin = new Thickness(6, 0, 0, 0);
        add.Click += (_, _) => { if (picker.SelectedValue == null) return; _units.Add(new ProductUnitEdit("", picker.SelectedValue.ToString()!, _units.Count + 1, Decimal(factor), baseCheck.IsChecked == true, salesCheck.IsChecked == true, purchaseCheck.IsChecked == true)); Refresh(); };
        toggle.Click += (_, _) => { if (list.SelectedIndex < 0) return; var i = list.SelectedIndex; _units[i] = _units[i] with { IsActive = !_units[i].IsActive }; Refresh(); list.SelectedIndex = i; };
        remove.Click += (_, _) => { if (list.SelectedIndex < 0) return; _units.RemoveAt(list.SelectedIndex); Refresh(); };
        bar.Children.Add(new TextBlock { Text = "Birim:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }); bar.Children.Add(picker);
        bar.Children.Add(new TextBlock { Text = "Çarpan:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) }); bar.Children.Add(factor);
        bar.Children.Add(baseCheck); bar.Children.Add(salesCheck); bar.Children.Add(purchaseCheck); bar.Children.Add(add); bar.Children.Add(toggle); bar.Children.Add(remove);
        root.Children.Add(list); Refresh(); return root;
    }

    private UIElement SuppliersPanel(StoreDatabase database, string companyId)
    {
        var root = new DockPanel { Margin = new Thickness(12) }; var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 9) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        var supplierData = database.Query("SELECT id AS Id, code || ' — ' || name AS Display FROM accounts WHERE company_id=$c AND is_active=1 AND account_type IN ('Supplier','CustomerAndSupplier') ORDER BY code", ("$c", companyId));
        var picker = new ComboBox { ItemsSource = supplierData.DefaultView, DisplayMemberPath = "Display", SelectedValuePath = "Id", IsTextSearchEnabled = true, Width = 220, ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel))) };
        var supplierCode = new TextBox { Width = 110, Tag = "SupplierCode", ToolTip = "Tedarikçinin kendi ürün kodu (opsiyonel)" }; var lead = IntBox(0); lead.Width = 55; var extra = IntBox(0); extra.Width = 55; var priority = IntBox(1); priority.Width = 45; var minimumOrder = NumberBox(0); minimumOrder.Width = 70; minimumOrder.ToolTip = "Boş veya 0: minimum sipariş sınırı yok";
        var list = new DataGrid { BorderBrush = Ui.Brush("R3.Border.Strong.Brush"), AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, MinHeight = 190 };
        foreach (var column in new[] { ("Tedarikci", "Tedarikçi"), ("Kod", "Tedarikçi Stok Kodu"), ("Termin", "Termin Günü"), ("EkTermin", "Ek Termin"), ("Oncelik", "Öncelik"), ("Minimum", "Minimum Sipariş"), ("Aktif", "Aktif") }) list.Columns.Add(new DataGridTextColumn { Header = column.Item2, Binding = new System.Windows.Data.Binding(column.Item1) });
        void Refresh()
        {
            var table = new DataTable(); foreach (var column in new[] { "Tedarikci", "Kod", "Termin", "EkTermin", "Oncelik", "Minimum", "Aktif" }) table.Columns.Add(column);
            foreach (var x in _suppliers) table.Rows.Add(TryDisplay(database, "accounts", x.SupplierAccountId), x.SupplierProductCode, x.LeadTimeDays, x.ExtraLeadTimeDays, x.Priority, x.MinimumOrderQuantity?.ToString("N2", Turkish) ?? "—", x.IsActive ? "Aktif" : "Pasif");
            list.ItemsSource = table.DefaultView;
        }
        var add = SmallButton("+  Tedarikçi Ekle"); var toggle = SmallButton("◐  Aktif / Pasif"); var remove = SmallButton("×  Listeden Çıkar"); toggle.Margin = remove.Margin = new Thickness(6, 0, 0, 0);
        add.Click += (_, _) => { if (picker.SelectedValue == null) return; decimal? minimum = string.IsNullOrWhiteSpace(minimumOrder.Text) ? null : Decimal(minimumOrder); _suppliers.Add(new ProductSupplierEdit("", picker.SelectedValue.ToString()!, supplierCode.Text.Trim(), true, Int(lead), Int(extra), Math.Max(1, Int(priority)), minimum)); Refresh(); };
        toggle.Click += (_, _) => { if (list.SelectedIndex < 0) return; var i = list.SelectedIndex; _suppliers[i] = _suppliers[i] with { IsActive = !_suppliers[i].IsActive }; Refresh(); list.SelectedIndex = i; };
        remove.Click += (_, _) => { if (list.SelectedIndex < 0) return; _suppliers.RemoveAt(list.SelectedIndex); Refresh(); };
        foreach (var (label, control) in new (string, UIElement)[] { ("Tedarikçi:", picker), ("Ürün Kodu:", supplierCode), ("Teslim:", lead), ("Ek Gün:", extra), ("Öncelik:", priority), ("Min. Sipariş:", minimumOrder) })
        { bar.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) }); bar.Children.Add(control); }
        bar.Children.Add(add); bar.Children.Add(toggle); bar.Children.Add(remove);
        root.Children.Add(list); Refresh(); return root;
    }

    private UIElement ChannelPanel(StoreDatabase database)
    {
        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(InfoPanel("E-Ticaret eşleştirmesi", "Kanal entegrasyon motoru bu sprintte devrede değil. Aşağıdaki tablo product_channel_mappings kaydını salt okunur gösterir; yayın durumu ve senkronizasyon zamanları entegrasyon backend'i hazır olduğunda buradan yönetilecektir."));
        var grid = new DataGrid { AutoGenerateColumns = true, IsReadOnly = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, MaxHeight = 160, Margin = new Thickness(0, 10, 0, 0) };
        if (!string.IsNullOrWhiteSpace(_id)) grid.ItemsSource = database.Query("SELECT channel_code AS Kanal, external_product_id AS HariciUrunId, external_variant_id AS HariciVaryantId, external_sku AS HariciSku, is_published AS Yayinda, COALESCE(inventory_synced_at,'') AS SonStokSenkronizasyonu, COALESCE(price_synced_at,'') AS SonFiyatSenkronizasyonu FROM product_channel_mappings WHERE product_id=$p ORDER BY channel_code", ("$p", _id)).DefaultView;
        root.Children.Add(grid); return root;
    }

    private UIElement StockStatusPanel(StoreDatabase database)
    {
        var root = new StackPanel { Margin = new Thickness(14) };
        if (string.IsNullOrWhiteSpace(_id)) { root.Children.Add(InfoPanel("Stok durumu", "Ürün kaydedildikten sonra depo bazlı stok durumu burada görüntülenir.")); return root; }
        var grid = new DataGrid { AutoGenerateColumns = true, IsReadOnly = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column };
        grid.ItemsSource = database.Query("""
            SELECT co.name AS Sirket, br.name AS Sube, w.name AS Depo, COALESCE(v.code,'') AS Varyant, b.quantity_on_hand AS FizikselStok, b.quantity_reserved AS RezerveStok, b.quantity_available AS KullanilabilirStok,
            p.minimum_stock AS Min, p.maximum_stock AS Max,
            CASE WHEN b.quantity_available<0 THEN 'Negatif stok' WHEN p.minimum_stock>0 AND b.quantity_available<p.minimum_stock THEN 'Minimum stok altında' WHEN p.maximum_stock>0 AND b.quantity_available>p.maximum_stock THEN 'Maksimum stok üzerinde' WHEN b.quantity_available=0 THEN 'Stok yok' ELSE 'Normal' END AS Durum
            FROM inventory_balances b JOIN warehouses w ON w.id=b.warehouse_id JOIN branches br ON br.id=w.branch_id JOIN companies co ON co.id=b.company_id JOIN products p ON p.id=b.product_id LEFT JOIN product_variants v ON v.id=b.variant_id
            WHERE b.product_id=$p ORDER BY w.code
            """, ("$p", _id)).DefaultView;
        root.Children.Add(grid); return root;
    }

    private UIElement MovementsPanel(StoreDatabase database)
    {
        var root = new StackPanel { Margin = new Thickness(14) };
        if (string.IsNullOrWhiteSpace(_id)) { root.Children.Add(InfoPanel("Hareketler", "Ürün kaydedildikten sonra stok hareketleri (giriş/çıkış/transfer) burada görüntülenir.")); return root; }
        root.Children.Add(new TextBlock { Text = "Salt okunur hareket dökümü. Cari alanı inventory hareket kaydında henüz tutulmadığı için gösterilmez.", Foreground = Ui.Brush("R3.Text.Secondary.Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var grid = new DataGrid { AutoGenerateColumns = true, IsReadOnly = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column };
        grid.ItemsSource = database.Query("""
            SELECT t.transaction_at AS Tarih, t.transaction_type AS HareketTipi, w.name AS Depo, COALESCE(v.code,'') AS Varyant, u.name AS Birim,
            t.quantity AS Miktar, t.quantity AS TemelBirimMiktari, COALESCE(t.document_type || ' ' || t.document_id,'') AS ReferansBelge,
            COALESCE(t.correlation_id,'') AS CorrelationId, '' AS Kullanici, COALESCE(t.description,'') AS Aciklama
            FROM inventory_transactions t JOIN warehouses w ON w.id=t.warehouse_id JOIN products p ON p.id=t.product_id JOIN units u ON u.id=p.base_unit_id LEFT JOIN product_variants v ON v.id=t.variant_id WHERE t.product_id=$p ORDER BY t.transaction_at DESC LIMIT 200
            """, ("$p", _id)).DefaultView;
        root.Children.Add(grid); return root;
    }

    private UIElement HistoryPanel(StoreDatabase database)
    {
        var root = new StackPanel { Margin = new Thickness(14) };
        if (string.IsNullOrWhiteSpace(_id)) { root.Children.Add(InfoPanel("Geçmiş", "Ürün kaydedildikten sonra denetim (audit) geçmişi burada görüntülenir.")); return root; }
        var grid = new DataGrid { AutoGenerateColumns = true, IsReadOnly = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column };
        var history = database.Query("SELECT created_at AS Tarih, COALESCE(user_id,'') AS Kullanici, action AS Islem, COALESCE(old_values,'') AS EskiDegerOzeti, COALESCE(new_values,'') AS YeniDegerOzeti, entity_type AS Kaynak, id AS IslemId FROM audit_logs WHERE entity_type='Product' AND entity_id=$p ORDER BY created_at DESC", ("$p", _id));
        foreach (DataRow row in history.Rows) row["Islem"] = R3.Desktop.Presentation.InventoryPresentation.AuditActionLabel(row["Islem"].ToString() ?? "");
        grid.ItemsSource = history.DefaultView;
        root.Children.Add(grid); return root;
    }

    private UIElement CommercialPanel()
    {
        var panel = new Grid { Margin = new Thickness(14) }; for (var i = 0; i < 4; i++) panel.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(130) : new GridLength(1, GridUnitType.Star) }); for (var i = 0; i < 3; i++) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddForm(panel, 0, 0, "Satış KDV %", _vat); AddForm(panel, 0, 2, "Alış KDV %", _purchaseVat);
        AddForm(panel, 1, 0, "ÖTV %", _excise); AddForm(panel, 1, 2, "ÖTV birim fiyatı", _exciseUnitPrice);
        var flags = new WrapPanel { Orientation = Orientation.Horizontal }; flags.Children.Add(_sellable); flags.Children.Add(_canQuote); flags.Children.Add(_allowFreeIssue); flags.Children.Add(_isBundle);
        Grid.SetRow(flags, 2); Grid.SetColumnSpan(flags, 4); panel.Children.Add(flags);
        return panel;
    }

    private UIElement StockPanel()
    {
        var panel = new Grid { Margin = new Thickness(14) }; for (var i = 0; i < 4; i++) panel.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(140) : new GridLength(1, GridUnitType.Star) }); for (var i = 0; i < 5; i++) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddForm(panel, 0, 0, "Minimum stok", _minimumStock); AddForm(panel, 0, 2, "Maksimum stok", _maximumStock);
        AddForm(panel, 1, 0, "Minimum sipariş miktarı", _minimumOrder); AddForm(panel, 1, 2, "Sipariş katı", _orderMultiple);
        AddForm(panel, 2, 0, "Satış teslim süresi (gün)", _deliveryLead); AddForm(panel, 2, 2, "Maksimum teslim süresi (gün)", _maxDeliveryLead);
        AddForm(panel, 3, 0, "Lot takibi", _lotTracking); AddForm(panel, 3, 2, "Parça sayısı", _pieceCount);
        AddForm(panel, 4, 0, "Sevk yeri", _shipmentLocation);
        var note = new Border { Background = Ui.Brush("R3.Accent.Soft.Brush"), CornerRadius = new CornerRadius(5), Padding = new Thickness(12), Margin = new Thickness(4, 14, 4, 0), Child = new TextBlock { Text = "Stok seviyeleri satınalma önerileri ve kritik stok uyarılarında kullanılır. 0 = sınırsız / tanımsız. Aşağıda depo bazlı değer tanımlanmayan depolarda bu ürün geneli değerler geçerlidir.", Foreground = Ui.Brush("R3.Info.Brush"), TextWrapping = TextWrapping.Wrap } };
        var noteRow = new RowDefinition { Height = GridLength.Auto }; panel.RowDefinitions.Add(noteRow); Grid.SetRow(note, 5); Grid.SetColumnSpan(note, 4); panel.Children.Add(note);
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); var overrides = WarehousePolicyEditor(); Grid.SetRow(overrides, 6); Grid.SetColumnSpan(overrides, 4); panel.Children.Add(overrides);
        return panel;
    }

    // Depo bazlı min/max (ASB STOKSUBEMINMAX equivalent): overrides the company-level values above for
    // one warehouse; Stok Durumu shows which one applied (PolitikaKaynagi) and flags Min Altı / Max Üstü.
    private UIElement WarehousePolicyEditor()
    {
        var root = new StackPanel { Margin = new Thickness(4, 10, 4, 0) };
        root.Children.Add(new TextBlock { Text = "Depo bazlı minimum / maksimum stok", FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Text.Primary.Brush"), Margin = new Thickness(0, 0, 0, 6) });
        var entry = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var warehouse = new ComboBox { Width = 240, Height = 24, ItemsSource = _warehouses, DisplayMemberPath = "Display", SelectedValuePath = "Id", SelectedIndex = _warehouses.Count > 0 ? 0 : -1 };
        var min = NumberBox(0); min.Width = 90; min.Margin = new Thickness(8, 0, 0, 0); var max = NumberBox(0); max.Width = 90; max.Margin = new Thickness(6, 0, 0, 0);
        var add = SmallButton("+  Ekle / Güncelle"); add.Margin = new Thickness(8, 0, 0, 0); var remove = SmallButton("×  Seçiliyi Sil"); remove.Margin = new Thickness(6, 0, 0, 0);
        entry.Children.Add(warehouse); entry.Children.Add(new TextBlock { Text = "Min", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Foreground = Ui.Brush("R3.Text.Secondary.Brush") }); entry.Children.Add(min);
        entry.Children.Add(new TextBlock { Text = "Max", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Foreground = Ui.Brush("R3.Text.Secondary.Brush") }); entry.Children.Add(max);
        entry.Children.Add(add); entry.Children.Add(remove); root.Children.Add(entry);
        var list = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column, SelectionMode = DataGridSelectionMode.Single, Height = 78, CanUserAddRows = false };
        list.Columns.Add(new DataGridTextColumn { Header = "Depo", Binding = new System.Windows.Data.Binding("Depo"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        list.Columns.Add(new DataGridTextColumn { Header = "Minimum", Binding = new System.Windows.Data.Binding("Min") { StringFormat = "N2" }, Width = 100 });
        list.Columns.Add(new DataGridTextColumn { Header = "Maksimum", Binding = new System.Windows.Data.Binding("Max") { StringFormat = "N2" }, Width = 100 });
        string WarehouseName(string id) => _warehouses.Cast<System.Data.DataRowView>().FirstOrDefault(r => r["Id"].ToString() == id)?["Display"].ToString() ?? id;
        void Refresh() => list.ItemsSource = _warehousePolicies.Select(x => new { x.WarehouseId, Depo = WarehouseName(x.WarehouseId), Min = x.MinimumStock, Max = x.MaximumStock }).ToList();
        add.Click += (_, _) =>
        {
            try
            {
                if (warehouse.SelectedValue is not string id) throw new ArgumentException("Önce bir depo seçin.");
                var policy = new ProductWarehousePolicyEdit(id, Decimal(min), Decimal(max));
                if (policy.MinimumStock < 0 || policy.MaximumStock < 0) throw new ArgumentException("Minimum/maksimum stok negatif olamaz.");
                if (policy.MaximumStock > 0 && policy.MaximumStock < policy.MinimumStock) throw new ArgumentException("Maksimum stok, minimum stoktan küçük olamaz.");
                _warehousePolicies.RemoveAll(x => x.WarehouseId == id); _warehousePolicies.Add(policy); Refresh(); Error.Text = "";
            }
            catch (ArgumentException ex) { Error.Text = ex.Message; }
        };
        remove.Click += (_, _) => { if (list.SelectedItem?.GetType().GetProperty("WarehouseId")?.GetValue(list.SelectedItem) is string id) { _warehousePolicies.RemoveAll(x => x.WarehouseId == id); Refresh(); } };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem?.GetType().GetProperty("WarehouseId")?.GetValue(list.SelectedItem) is not string id || _warehousePolicies.FirstOrDefault(x => x.WarehouseId == id) is not { } selected) return;
            warehouse.SelectedValue = id; min.Text = selected.MinimumStock.ToString("0.##", Turkish); max.Text = selected.MaximumStock.ToString("0.##", Turkish);
        };
        root.Children.Add(list); Refresh();
        return root;
    }

    private static ComboBox OptionalLookup(StoreDatabase db, string sql, string companyId, string? selected)
    {
        var data = db.Query($"SELECT '' AS Id, '(Seçilmedi)' AS Display, '' AS SortKey UNION ALL SELECT * FROM ({sql}) ORDER BY SortKey", ("$c", (object)companyId));
        return new ComboBox { ItemsSource = data.DefaultView, DisplayMemberPath = "Display", SelectedValuePath = "Id", IsTextSearchEnabled = true, SelectedValue = selected ?? "" };
    }

    private UIElement ChildPanel(bool variants, StoreDatabase database)
    {
        var source = variants ? _variants : _barcodes; var root = new DockPanel { Margin = new Thickness(12) }; var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 9) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        var list = new DataGrid { BorderBrush = Ui.Brush("R3.Border.Strong.Brush"), AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, MinHeight = 190 };
        if (variants)
        {
            foreach (var column in new[] { ("Kod", "Varyant Kodu"), ("Ad", "Varyant Adı"), ("Renk", "Renk"), ("Beden", "Beden"), ("BedenTipi", "Beden Tipi"), ("Model", "Model"), ("Aktif", "Aktif") }) list.Columns.Add(new DataGridTextColumn { Header = column.Item2, Binding = new System.Windows.Data.Binding(column.Item1) });
        }
        else
        {
            foreach (var column in new[] { ("Barkod", "Barkod"), ("Birim", "Birim"), ("Miktar", "Miktar Katsayısı"), ("Primary", "Birincil"), ("Aktif", "Aktif") }) list.Columns.Add(new DataGridTextColumn { Header = column.Item2, Binding = new System.Windows.Data.Binding(column.Item1) });
        }
        void Refresh()
        {
            var table = new DataTable();
            if (variants) foreach (var column in new[] { "Kod", "Ad", "Renk", "Beden", "BedenTipi", "Model", "Aktif" }) table.Columns.Add(column);
            else foreach (var column in new[] { "Barkod", "Birim", "Miktar", "Primary", "Aktif" }) table.Columns.Add(column);
            foreach (var x in source)
            {
                if (variants) table.Rows.Add(x.Code, x.Name, x.ColorCode, x.SizeCode, x.SizeType, x.ModelCode, x.IsActive ? "Aktif" : "Pasif");
                else table.Rows.Add(x.Barcode, TryDisplay(database, "units", x.UnitId), x.Quantity, x.IsPrimary && x.IsActive ? "Evet" : "Hayır", x.IsActive ? "Aktif" : "Pasif");
            }
            list.ItemsSource = table.DefaultView;
        }
        var add = SmallButton(variants ? "+  Varyant Ekle" : "+  Barkod Ekle"); var edit = SmallButton("Düzenle"); var toggle = SmallButton("◐  Aktif / Pasif"); var remove = SmallButton("×  Listeden Çıkar"); edit.Margin = toggle.Margin = remove.Margin = new Thickness(6, 0, 0, 0);
        add.Click += (_, _) =>
        {
            if (variants)
            {
                var dialog = new ProductVariantDialog(null) { Owner = this }; if (dialog.ShowDialog() == true) _variants.Add(dialog.ToEdit());
            }
            else { var dialog = new ProductBarcodeDialog(database, _companyId, null, Value(_unit, _defaultUnit)) { Owner = this }; if (dialog.ShowDialog() == true) { var item = dialog.ToEdit(); if (item.IsPrimary) for (var i = 0; i < _barcodes.Count; i++) _barcodes[i] = _barcodes[i] with { IsPrimary = false }; _barcodes.Add(item); } }
            Refresh();
        };
        edit.Click += (_, _) =>
        {
            if (list.SelectedIndex < 0) return; var index = list.SelectedIndex;
            if (variants) { var dialog = new ProductVariantDialog(_variants[index]) { Owner = this }; if (dialog.ShowDialog() == true) _variants[index] = dialog.ToEdit(); }
            else { var dialog = new ProductBarcodeDialog(database, _companyId, _barcodes[index], Value(_unit, _defaultUnit)) { Owner = this }; if (dialog.ShowDialog() == true) { var item = dialog.ToEdit(); if (item.IsPrimary) for (var i = 0; i < _barcodes.Count; i++) if (i != index) _barcodes[i] = _barcodes[i] with { IsPrimary = false }; _barcodes[index] = item; } }
            Refresh(); list.SelectedIndex = index;
        };
        toggle.Click += (_, _) => { if (list.SelectedIndex < 0) return; var i = list.SelectedIndex; source[i] = source[i] with { IsActive = !source[i].IsActive }; Refresh(); list.SelectedIndex = i; };
        remove.Click += (_, _) => { if (list.SelectedIndex < 0) return; source.RemoveAt(list.SelectedIndex); Refresh(); };
        bar.Children.Add(add); bar.Children.Add(edit); bar.Children.Add(toggle); bar.Children.Add(remove); root.Children.Add(list); Refresh(); return root;
    }

    private static UIElement InfoPanel(string title, string text)
    {
        var panel = new StackPanel { Margin = new Thickness(22) }; panel.Children.Add(new TextBlock { Text = title, FontSize = Ui.Font.Title, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Text.Primary.Brush") }); panel.Children.Add(new TextBlock { Text = text, Foreground = Ui.Brush("R3.Text.Secondary.Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }); return panel;
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
        if (Decimal(_exciseUnitPrice) < 0) throw new ArgumentException("ÖTV birim fiyatı negatif olamaz.");
        if (Decimal(_minimumStock) < 0 || Decimal(_maximumStock) < 0 || Decimal(_minimumOrder) < 0 || Decimal(_orderMultiple) < 0) throw new ArgumentException("Stok ve sipariş değerleri negatif olamaz.");
        if (Decimal(_maximumStock) > 0 && Decimal(_maximumStock) < Decimal(_minimumStock)) throw new ArgumentException("Maksimum stok, minimum stoktan küçük olamaz.");
        if (Int(_deliveryLead) < 0 || Int(_maxDeliveryLead) < 0 || Int(_pieceCount) < 0) throw new ArgumentException("Teslim süresi ve parça sayısı negatif olamaz.");
        if (Int(_maxDeliveryLead) > 0 && Int(_maxDeliveryLead) < Int(_deliveryLead)) throw new ArgumentException("Maksimum teslim süresi, satış teslim süresinden küçük olamaz.");
    }

    private static string TryDisplay(StoreDatabase db, string table, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "—";
        try { var rows = db.Query($"SELECT code || ' — ' || name AS Display FROM {table} WHERE id=$id", ("$id", id)); return rows.Rows.Count > 0 ? rows.Rows[0][0].ToString()! : id; } catch { return id; }
    }

    private static ComboBox Lookup(StoreDatabase db, string table, string companyId, string? selected)
    {
        var data = db.Query($"SELECT id AS Id, code || ' — ' || name AS Display FROM {table} WHERE company_id=$c AND is_active=1 ORDER BY code", ("$c", (object)companyId));
        var combo = new ComboBox { ItemsSource = data.DefaultView, DisplayMemberPath = "Display", SelectedValuePath = "Id", IsTextSearchEnabled = true, SelectedValue = selected ?? "" }; if (combo.SelectedIndex < 0 && table == "units") combo.SelectedIndex = 0; return combo;
    }
    private static TextBox NumberBox(decimal value) => new() { Text = value.ToString("0.##", Turkish), HorizontalContentAlignment = HorizontalAlignment.Right, Tag = "Numeric" };
    private static TextBox IntBox(int value) => new() { Text = value.ToString(Turkish), HorizontalContentAlignment = HorizontalAlignment.Right, Tag = "Numeric" };
    private static decimal Decimal(TextBox box) { if (!decimal.TryParse(box.Text, NumberStyles.Number, Turkish, out var value)) throw new ArgumentException("Sayısal alanları geçerli bir değerle doldurun."); return value; }
    private static int Int(TextBox box) { if (!int.TryParse(box.Text, NumberStyles.Integer, Turkish, out var value)) throw new ArgumentException("Tam sayı alanlarını geçerli bir değerle doldurun."); return value; }
    private static string Value(ComboBox box, string fallback = "") => box.SelectedValue?.ToString() ?? fallback;
    private static Button SmallButton(string text) => new() { Content = text, Height = 24, FontSize = 11, Padding = new Thickness(9, 2, 9, 2), Background = Ui.Brush("R3.Surface.Brush"), BorderBrush = Ui.Brush("R3.Border.Strong.Brush"), Foreground = Ui.Brush("R3.Text.Primary.Brush") };
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString("#" + hex));
    // DevExpress LayoutControl-style row: small bold-ish gray caption tight against a 22px-tall
    // white field, no wasted vertical air - denser than the app's already-compact global default
    // (24px/5,3 padding) since a form with this many fields needs the density more than most.
    private static void AddForm(Grid grid, int row, int column, string label, Control control)
    {
        var caption = new TextBlock { Text = label, Foreground = Ui.Brush("R3.Text.Secondary.Brush"), FontSize = Ui.Font.Grid, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 2, 8, 2) };
        Grid.SetRow(caption, row); Grid.SetColumn(caption, column); grid.Children.Add(caption);
        control.MinHeight = 22; control.FontSize = 11; control.Margin = new Thickness(0, 2, 10, 2); control.Padding = new Thickness(6, 2, 6, 2);
        Grid.SetRow(control, row); Grid.SetColumn(control, column + 1); grid.Children.Add(control);
    }
    private string Snapshot() => $"{Value(_productGroup)}|{Value(_originCountry)}|{string.Join(";", _warehousePolicies.Select(x => $"{x.WarehouseId}:{x.MinimumStock}:{x.MaximumStock}"))}|{_code.Text}|{_name.Text}|{_parentCode.Text}|{Value(_brand)}|{Value(_category)}|{Value(_unit)}|{Value(_productType)}|{_vat.Text}|{_purchaseVat.Text}|{_excise.Text}|{_exciseUnitPrice.Text}|{_minimumStock.Text}|{_maximumStock.Text}|{_minimumOrder.Text}|{_orderMultiple.Text}|{_deliveryLead.Text}|{_maxDeliveryLead.Text}|{Value(_lotTracking)}|{_pieceCount.Text}|{_shipmentLocation.Text}|{_active.IsChecked}|{_sellable.IsChecked}|{_definitionComplete.IsChecked}|{_canQuote.IsChecked}|{_allowFreeIssue.IsChecked}|{_isBundle.IsChecked}|{_imagePath}|{string.Join(';', _variants)}|{string.Join(';', _barcodes)}|{string.Join(';', _units)}|{string.Join(';', _suppliers)}";
    private bool IsDirty() => Snapshot() != _baseline;
    private sealed record Choice(string Id, string Name);
}

public sealed class ProductVariantDialog : EditorDialog
{
    private readonly string _id; private readonly TextBox _code, _name, _color, _size, _sizeType, _model; private readonly CheckBox _active;
    public ProductVariantDialog(ProductChildEdit? existing) : base(existing == null ? "Yeni Varyant" : "Varyantı Düzenle")
    {
        Width = 430; _id = existing?.Id ?? "";
        _code = Field("Varyant kodu *", new TextBox { Text = existing?.Code ?? "", MaxLength = 60 });
        _name = Field("Varyant adı *", new TextBox { Text = existing?.Name ?? "", MaxLength = 160 });
        _color = Field("Renk kodu", new TextBox { Text = existing?.ColorCode ?? "", MaxLength = 50 });
        _size = Field("Beden kodu", new TextBox { Text = existing?.SizeCode ?? "", MaxLength = 50 });
        _sizeType = Field("Beden tipi", new TextBox { Text = existing?.SizeType ?? "", MaxLength = 50 });
        _model = Field("Model kodu", new TextBox { Text = existing?.ModelCode ?? "", MaxLength = 80 });
        _active = Field("Durum", new CheckBox { Content = "Aktif", IsChecked = existing?.IsActive ?? true });
        Finish(() => { if (string.IsNullOrWhiteSpace(_code.Text) || string.IsNullOrWhiteSpace(_name.Text)) throw new ArgumentException("Varyant kodu ve adı zorunludur."); });
        Loaded += (_, _) => _code.Focus();
    }
    public ProductChildEdit ToEdit() => new(_id, _code.Text.Trim(), _name.Text.Trim(), _size.Text.Trim(), _color.Text.Trim(), _model.Text.Trim(), IsActive: _active.IsChecked == true, SizeType: _sizeType.Text.Trim());
}

public sealed class ProductBarcodeDialog : EditorDialog
{
    private readonly string _id; private readonly TextBox _barcode, _quantity; private readonly ComboBox _unit; private readonly CheckBox _primary, _active; private readonly string _variantId;
    public ProductBarcodeDialog(StoreDatabase database, string companyId, ProductChildEdit? existing, string defaultUnit) : base(existing == null ? "Yeni Barkod" : "Barkodu Düzenle")
    {
        Width = 430; _id = existing?.Id ?? ""; _variantId = existing?.VariantId ?? "";
        _barcode = Field("Barkod *", new TextBox { Text = existing?.Barcode ?? "", MaxLength = 80, ToolTip = "Metin olarak saklanır; baştaki sıfırlar korunur." });
        var units = database.Query("SELECT id AS Id, code || ' — ' || name AS Display FROM units WHERE company_id=$c AND is_active=1 ORDER BY code", ("$c", companyId));
        _unit = Field("Birim", new ComboBox { ItemsSource = units.DefaultView, DisplayMemberPath = "Display", SelectedValuePath = "Id", SelectedValue = existing?.UnitId ?? defaultUnit, IsTextSearchEnabled = true });
        _quantity = Field("Miktar katsayısı *", new TextBox { Text = (existing?.Quantity ?? 1).ToString("0.##", TurkishCulture), Tag = "Numeric" });
        _primary = Field("Birincil", new CheckBox { Content = "Birincil barkod", IsChecked = existing?.IsPrimary ?? false });
        _active = Field("Durum", new CheckBox { Content = "Aktif", IsChecked = existing?.IsActive ?? true });
        Fields.Children.Add(new TextBlock { Text = "Barkod tipi: Domain discovery gerekli; ASB kodu doğrulanmadan varsayılan tip atanmaz.", FontSize = 10, Foreground = Ui.Brush("R3.Text.Secondary.Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        Finish(() => { if (string.IsNullOrWhiteSpace(_barcode.Text)) throw new ArgumentException("Barkod boş olamaz."); if (!decimal.TryParse(_quantity.Text, NumberStyles.Number, TurkishCulture, out var quantity) || quantity <= 0) throw new ArgumentException("Miktar katsayısı sıfırdan büyük olmalıdır."); if (_unit.SelectedValue == null) throw new ArgumentException("Birim seçin."); });
        Loaded += (_, _) => _barcode.Focus();
    }
    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");
    public ProductChildEdit ToEdit() => new(_id, "", "", Barcode: _barcode.Text.Trim(), UnitId: _unit.SelectedValue?.ToString() ?? "", Quantity: decimal.Parse(_quantity.Text, NumberStyles.Number, TurkishCulture), IsPrimary: _primary.IsChecked == true, IsActive: _active.IsChecked == true, VariantId: _variantId);
}

public sealed class GridColumnVisibilityDialog : EditorDialog
{
    private readonly IReadOnlyList<(DataGridColumn Column, CheckBox Check)> _items;
    public GridColumnVisibilityDialog(DataGrid grid) : base("Kolon Görünürlüğü")
    {
        Width = 340;
        Fields.Children.Add(new TextBlock { Text = "Listede görmek istediğiniz kolonları seçin. Sıralama ve genişlik ayarları grid üzerinden değiştirilebilir.", TextWrapping = TextWrapping.Wrap, Foreground = Ui.Brush("R3.Text.Secondary.Brush"), Margin = new Thickness(0, 0, 0, 8) });
        var items = new List<(DataGridColumn Column, CheckBox Check)>();
        foreach (var column in grid.Columns)
        {
            var check = new CheckBox { Content = column.Header?.ToString() ?? "Kolon", IsChecked = column.Visibility == Visibility.Visible, Margin = new Thickness(2, 3, 2, 3) };
            Fields.Children.Add(check); items.Add((column, check));
        }
        _items = items;
        Finish(() =>
        {
            if (_items.All(x => x.Check.IsChecked != true)) throw new ArgumentException("En az bir kolon görünür olmalıdır.");
            foreach (var item in _items) item.Column.Visibility = item.Check.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        });
    }
}

public sealed class InventoryOperationDialog : EditorDialog
{
    private readonly string _kind; private readonly LocalInventoryService _service; private readonly LocalInventoryDocumentService _documents; private readonly LocalBarcodeResolver _resolver; private readonly StoreDatabase _database; private readonly string _company, _branch, _warehouse; private decimal _factor = 1;
    private readonly TextBox _product, _variant, _quantity, _cost, _reference, _description, _targetWarehouse, _counted;
    private readonly TextBox _lotNo = new(), _serials = new(); private readonly DatePicker _expiry = new();
    private readonly ComboBox _location;
    public InventoryOperationDialog(string kind, LocalInventoryService service, StoreDatabase database, string company, string branch, string warehouse) : base(kind)
    {
        _kind = kind; _service = service; _documents = new LocalInventoryDocumentService(database); _resolver = new LocalBarcodeResolver(database); _database = database; _company = company; _branch = branch; _warehouse = warehouse;
        Width = 500;
        Fields.Children.Add(new TextBlock { Text = "Ürünü barkod okuyucuyla okutun veya listeden seçin. Teknik kayıt kimlikleri ekranda gösterilmez.", Foreground = Ui.Brush("R3.Text.Secondary.Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
        var barcode = Field("Barkod / Ürün kodu", new TextBox { MaxLength = 80 });
        var products = database.Query("SELECT id AS Id, code || ' — ' || name AS Display FROM products WHERE company_id=$c AND is_active=1 AND product_type<>'Service' ORDER BY code", ("$c", company));
        var productPicker = Field("Ürün *", new ComboBox { ItemsSource = products.DefaultView, DisplayMemberPath = "Display", SelectedValuePath = "Id", IsTextSearchEnabled = true, IsEditable = true });
        var resolved = new TextBlock { Foreground = Ui.Brush("R3.Info.Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 2), FontWeight = FontWeights.SemiBold };
        Fields.Children.Add(resolved);
        _product = new TextBox(); _variant = new TextBox();
        async Task ShowBalance()
        {
            if (string.IsNullOrWhiteSpace(_product.Text)) return;
            var balance = await _service.GetBalanceAsync(_warehouse, _product.Text, string.IsNullOrWhiteSpace(_variant.Text) ? null : _variant.Text);
            resolved.Text = string.IsNullOrWhiteSpace(resolved.Text) ? $"Mevcut stok: {balance?.QuantityOnHand ?? 0:N2}" : $"{resolved.Text}  •  Mevcut stok: {balance?.QuantityOnHand ?? 0:N2}";
        }
        productPicker.SelectionChanged += async (_, _) => { _product.Text = productPicker.SelectedValue?.ToString() ?? ""; _variant.Text = ""; _factor = 1; resolved.Text = productPicker.Text; await ShowBalance(); };
        barcode.KeyDown += async (_, e) => { if (e.Key != Key.Enter) return; try { var result = _resolver.ResolveForInventory(barcode.Text, _company); _product.Text = result.ProductId; _variant.Text = result.VariantId ?? ""; _factor = result.QuantityFactor; productPicker.SelectedValue = result.ProductId; resolved.Text = $"{result.ProductCode} — {result.ProductName} • {result.UnitName} • Çarpan: {_factor:N2}"; await ShowBalance(); e.Handled = true; } catch (Exception ex) { resolved.Text = ex.Message; } };
        _quantity = kind == "Sayım" ? new TextBox { Text = "1", Tag = "Numeric" } : Field("Miktar *", new TextBox { Text = "1", Tag = "Numeric" });
        var locations = database.Query("SELECT id AS Id, code || ' — ' || name AS Display, is_default_inbound AS DefaultIn, is_default_outbound AS DefaultOut FROM warehouse_locations WHERE warehouse_id=$w AND is_active=1 ORDER BY code", ("$w", warehouse));
        _location = (ComboBox)Field("Lokasyon", new ComboBox { ItemsSource = locations.DefaultView, DisplayMemberPath = "Display", SelectedValuePath = "Id", IsTextSearchEnabled = true, IsEditable = true });
        var defaultColumn = kind == "Stok Giriş" ? "DefaultIn" : "DefaultOut";
        var defaultLocation = locations.Rows.Cast<DataRow>().FirstOrDefault(row => Convert.ToInt32(row[defaultColumn]) == 1);
        if (defaultLocation != null) _location.SelectedValue = defaultLocation["Id"].ToString();
        _cost = Field("Birim maliyet", new TextBox { Tag = "Numeric" }); _reference = Field("Referans no", new TextBox());
        if (kind == "Depo Transfer") _targetWarehouse = Field("Hedef depo ID *", new TextBox { MaxLength = 80 }); else _targetWarehouse = new TextBox();
        if (kind == "Sayım") _counted = Field("Fiziki sayım miktarı *", new TextBox { Text = "0", Tag = "Numeric" }); else _counted = new TextBox();
        if (kind is "Stok Giriş" or "Stok Çıkış")
        {
            // Lot / Seri: required only for products whose card sets lot takibi (checked on approve).
            var tracking = new TextBlock { FontSize = Ui.Font.Grid, Foreground = Ui.Brush("R3.Text.Secondary.Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) }; Fields.Children.Add(tracking);
            _lotNo = Field("Lot no", new TextBox { MaxLength = 50, CharacterCasing = CharacterCasing.Upper });
            if (kind == "Stok Giriş") _expiry = Field("Son kullanma tarihi (lot girişinde)", new DatePicker());
            _serials = Field("Seri numaraları (virgül veya satır ile ayırın; adet kadar)", new TextBox { Height = 44, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap });
            _product.TextChanged += (_, _) =>
            {
                var mode = string.IsNullOrWhiteSpace(_product.Text) ? "None" : database.Query("SELECT COALESCE(lot_tracking_type,'None') FROM product_inventory_policies WHERE product_id=$p", ("$p", _product.Text.Trim())).Rows.Cast<DataRow>().FirstOrDefault()?[0]?.ToString() ?? "None";
                tracking.Text = mode switch { "Lot" => "Bu ürün LOT takiplidir: lot numarası zorunludur.", "Serial" => "Bu ürün SERİ takiplidir: her adet için bir seri numarası girin.", _ => "Bu ürün lot/seri takipli değil (isteğe bağlı girilebilir)." };
            };
        }
        _description = Field("Açıklama", new TextBox { Height = 60, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap }); Finish(Save);
        Loaded += (_, _) => barcode.Focus();
    }
    private void Save()
    {
        if (!decimal.TryParse(_quantity.Text, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out var qty) || qty <= 0) throw new ArgumentException("Miktar 0'dan büyük olmalıdır.");
        if (!decimal.TryParse(string.IsNullOrWhiteSpace(_cost.Text) ? "0" : _cost.Text, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out var cost)) throw new ArgumentException("Maliyet sayı olmalıdır.");
        var post = new InventoryPost(_company, _branch, _warehouse, _product.Text.Trim(), string.IsNullOrWhiteSpace(_variant.Text) ? null : _variant.Text.Trim(), qty * _factor, cost, DateTime.Now, _reference.Text, _description.Text);
        if (_kind is "Stok Giriş" or "Stok Çıkış")
        {
            var unit = _database.Query("SELECT base_unit_id FROM products WHERE id=$product AND company_id=$company", ("$product", post.ProductId), ("$company", _company)).Rows.Cast<DataRow>().FirstOrDefault()?["base_unit_id"]?.ToString();
            if (string.IsNullOrWhiteSpace(unit)) throw new InvalidOperationException("Ürünün temel birimi bulunamadı.");
            var type = _kind == "Stok Giriş" ? "ManualIn" : "ManualOut";
            _documents.CreateDraft(new InventoryDocumentEdit(_company, _branch, _warehouse, type, post.TransactionAt, [new InventoryDocumentLineEdit(post.ProductId, unit, qty, post.Quantity, post.VariantId, post.UnitCost, LotNo: string.IsNullOrWhiteSpace(_lotNo.Text) ? null : _lotNo.Text.Trim(), SerialNo: string.IsNullOrWhiteSpace(_serials.Text) ? null : _serials.Text.Trim(), ExpiryDate: _expiry.SelectedDate, Description: post.Description ?? "", LocationId: _location.SelectedValue?.ToString())], post.ReferenceNo, post.Description ?? ""), Environment.UserName);
        }
        else if (_kind == "Depo Transfer")
        {
            var targetWarehouse = _targetWarehouse.Text.Trim();
            if (string.IsNullOrWhiteSpace(targetWarehouse)) throw new ArgumentException("Hedef depo ID zorunludur.");
            var transferUnit = _database.Query("SELECT base_unit_id FROM products WHERE id=$product AND company_id=$company", ("$product", post.ProductId), ("$company", _company)).Rows.Cast<DataRow>().FirstOrDefault()?["base_unit_id"]?.ToString();
            if (string.IsNullOrWhiteSpace(transferUnit)) throw new InvalidOperationException("Ürünün temel birimi bulunamadı.");
            var transfers = new LocalInventoryTransferService(_database);
            transfers.CreateDraft(new InventoryTransferEdit(_company, _branch, _warehouse, _location.SelectedValue?.ToString(), targetWarehouse, null, post.TransactionAt, [new InventoryTransferLineEdit(post.ProductId, transferUnit, qty, post.Quantity, post.VariantId, Description: post.Description ?? "")], post.Description ?? ""), Environment.UserName);
        }
        else { if (!decimal.TryParse(_counted.Text, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out var counted) || counted < 0) throw new ArgumentException("Sayım sonucu geçerli olmalıdır."); _service.PostCountAdjustmentAsync(post, counted).GetAwaiter().GetResult(); }
    }
}

// Phase 8 (§12-13/§40): real product lookup (code/name/barcode, reusing the exact same
// LocalProductService/LocalBarcodeResolver the product and inventory screens already use) and a
// real stock preview (InventoryBalance, never a guessed number), plus a real e-document routing
// preview (ElectronicDocumentRoutingService - the same service Post() itself calls) so the user
// sees E-Fatura/E-Arşiv before the invoice is even posted. Still a single-line quick draft form -
// multi-line entry is unchanged/deferred, see docs/architecture/INVOICE-EDOCUMENT-UI.md.
public sealed class SalesInvoiceDialog : EditorDialog
{
    private readonly LocalSalesService _sales; private readonly LocalProductService _products; private readonly LocalInventoryService _inventory; private readonly LocalBarcodeResolver _resolver;
    private readonly string _company, _branch, _warehouse, _account, _warehouseName; private decimal _factor = 1; private decimal _discountRate;
    private readonly string? _editingId;
    private readonly TextBox _product, _variant, _unit, _qty, _price, _vat;
    private readonly TextBlock _resolved;
    public string? DocumentId { get; private set; }
    public SalesInvoiceDialog(LocalSalesService sales, StoreDatabase database, string company, string branch, string warehouse, string account, string? editingId = null) : base(string.IsNullOrWhiteSpace(editingId) ? "Yeni Satış Faturası" : "Satış Faturası Taslağını Düzenle")
    {
        _sales = sales; _products = new LocalProductService(database); _inventory = new LocalInventoryService(database); _resolver = new LocalBarcodeResolver(database);
        _company = company; _branch = branch; _warehouse = warehouse; _account = account; _warehouseName = warehouse; _editingId = editingId;

        var routedType = new ElectronicDocumentRoutingService(database).RouteOutgoingInvoice(company, account);
        Fields.Children.Add(new TextBlock
        {
            Text = routedType == ElectronicDocumentType.EInvoice ? "Bu cari E-Fatura mükellefi olarak tanımlı.\nBelge: E-Fatura" : "Belge: E-Arşiv Fatura",
            Foreground = Ui.Brush("R3.Info.Brush"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        });

        var barcode = Field("Barkod / Ürün kodu (Enter ile ara)", new TextBox { MaxLength = 80 });
        var productList = database.Query("SELECT id AS Id, code || ' — ' || name AS Display FROM products WHERE company_id=$c AND is_active=1 AND product_type<>'Service' ORDER BY code", ("$c", company));
        var productPicker = Field("Ürün *", new ComboBox { ItemsSource = productList.DefaultView, DisplayMemberPath = "Display", SelectedValuePath = "Id", IsTextSearchEnabled = true, IsEditable = true });
        _resolved = new TextBlock { Foreground = Ui.Brush("R3.Info.Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 2), FontWeight = FontWeights.SemiBold };
        Fields.Children.Add(_resolved);

        _product = new TextBox(); _variant = new TextBox(); _unit = new TextBox();
        _qty = Field("Miktar *", new TextBox { Text = "1", Tag = "Numeric" });
        _price = Field("Birim fiyat *", new TextBox { Text = "0", Tag = "Numeric" });
        _vat = Field("KDV % (üründen otomatik dolar, gerekirse değiştirin)", new TextBox { Text = "20", Tag = "Numeric" });
        // Varsayılan fiyat: every product-selection path (barcode, picker, code/name lookup) ends by setting
        // _product, so one listener covers them all. Deferred so _factor/_unit set in the same statement are in
        // place: list prices are per base unit, a koli barcode multiplies by its factor. The user can still
        // overwrite the price; the source is shown under the fields.
        var priceSource = new TextBlock { FontSize = Ui.Font.Grid, Foreground = Ui.Brush("R3.Text.Secondary.Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        Fields.Children.Add(priceSource);
        var prices = new LocalPriceService(database);
        _product.TextChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _discountRate = 0; priceSource.Text = "";
            if (string.IsNullOrWhiteSpace(_product.Text)) return;
            var resolved = prices.ResolveSalesPrice(company, _product.Text.Trim(), account, DateTime.Today);
            if (resolved == null) { priceSource.Text = "Fiyat listelerinde bu ürün için geçerli satış fiyatı yok; fiyatı elle girin."; return; }
            _price.Text = (resolved.UnitPrice * _factor).ToString("0.####", CultureInfo.GetCultureInfo("tr-TR"));
            _discountRate = resolved.DiscountRate;
            priceSource.Text = $"Fiyat: {resolved.Source}{(_factor != 1 ? $" × çarpan {_factor:0.##}" : "")}{(resolved.DiscountRate > 0 ? $" • iskonto %{resolved.DiscountRate:0.##}" : "")}";
        });

        async Task ShowStockAsync()
        {
            if (string.IsNullOrWhiteSpace(_product.Text)) return;
            var balance = await _inventory.GetBalanceAsync(_warehouseName, _product.Text, string.IsNullOrWhiteSpace(_variant.Text) ? null : _variant.Text);
            var stockLine = $"Depoda: {balance?.QuantityOnHand ?? 0:N2}  •  Rezerve: {balance?.QuantityReserved ?? 0:N2}  •  Kullanılabilir: {balance?.QuantityAvailable ?? 0:N2}";
            _resolved.Text = string.IsNullOrWhiteSpace(_resolved.Text) ? stockLine : $"{_resolved.Text}\n{stockLine}";
        }

        async Task ResolveProductInputAsync()
        {
            var input = productPicker.Text.Trim();
            if (input.Length == 0) return;
            try
            {
                var result = _resolver.ResolveForInventory(input, company);
                _product.Text = result.ProductId; _variant.Text = result.VariantId ?? ""; _unit.Text = result.UnitId; _factor = result.QuantityFactor;
                productPicker.SelectedValue = result.ProductId;
                var detail = _products.GetDetail(result.ProductId, company);
                if (detail != null) _vat.Text = detail.Product.VatRate.ToString("0.##", CultureInfo.GetCultureInfo("tr-TR"));
                _resolved.Text = $"{result.ProductCode} — {result.ProductName} • {result.UnitName} • Çarpan: {_factor:N2}";
                await ShowStockAsync();
            }
            catch (KeyNotFoundException)
            {
                var matches = database.Query("SELECT id,code,name,base_unit_id FROM products WHERE company_id=$c AND is_active=1 AND (code=$q COLLATE NOCASE OR name=$q COLLATE NOCASE) LIMIT 1", ("$c", company), ("$q", input));
                if (matches.Rows.Count == 0) throw new KeyNotFoundException($"Stok kodu, barkod veya ürün adı bulunamadı: {input}");
                var row = matches.Rows[0];
                _product.Text = row[0].ToString()!; _variant.Text = ""; _unit.Text = row[3].ToString()!; _factor = 1;
                productPicker.SelectedValue = _product.Text;
                var detail = _products.GetDetail(_product.Text, company);
                if (detail != null) _vat.Text = detail.Product.VatRate.ToString("0.##", CultureInfo.GetCultureInfo("tr-TR"));
                _resolved.Text = $"{row[1]} — {row[2]} • ürün adı/kodu ile bulundu";
                await ShowStockAsync();
            }
        }

        productPicker.SelectionChanged += async (_, _) =>
        {
            if (productPicker.SelectedValue == null) return;
            _product.Text = productPicker.SelectedValue.ToString()!; _variant.Text = ""; _factor = 1;
            var detail = _products.GetDetail(_product.Text, company);
            if (detail != null) { _unit.Text = detail.Product.UnitId; _vat.Text = detail.Product.VatRate.ToString("0.##", CultureInfo.GetCultureInfo("tr-TR")); }
            _resolved.Text = productPicker.Text; await ShowStockAsync();
        };
        barcode.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            try
            {
                var result = _resolver.ResolveForInventory(barcode.Text, company);
                _product.Text = result.ProductId; _variant.Text = result.VariantId ?? ""; _unit.Text = result.UnitId; _factor = result.QuantityFactor;
                productPicker.SelectedValue = result.ProductId;
                var detail = _products.GetDetail(result.ProductId, company);
                if (detail != null) _vat.Text = detail.Product.VatRate.ToString("0.##", CultureInfo.GetCultureInfo("tr-TR"));
                _resolved.Text = $"{result.ProductCode} — {result.ProductName} • {result.UnitName} • Çarpan: {_factor:N2}";
                await ShowStockAsync();
                _qty.Focus(); _qty.SelectAll();
                e.Handled = true;
            }
            catch (Exception ex) { _resolved.Text = ex.Message; }
        };

        // Hızlı fatura girişi: barkod/ürün -> miktar -> fiyat -> KDV.
        // Enter her alanda bir sonraki anlamlı alana geçer; KDV'de Enter satırı tamamlar.
        productPicker.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            try { if (productPicker.SelectedValue == null || !string.Equals(productPicker.Text.Trim(), _resolved.Text.Split('—')[0].Trim(), StringComparison.OrdinalIgnoreCase)) await ResolveProductInputAsync(); _qty.Focus(); _qty.SelectAll(); }
            catch (Exception ex) { _resolved.Text = ex.Message; }
            e.Handled = true;
        };
        _qty.KeyDown += (_, e) => { if (e.Key != Key.Enter) return; _price.Focus(); _price.SelectAll(); e.Handled = true; };
        _price.KeyDown += (_, e) => { if (e.Key != Key.Enter) return; _vat.Focus(); _vat.SelectAll(); e.Handled = true; };
        _vat.KeyDown += (_, e) => { if (e.Key != Key.Enter) return; try { Save(); DialogResult = true; } catch (Exception ex) { _resolved.Text = ex.Message; } e.Handled = true; };

        Finish(Save);
        Loaded += (_, _) => barcode.Focus();
        if (!string.IsNullOrWhiteSpace(editingId))
            Loaded += (_, _) =>
            {
                var row = database.Query("SELECT product_id,variant_id,unit_id,quantity,quantity_factor,unit_price,discount_rate,vat_rate FROM sales_document_lines WHERE sales_document_id=$id ORDER BY line_no LIMIT 1", ("$id", editingId)).Rows.Cast<DataRow>().FirstOrDefault();
                if (row == null) return;
                _product.Text = row["product_id"].ToString()!; _variant.Text = row["variant_id"] is DBNull ? "" : row["variant_id"].ToString()!; _unit.Text = row["unit_id"].ToString()!;
                _factor = Convert.ToDecimal(row["quantity_factor"]); _qty.Text = Convert.ToDecimal(row["quantity"]).ToString("0.####", CultureInfo.GetCultureInfo("tr-TR")); _price.Text = Convert.ToDecimal(row["unit_price"]).ToString("0.####", CultureInfo.GetCultureInfo("tr-TR")); _discountRate = Convert.ToDecimal(row["discount_rate"]); _vat.Text = Convert.ToDecimal(row["vat_rate"]).ToString("0.##", CultureInfo.GetCultureInfo("tr-TR"));
                productPicker.SelectedValue = _product.Text; _resolved.Text = "Taslak satır yüklendi — düzenleyip Kaydet ile güncelleyebilirsiniz.";
            };
    }
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_product.Text)) throw new ArgumentException("Ürün seçin veya barkod okutun.");
        if (!decimal.TryParse(_qty.Text, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out var q) || q <= 0) throw new ArgumentException("Geçerli miktar girin.");
        if (!decimal.TryParse(_price.Text, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out var p) || p < 0) throw new ArgumentException("Geçerli fiyat girin.");
        if (!decimal.TryParse(_vat.Text, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out var vat) || vat is < 0 or > 100) throw new ArgumentException("Geçerli KDV oranı girin.");
        DocumentId = _sales.CreateDraft(new(_editingId ?? "", _company, _branch, _warehouse, _account, DateTime.Today, "",
            [new(_product.Text.Trim(), string.IsNullOrWhiteSpace(_variant.Text) ? null : _variant.Text.Trim(), _unit.Text.Trim(), null, q, _factor, p, _discountRate, vat)]));
    }
}
