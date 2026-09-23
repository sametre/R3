using System.Data;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using R3.Desktop.Design;
using R3.Infrastructure;

namespace R3.Desktop.Views;

/// <summary>
/// Stok Kartı'ndaki "…" düğmelerinin seçim penceresi (Stok Grup, İlişkili Stok Kodu, Marka, Alt Grup, Tedarikçi):
/// arama kutusu + Kod/Ad listesi. Yazdıkça (300 ms) arar, ↓ listeye geçer, Enter / çift tık seçer, Esc kapatır.
/// Arama LocalStockCardService.Lookup ile arka planda çalışır.
/// </summary>
public sealed class StockLookupDialog : Window
{
    private readonly LocalStockCardService _service;
    private readonly string _kind, _companyId;
    private readonly TextBox _search = new() { MinWidth = 260 };
    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column, CanUserAddRows = false };
    private readonly TextBlock _status = Ui.StatusText();
    private readonly LoadingOverlay _loading = new();
    private int _version;

    public (string Id, string Code, string Name)? Selected { get; private set; }

    public StockLookupDialog(LocalStockCardService service, string kind, string companyId, string title, string? initialSearch = null)
    {
        _service = service; _kind = kind; _companyId = companyId;
        Title = title; Width = 560; Height = 480; MinWidth = 420; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = Ui.Surface;

        AutomationProperties.SetName(_search, "Ara");
        _grid.Columns.Add(new DataGridTextColumn { Header = "Kod", Binding = new Binding("Kod"), Width = 140 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Ad", Binding = new Binding("Ad"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        AutomationProperties.SetName(_grid, title);

        var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, Ui.Space.S) };
        var label = Ui.FieldLabel("Ara:", first: true); DockPanel.SetDock(label, Dock.Left);
        searchRow.Children.Add(label); searchRow.Children.Add(_search);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, Ui.Space.S, 0, 0) };
        var ok = Ui.PrimaryButton("Seç", Accept); ok.IsDefault = true;
        var cancel = Ui.Button("Vazgeç", () => DialogResult = false); cancel.IsCancel = true; cancel.Margin = new Thickness(Ui.Space.S, 0, 0, 0);
        buttons.Children.Add(ok); buttons.Children.Add(cancel);

        var bottom = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right); bottom.Children.Add(buttons); bottom.Children.Add(_status);

        var root = new DockPanel { Margin = new Thickness(Ui.Space.L) };
        DockPanel.SetDock(searchRow, Dock.Top); DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(searchRow); root.Children.Add(bottom);
        root.Children.Add(Ui.Layered(_grid, _loading));
        Content = root;

        KeyboardInteractionService.AttachDebouncedSearch(_search, () => _ = LoadAsync(), 300);
        _search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && _grid.Items.Count > 0) { _grid.SelectedIndex = Math.Max(0, _grid.SelectedIndex); _grid.Focus(); e.Handled = true; }
        };
        _grid.MouseDoubleClick += (_, _) => Accept();
        _grid.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Accept(); e.Handled = true; } };
        _search.Text = initialSearch ?? "";
        Loaded += async (_, _) => { _search.Focus(); _search.SelectAll(); await LoadAsync(); };
    }

    private async Task LoadAsync()
    {
        var version = ++_version; var term = _search.Text;
        _loading.ShowLoading("Aranıyor…");
        try
        {
            var table = await Task.Run(() => _service.Lookup(_kind, _companyId, term));
            if (version != _version) return;
            _grid.ItemsSource = table.DefaultView;
            if (table.Rows.Count > 0) _grid.SelectedIndex = 0;
            _status.Text = table.Rows.Count switch
            {
                0 => "Kayıt bulunamadı. Aramayı değiştirin.",
                500 => "İlk 500 kayıt gösteriliyor; aramayı daraltın.",
                var n => $"{n.ToString("N0", Ui.Turkish)} kayıt"
            };
        }
        catch (Exception ex) { if (version == _version) _status.Text = "Liste yüklenemedi: " + ex.Message; }
        finally { if (version == _version) _loading.HideLoading(); }
    }

    private void Accept()
    {
        if (_grid.SelectedItem is not DataRowView row) return;
        Selected = (row["Id"].ToString()!, row["Kod"].ToString()!, row["Ad"].ToString()!);
        DialogResult = true;
    }
}
