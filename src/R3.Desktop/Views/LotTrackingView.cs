using System.Data;
using R3.Desktop.Design;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using R3.Desktop.ContextActions;
using R3.Infrastructure;

namespace R3.Desktop.Views;

/// <summary>
/// Stok › İzleme ve Ayarlar › Lot / Seri Takip: lot balances (with SKT warnings), serial numbers, a trace
/// of every movement for one lot/serial, and Takip Uyumu - tracked products whose stock is not covered by
/// lots/serials because it came through a flow that does not carry lots yet.
/// </summary>
public sealed class LotTrackingView : DockPanel
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public LotTrackingView(StoreDatabase db, string companyId, Action<string> openProduct, bool startOnTrace = false)
    {
        var service = new LocalLotService(db);
        Margin = new Thickness(18);
        var title = new TextBlock { Text = "Lot / Seri Takip", FontSize = Ui.Font.Title, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Text.Primary.Brush") };
        SetDock(title, Dock.Top); Children.Add(title);
        var help = new TextBlock { Text = "Takip türü ürün kartında (Stok & Sipariş › Lot takibi) seçilir. Lot/seri takipli ürünlerde Stok Giriş/Çıkış fişleri lot veya seri numarası olmadan kaydedilmez; lotlar eksiye düşmez. Fatura, transfer ve iadeler henüz lot taşımadığından bunlarla hareket eden miktar Takip Uyumu sekmesinde 'atanmamış' görünür.", Foreground = Ui.Brush("R3.Text.Secondary.Brush"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) };
        SetDock(help, Dock.Top); Children.Add(help);
        var tabs = new TabControl(); Children.Add(tabs);

        // Lotlar
        {
            var root = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var bar = Bar(root);
            var search = new TextBox { Width = 200, Padding = new Thickness(6, 3, 6, 3), ToolTip = "Ürün kodu/adı veya lot no" };
            var expiring = new ComboBox { Width = 190, Height = 26, ItemsSource = new[] { "Tüm lotlar", "SKT 30 gün içinde", "SKT 90 gün içinde", "SKT'si geçmiş" }, SelectedIndex = 0 };
            var withStock = new CheckBox { Content = "Yalnızca bakiyesi olanlar", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            Label(bar, "Ara:"); bar.Children.Add(search); Label(bar, "SKT:"); bar.Children.Add(expiring); bar.Children.Add(withStock);
            var grid = Grid(("Stok Kodu", "StokKodu", 120, null), ("Stok Adı", "StokAdi", 240, null), ("Lot No", "LotNo", 120, null), ("SKT", "Skt", 95, null), ("Kalan Gün", "KalanGun", 80, "N0"), ("Depo", "Depo", 140, null), ("Miktar", "Miktar", 90, "N2"));
            // Expired / close-to-expiry lots stand out without opening a filter.
            grid.LoadingRow += (_, e) =>
            {
                e.Row.Background = e.Row.Item is DataRowView r && r["KalanGun"] is int days
                    ? days < 0 ? Ui.Brush("R3.Danger.Soft.Brush") : days <= 30 ? Ui.Brush("R3.Warning.Soft.Brush") : Ui.Brush("R3.Surface.Brush")
                    : Brushes.White;
            };
            void Refresh()
            {
                int? within = expiring.SelectedIndex switch { 1 => 30, 2 => 90, 3 => -1, _ => null };
                var table = service.Lots(companyId, search.Text, within, withStock.IsChecked == true);
                grid.ItemsSource = table.DefaultView;
            }
            expiring.SelectionChanged += (_, _) => Refresh(); withStock.Click += (_, _) => Refresh();
            KeyboardInteractionService.AttachDebouncedSearch(search, Refresh);
            Register(grid, "inventory.lots", openProduct, Refresh);
            root.Children.Add(grid); Refresh();
            tabs.Items.Add(new TabItem { Header = "Lotlar", Content = root });
        }
        // Seri numaraları
        {
            var root = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var bar = Bar(root);
            var search = new TextBox { Width = 200, Padding = new Thickness(6, 3, 6, 3), ToolTip = "Seri no, ürün kodu/adı" };
            var inStock = new CheckBox { Content = "Yalnızca depodakiler", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            Label(bar, "Ara:"); bar.Children.Add(search); bar.Children.Add(inStock);
            var grid = Grid(("Stok Kodu", "StokKodu", 120, null), ("Stok Adı", "StokAdi", 240, null), ("Seri No", "SeriNo", 150, null), ("Durum", "Durum", 110, null), ("Depo", "Depo", 140, null), ("Lot No", "LotNo", 110, null));
            void Refresh() => grid.ItemsSource = service.Serials(companyId, search.Text, inStock.IsChecked == true).DefaultView;
            inStock.Click += (_, _) => Refresh();
            KeyboardInteractionService.AttachDebouncedSearch(search, Refresh);
            Register(grid, "inventory.serials", openProduct, Refresh);
            root.Children.Add(grid); Refresh();
            tabs.Items.Add(new TabItem { Header = "Seri Numaraları", Content = root });
        }
        // İzle
        {
            var root = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var bar = Bar(root);
            var key = new TextBox { Width = 220, Padding = new Thickness(6, 3, 6, 3), ToolTip = "Lot no veya seri no yazıp Enter" };
            var result = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Foreground = Ui.Brush("R3.Text.Secondary.Brush") };
            Label(bar, "Lot / Seri No:"); bar.Children.Add(key);
            var grid = Grid(("Tarih", "TarihYerel", 130, null), ("Stok Kodu", "StokKodu", 110, null), ("Stok Adı", "StokAdi", 200, null), ("İşlem", "TurAdi", 130, null), ("Depo", "Depo", 130, null),
                ("Miktar", "Miktar", 80, "N2"), ("Lot No", "LotNo", 100, null), ("Seri No", "SeriNo", 140, null), ("Referans", "Referans", 120, null));
            void Trace()
            {
                if (string.IsNullOrWhiteSpace(key.Text)) return;
                var table = service.Trace(companyId, key.Text);
                table.Columns.Add("TarihYerel", typeof(string)); table.Columns.Add("TurAdi", typeof(string));
                foreach (DataRow r in table.Rows)
                {
                    r["TarihYerel"] = DateTime.TryParse(r["Tarih"].ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : r["Tarih"];
                    r["TurAdi"] = R3.Desktop.Presentation.InventoryPresentation.TransactionTypeLabel(r["Tur"].ToString()!);
                }
                grid.ItemsSource = table.DefaultView;
                result.Text = table.Rows.Count == 0 ? "Bu numarayla hareket bulunamadı." : $"{table.Rows.Count} hareket";
            }
            key.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Trace(); e.Handled = true; } };
            Button(bar, "İzle", Trace); bar.Children.Add(result);
            ErpGridContext.Register(grid, "inventory.lottrace", [], null, "InventoryTransaction");
            root.Children.Add(grid);
            tabs.Items.Add(new TabItem { Header = "İzle", Content = root });
            if (startOnTrace) { tabs.SelectedIndex = 2; Loaded += (_, _) => key.Focus(); }
        }
        // Takip uyumu
        {
            var root = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var bar = Bar(root);
            var grid = Grid(("Stok Kodu", "StokKodu", 120, null), ("Stok Adı", "StokAdi", 240, null), ("Takip", "Takip", 60, null), ("Toplam Stok", "Stok", 100, "N2"), ("Lot/Seriye Atanan", "Atanan", 120, "N2"), ("Atanmamış", "Atanmamis", 100, "N2"));
            void Refresh() => grid.ItemsSource = service.Coverage(companyId).DefaultView;
            Button(bar, "Yenile", Refresh);
            bar.Children.Add(new TextBlock { Text = "Atanmamış ≠ 0 ise bu ürün lot/seri taşımayan bir akışla (fatura, transfer, iade, açılış) hareket görmüştür.", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Foreground = Ui.Brush("R3.Text.Secondary.Brush") });
            Register(grid, "inventory.lotcoverage", openProduct, Refresh);
            root.Children.Add(grid); Refresh();
            tabs.Items.Add(new TabItem { Header = "Takip Uyumu", Content = root });
        }
    }

    private static void Register(DataGrid grid, string key, Action<string> openProduct, Action refresh) =>
        ErpGridContext.Register(grid, key,
            [new ContextActionDefinition(key + ".product", "Ürün Kartını Aç", "inventory.product.view", "", 10, ContextActionGroup.Primary,
                x => { if (x is DataRowView r && r.Row.Table.Columns.Contains("UrunId")) openProduct(r["UrunId"].ToString()!); return Task.FromResult(ContextActionResult.Ok()); })],
            () => { refresh(); return Task.CompletedTask; }, "InventoryLot");

    private static WrapPanel Bar(DockPanel root) { var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; SetDock(bar, Dock.Top); root.Children.Add(bar); return bar; }
    private static void Label(Panel bar, string text) => bar.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 5, 0) });
    private static void Button(Panel bar, string text, Action action) { var b = new Button { Content = text, Height = 26, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) }; b.Click += (_, _) => action(); bar.Children.Add(b); }

    private static DataGrid Grid(params (string Header, string Path, double Width, string? Format)[] columns)
    {
        var grid = new DataGrid { Style = (Style)System.Windows.Application.Current.FindResource("ProfessionalDataGridStyle"), IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column };
        foreach (var (header, path, width, format) in columns) grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path) { StringFormat = format, ConverterCulture = Turkish }, Width = width });
        return grid;
    }
}
