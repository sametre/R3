using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ClosedXML.Excel;
using Microsoft.Win32;
using R3.Desktop.ViewModels;
using R3.Desktop.Logging;
using R3.Infrastructure;

namespace R3.Desktop.Views;

public sealed class CustomerDateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        DateTime.TryParse(value?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("tr-TR")) : value?.ToString() ?? "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public partial class CustomerWorkspaceView : UserControl
{
    private readonly AccountServices _services;
    private readonly string _userName;
    private readonly Func<string, bool> _allowed;
    private CustomerWorkspaceViewModel Model => (CustomerWorkspaceViewModel)DataContext;
    public event Action<string>? SaleRequested;
    public event Action? CloseRequested;

    public CustomerWorkspaceView(CustomerWorkspaceViewModel model, AccountServices services, string userName, Func<string, bool> allowed)
    {
        InitializeComponent();
        Language = System.Windows.Markup.XmlLanguage.GetLanguage("tr-TR");
        _services = services; _userName = userName; _allowed = allowed;
        DataContext = model;
        CustomerPicker.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, e) =>
            {
                if (e.OriginalSource is not TextBox { IsKeyboardFocused: true } input || input.Text == Model.SelectedCustomer?.Display) return;
                var search = input.Text.Trim();
                var compare = CultureInfo.GetCultureInfo("tr-TR").CompareInfo;
                CollectionViewSource.GetDefaultView(Model.Customers).Filter = row => row is CustomerChoice customer &&
                    compare.IndexOf(customer.Display, search, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
                CustomerPicker.IsDropDownOpen = true;
            }));
        NewCustomerButton.IsEnabled = allowed("accounts.create");
        ReceiptButton.IsEnabled = allowed("accounts.receipt.create");
        SaleButton.IsEnabled = allowed("sales.invoice.post");
        MobileBasketButton.IsEnabled = allowed("sales.invoice.post");
        Loaded += async (_, _) => { await Model.InitializeAsync(); CustomerPicker.Focus(); };
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F4) { PickCustomer_Click(sender, e); e.Handled = true; }
        if (e.Key == Key.F3) { Receipt_Click(sender, e); e.Handled = true; }
        if (e.Key == Key.F5) { Refresh_Click(sender, e); e.Handled = true; }
    }
    private async void PickCustomer_Click(object sender, RoutedEventArgs e)
    {
        CollectionViewSource.GetDefaultView(Model.Customers).Filter = null;
        var dialog = new CustomerFinderDialog(new LocalCustomerWorkspaceService(_services.Database), _services.CompanyId)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.SelectedCustomerId)) return;
        await Model.ReloadCustomersAsync(dialog.SelectedCustomerId);
        await Model.RefreshAsync();
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await Model.ReloadCustomersAsync();
        await Model.RefreshAsync();
    }
    private void Operations_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireCustomer()) return;
        if (sender is Button { ContextMenu: { } menu } button) { menu.PlacementTarget = button; menu.IsOpen = true; }
    }
    private void NewCustomer_Click(object sender, RoutedEventArgs e) => EditCustomer(true);
    private void EditCustomer_Click(object sender, RoutedEventArgs e) => EditCustomer(false);
    private async void EditCustomer(bool create)
    {
        if (!_allowed(create ? "accounts.create" : "accounts.edit") || (!create && !RequireCustomer())) return;
        var edit = new AccountEditViewModel(_services, _userName, create ? null : Model.SelectedCustomer!.Id);
        if (create) edit.AccountType = "Customer";
        var dialog = new AccountEditDialog(edit) { Owner = Window.GetWindow(this) };
        var saved = false;
        edit.Saved += (_, _) => saved = true;
        edit.Closed += (_, _) => dialog.Close();
        dialog.ShowDialog();
        if (saved) { await Model.ReloadCustomersAsync(edit.AccountId); await Model.RefreshAsync(); }
    }
    private async void Receipt_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireCustomer() || !_allowed("accounts.receipt.create")) return;
        var customer = Model.SelectedCustomer!;
        var dialog = new InstallmentCollectionDialog(
            Model.Data!.Installments,
            new LocalInstallmentCollectionService(_services.Database),
            _services.CompanyId, _services.BranchId, customer.Id, Model.Currency, customer.Display)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true) await Model.RefreshAsync();
    }
    private async void AddNote_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireCustomer() || !_allowed("accounts.edit")) return;
        var edit = new AccountNoteEditViewModel(_services.Notes, Model.SelectedCustomer!.Id, _userName,
            DesktopLogging.CreateLogger<AccountNoteEditViewModel>());
        var dialog = new AccountNoteEditDialog(edit) { Owner = Window.GetWindow(this) };
        edit.Saved += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() == true) await Model.RefreshAsync();
    }
    private async void Sale_Click(object sender, RoutedEventArgs e)
    {
        await OpenQuickSaleAsync("Satış faturası");
    }

    private async void MobileBasket_Click(object sender, RoutedEventArgs e) => await OpenQuickSaleAsync("Mobil Sepet");

    private async Task OpenQuickSaleAsync(string source)
    {
        if (!RequireCustomer() || !_allowed("sales.invoice.post")) return;
        Model.Status = $"{source} için seçili müşteriyle hızlı satış ekranı açılıyor…";
        SaleRequested?.Invoke(Model.SelectedCustomer!.Id);
        await Model.RefreshAsync();
    }

    private bool RequireCustomer()
    {
        if (Model.SelectedCustomer != null && Model.Data != null && !Model.IsBusy) return true;
        Model.Status = "Bu işlem için önce bir müşteri seçin. Müşteri kodunu yazın veya F4 ile listeden seçin.";
        CustomerPicker.Focus();
        return false;
    }
    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireCustomer() || Model.Data == null) return;
        var save = new SaveFileDialog { Filter = "Excel çalışma kitabı (*.xlsx)|*.xlsx", FileName = "Musteri-Cari-Ekstresi.xlsx" };
        if (save.ShowDialog(Window.GetWindow(this)) != true) return;
        var table = Model.Data.Statement.Copy();
        var headers = new[] { "Tarih", "İşlem", "No", "Şube", "Açıklama", "Fatura No", "Tutar", "İskonto", "Adet", "Borç Tutarı", "Alacak Tutarı", "Bakiye", "Döviz" };
        for (var i = 0; i < headers.Length; i++) table.Columns[i].ColumnName = headers[i];
        try
        {
            await Task.Run(() =>
            {
                using var book = new XLWorkbook();
                var sheet = book.Worksheets.Add(table, "Cari Hesap Ekstresi");
                sheet.Columns(7, 12).Style.NumberFormat.Format = "#,##0.00";
                sheet.Columns().AdjustToContents(8, 50); sheet.SheetView.FreezeRows(1);
                book.SaveAs(save.FileName);
            });
            Model.Status = "Cari hesap ekstresi Excel dosyasına aktarıldı.";
        }
        catch (Exception) { Model.Status = "Excel dosyası kaydedilemedi. Dosyanın açık olmadığını ve klasörün yazılabilir olduğunu kontrol edin."; }
    }

    private void OnAutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.Column is not DataGridTextColumn column) return;
        var binding = new Binding($"[{e.PropertyName}]");
        var type = Nullable.GetUnderlyingType(e.PropertyType) ?? e.PropertyType;
        if (type == typeof(decimal) || type == typeof(double) || type == typeof(float) || type == typeof(long) || type == typeof(int))
        {
            binding.StringFormat = e.PropertyName.Contains("No") ? "N0" : "N2";
            column.ElementStyle = (Style)FindResource("AmountCell");
        }
        else if (e.PropertyName.Contains("Tarih") || e.PropertyName == "Vade") binding.Converter = new CustomerDateConverter();
        column.Binding = binding;
        column.SortMemberPath = e.PropertyName;
        column.MinWidth = e.PropertyName is "Not" or "Ürün" or "Açıklama" ? 350 : 90;
        column.Width = e.PropertyName is "Not" or "Ürün" or "Açıklama" ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.SizeToHeader;
    }
}
