using System.Collections.ObjectModel;
using R3.Desktop.Design;
using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using R3.Desktop.ContextActions;
using R3.Infrastructure;

namespace R3.Desktop;

internal sealed class PurchaseModuleView : UserControl
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private readonly LocalPurchasingService _service;
    private readonly string _companyId, _branchId, _warehouseId, _documentType, _userName;
    private readonly TextBox _search = new() { Width = 270, Height = 29, Padding = new Thickness(8, 4, 8, 4) };
    private readonly ComboBox _status = new() { Width = 125, Height = 29, Margin = new Thickness(7, 0, 0, 0) };
    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 27 };
    private readonly WrapPanel _cards = new() { Margin = new Thickness(0, 0, 0, 11) };
    private readonly LoadingOverlay _loading = new();
    private int _loadVersion;

    public PurchaseModuleView(StoreDatabase database, string companyId, string branchId, string warehouseId, string documentType, string userName)
    {
        _service = new(database); _companyId = companyId; _branchId = branchId; _warehouseId = warehouseId; _documentType = documentType; _userName = userName;
        var title = documentType == "Order" ? "Satınalma Siparişleri" : "Alış Faturaları";
        var root = new DockPanel { Margin = new Thickness(14), Background = Ui.Brush("R3.Background.Brush") }; var host = new Grid(); host.Children.Add(root); host.Children.Add(_loading); Content = host;
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = title, FontSize = Ui.Font.Title, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Text.Primary.Brush") });
        header.Children.Add(new TextBlock { Text = documentType == "Order" ? "Tedarikçi taleplerini, beklenen teslim tarihlerini ve sipariş tutarlarını yönetin." : "Tedarikçi alış belgelerini tek merkezde izleyin ve onay akışına alın.", Foreground = Ui.Brush("R3.Text.Secondary.Brush"), Margin = new Thickness(0, 3, 0, 12) });
        header.Children.Add(_cards);
        var toolbar = new Border { Background = Ui.Brush("R3.Surface.Alt.Brush"), BorderBrush = Ui.Brush("R3.Border.Brush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(6) }; header.Children.Add(toolbar);
        var toolbarGrid = new DockPanel { LastChildFill = false }; toolbar.Child = toolbarGrid;
        var newButton = SmallButton(documentType == "Order" ? "+  Yeni Sipariş (F2)" : "+  Yeni Alış Faturası (F2)", true); newButton.Click += (_, _) => NewDocument(); toolbarGrid.Children.Add(newButton);
        var approve = SmallButton("✓  Onayla", false); approve.Click += (_, _) => ApproveSelected(); toolbarGrid.Children.Add(approve);
        var process = SmallButton(documentType == "Order" ? "⇩  Mal Kabul" : "●  Kesinleştir", false); process.Click += (_, _) => ProcessSelected(); toolbarGrid.Children.Add(process);
        var cancel = SmallButton("×  İptal Et", false); cancel.Click += (_, _) => CancelSelected(); toolbarGrid.Children.Add(cancel);
        var refresh = SmallButton("↻  Yenile (F5)", false); refresh.Click += (_, _) => Refresh(); toolbarGrid.Children.Add(refresh);
        var filter = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) }; DockPanel.SetDock(filter, Dock.Right); toolbarGrid.Children.Add(filter);
        filter.Children.Add(new TextBlock { Text = "Ara:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0), Foreground = Ui.Brush("R3.Text.Secondary.Brush") }); filter.Children.Add(_search);
        _status.ItemsSource = new[] { new Choice("", "Tüm durumlar"), new Choice("Draft", "Taslak"), new Choice("Approved", "Onaylı"), new Choice("PartiallyReceived", "Kısmi teslim"), new Choice("Posted", "Kesinleşmiş"), new Choice("Closed", "Kapalı"), new Choice("Cancelled", "İptal") }; _status.DisplayMemberPath = "Name"; _status.SelectedValuePath = "Id"; _status.SelectedIndex = 0; filter.Children.Add(_status);
        ConfigureGrid(); root.Children.Add(_grid);
        KeyboardInteractionService.AttachDebouncedSearch(_search, Refresh);
        KeyboardInteractionService.AttachListShortcuts(this, _search, NewDocument, null, Refresh);
        _status.SelectionChanged += (_, _) => Refresh();
        ErpGridContext.Register(_grid, documentType == "Order" ? "purchasing.orders" : "purchasing.invoices", Actions(), () => { Refresh(); return Task.CompletedTask; }, "PurchaseDocument");
        Loaded += (_, _) => Refresh();
    }

    private void ConfigureGrid()
    {
        AddColumn("BelgeNo", "Belge No", 125); AddColumn("MatbuNo", "Matbu No", 115); AddColumn("Seri", "Seri", 65); AddColumn("Tarih", "Tarih", 120); AddColumn("BeklenenTarih", "Beklenen Teslim", 125); AddColumn("TedarikciKodu", "Tedarikçi Kodu", 110); AddColumn("Tedarikci", "Tedarikçi", 220); AddColumn("Sube", "Şube", 130); AddColumn("Depo", "Depo", 130); AddColumn("Satir", "Satır", 55); AddColumn("Miktar", "Miktar", 75, "N2"); AddColumn("AraToplam", "Ara Toplam", 100, "N2"); AddColumn("KDV", "KDV", 90, "N2"); AddColumn("GenelToplam", "Genel Toplam", 110, "N2"); AddColumn("ParaBirimi", "PB", 50); AddColumn("Durum", "Durum", 90); AddColumn("Aciklama", "Açıklama", 220);
    }
    private void AddColumn(string path, string header, double width, string? format = null) => _grid.Columns.Add(new DataGridTextColumn { Header = header, Width = width, Binding = new Binding(path) { StringFormat = format } });
    private IReadOnlyList<ContextActionDefinition> Actions() =>
    [
        Action("purchase.approve", "Belgeyi Onayla", "purchasing.document.approve", "", 10, ContextActionGroup.Primary, ApproveSelected, row => Status(row) == "Draft"),
        Action("purchase.process", _documentType == "Order" ? "Mal Kabul Yap" : "Alış Faturasını Kesinleştir", _documentType == "Order" ? "purchasing.document.receive" : "purchasing.invoice.post", "", 10, ContextActionGroup.Operational, ProcessSelected, row => _documentType == "Order" ? Status(row) is "Approved" or "PartiallyReceived" : Status(row) == "Approved"),
        Action("purchase.cancel", "Belgeyi İptal Et", "purchasing.document.cancel", "", 10, ContextActionGroup.Critical, CancelSelected, row => Status(row) is not ("Cancelled" or "Closed")),
        Action("purchase.supplier", "Tedarikçi Cari Kartı", "accounts.view", "", 10, ContextActionGroup.Related, () => MessageBox.Show(Window.GetWindow(this), "Tedarikçi kodu: " + Selected?["TedarikciKodu"], "Tedarikçi")),
        Action("purchase.audit", "İşlem Geçmişi / Audit", "purchasing.audit.view", "", 10, ContextActionGroup.Audit, ShowAudit)
    ];
    private ContextActionDefinition Action(string id, string title, string permission, string icon, int order, ContextActionGroup group, Action execute, Func<object?, bool>? enabled = null, bool confirm = false) =>
        new(id, title, permission, icon, order, group, _ => { execute(); return Task.FromResult(ContextActionResult.Ok(refresh: true)); }, null, enabled, ContextSelectionMode.Single, true, confirm, _ => $"{title} işlemi uygulanacak.\n\n{Selected?["BelgeNo"]} — {Selected?["Tedarikci"]}", confirm ? "PurchaseDocumentCancelled" : null, "PurchaseDocument", row => (row as DataRowView)?["Id"]?.ToString());
    private static string Status(object? row)
    {
        if (row is not DataRowView view) return "";
        // Durum kullanıcıya Türkçe gösterilir; sağ tık/komut kuralları ham kodla çalışır.
        return view.Row.Table.Columns.Contains("DurumKod")
            ? view["DurumKod"]?.ToString() ?? ""
            : view["Durum"]?.ToString() ?? "";
    }
    private DataRowView? Selected => _grid.SelectedItem as DataRowView;

    private void NewDocument()
    {
        var dialog = new PurchaseDocumentDialog(_service, _companyId, _branchId, _warehouseId, _documentType, _userName) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) Refresh();
    }
    private void ApproveSelected()
    {
        if (Selected == null) return;
        try { var number = _service.Approve(Selected["Id"].ToString()!, _userName); MessageBox.Show(Window.GetWindow(this), $"Belge onaylandı.\nBelge no: {number}", "Satınalma", MessageBoxButton.OK, MessageBoxImage.Information); Refresh(); }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "Satınalma", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void ProcessSelected()
    {
        if (Selected == null) return;
        if (_documentType == "Order")
        {
            var dialog = new PurchaseReceiptDialog(_service, Selected["Id"].ToString()!, Selected["BelgeNo"].ToString()!) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true) { try { _service.Receive(Selected["Id"].ToString()!, dialog.Receipts, _userName); Refresh(); } catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "Mal Kabul", MessageBoxButton.OK, MessageBoxImage.Warning); } }
            return;
        }
        if (MessageBox.Show(Window.GetWindow(this), $"{Selected["BelgeNo"]} numaralı alış faturası kesinleştirilecek.\n\nStok girişi ve tedarikçi borç hareketi oluşturulsun mu?", "Alış Faturasını Kesinleştir", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { _service.PostInvoice(Selected["Id"].ToString()!, _userName); MessageBox.Show(Window.GetWindow(this), "Alış faturası kesinleştirildi; stok ve cari hareketleri oluşturuldu.", "Satınalma", MessageBoxButton.OK, MessageBoxImage.Information); Refresh(); }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "Satınalma", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void CancelSelected()
    {
        if (Selected == null) return;
        if (MessageBox.Show(Window.GetWindow(this), $"{Selected["BelgeNo"]} numaralı belge iptal edilsin mi?", "Belgeyi İptal Et", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { _service.Cancel(Selected["Id"].ToString()!, _userName); Refresh(); } catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "Satınalma", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void ShowAudit()
    {
        if (Selected == null) return;
        var table = _service.Database.Query("SELECT created_at AS Tarih,action AS Islem,COALESCE(user_id,'') AS Kullanici,COALESCE(new_values,'') AS Detay FROM audit_logs WHERE entity_type='PurchaseDocument' AND entity_id=$id ORDER BY created_at DESC", ("$id", Selected["Id"]));
        var text = table.Rows.Count == 0 ? "Bu belge için işlem geçmişi kaydı bulunamadı." : string.Join("\n\n", table.Rows.Cast<DataRow>().Select(x => $"{x["Tarih"]}  •  {R3.Desktop.Presentation.InventoryPresentation.AuditActionLabel(x["Islem"].ToString() ?? "")}\n{x["Kullanici"]}  {x["Detay"]}"));
        MessageBox.Show(Window.GetWindow(this), text, "Satınalma İşlem Geçmişi", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void Refresh() => _ = RefreshAsync();
    private async Task RefreshAsync()
    {
        var version = ++_loadVersion; var search = _search.Text; var status = _status.SelectedValue?.ToString(); _loading.ShowLoading("Satınalma verileri yükleniyor…");
        try
        {
            var result = await Task.Run(() => (_service.Search(_companyId, _documentType, search, status), _service.Summary(_companyId)));
            if (version != _loadVersion || !IsLoaded) return;
            if (!result.Item1.Columns.Contains("DurumKod")) result.Item1.Columns.Add("DurumKod", typeof(string));
            foreach (DataRow row in result.Item1.Rows)
            {
                row["DurumKod"] = row["Durum"]?.ToString() ?? "";
                row["Durum"] = R3.Desktop.Presentation.InventoryPresentation.PurchaseStatusLabel(row["DurumKod"].ToString()!);
            }
            _grid.ItemsSource = result.Item1.DefaultView;
            var summary = result.Item2; _cards.Children.Clear(); _cards.Children.Add(Card("Taslak", summary.Draft.ToString("N0", Turkish), "#B56B00")); _cards.Children.Add(Card("Onaylı", summary.Approved.ToString("N0", Turkish), "#177C70")); _cards.Children.Add(Card("Açık Sipariş", summary.OpenOrders.ToString("N0", Turkish), "#5E5E5E")); _cards.Children.Add(Card("Açık Sipariş Tutarı", summary.OpenOrderTotal.ToString("N2", Turkish) + " ₺", "#484848", 170));
        }
        catch (Exception ex) { if (version == _loadVersion) MessageBox.Show(Window.GetWindow(this), ex.Message, "Satınalma verileri yüklenemedi", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { if (version == _loadVersion) _loading.HideLoading(); }
    }
    private static Border Card(string label, string value, string color, double width = 125) => new() { Width = width, Height = 54, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 6, 10, 6), Background = Ui.Brush("R3.Surface.Brush"), BorderBrush = Ui.Brush("R3.Border.Brush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Child = new StackPanel { Children = { new TextBlock { Text = label, FontSize = Ui.Font.Grid, Foreground = Ui.Brush("R3.Text.Secondary.Brush") }, new TextBlock { Text = value, FontSize = Ui.Font.Title, FontWeight = FontWeights.SemiBold, Foreground = Brush(color) } } } };
    private static Button SmallButton(string text, bool primary) => new() { Content = text, Height = 29, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 3, 10, 3), Background = primary ? Ui.Brush("R3.Accent.Brush") : Ui.Brush("R3.Surface.Brush"), Foreground = primary ? Ui.Brush("R3.Text.OnAccent.Brush") : Ui.Brush("R3.Text.Primary.Brush"), BorderBrush = Ui.Brush("R3.Border.Strong.Brush") };
    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));
    private sealed record Choice(string Id, string Name);
}

internal sealed class PurchaseReceiptModuleView : UserControl
{
    private readonly LocalPurchaseReceiptService _service;
    private readonly string _companyId, _userName;
    private readonly TextBox _search = new() { Width = 290, Height = 29, Padding = new Thickness(8, 4, 8, 4) };
    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, RowHeight = 27 };
    private readonly LoadingOverlay _loading = new();
    private int _loadVersion;

    public PurchaseReceiptModuleView(StoreDatabase database, string companyId, string branchId, string warehouseId, string userName)
    {
        _service = new(database); _companyId = companyId; _userName = userName;
        var root = new DockPanel { Margin = new Thickness(14), Background = Ui.Brush("R3.Background.Brush") }; var host = new Grid(); host.Children.Add(root); host.Children.Add(_loading); Content = host;
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Alış İrsaliyeleri", FontSize = Ui.Font.Title, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Text.Primary.Brush") });
        header.Children.Add(new TextBlock { Text = "Satınalma siparişlerinden oluşturulan teslim belgelerini ve depo girişlerini yönetin.", Foreground = Ui.Brush("R3.Text.Secondary.Brush"), Margin = new Thickness(0, 3, 0, 12) });
        var toolbar = new Border { Background = Ui.Brush("R3.Surface.Alt.Brush"), BorderBrush = Ui.Brush("R3.Border.Brush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(6) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(KryptonWpfBridge.ActionBar(("İrsaliyeden Fatura", CreateInvoiceFromReceipt, true), ("Yenile (F5)", Refresh, false)));
        var refresh = SmallButton("↻  Yenile (F5)"); refresh.Click += (_, _) => Refresh(); row.Children.Add(refresh);
        var invoice = SmallButton("▣  İrsaliyeden Fatura"); invoice.Background = Ui.Brush("R3.Accent.Brush"); invoice.Foreground = Ui.Brush("R3.Text.OnAccent.Brush"); invoice.Click += (_, _) => CreateInvoiceFromReceipt(); row.Children.Add(invoice);
        row.Children.Add(new TextBlock { Text = "Ara:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 5, 0), Foreground = Ui.Brush("R3.Text.Secondary.Brush") }); row.Children.Add(_search); toolbar.Child = row; header.Children.Add(toolbar);
        foreach (var c in new[] { ("IrsaliyeNo", "İrsaliye No", 125d), ("Tarih", "Tarih", 125d), ("TedarikciKodu", "Tedarikçi Kodu", 110d), ("Tedarikci", "Tedarikçi", 220d), ("SiparisNo", "Sipariş No", 125d), ("Satir", "Satır", 55d), ("Miktar", "Miktar", 85d), ("Durum", "Durum", 120d), ("Aciklama", "Açıklama", 220d) }) _grid.Columns.Add(new DataGridTextColumn { Header = c.Item2, Width = c.Item3, Binding = new Binding(c.Item1) { StringFormat = c.Item1 == "Miktar" ? "N2" : null } });
        root.Children.Add(_grid);
        KeyboardInteractionService.AttachDebouncedSearch(_search, Refresh); KeyboardInteractionService.AttachListShortcuts(this, _search, null, null, Refresh);
        _grid.MouseDoubleClick += (_, _) => OpenSelected(); _grid.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; } };
        ErpGridContext.Register(_grid, "purchasing.receipts", FutureModuleContextActions.PurchaseReceipts(async (action, selected) =>
        {
            if (selected is not DataRowView row) return ContextActionResult.Failed("Bir alış irsaliyesi seçin.");
            var id = row["Id"].ToString()!;
            try
            {
                if (action == "purchase.receipt.approve") _service.Approve(id, _userName);
                else if (action == "purchase.receipt.cancel") _service.Cancel(id, _userName);
                else if (action == "purchase.receipt.open") OpenSelected(row);
                else if (action == "purchase.receipt.audit") ShowAudit(id);
                return ContextActionResult.Ok(refresh: action is "purchase.receipt.approve" or "purchase.receipt.cancel");
            }
            catch (Exception ex) { return ContextActionResult.Failed(ex.Message); }
        }), () => { Refresh(); return Task.CompletedTask; }, "PurchaseReceipt");
        Loaded += (_, _) => _ = RefreshAsync();
    }

    private void Refresh() => _ = RefreshAsync();
    private async Task RefreshAsync()
    {
        var version = ++_loadVersion; var search = _search.Text; _loading.ShowLoading("Alış irsaliyeleri yükleniyor…");
        try
        {
            var table = await Task.Run(() => _service.Search(_companyId, search));
            if (version != _loadVersion || !IsLoaded) return;
            foreach (DataRow r in table.Rows) r["Durum"] = R3.Desktop.Presentation.InventoryPresentation.PurchaseStatusLabel(r["Durum"].ToString()!);
            _grid.ItemsSource = table.DefaultView;
        }
        catch (Exception ex) { if (version == _loadVersion) MessageBox.Show(Window.GetWindow(this), ex.Message, "Alış irsaliyeleri yüklenemedi", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { if (version == _loadVersion) _loading.HideLoading(); }
    }
    private void OpenSelected() { if (_grid.SelectedItem is DataRowView row) OpenSelected(row); }
    private void OpenSelected(DataRowView row) => CreateInvoiceFromReceipt(row);
    private void CreateInvoiceFromReceipt() { if (_grid.SelectedItem is DataRowView row) CreateInvoiceFromReceipt(row); else MessageBox.Show(Window.GetWindow(this), "Önce depoya alınmış bir alış irsaliyesi seçin.", "İrsaliyeden fatura", MessageBoxButton.OK, MessageBoxImage.Information); }
    private void CreateInvoiceFromReceipt(DataRowView row)
    {
        try
        {
            var dialog = new PurchaseInvoiceFromReceiptDialog(_service, row["Id"].ToString()!, _userName) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true) MessageBox.Show(Window.GetWindow(this), "Alış faturası taslak olarak oluşturuldu. Alış Faturaları menüsünden onaylayabilirsiniz.", "İşlem tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "İrsaliyeden fatura oluşturulamadı", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void ShowAudit(string id) { var rows = _service.Database.Query("SELECT created_at AS Tarih,action AS Islem,COALESCE(user_id,'') AS Kullanici,new_values AS Detay FROM audit_logs WHERE entity_type='PurchaseReceipt' AND entity_id=$id ORDER BY created_at DESC", ("$id", id)); MessageBox.Show(Window.GetWindow(this), rows.Rows.Count == 0 ? "Bu belge için işlem geçmişi kaydı bulunamadı." : string.Join("\n\n", rows.Rows.Cast<DataRow>().Select(x => $"{x["Tarih"]} • {R3.Desktop.Presentation.InventoryPresentation.AuditActionLabel(x["Islem"].ToString() ?? "")}\n{x["Kullanici"]}  {x["Detay"]}")), "Alış İrsaliyesi Geçmişi", MessageBoxButton.OK, MessageBoxImage.Information); }
    private static Button SmallButton(string text) => new() { Content = text, Height = 29, Padding = new Thickness(10, 3, 10, 3), Background = Ui.Brush("R3.Surface.Brush"), Foreground = Ui.Brush("R3.Text.Primary.Brush"), BorderBrush = Ui.Brush("R3.Border.Strong.Brush") };
    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));
}

internal sealed class PurchaseInvoiceFromReceiptDialog : EditorDialog
{
    private readonly LocalPurchaseReceiptService _service; private readonly string _receiptId, _user;
    private readonly DatePicker _date, _due; private readonly TextBox _external, _series, _description; private readonly DataTable _lines;
    public PurchaseInvoiceFromReceiptDialog(LocalPurchaseReceiptService service, string receiptId, string userName) : base("Satınalma Faturası • İrsaliyeden")
    {
        _service = service; _receiptId = receiptId; _user = userName; Width = 1060; Height = 720; ResizeMode = ResizeMode.CanResize;
        var header = service.Header(receiptId); if (header.Rows.Count == 0) throw new KeyNotFoundException("Alış irsaliyesi bulunamadı."); var h = header.Rows[0];
        Fields.Children.Add(new TextBlock { Text = "İrsaliyedeki ürünleri faturaya aktarın; fiyat ve vergi bilgileri siparişten taşınır.", Foreground = Ui.Brush("R3.Text.Secondary.Brush"), Margin = new Thickness(0, 0, 0, 10) });
        var top = new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) }); top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) }); top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var cari = Box("Cari Hesap"); AddText((Panel)cari.Child, "Tedarikçi", $"{h["TedarikciKodu"]} — {h["Tedarikci"]}"); AddText((Panel)cari.Child, "Adres", h["Adres"].ToString()!); AddText((Panel)cari.Child, "Vergi No", h["VergiNo"].ToString()!); Grid.SetColumn(cari, 0); top.Children.Add(cari);
        var fatura = Box("Fatura"); _date = AddDate((Panel)fatura.Child, "Fatura tarihi", DateTime.Today); _external = AddBox((Panel)fatura.Child, "Matbu no"); _series = AddBox((Panel)fatura.Child, "Seri"); _due = AddDate((Panel)fatura.Child, "Vade tarihi", DateTime.Today); Grid.SetColumn(fatura, 2); top.Children.Add(fatura);
        var bilgi = Box("Kaynak irsaliye"); AddText((Panel)bilgi.Child, "İrsaliye no", h["IrsaliyeNo"].ToString()!); AddText((Panel)bilgi.Child, "İrsaliye tarihi", FormatDate(h["Tarih"])); AddText((Panel)bilgi.Child, "Şube / Depo", $"{h["Sube"]} / {h["Depo"]}"); Grid.SetColumn(bilgi, 4); top.Children.Add(bilgi);
        Fields.Children.Add(top); Fields.Children.Add(new Border { Height = 1, Background = Ui.Brush("R3.Surface.Selected.Brush"), Margin = new Thickness(0, 12, 0, 10) });
        Fields.Children.Add(new TextBlock { Text = "İrsaliye satırları", FontSize = Ui.Font.Section, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Text.Primary.Brush") });
        _lines = service.Lines(receiptId); var grid = new DataGrid { ItemsSource = _lines.DefaultView, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, Height = 300, Margin = new Thickness(0, 7, 0, 9), RowHeight = 27, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal };
        foreach (var c in new[] { ("UrunKodu", "Stok Kodu", 110d), ("Urun", "Stok Adı", 260d), ("Birim", "Birim", 70d), ("Miktar", "Miktar", 90d), ("BirimMaliyet", "Fiyat", 100d), ("Lokasyon", "Lokasyon", 120d), ("Lot", "Lot", 100d), ("Seri", "Seri", 100d) }) grid.Columns.Add(new DataGridTextColumn { Header = c.Item2, Width = c.Item3, Binding = new Binding(c.Item1) { StringFormat = c.Item1 is "Miktar" or "BirimMaliyet" ? "N2" : null } });
        Fields.Children.Add(grid);
        var bottom = new Grid(); bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) }); _description = AddBox(bottom, "Açıklama", 0); Grid.SetColumn(_description, 0); var totals = Box("Toplamlar"); AddText((Panel)totals.Child, "Satır / miktar", $"{_lines.Rows.Count} / {_lines.Rows.Cast<DataRow>().Sum(x => Convert.ToDecimal(x["Miktar"])):N2}"); AddText((Panel)totals.Child, "KDV", "Satır KDV oranlarına göre hesaplanır"); AddText((Panel)totals.Child, "Durum", "Taslak — onay bekliyor"); Grid.SetColumn(totals, 1); bottom.Children.Add(totals); Fields.Children.Add(bottom);
        Finish(Save);
    }
    private void Save() { _service.CreateInvoiceFromReceipt(_receiptId, new(_date.SelectedDate ?? DateTime.Today, _due.SelectedDate, _description.Text, _external.Text, _series.Text), _user); }
    private static Border Box(string title) { var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Text.Primary.Brush"), Margin = new Thickness(0, 0, 0, 6) }); return new Border { Background = Ui.Brush("R3.Background.Brush"), BorderBrush = Ui.Brush("R3.Border.Brush"), BorderThickness = new Thickness(1), Padding = new Thickness(10), Child = panel, Margin = new Thickness(0) }; }
    private static void AddText(Panel p, string label, string value) { p.Children.Add(new TextBlock { Text = label, FontSize = Ui.Font.Grid, Foreground = Ui.Brush("R3.Text.Secondary.Brush"), Margin = new Thickness(0, 2, 0, 1) }); p.Children.Add(new TextBlock { Text = value, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Ui.Brush("R3.Text.Primary.Brush"), Margin = new Thickness(0, 0, 0, 3) }); }
    private static TextBox AddBox(Panel panel, string label, int column = -1) { var p = panel is StackPanel s ? s : null; var wrap = new StackPanel { Margin = new Thickness(0, 1, 0, 3) }; wrap.Children.Add(new TextBlock { Text = label, FontSize = Ui.Font.Grid, Foreground = Ui.Brush("R3.Text.Secondary.Brush") }); var box = new TextBox { Height = 27, Padding = new Thickness(6, 3, 6, 3) }; wrap.Children.Add(box); if (p != null) p.Children.Add(wrap); else { Grid.SetColumn(wrap, column); panel.Children.Add(wrap); } return box; }
    private static DatePicker AddDate(Panel panel, string label, DateTime value) { var wrap = new StackPanel { Margin = new Thickness(0, 1, 0, 3) }; wrap.Children.Add(new TextBlock { Text = label, FontSize = Ui.Font.Grid, Foreground = Ui.Brush("R3.Text.Secondary.Brush") }); var date = new DatePicker { Height = 27, SelectedDate = value }; wrap.Children.Add(date); panel.Children.Add(wrap); return date; }
    private static string FormatDate(object value) => DateTime.TryParse(value?.ToString(), out var date) ? date.ToString("dd.MM.yyyy") : value?.ToString() ?? "";
    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));
}

internal sealed class PurchaseReceiptDialog : EditorDialog
{
    private readonly DataTable _table; private readonly DataGrid _grid;
    public IReadOnlyList<PurchaseReceiptLine> Receipts { get; private set; } = [];
    public PurchaseReceiptDialog(LocalPurchasingService service, string documentId, string documentNo) : base("Mal Kabul • " + documentNo)
    {
        Width = 760; Height = 520; SizeToContent = SizeToContent.Manual; ResizeMode = ResizeMode.CanResize;
        Fields.Children.Add(new TextBlock { Text = "Bu kabulde teslim alınan miktarları girin. Kalan miktar daha sonraki mal kabullerde açık kalır.", Foreground = Ui.Brush("R3.Text.Secondary.Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        _table = service.Lines(documentId); _table.Columns.Add("KabulMiktari", typeof(decimal)); foreach (DataRow row in _table.Rows) row["KabulMiktari"] = row["Kalan"];
        _grid = new DataGrid { ItemsSource = _table.DefaultView, AutoGenerateColumns = false, CanUserAddRows = false, Height = 315, RowHeight = 28, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal };
        Add("UrunKodu", "Ürün Kodu", 95, true); Add("Urun", "Ürün", 220, true); Add("Birim", "Birim", 60, true); Add("SiparisMiktari", "Sipariş", 75, true); Add("TeslimAlinan", "Önceki Kabul", 85, true); Add("Kalan", "Kalan", 75, true); Add("KabulMiktari", "Bu Kabul", 80, false);
        Fields.Children.Add(_grid); Finish(Save);
    }
    private void Add(string path, string header, double width, bool readOnly) => _grid.Columns.Add(new DataGridTextColumn { Header = header, Width = width, IsReadOnly = readOnly, Binding = new Binding(path) { StringFormat = path is "UrunKodu" or "Urun" or "Birim" ? null : "N2", UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
    private void Save()
    {
        _grid.CommitEdit(DataGridEditingUnit.Cell, true); _grid.CommitEdit(DataGridEditingUnit.Row, true);
        var result = new List<PurchaseReceiptLine>();
        foreach (DataRow row in _table.Rows)
        {
            var quantity = Convert.ToDecimal(row["KabulMiktari"]); var remaining = Convert.ToDecimal(row["Kalan"]);
            if (quantity < 0 || quantity > remaining) throw new ArgumentException($"{row["UrunKodu"]} için kabul miktarı 0 ile kalan miktar arasında olmalıdır.");
            if (quantity > 0) result.Add(new(row["Id"].ToString()!, quantity));
        }
        if (result.Count == 0) throw new ArgumentException("Teslim alınan en az bir miktar girin."); Receipts = result;
    }
}

internal sealed class PurchaseDocumentDialog : EditorDialog
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private readonly LocalPurchasingService _service; private readonly LocalBarcodeResolver _resolver; private readonly string _company, _branch, _warehouse, _type, _user;
    private readonly ComboBox _supplier, _product; private readonly DatePicker _date, _expected; private readonly TextBox _quantity, _price, _discount, _vat, _description; private readonly DataGrid _linesGrid;
    private readonly ObservableCollection<LineRow> _lines = [];

    public PurchaseDocumentDialog(LocalPurchasingService service, string company, string branch, string warehouse, string type, string user) : base(type == "Order" ? "Yeni Satınalma Siparişi" : "Yeni Alış Faturası")
    {
        _service = service; _resolver = new LocalBarcodeResolver(service.Database); _company = company; _branch = branch; _warehouse = warehouse; _type = type; _user = user;
        Width = 880; Height = 620; SizeToContent = SizeToContent.Manual; ResizeMode = ResizeMode.CanResize;
        Fields.Children.Add(new TextBlock { Text = type == "Order" ? "Tedarikçi, teslim tarihi ve ürün satırlarını girerek sipariş taslağını oluşturun." : "Tedarikçi alış belgesini ürün ve vergi detaylarıyla kaydedin.", Foreground = Ui.Brush("R3.Text.Secondary.Brush"), Margin = new Thickness(0, 0, 0, 8) });
        var database = service.Database;
        var suppliers = database.Query("SELECT id AS Id,code || ' — ' || name AS Display FROM accounts WHERE company_id=$company AND is_active=1 AND account_type IN ('Supplier','CustomerAndSupplier') ORDER BY code", ("$company", company));
        var products = database.Query("SELECT p.id AS Id,p.code || ' — ' || p.name AS Display,p.base_unit_id AS UnitId,p.purchase_vat_rate AS Vat FROM products p WHERE p.company_id=$company AND p.is_active=1 ORDER BY p.code", ("$company", company));
        var top = new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); top.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel(); var right = new StackPanel(); Grid.SetColumn(right, 2); top.Children.Add(left); top.Children.Add(right);
        _supplier = CompactField(left, "Tedarikçi *", new ComboBox { ItemsSource = suppliers.DefaultView, DisplayMemberPath = "Display", SelectedValuePath = "Id", IsTextSearchEnabled = true });
        _date = CompactField(left, "Belge tarihi *", new DatePicker { SelectedDate = DateTime.Today });
        _expected = CompactField(right, type == "Order" ? "Beklenen teslim tarihi" : "Vade / kabul tarihi", new DatePicker { SelectedDate = type == "Order" ? DateTime.Today.AddDays(7) : DateTime.Today });
        _description = CompactField(right, "Açıklama", new TextBox { MaxLength = 500 }); Fields.Children.Add(top);
        Fields.Children.Add(new Border { Height = 1, Background = Ui.Brush("R3.Surface.Selected.Brush"), Margin = new Thickness(0, 12, 0, 11) });
        Fields.Children.Add(new TextBlock { Text = "Ürün satırları", FontSize = Ui.Font.Section, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Text.Primary.Brush") });
        var lineEditor = new Grid { Margin = new Thickness(0, 7, 0, 8) }; foreach (var width in new[] { 2.8, 0.8, 1.0, 0.8, 0.8, 0.8 }) lineEditor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });
        _product = LineField(lineEditor, 0, "Stok kodu / barkod / ürün *", new ComboBox { ItemsSource = products.DefaultView, DisplayMemberPath = "Display", SelectedValuePath = "Id", IsTextSearchEnabled = true, IsEditable = true, StaysOpenOnEdit = true });
        _quantity = LineField(lineEditor, 1, "Miktar", new TextBox { Text = "1", Tag = "Numeric" }); _price = LineField(lineEditor, 2, "Birim fiyat", new TextBox { Text = "0", Tag = "Numeric" }); _discount = LineField(lineEditor, 3, "İskonto %", new TextBox { Text = "0", Tag = "Numeric" }); _vat = LineField(lineEditor, 4, "KDV %", new TextBox { Text = "20", Tag = "Numeric" });
        var add = new Button { Content = "+ Satır Ekle", Height = 29, Margin = new Thickness(5, 20, 0, 0), Background = Ui.Brush("R3.Accent.Brush"), Foreground = Ui.Brush("R3.Text.OnAccent.Brush"), BorderThickness = new Thickness(0) }; add.Click += (_, _) => { try { Error.Text = ""; AddLine(products); } catch (Exception ex) { Error.Text = ex.Message; } }; Grid.SetColumn(add, 5); lineEditor.Children.Add(add); Fields.Children.Add(lineEditor);
        _linesGrid = new DataGrid { ItemsSource = _lines, AutoGenerateColumns = false, IsReadOnly = true, Height = 205, RowHeight = 27, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal };
        foreach (var column in new[] { ("Product", "Ürün", 2.8), ("Quantity", "Miktar", .8), ("UnitPrice", "Birim Fiyat", 1.0), ("DiscountRate", "İskonto %", .8), ("VatRate", "KDV %", .8), ("Total", "Toplam", 1.0) }) _linesGrid.Columns.Add(new DataGridTextColumn { Header = column.Item2, Width = new DataGridLength(column.Item3, DataGridLengthUnitType.Star), Binding = new Binding(column.Item1) { StringFormat = column.Item1 == "Product" ? null : "N2" } });
        Fields.Children.Add(_linesGrid); var remove = new Button { Content = "− Seçili Satırı Kaldır", Height = 27, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 0), Padding = new Thickness(9, 2, 9, 2) }; remove.Click += (_, _) => { if (_linesGrid.SelectedItem is LineRow row) _lines.Remove(row); }; Fields.Children.Add(remove);
        _product.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            try
            {
                if (_product.SelectedValue == null || string.IsNullOrWhiteSpace(_product.Text) || !_product.Text.Contains("—", StringComparison.Ordinal))
                {
                    var input = _product.Text.Trim();
                    var resolved = _resolver.ResolveForInventory(input, _company);
                    _product.SelectedValue = resolved.ProductId;
                }
                _quantity.Focus(); _quantity.SelectAll();
            }
            catch (KeyNotFoundException)
            {
                var input = _product.Text.Trim();
                var match = database.Query("SELECT id FROM products WHERE company_id=$c AND is_active=1 AND (code=$q COLLATE NOCASE OR name=$q COLLATE NOCASE) LIMIT 1", ("$c", _company), ("$q", input));
                if (match.Rows.Count == 0) Error.Text = $"Stok kodu, barkod veya ürün adı bulunamadı: {input}";
                else { _product.SelectedValue = match.Rows[0][0].ToString(); _quantity.Focus(); _quantity.SelectAll(); }
            }
            catch (Exception ex) { Error.Text = ex.Message; }
            e.Handled = true;
        };
        _quantity.KeyDown += (_, e) => { if (e.Key != Key.Enter) return; _price.Focus(); e.Handled = true; };
        _price.KeyDown += (_, e) => { if (e.Key != Key.Enter) return; _discount.Focus(); e.Handled = true; };
        _discount.KeyDown += (_, e) => { if (e.Key != Key.Enter) return; _vat.Focus(); e.Handled = true; };
        _vat.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            try { Error.Text = ""; AddLine(products); } catch (Exception ex) { Error.Text = ex.Message; }
            e.Handled = true;
        };
        Finish(Save); Loaded += (_, _) => _supplier.Focus();
    }

    private void AddLine(DataTable products)
    {
        if (_product.SelectedValue == null) throw new ArgumentException("Ürün seçin.");
        var quantity = Number(_quantity, "Miktar"); var price = Number(_price, "Birim fiyat", true); var discount = Number(_discount, "İskonto", true); var vat = Number(_vat, "KDV", true);
        if (quantity <= 0 || discount > 100 || vat > 100) throw new ArgumentException("Miktar sıfırdan büyük; oranlar 0–100 arasında olmalıdır.");
        var selected = ((DataRowView)_product.SelectedItem).Row; var gross = quantity * price; var net = gross - gross * discount / 100; var total = net + net * vat / 100;
        _lines.Add(new(_product.SelectedValue.ToString()!, selected["UnitId"].ToString()!, selected["Display"].ToString()!, quantity, price, discount, vat, total));
        _quantity.Text = "1"; _price.Text = "0"; _product.Focus();
    }
    private void Save()
    {
        if (_supplier.SelectedValue == null || _date.SelectedDate == null) throw new ArgumentException("Tedarikçi ve belge tarihi zorunludur.");
        _service.Create(new(_company, _branch, _warehouse, _supplier.SelectedValue.ToString()!, _type, _date.SelectedDate.Value, _expected.SelectedDate, "TRY", _description.Text, _lines.Select(x => new PurchaseLineEdit(x.ProductId, x.UnitId, x.Quantity, x.UnitPrice, x.DiscountRate, x.VatRate)).ToArray()), _user);
    }
    private static T CompactField<T>(Panel panel, string caption, T control) where T : Control { panel.Children.Add(new TextBlock { Text = caption, FontSize = Ui.Font.Grid, Foreground = Ui.Brush("R3.Text.Secondary.Brush"), Margin = new Thickness(0, 5, 0, 3) }); control.Height = 29; control.Padding = new Thickness(7, 3, 7, 3); panel.Children.Add(control); return control; }
    private static T LineField<T>(Grid grid, int column, string caption, T control) where T : Control { var panel = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 5, 0, 0, 0) }; panel.Children.Add(new TextBlock { Text = caption, FontSize = Ui.Font.Grid, Foreground = Ui.Brush("R3.Text.Secondary.Brush"), Margin = new Thickness(0, 0, 0, 3) }); control.Height = 29; control.Padding = new Thickness(6, 3, 6, 3); panel.Children.Add(control); Grid.SetColumn(panel, column); grid.Children.Add(panel); return control; }
    private static decimal Number(TextBox box, string label, bool allowZero = false) { if (!decimal.TryParse(box.Text, NumberStyles.Number, Turkish, out var value) || value < 0 || (!allowZero && value == 0)) throw new ArgumentException(label + " geçerli bir sayı olmalıdır."); return value; }
    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));
    private sealed record LineRow(string ProductId, string UnitId, string Product, decimal Quantity, decimal UnitPrice, decimal DiscountRate, decimal VatRate, decimal Total);
}
