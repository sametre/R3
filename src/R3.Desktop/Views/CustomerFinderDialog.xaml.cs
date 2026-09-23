using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using R3.Infrastructure;

namespace R3.Desktop.Views;

public partial class CustomerFinderDialog : Window
{
    private readonly LocalCustomerWorkspaceService _service;
    private readonly string _companyId;
    public string? SelectedCustomerId { get; private set; }

    public CustomerFinderDialog(LocalCustomerWorkspaceService service, string companyId)
    {
        InitializeComponent(); _service = service; _companyId = companyId;
        Loaded += (_, _) => { Search(); CodeBox.Focus(); };
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F4) { Search(); e.Handled = true; }
        if (e.Key == Key.Enter && Keyboard.FocusedElement is TextBox) { Search(); e.Handled = true; }
    }
    private void Search_Click(object sender, RoutedEventArgs e) => Search();
    private void Search()
    {
        try
        {
            var table = _service.SearchCustomers(_companyId, CodeBox.Text, NameBox.Text, IdentityBox.Text, MobileBox.Text, PhoneBox.Text, CityBox.Text, DistrictBox.Text);
            ResultsGrid.ItemsSource = table.DefaultView; StatusText.Text = $"{table.Rows.Count:N0} müşteri bulundu";
            if (table.Rows.Count > 0) ResultsGrid.SelectedIndex = 0;
        }
        catch (Exception ex) { StatusText.Text = $"Arama yapılamadı: {ex.Message}"; }
    }
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var box in new[] { CodeBox, NameBox, IdentityBox, MobileBox, PhoneBox, CityBox, DistrictBox }) box.Clear();
        Search(); CodeBox.Focus();
    }
    private void ResultsGrid_DoubleClick(object sender, MouseButtonEventArgs e) => Select();
    private void Select_Click(object sender, RoutedEventArgs e) => Select();
    private void Select()
    {
        if (ResultsGrid.SelectedItem is not DataRowView row) { StatusText.Text = "Listeden bir müşteri seçin."; return; }
        SelectedCustomerId = row["Id"].ToString(); DialogResult = true;
    }
}
