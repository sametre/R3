using System.Windows.Controls;
using R3.Desktop.ViewModels;

namespace R3.Desktop.Views;

public partial class AccountTransactionsView : UserControl
{
    public AccountTransactionsView(AccountLedgerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
