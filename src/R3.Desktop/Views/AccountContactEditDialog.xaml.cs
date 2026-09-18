using System.Windows;
using R3.Desktop.ViewModels;

namespace R3.Desktop.Views;

public partial class AccountContactEditDialog : Window
{
    public AccountContactEditDialog(AccountContactEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
