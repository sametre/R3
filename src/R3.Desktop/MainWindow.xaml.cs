#nullable enable
#pragma warning disable CS8600,CS8604,CS8620
using System.Data;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
using Ribbon = Fluent;
using AvalonDock.Layout;

namespace R3.Desktop;

public partial class MainWindow : WpfUi.FluentWindow
{
    private StoreDatabase? _db;
    private LocalMasterDataService? _masterData;
    private LocalProductService? _products;
    private LocalBankService? _banks;
    private LocalUserAdminService? _users;
    private IReadOnlySet<string>? _allowedBranchIds, _allowedWarehouseIds;
    private R3.Application.Security.IPermissionService? _permissions;
    private LocalChequeService? _cheques;
    private LocalInventoryService? _inventory;
    private readonly WorkspaceContext _workspaceContext = new();
    private readonly Stack<LayoutDocument> _closedTabs = new();
    private StartupSession? _startupSession;
    private bool _loadingWorkspace;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private readonly ILogger<MainWindow> _logger = DesktopLogging.CreateLogger<MainWindow>();
    private Popup? _aiPopup;
    private StackPanel? _aiMessages;
    private TextBox? _aiInput;
    private bool _spaceHeld;
    private bool _aiSending;
    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewKeyUp += (_, e) => { if (e.Key == System.Windows.Input.Key.Space) _spaceHeld = false; };
        var startup = new StartupLoginWindow();
        if (startup.ShowDialog() != true || startup.Session is null)
        {
            System.Windows.Application.Current.Shutdown();
            return;
        }
        _startupSession = startup.Session;
        // Keep one reliable, compact navigation surface. Fluent.Ribbon remains
        // available for a future shell pass but is intentionally collapsed here
        // so it cannot compete with the visible classic menu or clip its content.
        HomeDocument.IsActive = true;
        HomeDocument.IsSelected = true;
        _clock.Tick += (_, _) => DateText.Text = DateTime.Now.ToString("dd MMMM yyyy • HH:mm", Turkish);
        DateText.Text = DateTime.Now.ToString("dd MMMM yyyy • HH:mm", Turkish);
        _clock.Start(); Closed += (_, _) => _clock.Stop();
        try { _db = new StoreDatabase(_startupSession.DatabasePath); _masterData = new LocalMasterDataService(_db); _products = new LocalProductService(_db); _inventory = new LocalInventoryService(_db); _banks = new LocalBankService(_db); _cheques = new LocalChequeService(_db); ErpGridContext.Configure(_db, _startupSession.PermissionUserName); _users = new LocalUserAdminService(_db); _allowedBranchIds = _users.AllowedBranchIds(_startupSession.PermissionUserName); _allowedWarehouseIds = _users.AllowedWarehouseIds(_startupSession.PermissionUserName); _permissions = new LocalPermissionService(_db, _startupSession.PermissionUserName); DatabaseStatus.Text = $"● {_startupSession.UserName} • {_startupSession.RoleName} • {_startupSession.BranchName} • SQLite 3 hazır"; DatabaseStatus.ToolTip = _db.Path; }
        catch (Exception ex) { _logger.LogError(ex, "Local SQLite database open failed. Path={DatabasePath}", _startupSession.DatabasePath); DatabaseStatus.Text = "Veritabanı açılamadı"; MessageBox.Show(this, ex.Message, "Veritabanı hatası"); }
        BuildVisibleMenu();
        _ = CheckServerAsync();
        LoadWorkspaceContext();
        ApplyStartupContext();
    }
    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Space) { _spaceHeld = true; return; }
        if (_spaceHeld && e.Key == System.Windows.Input.Key.Q)
        {
            ToggleAiAssistant(); e.Handled = true; return;
        }
        var control = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
        var shift = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);
        if (control && e.Key == System.Windows.Input.Key.W)
        {
            if (shift) CloseAllTabs_Click(this, new RoutedEventArgs()); else CloseCurrentTab_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (control && shift && e.Key == System.Windows.Input.Key.T) { ReopenClosedTab_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (control && e.Key == System.Windows.Input.Key.Tab && DocumentsPane.Children.Count > 0)
        {
            var docs = DocumentsPane.Children.ToList(); var current = docs.FindIndex(d => d.IsActive); if (current < 0) current = 0;
            var direction = shift ? -1 : 1; docs[(current + direction + docs.Count) % docs.Count].IsActive = true; e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Home && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)) { HomeDocument.IsActive = true; e.Handled = true; }
    }

    private void ToggleAiAssistant()
    {
        if (_aiPopup is { IsOpen: true }) { _aiPopup.IsOpen = false; return; }
        _aiMessages = new StackPanel { Margin = new Thickness(14, 12, 14, 8) };
        _aiInput = new TextBox { MinHeight = 38, MaxHeight = 90, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(10, 8, 10, 8), FontSize = 12, ToolTip = "Örn. R3 carisinin ekstresini getir" };
        _aiInput.KeyDown += async (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter || System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) return;
            e.Handled = true; await SendAiQuestionAsync();
        };
        var send = new Button { Content = "Gönder  Enter", Height = 38, MinWidth = 100, Margin = new Thickness(8, 0, 0, 0), Background = new SolidColorBrush(Color.FromRgb(22, 124, 130)), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold };
        send.Click += async (_, _) => await SendAiQuestionAsync();
        var composer = new Grid { Margin = new Thickness(12, 8, 12, 12) };
        composer.ColumnDefinitions.Add(new ColumnDefinition()); composer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        composer.Children.Add(_aiInput); Grid.SetColumn(send, 1); composer.Children.Add(send);
        var scroll = new ScrollViewer { Content = _aiMessages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var header = new DockPanel { Margin = new Thickness(14, 12, 12, 8), LastChildFill = false };
        var heading = new StackPanel(); heading.Children.Add(new TextBlock { Text = "AR3 AI Asistanı", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(52, 66, 74)) }); heading.Children.Add(new TextBlock { Text = "Ekstre, stok ve işletme raporlarını anlayarak getirir.", FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(112, 124, 132)), Margin = new Thickness(0, 2, 0, 0) });
        header.Children.Add(heading);
        var close = new Button { Content = "×", Width = 28, Height = 28, FontSize = 17, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = new SolidColorBrush(Color.FromRgb(82, 96, 106)), ToolTip = "AI panelini kapat" }; close.Click += (_, _) => _aiPopup!.IsOpen = false; DockPanel.SetDock(close, Dock.Right); header.Children.Insert(0, close);
        var panel = new DockPanel(); DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header); DockPanel.SetDock(composer, Dock.Bottom); panel.Children.Add(composer); panel.Children.Add(scroll);
        var border = new Border { Width = 560, Height = 640, Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)), BorderBrush = new SolidColorBrush(Color.FromRgb(190, 201, 207)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 6, Opacity = .25 }, Child = panel };
        _aiPopup = new Popup { PlacementTarget = this, Placement = PlacementMode.Center, AllowsTransparency = true, StaysOpen = false, Child = border };
        _aiPopup.IsOpen = true;
        AddAiMessage("Merhaba. AR3 verileriniz üzerinde güvenli ve salt-okunur raporlar hazırlayabilirim.\n\nÖrnek: ‘R3 carisinin ekstresini getir’ veya ‘stok kalemini sorgula’.", false);
        _aiInput.Focus();
    }

    private async Task SendAiQuestionAsync()
    {
        if (_aiSending || _aiInput == null || _aiMessages == null) return;
        var question = _aiInput.Text.Trim(); if (question.Length == 0) return;
        _aiInput.Clear(); AddAiMessage(question, true); _aiSending = true;
        try
        {
            var assistant = _db == null || _startupSession == null ? null : new Ar3AiAssistant(_db, CurrentCompanyId(), _startupSession.UserName);
            var reply = assistant == null ? new AiAssistantReply("Veritabanı bağlantısı hazır değil.") : await assistant.AskAsync(question);
            AddAiMessage(reply.Text, false);
        }
        catch (Exception ex) { AddAiMessage("AI isteği tamamlanamadı: " + ex.Message, false); }
        finally { _aiSending = false; _aiInput.Focus(); }
    }

    private void AddAiMessage(string text, bool fromUser)
    {
        if (_aiMessages == null) return;
        var bubble = new Border { MaxWidth = 470, Background = fromUser ? new SolidColorBrush(Color.FromRgb(225, 241, 242)) : Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(218, 226, 229)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(fromUser ? 70 : 0, 0, fromUser ? 0 : 70, 8), HorizontalAlignment = fromUser ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        bubble.Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(45, 57, 64)) };
        _aiMessages.Children.Add(bubble);
        if (_aiMessages.Parent is ScrollViewer viewer) viewer.Dispatcher.BeginInvoke(() => viewer.ScrollToEnd());
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
        if (_db == null) return; _loadingWorkspace = true; BranchContextCombo.ItemsSource = CompanyContextCombo.SelectedValue is string company && Guid.TryParse(company, out _) ? _db.Query("SELECT id AS Id, code AS Code, name AS Name FROM branches WHERE company_id=$id AND is_active=1 ORDER BY code", ("$id", company)).DefaultView : null; RestrictToAccess(BranchContextCombo, _allowedBranchIds); BranchContextCombo.SelectedIndex = BranchContextCombo.Items.Count > 0 ? 0 : -1; _loadingWorkspace = false; ReloadWarehouses();
    }
    // Kullanıcı şube/depo erişimi: hide the şubeler/depolar the signed-in user may not work in (null = unrestricted).
    private static void RestrictToAccess(ComboBox combo, IReadOnlySet<string>? allowed)
    {
        if (allowed == null || combo.ItemsSource is not DataView view) return;
        view.RowFilter = allowed.Count == 0 ? "1=0" : $"Id IN ({string.Join(",", allowed.Select(id => "'" + id.Replace("'", "''") + "'"))})";
    }
    private void ReloadWarehouses()
    {
        if (_db == null) return; _loadingWorkspace = true; WarehouseContextCombo.ItemsSource = BranchContextCombo.SelectedValue is string branch && Guid.TryParse(branch, out _) ? _db.Query("SELECT id AS Id, code AS Code, name AS Name FROM warehouses WHERE branch_id=$id AND is_active=1 ORDER BY code", ("$id", branch)).DefaultView : null; RestrictToAccess(WarehouseContextCombo, _allowedWarehouseIds); WarehouseContextCombo.SelectedIndex = WarehouseContextCombo.Items.Count > 0 ? 0 : -1; _loadingWorkspace = false;
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
    private static TextBlock MenuIcon(string glyph, int size = 18, Brush? color = null) => new() { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size, Foreground = color ?? new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), VerticalAlignment = VerticalAlignment.Center };
    private static FrameworkElement FluentIcon(WpfUi.SymbolRegular symbol, int size = 20, Brush? color = null)
    {
        var stroke = color ?? new SolidColorBrush(Color.FromRgb(102, 102, 102));
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
    // Real Fluent.Ribbon shell (2026-09-21). A menu node is one of: a RibbonTabItem (top-level tab), a
    // RibbonGroupBox (a labeled group of buttons within a tab), or a DropDownButton (a flyout used when
    // an Entry() call nests a group under something that is *not* a tab - Ribbon groups themselves
    // can't nest, so a "sub-group" becomes a dropdown instead). BuildMenu()'s ~140 lines of
    // TopMenu()/Entry()/AddGroup() calls are otherwise untouched by the Ribbon migration - only these
    // helpers' internals changed, which is exactly why that method needed zero edits.
    private sealed class MenuNode
    {
        public Ribbon.RibbonTabItem? Tab;
        public Ribbon.RibbonGroupBox? Group;
        public Ribbon.DropDownButton? Dropdown;
    }
    private readonly Dictionary<Ribbon.RibbonTabItem, Ribbon.RibbonGroupBox> _defaultGroups = new();
    private Ribbon.RibbonGroupBox DefaultGroup(Ribbon.RibbonTabItem tab)
    {
        if (!_defaultGroups.TryGetValue(tab, out var group)) { group = new Ribbon.RibbonGroupBox(); tab.Groups.Add(group); _defaultGroups[tab] = group; }
        return group;
    }
    private MenuNode TopMenu(string title, string glyph)
    {
        var tab = new Ribbon.RibbonTabItem { Header = title }; MainRibbon.Tabs.Add(tab); return new MenuNode { Tab = tab };
    }
    private MenuNode TopMenu(string title, WpfUi.SymbolRegular symbol)
    {
        var tab = new Ribbon.RibbonTabItem { Header = title }; MainRibbon.Tabs.Add(tab); return new MenuNode { Tab = tab };
    }
    private MenuNode Entry(MenuNode parent, string text, string glyph, Action? action = null) => EntryCore(parent, text, MenuIcon(glyph, 14, new SolidColorBrush(Color.FromRgb(132, 132, 132))), () => MenuIcon(glyph, 30, new SolidColorBrush(Color.FromRgb(132, 132, 132))), action);
    private MenuNode Entry(MenuNode parent, string text, WpfUi.SymbolRegular symbol, Action? action = null) => EntryCore(parent, text, FluentIcon(symbol, 16), () => FluentIcon(symbol, 32), action);
    // Office-style group richness: the first leaf added to any newly created RibbonGroupBox renders as
    // a big icon-over-text button (like Word's "Paste"), the rest stay small icon+text - matching real
    // Office ribbons instead of a flat row of identically-sized buttons.
    private MenuNode EntryCore(MenuNode parent, string text, object icon, Func<object> largeIcon, Action? action)
    {
        if (action != null)
        {
            if (parent.Dropdown != null)
            {
                var menuItem = new Ribbon.MenuItem { Header = text, Icon = icon };
                menuItem.Click += (_, _) => action();
                parent.Dropdown.Items.Add(menuItem);
            }
            else
            {
                var group = parent.Group ?? DefaultGroup(parent.Tab!);
                var isFirst = group.Items.Count == 0;
                var button = new Ribbon.Button { Header = text, Icon = icon, ToolTip = text };
                if (isFirst) { button.LargeIcon = largeIcon(); button.SizeDefinition = new Ribbon.RibbonControlSizeDefinition(Ribbon.RibbonControlSize.Large, Ribbon.RibbonControlSize.Large, Ribbon.RibbonControlSize.Large); }
                button.Click += (_, _) => action();
                group.Items.Add(button);
            }
            return new MenuNode();
        }
        if (parent.Tab != null)
        {
            var group = new Ribbon.RibbonGroupBox { Header = text };
            parent.Tab.Groups.Add(group);
            return new MenuNode { Group = group };
        }
        var dropdown = new Ribbon.DropDownButton { Header = text, Icon = icon };
        if (parent.Group != null) parent.Group.Items.Add(dropdown); else parent.Dropdown?.Items.Add(dropdown);
        return new MenuNode { Dropdown = dropdown };
    }
    private static void AddSeparator(MenuNode node) { if (node.Group != null) node.Group.Items.Add(new System.Windows.Controls.Separator()); }
    private void BuildVisibleMenu()
    {
        MainMenu.Items.Clear();
        MenuItem Top(string text, WpfUi.SymbolRegular icon)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            // One deliberate color per top-level module (no unmapped module falls through to a shared
            // default anymore - Mağaza/Satınalma/E-Belge used to collide on the same gray-blue).
            var color = text switch { "Finans" => Color.FromRgb(22, 124, 130), _ => Color.FromRgb(93, 104, 112) };
            var brush = new SolidColorBrush(color);
            header.Children.Add(FluentIcon(icon, 16, brush));
            header.Children.Add(new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(38, 52, 61)), FontWeight = FontWeights.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) });
            var item = new MenuItem { Header = header, Padding = new Thickness(10, 4, 10, 4), Foreground = new SolidColorBrush(Color.FromRgb(38, 52, 61)), Background = Brushes.Transparent, StaysOpenOnClick = true };
            item.PreviewMouseLeftButtonDown += (_, e) => { if (item.Items.Count > 0) { item.IsSubmenuOpen = true; e.Handled = true; } };
            item.MouseEnter += (_, _) => { if (MainMenu.IsMainMenu && item.Items.Count > 0) item.IsSubmenuOpen = true; };
            MainMenu.Items.Add(item); return item;
        }
        void Add(MenuItem parent, string text, Action action) { var item = new MenuItem { Header = new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(38, 52, 61)), FontSize = 11 }, Padding = new Thickness(10, 4, 24, 4) }; item.Click += (_, _) => action(); parent.Items.Add(item); }
        void Separator(MenuItem parent) => parent.Items.Add(new Separator());
        var home = Top("Giriş", WpfUi.SymbolRegular.Home24); Add(home, "Giriş ekranı", () => HomeDocument.IsActive = true);
        var store = Top("Mağaza", WpfUi.SymbolRegular.BuildingShop24); Add(store, "Cari Genel Bakış", OpenAccountDashboard); Add(store, "Müşteri Kartları", () => OpenCanonicalAccounts("Customer", "Müşteriler")); Add(store, "Yeni Satış", () => OpenModulePlan("Yeni Satış")); Add(store, "Sevkiyat Takibi", () => OpenPendingShipments());
        var accounts = Top("Cari", WpfUi.SymbolRegular.People24); Add(accounts, "Cari Kartlar", () => OpenCanonicalAccounts()); Add(accounts, "Müşteriler", () => OpenCanonicalAccounts("Customer", "Müşteriler")); Add(accounts, "Tedarikçiler", () => OpenCanonicalAccounts("Supplier", "Tedarikçiler")); Separator(accounts); Add(accounts, "Cari Hareketler", () => OpenAccountTransactions()); Add(accounts, "Cari Ekstre", () => OpenAccountStatement()); Add(accounts, "Risk ve Kredi", OpenCreditRisk);
        var stock = Top("Stok", WpfUi.SymbolRegular.Box24); var products = new MenuItem { Header = "Ürün Yönetimi", Icon = FluentIcon(WpfUi.SymbolRegular.Box24, 14) }; stock.Items.Add(products); Add(products, "Stok Kartları", OpenProductList); Add(products, "Yeni Stok Kartı", () => OpenProductList()); Add(products, "Toplu Ürün İşlemleri", OpenProductBulk);
         var inventory = new MenuItem { Header = "Stok Fişleri", Icon = FluentIcon(WpfUi.SymbolRegular.Receipt24, 14) }; stock.Items.Add(inventory); Add(inventory, "Stok Giriş Fişleri", () => OpenInventoryDocuments("Stok Giriş Fişleri", "ManualIn")); Add(inventory, "Stok Çıkış Fişleri", () => OpenInventoryDocuments("Stok Çıkış Fişleri", "ManualOut")); Add(inventory, "Depo Transferleri", OpenInventoryTransfers); Add(inventory, "Stok Sayımı", () => OpenInventoryOperation("Sayım")); Add(inventory, "Stok Rezervasyonları", OpenReservations);
        var stockReports = new MenuItem { Header = "Stok Raporları", Icon = FluentIcon(WpfUi.SymbolRegular.DataUsage24, 14) }; stock.Items.Add(stockReports); Add(stockReports, "Stok Durumu", OpenInventoryBalance); Add(stockReports, "Stok Hareketleri", OpenInventoryMovements); Add(stockReports, "Kritik Stoklar", () => OpenProductList("Kritik Stoklar", true, null, true)); Add(stockReports, "Stoksuz Ürünler", () => OpenProductList("Stoksuz Ürünler", null, true, true)); Add(stockReports, "Stok Değer Raporu", OpenStockValuation); Add(stockReports, "Ürün Ekstresi", () => OpenProductLedger());
         var warehouse = new MenuItem { Header = "Depo ve Lokasyonlar", Icon = FluentIcon(WpfUi.SymbolRegular.BuildingShop24, 14) }; stock.Items.Add(warehouse); Add(warehouse, "Depolar", () => OpenWarehouseManagement()); Add(warehouse, "Depo Lokasyonları", () => OpenWarehouseLocations()); Add(warehouse, "Raf / Göz Tanımları", () => OpenWarehouseLocations());
         var tracking = new MenuItem { Header = "İzleme ve Ayarlar", Icon = FluentIcon(WpfUi.SymbolRegular.Settings24, 14) }; stock.Items.Add(tracking); Add(tracking, "Lot / Seri Takip", () => OpenLotTracking()); Add(tracking, "Negatif Stok Politikası", OpenNegativeStockPolicy); Add(tracking, "Barkod Sorgulama", () => OpenBarcodeLookup()); Add(tracking, "Barkod Yazdırma", () => OpenLabelPrint());
         var definitions = new MenuItem { Header = "Tanımlar", Icon = FluentIcon(WpfUi.SymbolRegular.Settings24, 14) }; stock.Items.Add(definitions); Add(definitions, "Markalar", () => OpenMasterCrud("brands", "Marka Tanımları")); Add(definitions, "Kategoriler", () => OpenMasterCrud("categories", "Kategori Tanımları")); Add(definitions, "Stok Grupları", () => OpenMasterCrud("product_groups", "Stok Grubu Tanımları")); Add(definitions, "Menşe Ülkeler", () => OpenMasterCrud("countries", "Menşe Ülke Tanımları")); Add(definitions, "Birimler", () => OpenMasterCrud("units", "Birim Tanımları")); Add(definitions, "Ürün Özellikleri", () => OpenMasterCrud("product_attributes", "Ürün Özellik Tanımları")); Add(definitions, "Varyant Tanımları", () => OpenMasterCrud("variant_definitions", "Varyant Tanımları (Renk / Beden / Beden Tipi / Model)"));
         var pricingMenu = new MenuItem { Header = "Fiyat Yönetimi", Icon = FluentIcon(WpfUi.SymbolRegular.MoneyCalculator24, 14) }; stock.Items.Add(pricingMenu); Add(pricingMenu, "Fiyat Listeleri", OpenPriceLists); Add(pricingMenu, "Ürün Fiyatları", () => OpenProductPrices()); Add(pricingMenu, "Toplu Fiyat Güncelleme", () => OpenProductPrices()); Add(pricingMenu, "Fiyat Değişiklik Geçmişi", () => OpenPriceHistory()); Add(pricingMenu, "Kampanya Fiyatları", OpenCampaignPrices); Add(pricingMenu, "Müşteri Fiyat Grupları", OpenCustomerPriceGroups);
        var purchasing = Top("Satınalma", WpfUi.SymbolRegular.Cart24); Add(purchasing, "Satınalma Siparişleri", () => OpenPurchaseDocuments("Order")); Add(purchasing, "Alış Faturaları", () => OpenPurchaseDocuments("Invoice")); Add(purchasing, "Satınalma İadeleri", () => OpenReturns(ReturnDirection.Purchase));
        var sales = Top("Satış", WpfUi.SymbolRegular.ReceiptMoney24); Add(sales, "Yeni Satış Faturası", OpenNewSalesInvoice); Add(sales, "Satış Faturaları", OpenSalesList); Add(sales, "Satış İadeleri", () => OpenReturns(ReturnDirection.Sales)); Add(sales, "Sevkiyat", () => OpenPendingShipments());
        var finance = Top("Finans", WpfUi.SymbolRegular.WalletCreditCard24); Add(finance, "Genel Bakış", OpenFinanceOverview); Add(finance, "Kasa Kartları", OpenCashAccounts); Add(finance, "Kasa Hareketleri", () => OpenCashTransactions()); Add(finance, "Banka Hesapları", OpenBankAccounts); Add(finance, "Banka Hareketleri", () => OpenBankTransactions()); Add(finance, "Çek / Senet Portföyü", OpenCheques);
        var electronic = Top("E-Belge", WpfUi.SymbolRegular.DocumentArrowRight24); Add(electronic, "Genel Bakış", OpenElectronicDocumentDashboard); Add(electronic, "Giden Belgeler", OpenOutgoingElectronicDocuments); Add(electronic, "Gönderim Kuyruğu", OpenElectronicDocumentOutbox); Add(electronic, "Hatalı Belgeler", OpenFailedElectronicDocuments); Add(electronic, "Ayarlar", OpenElectronicDocumentProviderSettings);
         var settings = Top("Ayarlar", WpfUi.SymbolRegular.Settings24); Add(settings, "Genel ayarlar", OpenGeneralSettings); Add(settings, "Firmalar", () => OpenMasterCrud("companies", "Firma Tanımları")); Add(settings, "Şubeler", () => OpenMasterCrud("branches", "Şube Tanımları")); Add(settings, "Depolar", () => OpenWarehouseManagement()); Add(settings, "Kullanıcı ve Yetkiler", OpenUserRoleManagement);

        // Role-based module visibility (Kullanıcı ve Yetkiler): a top-level module disappears when the
        // signed-in user's role has been configured without that module's view permission. Roles
        // nobody has configured yet keep every module (LocalPermissionService bootstrap rule).
        if (_permissions != null)
        {
            static string TopText(MenuItem item) => item.Header is StackPanel stack ? stack.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty : item.Header?.ToString() ?? string.Empty;
            var required = new Dictionary<string, string[]>
            {
                ["Mağaza"] = ["accounts.view", "invoices.view"], ["Cari"] = ["accounts.view"], ["Stok"] = ["inventory.product.view", "inventory.transaction.view"],
                ["Satınalma"] = ["purchasing.document.view"], ["Satış"] = ["invoices.view", "sales.invoice.post"],
                ["Finans"] = ["cash.view", "cash.transaction.view", "instruments.view"], ["E-Belge"] = ["edocuments.view"]
            };
            foreach (var item in MainMenu.Items.OfType<MenuItem>().ToList())
                if (required.TryGetValue(TopText(item), out var codes) && !_permissions.HasAnyPermission(codes)) MainMenu.Items.Remove(item);
        }

        if (string.Equals(_startupSession?.RoleCode, "CASHIER", StringComparison.OrdinalIgnoreCase))
        {
            static string HeaderText(MenuItem item) => item.Header is StackPanel stack
                ? stack.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty
                : item.Header?.ToString() ?? string.Empty;

            foreach (var item in MainMenu.Items.OfType<MenuItem>().ToList())
                if (HeaderText(item) is not ("Giriş" or "Finans")) MainMenu.Items.Remove(item);

            var financeMenu = MainMenu.Items.OfType<MenuItem>().FirstOrDefault(x => HeaderText(x) == "Finans");
            if (financeMenu != null)
                foreach (var child in financeMenu.Items.OfType<MenuItem>().ToList())
                    if (child.Header?.ToString() is not ("Kasa Kartları" or "Kasa Hareketleri")) financeMenu.Items.Remove(child);
        }
    }
    private void BuildMenu()
    {
        MainRibbon.Tabs.Clear();
        void Planned(string title) => OpenModulePlan(title);
        void AddGroup(MenuNode parent, string title, WpfUi.SymbolRegular icon, params string[] labels)
        {
            var group = Entry(parent, title, icon);
            foreach (var label in labels) Entry(group, label, icon, () => Planned(label));
        }

        var store = TopMenu("Mağaza", WpfUi.SymbolRegular.BuildingShop24);
        Entry(store, "Giriş ekranı", WpfUi.SymbolRegular.Home24, () => HomeDocument.IsActive = true);
        Entry(store, "Müşteri cari / Hesap ekstresi", WpfUi.SymbolRegular.DocumentTable24, OpenLedger);
        Entry(store, "Müşteri kartları", WpfUi.SymbolRegular.PersonAccounts24, () => OpenDefinitions(false));
        AddGroup(store, "Perakende Satış", WpfUi.SymbolRegular.Cart24, "Yeni satış", "Satış geçmişi", "İleri teslim siparişleri");
        AddGroup(store, "Kasa ve Tahsilat", WpfUi.SymbolRegular.WalletCreditCard24, "Kasa işlemleri", "Taksit tahsilatı", "Tahsilat performansı");
        var storeShipments = Entry(store, "Sevkiyat Takibi", WpfUi.SymbolRegular.Box24);
        Entry(storeShipments, "Bekleyen sevkiyatlar", WpfUi.SymbolRegular.VehicleTruckProfile24, OpenPendingShipments);
        Entry(storeShipments, "Gerçekleşen sevkiyatlar", WpfUi.SymbolRegular.CheckmarkCircle24, () => Planned("Gerçekleşen sevkiyatlar"));
        Entry(storeShipments, "SMS sevk emirleri", WpfUi.SymbolRegular.Chat24, () => Planned("SMS sevk emirleri"));

        var accounts = TopMenu("Cari", WpfUi.SymbolRegular.People24);
        Entry(accounts, "Cari Genel Bakış", WpfUi.SymbolRegular.DataUsage24, OpenAccountDashboard);
        Entry(accounts, "Cari Kartlar", WpfUi.SymbolRegular.ContactCard24, () => OpenCanonicalAccounts());
        Entry(accounts, "Müşteriler", WpfUi.SymbolRegular.PersonAccounts24, () => OpenCanonicalAccounts("Customer", "Müşteriler"));
        Entry(accounts, "Tedarikçiler", WpfUi.SymbolRegular.BuildingShop24, () => OpenCanonicalAccounts("Supplier", "Tedarikçiler"));
        AddSeparator(accounts);
        Entry(accounts, "Cari Hareketler", WpfUi.SymbolRegular.ArrowSwap24, () => OpenAccountTransactions());
        Entry(accounts, "Cari Ekstre", WpfUi.SymbolRegular.DocumentTable24, () => OpenAccountStatement());
        Entry(accounts, "Risk & Kredi", WpfUi.SymbolRegular.ShieldCheckmark24, OpenCreditRisk);
        var accountDefinitions = Entry(accounts, "Cari Tanımları", WpfUi.SymbolRegular.PeopleTeam24);
        Entry(accountDefinitions, "Cari Grupları", WpfUi.SymbolRegular.PeopleTeam24, () => OpenMasterCrud("account_groups", "Cari Grubu Tanımları"));
        Entry(accountDefinitions, "Bölgeler", WpfUi.SymbolRegular.Map24, () => OpenMasterCrud("regions", "Bölge Tanımları"));
        Entry(accountDefinitions, "Sevk Bölgeleri", WpfUi.SymbolRegular.VehicleTruckProfile24, () => OpenMasterCrud("delivery_regions", "Sevk Bölgesi Tanımları"));
        Entry(accountDefinitions, "Fiyat Listeleri", WpfUi.SymbolRegular.MoneyCalculator24, OpenPriceLists);

        var stock = TopMenu("Stok", WpfUi.SymbolRegular.Box24);
        var productManagement = Entry(stock, "Ürün Yönetimi", WpfUi.SymbolRegular.Box24);
        Entry(productManagement, "Stok Kartları", WpfUi.SymbolRegular.Box24, OpenProductList);
        Entry(productManagement, "Yeni Stok Kartı", WpfUi.SymbolRegular.AddSquare24, () => OpenProductList());
        Entry(productManagement, "Toplu Ürün İşlemleri", WpfUi.SymbolRegular.BoxMultiple24, OpenProductBulk);
        Entry(productManagement, "Ürün Kopyala", WpfUi.SymbolRegular.DocumentCopy24, OpenProductList); // "Kopyala" toolbar action lives on the Stok Kartları grid — no separate business logic here.
        var barcodeGroup = Entry(stock, "Barkod", WpfUi.SymbolRegular.BarcodeScanner24);
        Entry(barcodeGroup, "Barkod Yönetimi", WpfUi.SymbolRegular.BarcodeScanner24, OpenProductList);
        Entry(barcodeGroup, "Barkod Sorgulama", WpfUi.SymbolRegular.BoxSearch24, OpenBarcodeLookup);
        Entry(barcodeGroup, "Barkod Yazdırma", WpfUi.SymbolRegular.DocumentPrint24, () => OpenLabelPrint());
        var stockDefinitions = Entry(stock, "Tanımlar", WpfUi.SymbolRegular.Settings24);
        Entry(stockDefinitions, "Markalar", WpfUi.SymbolRegular.Tag24, () => OpenMasterCrud("brands", "Marka Tanımları"));
        Entry(stockDefinitions, "Kategoriler", WpfUi.SymbolRegular.Folder24, () => OpenMasterCrud("categories", "Kategori Tanımları"));
        Entry(stockDefinitions, "Stok Grupları", WpfUi.SymbolRegular.Folder24, () => OpenMasterCrud("product_groups", "Stok Grubu Tanımları"));
        Entry(stockDefinitions, "Menşe Ülkeler", WpfUi.SymbolRegular.Globe24, () => OpenMasterCrud("countries", "Menşe Ülke Tanımları"));
        Entry(stockDefinitions, "Birimler", WpfUi.SymbolRegular.Ruler24, () => OpenMasterCrud("units", "Birim Tanımları"));
        Entry(stockDefinitions, "Ürün Özellikleri", WpfUi.SymbolRegular.Tag24, () => OpenMasterCrud("product_attributes", "Ürün Özellik Tanımları"));
        Entry(stockDefinitions, "Varyant Tanımları", WpfUi.SymbolRegular.BoxMultiple24, () => OpenMasterCrud("variant_definitions", "Varyant Tanımları (Renk / Beden / Beden Tipi / Model)"));
        var stockOperations = Entry(stock, "Stok Operasyonları", WpfUi.SymbolRegular.ArrowSwap24);
        Entry(stockOperations, "Stok Durumu", WpfUi.SymbolRegular.DataUsage24, OpenInventoryBalance);
        Entry(stockOperations, "Stok Hareketleri", WpfUi.SymbolRegular.ArrowSwap24, OpenInventoryMovements);
        Entry(stockOperations, "Stok Giriş Fişleri", WpfUi.SymbolRegular.BoxArrowUp24, () => OpenInventoryDocuments("Stok Giriş Fişleri", "ManualIn"));
        Entry(stockOperations, "Stok Çıkış Fişleri", WpfUi.SymbolRegular.BoxArrowLeft24, () => OpenInventoryDocuments("Stok Çıkış Fişleri", "ManualOut"));
         Entry(stockOperations, "Depo Transfer", WpfUi.SymbolRegular.BoxMultiple24, OpenInventoryTransfers);
        Entry(stockOperations, "Sayım", WpfUi.SymbolRegular.Clipboard24, () => OpenInventoryOperation("Sayım"));
        Entry(stockOperations, "Stok Rezervasyonları", WpfUi.SymbolRegular.LockClosed24, OpenReservations);
        Entry(stockOperations, "Ürün Ekstresi", WpfUi.SymbolRegular.DocumentTable24, () => OpenProductLedger());
        var stockReports = Entry(stock, "Stok Raporları", WpfUi.SymbolRegular.DataUsage24);
        Entry(stockReports, "Stok Durum Raporu", WpfUi.SymbolRegular.DataUsage24, OpenInventoryBalance);
        Entry(stockReports, "Stok Hareket Raporu", WpfUi.SymbolRegular.ArrowSwap24, OpenInventoryMovements);
        Entry(stockReports, "Kritik Stok Raporu", WpfUi.SymbolRegular.Warning24, () => OpenProductList("Kritik Stoklar", true, null, true));
        Entry(stockReports, "Stoksuz Ürün Raporu", WpfUi.SymbolRegular.BoxDismiss24, () => OpenProductList("Stoksuz Ürünler", null, true, true));
        Entry(stockReports, "Stok Değer Raporu", WpfUi.SymbolRegular.MoneyCalculator24, OpenStockValuation);
        var warehouseManagement = Entry(stock, "Depo ve Lokasyonlar", WpfUi.SymbolRegular.BuildingShop24);
         Entry(warehouseManagement, "Depolar", WpfUi.SymbolRegular.BuildingShop24, () => OpenWarehouseManagement());
         Entry(warehouseManagement, "Depo Lokasyonları", WpfUi.SymbolRegular.Map24, () => OpenWarehouseLocations());
         Entry(warehouseManagement, "Raf / Göz Tanımları", WpfUi.SymbolRegular.Folder24, () => OpenWarehouseLocations());
        var stockSettings = Entry(stock, "Stok Ayarları", WpfUi.SymbolRegular.Settings24);
        Entry(stockSettings, "Stok Hareket Ayarları", WpfUi.SymbolRegular.Settings24, () => Planned("Stok Hareket Ayarları"));
        Entry(stockSettings, "Negatif Stok Politikası", WpfUi.SymbolRegular.Warning24, OpenNegativeStockPolicy);
        var stockPolicies = Entry(stock, "Stok Politikaları", WpfUi.SymbolRegular.DataUsage24);
        Entry(stockPolicies, "Min / Max Stok", WpfUi.SymbolRegular.DataUsage24, () => OpenProductList("Min Stok Altındakiler", true, null, true));
        Entry(stockPolicies, "Sipariş Seviyeleri", WpfUi.SymbolRegular.Cart24, () => Planned("Sipariş Seviyeleri"));
        var supply = Entry(stock, "Tedarik", WpfUi.SymbolRegular.BuildingShop24);
        Entry(supply, "Ürün Tedarikçileri", WpfUi.SymbolRegular.BuildingShop24, OpenProductList);
        var tracking = Entry(stock, "İzleme", WpfUi.SymbolRegular.EyeLines24);
        Entry(tracking, "Ürün / Barkod İzle", WpfUi.SymbolRegular.EyeLines24, () => OpenLotTracking(trace: true));
        Entry(tracking, "Lot / Seri Takip", WpfUi.SymbolRegular.EyeLines24, () => OpenLotTracking());
        AddSeparator(stock);
        Entry(stock, "Hızlı Ürün Sorgulama", WpfUi.SymbolRegular.BoxMultipleSearch24, OpenQuickProductLookup);
        Entry(stock, "Hızlı Barkod Sorgulama", WpfUi.SymbolRegular.BoxSearch24, OpenBarcodeLookup);
        Entry(stock, "Min Stok Altındakiler", WpfUi.SymbolRegular.DataUsage24, () => OpenProductList("Min Stok Altındakiler", true, null, true));
        Entry(stock, "Stoksuz Ürünler", WpfUi.SymbolRegular.BoxDismiss24, () => OpenProductList("Stoksuz Ürünler", null, true, true));
        Entry(stock, "Pasif Ürünler", WpfUi.SymbolRegular.EyeOff24, () => OpenProductList("Pasif Ürünler", null, null, false));

        var pricing = TopMenu("Fiyat Yönetimi", WpfUi.SymbolRegular.MoneyCalculator24);
        Entry(pricing, "Fiyat Listeleri", WpfUi.SymbolRegular.MoneyCalculator24, OpenPriceLists);
        Entry(pricing, "Ürün Fiyatları", WpfUi.SymbolRegular.ReceiptMoney24, () => OpenProductPrices());
        Entry(pricing, "Toplu Fiyat Güncelleme", WpfUi.SymbolRegular.ArrowSwap24, () => OpenProductPrices());
        Entry(pricing, "Fiyat Değişiklik Geçmişi", WpfUi.SymbolRegular.ChatHistory24, () => OpenPriceHistory());
        Entry(pricing, "Kampanya Fiyatları", WpfUi.SymbolRegular.Tag24, OpenCampaignPrices);
        Entry(pricing, "Müşteri Fiyat Grupları", WpfUi.SymbolRegular.PeopleTeam24, OpenCustomerPriceGroups);

        var purchasing = TopMenu("Satınalma", WpfUi.SymbolRegular.Cart24);
        Entry(purchasing, "Tedarikçiler", WpfUi.SymbolRegular.BuildingShop24, () => OpenCanonicalAccounts("Supplier", "Tedarikçiler"));
        Entry(purchasing, "Satınalma Siparişleri", WpfUi.SymbolRegular.Cart24, () => OpenPurchaseDocuments("Order"));
        Entry(purchasing, "Alış Faturaları", WpfUi.SymbolRegular.DocumentTable24, () => OpenPurchaseDocuments("Invoice"));
        Entry(purchasing, "Satınalma İadeleri", WpfUi.SymbolRegular.ArrowUndo24, () => OpenReturns(ReturnDirection.Purchase));

        var sales = TopMenu("Satış", WpfUi.SymbolRegular.ReceiptMoney24);
        Entry(sales, "Yeni Satış Faturası", WpfUi.SymbolRegular.ReceiptAdd24, OpenNewSalesInvoice);
        Entry(sales, "Satış Faturaları", WpfUi.SymbolRegular.ReceiptMoney24, OpenSalesList);
        AddGroup(sales, "Satış Yönetimi", WpfUi.SymbolRegular.Cart24, "Satış siparişleri", "İade işlemleri", "Günlük satış", "Şube satışları");

        var finance = TopMenu("Finans", WpfUi.SymbolRegular.WalletCreditCard24);
        Entry(finance, "Genel Bakış", WpfUi.SymbolRegular.Money24, OpenFinanceOverview);
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
        Entry(einvoice, "Genel Bakış", WpfUi.SymbolRegular.DataUsage24, OpenElectronicDocumentDashboard);
        Entry(einvoice, "Giden Belgeler", WpfUi.SymbolRegular.DocumentArrowRight24, OpenOutgoingElectronicDocuments);
        Entry(einvoice, "Gönderim Kuyruğu", WpfUi.SymbolRegular.Send24, OpenElectronicDocumentOutbox);
        Entry(einvoice, "Hatalı Belgeler", WpfUi.SymbolRegular.DocumentCheckmark24, OpenFailedElectronicDocuments);
        Entry(einvoice, "Gelen Belgeler", WpfUi.SymbolRegular.MailInbox24, OpenIncomingElectronicDocuments);
        Entry(einvoice, "Ayarlar", WpfUi.SymbolRegular.Settings24, OpenElectronicDocumentProviderSettings);

        var reports = TopMenu("Raporlar", WpfUi.SymbolRegular.ChartMultiple24);
        AddGroup(reports, "Yönetim Raporları", WpfUi.SymbolRegular.ChartMultiple24, "Satış raporları", "Stok raporları", "Finans raporları", "Müşteri raporları", "Cari Raporları");

        var tools = TopMenu("Araçlar", WpfUi.SymbolRegular.Toolbox24);
        var office = Entry(tools, "Office ve Raporlama", WpfUi.SymbolRegular.DocumentTable24);
        Entry(office, "Excel tabloları (ClosedXML)", WpfUi.SymbolRegular.Table24, () => Planned("Excel tabloları (ClosedXML)"));
        Entry(office, "Office belgeleri (Open XML)", WpfUi.SymbolRegular.DocumentText24, () => Planned("Office belgeleri (Open XML)"));
        Entry(office, "Rapor şablonları (FastReport)", WpfUi.SymbolRegular.DocumentData24, () => Planned("Rapor şablonları (FastReport)"));
        Entry(office, "Dapper veri erişimi", WpfUi.SymbolRegular.Database24, () => Planned("Dapper veri erişimi"));
        Entry(tools, "Firmalar", WpfUi.SymbolRegular.Building24, () => OpenMasterCrud("companies", "Firma Tanımları"));
        Entry(tools, "Şubeler", WpfUi.SymbolRegular.BuildingMultiple24, () => OpenMasterCrud("branches", "Şube Tanımları"));
        Entry(tools, "Depolar", WpfUi.SymbolRegular.BoxMultiple24, () => OpenMasterCrud("warehouses", "Depo Tanımları"));
        var toolDefinitions = Entry(tools, "Tanımlar", WpfUi.SymbolRegular.Settings24);
        Entry(toolDefinitions, "Genel ayarlar", WpfUi.SymbolRegular.Settings24, OpenGeneralSettings);
        Entry(toolDefinitions, "Numara serileri", WpfUi.SymbolRegular.NumberSymbol24, () => Planned("Numara serileri"));
        Entry(toolDefinitions, "Para birimleri", WpfUi.SymbolRegular.Money24, () => Planned("Para birimleri"));
        Entry(toolDefinitions, "Vergi politikaları", WpfUi.SymbolRegular.ReceiptMoney24, () => Planned("Vergi politikaları"));
        AddGroup(tools, "Entegrasyon", WpfUi.SymbolRegular.PlugConnected24, "API bağlantıları", "E-Ticaret kanalları", "Migration geçmişi", "Senkronizasyon");
        AddGroup(tools, "İnsan Kaynakları", WpfUi.SymbolRegular.People24, "Personeller", "Departmanlar", "Pozisyonlar", "Bordro dönemleri");
        Entry(tools, "Web Merkezi (CefSharp)", WpfUi.SymbolRegular.Globe24, OpenWebCenter);
        Entry(tools, "Veritabanı bilgisi", WpfUi.SymbolRegular.Database24, () => MessageBox.Show(this, _db == null ? "Veritabanı açılamadı" : $"Provider: SQLite 3\nDurum: Hazır\nDosya: {_db.Path}\nŞema sürümü: {_db.SchemaVersion}\nSon yedek: {_db.LastBackup ?? "Yok"}", "Veritabanı"));
        Entry(tools, "Veritabanı yedeği al", WpfUi.SymbolRegular.Save24, () => Safe(() => MessageBox.Show(this, _db!.Backup(), "Yedek oluşturuldu")));
        Entry(tools, "Hakkında", WpfUi.SymbolRegular.Info24, () => MessageBox.Show(this, "AR3 ERP 0.3.0\nModern ticari işletme yönetimi", "AR3 ERP"));

        var close = TopMenu("Kapat", WpfUi.SymbolRegular.Dismiss24);
        Entry(close, "Programdan çık", WpfUi.SymbolRegular.Dismiss24, Close);
        if (MainRibbon.Tabs.Count > 0) MainRibbon.SelectedTabItem = MainRibbon.Tabs[0];
    }

    private void OpenUserRoleManagement()
    {
        if (!string.Equals(_startupSession?.RoleCode, "ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Kullanıcı ve rol yönetimi yalnızca Yönetici rolüne açıktır.", "Yetki", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_db == null || _users == null) return;
        OpenTab("Kullanıcı ve Yetkiler", () => new UserRoleManagementView(_users, _db, CurrentCompanyId(), _startupSession!.PermissionUserName, () => _permissions?.Refresh()));
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
            var state = new Border { Background = new SolidColorBrush(Color.FromRgb(246, 246, 246)), BorderBrush = new SolidColorBrush(Color.FromRgb(209, 209, 209)), BorderThickness = new Thickness(1), Padding = new Thickness(16), CornerRadius = new CornerRadius(6) };
            state.Child = new TextBlock { Text = "Taslak ekran • Menü bağlantısı hazır • SQLite 3 altyapısı aktif", Foreground = new SolidColorBrush(Color.FromRgb(76, 76, 76)), FontWeight = FontWeights.SemiBold };
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
            Task SetActive(bool active)
            {
                if (grid.SelectedItem is not DataRowView row) return Task.CompletedTask;
                try { _masterData.SetActive(kind, row["Id"].ToString()!, active); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Kayıt güncellenemedi"); }
                return Task.CompletedTask;
            }
            ErpGridContext.Register(grid, "master." + kind, StandardContextActions.MasterData(() => Edit(false), () => Edit(false), SetActive), () => { Refresh(); return Task.CompletedTask; }, "MasterData");
            ActionButton(bar, "+ Yeni (F2)", () => Edit(true)); ActionButton(bar, "Düzenle (F3)", () => Edit(false)); ActionButton(bar, "Yenile (F5)", Refresh); bar.Children.Add(new TextBlock { Text = "Ara: ", VerticalAlignment = VerticalAlignment.Center }); bar.Children.Add(search); KeyboardInteractionService.AttachDebouncedSearch(search, Refresh); KeyboardInteractionService.AttachListShortcuts(root, search, () => Edit(true), () => Edit(false), Refresh); grid.MouseDoubleClick += (_, _) => Edit(false); root.Children.Add(grid); Refresh(); return root;
        });
    }
    private string CurrentCompanyId() => _workspaceContext.CompanyId == Guid.Empty
        ? _db!.Query("SELECT id FROM companies LIMIT 1").Rows[0][0].ToString()!
        : _workspaceContext.CompanyId.ToString()!;

    private string CurrentBranchId() => _workspaceContext.BranchId == Guid.Empty
        ? _db!.Query("SELECT id FROM branches WHERE company_id=$c ORDER BY code LIMIT 1", ("$c", CurrentCompanyId())).Rows[0][0].ToString()!
        : _workspaceContext.BranchId.ToString()!;

    private string CurrentWarehouseId() => _workspaceContext.WarehouseId == Guid.Empty
        ? _db!.Query("SELECT id FROM warehouses WHERE branch_id=$branch ORDER BY code LIMIT 1", ("$branch", CurrentBranchId())).Rows[0][0].ToString()!
        : _workspaceContext.WarehouseId.ToString()!;

    private AccountServices CreateAccountServices() => new(
        new LocalAccountService(_db!), new LocalAccountAddressService(_db!), new LocalAccountContactService(_db!), new LocalAccountBankService(_db!),
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

    private void OpenCashTransactions(string? cashAccountId = null) => OpenTab(cashAccountId == null ? "Kasa Hareketleri" : $"Kasa Hareketleri • {RecordName("cash_accounts", cashAccountId)}", () =>
        WithGridActions(new CashTransactionsView(new CashTransactionsViewModel(CreateCashServices(), DesktopLogging.CreateLogger<CashTransactionsViewModel>(), cashAccountId)), "cash.transactions", LedgerLinkActions(), "CashTransaction"));

    private void OpenCashStatement(string? cashAccountId = null) => OpenTab(cashAccountId == null ? "Kasa Ekstresi" : $"Kasa Ekstresi • {RecordName("cash_accounts", cashAccountId)}", () =>
        WithGridActions(new CashStatementView(new CashStatementViewModel(CreateCashServices(), cashAccountId, DesktopLogging.CreateLogger<CashStatementViewModel>())), "cash.statement", LedgerLinkActions(), "CashTransaction"));

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

    // ===================== Banka (2026-09-23) =====================
    // LocalBankService-backed, ported from ASB's BANKAHESABI/BANKAHAR (SqlData\ASBDB_ERKUR02.mdf).
    // Built as inline code-behind screens (Table()/Column()/ActionButton, same pattern as
    // OpenSalesList/OpenMasterCrud) rather than separate MVVM view classes - the accounting rules
    // live in LocalBankService, not here.
    private void OpenBankAccounts() => OpenTab("Banka Hesapları", () =>
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        var search = new TextBox { Width = 220, Padding = new Thickness(8), ToolTip = "Kod, ad, banka veya IBAN ara" };
        var grid = Table();
        Column(grid, "Kod", "Kod"); Column(grid, "Ad", "Ad"); Column(grid, "Şube", "Sube"); Column(grid, "Banka", "Banka");
        Column(grid, "IBAN", "Iban"); Column(grid, "Tip", "HesapTipi"); Column(grid, "Döviz", "ParaBirimi");
        Column(grid, "Giriş", "Giris", "N2"); Column(grid, "Çıkış", "Cikis", "N2"); Column(grid, "Bakiye", "Bakiye", "N2");
        void Refresh() { grid.ItemsSource = _banks!.Search(CurrentCompanyId(), search: search.Text).DefaultView; }
        void Edit(bool create)
        {
            var row = create ? null : grid.SelectedItem as DataRowView; if (!create && row == null) { MessageBox.Show(this, "Önce bir hesap seçin."); return; }
            var detail = create ? null : _banks!.GetDetail(CurrentCompanyId(), row!["Id"].ToString()!);
            var dialog = new BankAccountDialog(detail?.Account) { Owner = this };
            if (dialog.ShowDialog() == true) { try { _banks!.Save(dialog.ToEditModel(CurrentCompanyId(), CurrentBranchId(), detail?.Account.Id ?? ""), _startupSession!.UserName); Refresh(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Banka hesabı kaydedilemedi"); } }
        }
        Task SetActive(bool active) { if (grid.SelectedItem is DataRowView row) { try { _banks!.SetActive(CurrentCompanyId(), row["Id"].ToString()!, active, _startupSession!.UserName); Refresh(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Güncellenemedi"); } } return Task.CompletedTask; }
        void Movement(bool isIn)
        {
            if (grid.SelectedItem is not DataRowView row) { MessageBox.Show(this, "Önce bir hesap seçin."); return; }
            var dialog = new BankMovementDialog(isIn ? "Banka Girişi" : "Banka Çıkışı") { Owner = this };
            if (dialog.ShowDialog() != true) return;
            try
            {
                if (isIn) _banks!.PostBankIn(CurrentCompanyId(), CurrentBranchId(), row["Id"].ToString()!, "BankIncome", null, dialog.Date, dialog.Amount, row["ParaBirimi"].ToString()!, 1, null, dialog.Description, _startupSession!.UserName);
                else _banks!.PostBankOut(CurrentCompanyId(), CurrentBranchId(), row["Id"].ToString()!, "BankExpense", null, dialog.Date, dialog.Amount, row["ParaBirimi"].ToString()!, 1, null, dialog.Description, _startupSession!.UserName);
                Refresh();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "İşlem kaydedilemedi"); }
        }
        ErpGridContext.Register(grid, "finance.bankaccounts", StandardContextActions.MasterData(() => Edit(false), () => Edit(false), SetActive), () => { Refresh(); return Task.CompletedTask; }, "BankAccount");
        ActionButton(bar, "+ Yeni Hesap (F2)", () => Edit(true)); ActionButton(bar, "Düzenle (F3)", () => Edit(false));
        ActionButton(bar, "Giriş", () => Movement(true)); ActionButton(bar, "Çıkış", () => Movement(false));
        ActionButton(bar, "Hareketler", () => { if (grid.SelectedItem is DataRowView row) OpenBankTransactions(row["Id"].ToString()); });
        ActionButton(bar, "Yenile (F5)", Refresh);
        bar.Children.Add(new TextBlock { Text = "Ara: ", VerticalAlignment = VerticalAlignment.Center }); bar.Children.Add(search);
        KeyboardInteractionService.AttachDebouncedSearch(search, Refresh); KeyboardInteractionService.AttachListShortcuts(root, search, () => Edit(true), () => Edit(false), Refresh);
        grid.MouseDoubleClick += (_, _) => Edit(false); root.Children.Add(grid); Refresh(); return root;
    });

    private void OpenBankTransactions(string? bankAccountId = null) => OpenTab(bankAccountId == null ? "Banka Hareketleri" : $"Banka Hareketleri • {RecordName("bank_accounts", bankAccountId)}", () =>
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        var grid = Table();
        Column(grid, "Tarih", "Tarih"); Column(grid, "Hesap", "Hesap"); Column(grid, "İşlem", "IslemTipi"); Column(grid, "Belge", "Belge");
        Column(grid, "Cari", "Cari"); Column(grid, "Açıklama", "Aciklama"); Column(grid, "Giriş", "Giris", "N2"); Column(grid, "Çıkış", "Cikis", "N2");
        Column(grid, "Döviz", "Doviz"); Column(grid, "Durum", "Durum");
        void Refresh() => grid.ItemsSource = _banks!.GetTransactions(CurrentCompanyId(), bankAccountId).DefaultView;
        ActionButton(bar, "Yenile (F5)", Refresh);
        KeyboardInteractionService.AttachListShortcuts(root, null, null, null, Refresh);
        root.Children.Add(grid); Refresh(); return root;
    });

    // ===================== Çek / Senet (2026-09-23) =====================
    // LocalChequeService-backed, ported from ASB's CEKSENET/CEKKARNE (SqlData\ASBDB_ERKUR02.mdf).
    // Toolbar drives the lifecycle directly (Bankaya Tahsile Ver / Tahsil Et / Karşılıksız / Ciro Et
    // / Öde / İade Et) rather than a right-click menu, since which actions are legal depends on both
    // direction and current status - see LocalChequeService's class doc for the state machine.
    private void OpenCheques() => OpenTab("Çek / Senet Portföyü", () =>
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var summary = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(summary, Dock.Top); root.Children.Add(summary);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        var search = new TextBox { Width = 200, Padding = new Thickness(8), ToolTip = "Belge no, keşideci veya cari ara" };
        var directionFilter = new ComboBox { ItemsSource = new[] { "Tümü", "Alınan", "Verilen" }, SelectedIndex = 0, Width = 100, Margin = new Thickness(8, 0, 0, 0) };
        var grid = Table();
        Column(grid, "Tür", "Tur"); Column(grid, "Yön", "Yon"); Column(grid, "Durum", "Durum"); Column(grid, "Cari", "Cari");
        Column(grid, "Tutar", "Tutar", "N2"); Column(grid, "Döviz", "Doviz"); Column(grid, "Vade", "VadeTarihi"); Column(grid, "Belge No", "BelgeNo");
        Column(grid, "Keşideci", "Kesideci"); Column(grid, "Banka", "Banka"); Column(grid, "Açıklama", "Aciklama");
        var statusLabels = new Dictionary<string, string> { ["Portfolio"] = "Portföyde", ["DepositedForCollection"] = "Tahsilde", ["Collected"] = "Tahsil Edildi", ["Bounced"] = "Karşılıksız", ["Endorsed"] = "Ciro Edildi", ["Paid"] = "Ödendi", ["ReturnedToDrawer"] = "İade Edildi" };
        void Refresh()
        {
            var direction = directionFilter.SelectedIndex switch { 1 => "Received", 2 => "Given", _ => null };
            var table = _cheques!.Search(CurrentCompanyId(), direction: direction, search: search.Text);
            foreach (DataRow r in table.Rows) { r["Tur"] = r["Tur"].ToString() == "Cheque" ? "Çek" : "Senet"; r["Yon"] = r["Yon"].ToString() == "Received" ? "Alınan" : "Verilen"; r["Durum"] = statusLabels.GetValueOrDefault(r["Durum"].ToString()!, r["Durum"].ToString()!); }
            grid.ItemsSource = table.DefaultView;
            var s = _cheques.Summary(CurrentCompanyId());
            summary.Children.Clear();
            void Card(string caption, string value, string color) => summary.Children.Add(new Border
            {
                Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(228, 228, 228)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0),
                Child = new StackPanel { Children = { new TextBlock { Text = caption, Foreground = Brushes.Gray, FontSize = 10 }, new TextBlock { Text = value, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = (Brush)new BrushConverter().ConvertFromString(color)! } } }
            });
            Card("Portföyde", s.Portfolio.ToString(Turkish), "#3578B8"); Card("Tahsilde", s.Deposited.ToString(Turkish), "#C88A21");
            Card("Vadesi Geçen", s.Overdue.ToString(Turkish), "#C0392B"); Card("Portföy Tutarı", s.PortfolioAmount.ToString("N2", Turkish), "#2A9D8F");
        }
        string SelectedId() { if (grid.SelectedItem is not DataRowView row) throw new ArgumentException("Önce bir kayıt seçin."); return row["Id"].ToString()!; }
        void Guard(Action action) { try { action(); Refresh(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Çek/Senet"); } }
        void New(string direction)
        {
            var accounts = _db!.Query("SELECT id AS Id, code || ' — ' || name AS Ad FROM accounts WHERE company_id=$c AND is_active=1 ORDER BY code", ("$c", CurrentCompanyId()));
            var dialog = new ChequeDialog(direction, accounts, "TRY") { Owner = this };
            if (dialog.ShowDialog() != true) return;
            Guard(() => { if (direction == "Received") _cheques!.Receive(dialog.ToEditModel(CurrentCompanyId(), CurrentBranchId(), direction), _startupSession!.UserName); else _cheques!.Give(dialog.ToEditModel(CurrentCompanyId(), CurrentBranchId(), direction), _startupSession!.UserName); });
        }
        void DepositForCollection()
        {
            var id = SelectedId();
            var dialog = new AccountPickerDialog("Bankaya Tahsile Ver", "Banka hesabı *", _banks!.Lookup(CurrentCompanyId())) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            Guard(() => _cheques!.DepositForCollection(CurrentCompanyId(), id, dialog.AccountId!, _startupSession!.UserName));
        }
        void Collect() { var id = SelectedId(); Guard(() => _cheques!.Collect(CurrentCompanyId(), id, DateTime.Today, _startupSession!.UserName)); }
        void CollectToCash()
        {
            var id = SelectedId();
            var dialog = new AccountPickerDialog("Kasaya Tahsil Et", "Kasa *", _db!.Query("SELECT id AS Id, code || ' — ' || name AS Ad FROM cash_accounts WHERE company_id=$c AND is_active=1 ORDER BY code", ("$c", CurrentCompanyId()))) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            Guard(() => _cheques!.CollectToCash(CurrentCompanyId(), id, dialog.AccountId!, dialog.Date, _startupSession!.UserName));
        }
        void Bounce() { var id = SelectedId(); if (MessageBox.Show(this, "Bu çek/senet karşılıksız olarak işaretlenecek. Onaylıyor musunuz?", "Karşılıksız", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; Guard(() => _cheques!.Bounce(CurrentCompanyId(), id, _startupSession!.UserName)); }
        void Endorse() { var id = SelectedId(); var dialog = new TextPromptDialog("Ciro Et", "Ciro edilen kişi/kurum *") { Owner = this }; if (dialog.ShowDialog() != true) return; Guard(() => _cheques!.Endorse(CurrentCompanyId(), id, dialog.Value, _startupSession!.UserName)); }
        void Pay()
        {
            var id = SelectedId();
            var cashLookup = _db!.Query("SELECT id AS Id, code || ' — ' || name AS Ad FROM cash_accounts WHERE company_id=$c AND is_active=1 ORDER BY code", ("$c", CurrentCompanyId()));
            var dialog = new ChequePaymentDialog(cashLookup, _banks!.Lookup(CurrentCompanyId())) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            Guard(() => _cheques!.Pay(CurrentCompanyId(), id, dialog.CashAccountId, dialog.BankAccountId, dialog.Date, _startupSession!.UserName));
        }
        void ReturnToDrawer() { var id = SelectedId(); var dialog = new TextPromptDialog("İade Et", "Not (opsiyonel)", required: false) { Owner = this }; if (dialog.ShowDialog() != true) return; Guard(() => _cheques!.ReturnToDrawer(CurrentCompanyId(), id, _startupSession!.UserName, dialog.Value)); }
        ActionButton(bar, "+ Yeni Alınan", () => New("Received")); ActionButton(bar, "+ Yeni Verilen", () => New("Given"));
        ActionButton(bar, "Bankaya Tahsile Ver", DepositForCollection); ActionButton(bar, "Tahsil Et", Collect); ActionButton(bar, "Kasaya Tahsil Et", CollectToCash);
        ActionButton(bar, "Karşılıksız", Bounce); ActionButton(bar, "Ciro Et", Endorse); ActionButton(bar, "Öde", Pay); ActionButton(bar, "İade Et", ReturnToDrawer);
        ActionButton(bar, "Yenile (F5)", Refresh);
        bar.Children.Add(new TextBlock { Text = "Ara: ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) }); bar.Children.Add(search); bar.Children.Add(directionFilter);
        directionFilter.SelectionChanged += (_, _) => Refresh(); KeyboardInteractionService.AttachDebouncedSearch(search, Refresh);
        root.Children.Add(grid); Refresh(); return root;
    });

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
            cards.Children.Add(new Border { Child = body, Width = 205, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(15), Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(228, 228, 228)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) });
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
        WithGridActions(new AccountTransactionsView(new AccountLedgerViewModel(new LocalAccountService(_db!), CurrentCompanyId(), accountId, DesktopLogging.CreateLogger<AccountLedgerViewModel>())), "accounts.transactions", LedgerLinkActions(), "AccountTransaction"));

    private void OpenAccountStatement(string? selectedAccountId = null) => OpenTab(selectedAccountId == null ? "Cari Ekstre" : $"Cari Ekstre • {RecordName("accounts", selectedAccountId)}", () =>
        WithGridActions(new AccountStatementView(new AccountStatementViewModel(new LocalAccountService(_db!), CurrentCompanyId(), selectedAccountId, DesktopLogging.CreateLogger<AccountStatementViewModel>())), "accounts.statement", LedgerLinkActions(), "AccountTransaction"));

    private void OpenCreditRisk() => OpenTab("Risk & Kredi", () =>
        WithGridActions(new CreditRiskView(new CreditRiskViewModel(new LocalAccountService(_db!), CurrentCompanyId(), DesktopLogging.CreateLogger<CreditRiskViewModel>())), "accounts.creditrisk", LedgerLinkActions(), "Account"));

    // Report views below are XAML UserControls whose DataGrid has no x:Name. InitializeComponent has
    // already built their logical tree when the constructor returns, so the grid is found and
    // registered right here - no dependency on Loaded ordering against the app-wide
    // ProfessionalGrid_Loaded default registration.
    private static T WithGridActions<T>(T view, string viewKey, IReadOnlyList<ContextActionDefinition> actions, string entityType) where T : FrameworkElement
    {
        static DataGrid? FindGrid(object node)
        {
            if (node is DataGrid grid) return grid;
            if (node is not DependencyObject element) return null;
            foreach (var child in LogicalTreeHelper.GetChildren(element)) if (FindGrid(child) is { } found) return found;
            return null;
        }
        if (FindGrid(view) is { } grid) ErpGridContext.Register(grid, viewKey, actions, null, entityType);
        return view;
    }

    private IReadOnlyList<ContextActionDefinition> LedgerLinkActions() => StandardContextActions.LedgerLinks(
        OpenAccountCard, id => OpenAccountStatement(id), id => OpenAccountTransactions(id, RecordName("accounts", id)),
        CanOpenSourceDocument, OpenSourceDocument, id => OpenCashTransactions(id), id => OpenCashStatement(id), id => OpenBankTransactions(id));

    // Tab titles are the dedupe key in OpenTab, so a filtered screen needs the record in its title -
    // otherwise Kasa Hareketleri for one cash account just re-activates the unfiltered tab.
    private string RecordName(string table, string id)
    {
        if (_db == null || table is not ("accounts" or "cash_accounts" or "bank_accounts" or "warehouses")) return id;
        var row = _db.Query($"SELECT code, name FROM {table} WHERE id=$id", ("$id", id)).Rows.Cast<DataRow>().FirstOrDefault();
        return row == null ? id[..Math.Min(8, id.Length)] : $"{row["code"]} {row["name"]}";
    }

    private void OpenPendingShipments() => OpenTab("Bekleyen sevkiyatlar", () =>
        LegacyAlignedViews.CreateShipmentQueue(_db!, CurrentCompanyId()));

    private void OpenFinanceOverview() => OpenTab("Finans genel bakış", () =>
        LegacyAlignedViews.CreateFinanceOverview(_db!, CurrentCompanyId()));

    // Phase 9 (§4/§8/§12/§20): E-Belge Operasyon Merkezi. Each screen opens as its own de-duplicated
    // tab (OpenTab already refuses to open a second tab with the same title); navigation callbacks
    // (openDocument/openSourceDocument/openAccount/openQueueRecord) are passed in here rather than
    // baked into ElectronicDocumentContextActions, so that factory stays screen-agnostic.
    private void OpenElectronicDocumentDashboard() => OpenTab("E-Belge Genel Bakış", () =>
        ElectronicDocumentDashboardView.Create(_db!, CurrentCompanyId(), OpenElectronicDocumentById));

    private void OpenOutgoingElectronicDocuments() => OpenTab("Giden Belgeler", () =>
        OutgoingElectronicDocumentsView.Create(_db!, CurrentCompanyId(), _startupSession!.UserName,
            OpenElectronicDocumentById, OpenSourceDocument, OpenAccountCard, OpenQueueRecord));

    private void OpenElectronicDocumentOutbox() => OpenTab("Gönderim Kuyruğu", () =>
        ElectronicDocumentOutboxView.Create(_db!, CurrentCompanyId(), _startupSession!.UserName, OpenElectronicDocumentById));

    private void OpenFailedElectronicDocuments() => OpenTab("Hatalı Belgeler", () =>
        FailedElectronicDocumentsView.Create(_db!, CurrentCompanyId(), _startupSession!.UserName,
            OpenElectronicDocumentById, OpenSourceDocument, OpenAccountCard, OpenQueueRecord));

    // §3: no real inbox engine exists yet (incoming e-document processing is out of scope for every
    // phase so far) - a real, honest empty state instead of a fake populated list.
    private void OpenIncomingElectronicDocuments() => OpenTab("Gelen Belgeler", () =>
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Gelen Belgeler", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        panel.Children.Add(new TextBlock { Text = "Gelen e-belge (e-Fatura/e-İrsaliye) işleme motoru henüz uygulanmadı. Bu ekran, o motor eklendiğinde gerçek verilerle doldurulacaktır.",
            TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(118, 118, 118)) });
        return (UIElement)panel;
    });

    private void OpenElectronicDocumentProviderSettings() => OpenTab("E-Belge Ayarları", () =>
        ElectronicDocumentProviderSettingsView.Create(_db!, CurrentCompanyId()));

    // A generic detail entry point: opens the invoice tab when the electronic document's source is
    // a SalesInvoice (the only source type that exists today); otherwise falls back to a minimal
    // read-only electronic-document panel so a future EDespatch/incoming source is never a dead click.
    private void OpenElectronicDocumentById(string electronicDocumentId)
    {
        var documents = new LocalElectronicDocumentService(_db!);
        var document = documents.Get(electronicDocumentId);
        if (document == null) { MessageBox.Show(this, "Elektronik belge bulunamadı.", "E-Belge"); return; }
        if (document.SourceEntityType == "SalesInvoice") { OpenSourceDocument(document.SourceEntityType, document.SourceEntityId); return; }
        OpenTab($"E-Belge • {document.DocumentNumber ?? document.Uuid[..8]}", () => ElectronicDocumentGenericDetailView.Create(_db!, electronicDocumentId));
    }

    private static bool CanOpenSourceDocument(string sourceEntityType) => sourceEntityType is "SalesInvoice" or "PurchaseInvoice";

    private void OpenSourceDocument(string sourceEntityType, string sourceEntityId)
    {
        // Alış faturalarının tekil detay ekranı yok; liste ekranına gider.
        if (sourceEntityType == "PurchaseInvoice") { OpenPurchaseDocuments("Invoice"); return; }
        if (sourceEntityType != "SalesInvoice" || string.IsNullOrWhiteSpace(sourceEntityId)) { MessageBox.Show(this, "Bu kaynak belge tipi için ekran henüz yok.", "Kaynak Belge"); return; }
        OpenTab($"Fatura • {sourceEntityId[..Math.Min(8, sourceEntityId.Length)]}", () => InvoiceDetailView.Create(_db!, sourceEntityId, _startupSession!.UserName));
    }

    private void OpenAccountCard(string accountId)
    {
        var editViewModel = new AccountEditViewModel(CreateAccountServices(), _startupSession!.UserName, accountId);
        new AccountEditDialog(editViewModel) { Owner = this }.ShowDialog();
    }

    // Reusable "open this product's card" for report/ledger grids (Stok Durumu, Stok Hareketleri)
    // that show a product by name only - unlike OpenProductList's own Edit(), there's no grid row
    // with the ProductDialog's expected Kod/Ad columns here, so this goes through GetDetail's
    // ProductDetailEdit instead (ProductDialog's ctor reads code/name from `detail` when given).
    private void OpenProductCard(string productId)
    {
        var company = CurrentCompanyId();
        var unit = _db!.Query("SELECT id FROM units WHERE company_id=$c AND is_active=1 ORDER BY code LIMIT 1", ("$c", company)).Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() ?? string.Empty;
        var detail = _products!.GetDetail(productId, company);
        if (detail == null) { MessageBox.Show(this, "Ürün kartı bulunamadı.", "Ürün Kartı"); return; }
        var dialog = new ProductDialog(null, unit, company, _db, detail) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { _products.Save(dialog.ToEditModel()); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Stok kartı kaydedilemedi"); }
    }

    private void OpenQueueRecord(string electronicDocumentId)
    {
        var outbox = new ElectronicDocumentOutboxService(_db!, new LocalElectronicDocumentService(_db!));
        var detail = outbox.GetLatestForDocument(electronicDocumentId);
        if (detail == null) { MessageBox.Show(this, "Bu belge için bir kuyruk kaydı yok.", "Kuyruk Kaydı"); return; }
        ElectronicDocumentDialogs.ShowOutboxDetail(detail, this);
    }

    private void OpenGeneralSettings() => OpenTab("Genel ayarlar", () =>
        LegacyAlignedViews.CreateGeneralSettings(_db!, CurrentCompanyId()));

    private void OpenPurchaseDocuments(string documentType)
    {
        var title = documentType == "Order" ? "Satınalma Siparişleri" : "Alış Faturaları";
        OpenTab(title, () => new PurchaseModuleView(_db!, CurrentCompanyId(), CurrentBranchId(), CurrentWarehouseId(), documentType, _startupSession!.UserName));
    }

    private void OpenSalesList()
    {
        OpenTab("Satış Faturaları", () =>
        {
            var root = new DockPanel { Margin = new Thickness(18) }; var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
            var search = new TextBox { Width = 240, Padding = new Thickness(8) };
            var grid = Table();
            foreach (var k in new[] { "FaturaNo", "Tarih", "CariKod", "Cari", "Sube", "Depo", "AraToplam", "Iskonto", "KDV", "GenelToplam", "Durum" }) Column(grid, k, k);
            Column(grid, "E-Belge Tipi", "EBelgeTipiTr"); Column(grid, "E-Belge Durumu", "EBelgeDurumuTr");
            string company = _workspaceContext.CompanyId == Guid.Empty ? _db!.Query("SELECT id FROM companies LIMIT 1").Rows[0][0].ToString()! : _workspaceContext.CompanyId.ToString();
            var service = new LocalSalesService(_db!, new ElectronicDocumentRoutingService(_db!), new LocalElectronicDocumentService(_db!));
            void Refresh()
            {
                var table = service.Search(company, search.Text);
                table.Columns.Add("EBelgeTipiTr", typeof(string)); table.Columns.Add("EBelgeDurumuTr", typeof(string));
                foreach (DataRow row in table.Rows)
                {
                    row["Durum"] = R3.Desktop.Presentation.EDocumentPresentation.SalesStatusLabel(row["Durum"].ToString()!);
                    row["EBelgeTipiTr"] = row["EBelgeTipi"] is DBNull ? "—" : R3.Desktop.Presentation.EDocumentPresentation.TypeLabel(Enum.Parse<ElectronicDocumentType>(row["EBelgeTipi"].ToString()!));
                    row["EBelgeDurumuTr"] = row["EBelgeDurumu"] is DBNull ? "—" : R3.Desktop.Presentation.EDocumentPresentation.StatusLabel(Enum.Parse<ElectronicDocumentStatus>(row["EBelgeDurumu"].ToString()!));
                }
                grid.ItemsSource = table.DefaultView;
            }
            void OpenSelected()
            {
                if (grid.SelectedItem is not DataRowView row) { MessageBox.Show(this, "Önce bir fatura seçin.", "Satış faturası"); return; }
                var id = row["Id"].ToString()!;
                OpenTab($"Fatura • {(row["FaturaNo"].ToString() == "Taslak" ? id[..Math.Min(8, id.Length)] : row["FaturaNo"])}", () => InvoiceDetailView.Create(_db!, id, _startupSession!.UserName));
            }
            void OpenSelectedAccount() { if (grid.SelectedItem is DataRowView row && row["CariId"]?.ToString() is { Length: > 0 } accountId) OpenAccountCard(accountId); }
            void OpenSelectedAccountTransactions() { if (grid.SelectedItem is DataRowView row && row["CariId"]?.ToString() is { Length: > 0 } accountId) OpenAccountTransactions(accountId, row["Cari"].ToString()); }
            ErpGridContext.Register(grid, "sales.invoices", StandardContextActions.SalesInvoices(OpenSelected, OpenSelectedAccount, OpenSelectedAccountTransactions), () => { Refresh(); return Task.CompletedTask; }, "SalesDocument");
            ActionButton(bar, "Yeni Fatura", OpenNewSalesInvoice);
            ActionButton(bar, "Aç / Düzenle", OpenSelected);
            ActionButton(bar, "Faturayı Kes", OpenSelected); // §14: confirm + progress + success/failure live on the detail screen (§15-18), not duplicated here.
            ActionButton(bar, "Yenile", Refresh);
            bar.Children.Add(search);
            KeyboardInteractionService.AttachDebouncedSearch(search, Refresh); KeyboardInteractionService.AttachListShortcuts(root, search, OpenNewSalesInvoice, OpenSelected, Refresh);
            grid.MouseDoubleClick += (_, _) => OpenSelected();
            root.Children.Add(grid); Refresh(); return root;
        });
    }
    private void OpenNewSalesInvoice()
    {
        if (_db == null) return;
        string company = _workspaceContext.CompanyId == Guid.Empty ? _db.Query("SELECT id FROM companies LIMIT 1").Rows[0][0].ToString()! : _workspaceContext.CompanyId.ToString();
        string branch = _workspaceContext.BranchId == Guid.Empty ? _db.Query("SELECT id FROM branches WHERE company_id=$c LIMIT 1", ("$c", (object)company)).Rows[0][0].ToString()! : _workspaceContext.BranchId.ToString();
        string warehouse = _workspaceContext.WarehouseId == Guid.Empty ? _db.Query("SELECT id FROM warehouses WHERE branch_id=$b LIMIT 1", ("$b", (object)branch)).Rows[0][0].ToString()! : _workspaceContext.WarehouseId.ToString();
        var account = _db.Query("SELECT id FROM accounts WHERE company_id=$c AND is_active=1 AND account_type IN ('Customer','CustomerAndSupplier') LIMIT 1", ("$c", (object)company));
        if (account.Rows.Count == 0) { MessageBox.Show(this, "Önce aktif bir müşteri cari hesabı oluşturun.", "Satış faturası"); return; }
        var dialog = new SalesInvoiceDialog(new LocalSalesService(_db, new ElectronicDocumentRoutingService(_db), new LocalElectronicDocumentService(_db)), _db, company, branch, warehouse, account.Rows[0][0].ToString()!) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.DocumentId != null)
            OpenTab($"Fatura • {dialog.DocumentId[..Math.Min(8, dialog.DocumentId.Length)]}", () => InvoiceDetailView.Create(_db, dialog.DocumentId, _startupSession!.UserName));
    }
    private void OpenProductList() => OpenProductList("Stok Kartları", null, null, null);
    private void OpenProductList(string title, bool? belowMinimumStock, bool? outOfStock, bool? activeOnly)
    {
        OpenTab(title, () =>
        {
            var root = new DockPanel { Margin = new Thickness(16) };
            var bar = new StackPanel { Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
            var actionBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var filterBar = new WrapPanel { Orientation = Orientation.Horizontal };
            bar.Children.Add(actionBar); bar.Children.Add(filterBar);
            var search = new TextBox { Width = 260, Height = 26, Padding = new Thickness(7, 4, 7, 4), ToolTip = "Stok kodu, stok adı veya barkod okutun" };
            string Company() => (_workspaceContext.CompanyId == Guid.Empty ? _db!.Query("SELECT id FROM companies WHERE is_active=1 ORDER BY code LIMIT 1").Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() : _workspaceContext.CompanyId.ToString()) ?? string.Empty;
            var brand = _db!.Query("SELECT id AS Id, code || ' — ' || name AS Display FROM brands WHERE company_id=$c AND is_active=1 ORDER BY code", ("$c", Company())).DefaultView; var category = _db.Query("SELECT id AS Id, code || ' — ' || name AS Display FROM categories WHERE company_id=$c AND is_active=1 ORDER BY code", ("$c", Company())).DefaultView;
            var brandFilter = new ComboBox { Width = 150, Height = 26, Margin = new Thickness(5, 0, 0, 0), ItemsSource = brand, DisplayMemberPath = "Display", SelectedValuePath = "Id", SelectedIndex = -1, ToolTip = "Marka filtresi" };
            var categoryFilter = new ComboBox { Width = 150, Height = 26, Margin = new Thickness(5, 0, 0, 0), ItemsSource = category, DisplayMemberPath = "Display", SelectedValuePath = "Id", SelectedIndex = -1, ToolTip = "Kategori filtresi" };
            var typeFilter = new ComboBox { Width = 125, Height = 26, Margin = new Thickness(5, 0, 0, 0), ItemsSource = new[] { new { Id = "", Name = "Ürün tipi" }, new { Id = "Stock", Name = "Stok" }, new { Id = "Service", Name = "Hizmet" }, new { Id = "Bundle", Name = "Takım / Set" }, new { Id = "RawMaterial", Name = "Hammadde" }, new { Id = "FinishedGood", Name = "Mamul" } }, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedIndex = 0 };
            var activeFilter = new ComboBox { Width = 105, Height = 26, Margin = new Thickness(5, 0, 0, 0), ItemsSource = new[] { new { Id = "", Name = "Aktiflik" }, new { Id = "true", Name = "Aktif" }, new { Id = "false", Name = "Pasif" } }, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedIndex = 0 };
            var negative = new CheckBox { Content = "Negatif stok", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            var belowMinimum = new CheckBox { Content = "Minimum altı", IsChecked = belowMinimumStock == true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var pageLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) }; var previous = new Button { Content = "‹", Height = 26, Padding = new Thickness(8, 2, 8, 2) }; var next = new Button { Content = "›", Height = 26, Padding = new Thickness(8, 2, 8, 2) }; var page = 1;
            // NOTE: Column()'s signature is (grid, title, path) - these tuples are (sqlAlias, label),
            // so the call below intentionally passes Item2 (label) then Item1 (sqlAlias/path).
            var grid = Table(); grid.CanUserReorderColumns = true; grid.CanUserResizeColumns = true;
            foreach (var column in new[] { ("StokKodu", "Stok Kodu"), ("StokAdi", "Stok Adı"), ("UrunTipi", "Ürün Tipi"), ("AnaBirim", "Ana Birim"), ("BirincilBarkod", "Birincil Barkod"), ("Marka", "Marka"), ("Kategori", "Kategori"), ("StokGrubu", "Stok Grubu"), ("Mense", "Menşe"), ("MevcutStok", "Mevcut Stok"), ("RezerveStok", "Rezerve Stok"), ("KullanilabilirStok", "Kullanılabilir Stok"), ("SonGuncelleme", "Son Güncelleme") }) Column(grid, column.Item2, column.Item1);
            foreach (var column in new[] { ("Aktif", "Aktif"), ("SatisaAcik", "Satışa Açık"), ("TanimTamam", "Tanım Tamam") }) BoolColumn(grid, column.Item2, column.Item1);
            void Refresh() { var selectedActive = activeFilter.SelectedValue?.ToString(); bool? activeValue = selectedActive switch { "true" => true, "false" => false, _ => activeOnly }; var result = _products!.SearchPage(new ProductListQuery(Company(), search.Text, BrandId: brandFilter.SelectedValue?.ToString(), CategoryId: categoryFilter.SelectedValue?.ToString(), ProductType: typeFilter.SelectedValue?.ToString(), ActiveOnly: activeValue, NegativeStockOnly: negative.IsChecked == true, OutOfStockOnly: outOfStock == true, BelowMinimumOnly: belowMinimum.IsChecked == true, Page: page, PageSize: 50)); foreach (DataRow row in result.Rows.Rows) row["UrunTipi"] = R3.Desktop.Presentation.InventoryPresentation.ProductTypeLabel(row["UrunTipi"].ToString()!); grid.ItemsSource = result.Rows.DefaultView; pageLabel.Text = $"Sayfa {result.Page} / {Math.Max(1, (int)Math.Ceiling(result.TotalCount / (double)result.PageSize))}"; previous.IsEnabled = page > 1; next.IsEnabled = page * result.PageSize < result.TotalCount; }
            void Edit(bool create)
            {
                while (true)
                {
                    var row = create ? null : grid.SelectedItem as DataRowView; if (!create && row == null) { MessageBox.Show(this, "Önce stok kartı seçin."); return; }
                    var company = Company(); var unit = _db!.Query("SELECT id FROM units WHERE company_id=$c AND is_active=1 ORDER BY code LIMIT 1", ("$c", company)).Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() ?? string.Empty;
                    var detail = !create ? _products!.GetDetail(row!["Id"].ToString()!, company) : null;
                    var dialog = new ProductDialog(row, unit, company, _db, detail) { Owner = this };
                    if (dialog.ShowDialog() != true) return;
                    try { _products!.Save(dialog.ToEditModel()); Refresh(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Stok kartı kaydedilemedi"); return; }
                    if (!dialog.SaveAndNew) return;
                    create = true; // §28/§40 "Kaydet ve Yeni": loop straight into another blank card.
                }
            }
            void Copy() { var row = grid.SelectedItem as DataRowView; if (row == null) { MessageBox.Show(this, "Önce stok kartı seçin."); return; } try { var source = _products!.GetDetail(row["Id"].ToString()!, Company())?.Product; if (source == null) return; _products.Copy(source, source.Code + "-KOPYA", source.Name + " (Kopya)"); Refresh(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Stok kartı kopyalanamadı"); } }
            void ToggleActive() { var row = grid.SelectedItem as DataRowView; if (row == null) { MessageBox.Show(this, "Önce stok kartı seçin."); return; } try { _products!.SetActive(row["Id"].ToString()!, Company(), !Convert.ToBoolean(row["Aktif"])); Refresh(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Stok kartı güncellenemedi"); } }
            void PrintBarcode() { if (grid.SelectedItem is not DataRowView row) { MessageBox.Show(this, "Önce stok kartı seçin."); return; } OpenLabelPrint(row["Id"].ToString()); }
            void ConfigureColumns() { var dialog = new GridColumnVisibilityDialog(grid) { Owner = this }; dialog.ShowDialog(); }
            ErpGridContext.Register(grid, "inventory.products", StandardContextActions.Products(
                () => Edit(false), () => Edit(false), OpenInventoryMovements, OpenInventoryBalance,
                () => OpenInventoryOperation("Stok Giriş"), () => OpenInventoryOperation("Stok Çıkış"),
                () => OpenInventoryOperation("Depo Transfer"), () => OpenInventoryOperation("Sayım")),
                () => { Refresh(); return Task.CompletedTask; }, "Product");
            ActionButton(actionBar, "+ Yeni Stok Kartı (F2)", () => Edit(true)); ActionButton(actionBar, "Düzenle (F3)", () => Edit(false)); ActionButton(actionBar, "Kopyala", Copy); ActionButton(actionBar, "Aktif / Pasif", ToggleActive); ActionButton(actionBar, "Barkod Yazdır", PrintBarcode); ActionButton(actionBar, "Kolonlar", ConfigureColumns); ActionButton(actionBar, "Yenile (F5)", Refresh); filterBar.Children.Add(new TextBlock { Text = "Ara: ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }); filterBar.Children.Add(search); filterBar.Children.Add(brandFilter); filterBar.Children.Add(categoryFilter); filterBar.Children.Add(typeFilter); filterBar.Children.Add(activeFilter); filterBar.Children.Add(negative); filterBar.Children.Add(belowMinimum); previous.Click += (_, _) => { if (page > 1) { page--; Refresh(); } }; next.Click += (_, _) => { page++; Refresh(); }; filterBar.Children.Add(previous); filterBar.Children.Add(pageLabel); filterBar.Children.Add(next); KeyboardInteractionService.AttachDebouncedSearch(search, () => { page = 1; Refresh(); }); brandFilter.SelectionChanged += (_, _) => { page = 1; Refresh(); }; categoryFilter.SelectionChanged += (_, _) => { page = 1; Refresh(); }; typeFilter.SelectionChanged += (_, _) => { page = 1; Refresh(); }; activeFilter.SelectionChanged += (_, _) => { page = 1; Refresh(); }; negative.Checked += (_, _) => { page = 1; Refresh(); }; negative.Unchecked += (_, _) => { page = 1; Refresh(); }; belowMinimum.Checked += (_, _) => { page = 1; Refresh(); }; belowMinimum.Unchecked += (_, _) => { page = 1; Refresh(); }; KeyboardInteractionService.AttachListShortcuts(root, search, () => Edit(true), () => Edit(false), Refresh); root.Children.Add(grid); Refresh(); return root;
        });
    }
    // Shared by "Barkod Sorgulama" (Stok > Barkod) and "Hızlı Barkod Sorgulama" (Stok quick screens) -
    // one screen, one query path (LocalBarcodeResolver), per the sprint's "duplicate business logic
    // yazma" rule.
    private void OpenBarcodeLookup() => OpenTab("Barkod Sorgulama", () =>
    {
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = "Barkod / Ürün Kodu Sorgulama", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(54, 54, 54)) });
        root.Children.Add(new TextBlock { Text = "Barkod okuyucuyla okutun veya ürün kodunu yazıp Enter'a basın.", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 3, 0, 12) });
        var input = new TextBox { Width = 320, HorizontalAlignment = HorizontalAlignment.Left, FontSize = 13, Padding = new Thickness(7, 5, 7, 5) };
        root.Children.Add(input);
        var error = new TextBlock { Foreground = Brushes.Firebrick, Margin = new Thickness(0, 8, 0, 0) }; root.Children.Add(error);
        var resultPanel = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(224, 224, 224)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(16), Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 340 };
        var resultText = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, LineHeight = 20 }; resultPanel.Child = resultText; root.Children.Add(resultPanel);
        string Company() => (_workspaceContext.CompanyId == Guid.Empty ? _db!.Query("SELECT id FROM companies WHERE is_active=1 ORDER BY code LIMIT 1").Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() : _workspaceContext.CompanyId.ToString()) ?? string.Empty;
        var resolver = new LocalBarcodeResolver(_db!);
        void Resolve()
        {
            error.Text = ""; resultPanel.Visibility = Visibility.Collapsed;
            if (string.IsNullOrWhiteSpace(input.Text)) return;
            try
            {
                var r = resolver.Resolve(input.Text, Company());
                if (r == null) { error.Text = "Barkod veya ürün kodu boş olamaz."; return; }
                var stock = _db!.Query("SELECT COALESCE(SUM(quantity_on_hand),0),COALESCE(SUM(quantity_available),0) FROM inventory_balances WHERE product_id=$p AND ($v IS NULL OR variant_id=$v)", ("$p", (object)r.ProductId), ("$v", (object?)r.VariantId ?? DBNull.Value)).Rows[0];
                resultText.Text = $"Ürün Kodu: {r.ProductCode}\nÜrün Adı: {r.ProductName}\nVaryant: {r.VariantName ?? "—"}\nBirim: {r.UnitName}   •   Çarpan: {r.QuantityFactor:N2}\nBarkod: {(r.IsPrimary ? "Ana barkod" : "Ek barkod")}\nDurum: {(r.ProductIsActive ? "Ürün aktif" : "Ürün PASİF")}{(r.VariantId != null && !r.VariantIsActive ? "  •  Varyant pasif" : "")}{(!r.BarcodeIsActive ? "  •  Barkod pasif" : "")}\n\nMevcut Stok: {Convert.ToDecimal(stock[0]):N2}\nKullanılabilir: {Convert.ToDecimal(stock[1]):N2}";
                resultPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { error.Text = ex.Message; }
        }
        input.KeyDown += (_, e) => { if (e.Key != System.Windows.Input.Key.Enter) return; Resolve(); input.SelectAll(); };
        root.Loaded += (_, _) => input.Focus();
        return root;
    });
    private void OpenQuickProductLookup() => OpenTab("Hızlı Ürün Sorgulama", () =>
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var left = new DockPanel { Width = 480 }; DockPanel.SetDock(left, Dock.Left); root.Children.Add(left);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(bar, Dock.Top); left.Children.Add(bar);
        bar.Children.Add(new TextBlock { Text = "Ara: ", VerticalAlignment = VerticalAlignment.Center }); var search = new TextBox { Width = 280, Padding = new Thickness(8) }; bar.Children.Add(search);
        var grid = Table(); foreach (var column in new[] { ("Kod", "Code"), ("Ad", "Name"), ("Marka", "Brand"), ("Kategori", "Category"), ("Tip", "ProductType") }) Column(grid, column.Item1, column.Item2);
        left.Children.Add(grid);
        var detail = new Border { Margin = new Thickness(14, 0, 0, 0), Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(224, 224, 224)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(20), VerticalAlignment = VerticalAlignment.Top };
        var detailText = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 21, Text = "Listeden bir stok kartı seçin." }; detail.Child = detailText; root.Children.Add(detail);
        string Company() => (_workspaceContext.CompanyId == Guid.Empty ? _db!.Query("SELECT id FROM companies WHERE is_active=1 ORDER BY code LIMIT 1").Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() : _workspaceContext.CompanyId.ToString()) ?? string.Empty;
        var lookup = new LocalProductLookupService(_db!);
        void Refresh() { var table = lookup.Search(Company(), search.Text); foreach (DataRow row in table.Rows) row["ProductType"] = R3.Desktop.Presentation.InventoryPresentation.ProductTypeLabel(row["ProductType"].ToString()!); grid.ItemsSource = table.DefaultView; }
        void ShowSelected()
        {
            if (grid.SelectedItem is not DataRowView row) return;
            var productId = row["ProductId"].ToString()!;
            var stock = _db!.Query("SELECT COALESCE(SUM(quantity_on_hand),0),COALESCE(SUM(quantity_reserved),0),COALESCE(SUM(quantity_available),0) FROM inventory_balances WHERE product_id=$p", ("$p", (object)productId)).Rows[0];
            var barcode = _db!.Query("SELECT barcode FROM product_barcodes WHERE product_id=$p AND is_active=1 ORDER BY is_primary DESC LIMIT 1", ("$p", (object)productId));
            detailText.Text = $"Ürün Kodu: {row["Code"]}\nÜrün Adı: {row["Name"]}\nMarka: {row["Brand"]}\nKategori: {row["Category"]}\nTip: {row["ProductType"]}\nAna Barkod: {(barcode.Rows.Count > 0 ? barcode.Rows[0][0] : "—")}\n\nMevcut Stok: {Convert.ToDecimal(stock[0]):N2}\nRezerve: {Convert.ToDecimal(stock[1]):N2}\nKullanılabilir: {Convert.ToDecimal(stock[2]):N2}";
        }
        grid.SelectionChanged += (_, _) => ShowSelected();
        KeyboardInteractionService.AttachDebouncedSearch(search, Refresh);
        Refresh(); return root;
    });
    private void OpenWarehouseManagement()
    {
        OpenTab("Depo Yönetimi", () =>
        {
            var root = new DockPanel { Margin = new Thickness(18) }; var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar); var grid = Table();
            foreach (var item in new[] { ("DepoKodu", "Depo Kodu"), ("DepoAdi", "Depo Adı"), ("Firma", "Şirket"), ("Sube", "Şube"), ("DepoTipi", "Depo Tipi"), ("UrunSayisi", "Ürün Sayısı"), ("ToplamStok", "Toplam Stok") }) Column(grid, item.Item2, item.Item1);
            foreach (var item in new[] { ("VarsayilanGiris", "Varsayılan Giriş"), ("VarsayilanCikis", "Varsayılan Çıkış"), ("LokasyonZorunlu", "Lokasyon Zorunlu"), ("NegatifStok", "Negatif Stok"), ("Aktif", "Aktif") }) BoolColumn(grid, item.Item2, item.Item1);
            var service = new LocalWarehouseService(_db!); var company = CurrentCompanyId();
            void Refresh() { grid.ItemsSource = service.Search(company).DefaultView; }
            void New() { OpenMasterCrud("warehouses", "Yeni Depo"); Refresh(); }
            void Edit() { if (grid.SelectedItem is not DataRowView) { MessageBox.Show(this, "Önce bir depo seçin."); return; } OpenMasterCrud("warehouses", "Depo Düzenle"); Refresh(); }
            void Locations() { if (grid.SelectedItem is DataRowView row) OpenWarehouseLocations(row["Id"].ToString()); else OpenWarehouseLocations(); }
            void Selected(Action<string> open) { if (grid.SelectedItem is DataRowView row) open(row["Id"].ToString()!); }
            ErpGridContext.Register(grid, "inventory.warehouses", StandardContextActions.Warehouses(Locations, () => Selected(id => OpenInventoryBalance(id)), () => Selected(id => OpenInventoryMovements(id)), Edit), () => { Refresh(); return Task.CompletedTask; }, "Warehouse");
            ActionButton(bar, "+ Yeni Depo", New); ActionButton(bar, "Düzenle", Edit); ActionButton(bar, "Lokasyonları Yönet", Locations); ActionButton(bar, "Yenile", Refresh); KeyboardInteractionService.AttachListShortcuts(root, null, New, Edit, Refresh); grid.MouseDoubleClick += (_, _) => Locations(); root.Children.Add(grid); Refresh(); return root;
        });
    }

    private void OpenReservations() => OpenTab("Stok Rezervasyonları", () =>
        InventoryControlViews.Reservations(_db!, CurrentCompanyId(), CurrentBranchId(), CurrentWarehouseId(), _startupSession!.PermissionUserName, OpenProductCard));

    private void OpenNegativeStockPolicy() => OpenTab("Negatif Stok Politikası", () =>
        InventoryControlViews.NegativeStockPolicy(_db!, CurrentCompanyId(), _startupSession!.PermissionUserName));

    private void OpenProductLedger(string? productId = null) => OpenTab(productId == null ? "Ürün Ekstresi" : $"Ürün Ekstresi • {ProductName(productId)}", () =>
        InventoryReportViews.ProductLedger(_db!, CurrentCompanyId(), productId, OpenProductCard));

    private void OpenStockValuation() => OpenTab("Stok Değer Raporu", () =>
        InventoryReportViews.StockValuation(_db!, CurrentCompanyId(), OpenProductCard, id => OpenProductLedger(id)));

    private string ProductName(string productId) =>
        _db!.Query("SELECT code FROM products WHERE id=$id", ("$id", productId)).Rows.Cast<DataRow>().FirstOrDefault()?[0]?.ToString() ?? productId[..Math.Min(8, productId.Length)];

    private void OpenPriceLists() => OpenTab("Fiyat Listeleri", () =>
        PriceViews.PriceLists(_db!, CurrentCompanyId(), _startupSession!.PermissionUserName, id => OpenProductPrices(id)));

    // One Ürün Fiyatları tab (the list is chosen inside it); "Toplu Fiyat Güncelleme" is its Toplu Güncelle button.
    private void OpenProductPrices(string? priceListId = null) => OpenTab("Ürün Fiyatları", () =>
        PriceViews.ProductPrices(_db!, CurrentCompanyId(), _startupSession!.PermissionUserName, priceListId, OpenProductCard, id => OpenPriceHistory(id)));

    private void OpenCampaignPrices() => OpenTab("Kampanya Fiyatları", () =>
        PriceViews.PriceLists(_db!, CurrentCompanyId(), _startupSession!.PermissionUserName, id => OpenProductPrices(id), campaigns: true));

    private void OpenCustomerPriceGroups() => OpenTab("Müşteri Fiyat Grupları", () =>
        PriceViews.CustomerPriceGroups(_db!, CurrentCompanyId(), _startupSession!.PermissionUserName, () => OpenMasterCrud("account_groups", "Cari Grup Tanımları")));

    private void OpenPriceHistory(string? productId = null) => OpenTab(productId == null ? "Fiyat Değişiklik Geçmişi" : $"Fiyat Geçmişi • {ProductName(productId)}", () =>
        PriceViews.PriceHistory(_db!, CurrentCompanyId(), productId));

    // One Barkod Yazdırma tab holds the print queue; opening it again for another product adds to that queue.
    private void OpenLabelPrint(string? productId = null)
    {
        var existing = DocumentsPane.Children.OfType<LayoutDocument>().FirstOrDefault(d => d.Title == "Barkod Yazdırma");
        if (existing?.Content is Border { Child: LabelPrintView view } && productId != null) { view.Add(productId); existing.IsActive = true; return; }
        OpenTab("Barkod Yazdırma", () => new LabelPrintView(_db!, CurrentCompanyId(), productId));
    }

    private void OpenProductBulk() => OpenTab("Toplu Ürün İşlemleri", () => new ProductBulkView(_db!, CurrentCompanyId(), _startupSession!.PermissionUserName));

    private void OpenReturns(ReturnDirection direction) => OpenTab(direction == ReturnDirection.Sales ? "Satış İadeleri" : "Satınalma İadeleri", () =>
        ReturnViews.Returns(_db!, CurrentCompanyId(), direction, _startupSession!.PermissionUserName, OpenAccountCard,
            id => { if (direction == ReturnDirection.Sales) OpenSourceDocument("SalesInvoice", id); else OpenPurchaseDocuments("Invoice"); }));

    private void OpenLotTracking(bool trace = false) => OpenTab("Lot / Seri Takip", () => new LotTrackingView(_db!, CurrentCompanyId(), OpenProductCard, trace));

    private void OpenWarehouseLocations(string? warehouseId = null)
    {
        OpenTab("Depo Lokasyonları", () =>
        {
            var root = new DockPanel { Margin = new Thickness(18) }; var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar); var grid = Table(); foreach (var item in new[] { ("LokasyonKodu", "Lokasyon Kodu"), ("LokasyonAdi", "Lokasyon Adı"), ("LokasyonTipi", "Tip"), ("Koridor", "Koridor"), ("Raf", "Raf"), ("Bolme", "Bölme"), ("Goz", "Göz"), ("Barkod", "Barkod"), ("Kapasite", "Kapasite"), ("MevcutStok", "Mevcut Stok"), ("Aktif", "Aktif") }) Column(grid, item.Item2, item.Item1);
            var service = new LocalWarehouseService(_db!); var selectedWarehouse = warehouseId ?? (_workspaceContext.WarehouseId == Guid.Empty ? CurrentWarehouseId() : _workspaceContext.WarehouseId.ToString());
            void Refresh() { grid.ItemsSource = service.Locations(selectedWarehouse).DefaultView; }
            void New() { var dialog = new MasterRecordDialog("Yeni Lokasyon", "warehouses", null) { Owner = this }; if (dialog.ShowDialog() == true) { try { service.SaveLocation(new WarehouseLocationEdit("", selectedWarehouse, dialog.Code, dialog.NameValue), _startupSession?.UserName ?? Environment.UserName); Refresh(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Lokasyon kaydedilemedi"); } } }
            void Edit() { if (grid.SelectedItem is not DataRowView row) { MessageBox.Show(this, "Önce bir lokasyon seçin."); return; } var table = new DataTable(); table.Columns.Add("Id"); table.Columns.Add("Kod"); table.Columns.Add("Ad"); table.Columns.Add("DepoTipi"); table.Columns.Add("Aktif", typeof(bool)); var r = table.NewRow(); r["Id"] = row["Id"]; r["Kod"] = row["LokasyonKodu"]; r["Ad"] = row["LokasyonAdi"]; r["DepoTipi"] = row["LokasyonTipi"]; r["Aktif"] = Convert.ToBoolean(row["Aktif"]); table.Rows.Add(r); var dialog = new MasterRecordDialog("Lokasyon Düzenle", "warehouses", table.DefaultView[0]) { Owner = this }; if (dialog.ShowDialog() == true) { try { service.SaveLocation(new WarehouseLocationEdit(row["Id"].ToString()!, selectedWarehouse, dialog.Code, dialog.NameValue, dialog.Extra, IsActive: dialog.ActiveValue), _startupSession?.UserName ?? Environment.UserName); Refresh(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Lokasyon güncellenemedi"); } } }
            ActionButton(bar, "+ Yeni Lokasyon", New); ActionButton(bar, "Düzenle", Edit); ActionButton(bar, "Yenile", Refresh); KeyboardInteractionService.AttachListShortcuts(root, null, New, Edit, Refresh); root.Children.Add(grid); Refresh(); return root;
        });
    }

    private void OpenInventoryBalance() => OpenInventoryBalance(null);
    private void OpenInventoryBalance(string? warehouseId) => OpenTab(warehouseId == null ? "Stok Durumu" : $"Stok Durumu • {RecordName("warehouses", warehouseId)}", () =>
    {
        var root = new DockPanel { Margin = new Thickness(18) }; var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar); var search = new TextBox { Width = 220, Padding = new Thickness(8), ToolTip = "Ürün kodu ara" }; var grid = Table(); foreach (var key in new[] { "Depo", "Urun", "Varyant", "Mevcut", "Rezerve", "Kullanilabilir", "MinStok", "MaxStok", "StokUyari", "PolitikaKaynagi", "SonHareket" }) Column(grid, key, key); async void Refresh() { try { grid.ItemsSource = (await _inventory!.SearchBalancesAsync(warehouseId ?? (_workspaceContext.WarehouseId == Guid.Empty ? null : _workspaceContext.WarehouseId.ToString()), search.Text)).DefaultView; } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Stok durumu"); } } void OpenSelectedProduct() { if (grid.SelectedItem is DataRowView row && row["UrunId"]?.ToString() is { Length: > 0 } id) OpenProductCard(id); } ErpGridContext.Register(grid, "inventory.balances", StandardContextActions.ProductLink(OpenSelectedProduct), () => { Refresh(); return Task.CompletedTask; }, "InventoryBalance"); ActionButton(bar, "Yenile (F5)", Refresh); bar.Children.Add(new TextBlock { Text = "Ara: ", VerticalAlignment = VerticalAlignment.Center }); bar.Children.Add(search); KeyboardInteractionService.AttachDebouncedSearch(search, Refresh); KeyboardInteractionService.AttachListShortcuts(root, search, null, null, Refresh); root.Children.Add(grid); Refresh(); return root;
    });
    private void OpenInventoryMovements() => OpenInventoryMovements(null);
    private void OpenInventoryMovements(string? warehouseId) => OpenTab(warehouseId == null ? "Stok Hareketleri" : $"Stok Hareketleri • {RecordName("warehouses", warehouseId)}", () => { var root = new DockPanel { Margin = new Thickness(18) }; var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar); var search = new TextBox { Width = 240, Padding = new Thickness(8) }; var grid = Table(); foreach (var key in new[] { "Tarih", "Referans", "Urun", "Varyant", "Sube", "Depo", "Tur", "Miktar", "BirimMaliyet", "Korelasyon", "Aciklama" }) Column(grid, key, key); async void Refresh() { var table = await _inventory!.SearchMovementsAsync(warehouseId ?? (_workspaceContext.WarehouseId == Guid.Empty ? null : _workspaceContext.WarehouseId.ToString()), search.Text); foreach (DataRow row in table.Rows) row["Tur"] = R3.Desktop.Presentation.InventoryPresentation.TransactionTypeLabel(row["Tur"].ToString()!); grid.ItemsSource = table.DefaultView; } void OpenSelectedProduct() { if (grid.SelectedItem is DataRowView row && row["UrunId"]?.ToString() is { Length: > 0 } id) OpenProductCard(id); } ErpGridContext.Register(grid, "inventory.movements", StandardContextActions.ProductLink(OpenSelectedProduct), () => { Refresh(); return Task.CompletedTask; }, "InventoryMovement"); ActionButton(bar, "Yenile (F5)", Refresh); bar.Children.Add(search); KeyboardInteractionService.AttachDebouncedSearch(search, Refresh); KeyboardInteractionService.AttachListShortcuts(root, search, null, null, Refresh); root.Children.Add(grid); Refresh(); return root; });
     private void OpenInventoryTransfers()
     {
         OpenTab("Depo Transferleri", () =>
         {
             var root = new DockPanel { Margin = new Thickness(22), Background = new SolidColorBrush(Color.FromRgb(247, 249, 251)) }; var head = ScreenHeader("Depolar Arası Transfer", "Kaynak depo ve lokasyondan hedef depo ve lokasyona güvenli stok aktarımı"); DockPanel.SetDock(head, Dock.Top); root.Children.Add(head); var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar); var grid = Table();
             foreach (var item in new[] { ("TransferNo", "Transfer No"), ("Tarih", "Tarih"), ("KaynakDepo", "Kaynak Depo"), ("KaynakLokasyon", "Kaynak Lokasyon"), ("HedefDepo", "Hedef Depo"), ("HedefLokasyon", "Hedef Lokasyon"), ("SatirSayisi", "Satır"), ("TemelMiktar", "Miktar"), ("Durum", "Durum"), ("Olusturan", "Oluşturan"), ("Onaylayan", "Onaylayan") }) Column(grid, item.Item2, item.Item1);
             var company = _workspaceContext.CompanyId == Guid.Empty ? (_db!.Query("SELECT id FROM companies WHERE is_active=1 ORDER BY code LIMIT 1").Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() ?? string.Empty) : _workspaceContext.CompanyId.ToString(); var service = new LocalInventoryTransferService(_db!);
             void Refresh() { var table = service.Search(company); foreach (DataRow row in table.Rows) row["Durum"] = R3.Desktop.Presentation.InventoryPresentation.StatusLabel(row["Durum"].ToString()!); grid.ItemsSource = table.DefaultView; }
             void New() { OpenInventoryOperation("Depo Transfer"); Refresh(); }
             void Approve() { if (grid.SelectedItem is not DataRowView row) { MessageBox.Show(this, "Önce bir transfer seçin."); return; } try { service.ApproveAsync(row["Id"].ToString()!, _startupSession?.UserName ?? Environment.UserName).GetAwaiter().GetResult(); Refresh(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Transfer onaylanamadı"); } }
             ActionButton(bar, "+ Yeni Transfer", New); ActionButton(bar, "Onayla", Approve); ActionButton(bar, "Yenile", Refresh); KeyboardInteractionService.AttachListShortcuts(root, null, New, null, Refresh); root.Children.Add(grid); Refresh(); return root;
         });
     }

     private void OpenInventoryDocuments(string title, string documentType)
    {
        OpenTab(title, () =>
        {
            var root = new DockPanel { Margin = new Thickness(22), Background = new SolidColorBrush(Color.FromRgb(247, 249, 251)) }; var head = ScreenHeader(documentType == "ManualIn" ? "Stok Giriş Fişleri" : "Stok Çıkış Fişleri", documentType == "ManualIn" ? "Depoya alınan stok hareketlerini yönetin" : "Depodan çıkan stok hareketlerini yönetin"); DockPanel.SetDock(head, Dock.Top); root.Children.Add(head); var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar); var grid = Table();
            foreach (var item in new[] { ("FisNo", "Fiş No"), ("FisTuru", "Fiş Türü"), ("Tarih", "Tarih"), ("Durum", "Durum"), ("Sube", "Şube"), ("Depo", "Depo"), ("LokasyonSayisi", "Lokasyon"), ("SatirSayisi", "Satır"), ("TemelMiktar", "Temel Miktar"), ("Olusturan", "Oluşturan"), ("Onaylayan", "Onaylayan"), ("Aciklama", "Açıklama") }) Column(grid, item.Item2, item.Item1);
            string company = _workspaceContext.CompanyId == Guid.Empty ? (_db!.Query("SELECT id FROM companies WHERE is_active=1 ORDER BY code LIMIT 1").Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() ?? string.Empty) : _workspaceContext.CompanyId.ToString(); var documents = new LocalInventoryDocumentService(_db!);
             void Refresh() { var table = documents.Search(company, documentType); foreach (DataRow row in table.Rows) { row["Durum"] = R3.Desktop.Presentation.InventoryPresentation.StatusLabel(row["Durum"].ToString()!); row["FisTuru"] = R3.Desktop.Presentation.InventoryPresentation.DocumentTypeLabel(row["FisTuru"].ToString()!); } grid.ItemsSource = table.DefaultView; }
            void New() { OpenInventoryOperation(documentType == "ManualIn" ? "Stok Giriş" : "Stok Çıkış"); Refresh(); }
            void OpenSelected() { if (grid.SelectedItem is DataRowView row) OpenInventoryDocumentDetail(row["Id"].ToString()!, row["FisNo"].ToString()!, documents); else MessageBox.Show(this, "Önce bir fiş seçin."); }
            void Approve() { if (grid.SelectedItem is not DataRowView row) { MessageBox.Show(this, "Önce bir fiş seçin."); return; } try { documents.ApproveAsync(row["Id"].ToString()!, _startupSession?.UserName ?? Environment.UserName).GetAwaiter().GetResult(); Refresh(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Fiş onaylanamadı"); } }
            void Reverse() { if (grid.SelectedItem is not DataRowView row) { MessageBox.Show(this, "Önce bir fiş seçin."); return; } try { documents.ReverseAsync(row["Id"].ToString()!, _startupSession?.UserName ?? Environment.UserName).GetAwaiter().GetResult(); Refresh(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Ters hareket oluşturulamadı"); } }
            ActionButton(bar, "+ Yeni Fiş", New); ActionButton(bar, "Aç / Detay", OpenSelected); ActionButton(bar, "Onayla", Approve); ActionButton(bar, "Ters Hareket", Reverse); ActionButton(bar, "Yenile", Refresh); KeyboardInteractionService.AttachListShortcuts(root, null, New, OpenSelected, Refresh); root.Children.Add(grid); Refresh(); return root;
        });
    }

    private void OpenInventoryDocumentDetail(string documentId, string documentNo, LocalInventoryDocumentService documents)
    {
        OpenTab($"Stok Fişi • {documentNo}", () => { var root = new DockPanel { Margin = new Thickness(18) }; var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar); var grid = Table(); foreach (var item in new[] { ("Satır", "Satır"), ("StokKodu", "Stok Kodu"), ("StokAdi", "Stok Adı"), ("Varyant", "Varyant"), ("Birim", "Birim"), ("Miktar", "Miktar"), ("TemelMiktar", "Temel Miktar"), ("Lokasyon", "Lokasyon"), ("BirimMaliyet", "Birim Maliyet"), ("Lot", "Lot"), ("Seri", "Seri No"), ("SonKullanma", "Son Kullanma") }) Column(grid, item.Item2, item.Item1); ActionButton(bar, "Yenile", () => grid.ItemsSource = documents.Details(documentId).DefaultView); root.Children.Add(grid); grid.ItemsSource = documents.Details(documentId).DefaultView; return root; });
    }

    private void OpenInventoryOperation(string kind) { if (_inventory == null) return; string company = _workspaceContext.CompanyId == Guid.Empty ? (_db!.Query("SELECT id FROM companies WHERE is_active=1 ORDER BY code LIMIT 1").Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() ?? string.Empty) : _workspaceContext.CompanyId.ToString(); string branch = _workspaceContext.BranchId == Guid.Empty ? (_db!.Query("SELECT id FROM branches WHERE company_id=$c AND is_active=1 ORDER BY code LIMIT 1", ("$c", (object)company)).Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() ?? string.Empty) : _workspaceContext.BranchId.ToString(); string warehouse = _workspaceContext.WarehouseId == Guid.Empty ? (_db!.Query("SELECT id FROM warehouses WHERE branch_id=$b AND is_active=1 ORDER BY code LIMIT 1", ("$b", (object)branch)).Rows.Cast<DataRow>().FirstOrDefault()?["id"]?.ToString() ?? string.Empty) : _workspaceContext.WarehouseId.ToString(); var dialog = new InventoryOperationDialog(kind, _inventory, _db!, company, branch, warehouse) { Owner = this }; if (dialog.ShowDialog() == true) MessageBox.Show(this, kind is "Stok Giriş" or "Stok Çıkış" ? "Fiş taslak olarak kaydedildi. Stok miktarı henüz değişmedi." : "İşlem kaydedildi.", kind); }
    private void OpenWebCenter() => OpenTab("Web Merkezi", () => new EmbeddedBrowserView());
    private void Safe(Action action)
    {
        try { if (_db == null) throw new InvalidOperationException("Veritabanı kullanılamıyor."); action(); }
        catch (SqliteException ex) { MessageBox.Show(this, ex.SqliteErrorCode == 19 ? "Kayıt kaydedilemedi. Kod benzersiz olmalı; müşteri ve mağaza seçimi geçerli olmalıdır." : ex.Message, "Veritabanı"); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "İşlem tamamlanamadı"); }
    }
    // AvalonDock document management (2026-09-21). Every screen still goes through this one OpenTab()
    // choke point, so none of the ~30 call sites elsewhere in this file needed to change - only what
    // OpenTab() does internally. HomeDocument (CanClose="False" in XAML) is never pushed onto
    // _closedTabs/never matched by these Close* helpers, mirroring the old Tag-based exclusion.
    private void OpenTab(string title, Func<UIElement> content)
    {
        Safe(() =>
        {
            var existing = DocumentsPane.Children.OfType<LayoutDocument>().FirstOrDefault(d => d.Title == title);
            if (existing != null) { existing.IsActive = true; return; }
            // AvalonDock's default Aero template renders its own close button when
            // CanClose is true. We use one shared R3 close button in the header
            // template instead, so document tabs never show two overlapping X icons.
            // The Aero theme also paints its own light-blue gradient behind the document
            // content area; none of the ~30 screens built through this choke point set a
            // root background of their own, so that blue always showed through. Wrapping
            // every tab's content in one opaque Border here (instead of patching each
            // screen) replaces it with the app's neutral gray canvas everywhere at once.
            var document = new LayoutDocument { Title = title, ContentId = title, Content = new Border { Background = (Brush)FindResource("R3.Background.Brush"), Child = content() }, CanClose = false, CanFloat = false };
            DocumentsPane.Children.Add(document); document.IsActive = true;
        });
    }

    private void DocumentHeaderClose_Click(object sender, RoutedEventArgs e)
    {
        var model = (sender as FrameworkElement)?.DataContext;
        var document = model switch
        {
            AvalonDock.Controls.LayoutItem item => item.Model as LayoutDocument,
            LayoutDocument layoutDocument => layoutDocument,
            _ => null
        };
        if (document != null && document != HomeDocument) CloseTab(document);
        e.Handled = true;
    }

    private void CloseCurrentTab_Click(object sender, RoutedEventArgs e)
    {
        var active = DocumentsPane.Children.OfType<LayoutDocument>().FirstOrDefault(d => d.IsActive);
        if (active != null) CloseTab(active);
    }

    private void CloseTab(LayoutDocument document)
    {
        if (document == HomeDocument || !DocumentsPane.Children.Contains(document)) return;
        _closedTabs.Push(document); DocumentsPane.Children.Remove(document);
        var remaining = DocumentsPane.Children.OfType<LayoutDocument>().FirstOrDefault();
        if (remaining != null && !DocumentsPane.Children.OfType<LayoutDocument>().Any(d => d.IsActive)) remaining.IsActive = true;
    }

    private void CloseTabsToLeft_Click(object sender, RoutedEventArgs e)
    {
        var docs = DocumentsPane.Children.OfType<LayoutDocument>().ToList(); var selected = docs.FirstOrDefault(d => d.IsActive); if (selected == null) return;
        var selectedIndex = docs.IndexOf(selected);
        foreach (var document in docs.Where(d => d != HomeDocument && docs.IndexOf(d) < selectedIndex).ToList()) CloseTab(document);
    }

    private void CloseTabsToRight_Click(object sender, RoutedEventArgs e)
    {
        var docs = DocumentsPane.Children.OfType<LayoutDocument>().ToList(); var selected = docs.FirstOrDefault(d => d.IsActive); if (selected == null) return;
        var selectedIndex = docs.IndexOf(selected);
        foreach (var document in docs.Where(d => d != HomeDocument && docs.IndexOf(d) > selectedIndex).ToList()) CloseTab(document);
    }

    private void CloseOtherTabs_Click(object sender, RoutedEventArgs e)
    {
        var docs = DocumentsPane.Children.OfType<LayoutDocument>().ToList(); var selected = docs.FirstOrDefault(d => d.IsActive);
        foreach (var document in docs.Where(d => d != HomeDocument && d != selected).ToList()) CloseTab(document);
    }

    private void CloseAllTabs_Click(object sender, RoutedEventArgs e)
    {
        foreach (var document in DocumentsPane.Children.OfType<LayoutDocument>().Where(d => d != HomeDocument).ToList()) CloseTab(document);
        HomeDocument.IsActive = true;
    }

    private void ReopenClosedTab_Click(object sender, RoutedEventArgs e)
    {
        if (_closedTabs.Count == 0) return; var document = _closedTabs.Pop(); DocumentsPane.Children.Add(document); document.IsActive = true;
    }

    private void CopyTabTitle_Click(object sender, RoutedEventArgs e)
    {
        var active = DocumentsPane.Children.OfType<LayoutDocument>().FirstOrDefault(d => d.IsActive);
        if (active != null) Clipboard.SetText(active.Title);
    }

    private void GoHome_Click(object sender, RoutedEventArgs e) => HomeDocument.IsActive = true;

    private static Button ActionButton(Panel panel, string text, Action action)
    {
        var b = new Button
        {
            Content = text, Height = 23, MinWidth = 70, Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 5, 0), Background = new SolidColorBrush(Color.FromRgb(244, 244, 244)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(198, 198, 198)), Foreground = new SolidColorBrush(Color.FromRgb(53, 53, 53)),
            FontSize = 10.5, FontWeight = FontWeights.Medium
        };
        b.Click += (_, _) => action(); panel.Children.Add(b); return b;
    }
    private static StackPanel ScreenHeader(string title, string subtitle)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(37, 67, 82)), Margin = new Thickness(0, 0, 0, 3) });
        panel.Children.Add(new TextBlock { Text = subtitle, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(102, 119, 128)), Margin = new Thickness(0, 0, 0, 10) });
        var rule = new Border { Height = 2, Background = new SolidColorBrush(Color.FromRgb(42, 133, 163)), HorizontalAlignment = HorizontalAlignment.Stretch }; panel.Children.Add(rule); return panel;
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
    private static readonly Style CenteredCellText = new(typeof(TextBlock)) { Setters = { new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center) } };
    private static void BoolColumn(DataGrid grid, string title, string path) => grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path) { Converter = new BoolToCheckGlyphConverter() }, Width = new DataGridLength(90), ElementStyle = CenteredCellText });
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
        var summary = new TextBlock { Padding = new Thickness(14), Background = new SolidColorBrush(Color.FromRgb(241, 241, 241)), FontWeight = FontWeights.SemiBold, Text = "Ekstre için müşteri seçin." };
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
