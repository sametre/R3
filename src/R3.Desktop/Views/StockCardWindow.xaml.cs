using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using R3.Desktop.ViewModels;
using R3.Infrastructure;

namespace R3.Desktop.Views;

/// <summary>
/// Stok Kartı penceresi (ASB düzeni). Veri ve kurallar <see cref="StockCardViewModel"/> / LocalProductService'te; burada
/// yalnızca pencere işleri var: "…" seçim pencereleri, onay soruları, resim önizleme, klavye kısayolları.
/// Klavye: F9/Ctrl+S kaydet · F2 Kayıt Güncelle · Ctrl+N yeni · Ctrl+G kayıt getir · Alt+←/→ önceki/sonraki ·
/// Ctrl+1…5 sekmeler · Esc kapat (kaydedilmemiş değişiklik varsa sorar).
/// </summary>
public partial class StockCardWindow : Window
{
    private readonly StockCardViewModel _vm;
    private readonly StoreDatabase _db;

    public StockCardWindow(StockCardViewModel viewModel, StoreDatabase database)
    {
        InitializeComponent();
        _vm = viewModel; _db = database;
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(StockCardViewModel.ImagePath)) ShowPicture(); };
        PreviewKeyDown += OnKeys;
        Loaded += (_, _) => { ShowPicture(); if (_vm.IsNew) { CodeBox.Focus(); } };
    }

    /// <summary>A card was saved, deactivated or copied: the list refreshes when the window closes.</summary>
    public bool Changed => _vm.Changed;

    // ---- kaydedilmemiş değişiklikler ------------------------------------------------------------------------

    /// <summary>true = devam edilebilir (değişiklik yok, kaydedildi ya da vazgeçildi).</summary>
    private bool ConfirmLeave()
    {
        if (!_vm.IsEditing || !_vm.IsDirty) return true;
        var answer = MessageBox.Show(this, "Kaydedilmemiş değişiklikler var. Kaydedilsin mi?", "Stok Kartı", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        return answer switch { MessageBoxResult.Yes => _vm.SaveCard(), MessageBoxResult.No => true, _ => false };
    }

    private void Window_Closing(object? sender, CancelEventArgs e) { if (!ConfirmLeave()) e.Cancel = true; }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---- komutlar -------------------------------------------------------------------------------------------

    private void New_Click(object sender, RoutedEventArgs e) { if (ConfirmLeave()) { _vm.NewCommand.Execute(null); CodeBox.Focus(); } }

    private void Copy_Click(object sender, RoutedEventArgs e) { if (ConfirmLeave()) { _vm.CopyCommand.Execute(null); CodeBox.Focus(); } }

    private void Previous_Click(object sender, RoutedEventArgs e) => _vm.Move(next: false);

    private void Next_Click(object sender, RoutedEventArgs e) => _vm.Move(next: true);

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.LoadedIsActive) { MessageBox.Show(this, "Bu kart zaten pasif.", "Kayıt Sil", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var question = $"{_vm.LoadedCode} kodlu stok kartı pasife alınacak.\n\nStok hareketleri ve belgeler korunur; kart listede görünmez, Pasif Stok Kartları'ndan geri açılabilir. Devam edilsin mi?";
        if (MessageBox.Show(this, question, "Kayıt Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (!ConfirmLeave()) return;
        _vm.Deactivate();
    }

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.LoadedId is not { } id || _vm.LoadedIsActive) { MessageBox.Show(this, "Kart zaten aktif.", "Stok Kartı"); return; }
        if (!ConfirmLeave()) return;
        try { new LocalProductService(_db).SetActive(id, _vm.CompanyId, true); _vm.Reload(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Stok kartı güncellenemedi"); }
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e) { if (_vm.LoadedCode.Length > 0) Clipboard.SetText(_vm.LoadedCode); }

    /// <summary>Kayıt Getir: Stok Kodu alanına yazılan kod/barkod; alan boşsa ya da açık kartın koduysa listeden seçim.</summary>
    private void Fetch_Click(object sender, RoutedEventArgs e) => Fetch();

    private void Fetch()
    {
        var typed = _vm.Code.Trim();
        if (typed.Length > 0 && !string.Equals(typed, _vm.LoadedCode, StringComparison.OrdinalIgnoreCase))
        {
            // Alan arama kutusu olarak kullanıldı: açık kartın kodunu geri koy, böylece kaydetme sorusu kartı yeniden adlandırmaz.
            // Adı bile girilmemiş boş yeni kartta sorulacak bir şey yok.
            var blank = _vm.IsNew && string.IsNullOrWhiteSpace(_vm.Name);
            _vm.Code = _vm.LoadedCode;
            if (!blank && !ConfirmLeave()) { _vm.Code = typed; return; }
            if (!_vm.Fetch(typed)) _vm.Code = typed;
            return;
        }
        if (!ConfirmLeave()) return;
        if (Pick("products", "Stok Kartı Seç") is { } picked) _vm.Fetch(picked.Code);
    }

    private void Actions_Click(object sender, RoutedEventArgs e)
    {
        var menu = ActionsButton.ContextMenu!;
        menu.PlacementTarget = ActionsButton; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true;
    }

    /// <summary>İşlemler › Detaylı Kart: barkod, birim, varyant, stok politikası için R3'ün tam ürün kartı.</summary>
    private void DetailedCard_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.LoadedId is not { } id) { MessageBox.Show(this, "Önce kartı kaydedin.", "Detaylı Kart"); return; }
        if (!ConfirmLeave()) return;
        var products = new LocalProductService(_db);
        var detail = products.GetDetail(id, _vm.CompanyId);
        if (detail == null) return;
        var dialog = new ProductDialog(null, detail.Product.UnitId, _vm.CompanyId, _db, detail) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { products.Save(dialog.ToEditModel()); _vm.Reload(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Stok kartı kaydedilemedi"); }
    }

    // ---- "…" seçimleri --------------------------------------------------------------------------------------

    private (string Id, string Code, string Name)? Pick(string kind, string title, string? search = null)
    {
        var dialog = new StockLookupDialog(_vm.Cards, kind, _vm.CompanyId, title, search) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Selected : null;
    }

    private void PickGroup_Click(object sender, RoutedEventArgs e) { if (Pick("product_groups", "Stok Grubu Seç") is { } p) _vm.SetGroup(p.Id, p.Code, p.Name); }

    private void PickRelated_Click(object sender, RoutedEventArgs e) { if (Pick("products", "İlişkili Stok Kartı Seç", _vm.RelatedCode) is { } p) _vm.SetRelated(p.Code, p.Name); }

    private void RelatedCode_LostFocus(object sender, RoutedEventArgs e) => _vm.SetRelated(_vm.RelatedCode, _vm.Cards.NameOfCode(_vm.CompanyId, _vm.RelatedCode));

    private void PickBrand_Click(object sender, RoutedEventArgs e) { if (Pick("brands", "Marka Seç") is { } p) _vm.SetBrand(p.Id, p.Code, p.Name); }

    private void PickCategory_Click(object sender, RoutedEventArgs e) { if (Pick("categories", "Alt Grup Seç") is { } p) _vm.SetCategory(p.Id, p.Code, p.Name); }

    private void PickSupplier_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSupplier == null) _vm.AddSupplierCommand.Execute(null);
        var row = _vm.SelectedSupplier!;
        if (Pick("suppliers", "Tedarikçi Seç") is { } p) _vm.SetSupplier(row, p.Id, p.Code, p.Name);
    }

    // ---- resim ----------------------------------------------------------------------------------------------

    private void PickImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Resim dosyaları|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Tüm dosyalar|*.*", Title = "Ürün resmi seçin" };
        if (dialog.ShowDialog(this) == true) _vm.ImagePath = dialog.FileName;
    }

    private void ClearImage_Click(object sender, RoutedEventArgs e) => _vm.ImagePath = "";

    private void ShowPicture()
    {
        Picture.Source = null;
        var path = _vm.ImagePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            // OnLoad + ayrı akış: dosya kart açıkken kilitlenmez.
            var image = new BitmapImage();
            using var stream = File.OpenRead(path);
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            Picture.Source = image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException) { Picture.Source = null; }
    }

    // ---- klavye ---------------------------------------------------------------------------------------------

    private void OnKeys(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { Close(); e.Handled = true; }
        else if (ctrl && key == Key.N) { New_Click(this, e); e.Handled = true; }
        else if (ctrl && key == Key.G) { Fetch(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && key is Key.Left or Key.Right && _vm.CanNavigate) { _vm.Move(next: key == Key.Right); e.Handled = true; }
        else if (ctrl && key is >= Key.D1 and <= Key.D5) { Tabs.SelectedIndex = key - Key.D1; e.Handled = true; }
    }
}
