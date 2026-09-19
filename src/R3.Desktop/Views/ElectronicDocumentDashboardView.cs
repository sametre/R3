using System.Data;
using System.Windows;
using System.Windows.Controls;
using R3.Desktop.Presentation;
using R3.Desktop.ViewModels;
using R3.Infrastructure;
using static R3.Desktop.Views.ElectronicDocumentUiKit;

namespace R3.Desktop.Views;

// Phase 9 (§4-7): E-Belge Genel Bakış. All numbers come from ElectronicDocumentDashboardViewModel;
// this class only lays them out.
internal static class ElectronicDocumentDashboardView
{
    public static UIElement Create(StoreDatabase database, string companyId, Action<string> openDocument)
    {
        var vm = new ElectronicDocumentDashboardViewModel(database, companyId);
        var root = new StackPanel { Margin = new Thickness(18) };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var buttonBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttonBar, Dock.Right); header.Children.Add(buttonBar);
        var refreshButton = ActionButton(buttonBar, "↻  Yenile", () => { });
        header.Children.Add(new TextBlock { Text = "E-Belge Genel Bakış", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brush("263746") });
        root.Children.Add(header);

        var status = new TextBlock { Foreground = Brush("#C4514B"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        root.Children.Add(status);

        var kpiCards = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        root.Children.Add(kpiCards);
        var typeCards = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        root.Children.Add(typeCards);

        root.Children.Add(new TextBlock { Text = "Son Hatalı Belgeler", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brush("2B5870"), Margin = new Thickness(0, 0, 0, 8) });
        var errorGrid = Grid_();
        Columns(errorGrid, ("BelgeTarihi", "Tarih", 100d), ("BelgeNo", "Belge No", 110d), ("Cari", "Cari", 220d), ("BelgeTipi", "Belge Tipi", 100d), ("SonHata", "Hata", 300d), ("Deneme", "Deneme", 70d), ("EBelgeDurumu", "Durum", 100d));
        errorGrid.MouseDoubleClick += (_, _) => { if (errorGrid.SelectedItem is DataRowView row) openDocument(row["Id"].ToString()!); };
        root.Children.Add(errorGrid);

        async Task RefreshAsync()
        {
            await vm.RefreshCommand.ExecuteAsync(null);
            status.Text = vm.StatusMessage ?? "";
            refreshButton.IsEnabled = !vm.IsBusy;
            kpiCards.Children.Clear();
            if (vm.Summary is { } s)
            {
                kpiCards.Children.Add(Card("Bugün Oluşturulan", s.CreatedToday.ToString("N0", Turkish), "#2E6F95"));
                kpiCards.Children.Add(Card("UBL Hazır", s.Generated.ToString("N0", Turkish), "#C0832B"));
                kpiCards.Children.Add(Card("Kuyrukta", s.Queued.ToString("N0", Turkish), "#C0832B"));
                kpiCards.Children.Add(Card("Gönderilen", s.Sent.ToString("N0", Turkish), "#2E6F95"));
                kpiCards.Children.Add(Card("Kabul Edilen", s.Accepted.ToString("N0", Turkish), "#2A8F7B"));
                kpiCards.Children.Add(Card("Reddedilen", s.Rejected.ToString("N0", Turkish), "#C4514B"));
                kpiCards.Children.Add(Card("Retry Bekleyen", s.RetryPending.ToString("N0", Turkish), "#C0832B"));
                kpiCards.Children.Add(Card("Dead Letter", s.DeadLetter.ToString("N0", Turkish), "#C4514B"));
            }
            typeCards.Children.Clear();
            typeCards.Children.Add(Card(EDocumentPresentation.TypeLabel(ElectronicDocumentType.EInvoice), vm.EInvoiceCount.ToString("N0", Turkish), "#2E6F95"));
            typeCards.Children.Add(Card(EDocumentPresentation.TypeLabel(ElectronicDocumentType.EArchiveInvoice), vm.EArchiveCount.ToString("N0", Turkish), "#75639A"));
            typeCards.Children.Add(Card(EDocumentPresentation.TypeLabel(ElectronicDocumentType.EDespatch), vm.EDespatchCount.ToString("N0", Turkish), "#8A96A0"));
            errorGrid.ItemsSource = vm.RecentErrors;
        }
        refreshButton.Click += async (_, _) => await RefreshAsync();
        _ = RefreshAsync();
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
