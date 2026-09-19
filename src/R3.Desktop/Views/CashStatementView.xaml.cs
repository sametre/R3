using System.Windows.Controls;
using R3.Desktop.ViewModels;

namespace R3.Desktop.Views;

public partial class CashStatementView : UserControl
{
    public CashStatementView(CashStatementViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
