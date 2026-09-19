using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using R3.Desktop.Presentation;
using R3.Desktop.ViewModels;
using R3.Infrastructure;

namespace R3.Desktop.Views;

// Phase 8: "Fatura Taslağı -> Faturayı Kes -> UBL Oluştur -> Gönder -> Durumu takip et" as one
// screen. All state and every action live in InvoiceDetailViewModel (WPF-free, unit tested); this
// class only renders that state and calls the ViewModel's commands - it never decides a business
// status or an action's availability itself (§44).
internal static class InvoiceDetailView
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly Brush Muted = Brush("#667986");
    private static readonly Brush BorderBrush = Brush("#D6E0E6");

    public static UIElement Create(StoreDatabase database, string invoiceId, string userId)
    {
        var vm = new InvoiceDetailViewModel(database, invoiceId, userId);
        vm.Load();

        var root = new DockPanel { Margin = new Thickness(18) };

        // --- header ---
        var header = new Border { Background = Brushes.White, BorderBrush = BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(header, Dock.Top);
        var headerGrid = new Grid(); headerGrid.ColumnDefinitions.Add(new ColumnDefinition()); headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headerLeft = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        var docNo = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brush("263746") };
        var salesBadge = Badge();
        titleRow.Children.Add(docNo); titleRow.Children.Add(salesBadge);
        headerLeft.Children.Add(titleRow);
        var accountLine = new TextBlock { Foreground = Muted, FontSize = 13, Margin = new Thickness(0, 4, 0, 0) };
        headerLeft.Children.Add(accountLine);
        var status = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), FontSize = 12 };
        headerLeft.Children.Add(status);
        headerGrid.Children.Add(headerLeft);

        var headerRight = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var eDocBadgeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var eDocTypeBadge = Badge(); var eDocStatusBadge = Badge();
        eDocBadgeRow.Children.Add(eDocTypeBadge); eDocBadgeRow.Children.Add(eDocStatusBadge);
        headerRight.Children.Add(eDocBadgeRow);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var postButton = PrimaryButton("Faturayı Kes");
        var refreshButton = SmallButton("↻  Yenile");
        buttons.Children.Add(postButton); buttons.Children.Add(refreshButton);
        headerRight.Children.Add(buttons);
        Grid.SetColumn(headerRight, 1); headerGrid.Children.Add(headerRight);
        header.Child = headerGrid;
        root.Children.Add(header);

        // --- tabs ---
        var tabs = new TabControl();
        var genelTab = new TabItem { Header = "Genel" }; var urunlerTab = new TabItem { Header = "Ürünler" }; var tutarlarTab = new TabItem { Header = "Tutarlar" };
        var eBelgeTab = new TabItem { Header = "E-Belge" }; var cariTab = new TabItem { Header = "Cari Hareketi" }; var stokTab = new TabItem { Header = "Stok Hareketleri" }; var gecmisTab = new TabItem { Header = "Geçmiş" };
        foreach (var t in new[] { genelTab, urunlerTab, tutarlarTab, eBelgeTab, cariTab, stokTab, gecmisTab }) tabs.Items.Add(t);
        root.Children.Add(tabs);

        void RefreshAll()
        {
            docNo.Text = vm.DocumentNo;
            SetBadge(salesBadge, EDocumentPresentation.SalesStatusLabel(vm.SalesStatus), EDocumentPresentation.SalesStatusColor(vm.SalesStatus));
            accountLine.Text = $"{vm.AccountCode} — {vm.AccountName}  •  {vm.BranchName} / {vm.WarehouseName}  •  {vm.DocumentDate:dd.MM.yyyy}";
            status.Text = vm.IsBusy ? vm.BusyText : vm.ErrorMessage ?? vm.StatusMessage ?? "";
            status.Foreground = vm.IsBusy ? Brush("2E6F95") : (vm.ErrorMessage != null ? Brushes.Firebrick : Brush("2A8F7B"));

            if (vm.EDocType is { } type) SetBadge(eDocTypeBadge, EDocumentPresentation.TypeLabel(type), "#2E6F95"); else eDocTypeBadge.Visibility = Visibility.Collapsed;
            if (vm.EDocStatus is { } eStatus) SetBadge(eDocStatusBadge, EDocumentPresentation.StatusLabel(eStatus), EDocumentPresentation.StatusColor(eStatus)); else eDocStatusBadge.Visibility = Visibility.Collapsed;

            postButton.Visibility = vm.IsDraft ? Visibility.Visible : Visibility.Collapsed;
            postButton.IsEnabled = vm.PostCommand.CanExecute(null);
            refreshButton.IsEnabled = !vm.IsBusy;

            genelTab.Content = BuildGenel(vm);
            urunlerTab.Content = BuildUrunler(vm);
            tutarlarTab.Content = BuildTutarlar(vm);
            eBelgeTab.Content = BuildEBelge(vm, RefreshAll);
            cariTab.Content = BuildGridTab(vm.AccountTransactions, ("Tarih", "Tarih", 140d), ("Tur", "Tür", 140d), ("Borc", "Borç", 110d), ("Alacak", "Alacak", 110d), ("Aciklama", "Açıklama", 260d));
            stokTab.Content = BuildGridTab(vm.InventoryTransactions, ("Tarih", "Tarih", 140d), ("Urun", "Ürün", 260d), ("Tur", "Tür", 120d), ("Miktar", "Miktar", 100d), ("Depo", "Depo", 140d));
            gecmisTab.Content = BuildGridTab(vm.AuditHistory, ("Tarih", "Tarih", 150d), ("Islem", "İşlem", 200d), ("Detay", "Detay", 420d));
        }

        postButton.Click += async (_, _) =>
        {
            if (MessageBox.Show(Window.GetWindow(root), $"Fatura kesilecek.\n\n{vm.DocumentNo}\n{vm.AccountName}\n\nGenel Toplam\n{vm.GrandTotal:C2}\n\nBu işlem cari bakiyesini ve stok hareketlerini oluşturacaktır.",
                "Faturayı Kes", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await vm.PostCommand.ExecuteAsync(null);
            RefreshAll();
            if (vm.ErrorMessage != null)
                MessageBox.Show(Window.GetWindow(root), vm.ErrorMessage, "Fatura kesilemedi", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
            {
                var stockNote = vm.InventoryTransactions.Count > 0 ? "Oluşturuldu" : "Hizmet kalemleri - stok hareketi yok";
                MessageBox.Show(Window.GetWindow(root),
                    $"Fatura başarıyla kesildi.\n\nCari Hareketi\nOluşturuldu\n\nStok Hareketleri\n{stockNote}\n\nE-Belge\n{(vm.EDocStatus is { } s ? EDocumentPresentation.StatusLabel(s) : "-")}",
                    "Fatura kesildi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        refreshButton.Click += (_, _) => { vm.Load(); RefreshAll(); };

        RefreshAll();
        return root;
    }

    private static UIElement BuildGenel(InvoiceDetailViewModel vm)
    {
        var panel = new StackPanel { Margin = new Thickness(18) };
        var grid = new Grid(); for (var i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(150) : new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Info(grid, 0, 0, "Fatura No", vm.DocumentNo); Info(grid, 0, 2, "Fatura Tarihi", vm.DocumentDate.ToString("dd.MM.yyyy", Turkish));
        Info(grid, 1, 0, "Cari", $"{vm.AccountCode} — {vm.AccountName}"); Info(grid, 1, 2, "Fatura Durumu", EDocumentPresentation.SalesStatusLabel(vm.SalesStatus));
        Info(grid, 2, 0, "Şube / Depo", $"{vm.BranchName} / {vm.WarehouseName}"); Info(grid, 2, 2, "Açıklama", string.IsNullOrWhiteSpace(vm.Description) ? "—" : vm.Description);
        panel.Children.Add(grid);
        if (!vm.IsDraft) panel.Children.Add(new TextBlock { Text = "Bu fatura kesildi. Cari, tarih, ürün, miktar, fiyat ve vergi alanları artık düzenlenemez; düzeltme için iptal/iade akışı kullanılmalıdır.", Foreground = Brush("A0752E"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) });
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static UIElement BuildUrunler(InvoiceDetailViewModel vm)
    {
        var grid = Grid_();
        Columns(grid, ("ProductCode", "Stok Kodu", 100d), ("ProductName", "Ürün", 220d), ("Quantity", "Miktar", 80d), ("UnitCode", "Birim", 70d),
            ("UnitPrice", "Birim Fiyat", 100d), ("DiscountRate", "İskonto %", 80d), ("VatRate", "KDV %", 70d), ("NetAmount", "Net", 100d), ("LineTotal", "Toplam", 100d));
        grid.ItemsSource = vm.Lines;
        return grid;
    }

    private static UIElement BuildTutarlar(InvoiceDetailViewModel vm)
    {
        var panel = new StackPanel { Margin = new Thickness(18) };
        var cards = new WrapPanel();
        cards.Children.Add(Card("Ara Toplam", vm.Subtotal.ToString("C2", Turkish), "#2E6F95"));
        cards.Children.Add(Card("İskonto", vm.DiscountTotal.ToString("C2", Turkish), "#C0832B"));
        cards.Children.Add(Card("KDV", vm.TaxTotal.ToString("C2", Turkish), "#75639A"));
        cards.Children.Add(Card("Genel Toplam", vm.GrandTotal.ToString("C2", Turkish), "#2A8F7B"));
        cards.Children.Add(Card("Cari Bakiyesi", vm.AccountBalance.ToString("C2", Turkish), vm.AccountBalance > 0 ? "#C4514B" : "#2A8F7B"));
        panel.Children.Add(cards);
        return panel;
    }

    private static UIElement BuildGridTab(DataView view, params (string Key, string Header, double Width)[] columns)
    {
        var grid = Grid_(); Columns(grid, columns); grid.ItemsSource = view;
        return grid;
    }

    private static UIElement BuildEBelge(InvoiceDetailViewModel vm, Action refresh)
    {
        var panel = new StackPanel { Margin = new Thickness(4) };
        if (vm.ElectronicDocumentId == null)
        {
            panel.Children.Add(new TextBlock { Text = "Bu fatura henüz kesilmedi; elektronik belge fatura kesildiğinde otomatik oluşturulur.", Foreground = Muted, TextWrapping = TextWrapping.Wrap });
            return panel;
        }

        var card = new Border { Background = Brushes.White, BorderBrush = BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(16) };
        var grid = new Grid(); for (var i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(150) : new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Info(grid, 0, 0, "Belge Tipi", vm.EDocType is { } t ? EDocumentPresentation.TypeLabel(t) : "—");
        Info(grid, 0, 2, "Durum", vm.EDocStatus is { } s ? EDocumentPresentation.StatusLabel(s) : "—");
        Info(grid, 1, 0, "UUID", vm.Uuid ?? "—"); Info(grid, 1, 2, "Provider Belge No", vm.ProviderDocumentId ?? "—");
        Info(grid, 2, 0, "Gönderim Denemesi", vm.SendAttemptCount.ToString(Turkish)); Info(grid, 2, 2, "Sonraki Deneme", vm.NextRetryAt?.ToString("dd.MM.yyyy HH:mm", Turkish) ?? "—");
        Info(grid, 3, 0, "Son Hata Kodu", vm.LastErrorCode ?? "—"); Info(grid, 3, 2, "Son Hata", vm.LastErrorMessage ?? "—");
        card.Child = grid; panel.Children.Add(card);

        if (vm.EDocStatus == ElectronicDocumentStatus.Failed && !string.IsNullOrWhiteSpace(vm.LastErrorMessage))
        {
            panel.Children.Add(new Border { Background = Brush("#FBEEEE"), BorderBrush = Brush("#E7C6C6"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12), Margin = new Thickness(0, 10, 0, 0), Child = new TextBlock { Text = $"Gönderim başarısız.\n\n{vm.LastErrorMessage}\n\nDeneme: {vm.SendAttemptCount}", Foreground = Brush("#8A3A3A"), TextWrapping = TextWrapping.Wrap } });
        }

        var actions = EDocumentPresentation.ActionsFor(vm.EDocStatus ?? ElectronicDocumentStatus.Draft);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        if (actions.CanGenerate) ActionBtn(bar, "UBL Oluştur", vm.GenerateCommand.CanExecute(null), async () => { await vm.GenerateCommand.ExecuteAsync(null); AfterAction(vm, refresh); });
        if (actions.CanQueue) ActionBtn(bar, "Gönderim Kuyruğuna Al", vm.QueueCommand.CanExecute(null), async () => { await vm.QueueCommand.ExecuteAsync(null); AfterAction(vm, refresh); });
        if (actions.CanSend) ActionBtn(bar, "Şimdi Gönder", vm.SendCommand.CanExecute(null), async () => { await vm.SendCommand.ExecuteAsync(null); AfterAction(vm, refresh); });
        if (actions.CanQueryStatus) ActionBtn(bar, "Durumu Sorgula", vm.QueryStatusCommand.CanExecute(null), async () => { await vm.QueryStatusCommand.ExecuteAsync(null); AfterAction(vm, refresh); });
        if (actions.CanRetry) ActionBtn(bar, "Tekrar Dene", vm.RetryCommand.CanExecute(null), async () => { await vm.RetryCommand.ExecuteAsync(null); AfterAction(vm, refresh); });
        if (actions.CanViewXml && vm.CanViewPayload) ActionBtn(bar, "XML Görüntüle", vm.XmlPayload != null, () => ElectronicDocumentDialogs.ShowXmlViewer(vm.Payloads));
        if (actions.CanViewProviderResponse && vm.CanViewProviderResponse) ActionBtn(bar, "Provider Yanıtı", vm.Document != null, () => ElectronicDocumentDialogs.ShowProviderResponse(vm.Document!, vm.Payloads));
        panel.Children.Add(bar);

        if (vm.EDocStatus is ElectronicDocumentStatus.Sending) panel.Children.Add(new TextBlock { Text = "Gönderim işleniyor…", Foreground = Brush("#C0832B"), Margin = new Thickness(0, 8, 0, 0) });

        panel.Children.Add(new TextBlock { Text = "Olay Geçmişi", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush("2B5870"), Margin = new Thickness(0, 18, 0, 8) });
        panel.Children.Add(ElectronicDocumentDialogs.BuildEventTimeline(vm.Events));
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static void AfterAction(InvoiceDetailViewModel vm, Action refresh)
    {
        refresh();
        if (vm.ErrorMessage != null) MessageBox.Show(vm.ErrorMessage, "İşlem tamamlanamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // --- small UI helpers (kept local to this file; deliberately not a dependency on LegacyAlignedViews) ---
    private static void Info(Grid grid, int row, int column, string label, string value)
    {
        var caption = new TextBlock { Text = label, Foreground = Muted, FontSize = 11, Margin = new Thickness(6, 6, 9, 2) }; Grid.SetRow(caption, row); Grid.SetColumn(caption, column); grid.Children.Add(caption);
        var text = new TextBlock { Text = value, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 6, 12, 2), TextWrapping = TextWrapping.Wrap }; Grid.SetRow(text, row); Grid.SetColumn(text, column + 1); grid.Children.Add(text);
    }
    private static Border Card(string caption, string value, string color)
    {
        var body = new StackPanel(); body.Children.Add(new TextBlock { Text = caption, Foreground = Muted, FontSize = 11 });
        body.Children.Add(new TextBlock { Text = value, Foreground = Brush(color), FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0) });
        return new Border { Child = body, MinWidth = 170, Margin = new Thickness(0, 0, 10, 8), Padding = new Thickness(13, 10, 13, 10), Background = Brushes.White, BorderBrush = BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7) };
    }
    private static Border Badge()
    {
        return new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold } };
    }
    private static void SetBadge(Border badge, string text, string color) { badge.Visibility = Visibility.Visible; badge.Background = Brush(color); ((TextBlock)badge.Child).Text = text; }
    private static Button PrimaryButton(string text) => new() { Content = text, Height = 32, Padding = new Thickness(14, 6, 14, 6), Background = Brush("2A8F7B"), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) };
    private static Button SmallButton(string text) => new() { Content = text, Height = 29, Padding = new Thickness(10, 3, 10, 3), Background = Brushes.White, BorderBrush = BorderBrush, Foreground = Brush("344955") };
    private static void ActionBtn(Panel bar, string text, bool enabled, Action click) { var b = SmallButton(text); b.IsEnabled = enabled; b.Margin = new Thickness(0, 0, 8, 0); b.Click += (_, _) => click(); bar.Children.Add(b); }
    private static DataGrid Grid_() => new() { Style = (Style)System.Windows.Application.Current.FindResource("ProfessionalDataGridStyle"), AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, MinHeight = 200, HeadersVisibility = DataGridHeadersVisibility.Column };
    private static void Columns(DataGrid grid, params (string Key, string Header, double Width)[] columns) { foreach (var c in columns) grid.Columns.Add(new DataGridTextColumn { Header = c.Header, Binding = new Binding(c.Key) { ConverterCulture = Turkish }, Width = new DataGridLength(c.Width) }); }
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
