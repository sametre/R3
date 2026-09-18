using System.Windows;
using R3.Desktop.ViewModels;

namespace R3.Desktop.Views;

public partial class AccountAddressEditDialog : Window
{
    public AccountAddressEditDialog(AccountAddressEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
