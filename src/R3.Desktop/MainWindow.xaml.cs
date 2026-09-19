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
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using R3.Desktop.Logging;
using R3.Desktop.ContextActions;
using R3.Desktop.ViewModels;
using R3.Desktop.Views;
using R3.Infrastructure;
using WpfUi = Wpf.Ui.Controls;

namespace R3.Desktop;

public partial class MainWindow : WpfUi.FluentWindow
{
    private StoreDatabase? _db;
    private LocalMasterDataService? _masterData;
    private LocalProductService? _products;
    private LocalInventoryService? _inventory;
    private readonly WorkspaceContext _workspaceContext = new();
    private readonly Stack<TabItem> _closedTabs = new();
    private StartupSession? _startupSession;
    private bool _loadingWorkspace;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private readonly ILogger<MainWindow> _logger = DesktopLogging.CreateLogger<MainWindow>();
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
        try { _db = new StoreDatabase(_startupSession.DatabasePath); _masterData = new LocalMasterDataService(_db); _products = new LocalProductService(_db); _inventory = new LocalInventoryService(_db); ErpGridContext.Configure(_db, _startupSession.UserName); DatabaseStatus.Text = $"● {_startupSession.UserName} • {_startupSession.BranchName} • SQLite 3 hazır"; DatabaseStatus.ToolTip = _db.Path; }
        catch (Exception ex) { _logger.LogError(ex, "Local SQLite database open failed. Path={DatabasePath}", _startupSession.DatabasePath); DatabaseStatus.Text = "Veritabanı açılamadı"; MessageBox.Show(this, ex.Message, "Veritabanı hatası"); }
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
    private void CompanyContextCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_loadingWorkspace) return; if (CompanyContextCombo.SelectedItem is DataRowView row && Guid.TryParse(row["Id"].ToString(), out var id)) _workspaceContext.SetCompany(id, row["Name"].ToString() ?? ""); ReloadBranches(); UpdateContextStatus(); }
    private void BranchContextCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_loadingWorkspace) return; if (BranchContextCombo.SelectedItem is DataRowView row && Guid.TryParse(row["Id"].ToString(), out var id)) _workspaceContext.SetBranch(id, row["Name"].ToString() ?? ""); ReloadWarehouses(); UpdateContextStatus(); }
    private void WarehouseContextCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_loadingWorkspace) return; if (WarehouseContextCombo.SelectedItem is DataRowView row && Guid.TryParse(row["Id"].ToString(), out var id)) _workspaceContext.SetWarehouse(id, row["Name"].ToString() ?? ""); UpdateContextStatus(); }
    private void UpdateContextStatus() => ContextStatus.Text = $"Firma: {_workspaceContext.CompanyName}   Şube: {_workspaceContext.BranchName}   Depo: {_workspaceContext.WarehouseName}";
    private async Task CheckServerAsync()
    {
        var baseUrl = Environment.GetEnvironmentVariable("R3_SERVER_URL") ?? "http://localhost:5189/";
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(3) };
        var client = new R3ApiClient(http);
        DatabaseStatus.Text = await client.IsHealthyAsync() ? "● API sunucusu bağlı • SQLite 3 mağaza prototipi hazır" : "● API sunucusuna ulaşılamıyor • SQLite 3 mağaza prototipi hazır";
    }
    private static TextBlock MenuIcon(string glyph, int size = 18, Brush? color = null) => new() { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size, Foreground = color ?? new SolidColorBrush(Color.FromRgb(39, 116, 165)), VerticalAlignment = VerticalAlignment.Center };
    private static FrameworkElement FluentIcon(WpfUi.SymbolRegular symbol, int size = 20, Brush? color = null)
    {
        var stroke = color ?? new SolidColorBrush(Color.FromRgb(39, 116, 165));
        var name = symbol.ToString();
        var data = name switch
        {
            var n when n.Contains("BuildingShop") => "M3,10 L5,4 L19,4 L21,10 M4,10 L4,21 L20,21 L20,10 M8,21 L8,15 L13,15 L13,21 M5,10 C5,12 8,12 8,10 C8,12 12,12 12,10 C12,12 16,12 16,10 C16,12 19,12 19,10",
            var n when n.Contains("People") || n.Contains("Person") || n.Contains("Contact") => "M9,11 A3,3 0 1 1 9,5 A3,3 0 1 1 9,11 M3.5,20 C3.8,15.5 6,13.5 9,13.5 C12,13.5 14.2,15.5 14.5,20 M17,11 A2.4,2.4 0 1 1 17,6.2 A2.4,2.4 0 1 1 17,11 M15.7,14 C19.2,13.4 21,15.8 21,19",
            var n when n.Contains("Box") => "M4,7 L12,3 L20,7 L20,17 L12,21 L4,17 Z M4,7 L12,12 L20,7 M12,12 L12,21 M8,5 L16,9",
            var n when n.Contains("Cart") => "M3,4 L5,4 L7,15.5 L18.5,15.5 L21,7 L6,7 M8,19.5 A1.5,1.5 0 1 1 8,19.4 M18,19.5 A1.5,1.5 0 1 1 18,19.4",
            var n when n.Contains("Receipt") => "M6,3 L18,3 L18,21 L15,19 L12,21 L9,19 L6,21 Z M9,8 L15,8 M9,12 L15,12 M9,16 L13,16",
            var n when n.Contains("Wallet") || n.Contains("Money") || n.Contains("Bank") => "M3,7 C3,5.3 4.3,4 6,4 L18,4 C19.7,4 21,5.3 21,7 L21,18 C21,19.1 20.1,20 19,20 L5,20 C3.9,20 3,19.1 3,18 Z M15,10 L21,10 L21,15 L15,15 C13.6,15 13,14 13,12.5 C13,11 13.6,10 15,10 Z M16,12.5 L17,12.5",
            var n when n.Contains("Calculator") => "M6,3 L18,3 L18,21 L6,21 Z M8.5,6 L15.5,6 L15.5,9 L8.5,9 Z M9,13 L9,13 M12,13 L12,13 M15,13 L15,13 M9,17 L9,17 M12,17 L12,17 M15,17 L15,17",
            var n when n.Contains("Chart") || n.Contains("DataUsage") => "M4,20 L4,4 M4,20 L21,20 M7,17 L7,12 L10,12 L10,17 M12,17 L12,8 L15,8 L15,17 M17,17 L17,5 L20,5 L20,17",
            var n when n.Contains("Document") => "M6,3 L15,3 L19,7 L19,21 L6,21 Z M15,3 L15,7 L19,7 M9,11 L16,11 M9,15 L16,15 M9,18 L14,18",
            var n when n.Contains("Tool") || n.Contains("Settings") => "M4,18 L10,12 M14,8 L20,2 M13,5 C15,2.7 18,2 21,3 L17,7 L17,10 L14,10 L10,14 M3,17 L7,21 L10,18 L6,14 Z",
            var n when n.Contains("Dismiss") => "M5,5 L19,19 M19,5 L5,19",
            var n when n.Contains("Home") => "M3,11 L12,3 L21,11 M5,10 L5,21 L19,21 L19,10 M9,21 L9,15 L15,15 L15,21",
            var n when n.Contains("ArrowSwap") => "M4,8 L18,8 M15,5 L18,8 L15,11 M20,16 L6,16 M9,13 L6,16 L9,19",
            var n when n.Contains("Shield") => "M12,3 L20,6 L20,12 C20,17 16.5,20 12,22 C7.5,20 4,17 4,12 L4,6 Z M8,12 L11,15 L17,9",
            var n when n.Contains("Clipboard") => "M7,5 L5,5 L5,21 L19,21 L19,5 L17,5 M9,3 L15,3 L16,7 L8,7 Z M8,12 L16,12 M8,16 L16,16",
            var n when n.Contains("History") => "M4,6 L4,11 L9,11 M5,10 A8,8 0 1 1 6,17 M13,8 L13,13 L17,15",
            var n when n.Contains("Database") => "M4,6 C4,3 20,3 20,6 C20,9 4,9 4,6 Z M4,6 L4,18 C4,21 20,21 20,18 L20,6 M4,12 C4,15 20,15 20,12",
            var n when n.Contains("Save") => "M4,3 L18,3 L21,6 L21,21 L3,21 L3,3 Z M7,3 L7,9 L16,9 L16,3 M7,21 L7,14 L17,14 L17,21",
            var n when n.Contains("Info") => "M12,21 A9,9 0 1 1 12,3 A9,9 0 1 1 12,21 M12,10 L12,17 M12,7 L12,7",
            var n when n.Contains("Plug") => "M8,3 L8,8 M16,3 L16,8 M5,8 L19,8 L19,11 C19,15 16,18 12,18 C8,18 5,15 5,11 Z M12,18 L12,22",
            var n when n.Contains("Building") => "M5,21 L5,5 L15,3 L15,21 M15,9 L20,9 L20,21 M3,21 L22,21 M8,8 L11,8 M8,12 L11,12 M8,16 L11,16",
            var n when n.Contains("Globe") => "M12,21 A9,9 0 1 1 12,3 A9,9 0 1 1 12,21 M3,12 L21,12 M12,3 C8,7 8,17 12,21 M12,3 C16,7 16,17 12,21",
            _ => "M4,4 L20,4 L20,20 L4,20 Z M8,8 L16,8 M8,12 L16,12 M8,16 L14,16"
        };

        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data), Stroke = stroke, StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round, Fill = Brushes.Transparent
        };
        return new Viewbox { Width = size, Height = size, Child = path, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
    }
    private MenuItem TopMenu(string title, string glyph)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 2, 0) };
        var colors = new[] { "#E76F51", "#2A9D8F", "#E9C46A", "#457B9D", "#9B5DE5", "#F15BB5", "#00B4D8", "#F4A261" };
        var color = (SolidColorBrush)new BrushConverter().ConvertFromString(colors[MainMenu.Items.Count % colors.Length])!;
        var icon = MenuIcon(glyph, 15, color); panel.Children.Add(icon);
        panel.Children.Add(new TextBlock { Text = title, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        var item = new MenuItem { Header = panel, Padding = new Thickness(5, 4, 5, 4) }; MainMenu.Items.Add(item); return item;
    }
    private static MenuItem Entry(MenuItem parent, string text, string glyph, Action? action = null)
    {
        var item = new MenuItem { Header = text, Icon = MenuIcon(glyph, 15, new SolidColorBrush(Color.FromRgb(76, 142, 189))), Padding = new Thickness(8, 5, 16, 5) };
        if (action != null) item.Click += (_, _) => action();
        parent.Items.Add(item); return item;
    }
    private MenuItem TopMenu(string title, WpfUi.SymbolRegular symbol)
    {
        var panel = new StackPanel { MinWidth = 67, Margin = new Thickness(1, 0, 1, 0) };
        var palette = new[] { "#C57A2A", "#347F9F", "#6E8B52", "#B88938", "#C4584F", "#287E89", "#64708A", "#2F8E87", "#75639A", "#59636B", "#C64F4F" };
        var color = (SolidColorBrush)new BrushConverter().ConvertFromString(palette[MainMenu.Items.Count % palette.Length])!;
        var icon = FluentIcon(symbol, 29, color); icon.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(icon);
        panel.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 10, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush(Color.FromRgb(39, 50, 59)) });
        var item = new MenuItem { Header = panel, Padding = new Thickness(5, 3, 5, 3), ToolTip = title }; MainMenu.Items.Add(item); return item;
    }
    private static MenuItem Entry(MenuItem parent, string text, WpfUi.SymbolRegular symbol, Action? action = null)
    {
        var item = new MenuItem { Header = text, Icon = FluentIcon(symbol, 18), Padding = new Thickness(8, 5, 16, 5) };
        if (action != null) item.Click += (_, _) => action();
        parent.Items.Add(item); return item;
    }
    private void BuildMenu()
    {
        MainMenu.Items.Clear();
        void Planned(string title) => OpenModulePlan(title);
        void AddGroup(MenuItem parent, string title, WpfUi.SymbolRegular icon, params string[] labels)
        {
            var group = Entry(parent, title, icon);
            foreach (var label in labels) Entry(group, label, icon, () => Planned(label));
        }

        var store = TopMenu("Mağaza", WpfUi.SymbolRegular.BuildingShop24);
        Entry(store, "Giriş ekranı", WpfUi.SymbolRegular.Home24, () => Workspace.SelectedIndex = 0);
        Entry(store, "Müşteri cari / Hesap ekstresi", WpfUi.SymbolRegular.DocumentTable24, OpenLedger);
        Entry(store, "Müşteri kartları", WpfUi.SymbolRegular.PersonAccounts24, () => OpenDefinitions(false));
        AddGroup(store, "Perakende Satış", WpfUi.SymbolRegular.Cart24, "Yeni satış", "Satış geçmişi", "İleri teslim siparişleri");
        AddGroup(store, "Kasa ve Tahsilat", WpfUi.SymbolRegular.WalletCreditCard24, "Kasa işlemleri", "Taksit tahsilatı", "Tahsilat performansı");
        AddGroup(store, "Sevkiyat Takibi", WpfUi.SymbolRegular.Box24, "Bekleyen sevkiyatlar", "Gerçekleşen sevkiyatlar", "SMS sevk emirleri");

        var accounts = TopMenu("Cari", WpfUi.SymbolRegular.People24);
        Entry(accounts, "Cari Genel Bakış", WpfUi.SymbolRegular.DataUsage24, OpenAccountDashboard);
        Entry(accounts, "Cari Kartlar", WpfUi.SymbolRegular.ContactCard24, () => OpenCanonicalAccounts());
        Entry(accounts, "Müşteriler", WpfUi.SymbolRegular.PersonAccounts24, () => OpenCanonicalAccounts("Customer", "Müşteriler"));
        Entry(accounts, "Tedarikçiler", WpfUi.SymbolRegular.BuildingShop24, () => OpenCanonicalAccounts("Supplier", "Tedarikçiler"));
        accounts.Items.Add(new Separator());
        Entry(accounts, "Cari Hareketler", WpfUi.SymbolRegular.ArrowSwap24, () => OpenAccountTransactions());
        Entry(accounts, "Cari Ekstre", WpfUi.SymbolRegular.DocumentTable24, () => OpenAccountStatement());
        Entry(accounts, "Risk & Kredi", WpfUi.SymbolRegular.ShieldCheckmark24, OpenCreditRisk);
        var accountDefinitions = Entry(accounts, "Cari Tanımları", WpfUi.SymbolRegular.PeopleTeam24);
        Entry(accountDefinitions, "Cari Grupları", WpfUi.SymbolRegular.PeopleTeam24, () => OpenMasterCrud("account_groups", "Cari Grubu Tanımları"));
        Entry(accountDefinitions, "Bölgeler", WpfUi.SymbolRegular.Map24, () => OpenMasterCrud("regions", "Bölge Tanımları"));
        Entry(accountDefinitions, "Sevk Bölgeleri", WpfUi.SymbolRegular.VehicleTruckProfile24, () => OpenMasterCrud("delivery_regions", "Sevk Bölgesi Tanımları"));
        Entry(accountDefinitions, "Fiyat Listeleri", WpfUi.SymbolRegular.MoneyCalculator24, () => OpenMasterCrud("price_lists", "Fiyat Listesi Tanımları"));

        var stock = TopMenu("Stok", WpfUi.SymbolRegular.Box24);
        Entry(stock, "Ürünler", WpfUi.SymbolRegular.Box24, OpenProductList);
        Entry(stock, "Stok Durumu", WpfUi.SymbolRegular.DataUsage24, OpenInventoryBalance);
        Entry(stock, "Stok Hareketleri", WpfUi.SymbolRegular.ArrowSwap24, OpenInventoryMovements);
        Entry(stock, "Stok Giriş", WpfUi.SymbolRegular.BoxArrowUp24, () => OpenInventoryOperation("Stok Giriş"));
        Entry(stock, "Stok Çıkış", WpfUi.SymbolRegular.BoxArrowLeft24, () => OpenInventoryOperation("Stok Çıkış"));
        Entry(stock, "Depo Transfer", WpfUi.SymbolRegular.BoxMultiple24, () => OpenInventoryOperation("Depo Transfer"));
        Entry(stock, "Sayım", WpfUi.SymbolRegular.Clipboard24, () => OpenInventoryOperation("Sayım"));
        AddGroup(stock, "Stok Tanımları", WpfUi.SymbolRegular.Settings24, "Markalar", "Kategoriler", "Birimler", "Varyant ve Yapı", "Fiyat ve Barkod");

        var purchasing = TopMenu("Satınalma", WpfUi.SymbolRegular.Cart24);
        AddGroup(purchasing, "Satınalma Yönetimi", WpfUi.SymbolRegular.Cart24, "Tedarikçiler", "Satınalma siparişleri", "Alış faturaları", "Satınalma iadeleri");

        var sales = TopMenu("Satış", WpfUi.SymbolRegular.ReceiptMoney24);
        Entry(sales, "Yeni Satış Faturası", WpfUi.SymbolRegular.ReceiptAdd24, OpenNewSalesInvoice);
        Entry(sales, "Satış Faturaları", WpfUi.SymbolRegular.ReceiptMoney24, OpenSalesList);
        AddGroup(sales, "Satış Yönetimi", WpfUi.SymbolRegular.Cart24, "Satış siparişleri", "İade işlemleri", "Günlük satış", "Şube satışları");

        var finance = TopMenu("Finans", WpfUi.SymbolRegular.WalletCreditCard24);
        Entry(finance, "Genel Bakış", WpfUi.SymbolRegular.Money24, () => OpenModulePlan("Finans genel bakış"));
        var cashManagement = Entry(finance, "Kasa Yönetimi", WpfUi.SymbolRegular.Wallet24);
        Entry(cashManagement, "Kasa Kartları", WpfUi.SymbolRegular.Wallet24, OpenCashAccounts);
        Entry(cashManagement, "Kasa Hareketleri", WpfUi.SymbolRegular.ArrowSwap24, () => OpenCashTransactions());
        Entry(cashManagement, "Nakit Giriş", WpfUi.SymbolRegular.ArrowDownload24, OpenCashInFromMenu);
        Entry(cashManagement, "Nakit Çıkış", WpfUi.SymbolRegular.ArrowUpload24, OpenCashOutFromMenu);
        Entry(cashManagement, "Kasalar Arası Transfer", WpfUi.SymbolRegular.ArrowSwap24, OpenCashTransferFromMenu);
        Entry(cashManagement, "Kasa Ekstresi", WpfUi.SymbolRegular.DocumentTable24, () => OpenCashStatement());
        AddGroup(finance, "Banka", WpfUi.SymbolRegular.BuildingBank24, "Banka hesapları", "Banka işlemleri");
        AddGroup(finance, "Çek / Senet", WpfUi.SymbolRegular.DocumentTable24, "Çek portföyü", "Senet portföyü");

        var accounting = TopMenu("Muhasebe", WpfUi.SymbolRegular.Calculator24);
        AddGroup(accounting, "Genel Muhasebe", WpfUi.SymbolRegular.Calculator24, "Hesap planı", "Muhasebe fişleri", "Cari muhasebe", "Mali raporlar");

        var einvoice = TopMenu("E-Belge", WpfUi.SymbolRegular.DocumentArrowRight24);
        AddGroup(einvoice, "Belge Yönetimi", WpfUi.SymbolRegular.DocumentArrowRight24, "Gelen faturalar", "Giden faturalar", "e-Arşiv", "e-İrsaliye");
        AddGroup(einvoice, "Takip", WpfUi.SymbolRegular.History24, "Gönderim kuyruğu", "Belge durumları", "Gelen kutusu", "Alias tanımları");

        var reports = TopMenu("Raporlar", WpfUi.SymbolRegular.ChartMultiple24);
        AddGroup(reports, "Yönetim Raporları", WpfUi.SymbolRegular.ChartMultiple24, "Satış raporları", "Stok raporları", "Finans raporları", "Müşteri raporları", "Cari Raporları");

        var tools = TopMenu("Araçlar", WpfUi.SymbolRegular.Toolbox24);
        Entry(tools, "Firmalar", WpfUi.SymbolRegular.Building24, () => OpenMasterCrud("companies", "Firma Tanımları"));
        Entry(tools, "Şubeler", WpfUi.SymbolRegular.BuildingMultiple24, () => OpenMasterCrud("branches", "Şube Tanımları"));
        Entry(tools, "Depolar", WpfUi.SymbolRegular.BoxMultiple24, () => OpenMasterCrud("warehouses", "Depo Tanımları"));
        AddGroup(tools, "Tanımlar", WpfUi.SymbolRegular.Settings24, "Genel ayarlar", "Numara serileri", "Para birimleri", "Vergi politikaları");
        AddGroup(tools, "Entegrasyon", WpfUi.SymbolRegular.PlugConnected24, "API bağlantıları", "E-Ticaret kanalları", "Migration geçmişi", "Senkronizasyon");
        AddGroup(tools, "İnsan Kaynakları", WpfUi.SymbolRegular.People24, "Personeller", "Departmanlar", "Pozisyonlar", "Bordro dönemleri");
        Entry(tools, "Web Merkezi (CefSharp)", WpfUi.SymbolRegular.Globe24, OpenWebCenter);
        Entry(tools, "Veritabanı bilgisi", WpfUi.SymbolRegular.Database24, () => MessageBox.Show(this, _db == null ? "Veritabanı açılamadı" : $"Provider: SQLite 3\nDurum: Hazır\nDosya: {_db.Path}\nŞema sürümü: {_db.SchemaVersion}\nSon yedek: {_db.LastBackup ?? "Yok"}", "Veritabanı"));
        Entry(tools, "Veritabanı yedeği al", WpfUi.SymbolRegular.Save24, () => Safe(() => MessageBox.Show(this, _db!.Backup(), "Yedek oluşturuldu")));
        Entry(tools, "Hakkında", WpfUi.SymbolRegular.Info24, () => MessageBox.Show(this, "R3 ERP 0.3.0\nModern ticari işletme yönetimi", "R3 ERP"));

        var close = TopMenu("Kapat", WpfUi.SymbolRegular.Dismiss24);
        Entry(close, "Programdan çık", WpfUi.SymbolRegular.Dismiss24, Close);
    }

    private void BuildLegacyMenu()
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

        var accounts = TopMenu("Cari Yönetimi", WpfUi.SymbolRegular.People24);
        Entry(accounts, "Cari Genel Bakış", WpfUi.SymbolRegular.DataUsage24, OpenAccountDashboard);
        accounts.Items.Add(new Separator());
        Entry(accounts, "Cari Kartlar", WpfUi.SymbolRegular.ContactCard24, () => OpenCanonicalAccounts());
        Entry(accounts, "Müşteriler", WpfUi.SymbolRegular.PersonAccounts24, () => OpenCanonicalAccounts("Customer", "Müşteriler"));
        Entry(accounts, "Tedarikçiler", WpfUi.SymbolRegular.BuildingShop24, () => OpenCanonicalAccounts("Supplier", "Tedarikçiler"));
        accounts.Items.Add(new Separator());
        Entry(accounts, "Cari Hareketler", WpfUi.SymbolRegular.ArrowSwap24, () => OpenAccountTransactions());
        Entry(accounts, "Cari Ekstre", WpfUi.SymbolRegular.DocumentTable24, () => OpenAccountStatement());
        Entry(accounts, "Tahsilat", WpfUi.SymbolRegular.WalletCreditCard24, () => Planned("Tahsilat"));
        Entry(accounts, "Ödeme", WpfUi.SymbolRegular.Money24, () => Planned("Ödeme"));
        Entry(accounts, "Risk & Kredi", WpfUi.SymbolRegular.ShieldCheckmark24, OpenCreditRisk);
        accounts.Items.Add(new Separator());
        Entry(accounts, "Adresler & Yetkililer", WpfUi.SymbolRegular.ContactCardGroup24, () => Planned("Adresler & Yetkililer"));
        Entry(accounts, "E-Belge Profilleri", WpfUi.SymbolRegular.DocumentTableArrowRight24, () => Planned("E-Belge Profilleri"));
        Entry(accounts, "Cari Grupları", WpfUi.SymbolRegular.PeopleTeam24, () => Planned("Cari Grupları"));
        Entry(accounts, "Bölgeler", WpfUi.SymbolRegular.Map24, () => Planned("Bölgeler"));
        Entry(accounts, "Raporlar", WpfUi.SymbolRegular.ChartMultiple24, () => Planned("Cari Raporları"));

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
        Entry(crm, "Cari Kartlar", "\uE77B", () => OpenCanonicalAccounts());
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
            var grid = new DataGrid { Style = (Style)System.Windows.Application.Current.FindResource("ProfessionalDataGridStyle"), AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, Height = 180, HeadersVisibility = DataGridHeadersVisibility.Column };
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
    private string CurrentCompanyId() => _workspaceContext.CompanyId == Guid.Empty
        ? _db!.Query("SELECT id FROM companies LIMIT 1").Rows[0][0].ToString()!
        : _workspaceContext.CompanyId.ToString()!;

    private string CurrentBranchId() => _workspaceContext.BranchId == Guid.Empty
        ? _db!.Query("SELECT id FROM branches WHERE company_id=$c ORDER BY code LIMIT 1", ("$c", CurrentCompanyId())).Rows[0][0].ToString()!
        : _workspaceContext.BranchId.ToString()!;

    private AccountServices CreateAccountServices() => new(
        new LocalAccountService(_db!), new LocalAccountAddressService(_db!), new LocalAccountContactService(_db!),
        new LocalAccountNoteService(_db!), _masterData!, _db!, CurrentCompanyId(), CurrentBranchId());

    private CashServices CreateCashServices() => new(
        new LocalCashService(_db!), new LocalAccountService(_db!), _masterData!, _db!, CurrentCompanyId(), CurrentBranchId(), _startupSession!.UserName);

    private void OpenCashAccounts() => OpenTab("Kasa Kartları", () =>
    {
        var services = CreateCashServices();
        var view = new CashAccountsView(new CashAccountsViewModel(services, DesktopLogging.CreateLogger<CashAccountsViewModel>()));
        view.StatementRequested += id => OpenCashStatement(id);
        view.TransactionsRequested += id => OpenCashTransactions(id);
        return view;
    });

    private void OpenCashTransactions(string? cashAccountId = null) => OpenTab("Kasa Hareketleri", () =>
        new CashTransactionsView(new CashTransactionsViewModel(CreateCashServices(), DesktopLogging.CreateLogger<CashTransactionsViewModel>(), cashAccountId)));

    private void OpenCashStatement(string? cashAccountId = null) => OpenTab("Kasa Ekstresi", () =>
        new CashStatementView(new CashStatementViewModel(CreateCashServices(), cashAccountId, DesktopLogging.CreateLogger<CashStatementViewModel>())));

    private void OpenCashInFromMenu() => OpenCashInOutDialog(CashDirectionKind.In);
    private void OpenCashOutFromMenu() => OpenCashInOutDialog(CashDirectionKind.Out);

    private void OpenCashInOutDialog(CashDirectionKind kind)
    {
        var services = CreateCashServices();
        var editViewModel = new CashInOutViewModel(services, kind, null, DesktopLogging.CreateLogger<CashInOutViewModel>());
        var dialog = new CashInOutDialog(editViewModel) { Owner = this };
        dialog.ShowDialog();
    }

    private void OpenCashTransferFromMenu()
    {
        var services = CreateCashServices();
        var editViewModel = new CashTransferViewModel(services, null, DesktopLogging.CreateLogger<CashTransferViewModel>());
        var dialog = new CashTransferDialog(editViewModel) { Owner = this };
        dialog.ShowDialog();
    }

    private void OpenCanonicalAccounts(string? accountType = null, string title = "Cari Kartlar")
    {
        // MVVM reference screen: View -> ViewModel -> LocalAccountService -> StoreDatabase.
        // No SQL, business rule or SQLiteConnection lives in this Window/View anymore.
        OpenTab(title, () =>
        {
            var services = CreateAccountServices();
            var viewModel = new AccountsViewModel(services, _startupSession!.UserName, DesktopLogging.CreateLogger<AccountsViewModel>(), accountType, title);
            var view = new AccountsView(viewModel);
            view.TransactionsRequested += account => OpenAccountTransactions(account.Id, account.Name);
            view.StatementRequested += account => OpenAccountStatement(account.Id);
            return view;
        });
    }

    private void OpenAccountDashboard() => OpenTab("Cari Genel Bakış", () =>
    {
        var service = new LocalAccountService(_db!);
        var summary = service.GetDashboardSummary(CurrentCompanyId());
        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock { Text = "Cari Genel Bakış", FontSize = 26, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Cari bakiyeleri AccountTransaction → AccountBalance projeksiyonundan hesaplanır.", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 4, 0, 0) });
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var cards = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
        void Card(string caption, string value, string color)
        {
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = caption, Foreground = Brushes.SlateGray, FontSize = 12 });
            body.Children.Add(new TextBlock { Text = value, FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = (Brush)new BrushConverter().ConvertFromString(color)! });
            cards.Children.Add(new Border { Child = body, Width = 205, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(15), Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(216, 228, 240)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) });
        }
        Card("Toplam Aktif Cari", summary.ActiveAccounts.ToString("N0", Turkish), "#3578B8");
        Card("Toplam Müşteri", summary.Customers.ToString("N0", Turkish), "#2A9D8F");
        Card("Toplam Tedarikçi", summary.Suppliers.ToString("N0", Turkish), "#8E6BBE");
        Card("Toplam Müşteri Alacağı", summary.CustomerReceivable.ToString("C2", Turkish), "#E76F51");
        Card("Toplam Tedarikçi Borcu", summary.SupplierPayable.ToString("C2", Turkish), "#C88A21");
        Card("Risk Limitini Aşan", summary.CreditLimitExceeded.ToString("N0", Turkish), "#C0392B");
        DockPanel.SetDock(cards, Dock.Top); root.Children.Add(cards);
        var section = new DockPanel();
        var sectionTitle = new TextBlock { Text = "Son Cari Hareketleri", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 9) };
        DockPanel.SetDock(sectionTitle, Dock.Top); section.Children.Add(sectionTitle);
        var grid = Table();
        foreach (var column in new[] { "Tarih", "CariKodu", "Cari", "IslemTipi", "BelgeNo", "Aciklama", "Borc", "Alacak", "Doviz" }) Column(grid, column, column, column is "Borc" or "Alacak" ? "N2" : null);
        grid.ItemsSource = service.RecentTransactions(CurrentCompanyId()).DefaultView; section.Children.Add(grid); root.Children.Add(section);
        return root;
    });

    private void OpenAccountTransactions(string? accountId = null, string? accountName = null) => OpenTab(accountId == null ? "Cari Hareketler" : $"Hareketler • {accountName}", () =>
        new AccountTransactionsView(new AccountLedgerViewModel(new LocalAccountService(_db!), CurrentCompanyId(), accountId, DesktopLogging.CreateLogger<AccountLedgerViewModel>())));

    private void OpenAccountStatement(string? selectedAccountId = null) => OpenTab(selectedAccountId == null ? "Cari Ekstre" : $"Cari Ekstre • {selectedAccountId[..Math.Min(8, selectedAccountId.Length)]}", () =>
        new AccountStatementView(new AccountStatementViewModel(new LocalAccountService(_db!), CurrentCompanyId(), selectedAccountId, DesktopLogging.CreateLogger<AccountStatementViewModel>())));

    private void OpenCreditRisk() => OpenTab("Risk & Kredi", () =>
        new CreditRiskView(new CreditRiskViewModel(new LocalAccountService(_db!), CurrentCompanyId(), DesktopLogging.CreateLogger<CreditRiskViewModel>())));

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
            void Refresh() { grid.ItemsSource = _products!.Search(search.Text).DefaultView; } void Edit(bool create) { var row = create ? null : grid.SelectedItem as DataRowView; if (!create && row == null) { MessageBox.Show(this, "Önce ürün seçin."); return; } var unit = _db!.Query("SELECT id FROM units WHERE is_active=1 ORDER BY code LIMIT 1").Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() ?? string.Empty; string company = _workspaceContext.CompanyId == Guid.Empty ? (_db.Query("SELECT id FROM companies WHERE is_active=1 ORDER BY code LIMIT 1").Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() ?? string.Empty) : _workspaceContext.CompanyId.ToString(); var detail = !create ? _products!.GetDetail(row!["Id"].ToString()!, company) : null; var dialog = new ProductDialog(row, unit, company, _db, detail) { Owner = this }; if (dialog.ShowDialog() == true) { try { _products!.Save(dialog.ToEditModel()); Refresh(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Ürün kaydedilemedi"); } } }
            ErpGridContext.Register(grid, "inventory.products", StandardContextActions.Products(
                () => Edit(false), () => Edit(false), OpenInventoryMovements, OpenInventoryBalance,
                () => OpenInventoryOperation("Stok Giriş"), () => OpenInventoryOperation("Stok Çıkış"),
                () => OpenInventoryOperation("Depo Transfer"), () => OpenInventoryOperation("Sayım")),
                () => { Refresh(); return Task.CompletedTask; }, "Product");
            ActionButton(bar, "+ Yeni Ürün (F2)", () => Edit(true)); ActionButton(bar, "Düzenle (F3)", () => Edit(false)); ActionButton(bar, "Yenile (F5)", Refresh); bar.Children.Add(new TextBlock { Text = "Ara: ", VerticalAlignment = VerticalAlignment.Center }); bar.Children.Add(search); search.TextChanged += (_, _) => Refresh(); root.Children.Add(grid); Refresh(); return root;
        });
    }
    private void OpenInventoryBalance() => OpenTab("Stok Durumu", () =>
    {
        var root = new DockPanel { Margin = new Thickness(18) }; var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar); var search = new TextBox { Width = 220, Padding = new Thickness(8), ToolTip = "Ürün kodu ara" }; var grid = Table(); foreach (var key in new[] { "Depo", "Urun", "Varyant", "Mevcut", "Rezerve", "Kullanilabilir", "SonHareket" }) Column(grid, key, key); async void Refresh() { try { grid.ItemsSource = (await _inventory!.SearchBalancesAsync(_workspaceContext.WarehouseId == Guid.Empty ? null : _workspaceContext.WarehouseId.ToString(), search.Text)).DefaultView; } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Stok durumu"); } } ActionButton(bar, "Yenile (F5)", Refresh); bar.Children.Add(new TextBlock { Text = "Ara: ", VerticalAlignment = VerticalAlignment.Center }); bar.Children.Add(search); search.TextChanged += (_, _) => Refresh(); root.Children.Add(grid); Refresh(); return root;
    });
    private void OpenInventoryMovements() => OpenTab("Stok Hareketleri", () => { var root = new DockPanel { Margin = new Thickness(18) }; var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar); var search = new TextBox { Width = 240, Padding = new Thickness(8) }; var grid = Table(); foreach (var key in new[] { "Tarih", "Referans", "Urun", "Varyant", "Sube", "Depo", "Tur", "Miktar", "BirimMaliyet", "Korelasyon", "Aciklama" }) Column(grid, key, key); async void Refresh() { grid.ItemsSource = (await _inventory!.SearchMovementsAsync(_workspaceContext.WarehouseId == Guid.Empty ? null : _workspaceContext.WarehouseId.ToString(), search.Text)).DefaultView; } ActionButton(bar, "Yenile", Refresh); bar.Children.Add(search); search.TextChanged += (_, _) => Refresh(); root.Children.Add(grid); Refresh(); return root; });
    private void OpenInventoryOperation(string kind) { if (_inventory == null) return; string company = _workspaceContext.CompanyId == Guid.Empty ? (_db!.Query("SELECT id FROM companies WHERE is_active=1 ORDER BY code LIMIT 1").Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() ?? string.Empty) : _workspaceContext.CompanyId.ToString(); string branch = _workspaceContext.BranchId == Guid.Empty ? (_db!.Query("SELECT id FROM branches WHERE company_id=$c AND is_active=1 ORDER BY code LIMIT 1", ("$c", (object)company)).Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() ?? string.Empty) : _workspaceContext.BranchId.ToString(); string warehouse = _workspaceContext.WarehouseId == Guid.Empty ? (_db!.Query("SELECT id FROM warehouses WHERE branch_id=$b AND is_active=1 ORDER BY code LIMIT 1", ("$b", (object)branch)).Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() ?? string.Empty) : _workspaceContext.WarehouseId.ToString(); var dialog = new InventoryOperationDialog(kind, _inventory, _db!, company, branch, warehouse) { Owner = this }; if (dialog.ShowDialog() == true) MessageBox.Show(this, "İşlem kaydedildi.", kind); }
    private void OpenWebCenter() => OpenTab("Web Merkezi", () => new EmbeddedBrowserView());
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
            close.Click += (_, _) => CloseTab(tab); header.Children.Add(close); tab.Header = header;
            Workspace.Items.Add(tab); Workspace.SelectedItem = tab;
        });
    }

    private void Workspace_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var tab = FindAncestor<TabItem>(e.OriginalSource as DependencyObject);
        if (tab != null) Workspace.SelectedItem = tab;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source != null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void CloseCurrentTab_Click(object sender, RoutedEventArgs e)
    {
        if (Workspace.SelectedItem is TabItem { Tag: string } selected) CloseTab(selected);
    }

    private void CloseTab(TabItem tab)
    {
        if (tab.Tag is not string || !Workspace.Items.Contains(tab)) return;
        _closedTabs.Push(tab); Workspace.Items.Remove(tab);
        if (Workspace.Items.Count > 0 && Workspace.SelectedIndex < 0) Workspace.SelectedIndex = 0;
    }

    private void CloseTabsToLeft_Click(object sender, RoutedEventArgs e)
    {
        if (Workspace.SelectedItem is not TabItem selected) return; var selectedIndex = Workspace.Items.IndexOf(selected);
        foreach (var tab in Workspace.Items.OfType<TabItem>().Where(tab => tab.Tag is string && Workspace.Items.IndexOf(tab) < selectedIndex).ToList()) CloseTab(tab);
    }

    private void CloseTabsToRight_Click(object sender, RoutedEventArgs e)
    {
        if (Workspace.SelectedItem is not TabItem selected) return; var selectedIndex = Workspace.Items.IndexOf(selected);
        foreach (var tab in Workspace.Items.OfType<TabItem>().Where(tab => tab.Tag is string && Workspace.Items.IndexOf(tab) > selectedIndex).ToList()) CloseTab(tab);
    }

    private void CloseOtherTabs_Click(object sender, RoutedEventArgs e)
    {
        var selected = Workspace.SelectedItem as TabItem;
        foreach (var tab in Workspace.Items.OfType<TabItem>().Where(tab => tab.Tag is string && tab != selected).ToList()) CloseTab(tab);
    }

    private void CloseAllTabs_Click(object sender, RoutedEventArgs e)
    {
        foreach (var tab in Workspace.Items.OfType<TabItem>().Where(tab => tab.Tag is string).ToList()) CloseTab(tab);
        Workspace.SelectedIndex = 0;
    }

    private void ReopenClosedTab_Click(object sender, RoutedEventArgs e)
    {
        if (_closedTabs.Count == 0) return; var tab = _closedTabs.Pop(); Workspace.Items.Add(tab); Workspace.SelectedItem = tab;
    }

    private void CopyTabTitle_Click(object sender, RoutedEventArgs e)
    {
        if (Workspace.SelectedItem is TabItem { Tag: string title }) Clipboard.SetText(title);
    }

    private void GoHome_Click(object sender, RoutedEventArgs e) => Workspace.SelectedIndex = 0;

    private static Button ActionButton(Panel panel, string text, Action action)
    {
        var b = new Button
        {
            Content = text, Height = 29, MinWidth = 82, Padding = new Thickness(11, 4, 11, 4),
            Margin = new Thickness(0, 0, 7, 0), Background = new SolidColorBrush(Color.FromRgb(242, 244, 246)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(190, 199, 207)), Foreground = new SolidColorBrush(Color.FromRgb(42, 55, 65)),
            FontSize = 10.5, FontWeight = FontWeights.Medium
        };
        b.Click += (_, _) => action(); panel.Children.Add(b); return b;
    }
    private static DataGrid Table()
    {
        var grid = new DataGrid
        {
            Style = (Style)System.Windows.Application.Current.FindResource("ProfessionalDataGridStyle"),
            IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false,
            SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column
        };
        return grid;
    }
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
