using System.Data;
using R3.Desktop.Design;
using System.Windows;
using System.Windows.Controls;
using R3.Desktop.ContextActions;
using R3.Desktop.Presentation;
using R3.Desktop.ViewModels;
using R3.Infrastructure;
using static R3.Desktop.Views.ElectronicDocumentUiKit;

namespace R3.Desktop.Views;

// Phase 9 (§20-23): Hatalı Belgeler.
internal static class FailedElectronicDocumentsView
{
    public static UIElement Create(StoreDatabase database, string companyId, string userId,
        Action<string> openDocument, Action<string, string> openSourceDocument, Action<string> openAccount, Action<string> openQueueRecord)
    {
        var vm = new FailedElectronicDocumentsViewModel(database, companyId, userId);
        var root = new DockPanel { Margin = new Thickness(18) };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        bar.Children.Add(new TextBlock { Text = "Hatalı Belgeler", FontSize = Ui.Font.Title, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("R3.Text.Primary.Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) });
        var typeFilter = FilterCombo("Tüm Tipler", ("EInvoice", "E-Fatura"), ("EArchiveInvoice", "E-Arşiv Fatura"), ("EDespatch", "E-İrsaliye"));
        bar.Children.Add(typeFilter);

        var kpi = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(kpi, Dock.Top); root.Children.Add(kpi);

        var searchBar = Toolbar(out var search, "Belge no, UUID veya cari ara");
        DockPanel.SetDock(searchBar, Dock.Top); root.Children.Add(searchBar);
        var refresh = ActionButton(searchBar, "↻  Yenile", () => { });

        var status = new TextBlock { Foreground = Ui.Brush("R3.Danger.Brush"), Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(status, Dock.Top); root.Children.Add(status);

        var grid = Grid_();
        Columns(grid, ("BelgeTarihi", "Belge Tarihi", 95d), ("BelgeNo", "Belge No", 105d), ("BelgeTipiTr", "Belge Tipi", 90d), ("Cari", "Cari", 190d),
            ("UUID", "UUID", 160d), ("EBelgeDurumuTr", "E-Belge Durumu", 105d), ("OutboxDurumuTr", "Outbox Durumu", 100d), ("HataKodu", "Hata Kodu", 90d),
            ("SonHata", "Son Hata", 240d), ("Deneme", "Deneme", 60d), ("SonDeneme", "Son Deneme", 105d), ("SonrakiDeneme", "Sonraki Deneme", 105d), ("Provider", "Provider", 90d));
        root.Children.Add(grid);

        ErpGridContext.Register(grid, "edocuments.errors", ElectronicDocumentContextActions.ForDocument(database, userId, openDocument, openSourceDocument, openAccount, openQueueRecord),
            () => RefreshAsync(), "ElectronicDocument");

        async Task RefreshAsync()
        {
            vm.DocumentTypeFilter = (string?)typeFilter.SelectedValue; vm.SearchText = search.Text;
            await vm.RefreshCommand.ExecuteAsync(null);
            status.Text = vm.ErrorMessage ?? vm.StatusMessage ?? "";
            refresh.IsEnabled = !vm.IsBusy;
            var table = vm.Rows?.Table;
            if (table != null)
            {
                foreach (var col in new[] { "BelgeTipiTr", "EBelgeDurumuTr", "OutboxDurumuTr" }) if (!table.Columns.Contains(col)) table.Columns.Add(col, typeof(string));
                foreach (DataRow row in table.Rows)
                {
                    row["BelgeTipiTr"] = Enum.TryParse<ElectronicDocumentType>(row["BelgeTipi"].ToString(), out var t) ? EDocumentPresentation.TypeLabel(t) : row["BelgeTipi"];
                    row["EBelgeDurumuTr"] = Enum.TryParse<ElectronicDocumentStatus>(row["EBelgeDurumu"].ToString(), out var s) ? EDocumentPresentation.StatusLabel(s) : row["EBelgeDurumu"];
                    row["OutboxDurumuTr"] = Enum.TryParse<ElectronicDocumentOutboxStatus>(row["OutboxDurumu"].ToString(), out var os) ? EDocumentPresentation.OutboxStatusLabel(os) : row["OutboxDurumu"];
                }
            }
            kpi.Children.Clear();
            if (vm.Summary is { } s2)
            {
                kpi.Children.Add(Card("Hatalı (Failed)", s2.Failed.ToString(Turkish), "#C4514B"));
                kpi.Children.Add(Card("Reddedilen", s2.Rejected.ToString(Turkish), "#C4514B"));
                kpi.Children.Add(Card("Retry Bekleyen", s2.RetryPending.ToString(Turkish), "#C0832B"));
                kpi.Children.Add(Card("Dead Letter", s2.DeadLetter.ToString(Turkish), "#C4514B"));
            }
            grid.ItemsSource = vm.Rows;
        }
        refresh.Click += async (_, _) => await RefreshAsync();
        typeFilter.SelectionChanged += async (_, _) => await RefreshAsync();
        KeyboardInteractionService.AttachDebouncedSearch(search, () => _ = RefreshAsync());

        _ = RefreshAsync();
        return root;
    }
}
