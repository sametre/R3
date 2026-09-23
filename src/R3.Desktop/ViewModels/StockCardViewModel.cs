using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using R3.Desktop.Logging;
using R3.Desktop.Presentation;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

/// <summary>Tedarikçiler sekmesinin düzenlenebilir satırı (ProductSupplierEdit + görünen cari kodu/adı).</summary>
public sealed partial class StockCardSupplierRow : ObservableObject
{
    public string Id { get; init; } = "";
    [ObservableProperty] private int _priority = 1;
    [ObservableProperty] private string _accountId = "";
    [ObservableProperty] private string _accountCode = "";
    [ObservableProperty] private string _accountName = "";
    [ObservableProperty] private string _supplierProductCode = "";
    [ObservableProperty] private int _leadTimeDays;
    [ObservableProperty] private int _extraLeadTimeDays;
    [ObservableProperty] private decimal? _minimumOrderQuantity;
    [ObservableProperty] private bool _isActive = true;
}

/// <summary>
/// Stok Kartı penceresi (ASB STOKKARTI düzeni). Mevcut kart görüntüleme modunda açılır; "Kayıt Güncelle" (F2) düzenlemeye
/// açar; Yeni Kayıt / Kopyala düzenleme modunda boş (veya kopyalanmış) kart hazırlar. Önceki/Sonraki listedeki sırayı
/// izler (liste verilmediyse stok kodu sırası). Kaydetme tek transaction'da LocalProductService.Save; kural doğrulaması
/// orada. Barkod/birim/stok politikası bu pencerede değişmez ve kaydederken korunur (İşlemler › Detaylı Kart).
/// </summary>
public sealed partial class StockCardViewModel : ObservableObject
{
    private readonly StoreDatabase _db;
    private readonly LocalProductService _products;
    private readonly LocalStockCardService _cards;
    private readonly string _companyId;
    private readonly IReadOnlyList<string> _siblings;
    private readonly ILogger _log = DesktopLogging.CreateLogger<StockCardViewModel>();
    private ProductAggregateEdit? _loaded;
    private bool _binding;

    public StockCardViewModel(StoreDatabase db, string companyId, string? productId, IReadOnlyList<string>? siblingIds = null, bool startAsCopy = false)
    {
        _db = db; _companyId = companyId; _siblings = siblingIds ?? [];
        _products = new LocalProductService(db); _cards = new LocalStockCardService(db);
        var master = new LocalMasterDataService(db);
        Units = master.List("units").DefaultView; Units.RowFilter = "Aktif = 1";
        Countries = master.List("countries").DefaultView; Countries.RowFilter = "Aktif = 1";
        PropertyChanged += OnFieldChanged;
        if (productId == null) PrepareNew(); else LoadCard(productId);
        if (startAsCopy && _loaded is { Id.Length: > 0 }) Copy();
    }

    // ---- sabit listeler -------------------------------------------------------------------------------------
    public IReadOnlyList<Option> ProductTypes => StockCardPresentation.ProductTypes;
    public IReadOnlyList<ClassOption> PurchaseClasses => StockCardPresentation.PurchaseClasses;
    public IReadOnlyList<ClassOption> SalesClasses => StockCardPresentation.SalesClasses;
    public IReadOnlyList<Option> VariantModes => StockCardPresentation.VariantModes;
    public IReadOnlyList<Option> States { get; } = [new("1", "Aktif"), new("0", "Pasif")];
    public DataView Units { get; }
    public DataView Countries { get; }
    public string CompanyId => _companyId;
    public LocalStockCardService Cards => _cards;

    // ---- durum ----------------------------------------------------------------------------------------------
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _errorMessage = "";
    public bool IsNew => _loaded is not { Id.Length: > 0 };
    public bool IsExisting => !IsNew;
    public bool CanNavigate => IsExisting && !IsEditing;
    public bool IsViewing => !IsEditing;
    partial void OnIsEditingChanged(bool value) { OnPropertyChanged(nameof(IsViewing)); OnPropertyChanged(nameof(CanNavigate)); }
    /// <summary>Kaydedilen / pasife alınan bir kart oldu: liste kapanışta yenilenir.</summary>
    public bool Changed { get; private set; }

    // ---- genel ----------------------------------------------------------------------------------------------
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _productType = "Stock";
    [ObservableProperty] private string _groupId = "";
    [ObservableProperty] private string _groupCode = "";
    [ObservableProperty] private string _groupName = "";
    [ObservableProperty] private string _unitId = "";
    [ObservableProperty] private string _originCountryId = "";
    [ObservableProperty] private string _relatedCode = "";
    [ObservableProperty] private string _relatedName = "";
    [ObservableProperty] private int _purchaseClass;
    [ObservableProperty] private int _salesClass;
    [ObservableProperty] private string _variantMode = "None";
    [ObservableProperty] private string _shortName = "";
    [ObservableProperty] private string _state = "1";

    // ---- satış / satınalma ----------------------------------------------------------------------------------
    [ObservableProperty] private decimal _vatRate;
    [ObservableProperty] private decimal _salesDiscountRate;
    [ObservableProperty] private decimal _exciseRate;
    [ObservableProperty] private decimal _exciseUnitPrice;
    [ObservableProperty] private int _deliveryLeadTimeDays;
    [ObservableProperty] private int _maximumDeliveryLeadTimeDays;
    [ObservableProperty] private string _shipmentLocation = "";
    [ObservableProperty] private decimal _purchaseVatRate;
    [ObservableProperty] private decimal _purchaseExciseRate;
    [ObservableProperty] private decimal _purchaseExciseUnitPrice;
    [ObservableProperty] private decimal _orderMultiple;
    [ObservableProperty] private decimal _minimumOrderQuantity;
    [ObservableProperty] private int _storageDays;
    [ObservableProperty] private int _installationDays;
    [ObservableProperty] private bool _allowFreeIssue;
    [ObservableProperty] private bool _isECommerce;
    [ObservableProperty] private bool _noCreditImmediateDelivery;

    // Sevk Yeri Tipi radyo düğmeleri (ShipmentLocation'ın dört görünümü)
    public bool ShipBranch { get => ShipmentLocation == "Branch"; set { if (value) ShipmentLocation = "Branch"; } }
    public bool ShipHeadquarters { get => ShipmentLocation == "Headquarters"; set { if (value) ShipmentLocation = "Headquarters"; } }
    public bool ShipSupplier { get => ShipmentLocation == "Supplier"; set { if (value) ShipmentLocation = "Supplier"; } }
    public bool ShipUnknown { get => ShipmentLocation is not ("Branch" or "Headquarters" or "Supplier"); set { if (value) ShipmentLocation = ""; } }
    partial void OnShipmentLocationChanged(string value)
    {
        foreach (var name in new[] { nameof(ShipBranch), nameof(ShipHeadquarters), nameof(ShipSupplier), nameof(ShipUnknown) }) OnPropertyChanged(name);
    }

    // ---- sekmeler -------------------------------------------------------------------------------------------
    [ObservableProperty] private string _brandId = "";
    [ObservableProperty] private string _brandCode = "";
    [ObservableProperty] private string _brandName = "";
    [ObservableProperty] private string _categoryId = "";
    [ObservableProperty] private string _categoryCode = "";
    [ObservableProperty] private string _categoryName = "";
    [ObservableProperty] private string _setCode = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _imagePath = "";
    public ObservableCollection<StockCardSupplierRow> Suppliers { get; } = [];
    [ObservableProperty] private StockCardSupplierRow? _selectedSupplier;
    [ObservableProperty] private DataView? _variants;

    // ---- yükleme / bağlama ----------------------------------------------------------------------------------

    private void OnFieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_binding || e.PropertyName is nameof(IsEditing) or nameof(IsDirty) or nameof(StatusText) or nameof(ErrorMessage) or nameof(SelectedSupplier)
            or nameof(Variants) or nameof(IsNew) or nameof(IsExisting) or nameof(CanNavigate) or nameof(IsViewing) or nameof(GroupName) or nameof(RelatedName) or nameof(BrandName) or nameof(CategoryName)) return;
        IsDirty = true;
    }

    private void LoadCard(string productId)
    {
        var detail = _products.GetDetail(productId, _companyId) ?? throw new KeyNotFoundException("Stok kartı bulunamadı.");
        Bind(detail.Product);
        SetMode(editing: false);
    }

    private void PrepareNew()
    {
        var unit = Units.Count > 0 ? Units[0]["Id"].ToString()! : "";
        Bind(new ProductAggregateEdit("", _companyId, "", "", "", "", unit, "Stock", 20, true, [], [], PurchaseVatRate: 20, IsSellable: true, CanQuote: true) { Details = new ProductCardDetailsEdit() });
        SetMode(editing: true);
    }

    private void Bind(ProductAggregateEdit p)
    {
        _binding = true;
        try
        {
            _loaded = p;
            var d = p.Details ?? new ProductCardDetailsEdit();
            Code = p.Code; Name = p.Name; ProductType = p.ProductType; UnitId = p.UnitId; OriginCountryId = p.OriginCountryId ?? "";
            GroupId = p.ProductGroupId ?? ""; (GroupCode, GroupName) = _cards.Describe("product_groups", GroupId);
            RelatedCode = p.ParentCode; RelatedName = _cards.NameOfCode(_companyId, p.ParentCode);
            PurchaseClass = d.PurchaseClass; SalesClass = d.SalesClass; ShortName = d.ShortName; State = p.IsActive ? "1" : "0";
            VariantMode = p.Variants.Any(v => v.IsActive) ? "Variants" : "None";
            VatRate = p.VatRate; SalesDiscountRate = d.SalesDiscountRate; ExciseRate = p.ExciseRate; ExciseUnitPrice = p.ExciseUnitPrice;
            DeliveryLeadTimeDays = p.Policy.DeliveryLeadTimeDays; MaximumDeliveryLeadTimeDays = p.Policy.MaximumDeliveryLeadTimeDays; ShipmentLocation = p.Policy.ShipmentLocationType;
            PurchaseVatRate = p.PurchaseVatRate; PurchaseExciseRate = d.PurchaseExciseRate; PurchaseExciseUnitPrice = d.PurchaseExciseUnitPrice;
            OrderMultiple = p.OrderMultiple; MinimumOrderQuantity = p.MinimumOrderQuantity; StorageDays = d.StorageDays; InstallationDays = d.InstallationDays;
            AllowFreeIssue = p.AllowFreeIssue; IsECommerce = d.IsECommerce; NoCreditImmediateDelivery = d.NoCreditImmediateDelivery;
            BrandId = p.BrandId; (BrandCode, BrandName) = _cards.Describe("brands", p.BrandId);
            CategoryId = p.CategoryId; (CategoryCode, CategoryName) = _cards.Describe("categories", p.CategoryId);
            SetCode = d.SetCode; Notes = d.Notes; ImagePath = p.ImagePath;
            Suppliers.Clear();
            foreach (var s in p.Suppliers.OrderBy(s => s.Priority))
            {
                var (code, name) = _cards.Describe("suppliers", s.SupplierAccountId);
                Suppliers.Add(Track(new StockCardSupplierRow { Id = s.Id, Priority = s.Priority, AccountId = s.SupplierAccountId, AccountCode = code, AccountName = name, SupplierProductCode = s.SupplierProductCode, LeadTimeDays = s.LeadTimeDays, ExtraLeadTimeDays = s.ExtraLeadTimeDays, MinimumOrderQuantity = s.MinimumOrderQuantity, IsActive = s.IsActive }));
            }
            var variants = new DataTable();
            foreach (var column in new[] { "Kod", "Ad", "Renk", "BedenTipi", "Beden", "Model", "Durum" }) variants.Columns.Add(column);
            foreach (var v in p.Variants) variants.Rows.Add(v.Code, v.Name, v.ColorCode, v.SizeType, v.SizeCode, v.ModelCode, v.IsActive ? "Aktif" : "Pasif");
            Variants = variants.DefaultView;
            ErrorMessage = ""; IsDirty = false;
        }
        finally { _binding = false; }
        OnPropertyChanged(nameof(IsNew)); OnPropertyChanged(nameof(IsExisting)); OnPropertyChanged(nameof(CanNavigate));
    }

    private void SetMode(bool editing)
    {
        IsEditing = editing;
        StatusText = IsNew ? "Yeni Kayıt" : editing ? "Kayıt Güncelleme" : "Görüntüleme  •  düzenlemek için Kayıt Güncelle (F2)";
        OnPropertyChanged(nameof(CanNavigate));
    }

    // ---- "…" seçimlerinden gelen değerler --------------------------------------------------------------------

    public void SetGroup(string id, string code, string name) { GroupId = id; GroupCode = code; GroupName = name; }
    public void SetRelated(string code, string name) { RelatedCode = code; RelatedName = name; }
    public void SetBrand(string id, string code, string name) { BrandId = id; BrandCode = code; BrandName = name; }
    public void SetCategory(string id, string code, string name) { CategoryId = id; CategoryCode = code; CategoryName = name; }
    public void SetSupplier(StockCardSupplierRow row, string id, string code, string name) { row.AccountId = id; row.AccountCode = code; row.AccountName = name; IsDirty = true; }

    private StockCardSupplierRow Track(StockCardSupplierRow row)
    {
        row.PropertyChanged += (_, _) => { if (!_binding) IsDirty = true; };
        return row;
    }

    [RelayCommand]
    private void AddSupplier()
    {
        var row = Track(new StockCardSupplierRow { Priority = Suppliers.Count == 0 ? 1 : Suppliers.Max(s => s.Priority) + 1 });
        Suppliers.Add(row); SelectedSupplier = row; IsDirty = true;
    }

    [RelayCommand]
    private void RemoveSupplier()
    {
        if (SelectedSupplier == null) return;
        Suppliers.Remove(SelectedSupplier); IsDirty = true;
    }

    // ---- komutlar -------------------------------------------------------------------------------------------

    [RelayCommand]
    private void BeginEdit()
    {
        if (IsNew || IsEditing) return;
        SetMode(editing: true);
    }

    [RelayCommand]
    private void Save() => SaveCard();

    /// <summary>Kaydet. true = kaydedildi (veya görüntüleme modunda kaydedilecek bir şey yoktu).</summary>
    public bool SaveCard()
    {
        if (!IsEditing) return true;
        ErrorMessage = "";
        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Name)) { ErrorMessage = "Stok kodu ve stok adı zorunludur."; return false; }
        if (string.IsNullOrWhiteSpace(UnitId)) { ErrorMessage = "Stok birimi seçin."; return false; }
        if (Suppliers.Any(s => string.IsNullOrWhiteSpace(s.AccountId))) { ErrorMessage = "Tedarikçiler sekmesinde tedarikçisi seçilmemiş satır var. Tedarikçi seçin veya satırı silin."; return false; }
        try
        {
            var edit = ToEdit();
            _products.Save(edit);
            var id = edit.Id.Length > 0 ? edit.Id : _cards.FindIdByCodeOrBarcode(_companyId, edit.Code) ?? throw new InvalidOperationException("Kaydedilen kart bulunamadı.");
            Changed = true;
            LoadCard(id);
            StatusText = $"Kaydedildi ({DateTime.Now:HH:mm:ss})  •  düzenlemek için Kayıt Güncelle (F2)";
            _log.LogInformation("Stock card {Code} saved", edit.Code);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException or Microsoft.Data.Sqlite.SqliteException)
        {
            _log.LogWarning(ex, "Stock card {Code} save failed", Code);
            ErrorMessage = ex is Microsoft.Data.Sqlite.SqliteException ? "Kayıt kaydedilemedi: " + ex.Message : ex.Message;
            return false;
        }
    }

    internal ProductAggregateEdit ToEdit()
    {
        var p = _loaded!;
        // Birim değişirse ürün birimlerindeki temel birim de değişir (aksi halde temel birim tutarsız kalır).
        var units = p.Units.Select(u => u.IsBaseUnit ? u with { UnitId = UnitId } : u).ToList();
        return p with
        {
            CompanyId = _companyId, Code = Code.Trim(), Name = Name.Trim(), ProductType = ProductType, UnitId = UnitId, IsActive = State == "1",
            ProductGroupId = GroupId, OriginCountryId = OriginCountryId ?? "", ParentCode = RelatedCode.Trim(),
            VatRate = VatRate, ExciseRate = ExciseRate, ExciseUnitPrice = ExciseUnitPrice, PurchaseVatRate = PurchaseVatRate,
            OrderMultiple = OrderMultiple, MinimumOrderQuantity = MinimumOrderQuantity, AllowFreeIssue = AllowFreeIssue,
            BrandId = BrandId, CategoryId = CategoryId, ImagePath = ImagePath.Trim(), Units = units,
            Policy = p.Policy with { DeliveryLeadTimeDays = DeliveryLeadTimeDays, MaximumDeliveryLeadTimeDays = MaximumDeliveryLeadTimeDays, ShipmentLocationType = ShipmentLocation },
            Suppliers = Suppliers.Select(s => new ProductSupplierEdit(s.Id, s.AccountId, s.SupplierProductCode, s.IsActive, s.LeadTimeDays, s.ExtraLeadTimeDays, Math.Max(1, s.Priority), s.MinimumOrderQuantity)).ToList(),
            Details = new ProductCardDetailsEdit(ShortName, Notes, SalesDiscountRate, PurchaseExciseRate, PurchaseExciseUnitPrice, StorageDays, InstallationDays, IsECommerce, NoCreditImmediateDelivery, PurchaseClass, SalesClass, SetCode)
        };
    }

    /// <summary>Yeni Kayıt: boş kart, düzenleme modunda.</summary>
    [RelayCommand]
    private void New() { PrepareNew(); }

    /// <summary>Kopyala: açık kartın değerleri yeni kayda taşınır (kod boş, barkodlar kopyalanmaz), düzenleme modunda.</summary>
    [RelayCommand]
    private void Copy()
    {
        if (_loaded is not { Id.Length: > 0 } source) return;
        var copy = source with
        {
            Id = "", Code = "", Barcodes = [],
            Units = source.Units.Select(u => u with { Id = "" }).ToList(),
            Suppliers = source.Suppliers.Select(s => s with { Id = "" }).ToList(),
            Variants = source.Variants.Select(v => v with { Id = "" }).ToList(),
            WarehousePolicies = source.WarehousePolicies
        };
        Bind(copy);
        SetMode(editing: true);
        IsDirty = true;
        StatusText = "Yeni Kayıt (kopya)  •  yeni stok kodunu yazıp kaydedin; barkodlar kopyalanmaz";
    }

    /// <summary>Kayıt Sil: stok kartı hareket geçmişini korumak için silinmez, pasife alınır.</summary>
    public void Deactivate()
    {
        if (_loaded is not { Id.Length: > 0 } current) return;
        try
        {
            _products.SetActive(current.Id, _companyId, false);
            Changed = true;
            LoadCard(current.Id);
            StatusText = "Kart pasife alındı.";
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException) { ErrorMessage = ex.Message; }
    }

    public bool Fetch(string codeOrBarcode)
    {
        var id = _cards.FindIdByCodeOrBarcode(_companyId, codeOrBarcode);
        if (id == null) { ErrorMessage = $"\"{codeOrBarcode.Trim()}\" kodlu veya barkodlu stok kartı bulunamadı."; return false; }
        LoadCard(id);
        return true;
    }

    public void Reload() { if (_loaded is { Id.Length: > 0 } current) LoadCard(current.Id); }

    /// <summary>Önceki / Sonraki: listeden açıldıysa listedeki sıra, değilse stok kodu sırası. false = uçtayız.</summary>
    public bool Move(bool next)
    {
        if (_loaded is not { Id.Length: > 0 } current) return false;
        string? target = null;
        var index = _siblings.ToList().IndexOf(current.Id);
        if (index >= 0)
        {
            var newIndex = index + (next ? 1 : -1);
            if (newIndex >= 0 && newIndex < _siblings.Count) target = _siblings[newIndex];
        }
        else target = _cards.Adjacent(_companyId, current.Code, next)?.Id;
        if (target == null) { StatusText = next ? "Son karttasınız." : "İlk karttasınız."; return false; }
        LoadCard(target);
        return true;
    }

    public string LoadedCode => _loaded?.Code ?? "";
    public string? LoadedId => _loaded is { Id.Length: > 0 } p ? p.Id : null;
    public bool LoadedIsActive => _loaded?.IsActive ?? true;
}
