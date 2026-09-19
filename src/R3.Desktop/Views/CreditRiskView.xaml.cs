using System.Windows.Controls;
using R3.Desktop.ViewModels;

namespace R3.Desktop.Views;

public partial class CreditRiskView : UserControl
{
    public CreditRiskView(CreditRiskViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
