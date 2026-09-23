using System.Windows.Controls;
using R3.Desktop.ViewModels;

namespace R3.Desktop.Views;

public partial class AccountStatementView : UserControl
{
    public AccountStatementView(AccountStatementViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        // Keyboard-first: focus the account picker so typing the first letters of a name jumps
        // straight to it (WPF's built-in ComboBox type-ahead), no mouse needed to open the ekstre.
        Loaded += async (_, _) =>
        {
            await viewModel.InitializeAsync();
            AccountCombo.Focus();
        };
    }
}
