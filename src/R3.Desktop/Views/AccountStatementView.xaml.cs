using System.Windows.Controls;
using R3.Desktop.ViewModels;

namespace R3.Desktop.Views;

public partial class AccountStatementView : UserControl
{
    public AccountStatementView(AccountStatementViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
