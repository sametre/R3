using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using R3.Desktop.ViewModels;

namespace R3.Desktop.Views;

public partial class AccountAddressesContactsView : UserControl
{
    public AccountAddressesContactsView(AccountAddressesContactsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private AccountAddressesContactsViewModel ViewModel => (AccountAddressesContactsViewModel)DataContext;

    private void AddAddress_Click(object sender, RoutedEventArgs e) => OpenAddressEditor(asNew: true);

    private void EditAddress_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.AddressList.Selected == null) { MessageBox.Show(Window.GetWindow(this), "Önce bir adres seçin.", "Adresler"); return; }
        OpenAddressEditor(asNew: false);
    }

    private void EditAddress_Click(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.AddressList.Selected != null) OpenAddressEditor(asNew: false);
    }

    private void DeactivateAddress_Click(object sender, RoutedEventArgs e) => ViewModel.AddressList.DeactivateCommand.Execute(null);
    private void MakeDefaultAddress_Click(object sender, RoutedEventArgs e) => ViewModel.AddressList.MakeDefaultCommand.Execute(null);
    private void RefreshAddresses_Click(object sender, RoutedEventArgs e) => ViewModel.AddressList.RefreshCommand.Execute(null);
    private void CopyAddress_Click(object sender, RoutedEventArgs e) { if (ViewModel.AddressList.Selected is { } item) Clipboard.SetText(item.AddressLine); }

    private void OpenAddressEditor(bool asNew)
    {
        var editViewModel = ViewModel.AddressList.CreateEditViewModel(asNew);
        var dialog = new AccountAddressEditDialog(editViewModel) { Owner = Window.GetWindow(this) };
        var saved = false;
        editViewModel.Saved += (_, _) => { saved = true; dialog.DialogResult = true; };
        dialog.ShowDialog();
        if (saved) ViewModel.AddressList.RefreshCommand.Execute(null);
    }

    private void AddContact_Click(object sender, RoutedEventArgs e) => OpenContactEditor(asNew: true);

    private void EditContact_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ContactList.Selected == null) { MessageBox.Show(Window.GetWindow(this), "Önce bir yetkili seçin.", "Yetkililer"); return; }
        OpenContactEditor(asNew: false);
    }

    private void EditContact_Click(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.ContactList.Selected != null) OpenContactEditor(asNew: false);
    }

    private void DeactivateContact_Click(object sender, RoutedEventArgs e) => ViewModel.ContactList.DeactivateCommand.Execute(null);
    private void MakePrimaryContact_Click(object sender, RoutedEventArgs e) => ViewModel.ContactList.MakePrimaryCommand.Execute(null);
    private void RefreshContacts_Click(object sender, RoutedEventArgs e) => ViewModel.ContactList.RefreshCommand.Execute(null);
    private void CopyContactPhone_Click(object sender, RoutedEventArgs e) { if (ViewModel.ContactList.Selected is { } item) Clipboard.SetText(string.IsNullOrWhiteSpace(item.MobilePhone) ? item.Phone : item.MobilePhone); }
    private void CopyContactEmail_Click(object sender, RoutedEventArgs e) { if (ViewModel.ContactList.Selected is { Email.Length: > 0 } item) Clipboard.SetText(item.Email); }

    private void Grid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source != null && source is not DataGridRow) source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        if (source is DataGridRow row) row.IsSelected = true;
    }

    private void OpenContactEditor(bool asNew)
    {
        var editViewModel = ViewModel.ContactList.CreateEditViewModel(asNew);
        var dialog = new AccountContactEditDialog(editViewModel) { Owner = Window.GetWindow(this) };
        var saved = false;
        editViewModel.Saved += (_, _) => { saved = true; dialog.DialogResult = true; };
        dialog.ShowDialog();
        if (saved) ViewModel.ContactList.RefreshCommand.Execute(null);
    }
}
