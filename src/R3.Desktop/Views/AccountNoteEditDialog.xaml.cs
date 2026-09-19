using System.Windows;
using R3.Desktop.ViewModels;

namespace R3.Desktop.Views;

public partial class AccountNoteEditDialog : Window
{
    public AccountNoteEditDialog(AccountNoteEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
