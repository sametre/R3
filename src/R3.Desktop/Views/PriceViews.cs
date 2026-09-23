using System.Data;
using R3.Desktop.Design;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using R3.Desktop.ContextActions;
using R3.Infrastructure;

namespace R3.Desktop.Views;

/// <summary>Stok › Fiyat Yönetimi screens backed by <see cref="LocalPriceService"/>.</summary>
public static class PriceViews
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly Brush Muted = Ui.Brush("R3.Text.Secondary.Brush");
    private static readonly Brush Accent = Ui.Brush("R3.Accent.Brush");

    public static UIElement PriceLists(StoreDatabase db, string companyId, string userName, Action<string> openPrices, bool campaigns = false)
    {
        var service = new LocalPriceService(db);
        var root = campaigns
            ? Shell("Kampanya Fiyatları", "Kampanya, başlangıç ve bitiş tarihi olan bir satış fiyat listesidir. Tarih aralığındaki kampanya fiyatı, cari ve grup fiyatlarının önüne geçer (birden fazla kampanya varsa en düşük fiyat). Kampanya fiyatına grup iskontosu eklenmez.", out var bar)
            : Shell("Fiyat Listeleri", "Satış ve alış fiyat listeleri. KDV dahil listelerde girilen fiyat KDV'yi içerir. Geçerlilik tarihi dışındaki veya pasif listeler fiyat vermez.", out bar);
        var grid = Grid();
        Col(grid, "Kod", "Kod", 100); Col(grid, "Ad", "Ad", 200); Col(grid, "Tip", "TipAdi", 70); Col(grid, "KDV", "KdvAdi", 70); Col(grid, "Döviz", "ParaBirimi", 60);
        Col(grid, "Başlangıç", "Baslangic", 95); Col(grid, "Bitiş", "Bitis", 95); Col(grid, "Sıra", "Sira", 50); Col(grid, "Fiyatlı Ürün", "UrunSayisi", 90, "N0"); Col(grid, "Durum", "DurumAdi", 70);
        void Refresh()
        {
            var table = service.PriceLists(companyId);
            table.DefaultView.RowFilter = campaigns ? "Kampanya=1" : "";
            foreach (var name in new[] { "TipAdi", "KdvAdi", "DurumAdi" }) table.Columns.Add(name, typeof(string));
            foreach (DataRow r in table.Rows) { r["TipAdi"] = Convert.ToInt64(r["Kampanya"]) == 1 ? "Kampanya" : r["Tip"].ToString() == "Purchase" ? "Alış" : "Satış"; r["KdvAdi"] = Convert.ToInt64(r["KdvDahil"]) == 1 ? "Dahil" : "Hariç"; r["DurumAdi"] = Convert.ToInt64(r["Aktif"]) == 1 ? "Aktif" : "Pasif"; }
            grid.ItemsSource = table.DefaultView;
        }
        void Edit(bool create)
        {
            var row = grid.SelectedItem as DataRowView; if (!create && row == null) { Info(root, "Önce bir fiyat listesi seçin."); return; }
            var dialog = new PriceListDialog(create ? null : service.GetPriceList(row!["Id"].ToString()!), companyId, campaigns) { Owner = Window.GetWindow(root) };
            if (dialog.ShowDialog() == true && dialog.Result != null && Try(root, () => service.SavePriceList(dialog.Result, userName), "Fiyat listesi kaydedilemedi")) Refresh();
        }
        Task SetActive(bool active)
        {
            if (grid.SelectedItem is DataRowView row && service.GetPriceList(row["Id"].ToString()!) is { } list && Try(root, () => service.SavePriceList(list with { IsActive = active }, userName), "Güncellenemedi")) Refresh();
            return Task.CompletedTask;
        }
        void Prices() { if (grid.SelectedItem is DataRowView row) openPrices(row["Id"].ToString()!); }
        ErpGridContext.Register(grid, "pricing.lists",
        [
            Action("pricelist.prices", "Ürün Fiyatlarını Aç", ContextActionGroup.Primary, Prices),
            Action("pricelist.edit", "Listeyi Düzenle", ContextActionGroup.Primary, () => Edit(false), order: 20),
            new ContextActionDefinition("pricelist.activate", "Aktif Yap", "", "", 10, ContextActionGroup.Critical, async _ => { await SetActive(true); return ContextActionResult.Ok(refresh: true); }, x => x is DataRowView r && r["DurumAdi"].ToString() == "Pasif"),
            new ContextActionDefinition("pricelist.deactivate", "Pasife Al", "", "", 20, ContextActionGroup.Critical, async _ => { await SetActive(false); return ContextActionResult.Ok(refresh: true); }, x => x is DataRowView r && r["DurumAdi"].ToString() == "Aktif", RequiresConfirmation: true, ConfirmationText: _ => "Pasif fiyat listesi fiyat vermez. Devam edilsin mi?")
        ], () => { Refresh(); return Task.CompletedTask; }, "PriceList");
        Button(bar, campaigns ? "+ Yeni Kampanya (F2)" : "+ Yeni Liste (F2)", () => Edit(true), true); Button(bar, "Düzenle (F3)", () => Edit(false)); Button(bar, campaigns ? "Kampanya Fiyatları" : "Ürün Fiyatları", Prices);
        Button(bar, "Satış Fiyatı Sorgula", () => new SalesPriceQueryDialog(db, companyId) { Owner = Window.GetWindow(root) }.ShowDialog()); Button(bar, "Yenile (F5)", Refresh);
        KeyboardInteractionService.AttachListShortcuts(root, null, () => Edit(true), () => Edit(false), Refresh);
        root.Children.Add(grid); Refresh();
        return root;
    }

    public static UIElement ProductPrices(StoreDatabase db, string companyId, string userName, string? initialListId, Action<string> openProduct, Action<string?> openHistory)
    {
        var service = new LocalPriceService(db);
        var root = Shell("Ürün Fiyatları", "Fiyat sütununa yazıp Enter'a basınca fiyat hemen kaydedilir ve değişiklik geçmişine işlenir. Boş bırakmak fiyatı listeden kaldırır. En fazla 1000 satır gösterilir; aramayı daraltın.", out var bar);
        var lists = service.PriceLists(companyId, activeOnly: false);
        var list = new ComboBox { Width = 230, Height = 26, ItemsSource = lists.DefaultView, DisplayMemberPath = "Ad", SelectedValuePath = "Id" };
        list.SelectedValue = initialListId ?? (lists.Rows.Count > 0 ? lists.Rows[0]["Id"] : null);
        var groups = db.Query("SELECT '' AS Id, 'Tüm stok grupları' AS Name, '' AS SortKey UNION ALL SELECT id, code || ' — ' || name, code FROM product_groups WHERE company_id=$c AND is_active=1 ORDER BY 3", ("$c", companyId));
        var group = new ComboBox { Width = 200, Height = 26, ItemsSource = groups.DefaultView, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedIndex = 0 };
        var search = new TextBox { Width = 200, Padding = new Thickness(6, 3, 6, 3), ToolTip = "Kod, ad veya barkod" };
        var onlyPriced = new CheckBox { Content = "Yalnızca fiyatı olanlar", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var status = new TextBlock { Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        Label(bar, "Liste:"); bar.Children.Add(list); Label(bar, "Grup:"); bar.Children.Add(group); Label(bar, "Ara:"); bar.Children.Add(search); bar.Children.Add(onlyPriced);

        var grid = Grid(); grid.IsReadOnly = false; grid.CanUserDeleteRows = false;
        Col(grid, "Stok Kodu", "StokKodu", 130); Col(grid, "Stok Adı", "StokAdi", 280); Col(grid, "Stok Grubu", "StokGrubu", 150); Col(grid, "Birim", "Birim", 60); Col(grid, "KDV %", "Kdv", 60, "N0");
        var priceColumn = new DataGridTextColumn { Header = "Fiyat ✎", Binding = new Binding("Fiyat") { StringFormat = "N2", ConverterCulture = Turkish, TargetNullValue = "" }, Width = 110, IsReadOnly = false };
        grid.Columns.Add(priceColumn);
        Col(grid, "Karşılık", "Karsilik", 110, "N2"); Col(grid, "Son Değişiklik", "SonDegisiklikYerel", 130); Col(grid, "Değiştiren", "Degistiren", 100);
        foreach (var column in grid.Columns) if (column != priceColumn) column.IsReadOnly = true;

        bool VatIncluded() => (list.SelectedItem as DataRowView) is { } l && Convert.ToInt64(l["KdvDahil"]) == 1;
        void Refresh()
        {
            if (list.SelectedValue is not string listId) { grid.ItemsSource = null; return; }
            var table = service.ProductPrices(companyId, listId, search.Text, group.SelectedValue as string, onlyPriced.IsChecked == true);
            table.Columns.Add("Karsilik", typeof(decimal)); table.Columns.Add("SonDegisiklikYerel", typeof(string));
            foreach (DataRow r in table.Rows) Decorate(r);
            grid.ItemsSource = table.DefaultView;
            priceColumn.Header = VatIncluded() ? "Fiyat (KDV dahil) ✎" : "Fiyat (KDV hariç) ✎";
            grid.Columns[6].Header = VatIncluded() ? "KDV hariç" : "KDV dahil";
            status.Text = $"{table.Rows.Count:N0} ürün • {table.Rows.Cast<DataRow>().Count(r => r["Fiyat"] != DBNull.Value):N0} fiyatlı";
        }
        void Decorate(DataRow r)
        {
            var vat = Convert.ToDecimal(r["Kdv"]);
            r["Karsilik"] = r["Fiyat"] == DBNull.Value ? DBNull.Value : Math.Round(VatIncluded() ? Convert.ToDecimal(r["Fiyat"]) / (1 + vat / 100m) : Convert.ToDecimal(r["Fiyat"]) * (1 + vat / 100m), 2);
            r["SonDegisiklikYerel"] = DateTime.TryParse(r["SonDegisiklik"].ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "";
        }
        grid.CellEditEnding += (_, e) =>
        {
            if (e.EditAction != DataGridEditAction.Commit || e.Column != priceColumn || e.Row.Item is not DataRowView row || e.EditingElement is not TextBox box || list.SelectedValue is not string listId) return;
            var text = box.Text.Trim(); decimal? price = null;
            if (text.Length > 0)
            {
                if (!decimal.TryParse(text, NumberStyles.Number, Turkish, out var parsed) || parsed < 0) { e.Cancel = true; status.Text = "Geçerli bir fiyat girin (örn. 1.250,90)."; return; }
                price = Math.Round(parsed, 4);
            }
            if (!Try(root, () => service.SetPrice(companyId, listId, row["UrunId"].ToString()!, price, userName), "Fiyat kaydedilemedi")) { e.Cancel = true; return; }
            // The binding writes the typed text into the row after this event; re-read the saved value on the dispatcher so the row shows what is stored.
            grid.Dispatcher.BeginInvoke(() =>
            {
                row.Row["Fiyat"] = (object?)price ?? DBNull.Value; row.Row["SonDegisiklik"] = DateTime.UtcNow.ToString("O"); row.Row["Degistiren"] = userName; Decorate(row.Row);
                status.Text = $"{row["StokKodu"]} fiyatı kaydedildi: {(price is { } p ? p.ToString("N2", Turkish) : "kaldırıldı")}";
            });
        };
        IReadOnlyList<string> VisibleProducts() => (grid.ItemsSource as DataView)?.Cast<DataRowView>().Select(r => r["UrunId"].ToString()!).ToList() ?? [];
        void Bulk()
        {
            if (list.SelectedValue is not string listId) return;
            var dialog = new BulkPriceDialog(lists, listId, VisibleProducts().Count, group.SelectedValue as string ?? "") { Owner = Window.GetWindow(root) };
            if (dialog.ShowDialog() != true || dialog.Request is not { } request) return;
            var filter = request.Scope switch
            {
                "Visible" => new BulkPriceFilter(VisibleProducts(), OnlyExistingPrices: request.OnlyExisting),
                "Group" => new BulkPriceFilter(ProductGroupId: group.SelectedValue as string, OnlyExistingPrices: request.OnlyExisting),
                _ => new BulkPriceFilter(OnlyExistingPrices: request.OnlyExisting)
            };
            var changed = 0;
            if (Try(root, () => changed = service.BulkUpdate(companyId, listId, filter, request.Mode, request.Value, userName, request.SourceListId, request.RoundTo, request.EndWith), "Toplu güncelleme yapılamadı"))
            { Refresh(); Info(root, $"{changed:N0} ürünün fiyatı güncellendi. Değişiklikler Fiyat Değişiklik Geçmişi'nde."); }
        }
        string? SelectedProduct() => (grid.SelectedItem as DataRowView)?["UrunId"].ToString();
        ErpGridContext.Register(grid, "pricing.productprices",
        [
            Action("price.product", "Ürün Kartını Aç", ContextActionGroup.Related, () => { if (SelectedProduct() is { } id) openProduct(id); }),
            Action("price.history", "Fiyat Geçmişi", ContextActionGroup.Related, () => openHistory(SelectedProduct()), order: 20)
        ], () => { Refresh(); return Task.CompletedTask; }, "ProductPrice");
        Button(bar, "Toplu Güncelle", Bulk, true); Button(bar, "Fiyat Geçmişi", () => openHistory(SelectedProduct()));
        Button(bar, "Satış Fiyatı Sorgula", () => new SalesPriceQueryDialog(db, companyId, SelectedProduct()) { Owner = Window.GetWindow(root) }.ShowDialog()); Button(bar, "Yenile (F5)", Refresh); bar.Children.Add(status);
        list.SelectionChanged += (_, _) => Refresh(); group.SelectionChanged += (_, _) => Refresh(); onlyPriced.Click += (_, _) => Refresh();
        KeyboardInteractionService.AttachDebouncedSearch(search, Refresh);
        KeyboardInteractionService.AttachListShortcuts(root, search, null, null, Refresh);
        root.Children.Add(grid); Refresh();
        return root;
    }

    public static UIElement CustomerPriceGroups(StoreDatabase db, string companyId, string userName, Action openAccountGroups)
    {
        var service = new LocalPriceService(db);
        var root = Shell("Müşteri Fiyat Grupları", "Cari grubuna bir satış fiyat listesi ve iskonto atayın. Gruptaki müşteriler, kendilerine özel liste tanımlanmamışsa bu listeden fiyat alır; iskonto fatura satırına yazılır. Kampanya fiyatı grup fiyatının önüne geçer. Carinin grubu Cari Kart'tan seçilir.", out var bar);
        var grid = Grid();
        Col(grid, "Grup Kodu", "Kod", 110); Col(grid, "Grup Adı", "Ad", 200); Col(grid, "Fiyat Listesi", "FiyatListesi", 220); Col(grid, "İskonto %", "Iskonto", 80, "N2"); Col(grid, "Aktif Cari", "CariSayisi", 80, "N0");
        void Refresh() => grid.ItemsSource = service.CustomerPriceGroups(companyId).DefaultView;
        void Edit()
        {
            if (grid.SelectedItem is not DataRowView row) { Info(root, "Önce bir cari grubu seçin."); return; }
            var dialog = new CustomerPriceGroupDialog(db, companyId, row) { Owner = Window.GetWindow(root) };
            if (dialog.ShowDialog() == true && dialog.Result != null && Try(root, () => service.SaveCustomerPriceGroup(companyId, dialog.Result, userName), "Grup kaydedilemedi")) Refresh();
        }
        ErpGridContext.Register(grid, "pricing.customergroups", [Action("pricegroup.edit", "Fiyat Listesi / İskonto Ata", ContextActionGroup.Primary, Edit)], () => { Refresh(); return Task.CompletedTask; }, "AccountGroup");
        Button(bar, "Fiyat Listesi / İskonto Ata (F3)", Edit, true); Button(bar, "Cari Grupları Tanımla", openAccountGroups);
        Button(bar, "Satış Fiyatı Sorgula", () => new SalesPriceQueryDialog(db, companyId) { Owner = Window.GetWindow(root) }.ShowDialog()); Button(bar, "Yenile (F5)", Refresh);
        KeyboardInteractionService.AttachListShortcuts(root, null, null, Edit, Refresh);
        root.Children.Add(grid); Refresh();
        return root;
    }

    public static UIElement PriceHistory(StoreDatabase db, string companyId, string? productId)
    {
        var service = new LocalPriceService(db);
        var root = Shell("Fiyat Değişiklik Geçmişi", "Her fiyat değişikliği (elle, toplu güncelleme veya aktarım) eski/yeni fiyat, kullanıcı ve zamanla kaydedilir. Son 500 değişiklik gösterilir.", out var bar);
        var lists = db.Query("SELECT '' AS Id, 'Tüm listeler' AS Ad, '' AS SortKey UNION ALL SELECT id, code || ' — ' || name, code FROM price_lists WHERE company_id=$c ORDER BY 3", ("$c", companyId));
        var list = new ComboBox { Width = 220, Height = 26, ItemsSource = lists.DefaultView, DisplayMemberPath = "Ad", SelectedValuePath = "Id", SelectedIndex = 0 };
        var products = db.Query("SELECT '' AS Id, 'Tüm ürünler' AS Display, '' AS SortKey UNION ALL SELECT id, code || ' — ' || name, code FROM products WHERE company_id=$c ORDER BY 3", ("$c", companyId));
        var product = new ComboBox { Width = 300, Height = 26, ItemsSource = products.DefaultView, DisplayMemberPath = "Display", SelectedValuePath = "Id", IsEditable = true, IsTextSearchEnabled = true, SelectedValue = productId ?? "" };
        Label(bar, "Liste:"); bar.Children.Add(list); Label(bar, "Ürün:"); bar.Children.Add(product);
        var grid = Grid();
        Col(grid, "Tarih", "TarihYerel", 130); Col(grid, "Liste", "Liste", 90); Col(grid, "Stok Kodu", "StokKodu", 120); Col(grid, "Stok Adı", "StokAdi", 240);
        Col(grid, "Eski Fiyat", "EskiFiyat", 100, "N2"); Col(grid, "Yeni Fiyat", "YeniFiyat", 100, "N2"); Col(grid, "Değişim %", "DegisimYuzde", 85, "N2"); Col(grid, "Kaynak", "KaynakAdi", 90); Col(grid, "Kullanıcı", "Kullanici", 100);
        void Refresh()
        {
            var table = service.History(companyId, product.SelectedValue as string is { Length: > 0 } p ? p : null, list.SelectedValue as string is { Length: > 0 } l ? l : null);
            table.Columns.Add("TarihYerel", typeof(string)); table.Columns.Add("KaynakAdi", typeof(string));
            foreach (DataRow r in table.Rows)
            {
                r["TarihYerel"] = DateTime.TryParse(r["Tarih"].ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : r["Tarih"];
                r["KaynakAdi"] = r["Kaynak"] switch { "Manual" => "Elle", "Bulk" => "Toplu", "Import" => "Aktarım", var s => s };
            }
            grid.ItemsSource = table.DefaultView;
        }
        list.SelectionChanged += (_, _) => Refresh(); product.SelectionChanged += (_, _) => Refresh();
        Button(bar, "Yenile (F5)", Refresh, true);
        ErpGridContext.Register(grid, "pricing.history", [], () => { Refresh(); return Task.CompletedTask; }, "ProductPriceHistory");
        KeyboardInteractionService.AttachListShortcuts(root, null, null, null, Refresh);
        root.Children.Add(grid); Refresh();
        return root;
    }

    private static ContextActionDefinition Action(string id, string header, ContextActionGroup group, Action run, int order = 10) =>
        new(id, header, "", "", order, group, _ => { run(); return Task.FromResult(ContextActionResult.Ok()); });

    private static DockPanel Shell(string title, string help, out WrapPanel bar)
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var header = new TextBlock { Text = title, FontSize = Ui.Font.Title, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Text.Primary.Brush") };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var note = new TextBlock { Text = help, Foreground = Muted, FontSize = Ui.Font.Caption, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) };
        DockPanel.SetDock(note, Dock.Top); root.Children.Add(note);
        bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        return root;
    }

    private static void Label(Panel bar, string text) => bar.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 5, 0) });

    private static DataGrid Grid() => new()
    {
        Style = (Style)System.Windows.Application.Current.FindResource("ProfessionalDataGridStyle"),
        IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column
    };

    private static void Col(DataGrid grid, string header, string path, double width, string? format = null) =>
        grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path) { StringFormat = format, ConverterCulture = Turkish }, Width = width });

    private static void Button(Panel bar, string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, Height = 26, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0) };
        if (primary) { button.Background = Accent; button.Foreground = Ui.Brush("R3.Text.OnAccent.Brush"); button.BorderThickness = new Thickness(0); }
        button.Click += (_, _) => action(); bar.Children.Add(button);
    }

    private static void Info(FrameworkElement owner, string message) => MessageBox.Show(Window.GetWindow(owner)!, message, "Fiyat Yönetimi", MessageBoxButton.OK, MessageBoxImage.Information);

    private static bool Try(FrameworkElement owner, Action action, string title)
    {
        try { action(); return true; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        { MessageBox.Show(Window.GetWindow(owner)!, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
    }
}

public sealed class PriceListDialog : EditorDialog
{
    public PriceListEdit? Result { get; private set; }

    public PriceListDialog(PriceListEdit? existing, string companyId, bool campaignDefault = false) : base(existing == null ? (campaignDefault ? "Yeni Kampanya" : "Yeni Fiyat Listesi") : "Fiyat Listesini Düzenle")
    {
        Width = 420;
        var code = Field("Liste kodu *", new TextBox { Text = existing?.Code ?? "", MaxLength = 20, CharacterCasing = CharacterCasing.Upper });
        var name = Field("Liste adı *", new TextBox { Text = existing?.Name ?? "", MaxLength = 100 });
        var type = Field("Tip *", new ComboBox { ItemsSource = new[] { new KeyValuePair<string, string>("Sales", "Satış"), new KeyValuePair<string, string>("Purchase", "Alış") }, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = existing?.PriceType ?? "Sales" });
        var vat = Field("KDV *", new ComboBox { ItemsSource = new[] { new KeyValuePair<bool, string>(true, "KDV dahil"), new KeyValuePair<bool, string>(false, "KDV hariç") }, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = existing?.VatIncluded ?? true });
        var currency = Field("Para birimi *", new ComboBox { ItemsSource = new[] { "TRY", "USD", "EUR", "GBP" }, SelectedItem = existing?.CurrencyCode ?? "TRY" });
        var from = Field("Geçerlilik başlangıcı (boş = sınırsız)", new DatePicker { SelectedDate = existing?.ValidFrom });
        var to = Field("Geçerlilik bitişi (boş = sınırsız)", new DatePicker { SelectedDate = existing?.ValidTo });
        var sequence = Field("Sıra", new TextBox { Text = (existing?.Sequence ?? 0).ToString(CultureInfo.InvariantCulture), MaxLength = 4 });
        var active = new CheckBox { Content = "Aktif", IsChecked = existing?.IsActive ?? true, Margin = new Thickness(0, 10, 0, 0) }; Fields.Children.Add(active);
        var campaign = new CheckBox { Content = "Kampanya (tarih aralığında diğer satış fiyatlarının önüne geçer)", IsChecked = existing?.IsCampaign ?? campaignDefault, Margin = new Thickness(0, 6, 0, 0) }; Fields.Children.Add(campaign);
        if (existing == null && campaignDefault) { from.SelectedDate = DateTime.Today; to.SelectedDate = DateTime.Today.AddDays(30); }
        Finish(() =>
        {
            if (!int.TryParse(sequence.Text, out var seq) || seq < 0) throw new ArgumentException("Sıra 0 veya pozitif bir tam sayı olmalıdır.");
            Result = new PriceListEdit(existing?.Id ?? "", companyId, code.Text, name.Text, type.SelectedValue as string ?? "Sales", vat.SelectedValue is true,
                currency.SelectedItem?.ToString() ?? "TRY", from.SelectedDate, to.SelectedDate, seq, active.IsChecked == true, campaign.IsChecked == true);
        });
        Loaded += (_, _) => (existing == null ? code : name).Focus();
    }
}

public sealed class CustomerPriceGroupDialog : EditorDialog
{
    public CustomerPriceGroupEdit? Result { get; private set; }

    public CustomerPriceGroupDialog(StoreDatabase db, string companyId, DataRowView group) : base($"Müşteri Fiyat Grubu — {group["Ad"]}")
    {
        Width = 420;
        var lists = db.Query("SELECT '' AS Id, '(Liste yok — varsayılan liste kullanılır)' AS Ad, -1 AS SortKey UNION ALL SELECT id, code || ' — ' || name, sequence FROM price_lists WHERE company_id=$c AND price_type='Sales' AND is_campaign=0 AND is_active=1 ORDER BY 3", ("$c", companyId));
        var list = Field("Satış fiyat listesi", new ComboBox { ItemsSource = lists.DefaultView, DisplayMemberPath = "Ad", SelectedValuePath = "Id", SelectedValue = group["FiyatListesiId"].ToString() });
        var discount = Field("Grup iskontosu % (fatura satırına yazılır)", new TextBox { Text = Convert.ToDecimal(group["Iskonto"]).ToString("0.##", CultureInfo.GetCultureInfo("tr-TR")), Tag = "Numeric" });
        Finish(() =>
        {
            if (!decimal.TryParse(discount.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("tr-TR"), out var d) || d is < 0 or > 100) throw new ArgumentException("İskonto 0 ile 100 arasında olmalıdır.");
            Result = new CustomerPriceGroupEdit(group["Id"].ToString()!, list.SelectedValue as string is { Length: > 0 } l ? l : null, d);
        });
        Loaded += (_, _) => list.Focus();
    }
}

/// <summary>Shows which price a customer would get for a product on a date, and why (the resolution order).</summary>
public sealed class SalesPriceQueryDialog : EditorDialog
{
    public SalesPriceQueryDialog(StoreDatabase db, string companyId, string? productId = null) : base("Satış Fiyatı Sorgula")
    {
        Width = 480;
        var turkish = CultureInfo.GetCultureInfo("tr-TR");
        var products = db.Query("SELECT id AS Id, code || ' — ' || name AS Display FROM products WHERE company_id=$c AND is_active=1 ORDER BY code", ("$c", companyId));
        var product = Field("Ürün", new ComboBox { ItemsSource = products.DefaultView, DisplayMemberPath = "Display", SelectedValuePath = "Id", IsEditable = true, IsTextSearchEnabled = true, SelectedValue = productId ?? "" });
        var customers = db.Query("SELECT '' AS Id, '(Cari yok — perakende)' AS Ad, '' AS SortKey UNION ALL SELECT id, code || ' — ' || name, code FROM accounts WHERE company_id=$c AND is_active=1 AND account_type IN ('Customer','CustomerAndSupplier') ORDER BY 3", ("$c", companyId));
        var customer = Field("Müşteri", new ComboBox { ItemsSource = customers.DefaultView, DisplayMemberPath = "Ad", SelectedValuePath = "Id", SelectedIndex = 0, IsEditable = true, IsTextSearchEnabled = true });
        var date = Field("Tarih", new DatePicker { SelectedDate = DateTime.Today });
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), FontSize = Ui.Font.Body }; Fields.Children.Add(result);
        void Resolve()
        {
            if (product.SelectedValue is not string p || p.Length == 0) { result.Text = "Ürün seçin."; return; }
            var r = new LocalPriceService(db).ResolveSalesPrice(companyId, p, customer.SelectedValue as string is { Length: > 0 } a ? a : null, date.SelectedDate);
            result.Text = r == null
                ? "Bu ürün için geçerli bir satış fiyatı yok (hiçbir aktif, tarihi uygun liste fiyat vermiyor)."
                : $"Birim fiyat (KDV hariç): {r.UnitPrice.ToString("N2", turkish)} ₺\nListe fiyatı: {r.ListPrice.ToString("N2", turkish)} ₺ ({(r.ListVatIncluded ? "KDV dahil" : "KDV hariç")})\nİskonto: %{r.DiscountRate.ToString("0.##", turkish)}\nKaynak: {r.Source}";
        }
        product.SelectionChanged += (_, _) => Resolve(); customer.SelectionChanged += (_, _) => Resolve(); date.SelectedDateChanged += (_, _) => Resolve();
        Finish(() => { });
        AcceptButton!.Content = "Kapat"; CancelButton!.Visibility = Visibility.Collapsed;
        Loaded += (_, _) => { Resolve(); product.Focus(); };
    }
}

public sealed record BulkPriceRequest(BulkPriceMode Mode, decimal Value, string? SourceListId, string Scope, bool OnlyExisting, decimal RoundTo, decimal? EndWith);

public sealed class BulkPriceDialog : EditorDialog
{
    public BulkPriceRequest? Request { get; private set; }

    public BulkPriceDialog(DataTable lists, string targetListId, int visibleCount, string selectedGroupId) : base("Toplu Fiyat Güncelleme")
    {
        Width = 460;
        var turkish = CultureInfo.GetCultureInfo("tr-TR");
        var target = lists.Rows.Cast<DataRow>().FirstOrDefault(r => r["Id"].ToString() == targetListId)?["Ad"]?.ToString() ?? "";
        Fields.Children.Add(new TextBlock { Text = $"Hedef liste: {target}", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var mode = Field("İşlem *", new ComboBox
        {
            ItemsSource = new[] { new KeyValuePair<BulkPriceMode, string>(BulkPriceMode.IncreasePercent, "Yüzde artır / azalt (%)"), new KeyValuePair<BulkPriceMode, string>(BulkPriceMode.IncreaseAmount, "Tutar ekle / çıkar"),
                new KeyValuePair<BulkPriceMode, string>(BulkPriceMode.SetAmount, "Sabit fiyat ata"), new KeyValuePair<BulkPriceMode, string>(BulkPriceMode.CopyFromList, "Başka listeden kopyala × çarpan") },
            DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedIndex = 0
        });
        var value = Field("Değer * (yüzde: 10 = %10 artış, -5 = %5 indirim; kopyada çarpan: 1,35)", new TextBox { Text = "10", Tag = "Numeric" });
        var sources = lists.Rows.Cast<DataRow>().Where(r => r["Id"].ToString() != targetListId).Select(r => new KeyValuePair<string, string>(r["Id"].ToString()!, r["Ad"].ToString()!)).ToList();
        var source = Field("Kaynak liste (yalnızca kopyalamada)", new ComboBox { ItemsSource = sources, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedIndex = sources.Count > 0 ? 0 : -1 });
        var scopes = new List<KeyValuePair<string, string>> { new("Visible", $"Ekranda listelenen ürünler ({visibleCount:N0})"), new("All", "Firmadaki tüm aktif ürünler") };
        if (!string.IsNullOrWhiteSpace(selectedGroupId)) scopes.Insert(1, new("Group", "Seçili stok grubundaki tüm ürünler"));
        var scope = Field("Kapsam *", new ComboBox { ItemsSource = scopes, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedIndex = 0 });
        var onlyExisting = new CheckBox { Content = "Yalnızca bu listede fiyatı olan ürünler", IsChecked = true, Margin = new Thickness(0, 8, 0, 0) }; Fields.Children.Add(onlyExisting);
        var round = Field("Yuvarlama adımı", new ComboBox { ItemsSource = new[] { "0,01", "0,05", "0,10", "0,50", "1", "5", "10" }, SelectedIndex = 0 });
        var ending = Field("Küsurat (boş = yok, örn. 0,99 → 124,99)", new TextBox { Tag = "Numeric" });
        void Sync() { source.IsEnabled = mode.SelectedValue is BulkPriceMode.CopyFromList; if (mode.SelectedValue is BulkPriceMode.CopyFromList) onlyExisting.IsChecked = false; }
        mode.SelectionChanged += (_, _) => Sync(); Sync();
        Finish(() =>
        {
            if (!decimal.TryParse(value.Text, NumberStyles.Number | NumberStyles.AllowLeadingSign, turkish, out var v)) throw new ArgumentException("Değer geçerli bir sayı olmalıdır.");
            var step = decimal.Parse(round.SelectedItem?.ToString() ?? "0,01", turkish);
            decimal? end = null;
            if (!string.IsNullOrWhiteSpace(ending.Text))
            {
                if (!decimal.TryParse(ending.Text, NumberStyles.Number, turkish, out var e) || e is < 0 or >= 1) throw new ArgumentException("Küsurat 0 ile 1 arasında olmalıdır (örn. 0,99).");
                end = e;
            }
            var selectedMode = (BulkPriceMode)mode.SelectedValue!;
            var confirm = MessageBox.Show(this, $"Seçilen kapsamdaki fiyatlar güncellenecek. Bu işlem geri alınamaz; her değişiklik fiyat geçmişine kaydedilir.\n\nDevam edilsin mi?", "Toplu Fiyat Güncelleme", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) throw new ArgumentException("İşlem iptal edildi.");
            Request = new BulkPriceRequest(selectedMode, v, source.SelectedValue as string, scope.SelectedValue as string ?? "Visible", onlyExisting.IsChecked == true, step, end);
        });
        Loaded += (_, _) => value.Focus();
    }
}
