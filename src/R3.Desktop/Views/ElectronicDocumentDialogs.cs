using System.Globalization;
using R3.Desktop.Design;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using R3.Desktop.Presentation;
using R3.Infrastructure;

namespace R3.Desktop.Views;

// Phase 9 (§24-27): the XML viewer, provider-response viewer and event timeline used to be private
// copies inside InvoiceDetailView (Phase 8). They now live here once, and InvoiceDetailView,
// OutgoingElectronicDocumentsView and FailedElectronicDocumentsView all call the same three
// methods - nothing here knows about invoices, sales documents or any specific screen, only the
// canonical ElectronicDocumentPayloadRow/ElectronicDocumentEventRow/ElectronicDocumentRow shapes.
internal static class ElectronicDocumentDialogs
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly Brush Muted = Ui.Brush("R3.Text.Secondary.Brush");
    private static readonly Brush BorderBrush = Ui.Brush("R3.Border.Brush");

    // §25/§27: shows the sendable (SignedXml if present, else latest UblXml) version by default,
    // with a picker when more than one version exists - never silently picks an old one.
    public static void ShowXmlViewer(IReadOnlyList<ElectronicDocumentPayloadRow> payloads, Window? owner = null)
    {
        var candidates = payloads.Where(p => p.PayloadType is ElectronicDocumentPayloadType.UblXml or ElectronicDocumentPayloadType.SignedXml)
            .OrderByDescending(p => p.PayloadType == ElectronicDocumentPayloadType.SignedXml).ThenByDescending(p => p.Version).ToList();
        if (candidates.Count == 0) { MessageBox.Show(owner, "Bu belge için kaydedilmiş UBL içeriği yok.", "UBL XML"); return; }

        var window = new Window { Title = "UBL XML", Width = 860, Height = 660, Owner = owner, Background = Ui.Brush("R3.Surface.Brush"),
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen };
        var root = new DockPanel { Margin = new Thickness(14) };
        var info = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var versionLabels = candidates.Select((p, i) => $"{(p.PayloadType == ElectronicDocumentPayloadType.SignedXml ? "İmzalı XML" : "UBL XML")} v{p.Version}{(i == 0 ? "  (gönderilecek)" : "")}").ToList();
        var picker = new ComboBox { Width = 240, ItemsSource = versionLabels, SelectedIndex = 0, Visibility = candidates.Count > 1 ? Visibility.Visible : Visibility.Collapsed };
        info.Children.Add(picker);
        var hashText = new TextBlock { Foreground = Muted, Margin = new Thickness(10, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(hashText);
        var copyHash = SmallButton("Hash Kopyala"); info.Children.Add(copyHash);
        var copyXml = SmallButton("XML Kopyala"); copyXml.Margin = new Thickness(6, 0, 0, 0); info.Children.Add(copyXml);
        DockPanel.SetDock(info, Dock.Top); root.Children.Add(info);
        var search = new TextBox { Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(search, Dock.Top); root.Children.Add(search);
        var textBox = new TextBox { IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.NoWrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(textBox);

        var pretty = "";
        void Show(int index)
        {
            var payload = candidates[index];
            pretty = PrettyXml(payload.Content);
            textBox.Text = pretty;
            hashText.Text = $"SHA-256: {Short(payload.ContentHash)}  •  v{payload.Version}  •  {payload.CreatedAt:dd.MM.yyyy HH:mm}";
        }
        picker.SelectionChanged += (_, _) => Show(picker.SelectedIndex);
        copyHash.Click += (_, _) => Clipboard.SetText(candidates[picker.SelectedIndex].ContentHash);
        copyXml.Click += (_, _) => Clipboard.SetText(pretty);
        search.TextChanged += (_, _) => { if (string.IsNullOrEmpty(search.Text)) return; var idx = pretty.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase); if (idx >= 0) { textBox.Select(idx, search.Text.Length); textBox.Focus(); } };

        Show(0);
        window.Content = root; window.ShowDialog();
    }

    // §26: structured fields come from the ElectronicDocumentRow itself (Provider/ProviderDocumentId/
    // EnvelopeId are real stored columns, not parsed out of the raw blob) - the raw
    // ProviderResponse payload, if one was ever saved, is shown read-only underneath. Never invents
    // a status code/message the provider didn't actually return.
    public static void ShowProviderResponse(ElectronicDocumentRow document, IReadOnlyList<ElectronicDocumentPayloadRow> payloads, Window? owner = null)
    {
        var response = payloads.Where(p => p.PayloadType == ElectronicDocumentPayloadType.ProviderResponse).OrderByDescending(p => p.Version).FirstOrDefault();
        var window = new Window { Title = "Provider Yanıtı", Width = 680, Height = 520, Owner = owner, Background = Ui.Brush("R3.Surface.Brush"),
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen };
        var panel = new StackPanel { Margin = new Thickness(16) };

        var grid = new Grid(); for (var i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(140) : new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 2; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Info(grid, 0, 0, "Provider", string.IsNullOrWhiteSpace(document.ProviderType) ? "—" : document.ProviderType);
        Info(grid, 0, 2, "Provider Belge No", document.ProviderDocumentId ?? "—");
        Info(grid, 1, 0, "Envelope ID", document.EnvelopeId ?? "—");
        Info(grid, 1, 2, "Son Hata Kodu", document.LastErrorCode ?? "—");
        panel.Children.Add(grid);

        if (response == null)
            panel.Children.Add(new TextBlock { Text = "Henüz sağlayıcıdan kaydedilmiş bir ham yanıt yok.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        else
        {
            panel.Children.Add(new TextBlock { Text = $"Yanıt Tarihi: {response.CreatedAt:dd.MM.yyyy HH:mm:ss}", Foreground = Muted, Margin = new Thickness(0, 12, 0, 0) });
            var raw = new TextBox { Text = response.Content, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Height = 300, Margin = new Thickness(0, 8, 0, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            panel.Children.Add(raw);
        }
        window.Content = panel; window.ShowDialog();
    }

    // §24/§31: consumed identically by InvoiceDetailView's E-Belge tab and the error center's detail
    // drawer - one chronological list built from electronic_document_events, nothing else.
    public static UIElement BuildEventTimeline(IReadOnlyList<ElectronicDocumentEventRow> events)
    {
        var timeline = new StackPanel();
        var ordered = events.OrderBy(e => e.OccurredAt).ToList();
        if (ordered.Count == 0) { timeline.Children.Add(new TextBlock { Text = "Henüz olay kaydı yok.", Foreground = Muted }); return timeline; }
        foreach (var e in ordered)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            row.Children.Add(new TextBlock { Text = e.OccurredAt.ToString("dd.MM.yyyy HH:mm:ss", Turkish), Foreground = Muted, Width = 150 });
            row.Children.Add(new TextBlock { Text = EDocumentPresentation.EventLabel(e.EventType), FontWeight = FontWeights.Medium });
            if (!string.IsNullOrWhiteSpace(e.ProviderMessage)) row.Children.Add(new TextBlock { Text = $"  — {e.ProviderMessage}", Foreground = Muted });
            timeline.Children.Add(row);
        }
        return timeline;
    }

    // §11/§24: standalone "Olay Geçmişi" dialog for screens with no detail tab of their own (Giden
    // Belgeler / Hatalı Belgeler context menu) - wraps the same BuildEventTimeline used inline on
    // InvoiceDetailView's E-Belge tab.
    public static void ShowEventTimeline(ElectronicDocumentRow document, IReadOnlyList<ElectronicDocumentEventRow> events, Window? owner = null)
    {
        var window = new Window { Title = $"Olay Geçmişi — {document.DocumentNumber ?? document.Uuid}", Width = 560, Height = 520, Owner = owner, Background = Ui.Brush("R3.Surface.Brush"),
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen };
        window.Content = new ScrollViewer { Content = BuildEventTimeline(events), Margin = new Thickness(16), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.ShowDialog();
    }

    // §14-15: read-only outbox row detail. IdempotencyKey has no edit control anywhere in this
    // dialog - only a copy button - so there is no code path that could let a user change it.
    public static void ShowOutboxDetail(ElectronicDocumentOutboxRow row, Window? owner = null)
    {
        var window = new Window { Title = "Kuyruk Kaydı", Width = 560, Height = 560, Owner = owner, Background = Ui.Brush("R3.Surface.Brush"),
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen };
        var panel = new StackPanel { Margin = new Thickness(16) };
        var grid = new Grid(); for (var i = 0; i < 2; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? new GridLength(150) : new GridLength(1, GridUnitType.Star) });
        var fields = new (string Label, string Value)[]
        {
            ("Kuyruk Kaydı", row.Id), ("E-Belge Kaydı", row.ElectronicDocumentId), ("İşlem", InventoryPresentation.OperationLabel(row.OperationType.ToString())),
            ("Durum", EDocumentPresentation.OutboxStatusLabel(row.Status)), ("Deneme", $"{row.AttemptCount} / {row.MaxAttempts}"),
            ("Oluşturma", row.CreatedAt.ToString("dd.MM.yyyy HH:mm:ss", Turkish)), ("Kullanılabilir", row.AvailableAt.ToString("dd.MM.yyyy HH:mm:ss", Turkish)),
            ("Son Deneme", row.LastAttemptAt?.ToString("dd.MM.yyyy HH:mm:ss", Turkish) ?? "—"), ("Sonraki Deneme", row.NextAttemptAt?.ToString("dd.MM.yyyy HH:mm:ss", Turkish) ?? "—"),
            ("Kilit Sahibi", row.LockedBy ?? "—"), ("Kilit Zamanı", row.LockedAt?.ToString("dd.MM.yyyy HH:mm:ss", Turkish) ?? "—"),
            ("Correlation ID", row.CorrelationId ?? "—"), ("Son Hata Kodu", row.LastErrorCode ?? "—"), ("Son Hata", row.LastErrorMessage ?? "—"),
        };
        for (var i = 0; i < fields.Length; i++) { grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Info(grid, i, 0, fields[i].Label, fields[i].Value); }
        panel.Children.Add(grid);

        var idempotencyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        idempotencyRow.Children.Add(new TextBlock { Text = "Idempotency Key: ", Foreground = Muted, FontSize = Ui.Font.Caption, VerticalAlignment = VerticalAlignment.Center });
        idempotencyRow.Children.Add(new TextBlock { Text = row.IdempotencyKey, FontFamily = new FontFamily("Consolas"), FontSize = Ui.Font.Caption, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Tekrarlanan gönderimlerde aynı uzak belgeyi güvenli şekilde tanımlamak için kullanılır." });
        var copyKey = SmallButton("Kopyala"); copyKey.Margin = new Thickness(8, 0, 0, 0); copyKey.Click += (_, _) => Clipboard.SetText(row.IdempotencyKey);
        idempotencyRow.Children.Add(copyKey);
        panel.Children.Add(idempotencyRow);

        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.ShowDialog();
    }

    private static string PrettyXml(string xml) { try { return System.Xml.Linq.XDocument.Parse(xml).ToString(); } catch { return xml; } }
    private static string Short(string? hash) => string.IsNullOrEmpty(hash) ? "—" : hash.Length <= 16 ? hash : hash[..8] + "…" + hash[^8..];
    private static void Info(Grid grid, int row, int column, string label, string value)
    {
        var caption = new TextBlock { Text = label, Foreground = Muted, FontSize = Ui.Font.Caption, Margin = new Thickness(0, 6, 9, 2) }; Grid.SetRow(caption, row); Grid.SetColumn(caption, column); grid.Children.Add(caption);
        var text = new TextBlock { Text = value, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 6, 12, 2), TextWrapping = TextWrapping.Wrap }; Grid.SetRow(text, row); Grid.SetColumn(text, column + 1); grid.Children.Add(text);
    }
    private static Button SmallButton(string text) => new() { Content = text, Height = 29, Padding = new Thickness(10, 3, 10, 3), Background = Ui.Brush("R3.Surface.Brush"), BorderBrush = BorderBrush, Foreground = Ui.Brush("R3.Text.Primary.Brush") };
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
