using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using R3.Infrastructure;

namespace R3.Desktop.Views;

public partial class InstallmentCollectionDialog : Window
{
    private readonly DataTable _lines;
    private readonly LocalInstallmentCollectionService _service;
    private readonly string _companyId, _branchId, _accountId, _currency;

    public InstallmentCollectionDialog(DataTable installments, LocalInstallmentCollectionService service,
        string companyId, string branchId, string accountId, string currency, string customer)
    {
        InitializeComponent();
        _service = service; _companyId = companyId; _branchId = branchId; _accountId = accountId; _currency = currency;
        _lines = installments.Clone();
        _lines.Columns.Add("Seç", typeof(bool));
        _lines.Columns.Add("Tahsilat", typeof(decimal));
        foreach (DataRow source in installments.Rows)
        {
            var row = _lines.NewRow();
            foreach (DataColumn column in installments.Columns) row[column.ColumnName] = source[column];
            row["Seç"] = false;
            row["Tahsilat"] = Convert.ToDecimal(source["Kalan"], CultureInfo.InvariantCulture);
            _lines.Rows.Add(row);
        }
        CustomerText.Text = customer;
        CollectionDate.SelectedDate = DateTime.Today;
        InstallmentGrid.ItemsSource = _lines.DefaultView;
        StatusText.Text = _lines.Rows.Count == 0 ? "Bu müşteri için seçili dövizde açık taksit bulunmuyor." : "Tahsil edilecek taksitleri ve tutarlarını seçin.";
        UpdateSummary();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && CollectButton.IsEnabled) Collect_Click(sender, e);
    }
    private void InstallmentGrid_CurrentCellChanged(object? sender, EventArgs e) => UpdateSummary();
    private void InstallmentGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) => Dispatcher.BeginInvoke(UpdateSummary);
    private void UpdateSummary()
    {
        var total = _lines.AsEnumerable().Where(row => row.Field<bool>("Seç"))
            .Sum(row => Math.Max(0, Convert.ToDecimal(row["Tahsilat"], CultureInfo.InvariantCulture)));
        TotalText.Text = total.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"));
        CollectButton.IsEnabled = total > 0;
    }
    private void Collect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var amounts = _lines.AsEnumerable().Where(row => row.Field<bool>("Seç"))
                .ToDictionary(row => row.Field<string>("Id")!, row => Convert.ToDecimal(row["Tahsilat"], CultureInfo.InvariantCulture));
            var method = ((ComboBoxItem)PaymentMethodCombo.SelectedItem).Tag.ToString()!;
            _service.Collect(_companyId, _branchId, _accountId, _currency, CollectionDate.SelectedDate ?? DateTime.Today, amounts, method, DescriptionBox.Text);
            DialogResult = true;
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
}
