using System.Windows;
using R3.Desktop.Design;
using System.Windows.Controls;
using System.Windows.Media;
using R3.Desktop.Presentation;
using R3.Infrastructure;
using static R3.Desktop.Views.ElectronicDocumentUiKit;

namespace R3.Desktop.Views;

// Phase 9 (§38-39): a document whose source is not (yet) a SalesInvoice - e.g. a future EDespatch -
// still needs a detail screen. Deliberately generic: Belge No / Belge Tipi / Kaynak Belge / Cari /
// UUID / Status only, plus the same shared XML/provider-response/event-timeline actions
// InvoiceDetailView's E-Belge tab uses - never an invoice-shaped layout forced onto a non-invoice
// document.
internal static class ElectronicDocumentGenericDetailView
{
    public static UIElement Create(StoreDatabase database, string electronicDocumentId)
    {
        var documents = new LocalElectronicDocumentService(database);
        var document = documents.Get(electronicDocumentId);
        var panel = new StackPanel { Margin = new Thickness(18) };
        if (document == null) { panel.Children.Add(new TextBlock { Text = "Elektronik belge bulunamadı.", Foreground = Muted }); return panel; }

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock { Text = document.DocumentNumber ?? document.Uuid, FontSize = Ui.Font.Title, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Text.Primary.Brush") });
        var statusBadge = Badge(EDocumentPresentation.StatusLabel(document.Status), EDocumentPresentation.StatusColor(document.Status));
        statusBadge.Margin = new Thickness(10, 0, 0, 0); statusBadge.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(statusBadge);
        panel.Children.Add(header);

        var grid = new Grid(); for (var i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(140) : new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Info(grid, 0, 0, "Belge Tipi", EDocumentPresentation.TypeLabel(document.DocumentType)); Info(grid, 0, 2, "Kaynak Belge", $"{document.SourceEntityType} — {document.SourceEntityId[..Math.Min(8, document.SourceEntityId.Length)]}");
        Info(grid, 1, 0, "UUID", document.Uuid); Info(grid, 1, 2, "Provider Belge No", document.ProviderDocumentId ?? "—");
        Info(grid, 2, 0, "Oluşturma", document.CreatedAt.ToString("dd.MM.yyyy HH:mm")); Info(grid, 2, 2, "Son Hata", document.LastErrorMessage ?? "—");
        panel.Children.Add(new Border { Background = Ui.Brush("R3.Surface.Brush"), BorderBrush = PanelBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(16), Child = grid });

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var payloads = documents.GetPayloads(document.Id);
        var actions = EDocumentPresentation.ActionsFor(document.Status);
        if (actions.CanViewXml) ActionButton(bar, "XML Görüntüle", () => ElectronicDocumentDialogs.ShowXmlViewer(payloads));
        if (actions.CanViewProviderResponse) ActionButton(bar, "Provider Yanıtı", () => ElectronicDocumentDialogs.ShowProviderResponse(document, payloads));
        panel.Children.Add(bar);

        panel.Children.Add(new TextBlock { Text = "Olay Geçmişi", FontSize = Ui.Font.Section, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Info.Brush"), Margin = new Thickness(0, 18, 0, 8) });
        panel.Children.Add(ElectronicDocumentDialogs.BuildEventTimeline(documents.GetEvents(document.Id)));
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static void Info(Grid grid, int row, int column, string label, string value)
    {
        var caption = new TextBlock { Text = label, Foreground = Muted, FontSize = Ui.Font.Caption, Margin = new Thickness(0, 6, 9, 2) }; Grid.SetRow(caption, row); Grid.SetColumn(caption, column); grid.Children.Add(caption);
        var text = new TextBlock { Text = value, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 6, 12, 2), TextWrapping = TextWrapping.Wrap }; Grid.SetRow(text, row); Grid.SetColumn(text, column + 1); grid.Children.Add(text);
    }
}
