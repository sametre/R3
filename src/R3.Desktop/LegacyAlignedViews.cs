using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using R3.Infrastructure;
// This file has its own static "Border" brush field and "Grid()" helper method, both of which
// shadow the WPF types of the same name for static member access (Border.BackgroundProperty,
// Grid.SetColumn) - these aliases sidestep that collision wherever it matters.
using WpfBorder = System.Windows.Controls.Border;
using WpfGrid = System.Windows.Controls.Grid;

namespace R3.Desktop;

internal static class LegacyAlignedViews
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly Brush Muted = Brush("#767676");
    private static readonly Brush Border = Brush("#DEDEDE");

    public static UIElement CreateShipmentQueue(StoreDatabase database, string companyId)
    {
        var service = new LocalShipmentService(database);
        var page = Page("Bekleyen sevkiyatlar", "Siparişten sevk emrine, yüklemeye ve teslim sonucuna uzanan operasyon kuyruğu.");
        var cards = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        page.Children.Add(cards);
        var toolbar = Toolbar(out var search, "Sevk no, cari veya plaka ara");
        page.Children.Add(toolbar);
        var grid = Grid();
        Columns(grid, ("SevkNo", "Sevk No", 115d), ("PlanlananSevk", "Planlanan", 130d), ("CariKodu", "Cari Kodu", 100d),
            ("Cari", "Cari", 210d), ("Sube", "Şube", 120d), ("Depo", "Depo", 120d), ("Satir", "Satır", 60d),
            ("PlanlananMiktar", "Planlanan", 90d), ("SevkEdilen", "Sevk Edilen", 90d), ("SevkBolgesi", "Sevk Bölgesi", 110d),
            ("Nakliyeci", "Nakliyeci", 110d), ("Plaka", "Plaka", 90d), ("Surucu", "Sürücü", 120d), ("Durum", "Durum", 90d));
        page.Children.Add(grid);

        void Refresh()
        {
            cards.Children.Clear();
            var summary = service.GetSummary(companyId);
            cards.Children.Add(Card("Bekleyen", summary.Pending.ToString("N0", Turkish), "#626262"));
            cards.Children.Add(Card("Bugün planlanan", summary.PlannedToday.ToString("N0", Turkish), "#2A8F7B"));
            cards.Children.Add(Card("Yolda", summary.InTransit.ToString("N0", Turkish), "#7E7E7E"));
            cards.Children.Add(Card("Geciken", summary.Overdue.ToString("N0", Turkish), "#C4514B"));
            grid.ItemsSource = service.SearchPending(companyId, search.Text).DefaultView;
        }

        ((Button)toolbar.Children[0]).Click += (_, _) => Refresh();
        KeyboardInteractionService.AttachDebouncedSearch(search, Refresh);
        Refresh();
        return Wrap(page);
    }

    public static UIElement CreateFinanceOverview(StoreDatabase database, string companyId)
    {
        var page = Page("Finans genel bakış", "Kasa, cari ve satış hareketlerini aynı finansal görünümde özetler.");
        var totals = database.Query("""
            SELECT
              (SELECT COALESCE(SUM(balance),0) FROM cash_balances WHERE company_id=$company) AS cash,
              (SELECT COALESCE(SUM(CASE WHEN ab.balance>0 THEN ab.balance ELSE 0 END),0) FROM account_balances ab JOIN accounts a ON a.id=ab.account_id WHERE ab.company_id=$company AND a.account_type IN ('Customer','CustomerAndSupplier')) AS receivable,
              (SELECT COALESCE(SUM(CASE WHEN ab.balance<0 THEN -ab.balance ELSE 0 END),0) FROM account_balances ab JOIN accounts a ON a.id=ab.account_id WHERE ab.company_id=$company AND a.account_type IN ('Supplier','CustomerAndSupplier')) AS payable,
              (SELECT COALESCE(SUM(grand_total),0) FROM sales_documents WHERE company_id=$company AND status='Posted' AND date(document_date)=date('now','localtime')) AS today_sales,
              (SELECT COALESCE(SUM(grand_total),0) FROM sales_documents WHERE company_id=$company AND status='Posted' AND strftime('%Y-%m',document_date)=strftime('%Y-%m','now','localtime')) AS month_sales
            """, ("$company", companyId)).Rows[0];
        var cards = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        cards.Children.Add(Card("Toplam kasa", Money(totals["cash"]), "#575757"));
        cards.Children.Add(Card("Müşteri alacağı", Money(totals["receivable"]), "#626262"));
        cards.Children.Add(Card("Tedarikçi borcu", Money(totals["payable"]), "#C0832B"));
        cards.Children.Add(Card("Bugünkü satış", Money(totals["today_sales"]), "#2A8F7B"));
        cards.Children.Add(Card("Aylık satış", Money(totals["month_sales"]), "#7E7E7E"));
        page.Children.Add(cards);
        page.Children.Add(Section("Son finans hareketleri"));
        var grid = Grid();
        Columns(grid, ("Tarih", "Tarih", 135d), ("Kaynak", "Kaynak", 90d), ("BelgeNo", "Belge No", 110d),
            ("Cari", "Cari / Kasa", 220d), ("Aciklama", "Açıklama", 260d), ("Giris", "Giriş", 100d),
            ("Cikis", "Çıkış", 100d), ("Doviz", "Döviz", 65d), ("Durum", "Durum", 85d));
        grid.ItemsSource = database.Query("""
            SELECT ct.transaction_date AS Tarih, 'Kasa' AS Kaynak, COALESCE(ct.document_number,'') AS BelgeNo,
                   ca.code || ' — ' || ca.name AS Cari, ct.description AS Aciklama,
                   CASE WHEN ct.direction='In' THEN ct.local_amount ELSE 0 END AS Giris,
                   CASE WHEN ct.direction='Out' THEN ct.local_amount ELSE 0 END AS Cikis,
                   ct.currency_code AS Doviz, ct.status AS Durum
            FROM cash_transactions ct JOIN cash_accounts ca ON ca.id=ct.cash_account_id WHERE ct.company_id=$company
            UNION ALL
            SELECT at.transaction_at, 'Cari', COALESCE(at.document_no,''), a.code || ' — ' || a.name,
                   at.description, at.credit, at.debit, at.currency_code, 'Posted'
            FROM account_transactions at JOIN accounts a ON a.id=at.account_id WHERE at.company_id=$company
            ORDER BY Tarih DESC LIMIT 200
            """, ("$company", companyId)).DefaultView;
        page.Children.Add(grid);
        return Wrap(page);
    }

    public static UIElement CreateElectronicQueue(StoreDatabase database, string companyId)
    {
        var page = Page("Gönderim kuyruğu", "E-Fatura ve e-Arşiv belgelerinin üretim, kuyruk ve gönderim durumları.");
        var counts = database.Query("""
            SELECT COUNT(*) AS total,
                   SUM(CASE WHEN status IN ('Ready','Generated','Queued') THEN 1 ELSE 0 END) AS waiting,
                   SUM(CASE WHEN status IN ('Sent','Delivered','Accepted') THEN 1 ELSE 0 END) AS successful,
                   SUM(CASE WHEN status IN ('Failed','Rejected') THEN 1 ELSE 0 END) AS failed
            FROM electronic_documents WHERE company_id=$company
            """, ("$company", companyId)).Rows[0];
        var cards = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        cards.Children.Add(Card("Toplam belge", Number(counts["total"]), "#626262"));
        cards.Children.Add(Card("Bekleyen", Number(counts["waiting"]), "#C0832B"));
        cards.Children.Add(Card("Başarılı", Number(counts["successful"]), "#2A8F7B"));
        cards.Children.Add(Card("Hatalı", Number(counts["failed"]), "#C4514B"));
        page.Children.Add(cards);
        var toolbar = Toolbar(out var search, "Belge no, cari veya UUID ara");
        page.Children.Add(toolbar);
        var grid = Grid();
        Columns(grid, ("BelgeNo", "Belge No", 115d), ("BelgeTipi", "Belge Tipi", 95d), ("Yon", "Yön", 75d),
            ("Tarih", "Tarih", 115d), ("Cari", "Cari", 210d), ("Tutar", "Tutar", 95d), ("BelgeDurumu", "Belge Durumu", 105d),
            ("KuyrukDurumu", "Kuyruk", 90d), ("Deneme", "Deneme", 65d), ("SonrakiDeneme", "Sonraki Deneme", 130d),
            ("Hata", "Son Hata", 260d));
        void Refresh() => grid.ItemsSource = database.Query("""
            SELECT COALESCE(d.document_number,'Taslak') AS BelgeNo, d.document_type AS BelgeTipi, d.direction AS Yon,
                   d.issue_date AS Tarih, COALESCE(a.code || ' — ' || a.name,'') AS Cari, d.payable_amount AS Tutar,
                   d.status AS BelgeDurumu, COALESCE(o.status,'—') AS KuyrukDurumu, COALESCE(o.attempt_count,0) AS Deneme,
                   o.next_attempt_at AS SonrakiDeneme, COALESCE(o.last_error_message,d.last_error_message,'') AS Hata
            FROM electronic_documents d LEFT JOIN accounts a ON a.id=d.account_id
            LEFT JOIN electronic_document_outbox o ON o.id=(SELECT x.id FROM electronic_document_outbox x WHERE x.electronic_document_id=d.id ORDER BY x.created_at DESC LIMIT 1)
            WHERE d.company_id=$company AND ($search='' OR COALESCE(d.document_number,'') LIKE $like OR d.uuid LIKE $like OR a.code LIKE $like OR a.name LIKE $like)
            ORDER BY d.created_at DESC LIMIT 300
            """, ("$company", companyId), ("$search", search.Text.Trim()), ("$like", $"%{search.Text.Trim()}%")).DefaultView;
        ((Button)toolbar.Children[0]).Click += (_, _) => Refresh();
        KeyboardInteractionService.AttachDebouncedSearch(search, Refresh);
        page.Children.Add(grid);
        Refresh();
        return Wrap(page);
    }

    public static UIElement CreateGeneralSettings(StoreDatabase database, string companyId)
    {
        var page = Page("Genel ayarlar", "Firma, şube, depo ve kullanıcı yetki yapısının merkezi görünümü.");
        var counts = database.Query("""
            SELECT (SELECT COUNT(*) FROM companies WHERE is_active=1) AS companies,
                   (SELECT COUNT(*) FROM branches WHERE company_id=$company AND is_active=1) AS branches,
                   (SELECT COUNT(*) FROM warehouses WHERE company_id=$company AND is_active=1) AS warehouses,
                   (SELECT COUNT(*) FROM users WHERE is_active=1) AS users,
                   (SELECT COUNT(*) FROM roles WHERE is_active=1) AS roles,
                   (SELECT COUNT(*) FROM permissions) AS permissions
            """, ("$company", companyId)).Rows[0];
        var cards = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        cards.Children.Add(Card("Firma", Number(counts["companies"]), "#626262"));
        cards.Children.Add(Card("Şube", Number(counts["branches"]), "#2A8F7B"));
        cards.Children.Add(Card("Depo", Number(counts["warehouses"]), "#7E7E7E"));
        cards.Children.Add(Card("Kullanıcı", Number(counts["users"]), "#C0832B"));
        cards.Children.Add(Card("Rol / Yetki", $"{Number(counts["roles"])} / {Number(counts["permissions"])}", "#C4514B"));
        page.Children.Add(cards);
        page.Children.Add(Section("Organizasyon ve güvenlik durumu"));
        var grid = Grid();
        Columns(grid, ("Tur", "Tür", 110d), ("Kod", "Kod", 135d), ("Ad", "Ad", 250d), ("Baglam", "Bağlam", 230d), ("Durum", "Durum", 90d));
        grid.ItemsSource = database.Query("""
            SELECT 'Firma' AS Tur, code AS Kod, name AS Ad, legal_name AS Baglam, CASE is_active WHEN 1 THEN 'Aktif' ELSE 'Pasif' END AS Durum FROM companies
            UNION ALL SELECT 'Şube', b.code, b.name, c.code || ' — ' || c.name, CASE b.is_active WHEN 1 THEN 'Aktif' ELSE 'Pasif' END FROM branches b JOIN companies c ON c.id=b.company_id WHERE b.company_id=$company
            UNION ALL SELECT 'Depo', w.code, w.name, b.code || ' — ' || b.name, CASE w.is_active WHEN 1 THEN 'Aktif' ELSE 'Pasif' END FROM warehouses w JOIN branches b ON b.id=w.branch_id WHERE w.company_id=$company
            UNION ALL SELECT 'Kullanıcı', username, display_name, 'Oturum hesabı', CASE is_active WHEN 1 THEN 'Aktif' ELSE 'Pasif' END FROM users
            UNION ALL SELECT 'Rol', code, name, 'Yetki grubu', CASE is_active WHEN 1 THEN 'Aktif' ELSE 'Pasif' END FROM roles
            ORDER BY Tur, Kod
            """, ("$company", companyId)).DefaultView;
        page.Children.Add(grid);
        var note = new Border { Background = Brush("#F4F4F4"), BorderBrush = Brush("#D8D8D8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12), Margin = new Thickness(0, 12, 0, 0), Child = new TextBlock { Text = "Legacy eşleme: SIRKET → Firma, SUBE → Şube, DEPO → Depo, MNUSER/MNUSERGRUP → Kullanıcı/Rol, YETKI/YETKITANIM → Rol yetkileri.", Foreground = Brush("#4E4E4E"), TextWrapping = TextWrapping.Wrap } };
        page.Children.Add(note);
        return Wrap(page);
    }

    private static StackPanel Page(string title, string subtitle)
    {
        var page = new StackPanel { Margin = new Thickness(24) };
        page.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brush("#353535") });
        page.Children.Add(new TextBlock { Text = subtitle, Foreground = Muted, FontSize = 13, Margin = new Thickness(0, 4, 0, 18) });
        return page;
    }

    private static ScrollViewer Wrap(StackPanel page) => new() { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private static TextBlock Section(string text) => new() { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#4E4E4E"), Margin = new Thickness(0, 4, 0, 9) };

    // Flat "SaaS stat tile" card: a thin colored accent bar ties the number back to its meaning
    // (mirrors the color-coded top menu), a soft shadow lifts it off the gray tab canvas instead
    // of the old flat white-on-white rectangle, and rounded corners match the app's other surfaces
    // (menu flyouts, dialog buttons) so opened tabs read as one consistent design language.
    private static Border Card(string caption, string value, string color)
    {
        var body = new StackPanel { Margin = new Thickness(14, 10, 16, 10), VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(new TextBlock { Text = caption, Foreground = Muted, FontSize = 10.5, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = value, Foreground = Brush(color), FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
        var content = new WpfGrid { MinWidth = 168 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        var accent = new WpfBorder { Background = Brush(color), CornerRadius = new CornerRadius(3, 0, 0, 3) };
        WpfGrid.SetColumn(accent, 0); WpfGrid.SetColumn(body, 1);
        content.Children.Add(accent); content.Children.Add(body);
        return new Border
        {
            Child = content, Margin = new Thickness(0, 0, 10, 10), Background = Brushes.White,
            BorderBrush = Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, Opacity = 0.08, BlurRadius = 10, ShadowDepth = 2, Direction = 270 }
        };
    }

    private static StackPanel Toolbar(out TextBox search, string hint)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var refresh = new Button
        {
            Content = "↻  Yenile", Height = 32, Padding = new Thickness(12, 3, 12, 3), Cursor = System.Windows.Input.Cursors.Hand,
            Background = Brushes.White, BorderBrush = Border, BorderThickness = new Thickness(1), Foreground = Brush("#454545")
        };
        refresh.Template = RoundedButtonTemplate();
        bar.Children.Add(refresh);
        bar.Children.Add(new TextBlock { Text = "Ara", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 6, 0), Foreground = Muted });
        search = new TextBox { Width = 280, Height = 32, Padding = new Thickness(10, 5, 10, 5), ToolTip = hint, VerticalContentAlignment = VerticalAlignment.Center };
        bar.Children.Add(search);
        return bar;
    }

    // Self-contained flat-button chrome (rounded corners, accent-tinted hover/press) so the
    // toolbar's "Yenile" button matches the app's new visual language without changing the
    // default WPF Button look app-wide, which other screens' buttons still rely on as-is.
    private static ControlTemplate RoundedButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(WpfBorder), "Bd");
        border.SetValue(WpfBorder.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        border.SetValue(WpfBorder.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
        border.SetValue(WpfBorder.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
        border.SetValue(WpfBorder.CornerRadiusProperty, new CornerRadius(7));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Button.PaddingProperty));
        border.AppendChild(content);
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(WpfBorder.BackgroundProperty, Brush("#FFF1E8"), "Bd"));
        hover.Setters.Add(new Setter(WpfBorder.BorderBrushProperty, Brush("#F97316"), "Bd"));
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(WpfBorder.BackgroundProperty, Brush("#FFE2CC"), "Bd"));
        template.Triggers.Add(hover); template.Triggers.Add(pressed);
        return template;
    }

    private static DataGrid Grid() => new()
    {
        Style = (Style)System.Windows.Application.Current.FindResource("ProfessionalDataGridStyle"), AutoGenerateColumns = false,
        IsReadOnly = true, CanUserAddRows = false, MinHeight = 250, HeadersVisibility = DataGridHeadersVisibility.Column
    };

    private static void Columns(DataGrid grid, params (string Key, string Header, double Width)[] columns)
    {
        foreach (var column in columns)
            grid.Columns.Add(new DataGridTextColumn { Header = column.Header, Binding = new Binding(column.Key), Width = new DataGridLength(column.Width) });
    }

    private static string Money(object value) => Convert.ToDecimal(value == DBNull.Value ? 0 : value).ToString("C2", Turkish);
    private static string Number(object value) => Convert.ToInt32(value == DBNull.Value ? 0 : value).ToString("N0", Turkish);
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
}
