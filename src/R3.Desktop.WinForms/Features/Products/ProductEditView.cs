using System.Data;
using Krypton.Toolkit;
using Microsoft.Extensions.Logging;
using R3.Desktop.WinForms.Components.Cards;
using R3.Desktop.WinForms.Components.Dialogs;
using R3.Desktop.WinForms.Components.Editors;
using R3.Desktop.WinForms.Components.Grids;
using R3.Desktop.WinForms.Components.Toolbars;
using R3.Desktop.WinForms.Design;
using R3.Desktop.WinForms.Features.Shared;
using R3.Desktop.WinForms.Infrastructure.Errors;
using R3.Desktop.WinForms.Infrastructure.Logging;
using R3.Desktop.WinForms.Infrastructure.Navigation;
using R3.Desktop.WinForms.Infrastructure.Session;
using R3.Desktop.WinForms.Shell.Workspace;
using R3.Infrastructure;

namespace R3.Desktop.WinForms.Features.Products;

/// <summary>
/// Stok kartı (open/edit/new) as a workspace tab. Header fields are editable; barcodes, units and suppliers are
/// shown read-only in this migration step and saved back unchanged. Ctrl+S saves, Esc/Ctrl+W close (asks when
/// there are unsaved changes). Loads off the UI thread; saving goes through LocalProductService.Save.
/// </summary>
public sealed class ProductEditView : UserControl, ICloseGuard
{
    private const string EditPermission = "inventory.product.edit";

    private readonly AppSession _session;
    private readonly IWorkspaceNavigator _navigator;
    private readonly LocalProductService _products;
    private readonly Action<string>? _saved;
    private readonly ILogger _log = AppLog.For<ProductEditView>();
    private readonly Panel _body = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = AppColors.Background };
    private readonly Label _title = new() { AutoSize = true, Font = AppTypography.PageTitle, ForeColor = AppColors.TextPrimary, Margin = Padding.Empty };
    private readonly Label _subtitle = new() { AutoSize = true, Font = AppTypography.Caption, ForeColor = AppColors.TextSecondary, Margin = new Padding(0, 2, 0, 0) };
    private readonly Label _message = new() { AutoSize = true, Font = AppTypography.Body, ForeColor = AppColors.Danger, Margin = new Padding(AppSpacing.MD, AppSpacing.SM, 0, 0), MaximumSize = new Size(700, 0) };
    private readonly R3PrimaryButton _save;
    private string _productId;
    private ProductAggregateEdit? _loaded;
    private ProductEditModel? _baseline;
    private Func<ProductEditModel>? _read;

    private ProductEditView(AppSession session, IWorkspaceNavigator navigator, string? productId, Action<string>? saved)
    {
        _session = session; _navigator = navigator; _productId = productId ?? ""; _saved = saved;
        _products = new LocalProductService(session.Database);
        BackColor = AppColors.Background; Font = AppTypography.Body;

        var toolbar = new R3Toolbar();
        _save = toolbar.Add(new R3PrimaryButton("Kaydet", () => Save()), "Kaydet (Ctrl+S)");
        toolbar.Add(new R3SecondaryButton("Kapat", () => _navigator.Close(this)), "Kapat (Esc)");
        toolbar.Controls.Add(_message);
        _save.Enabled = false;

        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, BackColor = AppColors.Background, Padding = new Padding(0, 0, 0, AppSpacing.SM) };
        header.Controls.Add(_title); header.Controls.Add(_subtitle);

        SuspendLayout();
        Controls.Add(_body); Controls.Add(toolbar); Controls.Add(header);
        ResumeLayout();
        _title.Text = productId == null ? "Yeni Stok Kartı" : "Stok kartı yükleniyor…";
        Load += async (_, _) => await LoadAsync();
    }

    public static void Open(IWorkspaceNavigator navigator, AppSession session, string productId, string code, Action<string>? saved) =>
        navigator.Open("inventory.product:" + productId, code, () => new ProductEditView(session, navigator, productId, saved));

    public static void OpenNew(IWorkspaceNavigator navigator, AppSession session, Action<string>? saved) =>
        navigator.Open("inventory.product:new:" + Guid.NewGuid().ToString("N"), "Yeni Stok Kartı", () => new ProductEditView(session, navigator, null, saved));

    // ---- load -----------------------------------------------------------------------------------------------

    private async Task LoadAsync()
    {
        try
        {
            var master = new LocalMasterDataService(_session.Database);
            var data = await Task.Run(() => new
            {
                Detail = _productId.Length == 0 ? null : _products.GetDetail(_productId, _session.CompanyId),
                Units = Lookups.Load(master, "units"), Brands = Lookups.Load(master, "brands"), Categories = Lookups.Load(master, "categories"),
                Groups = Lookups.Load(master, "product_groups"), Countries = Lookups.Load(master, "countries")
            });
            if (IsDisposed) return;
            if (_productId.Length > 0 && data.Detail == null) { _title.Text = "Stok kartı bulunamadı"; _message.Text = "Kart silinmiş veya bu firmaya ait değil."; return; }
            _loaded = data.Detail?.Product ?? ProductEditModel.NewAggregate(_session.CompanyId, data.Units.FirstOrDefault()?.Id ?? "");
            _baseline = ProductEditModel.From(_loaded);
            BuildForm(_baseline, data.Units, data.Brands, data.Categories, data.Groups, data.Countries);
            UpdateHeader();
            _save.Enabled = _session.Can(EditPermission);
            if (!_save.Enabled) _message.Text = "Bu kartı düzenleme yetkiniz yok; salt okunur açıldı.";
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _log.LogError(ex, "Product {Id} failed to load", _productId);
            _title.Text = "Stok kartı açılamadı"; _message.Text = ErrorHandler.UserMessage(ex);
        }
    }

    private void UpdateHeader()
    {
        if (_loaded == null) return;
        _title.Text = _loaded.Id.Length == 0 ? "Yeni Stok Kartı" : $"{_loaded.Code}  ·  {_loaded.Name}";
        _subtitle.Text = _loaded.Id.Length == 0 ? "Zorunlu alanlar * ile işaretlidir. Ctrl+S kaydeder."
            : $"{(_loaded.IsActive ? "Aktif" : "Pasif")} · {ProductText.Type(_loaded.ProductType)} · {_loaded.Barcodes.Count(b => b.IsActive)} barkod · {_loaded.Units.Count} birim · {_loaded.Suppliers.Count} tedarikçi";
    }

    private void BuildForm(ProductEditModel m, List<LookupItem> units, List<LookupItem> brands, List<LookupItem> categories, List<LookupItem> groups, List<LookupItem> countries)
    {
        var code = R3Editors.Text(m.Code, 50); var name = R3Editors.Text(m.Name, 200); var parent = R3Editors.Text(m.ParentCode, 50);
        var type = R3Editors.Choice(ProductText.Types, m.ProductType); var unit = R3Editors.Lookup(units, m.UnitId, optional: false);
        var barcode = R3Editors.Text(m.PrimaryBarcode, 50);
        var active = R3Editors.Check("Aktif", m.IsActive); var sellable = R3Editors.Check("Satışa açık", m.IsSellable);
        var quote = R3Editors.Check("Teklifte kullanılabilir", m.CanQuote); var complete = R3Editors.Check("Tanım tamamlandı / onaylandı", m.IsDefinitionComplete);
        var brand = R3Editors.Lookup(brands, m.BrandId, optional: true); var category = R3Editors.Lookup(categories, m.CategoryId, optional: true);
        var group = R3Editors.Lookup(groups, m.ProductGroupId, optional: true); var country = R3Editors.Lookup(countries, m.OriginCountryId, optional: true);
        var vat = R3Editors.Number(m.VatRate, 2, 100); var purchaseVat = R3Editors.Number(m.PurchaseVatRate, 2, 100);
        var excise = R3Editors.Number(m.ExciseRate, 2, 100); var exciseUnit = R3Editors.Number(m.ExciseUnitPrice);
        var freeIssue = R3Editors.Check("Bedelsiz girişe izin", m.AllowFreeIssue); var bundle = R3Editors.Check("Takım / set ürün", m.IsBundle);
        var minStock = R3Editors.Number(m.MinimumStock); var maxStock = R3Editors.Number(m.MaximumStock);
        var minOrder = R3Editors.Number(m.MinimumOrderQuantity); var multiple = R3Editors.Number(m.OrderMultiple);
        var lead = R3Editors.Number(m.DeliveryLeadTimeDays, 0, 3650); var maxLead = R3Editors.Number(m.MaximumDeliveryLeadTimeDays, 0, 3650);
        var lot = R3Editors.Choice(ProductEditModel.LotTrackingTypes, m.LotTrackingType); var pieces = R3Editors.Number(m.PieceCount, 0, 100_000);
        var shipment = R3Editors.Text(m.ShipmentLocationType, 80);

        var general = new R3FormSection("Genel");
        general.Add("Stok kodu", code, required: true); general.Add("Ana stok kodu", parent, toolTip: "Varyantların bağlı olduğu üst kod (opsiyonel)");
        general.Add("Stok adı", name, required: true, wide: true);
        general.Add("Ürün tipi", type); general.Add("Temel birim", unit, required: true);
        general.Add("Birincil barkod", barcode, toolTip: "Var olan bir barkodu yazarsanız birincil yapılır; yeni barkod birincil barkodun yerine geçer.");
        general.AddFlags(active, sellable, quote, complete);
        var classification = new R3FormSection("Sınıflandırma");
        classification.Add("Marka", brand); classification.Add("Kategori", category);
        classification.Add("Stok grubu", group); classification.Add("Menşe", country);
        var commercial = new R3FormSection("Ticari bilgiler");
        commercial.Add("Satış KDV %", vat); commercial.Add("Alış KDV %", purchaseVat);
        commercial.Add("ÖTV %", excise); commercial.Add("ÖTV birim fiyatı", exciseUnit);
        commercial.AddFlags(freeIssue, bundle);
        var stock = new R3FormSection("Stok ve sipariş");
        stock.Add("Minimum stok", minStock); stock.Add("Maksimum stok", maxStock);
        stock.Add("Min. sipariş miktarı", minOrder); stock.Add("Sipariş katı", multiple, toolTip: "0 = kısıt yok");
        stock.Add("Teslim süresi (gün)", lead); stock.Add("Maks. teslim (gün)", maxLead);
        stock.Add("Lot / seri takibi", lot); stock.Add("Parça sayısı", pieces);
        stock.Add("Sevk yeri tipi", shipment);

        _read = () => new ProductEditModel(code.Text, name.Text, parent.Text, R3Editors.Value(type), R3Editors.Value(unit), barcode.Text,
            active.Checked, sellable.Checked, quote.Checked, complete.Checked, freeIssue.Checked, bundle.Checked,
            R3Editors.Value(brand), R3Editors.Value(category), R3Editors.Value(group), R3Editors.Value(country),
            vat.Value, purchaseVat.Value, excise.Value, exciseUnit.Value, minStock.Value, maxStock.Value, minOrder.Value, multiple.Value,
            (int)lead.Value, (int)maxLead.Value, R3Editors.Value(lot), (int)pieces.Value, shipment.Text);

        var sections = new List<Control> { general, classification, commercial, stock, ChildList("Barkodlar", BarcodeTable(_loaded!), units) };
        _body.SuspendLayout();
        _body.Controls.Clear();
        foreach (var section in Enumerable.Reverse(sections))
        {
            _body.Controls.Add(section);
            _body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = AppSpacing.SM, BackColor = AppColors.Background });
        }
        _body.ResumeLayout();
        var readOnly = !_session.Can(EditPermission);
        foreach (var section in sections.OfType<R3FormSection>()) section.Enabled = !readOnly;
        (m.Code.Length == 0 ? code : name).Focus();
    }

    private static DataTable BarcodeTable(ProductAggregateEdit product)
    {
        var table = new DataTable();
        table.Columns.Add("Barkod"); table.Columns.Add("Birim"); table.Columns.Add("Miktar", typeof(decimal)); table.Columns.Add("Birincil"); table.Columns.Add("Durum");
        foreach (var b in product.Barcodes) table.Rows.Add(b.Barcode, b.UnitId, b.Quantity, b.IsPrimary ? "Evet" : "", b.IsActive ? "Aktif" : "Pasif");
        return table;
    }

    private static Control ChildList(string title, DataTable rows, List<LookupItem> units)
    {
        foreach (DataRow row in rows.Rows) row["Birim"] = units.FirstOrDefault(u => u.Id == row["Birim"]?.ToString())?.Name ?? row["Birim"];
        var section = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, BackColor = AppColors.Surface, Padding = new Padding(AppSpacing.LG, AppSpacing.MD, AppSpacing.LG, AppSpacing.MD) };
        section.Controls.Add(new Label { Text = $"{title} ({rows.Rows.Count})", Font = AppTypography.Section, ForeColor = AppColors.TextPrimary, AutoSize = true, Margin = new Padding(0, 0, 0, AppSpacing.XS) });
        section.Controls.Add(new Label { Text = "Bu bölüm bu geçiş adımında salt okunurdur; kayıtlar kaydederken değişmeden korunur.", Font = AppTypography.Caption, ForeColor = AppColors.TextSecondary, AutoSize = true, Margin = new Padding(0, 0, 0, AppSpacing.SM) });
        var grid = new R3DataGrid { Height = Math.Min(220, 34 + rows.Rows.Count * AppSizes.GridRowHeight(GridDensity.Compact)), Dock = DockStyle.Top, MultiSelect = false };
        grid.AddText("Barkod", "Barkod", 180); grid.AddText("Birim", "Birim", 120); grid.AddNumber("Miktar", "Miktar", 90); grid.AddText("Birincil", "Birincil", 80); grid.AddText("Durum", "Durum", 80, fill: true);
        grid.SetEmptyText("Barkod tanımlı değil.");
        grid.DataSource = rows;
        section.Controls.Add(grid);
        return section;
    }

    // ---- save / close ---------------------------------------------------------------------------------------

    private bool IsDirty => _read != null && _baseline != null && _read() != _baseline;

    private bool Save()
    {
        if (_read == null || _loaded == null || !_session.Can(EditPermission)) return false;
        var model = _read();
        var errors = model.Validate();
        if (errors.Count > 0) { _message.Text = string.Join(" ", errors); return false; }
        try
        {
            var edit = model.ApplyTo(_loaded);
            _products.Save(edit);
            var id = edit.Id.Length > 0 ? edit.Id : FindIdByCode(edit.Code);
            _loaded = _products.GetDetail(id, _session.CompanyId)?.Product ?? edit;
            _productId = _loaded.Id; _baseline = ProductEditModel.From(_loaded);
            _message.ForeColor = AppColors.Success; _message.Text = $"Kaydedildi ({DateTime.Now:HH:mm:ss}).";
            _log.LogInformation("Product {Code} saved by {User}", _loaded.Code, _session.LoginName);
            UpdateHeader(); _navigator.SetTitle(this, _loaded.Code);
            _saved?.Invoke(_productId);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Product {Code} save failed", model.Code);
            _message.ForeColor = AppColors.Danger; _message.Text = ErrorHandler.UserMessage(ex);
            return false;
        }
    }

    private string FindIdByCode(string code)
    {
        var page = _products.SearchPage(new ProductListQuery(_session.CompanyId, Code: code, PageSize: 20));
        return page.Rows.Rows.Cast<DataRow>().FirstOrDefault(r => string.Equals(r["StokKodu"]?.ToString(), code, StringComparison.OrdinalIgnoreCase))?["Id"]?.ToString() ?? "";
    }

    public bool CanClose()
    {
        if (!IsDirty) return true;
        var answer = R3Dialogs.SaveChanges(FindForm(), "Stok kartı");
        return answer switch { DialogResult.Yes => Save(), DialogResult.No => true, _ => false };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.S: Save(); return true;
            case Keys.Escape: _navigator.Close(this); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
