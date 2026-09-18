using System.Windows;
using R3.Desktop.ViewModels;
using R3.Infrastructure;

namespace R3.Desktop.Views;

public partial class AccountEditDialog : Window
{
    public AccountEditDialog(AccountEditViewModel viewModel, StoreDatabase? database = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        if (database != null && !string.IsNullOrEmpty(viewModel.AccountId))
        {
            var addressesContacts = AccountAddressesContactsViewModel.Create(database, viewModel.AccountId);
            AddressesContactsHost.Content = new AccountAddressesContactsView(addressesContacts);
        }
    }
}
