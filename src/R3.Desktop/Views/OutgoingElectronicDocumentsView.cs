using System.Data;
using System.Windows;
using System.Windows.Controls;
using R3.Desktop.ContextActions;
using R3.Desktop.Presentation;
using R3.Desktop.ViewModels;
using R3.Infrastructure;
using static R3.Desktop.Views.ElectronicDocumentUiKit;

namespace R3.Desktop.Views;

// Phase 9 (§8-11): Giden Belgeler.
internal static class OutgoingElectronicDocumentsView
{
    public static UIElement Create(StoreDatabase database, string companyId, string userId,
        Action<string> openDocument, Action<string, string> openSourceDocument, Action<string> openAccount, Action<string> openQueueRecord)
    {
        var vm = new OutgoingElectronicDocumentsViewModel(database, companyId);
        var root = new DockPanel { Margin = new Thickness(18) };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        bar.Children.Add(new TextBlock { Text = "Giden Belgeler", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brush("263746"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) });
        var typeFilter = FilterCombo("Tüm Tipler", ("EInvoice", "E-Fatura"), ("EArchiveInvoice", "E-Arşiv Fatura"), ("EDespatch", "E-İrsaliye"));
        var statusFilter = FilterCombo("Tüm Durumlar", Enum.GetValues<ElectronicDocumentStatus>().Select(s => (s.ToString(), EDocumentPresentation.StatusLabel(s))).ToArray());
        var startDate = new DatePicker { Width = 130, Margin = new Thickness(8, 0, 0, 0) };
        var endDate = new DatePicker { Width = 130, Margin = new Thickness(6, 0, 0, 0) };
        bar.Children.Add(typeFilter); bar.Children.Add(statusFilter);
        bar.Children.Add(new TextBlock { Text = "Tarih:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Foreground = Muted });
        bar.Children.Add(startDate); bar.Children.Add(endDate);

        var searchBar = Toolbar(out var search, "Belge no, UUID, cari veya VKN/TCKN ara");
        DockPanel.SetDock(searchBar, Dock.Top); root.Children.Add(searchBar);
        var refresh = ActionButton(searchBar, "↻  Yenile", () => { });

        var status = new TextBlock { Foreground = Brush("#C4514B"), Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(status, Dock.Top); root.Children.Add(status);

        var grid = Grid_();
        Columns(grid, ("BelgeTarihi", "Belge Tarihi", 95d), ("BelgeTipiTr", "Belge Tipi", 95d), ("BelgeNo", "Belge No", 110d), ("Cari", "Cari", 200d),
            ("VknTckn", "VKN/TCKN", 100d), ("UUID", "UUID", 160d), ("Tutar", "Tutar", 100d), ("DurumTr", "Durum", 105d), ("Provider", "Provider", 90d),
            ("GonderimTarihi", "Gönderim Tarihi", 110d), ("Deneme", "Deneme", 65d), ("SonHata", "Son Hata", 220d));
        root.Children.Add(grid);

        ErpGridContext.Register(grid, "edocuments.outgoing", ElectronicDocumentContextActions.ForDocument(database, userId, openDocument, openSourceDocument, openAccount, openQueueRecord),
            () => RefreshAsync(), "ElectronicDocument");

        async Task RefreshAsync()
        {
            vm.DocumentTypeFilter = (string?)typeFilter.SelectedValue; vm.StatusFilter = (string?)statusFilter.SelectedValue;
            vm.StartDate = startDate.SelectedDate; vm.EndDate = endDate.SelectedDate; vm.SearchText = search.Text;
            await vm.RefreshCommand.ExecuteAsync(null);
            status.Text = vm.StatusMessage ?? "";
            refresh.IsEnabled = !vm.IsBusy;
            var table = vm.Rows?.Table;
            if (table != null)
            {
                if (!table.Columns.Contains("BelgeTipiTr")) table.Columns.Add("BelgeTipiTr", typeof(string));
                if (!table.Columns.Contains("DurumTr")) table.Columns.Add("DurumTr", typeof(string));
                foreach (DataRow row in table.Rows)
                {
                    row["BelgeTipiTr"] = Enum.TryParse<ElectronicDocumentType>(row["BelgeTipi"].ToString(), out var t) ? EDocumentPresentation.TypeLabel(t) : row["BelgeTipi"];
                    row["DurumTr"] = Enum.TryParse<ElectronicDocumentStatus>(row["Durum"].ToString(), out var s) ? EDocumentPresentation.StatusLabel(s) : row["Durum"];
                }
            }
            grid.ItemsSource = vm.Rows;
        }
        refresh.Click += async (_, _) => await RefreshAsync();
        typeFilter.SelectionChanged += async (_, _) => await RefreshAsync();
        statusFilter.SelectionChanged += async (_, _) => await RefreshAsync();
        startDate.SelectedDateChanged += async (_, _) => await RefreshAsync();
        endDate.SelectedDateChanged += async (_, _) => await RefreshAsync();
        KeyboardInteractionService.AttachDebouncedSearch(search, () => _ = RefreshAsync());

        _ = RefreshAsync();
        return root;
    }
}
