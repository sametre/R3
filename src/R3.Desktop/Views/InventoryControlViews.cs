using System.Data;
using R3.Desktop.Design;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using R3.Desktop.ContextActions;
using R3.Desktop.Presentation;
using R3.Infrastructure;

namespace R3.Desktop.Views;

/// <summary>Stok › İzleme ve Ayarlar screens: Stok Rezervasyonları and Negatif Stok Politikası.</summary>
public static class InventoryControlViews
{
    private static readonly Brush Muted = Ui.Brush("R3.Text.Secondary.Brush");
    private static readonly Brush Accent = Ui.Brush("R3.Accent.Brush");

    public static UIElement Reservations(StoreDatabase db, string companyId, string branchId, string defaultWarehouseId, string userName, Action<string> openProduct)
    {
        var service = new LocalReservationService(db);
        var root = Shell("Stok Rezervasyonları", "Rezerve edilen miktar kullanılabilir stoktan düşer; stok çıkışları rezerve stoğu kullanamaz. Süresi dolan rezervasyonlar ekran her yenilendiğinde otomatik serbest bırakılır.", out var bar);
        var status = new ComboBox { Width = 130, Height = 26, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedIndex = 0,
            ItemsSource = new[] { new Choice("Active", "Aktif"), new Choice("", "Tümü"), new Choice("Released", "Serbest bırakılan"), new Choice("Consumed", "Teslim edilen"), new Choice("Expired", "Süresi dolan") } };
        var search = new TextBox { Width = 220, Padding = new Thickness(6, 3, 6, 3), ToolTip = "Ürün, referans veya cari ara" };
        var grid = Grid();
        Col(grid, "Tarih", "Tarih", 140); Col(grid, "Depo", "Depo", 130); Col(grid, "Ürün", "Urun", 260); Col(grid, "Miktar", "Miktar", 80, "N2");
        Col(grid, "Cari", "Cari", 180); Col(grid, "Referans", "Referans", 110); Col(grid, "Son Tarih", "SonTarih", 140); Col(grid, "Durum", "Durum", 100); Col(grid, "Açıklama", "Aciklama", 200);

        string? SelectedId() => (grid.SelectedItem as DataRowView)?["Id"].ToString();
        void Refresh()
        {
            try { service.ExpireDue(companyId, userName); } catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { }
            var table = service.Search(companyId, null, status.SelectedValue as string, search.Text);
            foreach (DataRow row in table.Rows)
            {
                row["Durum"] = InventoryPresentation.ReservationStatusLabel(row["Durum"].ToString()!);
                row["Tarih"] = LocalTime(row["Tarih"]); row["SonTarih"] = LocalTime(row["SonTarih"]);
            }
            grid.ItemsSource = table.DefaultView;
        }
        void New()
        {
            var dialog = new ReservationDialog(db, companyId, branchId, defaultWarehouseId) { Owner = Window.GetWindow(root) };
            if (dialog.ShowDialog() != true || dialog.Result == null) return;
            if (Try(root, () => service.Create(dialog.Result, userName), "Rezervasyon oluşturulamadı")) Refresh();
        }
        void Close(string newStatus)
        {
            if (SelectedId() is not { } id) { Info(root, "Önce bir rezervasyon seçin."); return; }
            if (Try(root, () => service.Close(id, newStatus, userName), "Rezervasyon kapatılamadı")) Refresh();
        }
        bool IsActive(object? x) => x is DataRowView row && row["Durum"].ToString() == "Aktif";
        ErpGridContext.Register(grid, "inventory.reservations",
        [
            new ContextActionDefinition("reservation.product", "Ürün Kartını Aç", "inventory.product.view", "", 10, ContextActionGroup.Primary, x => { if (x is DataRowView r) openProduct(r["UrunId"].ToString()!); return Task.FromResult(ContextActionResult.Ok()); }),
            new ContextActionDefinition("reservation.consume", "Teslim Edildi Olarak Kapat", "orders.release_reservation", "", 10, ContextActionGroup.Operational, _ => { Close("Consumed"); return Task.FromResult(ContextActionResult.Ok()); }, IsActive, IsActive),
            new ContextActionDefinition("reservation.release", "Rezervasyonu Serbest Bırak", "orders.release_reservation", "", 10, ContextActionGroup.Critical, _ => { Close("Released"); return Task.FromResult(ContextActionResult.Ok()); }, IsActive, IsActive, RequiresConfirmation: true,
                ConfirmationText: x => $"Rezervasyon serbest bırakılacak ve miktarı kullanılabilir stoğa dönecek.\n\n{(x as DataRowView)?["Urun"]} — {(x as DataRowView)?["Miktar"]:N2}")
        ], () => { Refresh(); return Task.CompletedTask; }, "InventoryReservation");

        Button(bar, "+ Yeni Rezervasyon (F2)", New, true); Button(bar, "Teslim Edildi", () => Close("Consumed")); Button(bar, "Serbest Bırak", () => Close("Released")); Button(bar, "Yenile (F5)", Refresh);
        bar.Children.Add(new TextBlock { Text = "Durum:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) }); bar.Children.Add(status);
        bar.Children.Add(new TextBlock { Text = "Ara:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) }); bar.Children.Add(search);
        status.SelectionChanged += (_, _) => Refresh();
        KeyboardInteractionService.AttachDebouncedSearch(search, Refresh);
        KeyboardInteractionService.AttachListShortcuts(root, search, New, null, Refresh);
        root.Children.Add(grid); Refresh();
        return root;
    }

    public static UIElement NegativeStockPolicy(StoreDatabase db, string companyId, string userName)
    {
        var service = new LocalWarehouseService(db);
        var root = Shell("Negatif Stok Politikası", "Negatif stoğa izin verilen depolarda, kullanılabilir stok yetersiz olsa da stok çıkışı, satış faturası, transfer ve stok fişi kaydedilebilir; bakiye eksiye düşer. İzin verilmeyen depolarda bu işlemler \"Yetersiz stok\" ile reddedilir. Rezervasyonlar her durumda mevcut stok gerektirir.", out var bar);
        var grid = Grid();
        Col(grid, "Depo Kodu", "DepoKodu", 110); Col(grid, "Depo Adı", "DepoAdi", 220); Col(grid, "Şube", "Sube", 160); Col(grid, "Depo Tipi", "DepoTipi", 110);
        Col(grid, "Negatif Stok", "NegatifDurum", 140); Col(grid, "Eksideki Ürün", "EksiUrun", 110);
        void Refresh()
        {
            var table = service.Search(companyId);
            table.Columns.Add("NegatifDurum", typeof(string)); table.Columns.Add("EksiUrun", typeof(long));
            foreach (DataRow row in table.Rows)
            {
                row["NegatifDurum"] = Convert.ToInt64(row["NegatifStok"]) == 1 ? "İzin veriliyor" : "Engelli";
                row["EksiUrun"] = Convert.ToInt64(db.Query("SELECT COUNT(*) FROM inventory_balances WHERE warehouse_id=$w AND quantity_on_hand<0", ("$w", row["Id"])).Rows[0][0]);
            }
            grid.ItemsSource = table.DefaultView;
        }
        void Set(bool allow)
        {
            if (grid.SelectedItem is not DataRowView row) { Info(root, "Önce bir depo seçin."); return; }
            if (allow && MessageBox.Show(Window.GetWindow(root)!, $"{row["DepoAdi"]} deposunda negatif stoğa izin verilecek. Stok çıkışları kullanılabilir miktarı aşabilecek.\n\nDevam edilsin mi?", "Negatif Stok Politikası", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (Try(root, () => service.SetNegativeStockPolicy(companyId, row["Id"].ToString()!, allow, userName), "Politika değiştirilemedi")) Refresh();
        }
        Button(bar, "Negatif Stoğa İzin Ver", () => Set(true)); Button(bar, "Negatif Stoğu Engelle", () => Set(false), true); Button(bar, "Yenile (F5)", Refresh);
        ErpGridContext.Register(grid, "inventory.negativestock", [], () => { Refresh(); return Task.CompletedTask; }, "Warehouse");
        KeyboardInteractionService.AttachListShortcuts(root, null, null, null, Refresh);
        root.Children.Add(grid); Refresh();
        return root;
    }

    private static string LocalTime(object value) =>
        DateTime.TryParse(value?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : value?.ToString() ?? "";

    private static DockPanel Shell(string title, string help, out StackPanel bar)
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var header = new TextBlock { Text = title, FontSize = Ui.Font.Title, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Text.Primary.Brush") };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var note = new TextBlock { Text = help, Foreground = Muted, FontSize = Ui.Font.Caption, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) };
        DockPanel.SetDock(note, Dock.Top); root.Children.Add(note);
        bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        return root;
    }

    private static DataGrid Grid() => new()
    {
        Style = (Style)System.Windows.Application.Current.FindResource("ProfessionalDataGridStyle"),
        IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column
    };

    private static void Col(DataGrid grid, string header, string path, double width, string? format = null) =>
        grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path) { StringFormat = format, ConverterCulture = CultureInfo.GetCultureInfo("tr-TR") }, Width = width });

    private static void Button(Panel bar, string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, Height = 26, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        if (primary) { button.Background = Accent; button.Foreground = Ui.Brush("R3.Text.OnAccent.Brush"); button.BorderThickness = new Thickness(0); }
        button.Click += (_, _) => action(); bar.Children.Add(button);
    }

    private static void Info(FrameworkElement owner, string message) => MessageBox.Show(Window.GetWindow(owner)!, message, "Stok", MessageBoxButton.OK, MessageBoxImage.Information);

    private static bool Try(FrameworkElement owner, Action action, string title)
    {
        try { action(); return true; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        { MessageBox.Show(Window.GetWindow(owner)!, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
    }

    private sealed record Choice(string Id, string Name);
}

public sealed class ReservationDialog : EditorDialog
{
    public ReservationEdit? Result { get; private set; }

    public ReservationDialog(StoreDatabase db, string companyId, string branchId, string defaultWarehouseId) : base("Yeni Stok Rezervasyonu")
    {
        Width = 480;
        var resolver = new LocalBarcodeResolver(db);
        var warehouses = db.Query("SELECT id AS Id, code || ' — ' || name AS Display, branch_id AS BranchId FROM warehouses WHERE company_id=$c AND is_active=1 ORDER BY code", ("$c", companyId));
        var warehouse = Field("Depo *", new ComboBox { ItemsSource = warehouses.DefaultView, DisplayMemberPath = "Display", SelectedValuePath = "Id", SelectedValue = defaultWarehouseId });
        if (warehouse.SelectedIndex < 0 && warehouses.Rows.Count > 0) warehouse.SelectedIndex = 0;
        var barcode = Field("Barkod / ürün kodu (Enter)", new TextBox { MaxLength = 80 });
        var products = db.Query("SELECT id AS Id, code || ' — ' || name AS Display FROM products WHERE company_id=$c AND is_active=1 AND product_type<>'Service' ORDER BY code", ("$c", companyId));
        var product = Field("Ürün *", new ComboBox { ItemsSource = products.DefaultView, DisplayMemberPath = "Display", SelectedValuePath = "Id", IsTextSearchEnabled = true, IsEditable = true });
        var availability = new TextBlock { Foreground = Ui.Brush("R3.Info.Brush"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) }; Fields.Children.Add(availability);
        string? variantId = null;
        void ShowAvailability()
        {
            if (warehouse.SelectedValue is not string w || product.SelectedValue is not string p) { availability.Text = ""; return; }
            var row = db.Query("SELECT quantity_on_hand, quantity_reserved, quantity_available FROM inventory_balances WHERE warehouse_id=$w AND product_id=$p AND (variant_id=$v OR (variant_id IS NULL AND $v IS NULL))",
                ("$w", w), ("$p", p), ("$v", (object?)variantId ?? DBNull.Value)).Rows.Cast<DataRow>().FirstOrDefault();
            availability.Text = row == null ? "Bu depoda stok yok." : $"Mevcut: {Convert.ToDecimal(row[0]):N2}   •   Rezerve: {Convert.ToDecimal(row[1]):N2}   •   Kullanılabilir: {Convert.ToDecimal(row[2]):N2}";
        }
        barcode.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return; e.Handled = true;
            try { var r = resolver.ResolveForInventory(barcode.Text, companyId); product.SelectedValue = r.ProductId; variantId = r.VariantId; Error.Text = ""; ShowAvailability(); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { Error.Text = ex.Message; }
        };
        product.SelectionChanged += (_, _) => { variantId = null; ShowAvailability(); };
        warehouse.SelectionChanged += (_, _) => ShowAvailability();
        var quantity = Field("Rezerve miktar *", new TextBox { Text = "1", Tag = "Numeric" });
        var accounts = db.Query("SELECT '' AS Id, '(Cari seçilmedi)' AS Ad UNION ALL SELECT id, code || ' — ' || name FROM accounts WHERE company_id=$c AND is_active=1 AND account_type IN ('Customer','CustomerAndSupplier')", ("$c", companyId));
        var account = Field("Müşteri (isteğe bağlı)", new ComboBox { ItemsSource = accounts.DefaultView, DisplayMemberPath = "Ad", SelectedValuePath = "Id", SelectedIndex = 0, IsTextSearchEnabled = true, IsEditable = true });
        var reference = Field("Referans (sipariş / teklif no)", new TextBox { MaxLength = 50 });
        var expires = Field("Son geçerlilik tarihi (boş = süresiz)", new DatePicker());
        var description = Field("Açıklama", new TextBox { MaxLength = 300 });
        Finish(() =>
        {
            if (warehouse.SelectedValue is not string w) throw new ArgumentException("Depo seçin.");
            if (product.SelectedValue is not string p) throw new ArgumentException("Ürün seçin.");
            if (!decimal.TryParse(quantity.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("tr-TR"), out var q) || q <= 0) throw new ArgumentException("Miktar 0'dan büyük bir sayı olmalıdır.");
            var branch = warehouses.Rows.Cast<DataRow>().FirstOrDefault(r => r["Id"].ToString() == w)?["BranchId"].ToString() ?? branchId;
            Result = new ReservationEdit(companyId, branch, w, p, variantId, q, account.SelectedValue as string, reference.Text, description.Text,
                expires.SelectedDate is { } d ? d.Date.AddDays(1).AddSeconds(-1) : null);
        });
        Loaded += (_, _) => { barcode.Focus(); ShowAvailability(); };
    }
}
