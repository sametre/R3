using System.Windows;
using R3.Desktop.ViewModels;

namespace R3.Desktop.Views;

public partial class ManualLedgerEntryDialog : Window
{
    public ManualLedgerEntryDialog(ManualLedgerEntryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
