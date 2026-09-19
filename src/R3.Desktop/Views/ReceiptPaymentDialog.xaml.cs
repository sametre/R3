using System.Windows;
using R3.Desktop.ViewModels;

namespace R3.Desktop.Views;

public partial class ReceiptPaymentDialog : Window
{
    public ReceiptPaymentDialog(ReceiptPaymentViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
