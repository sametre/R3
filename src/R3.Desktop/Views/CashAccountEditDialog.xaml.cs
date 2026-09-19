using System.Windows;
using R3.Desktop.ViewModels;

namespace R3.Desktop.Views;

public partial class CashAccountEditDialog : Window
{
    public CashAccountEditDialog(CashAccountEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
