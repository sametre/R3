using System.Data;
using System.Windows;
using System.Windows.Controls;
using R3.Desktop.ContextActions;
using R3.Desktop.Presentation;
using R3.Desktop.ViewModels;
using R3.Infrastructure;
using static R3.Desktop.Views.ElectronicDocumentUiKit;

namespace R3.Desktop.Views;

// Phase 9 (§12-19): Gönderim Kuyruğu.
internal static class ElectronicDocumentOutboxView
{
    public static UIElement Create(StoreDatabase database, string companyId, string userId, Action<string> openDocument)
    {
        var vm = new ElectronicDocumentOutboxViewModel(database, companyId, userId);
        var root = new DockPanel { Margin = new Thickness(18) };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        bar.Children.Add(new TextBlock { Text = "Gönderim Kuyruğu", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brush("263746"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) });
        var statusFilter = FilterCombo("Tüm Durumlar", Enum.GetValues<ElectronicDocumentOutboxStatus>().Select(s => (s.ToString(), EDocumentPresentation.OutboxStatusLabel(s))).ToArray());
        var typeFilter = FilterCombo("Tüm Tipler", ("EInvoice", "E-Fatura"), ("EArchiveInvoice", "E-Arşiv Fatura"), ("EDespatch", "E-İrsaliye"));
        bar.Children.Add(statusFilter); bar.Children.Add(typeFilter);

        var searchBar = Toolbar(out var search, "Belge no, UUID veya cari ara");
        DockPanel.SetDock(searchBar, Dock.Top); root.Children.Add(searchBar);
        var refresh = ActionButton(searchBar, "↻  Yenile", () => { });
        var runDue = ActionButton(searchBar, "Bekleyenleri Çalıştır", () => { });

        var status = new TextBlock { Foreground = Brush("#C4514B"), Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(status, Dock.Top); root.Children.Add(status);

        var grid = Grid_();
        Columns(grid, ("Olusturma", "Oluşturma", 100d), ("BelgeTipiTr", "Belge Tipi", 90d), ("BelgeNo", "Belge No", 105d), ("Cari", "Cari", 190d),
            ("OperationTr", "İşlem", 90d), ("OutboxDurumuTr", "Kuyruk Durumu", 100d), ("EBelgeDurumuTr", "E-Belge Durumu", 110d),
            ("Deneme", "Deneme", 60d), ("MaksDeneme", "Maks.", 55d), ("SonDeneme", "Son Deneme", 105d), ("SonrakiDeneme", "Sonraki Deneme", 105d),
            ("Kilit", "Kilit", 90d), ("Provider", "Provider", 90d), ("SonHata", "Son Hata", 220d));
        root.Children.Add(grid);

        void OpenDrawer(string outboxId) { var row = vm.GetRow(outboxId); if (row != null) ElectronicDocumentDialogs.ShowOutboxDetail(row); }
        ErpGridContext.Register(grid, "edocuments.outbox", ElectronicDocumentContextActions.ForOutbox(database, userId, OpenDrawer, openDocument),
            () => RefreshAsync(), "ElectronicDocumentOutbox");

        async Task RefreshAsync()
        {
            vm.OutboxStatusFilter = (string?)statusFilter.SelectedValue; vm.DocumentTypeFilter = (string?)typeFilter.SelectedValue; vm.SearchText = search.Text;
            await vm.RefreshCommand.ExecuteAsync(null);
            status.Text = vm.ErrorMessage ?? vm.StatusMessage ?? "";
            refresh.IsEnabled = !vm.IsBusy; runDue.IsEnabled = !vm.IsBusy;
            var table = vm.Rows?.Table;
            if (table != null)
            {
                foreach (var col in new[] { "BelgeTipiTr", "OperationTr", "OutboxDurumuTr", "EBelgeDurumuTr" }) if (!table.Columns.Contains(col)) table.Columns.Add(col, typeof(string));
                foreach (DataRow row in table.Rows)
                {
                    row["BelgeTipiTr"] = Enum.TryParse<ElectronicDocumentType>(row["BelgeTipi"].ToString(), out var t) ? EDocumentPresentation.TypeLabel(t) : row["BelgeTipi"];
                    row["OperationTr"] = row["Operation"].ToString() == "Send" ? "Gönderim" : row["Operation"].ToString() == "QueryStatus" ? "Durum Sorgusu" : row["Operation"];
                    row["OutboxDurumuTr"] = Enum.TryParse<ElectronicDocumentOutboxStatus>(row["OutboxDurumu"].ToString(), out var os) ? EDocumentPresentation.OutboxStatusLabel(os) : row["OutboxDurumu"];
                    row["EBelgeDurumuTr"] = Enum.TryParse<ElectronicDocumentStatus>(row["EBelgeDurumu"].ToString(), out var s) ? EDocumentPresentation.StatusLabel(s) : row["EBelgeDurumu"];
                }
            }
            grid.ItemsSource = vm.Rows;
        }
        refresh.Click += async (_, _) => await RefreshAsync();
        runDue.Click += async (_, _) => { await vm.ManualRunCommand.ExecuteAsync(null); await RefreshAsync(); };
        statusFilter.SelectionChanged += async (_, _) => await RefreshAsync();
        typeFilter.SelectionChanged += async (_, _) => await RefreshAsync();
        KeyboardInteractionService.AttachDebouncedSearch(search, () => _ = RefreshAsync());

        _ = RefreshAsync();
        return root;
    }
}
