using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using R3.Desktop.ContextActions;
using R3.Infrastructure;

namespace R3.Desktop.Views;

/// <summary>Satış İadeleri / Satınalma İadeleri list (LocalReturnService).</summary>
public static class ReturnViews
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static UIElement Returns(StoreDatabase db, string companyId, ReturnDirection direction, string userName, Action<string> openAccount, Action<string> openInvoice)
    {
        var service = new LocalReturnService(db);
        var sales = direction == ReturnDirection.Sales;
        var root = new DockPanel { Margin = new Thickness(18) };
        var title = new TextBlock { Text = sales ? "Satış İadeleri" : "Satınalma İadeleri", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(47, 56, 63)) };
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var help = new TextBlock
        {
            Text = sales
                ? "Kesinleşmiş satış faturasından iade: cariye alacak yazılır, ürünler faturadaki depoya geri girer (fatura fiyatı, iskontosu ve KDV'si ile). Bir satırdan en fazla faturalanan − önceden iade edilen miktar iade edilebilir. İptal, tüm kayıtları ters kayıtla geri alır."
                : "Kesinleşmiş alış faturasından iade: tedarikçiye borç yazılır, ürünler depodan alış maliyetiyle çıkar (deponun negatif stok politikası geçerlidir). İptal, tüm kayıtları ters kayıtla geri alır.",
            Foreground = Brushes.DimGray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10)
        };
        DockPanel.SetDock(help, Dock.Top); root.Children.Add(help);
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        var search = new TextBox { Width = 220, Padding = new Thickness(6, 3, 6, 3), ToolTip = "İade no, fatura no veya cari" };
        var grid = new DataGrid { Style = (Style)System.Windows.Application.Current.FindResource("ProfessionalDataGridStyle"), IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column };
        foreach (var (header, path, width, format) in new (string, string, double, string?)[] { ("İade No", "IadeNo", 130, null), ("Tarih", "Tarih", 95, null), ("Cari Kodu", "CariKodu", 100, null), ("Cari", "Cari", 220, null), ("Fatura No", "FaturaNo", 130, null),
                     ("Net", "Net", 100, "N2"), ("KDV", "Kdv", 90, "N2"), ("Toplam", "Toplam", 110, "N2"), ("Durum", "Durum", 90, null), ("Açıklama", "Aciklama", 200, null) })
            grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path) { StringFormat = format, ConverterCulture = Turkish }, Width = width });

        void Refresh() => grid.ItemsSource = service.Search(companyId, direction, search.Text).DefaultView;
        void New()
        {
            var dialog = new ReturnDialog(service, companyId, direction) { Owner = Window.GetWindow(root) };
            if (dialog.ShowDialog() != true || dialog.Draft is not { } draft) return;
            try
            {
                var result = service.Post(draft, userName);
                Refresh();
                MessageBox.Show(Window.GetWindow(root)!, $"{result.DocumentNo} numaralı iade kesinleşti. Toplam: {result.GrandTotal.ToString("N2", Turkish)} ₺", title.Text, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
            { MessageBox.Show(Window.GetWindow(root)!, ex.Message, "İade kaydedilemedi", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        void Cancel()
        {
            if (grid.SelectedItem is not DataRowView row) return;
            var prompt = new TextPromptDialog($"İadeyi İptal Et — {row["IadeNo"]}", "İptal nedeni *") { Owner = Window.GetWindow(root) };
            if (prompt.ShowDialog() != true) return;
            try { service.Cancel(row["Id"].ToString()!, prompt.Value, userName); Refresh(); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { MessageBox.Show(Window.GetWindow(root)!, ex.Message, "İptal edilemedi", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        void ShowLines()
        {
            if (grid.SelectedItem is not DataRowView row) return;
            var lines = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, ItemsSource = service.Lines(row["Id"].ToString()!).DefaultView, Margin = new Thickness(10) };
            new Window { Title = $"İade Satırları — {row["IadeNo"]}", Width = 760, Height = 360, Owner = Window.GetWindow(root), WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = lines }.ShowDialog();
        }
        bool Posted(object? x) => x is DataRowView r && r["Durum"].ToString() == "Kesinleşti";
        ErpGridContext.Register(grid, sales ? "sales.returns" : "purchasing.returns",
        [
            new ContextActionDefinition("return.lines", "İade Satırları", "", "", 10, ContextActionGroup.Primary, _ => { ShowLines(); return Task.FromResult(ContextActionResult.Ok()); }),
            new ContextActionDefinition("return.account", "Cari Kartını Aç", "accounts.view", "", 10, ContextActionGroup.Related, x => { if (x is DataRowView r) openAccount(r["CariId"].ToString()!); return Task.FromResult(ContextActionResult.Ok()); }),
            new ContextActionDefinition("return.invoice", "Kaynak Faturayı Aç", "", "", 20, ContextActionGroup.Related, x => { if (x is DataRowView r) openInvoice(r["FaturaId"].ToString()!); return Task.FromResult(ContextActionResult.Ok()); }),
            new ContextActionDefinition("return.cancel", "İadeyi İptal Et", sales ? "invoices.reverse" : "purchasing.document.cancel", "", 10, ContextActionGroup.Critical, _ => { Cancel(); return Task.FromResult(ContextActionResult.Ok(refresh: true)); }, Posted, Posted)
        ], () => { Refresh(); return Task.CompletedTask; }, "ReturnDocument");
        foreach (var (text, action, primary) in new (string, Action, bool)[] { ("+ Faturadan İade (F2)", New, true), ("Satırlar", ShowLines, false), ("İptal Et", Cancel, false), ("Yenile (F5)", Refresh, false) })
        {
            var button = new Button { Content = text, Height = 26, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
            if (primary) { button.Background = new SolidColorBrush(Color.FromRgb(22, 124, 130)); button.Foreground = Brushes.White; button.BorderThickness = new Thickness(0); }
            button.Click += (_, _) => action(); bar.Children.Add(button);
        }
        bar.Children.Add(new TextBlock { Text = "Ara:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 5, 0) }); bar.Children.Add(search);
        KeyboardInteractionService.AttachDebouncedSearch(search, Refresh);
        KeyboardInteractionService.AttachListShortcuts(root, search, New, ShowLines, Refresh);
        root.Children.Add(grid); Refresh();
        return root;
    }
}

/// <summary>Pick a posted invoice, type the quantity to return per line, see the totals, save.</summary>
public sealed class ReturnDialog : EditorDialog
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    public ReturnDraft? Draft { get; private set; }

    public ReturnDialog(LocalReturnService service, string companyId, ReturnDirection direction) : base(direction == ReturnDirection.Sales ? "Satış Faturasından İade" : "Alış Faturasından İade")
    {
        Width = 860; MaxHeight = 760;
        var invoices = service.SourceDocuments(companyId, direction);
        invoices.Columns.Add("Display", typeof(string));
        foreach (DataRow r in invoices.Rows) r["Display"] = $"{r["BelgeNo"]}  •  {r["Tarih"]}  •  {r["Cari"]}  •  {Convert.ToDecimal(r["Tutar"]).ToString("N2", Turkish)} ₺{(Convert.ToDecimal(r["IadeEdilen"]) > 0 ? $"  (iade: {Convert.ToDecimal(r["IadeEdilen"]).ToString("N2", Turkish)})" : "")}";
        var invoice = Field("Kesinleşmiş fatura * (belge no veya cari adı yazarak arayın)", new ComboBox { ItemsSource = invoices.DefaultView, DisplayMemberPath = "Display", SelectedValuePath = "Id", IsEditable = true, IsTextSearchEnabled = true });
        var date = Field("İade tarihi *", new DatePicker { SelectedDate = DateTime.Today });
        var description = Field("Açıklama / iade nedeni", new TextBox { MaxLength = 300 });
        var lines = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, Height = 260, Margin = new Thickness(0, 10, 0, 0), HeadersVisibility = DataGridHeadersVisibility.Column };
        foreach (var (header, path, width, format) in new (string, string, double, string?)[] { ("Stok Kodu", "StokKodu", 110, null), ("Stok Adı", "StokAdi", 220, null), ("Birim", "Birim", 55, null), ("Faturalanan", "Miktar", 85, "N2"),
                     ("Önceki İade", "IadeEdilen", 85, "N2"), ("Kalan", "Kalan", 70, "N2"), ("Birim Fiyat", "BirimFiyat", 85, "N2"), ("İsk. %", "Iskonto", 55, "N2"), ("KDV %", "Kdv", 55, "N0") })
            lines.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path) { StringFormat = format, ConverterCulture = Turkish }, Width = width, IsReadOnly = true });
        lines.Columns.Add(new DataGridTextColumn { Header = "İade Miktarı ✎", Binding = new Binding("Iade") { ConverterCulture = Turkish, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 95 });
        Fields.Children.Add(lines);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var all = new Button { Content = "Kalanın tamamını iade et", Height = 24, Padding = new Thickness(8, 2, 8, 2) };
        var total = new TextBlock { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        tools.Children.Add(all); tools.Children.Add(total); Fields.Children.Add(tools);
        DataTable? table = null;
        void UpdateTotal()
        {
            if (table == null) { total.Text = ""; return; }
            decimal net = 0, vat = 0;
            foreach (DataRow r in table.Rows)
            {
                var q = r["Iade"] is decimal d ? d : 0; var gross = q * (decimal)r["BirimFiyat"]; var n = gross - gross * (decimal)r["Iskonto"] / 100m;
                net += Math.Round(n, 2); vat += Math.Round(n * (decimal)r["Kdv"] / 100m, 2);
            }
            total.Text = $"İade toplamı: {net.ToString("N2", Turkish)} + KDV {vat.ToString("N2", Turkish)} = {(net + vat).ToString("N2", Turkish)} ₺";
        }
        invoice.SelectionChanged += (_, _) =>
        {
            if (invoice.SelectedValue is not string id) { lines.ItemsSource = null; table = null; UpdateTotal(); return; }
            table = service.SourceLines(direction, id); table.Columns.Add("Iade", typeof(decimal));
            foreach (DataRow r in table.Rows) r["Iade"] = 0m;
            table.ColumnChanged += (_, e) => { if (e.Column?.ColumnName == "Iade") UpdateTotal(); };
            lines.ItemsSource = table.DefaultView; UpdateTotal();
        };
        all.Click += (_, _) => { if (table == null) return; foreach (DataRow r in table.Rows) r["Iade"] = r["Kalan"]; UpdateTotal(); };
        Finish(() =>
        {
            lines.CommitEdit(DataGridEditingUnit.Row, true);
            if (invoice.SelectedValue is not string id || table == null) throw new ArgumentException("Önce bir fatura seçin.");
            if (date.SelectedDate == null) throw new ArgumentException("İade tarihi seçin.");
            var items = table.Rows.Cast<DataRow>().Where(r => r["Iade"] is decimal q && q > 0).Select(r => new ReturnLineEdit(r["LineId"].ToString()!, (decimal)r["Iade"])).ToList();
            if (items.Count == 0) throw new ArgumentException("En az bir satıra iade miktarı girin.");
            var over = table.Rows.Cast<DataRow>().FirstOrDefault(r => r["Iade"] is decimal q && q > (decimal)r["Kalan"]);
            if (over != null) throw new ArgumentException($"{over["StokKodu"]}: iade miktarı kalan miktarı ({(decimal)over["Kalan"]:N2}) aşıyor.");
            Draft = new ReturnDraft(direction, companyId, id, date.SelectedDate.Value, description.Text, items);
        });
        AcceptButton!.Content = "İadeyi Kesinleştir"; AcceptButton.Width = 130;
        Loaded += (_, _) => invoice.Focus();
    }
}
