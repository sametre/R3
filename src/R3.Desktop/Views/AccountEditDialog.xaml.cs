using System.Windows;
using R3.Desktop.ViewModels;

namespace R3.Desktop.Views;

public partial class AccountEditDialog : Window
{
    public AccountEditDialog(AccountEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
