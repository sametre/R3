using System.Windows;
using R3.Desktop.ViewModels;

namespace R3.Desktop.Views;

public partial class CashTransferDialog : Window
{
    public CashTransferDialog(CashTransferViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
