using System.Windows;
using R3.Desktop.ViewModels;

namespace R3.Desktop.Views;

public partial class CashInOutDialog : Window
{
    public CashInOutDialog(CashInOutViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
