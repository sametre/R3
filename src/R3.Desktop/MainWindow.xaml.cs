#nullable enable
#pragma warning disable CS8600,CS8604,CS8620
using System.Data;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using R3.Desktop.ViewModels;
using R3.Desktop.Views;
using R3.Infrastructure;

namespace R3.Desktop;

public partial class MainWindow : Window
{
    private StoreDatabase? _db;
    private LocalMasterDataService? _masterData;
    private LocalProductService? _products;
    private LocalInventoryService? _inventory;
    private readonly WorkspaceContext _workspaceContext = new();
    private StartupSession? _startupSession;
    private bool _loadingWorkspace;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    public MainWindow()
    {
        InitializeComponent();
        var startup = new StartupLoginWindow();
        if (startup.ShowDialog() != true || startup.Session is null)
        {
            System.Windows.Application.Current.Shutdown();
            return;
        }
        _startupSession = startup.Session;
        BuildMenu();
        _clock.Tick += (_, _) => DateText.Text = DateTime.Now.ToString("dd MMMM yyyy • HH:mm", Turkish);
        DateText.Text = DateTime.Now.ToString("dd MMMM yyyy • HH:mm", Turkish);
        _clock.Start(); Closed += (_, _) => _clock.Stop();
        try { _db = new StoreDatabase(_startupSession.DatabasePath); _masterData = new LocalMasterDataService(_db); _products = new LocalProductService(_db); _inventory = new LocalInventoryService(_db); DatabaseStatus.Text = $"● {_startupSession.UserName} • {_startupSession.BranchName} • SQLite 3 hazır"; DatabaseStatus.ToolTip = _db.Path; }
        catch (Exception ex) { DatabaseStatus.Text = "Veritabanı açılamadı"; MessageBox.Show(this, ex.Message, "Veritabanı hatası"); }
        _ = CheckServerAsync();
        LoadWorkspaceContext();
        ApplyStartupContext();
    }
    private void LoadWorkspaceContext()
    {
        if (_db == null) return;
        _loadingWorkspace = true;
        CompanyContextCombo.ItemsSource = _db.Query("SELECT id AS Id, code AS Code, name AS Name FROM companies WHERE is_active=1 ORDER BY code").DefaultView;
        if (CompanyContextCombo.Items.Count > 0) CompanyContextCombo.SelectedIndex = 0;
        _loadingWorkspace = false;
        ReloadBranches();
    }
    private void ApplyStartupContext()
    {
        if (_startupSession == null) return;
        _loadingWorkspace = true;
        CompanyContextCombo.SelectedValue = _startupSession.CompanyId.ToString();
        _loadingWorkspace = false;
        if (CompanyContextCombo.SelectedItem is DataRowView company) _workspaceContext.SetCompany(_startupSession.CompanyId, company["Name"].ToString() ?? _startupSession.CompanyName);
        ReloadBranches();
        _loadingWorkspace = true;
        BranchContextCombo.SelectedValue = _startupSession.BranchId.ToString();
        _loadingWorkspace = false;
        if (BranchContextCombo.SelectedItem is DataRowView branch) _workspaceContext.SetBranch(_startupSession.BranchId, branch["Name"].ToString() ?? _startupSession.BranchName);
        ReloadWarehouses();
        if (WarehouseContextCombo.SelectedItem is DataRowView warehouse && Guid.TryParse(warehouse["Id"].ToString(), out var warehouseId)) _workspaceContext.SetWarehouse(warehouseId, warehouse["Name"].ToString() ?? "");
    }
    private void ReloadBranches()
    {
        if (_db == null) return; _loadingWorkspace = true; BranchContextCombo.ItemsSource = CompanyContextCombo.SelectedValue is string company && Guid.TryParse(company, out _) ? _db.Query("SELECT id AS Id, code AS Code, name AS Name FROM branches WHERE company_id=$id AND is_active=1 ORDER BY code", ("$id", company)).DefaultView : null; BranchContextCombo.SelectedIndex = BranchContextCombo.Items.Count > 0 ? 0 : -1; _loadingWorkspace = false; ReloadWarehouses();
    }
    private void ReloadWarehouses()
    {
        if (_db == null) return; _loadingWorkspace = true; WarehouseContextCombo.ItemsSource = BranchContextCombo.SelectedValue is string branch && Guid.TryParse(branch, out _) ? _db.Query("SELECT id AS Id, code AS Code, name AS Name FROM warehouses WHERE branch_id=$id AND is_active=1 ORDER BY code", ("$id", branch)).DefaultView : null; WarehouseContextCombo.SelectedIndex = WarehouseContextCombo.Items.Count > 0 ? 0 : -1; _loadingWorkspace = false;
    }
    private void CompanyContextCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_loadingWorkspace) return; if (CompanyContextCombo.SelectedItem is DataRowView row && Guid.TryParse(row["Id"].ToString(), out var id)) _workspaceContext.SetCompany(id, row["Name"].ToString() ?? ""); ReloadBranches(); }
    private void BranchContextCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_loadingWorkspace) return; if (BranchContextCombo.SelectedItem is DataRowView row && Guid.TryParse(row["Id"].ToString(), out var id)) _workspaceContext.SetBranch(id, row["Name"].ToString() ?? ""); ReloadWarehouses(); }
    private void WarehouseContextCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_loadingWorkspace) return; if (WarehouseContextCombo.SelectedItem is DataRowView row && Guid.TryParse(row["Id"].ToString(), out var id)) _workspaceContext.SetWarehouse(id, row["Name"].ToString() ?? ""); }
    private async Task CheckServerAsync()
    {
        var baseUrl = Environment.GetEnvironmentVariable("R3_SERVER_URL") ?? "http://localhost:5189/";
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(3) };
        var client = new R3ApiClient(http);
        DatabaseStatus.Text = await client.IsHealthyAsync() ? "● API sunucusu bağlı • SQLite 3 mağaza prototipi hazır" : "● API sunucusuna ulaşılamıyor • SQLite 3 mağaza prototipi hazır";
    }
    private static TextBlock MenuIcon(string glyph, int size = 18, Brush? color = null) => new() { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size, Foreground = color ?? new SolidColorBrush(Color.FromRgb(39, 116, 165)), VerticalAlignment = VerticalAlignment.Center };
    private MenuItem TopMenu(string title, string glyph)
    {
        var panel = new StackPanel { MinWidth = 84, Margin = new Thickness(3, 1, 3, 1) };
        var colors = new[] { "#E76F51", "#2A9D8F", "#E9C46A", "#457B9D", "#9B5DE5", "#F15BB5", "#00B4D8", "#F4A261" };
        var color = (SolidColorBrush)new BrushConverter().ConvertFromString(colors[MainMenu.Items.Count % colors.Length])!;
        var icon = MenuIcon(glyph, 20, color); icon.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(icon);
        panel.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.SemiBold });
        var item = new MenuItem { Header = panel }; MainMenu.Items.Add(item); return item;
    }
    private static MenuItem Entry(MenuItem parent, string text, string glyph, Action? action = null)
    {
        var item = new MenuItem { Header = text, Icon = MenuIcon(glyph, 15, new SolidColorBrush(Color.FromRgb(76, 142, 189))), Padding = new Thickness(8, 5, 16, 5) };
        if (action != null) item.Click += (_, _) => action();
        parent.Items.Add(item); return item;
    }
    private void BuildMenu()
    {
        var home = TopMenu("Ana Sayfa", "\uE80F");
        Entry(home, "Giriş paneli", "\uE80F", () => Workspace.SelectedIndex = 0);

        var store = TopMenu("Mağaza", "\uE719");
        Entry(store, "Müşteri cari / Hesap ekstresi", "\uE8D4", OpenLedger);
        Entry(store, "Müşteri kartları", "\uE77B", () => OpenDefinitions(false));
        Entry(store, "Ürün sorgulama", "\uE721", () => Planned("Ürün sorgulama"));
        Entry(store, "Müşteri / kefil sorgulama", "\uE77B", () => Planned("Müşteri / kefil sorgulama"));
        Entry(store, "Kefil tanımları", "\uE716", () => Planned("Kefil tanımları"));
        AddGroup(store, "Perakende Satış", "\uE8CC", "Yeni satış", "Satış geçmişi", "İleri teslim siparişleri");
        AddGroup(store, "Toptan Satış", "\uE8CC", "Siparişler", "Faturalar");
        AddGroup(store, "Arşiv", "\uE8B7", "Arşivdeki belgeler", "Arşivden geri alma");
        AddGroup(store, "Kasa ve Tahsilat", "\uE825", "Kasa işlemleri", "Taksit tahsilatı", "Tahsilat performansı");
        AddGroup(store, "Kredi ve Takip", "\uE8D4", "Kredi onay talepleri", "Borç takip", "Geciken taksitler");
        AddGroup(store, "Müşteri Hizmetleri", "\uE716", "Araştırma talepleri", "Müşteri memnuniyeti", "Avukatlık işlemleri");
        AddGroup(store, "Sevkiyat Takibi", "\uE8A5", "Bekleyen sevkiyatlar", "SMS sevk emirleri", "Gerçekleşen sevkiyatlar", "Ürün bazlı gerçekleşen sevkiyatlar");

        var sales = TopMenu("Satış", "\uE8CC");
        Entry(sales, "Satış Faturaları", "\uE8CC", OpenSalesList);
        Entry(sales, "Yeni Satış Faturası", "\uE8CC", OpenNewSalesInvoice);
        AddGroup(sales, "Satış Yönetimi", "\uE8CC", "Satış siparişleri", "Satış faturaları", "İade işlemleri");
        AddGroup(sales, "Satış Raporları", "\uE7F4", "Günlük satış", "Şube satışları", "Ürün bazlı satışlar");

        var stock = TopMenu("Stok", "\uE7B8");
        Entry(stock, "Ürünler", "\uE77B", OpenProductList);
        Entry(stock, "Markalar", "\uE77B", () => OpenMasterCrud("brands", "Marka Tanımları"));
        Entry(stock, "Kategoriler", "\uE77B", () => OpenMasterCrud("categories", "Kategori Tanımları"));
        Entry(stock, "Birimler", "\uE77B", () => OpenMasterCrud("units", "Birim Tanımları"));
        AddGroup(stock, "Ürün Yönetimi", "\uE77B", "Stok tipleri", "Stok grup kodları", "Ürün özellikleri");
        AddGroup(stock, "Varyant ve Yapı", "\uE8A5", "Renk / beden", "Varyant boyutları", "Kartela", "Takım / set", "Stok parçası");
        AddGroup(stock, "Stok İşlemleri", "\uE7B8", "Stok hareketleri", "Stok ekstresi", "Sayım", "Pasif stoklar", "Stok etiketi");
        Entry(stock, "Stok Durumu", "\uE7B8", OpenInventoryBalance);
        Entry(stock, "Stok Hareketleri", "\uE8A5", OpenInventoryMovements);
        Entry(stock, "Stok Giriş", "\uE8A5", () => OpenInventoryOperation("Stok Giriş"));
        Entry(stock, "Stok Çıkış", "\uE8A5", () => OpenInventoryOperation("Stok Çıkış"));
        Entry(stock, "Depo Transfer", "\uE8A5", () => OpenInventoryOperation("Depo Transfer"));
        Entry(stock, "Sayım", "\uE8A5", () => OpenInventoryOperation("Sayım"));
        AddGroup(stock, "Fiyat ve Barkod", "\uE8CB", "Fiyat yönetimi", "Barkod yönetimi");
        AddGroup(stock, "Planlama", "\uE9D9", "Kritik stok seviyeleri", "MİP", "Ürün talep ve takip merkezi");
        AddGroup(stock, "Özel Operasyonlar", "\uE8A5", "Konsinye işlemleri", "Emanetteki ürünler", "Satınalma iadeleri");

        var logistics = TopMenu("Lojistik", "\uE7C1");
        AddGroup(logistics, "Depo", "\uE7B8", "Depolar", "Depolararası transfer", "Transfer planı");
        AddGroup(logistics, "Sevkiyat", "\uE8A5", "Bekleyen sevkiyatlar", "Gerçekleşen sevkiyatlar", "SMS sevk emirleri");
        AddGroup(logistics, "Dağıtım Tanımları", "\uE707", "Sevkiyat bölgesi", "Route tanımları", "Kargo kodları", "Sürücü kodları", "Sevk adresi eşleştirme");
        Entry(logistics, "El terminali", "\uE8EA", () => Planned("El terminali"));

        var purchasing = TopMenu("Satınalma", "\uE7BF");
        AddGroup(purchasing, "Satınalma Yönetimi", "\uE7BF", "Tedarikçiler", "Satınalma siparişleri", "Alış faturaları", "Satınalma iadeleri");

        var finance = TopMenu("Finans", "\uE825");
        AddGroup(finance, "Kasa", "\uE825", "Kasa işlemleri", "Kasa tanımları", "Tahsilat");
        AddGroup(finance, "Banka", "\uE8D4", "Banka hesapları", "Banka işlemleri");
        AddGroup(finance, "Çek / Senet", "\uE8A5", "Çek portföyü", "Senet portföyü");

        var accounting = TopMenu("Muhasebe", "\uE8D4");
        AddGroup(accounting, "Muhasebe", "\uE8D4", "Hesap planı", "Muhasebe fişleri", "Cari muhasebe");

        var organization = TopMenu("Organizasyon", "\uE716");
        Entry(organization, "Firmalar", "\uE716", () => OpenMasterCrud("companies", "Firma Tanımları"));
        Entry(organization, "Şubeler", "\uE716", () => OpenMasterCrud("branches", "Şube Tanımları"));
        Entry(organization, "Depolar", "\uE7B8", () => OpenMasterCrud("warehouses", "Depo Tanımları"));
        AddGroup(organization, "Şirket ve Şube", "\uE716", "Organizasyon politikaları");
        AddGroup(organization, "Çalışma Parametreleri", "\uE713", "Stok politikaları", "Belge numara şablonları", "Para birimleri", "Vergi politikaları");

        var accounts = TopMenu("Cari", "\uE77B");
        AddGroup(accounts, "Cari Kartlar", "\uE77B", "Müşteri hesapları", "Tedarikçi hesapları", "Personel hesapları", "Cari adresler");
        AddGroup(accounts, "Cari Hareketler", "\uE8D4", "Cari borç / alacak", "Açılış bakiyeleri", "Cari ekstre", "Mutabakat");
        AddGroup(accounts, "e-Fatura Profilleri", "\uE8A5", "Fatura alias", "İrsaliye alias", "Senaryo ve durum");

        var pricing = TopMenu("Fiyat", "\uE8CB");
        AddGroup(pricing, "Fiyat Listeleri", "\uE8CB", "Fiyat listeleri", "Ürün fiyatları", "Kampanya fiyatları", "Fiyat kuralları");
        AddGroup(pricing, "İskonto", "\uE8A5", "İskonto grupları", "Müşteri iskonto kuralları", "Toplu fiyat güncelleme");

        var ecommerce = TopMenu("E-Ticaret", "\uE8B7");
        AddGroup(ecommerce, "Kanallar", "\uE8B7", "Trendyol", "Hepsiburada", "N11", "Web sitesi");
        AddGroup(ecommerce, "Eşleştirme", "\uE8A5", "Entegrasyon ürünleri", "Varyant eşleştirme", "Harici SKU eşleştirme", "Stok senkronizasyonu");
        AddGroup(ecommerce, "Senkronizasyon", "\uE895", "Bekleyen işler", "Senkronizasyon geçmişi", "Hata kayıtları");

        var einvoice = TopMenu("E-Fatura", "\uE8A5");
        AddGroup(einvoice, "Belge Yönetimi", "\uE8A5", "Gelen faturalar", "Giden faturalar", "e-Arşiv", "e-İrsaliye");
        AddGroup(einvoice, "Takip", "\uE895", "Gönderim kuyruğu", "Belge durumları", "Gelen kutusu", "Alias tanımları");

        var hr = TopMenu("İK", "\uE77B");
        AddGroup(hr, "İnsan Kaynakları", "\uE77B", "Personeller", "Departmanlar", "Pozisyonlar", "İzinler");
        AddGroup(hr, "Bordro", "\uE8D4", "Bordro dönemleri", "Bordro çalıştırma", "Kazançlar", "Kesintiler", "SGK ve vergi");

        var integrations = TopMenu("Entegrasyon", "\uE8B7");
        AddGroup(integrations, "Dış Sistemler", "\uE8B7", "API bağlantıları", "Web servisleri", "Gelen kutusu", "Gönderim kuyruğu");
        AddGroup(integrations, "Migration", "\uE7F4", "ASB tablo eşleştirme", "Legacy kayıt arama", "Migration geçmişi", "Tekrarlanabilir aktarım");

        var crm = TopMenu("CRM", "\uE716");
        Entry(crm, "Cari Kartlar", "\uE77B", OpenCanonicalAccounts);
        Entry(crm, "Müşteri 360°", "\uE77B", () => OpenDefinitions(false));
        AddGroup(crm, "Müşteri İlişkileri", "\uE716", "Notlar", "Müşteri logları", "Müşteri memnuniyeti");
        AddGroup(crm, "Analiz", "\uE7F4", "Tahsilat performansı", "Müşteri hareketleri");

        var reports = TopMenu("Raporlar", "\uE7F4");
        AddGroup(reports, "Yönetim Raporları", "\uE7F4", "Satış raporları", "Stok raporları", "Finans raporları", "Müşteri raporları");

        var definitions = TopMenu("Tanımlar", "\uE713");
        Entry(definitions, "Mağaza tanımları", "\uE80F", () => OpenDefinitions(true));
        Entry(definitions, "Müşteri tanımları", "\uE716", () => OpenDefinitions(false));
        AddGroup(definitions, "Stok Tanımları", "\uE7B8", "Stok kartı", "Depolar", "Kritik stok seviyeleri", "Stok tipleri", "Stok grup kodları", "İskonto grupları", "Stok birimleri", "KDV oranları", "Tevkifat kodları", "Stok hareket kodları", "Kategori tanımları");
        AddGroup(definitions, "Renk / Beden", "\uE8A5", "Renk tanımları", "Beden tanımları", "Varyant boyut kodları");
        AddGroup(definitions, "Lojistik Tanımları", "\uE7C1", "Sevkiyat bölge kodları", "Kargo kodları", "Sürücü kodları", "Route tanımları", "Tedarikçi / şube teslim tablos");
        AddGroup(definitions, "Toplu İşlemler", "\uE8B7", "Toplu stok kartı oluştur", "Barkodsuz ürünlere özel barkod oluştur");

        var tools = TopMenu("Sistem", "\uE713");
        AddGroup(tools, "Kullanıcı ve Yetki", "\uE77B", "Kullanıcı yönetimi", "Yetki ve roller", "İzin matrisi", "Şifre değiştir");
        AddGroup(tools, "İzleme", "\uE7F4", "Audit logları", "Arka plan işleri", "Hata kayıtları", "Sistem olayları");
        AddGroup(tools, "Ayarlar", "\uE713", "Genel ayarlar", "Numara serileri", "Bildirim ayarları", "Yedekleme");
        Entry(tools, "Veritabanı bilgisi", "\uE7F4", () => MessageBox.Show(this, _db == null ? "Veritabanı açılamadı" : $"Provider: SQLite 3\nDurum: Hazır\nDosya: {_db.Path}\nŞema sürümü: {_db.SchemaVersion}\nSon yedek: {_db.LastBackup ?? "Yok"}", "Veritabanı"));
        Entry(tools, "Veritabanı yedeği al", "\uE74C", () => Safe(() => MessageBox.Show(this, _db!.Backup(), "Yedek oluşturuldu")));
        Entry(tools, "Hakkında", "\uE946", () => MessageBox.Show(this, "R3 ERP 0.2.0\nMağaza yönetimi • SQLite 3", "R3 ERP"));

        var close = TopMenu("Kapat", "\uE8BB"); Entry(close, "Programdan çık", "\uE8BB", Close);

        void AddGroup(MenuItem parent, string title, string glyph, params string[] labels)
        {
            var group = Entry(parent, title, glyph);
            foreach (var label in labels) Entry(group, label, glyph, () => Planned(label));
        }
        void Planned(string title) => OpenModulePlan(title);
    }
    private void OpenModulePlan(string title)
    {
        var descriptions = new Dictionary<string, (string Purpose, string[] Entities)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Stok kartları"] = ("Ürün, varyant, birim ve barkodların merkezi kataloğu.", new[] { "products", "product_variants", "product_barcodes", "units" }),
            ["Stok hareketleri"] = ("Her giriş, çıkış ve transferi tek hareket kaynağında izler.", new[] { "inventory_transactions", "inventory_balances" }),
            ["Fiyat yönetimi"] = ("Liste, ürün fiyatı ve kural tabanlı fiyatlandırma.", new[] { "price_lists", "product_prices", "price_rules" }),
            ["Depolar"] = ("Şube ve depo organizasyonunu yönetir.", new[] { "companies", "branches", "warehouses" }),
            ["Satış siparişleri"] = ("Siparişten irsaliyeye ve faturaya belge ilişkisi kurar.", new[] { "sales_documents", "sales_document_lines", "document_relations" }),
            ["Alış faturaları"] = ("Tedarikçi alış belgelerini ve stok etkisini yönetir.", new[] { "purchase_documents", "purchase_document_lines" }),
            ["Banka hesapları"] = ("Kasa ve banka finans hareketlerinin merkezi ekranı.", new[] { "cash_accounts", "bank_accounts", "financial_transactions" }),
            ["Çek portföyü"] = ("Çek ve senet yaşam döngüsünü takip eder.", new[] { "cheques", "promissory_notes" }),
            ["Personeller"] = ("İnsan kaynakları temel kayıtları.", new[] { "employees", "departments" }),
            ["Bordro dönemleri"] = ("Bordroyu bağımsız bir modül olarak çalıştırır.", new[] { "payroll_periods", "payroll_runs", "payroll_taxes" }),
            ["Gelen faturalar"] = ("e-Fatura ve e-Arşiv belge durumlarını takip eder.", new[] { "einvoice_documents", "einvoice_events" }),
            ["Entegrasyon ürünleri"] = ("Kanal bazlı ürün ve varyant eşleştirmesi.", new[] { "integration_channels", "integration_product_mappings" }),
            ["ASB tablo eşleştirme"] = ("Eski ASB kayıtlarını legacy_source ve legacy_id ile izler.", new[] { "legacy_source", "legacy_id", "migration_runs" })
        };
        if (!descriptions.TryGetValue(title, out var detail))
            detail = ($"{title} için tasarım ekranı. İş kuralları domain servislerinde uygulanacaktır.", new[] { "domain entity", "application command", "audit log" });
        OpenTab(title, () =>
        {
            var panel = new StackPanel { Margin = new Thickness(28) };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = detail.Purpose, Margin = new Thickness(0, 8, 0, 22), Foreground = Brushes.SlateGray, FontSize = 14 });
            var state = new Border { Background = new SolidColorBrush(Color.FromRgb(239, 247, 252)), BorderBrush = new SolidColorBrush(Color.FromRgb(184, 216, 234)), BorderThickness = new Thickness(1), Padding = new Thickness(16), CornerRadius = new CornerRadius(6) };
            state.Child = new TextBlock { Text = "Taslak ekran • Menü bağlantısı hazır • SQLite 3 altyapısı aktif", Foreground = new SolidColorBrush(Color.FromRgb(26, 91, 125)), FontWeight = FontWeights.SemiBold };
            panel.Children.Add(state);
            panel.Children.Add(new TextBlock { Text = "Canonical model varlıkları", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 26, 0, 10) });
            var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, Height = 180, HeadersVisibility = DataGridHeadersVisibility.Column };
            grid.Columns.Add(new DataGridTextColumn { Header = "R3 varlığı", Binding = new Binding("Entity"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Durum", Binding = new Binding("Status"), Width = new DataGridLength(180) });
            grid.ItemsSource = detail.Entities.Select(entity => new { Entity = entity, Status = "Tasarıma alındı" }); panel.Children.Add(grid);
            panel.Children.Add(new TextBlock { Text = "Sonraki adım: bu menü komutu için server/application handler ve ekran veri kaynağı bağlanacak.", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 18, 0, 0) });
            return panel;
        });
    }
    private void OpenMasterCrud(string kind, string title)
    {
        OpenTab(title, () =>
        {
            var root = new DockPanel { Margin = new Thickness(18) }; var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
            var search = new TextBox { Width = 220, Padding = new Thickness(8), ToolTip = "Kod veya ad ara" }; var grid = Table();
            foreach (DataColumn c in _masterData!.List(kind).Columns) if (c.ColumnName != "Id") Column(grid, c.ColumnName, c.ColumnName);
            void Refresh() { grid.ItemsSource = _masterData.List(kind, search.Text).DefaultView; }
            void Edit(bool create)
            {
                var row = create ? null : grid.SelectedItem as DataRowView; if (!create && row == null) { MessageBox.Show(this, "Önce bir kayıt seçin."); return; }
                var dialog = new MasterRecordDialog(title, kind, row) { Owner = this };
                if (dialog.ShowDialog() == true) { try { _masterData.Save(kind, row?["Id"].ToString(), new MasterRecord(dialog.Code, dialog.NameValue, dialog.ParentId, dialog.Extra, dialog.ActiveValue)); Refresh(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Kayıt kaydedilemedi"); } }
            }
            ActionButton(bar, "+ Yeni (F2)", () => Edit(true)); ActionButton(bar, "Düzenle (F3)", () => Edit(false)); ActionButton(bar, "Yenile (F5)", Refresh); bar.Children.Add(new TextBlock { Text = "Ara: ", VerticalAlignment = VerticalAlignment.Center }); bar.Children.Add(search); search.TextChanged += (_, _) => Refresh(); grid.MouseDoubleClick += (_, _) => Edit(false); root.Children.Add(grid); Refresh(); return root;
        });
    }
    private void OpenCanonicalAccounts()
    {
        // MVVM reference screen (Phase 2 POC): View -> ViewModel -> LocalAccountService -> StoreDatabase.
        // No SQL, business rule or SQLiteConnection lives in this Window/View anymore.
        OpenTab("Cari Kartlar", () =>
        {
            var company = _workspaceContext.CompanyId == Guid.Empty
                ? _db!.Query("SELECT id FROM companies LIMIT 1").Rows[0][0].ToString()!
                : _workspaceContext.CompanyId.ToString()!;
            var viewModel = new AccountsViewModel(new LocalAccountService(_db!), company);
            return new AccountsView(viewModel);
        });
    }
    private void OpenSalesList()
    {
        OpenTab("Satış Faturaları", () => { var root=new DockPanel{Margin=new Thickness(18)};var bar=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(bar,Dock.Top);root.Children.Add(bar);var search=new TextBox{Width=240,Padding=new Thickness(8)};var grid=Table();foreach(var k in new[]{"FaturaNo","Tarih","CariKod","Cari","Sube","Depo","AraToplam","Iskonto","KDV","GenelToplam","Durum"})Column(grid,k,k);string company=_workspaceContext.CompanyId==Guid.Empty?_db!.Query("SELECT id FROM companies LIMIT 1").Rows[0][0].ToString()!: _workspaceContext.CompanyId.ToString();var service=new LocalSalesService(_db!);void Refresh(){grid.ItemsSource=service.Search(company,search.Text).DefaultView;}ActionButton(bar,"Yeni Fatura",OpenNewSalesInvoice);ActionButton(bar,"Yenile",Refresh);bar.Children.Add(search);search.TextChanged+=(_,_)=>Refresh();root.Children.Add(grid);Refresh();return root;});
    }
    private void OpenNewSalesInvoice()
    {
        if(_db==null)return;string company=_workspaceContext.CompanyId==Guid.Empty?_db.Query("SELECT id FROM companies LIMIT 1").Rows[0][0].ToString()!: _workspaceContext.CompanyId.ToString();string branch=_workspaceContext.BranchId==Guid.Empty?_db.Query("SELECT id FROM branches WHERE company_id=$c LIMIT 1",("$c",(object)company)).Rows[0][0].ToString()!: _workspaceContext.BranchId.ToString();string warehouse=_workspaceContext.WarehouseId==Guid.Empty?_db.Query("SELECT id FROM warehouses WHERE branch_id=$b LIMIT 1",("$b",(object)branch)).Rows[0][0].ToString()!: _workspaceContext.WarehouseId.ToString();var account=_db.Query("SELECT id FROM accounts WHERE company_id=$c AND is_active=1 AND account_type IN ('Customer','CustomerAndSupplier') LIMIT 1",("$c",(object)company));if(account.Rows.Count==0){MessageBox.Show(this,"Önce aktif bir müşteri cari hesabı oluşturun.","Satış faturası");return;}var dialog=new SalesInvoiceDialog(new LocalSalesService(_db),company,branch,warehouse,account.Rows[0][0].ToString()!){Owner=this};if(dialog.ShowDialog()==true)MessageBox.Show(this,$"Taslak oluşturuldu.\nBelge ID: {dialog.DocumentId}","Satış faturası");
    }
    private void OpenProductList()
    {
        OpenTab("Ürün Listesi", () =>
        {
            var root = new DockPanel { Margin = new Thickness(18) }; var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar); var search = new TextBox { Width = 220, Padding = new Thickness(8) }; var grid = Table(); foreach (var key in new[] { "Kod", "Ad", "Marka", "Kategori", "Birim", "KDV", "Varyant", "Barkod", "Aktif" }) Column(grid, key, key);
            void Refresh() { grid.ItemsSource = _products!.Search(search.Text).DefaultView; } void Edit(bool create) { var row = create ? null : grid.SelectedItem as DataRowView; if (!create && row == null) { MessageBox.Show(this, "Önce ürün seçin."); return; } var unit = _db!.Query("SELECT id FROM units WHERE is_active=1 ORDER BY code LIMIT 1").Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() ?? string.Empty; string company = _workspaceContext.CompanyId == Guid.Empty ? (_db.Query("SELECT id FROM companies WHERE is_active=1 ORDER BY code LIMIT 1").Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() ?? string.Empty) : _workspaceContext.CompanyId.ToString(); var detail = !create ? _products!.GetDetail(row!["Id"].ToString()!, company) : null; var dialog = new ProductDialog(row, unit, company, detail) { Owner = this }; if (dialog.ShowDialog() == true) { try { _products!.Save(dialog.ToEditModel()); Refresh(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Ürün kaydedilemedi"); } } }
            ActionButton(bar, "+ Yeni Ürün (F2)", () => Edit(true)); ActionButton(bar, "Düzenle (F3)", () => Edit(false)); ActionButton(bar, "Yenile (F5)", Refresh); bar.Children.Add(new TextBlock { Text = "Ara: ", VerticalAlignment = VerticalAlignment.Center }); bar.Children.Add(search); search.TextChanged += (_, _) => Refresh(); grid.MouseDoubleClick += (_, _) => Edit(false); root.Children.Add(grid); Refresh(); return root;
        });
    }
    private void OpenInventoryBalance() => OpenTab("Stok Durumu", () =>
    {
        var root = new DockPanel { Margin = new Thickness(18) }; var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar); var search = new TextBox { Width = 220, Padding = new Thickness(8), ToolTip = "Ürün kodu ara" }; var grid = Table(); foreach (var key in new[] { "Depo", "Urun", "Varyant", "Mevcut", "Rezerve", "Kullanilabilir", "SonHareket" }) Column(grid, key, key); async void Refresh() { try { grid.ItemsSource = (await _inventory!.SearchBalancesAsync(_workspaceContext.WarehouseId == Guid.Empty ? null : _workspaceContext.WarehouseId.ToString(), search.Text)).DefaultView; } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Stok durumu"); } } ActionButton(bar, "Yenile (F5)", Refresh); bar.Children.Add(new TextBlock { Text = "Ara: ", VerticalAlignment = VerticalAlignment.Center }); bar.Children.Add(search); search.TextChanged += (_, _) => Refresh(); root.Children.Add(grid); Refresh(); return root;
    });
    private void OpenInventoryMovements() => OpenTab("Stok Hareketleri", () => { var root = new DockPanel { Margin = new Thickness(18) }; var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar); var search = new TextBox { Width = 240, Padding = new Thickness(8) }; var grid = Table(); foreach (var key in new[] { "Tarih", "Referans", "Urun", "Varyant", "Sube", "Depo", "Tur", "Miktar", "BirimMaliyet", "Korelasyon", "Aciklama" }) Column(grid, key, key); async void Refresh() { grid.ItemsSource = (await _inventory!.SearchMovementsAsync(_workspaceContext.WarehouseId == Guid.Empty ? null : _workspaceContext.WarehouseId.ToString(), search.Text)).DefaultView; } ActionButton(bar, "Yenile", Refresh); bar.Children.Add(search); search.TextChanged += (_, _) => Refresh(); root.Children.Add(grid); Refresh(); return root; });
    private void OpenInventoryOperation(string kind) { if (_inventory == null) return; string company = _workspaceContext.CompanyId == Guid.Empty ? (_db!.Query("SELECT id FROM companies WHERE is_active=1 ORDER BY code LIMIT 1").Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() ?? string.Empty) : _workspaceContext.CompanyId.ToString(); string branch = _workspaceContext.BranchId == Guid.Empty ? (_db!.Query("SELECT id FROM branches WHERE company_id=$c AND is_active=1 ORDER BY code LIMIT 1", ("$c", (object)company)).Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() ?? string.Empty) : _workspaceContext.BranchId.ToString(); string warehouse = _workspaceContext.WarehouseId == Guid.Empty ? (_db!.Query("SELECT id FROM warehouses WHERE branch_id=$b AND is_active=1 ORDER BY code LIMIT 1", ("$b", (object)branch)).Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() ?? string.Empty) : _workspaceContext.WarehouseId.ToString(); var dialog = new InventoryOperationDialog(kind, _inventory, _db!, company, branch, warehouse) { Owner = this }; if (dialog.ShowDialog() == true) MessageBox.Show(this, "İşlem kaydedildi.", kind); }
    private void Safe(Action action)
    {
        try { if (_db == null) throw new InvalidOperationException("Veritabanı kullanılamıyor."); action(); }
        catch (SqliteException ex) { MessageBox.Show(this, ex.SqliteErrorCode == 19 ? "Kayıt kaydedilemedi. Kod benzersiz olmalı; müşteri ve mağaza seçimi geçerli olmalıdır." : ex.Message, "Veritabanı"); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "İşlem tamamlanamadı"); }
    }
    private void OpenTab(string title, Func<UIElement> content)
    {
        Safe(() =>
        {
            foreach (TabItem existing in Workspace.Items) if (existing.Tag as string == title) { Workspace.SelectedItem = existing; return; }
            var tab = new TabItem { Tag = title, Content = content() };
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) });
            var close = new Button { Content = "×", Padding = new Thickness(5, 0, 5, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            close.Click += (_, _) => Workspace.Items.Remove(tab); header.Children.Add(close); tab.Header = header;
            Workspace.Items.Add(tab); Workspace.SelectedItem = tab;
        });
    }
    private static Button ActionButton(Panel panel, string text, Action action)
    {
        var b = new Button { Content = text, Padding = new Thickness(14, 8, 14, 0), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += (_, _) => action(); panel.Children.Add(b); return b;
    }
    private static DataGrid Table() => new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, BorderBrush = Brushes.LightGray, RowHeight = 34, AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(245, 248, 251)), ColumnHeaderHeight = 36 };
    private static void Column(DataGrid grid, string title, string path, string? format = null) => grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path) { StringFormat = format, ConverterCulture = Turkish }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
    private void OpenDefinitions(bool stores) => OpenTab(stores ? "Mağaza tanımları" : "Müşteri kartları", () =>
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        var grid = Table(); foreach (var key in new[] { "Kod", "Ad", "Telefon", "Adres" }) Column(grid, key, key);
        var search = new TextBox { Width = 220, Padding = new Thickness(8), ToolTip = "Kod, ad veya telefon ara" };
        DataTable data = _db!.List(stores);
        void Refresh() { data = _db.List(stores); Filter(); }
        void Filter() { var term = search.Text.Trim(); grid.ItemsSource = data.AsEnumerable().Where(r => new[] { "Kod", "Ad", "Telefon" }.Any(k => Turkish.CompareInfo.IndexOf(r[k].ToString()!, term, CompareOptions.IgnoreCase) >= 0)).AsDataView(); }
        void Edit(bool isNew)
        {
            var row = isNew ? null : grid.SelectedItem as DataRowView;
            if (!isNew && row == null) { MessageBox.Show(this, "Düzenlemek için bir kayıt seçin."); return; }
            var dialog = new RecordDialog(stores ? "Mağaza kartı" : "Müşteri kartı", row) { Owner = this };
            dialog.SaveRecord = () => _db.Save(stores, row == null ? null : Convert.ToInt64(row["Id"]), dialog.Code, dialog.RecordName, dialog.Phone, dialog.Address);
            if (dialog.ShowDialog() == true) Refresh();
        }
        ActionButton(bar, "+ Yeni kayıt", () => Safe(() => Edit(true)));
        ActionButton(bar, "Düzenle", () => Safe(() => Edit(false)));
        ActionButton(bar, "Yenile", () => Safe(Refresh));
        ActionButton(bar, "CSV dışa aktar", () => Safe(() => Export(grid)));
        bar.Children.Add(new TextBlock { Text = "Ara: ", VerticalAlignment = VerticalAlignment.Center }); bar.Children.Add(search);
        search.TextChanged += (_, _) => Filter(); grid.MouseDoubleClick += (_, _) => Safe(() => Edit(false));
        root.Children.Add(grid); Refresh(); return root;
    });
    private void OpenLedger() => OpenTab("Müşteri cari", () =>
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        var customers = new ComboBox { Width = 280, DisplayMemberPath = "Ad", SelectedValuePath = "Id", Margin = new Thickness(8, 0, 16, 0), ToolTip = "Müşteri seçin" };
        bar.Children.Add(new TextBlock { Text = "Müşteri", VerticalAlignment = VerticalAlignment.Center }); bar.Children.Add(customers);
        var grid = Table(); foreach (var key in new[] { "No", "Tarih", "Mağaza", "İşlem", "Açıklama" }) Column(grid, key, key);
        foreach (var key in new[] { "Borç", "Alacak", "Bakiye" }) Column(grid, key + " (₺)", key, "N2");
        var summary = new TextBlock { Padding = new Thickness(14), Background = new SolidColorBrush(Color.FromRgb(234, 242, 248)), FontWeight = FontWeights.SemiBold, Text = "Ekstre için müşteri seçin." };
        DockPanel.SetDock(summary, Dock.Bottom); root.Children.Add(summary);
        void Refresh()
        {
            if (customers.SelectedValue == null) { grid.ItemsSource = null; summary.Text = "Ekstre için müşteri seçin."; return; }
            var data = _db!.Ledger(Convert.ToInt64(customers.SelectedValue));
            foreach (var key in new[] { "Borç", "Alacak", "Bakiye" }) data.Columns.Add(key, typeof(decimal));
            decimal debt = 0, credit = 0;
            foreach (DataRow row in data.Rows) { var d = Convert.ToInt64(row["BorçKuruş"]) / 100m; var c = Convert.ToInt64(row["AlacakKuruş"]) / 100m; debt += d; credit += c; row["Borç"] = d; row["Alacak"] = c; row["Bakiye"] = debt - credit; }
            grid.ItemsSource = data.DefaultView;
            summary.Text = string.Format(Turkish, "{0} hareket     •     Toplam borç: {1:N2} ₺     •     Tahsilat: {2:N2} ₺     •     Bakiye: {3:N2} ₺", data.Rows.Count, debt, credit, debt - credit);
        }
        void ReloadCustomers() { var selected = customers.SelectedValue; customers.ItemsSource = _db!.List(false).DefaultView; customers.SelectedValue = selected; Refresh(); }
        ActionButton(bar, "+ Cari hareket", () => Safe(() =>
        {
            if (customers.SelectedValue == null) throw new ArgumentException("Önce müşteri seçin.");
            var stores = _db!.List(true); if (stores.Rows.Count == 0) throw new ArgumentException("Önce Tanımlar > Mağaza tanımları ekranından mağaza ekleyin.");
            var dialog = new MovementDialog(_db, Convert.ToInt64(customers.SelectedValue), stores) { Owner = this };
            if (dialog.ShowDialog() == true) Refresh();
        }));
        ActionButton(bar, "Yenile", () => Safe(ReloadCustomers)); ActionButton(bar, "CSV dışa aktar", () => Safe(() => Export(grid)));
        customers.SelectionChanged += (_, _) => Safe(Refresh); root.Children.Add(grid); ReloadCustomers(); return root;
    });
    private void Export(DataGrid grid)
    {
        if (grid.ItemsSource == null) throw new ArgumentException("Önce bir liste görüntüleyin.");
        var dialog = new SaveFileDialog { Filter = "CSV dosyası|*.csv", FileName = "R3-liste.csv" };
        if (dialog.ShowDialog(this) != true) return;
        static string Cell(string s) { if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s; return "\"" + s.Replace("\"", "\"\"") + "\""; }
        var columns = grid.Columns.Cast<DataGridTextColumn>().ToArray();
        var lines = new List<string> { string.Join(";", columns.Select(c => Cell(c.Header.ToString()!))) };
        foreach (DataRowView row in grid.ItemsSource) lines.Add(string.Join(";", columns.Select(c => Cell(row[((Binding)c.Binding).Path.Path]?.ToString() ?? ""))));
        File.WriteAllLines(dialog.FileName, lines, Encoding.UTF8);
    }
}
