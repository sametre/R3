using System.Data;
using Krypton.Toolkit;
using Microsoft.Extensions.Logging;
using R3.Desktop.WinForms.Components.Cards;
using R3.Desktop.WinForms.Components.Dialogs;
using R3.Desktop.WinForms.Components.Filters;
using R3.Desktop.WinForms.Components.Grids;
using R3.Desktop.WinForms.Components.Toolbars;
using R3.Desktop.WinForms.Design;
using R3.Desktop.WinForms.Features.Shared;
using R3.Desktop.WinForms.Infrastructure.Errors;
using R3.Desktop.WinForms.Infrastructure.Logging;
using R3.Desktop.WinForms.Infrastructure.Navigation;
using R3.Desktop.WinForms.Infrastructure.Session;
using R3.Infrastructure;

namespace R3.Desktop.WinForms.Features.Products;

/// <summary>
/// Stok › Stok Kartları. Server-side paged/sorted list over LocalProductService.SearchPage (35K+ products: only one
/// page is ever bound). Search (debounced, barcode Enter), status/type/brand/category filters, stock toggles,
/// multi-select + bulk Aktif/Pasif, copy, Excel export of the whole filtered set, column chooser with saved layout.
/// Keys: Ctrl+F search · Enter/F3 open · Ctrl+N/F2 new · F5 refresh · Alt+←/→ page · Ctrl+E export.
/// </summary>
public sealed class ProductListView : UserControl
{
    public const string ScreenKey = "inventory.products";
    private const string EditPermission = "inventory.product.edit";

    private readonly AppSession _session;
    private readonly IWorkspaceNavigator _navigator;
    private readonly LocalProductService _products;
    private readonly ILogger _log = AppLog.For<ProductListView>();

    private readonly R3SearchBox _search = new("Stok kodu, adı veya barkod");
    private readonly KryptonComboBox _status = R3FilterPanel.Choice(("active", "Aktif"), ("passive", "Pasif"), (null, "Tümü"));
    private readonly KryptonComboBox _type = R3FilterPanel.Choice([(null, "Tüm tipler"), .. ProductText.Types.Select(t => ((string?)t.Value, t.Text))]);
    private readonly KryptonComboBox _brand = R3FilterPanel.Choice((null, "Tüm markalar"));
    private readonly KryptonComboBox _category = R3FilterPanel.Choice((null, "Tüm kategoriler"));
    private readonly KryptonCheckBox _belowMinimum, _negative, _outOfStock;
    private readonly R3DataGrid _grid = new() { Dock = DockStyle.Fill };
    private readonly GridPreferences _preferences;
    private readonly Label _summary = new() { AutoSize = true, Font = AppTypography.Caption, ForeColor = AppColors.TextSecondary, Margin = new Padding(0, AppSpacing.SM, AppSpacing.MD, 0) };
    private readonly Label _pageLabel = new() { AutoSize = true, Font = AppTypography.Caption, ForeColor = AppColors.TextSecondary, Margin = new Padding(AppSpacing.SM, AppSpacing.SM, AppSpacing.SM, 0) };
    private readonly KryptonComboBox _pageSize = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    private readonly R3SecondaryButton _previous, _next, _edit, _copy, _toggle;
    private ProductListFilter _filter;
    private int _totalCount, _loadVersion;
    private bool _ready;

    public ProductListView(AppSession session, IWorkspaceNavigator navigator)
    {
        _session = session; _navigator = navigator; _products = new LocalProductService(session.Database);
        BackColor = AppColors.Background; Font = AppTypography.Body;
        var canEdit = session.Can(EditPermission);

        // Toolbar
        var toolbar = new R3Toolbar();
        var create = toolbar.Add(new R3PrimaryButton("Yeni Stok Kartı", OpenNew), "Yeni stok kartı (Ctrl+N / F2)");
        _edit = toolbar.Add(new R3SecondaryButton("Aç", OpenCurrent), "Seçili kartı aç (Enter / F3)");
        _copy = toolbar.Add(new R3SecondaryButton("Kopyala", CopyCurrent), "Seçili kartı yeni kodla kopyalar (barkodlar kopyalanmaz)");
        _toggle = toolbar.Add(new R3SecondaryButton("Aktif / Pasif", ToggleSelected), "Seçili kartları aktif ya da pasif yapar");
        toolbar.AddSeparator();
        toolbar.Add(new R3SecondaryButton("Excel'e aktar", () => _ = ExportAsync()), "Filtreye uyan tüm kayıtları Excel dosyasına aktarır (Ctrl+E)");
        toolbar.Add(new R3SecondaryButton("Kolonlar", ChooseColumns), "Kolonları ve satır yoğunluğunu ayarla");
        toolbar.Add(new R3SecondaryButton("Yenile", () => _ = LoadAsync()), "Listeyi yenile (F5)");
        create.Enabled = _copy.Enabled = _toggle.Enabled = canEdit;

        // Filters
        var filters = new R3FilterPanel();
        filters.AddField("Ara:", _search);
        filters.AddField("Durum:", _status).Width = 100;
        filters.AddField("Tip:", _type);
        filters.AddField("Marka:", _brand);
        filters.AddField("Kategori:", _category);
        _belowMinimum = filters.AddToggle("Minimum altı");
        _negative = filters.AddToggle("Negatif stok");
        _outOfStock = filters.AddToggle("Stokta yok");

        // Grid (sort keys = LocalProductService.SearchPage sort columns)
        _grid.AddText("StokKodu", "Stok Kodu", 120, sortKey: "code");
        _grid.AddText("StokAdi", "Stok Adı", 260, sortKey: "name", fill: true);
        _grid.AddText("UrunTipiText", "Tip", 90);
        _grid.AddText("AnaBirim", "Birim", 70);
        _grid.AddText("BirincilBarkod", "Birincil Barkod", 130);
        _grid.AddText("Marka", "Marka", 130, sortKey: "brand");
        _grid.AddText("Kategori", "Kategori", 130, sortKey: "category");
        _grid.AddText("StokGrubu", "Stok Grubu", 130, sortKey: "group");
        _grid.AddText("Mense", "Menşe", 90).Visible = false;
        _grid.AddNumber("MevcutStok", "Mevcut", 90);
        _grid.AddNumber("RezerveStok", "Rezerve", 90);
        _grid.AddNumber("KullanilabilirStok", "Kullanılabilir", 105, sortKey: "available");
        _grid.AddNumber("MinimumStok", "Min. Stok", 85).Visible = false;
        _grid.AddText("DurumText", "Durum", 70);
        _grid.AddText("SatisText", "Satış", 70).Visible = false;
        _grid.AddDate("SonGuncelleme", "Son Güncelleme", 120, AppFormats.DateTime, sortKey: "updated_at").Visible = false;
        _grid.AccessibleName = "Stok kartları listesi";
        _preferences = new GridPreferences(_grid, ScreenKey);
        _preferences.Load();
        _grid.FreezeLeading(1); // code stays visible; the name column fills (a frozen column cannot fill)
        _grid.SetSortIndicator("code", false);
        _grid.ContextMenuStrip = BuildContextMenu(canEdit);

        _filter = new ProductListFilter(PageSize: _preferences.PageSize);

        // Pager
        _pageSize.Items.AddRange(ProductListFilter.PageSizes.Cast<object>().ToArray());
        _pageSize.SelectedItem = _preferences.PageSize; _pageSize.AccessibleName = "Sayfa boyutu";
        var pager = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, AppSpacing.SM, 0, 0), BackColor = AppColors.Background };
        _previous = new R3SecondaryButton("‹ Önceki", () => GoToPage(_filter.Page - 1), "Önceki sayfa (Alt+←)");
        _next = new R3SecondaryButton("Sonraki ›", () => GoToPage(_filter.Page + 1), "Sonraki sayfa (Alt+→)");
        pager.Controls.AddRange([_summary, _previous, _pageLabel, _next, new Label { Text = "Sayfa boyutu:", AutoSize = true, ForeColor = AppColors.TextSecondary, Margin = new Padding(AppSpacing.LG, AppSpacing.SM, AppSpacing.XS, 0) }, _pageSize]);

        var gridFrame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, AppSpacing.SM, 0, 0), BackColor = AppColors.Background };
        gridFrame.Controls.Add(_grid);
        SuspendLayout();
        Controls.Add(gridFrame); Controls.Add(pager); Controls.Add(filters); Controls.Add(toolbar);
        Controls.Add(new R3PageHeader("Stok Kartları", "Stok kodu, adı veya barkod ile arayın; filtreler anında uygulanır. Enter kartı açar, Ctrl+N yeni kart."));
        ResumeLayout();

        // Events
        _search.SearchRequested += (_, _) => ApplyFilter();
        _search.MoveToResults += (_, _) => _grid.Focus();
        foreach (var combo in new[] { _status, _type, _brand, _category }) combo.SelectedIndexChanged += (_, _) => ApplyFilter();
        foreach (var toggle in new[] { _belowMinimum, _negative, _outOfStock }) toggle.CheckedChanged += (_, _) => ApplyFilter();
        _pageSize.SelectedIndexChanged += (_, _) => { if (_pageSize.SelectedItem is int size) { _preferences.PageSize = size; _preferences.Save(); ApplyFilter(); } };
        _grid.SortRequested += (_, sort) => { _filter = _filter with { SortKey = sort.Key, SortDescending = sort.Descending, Page = 1 }; _ = LoadAsync(); };
        _grid.RowActivated += (_, _) => OpenCurrent();
        _grid.SelectionChanged += (_, _) => UpdateSelectionState();
        Load += async (_, _) => { _search.Focus(); await LoadLookupsAsync(); _ready = true; await LoadAsync(); };
    }

    // ---- data -----------------------------------------------------------------------------------------------

    private ProductListFilter ReadFilter(int page) => _filter with
    {
        Search = _search.Term,
        Status = (_status.SelectedItem as R3FilterPanel.FilterItem)?.Value,
        ProductType = (_type.SelectedItem as R3FilterPanel.FilterItem)?.Value,
        BrandId = (_brand.SelectedItem as R3FilterPanel.FilterItem)?.Value,
        CategoryId = (_category.SelectedItem as R3FilterPanel.FilterItem)?.Value,
        BelowMinimum = _belowMinimum.Checked, NegativeStock = _negative.Checked, OutOfStock = _outOfStock.Checked,
        Page = page, PageSize = _pageSize.SelectedItem is int size ? size : 50
    };

    private void ApplyFilter() { if (!_ready) return; _filter = ReadFilter(1); _ = LoadAsync(); }

    private void GoToPage(int page)
    {
        var pages = ProductListFilter.PageCount(_totalCount, _filter.PageSize);
        if (page < 1 || page > pages || _grid.IsLoading) return;
        _filter = _filter with { Page = page };
        _ = LoadAsync();
    }

    private async Task LoadLookupsAsync()
    {
        try
        {
            var master = new LocalMasterDataService(_session.Database);
            var (brands, categories) = await Task.Run(() => (Lookups.Load(master, "brands"), Lookups.Load(master, "categories")));
            if (IsDisposed) return;
            _brand.DataSource = new[] { new R3FilterPanel.FilterItem(null, "Tüm markalar") }.Concat(brands.Select(b => new R3FilterPanel.FilterItem(b.Id, b.Text))).ToList();
            _category.DataSource = new[] { new R3FilterPanel.FilterItem(null, "Tüm kategoriler") }.Concat(categories.Select(c => new R3FilterPanel.FilterItem(c.Id, c.Text))).ToList();
        }
        catch (Exception ex) { _log.LogWarning(ex, "Brand/category lookups failed"); }
    }

    /// <summary>Loads the current page off the UI thread; an older, slower result never overwrites a newer one.</summary>
    internal async Task LoadAsync()
    {
        var version = ++_loadVersion;
        var query = _filter.ToQuery(_session.CompanyId);
        _grid.SetLoading("Stok kartları yükleniyor…");
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var page = await Task.Run(() => _products.SearchPage(query));
            if (version != _loadVersion || IsDisposed) return;
            var table = Present(page.Rows);
            var keep = _grid.CurrentDataRow?["Id"]?.ToString();
            _grid.SuspendLayout();
            _grid.DataSource = table;
            _grid.ResumeLayout();
            if (keep != null) Reselect(keep);
            _totalCount = page.TotalCount;
            _grid.SetEmptyText(_filter.HasCriteria ? "Filtreye uyan stok kartı yok. Aramayı veya filtreleri değiştirin." : "Bu firmada aktif stok kartı yok. Yeni Stok Kartı ile ekleyin.");
            _grid.SetError(null);
            UpdatePager();
            _log.LogInformation("Product page {Page} loaded: {Rows}/{Total} rows in {Elapsed} ms", page.Page, table.Rows.Count, page.TotalCount, started.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            if (version != _loadVersion || IsDisposed) return;
            _log.LogError(ex, "Product list failed to load");
            _grid.DataSource = null;
            _grid.SetError("Liste yüklenemedi. " + ErrorHandler.UserMessage(ex) + " Yenile (F5) ile tekrar deneyin.");
            _summary.Text = "";
        }
        finally { if (version == _loadVersion && !IsDisposed) _grid.SetLoading(null); }
    }

    /// <summary>Adds display columns for enum/flag values (0/1, "Stock") the grid shows as Turkish text.</summary>
    internal static DataTable Present(DataTable table)
    {
        table.Columns.Add("UrunTipiText", typeof(string)); table.Columns.Add("DurumText", typeof(string)); table.Columns.Add("SatisText", typeof(string));
        foreach (DataRow row in table.Rows)
        {
            row["UrunTipiText"] = ProductText.Type(row["UrunTipi"]?.ToString());
            row["DurumText"] = Flag(row["Aktif"]) ? "Aktif" : "Pasif";
            row["SatisText"] = Flag(row["SatisaAcik"]) ? "Açık" : "Kapalı";
            if (row["SonGuncelleme"] is string text && DateTime.TryParse(text, AppFormats.Culture, System.Globalization.DateTimeStyles.RoundtripKind, out var updated)) row["SonGuncelleme"] = updated.ToLocalTime().ToString(AppFormats.DateTime, AppFormats.Culture);
        }
        return table;
    }

    private static bool Flag(object? value) => value is not (null or DBNull) && Convert.ToInt64(value) == 1;

    private void Reselect(string id)
    {
        foreach (DataGridViewRow row in _grid.Rows)
            if ((row.DataBoundItem as DataRowView)?["Id"]?.ToString() == id) { _grid.ClearSelection(); row.Selected = true; _grid.CurrentCell = row.Cells[_grid.FirstDisplayedCell?.ColumnIndex ?? 0]; return; }
    }

    private void UpdatePager()
    {
        var pages = ProductListFilter.PageCount(_totalCount, _filter.PageSize);
        _pageLabel.Text = $"Sayfa {_filter.Page.ToString("N0", AppFormats.Culture)} / {pages.ToString("N0", AppFormats.Culture)}";
        _previous.Enabled = _filter.Page > 1; _next.Enabled = _filter.Page < pages;
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        var selected = _grid.SelectedRows.Count;
        _summary.Text = $"{_totalCount.ToString("N0", AppFormats.Culture)} kayıt" + (selected > 1 ? $" • {selected} seçili" : "");
        _edit.Enabled = selected >= 1;
        var canEdit = _session.Can(EditPermission);
        _copy.Enabled = canEdit && selected == 1;
        _toggle.Enabled = canEdit && selected >= 1;
    }

    // ---- actions --------------------------------------------------------------------------------------------

    private void OpenNew()
    {
        if (!_session.Can(EditPermission)) { R3Dialogs.Warning(this, "Stok kartı", "Stok kartı oluşturma yetkiniz yok."); return; }
        ProductEditView.OpenNew(_navigator, _session, OnProductSaved);
    }

    private void OpenCurrent()
    {
        if (_grid.CurrentDataRow is not { } row) return;
        ProductEditView.Open(_navigator, _session, row["Id"].ToString()!, row["StokKodu"].ToString()!, OnProductSaved);
    }

    private void OnProductSaved(string productId) => _ = LoadAsync();

    private void CopyCurrent()
    {
        if (_grid.SelectedDataRows() is not [var row]) return;
        var code = row["StokKodu"].ToString()!;
        try
        {
            var source = _products.GetDetail(row["Id"].ToString()!, _session.CompanyId)?.Product ?? throw new KeyNotFoundException("Stok kartı bulunamadı.");
            var newId = _products.Copy(source, code + "-KOPYA", source.Name + " (Kopya)");
            _log.LogInformation("Product {Code} copied to {NewId}", code, newId);
            _ = LoadAsync();
            ProductEditView.Open(_navigator, _session, newId, code + "-KOPYA", OnProductSaved);
        }
        catch (Exception ex) { ErrorHandler.Report(ex, "Stok kartı kopyalanamadı", FindForm()); }
    }

    private void ToggleSelected()
    {
        var rows = _grid.SelectedDataRows();
        if (rows.Count == 0) return;
        // Mixed selection → activate all; all active → deactivate all.
        var activate = !rows.All(r => Flag(r["Aktif"]));
        var verb = activate ? "aktif" : "pasif";
        if (rows.Count > 1 && !R3Dialogs.Confirm(FindForm(), "Aktif / Pasif", $"{rows.Count} stok kartı {verb} yapılacak. Devam edilsin mi?", destructive: !activate)) return;
        try
        {
            foreach (var row in rows) _products.SetActive(row["Id"].ToString()!, _session.CompanyId, activate);
            _log.LogInformation("{Count} products set {State}", rows.Count, verb);
            _ = LoadAsync();
        }
        catch (Exception ex) { ErrorHandler.Report(ex, "Stok kartı güncellenemedi", FindForm()); _ = LoadAsync(); }
    }

    private void ChooseColumns()
    {
        using var dialog = new ColumnChooserDialog(_grid, _preferences);
        dialog.ShowDialog(FindForm());
        _grid.FreezeLeading(1);
    }

    /// <summary>Exports every record matching the current filter (not just this page), paging the service in the background.</summary>
    private async Task ExportAsync()
    {
        using var save = new SaveFileDialog { Filter = "Excel dosyası|*.xlsx", FileName = $"Stok Kartları {DateTime.Now:yyyy-MM-dd HHmm}.xlsx" };
        if (save.ShowDialog(FindForm()) != DialogResult.OK) return;
        var filter = _filter; var path = save.FileName; var columns = GridExporter.ColumnsOf(_grid);
        _grid.SetLoading("Excel dosyası hazırlanıyor…");
        try
        {
            var count = await Task.Run(() =>
            {
                DataTable? all = null;
                for (var page = 1; ; page++)
                {
                    var result = _products.SearchPage((filter with { Page = page }).ToQuery(_session.CompanyId) with { PageSize = 500 });
                    var rows = Present(result.Rows);
                    if (all == null) all = rows; else all.Merge(rows);
                    if (page * 500 >= result.TotalCount) break;
                }
                GridExporter.ToExcel(columns, all!, path, "Stok Kartları");
                return all!.Rows.Count;
            });
            _log.LogInformation("Exported {Count} products to {Path}", count, path);
            R3Dialogs.Info(FindForm(), "Excel'e aktar", $"{count.ToString("N0", AppFormats.Culture)} kayıt aktarıldı:" + Environment.NewLine + path);
        }
        catch (Exception ex) { ErrorHandler.Report(ex, "Excel'e aktarılamadı", FindForm()); }
        finally { if (!IsDisposed) _grid.SetLoading(null); }
    }

    private ContextMenuStrip BuildContextMenu(bool canEdit)
    {
        var menu = new ContextMenuStrip { Font = AppTypography.Body };
        ToolStripMenuItem Item(string text, Action action, Keys keys = Keys.None, bool enabled = true)
        {
            var item = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = keys == Keys.None ? null : new KeysConverter().ConvertToString(keys), Enabled = enabled };
            item.Click += (_, _) => action();
            return item;
        }
        menu.Items.AddRange([
            Item("Aç", OpenCurrent, Keys.Enter), Item("Yeni Stok Kartı", OpenNew, Keys.Control | Keys.N, canEdit),
            Item("Kopyala", CopyCurrent, enabled: canEdit), Item("Aktif / Pasif", ToggleSelected, enabled: canEdit),
            new ToolStripSeparator(),
            Item("Stok kodunu kopyala", () => { if (_grid.CurrentDataRow is { } r) Clipboard.SetText(r["StokKodu"].ToString()!); }, Keys.Control | Keys.C),
            new ToolStripSeparator(),
            Item("Excel'e aktar", () => _ = ExportAsync(), Keys.Control | Keys.E), Item("Kolonlar…", ChooseColumns), Item("Yenile", () => _ = LoadAsync(), Keys.F5)
        ]);
        menu.Opening += (_, _) => { menu.Items[0].Enabled = _grid.CurrentRow != null; menu.Items[2].Enabled = canEdit && _grid.SelectedRows.Count == 1; menu.Items[3].Enabled = canEdit && _grid.SelectedRows.Count > 0; };
        return menu;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F5: _ = LoadAsync(); return true;
            case Keys.Control | Keys.F: _search.Focus(); _search.SelectAll(); return true;
            case Keys.Control | Keys.N or Keys.F2: OpenNew(); return true;
            case Keys.F3: OpenCurrent(); return true;
            case Keys.Control | Keys.E: _ = ExportAsync(); return true;
            case Keys.Alt | Keys.Left: GoToPage(_filter.Page - 1); return true;
            case Keys.Alt | Keys.Right: GoToPage(_filter.Page + 1); return true;
            case Keys.Control | Keys.C when _grid.Focused && _grid.CurrentDataRow is { } r: Clipboard.SetText(r["StokKodu"].ToString()!); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    internal R3DataGrid Grid => _grid;
    internal int TotalCount => _totalCount;
}
