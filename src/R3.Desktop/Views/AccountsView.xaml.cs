using System.Windows;
using System.Windows.Controls;
using R3.Desktop.ViewModels;

namespace R3.Desktop.Views;

public partial class AccountsView : UserControl
{
    public AccountsView(AccountsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.RefreshCommand.Execute(null);
    }

    private AccountsViewModel ViewModel => (AccountsViewModel)DataContext;

    private void NewButton_Click(object sender, RoutedEventArgs e) => OpenEditor(asNew: true);

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedAccount == null)
        {
            MessageBox.Show(Window.GetWindow(this), "Önce bir cari seçin.", "Cari Kartları");
            return;
        }
        OpenEditor(asNew: false);
    }

    private void Grid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedAccount != null) OpenEditor(asNew: false);
    }

    private void OpenEditor(bool asNew)
    {
        var editViewModel = ViewModel.CreateEditViewModel(asNew);
        var dialog = new AccountEditDialog(editViewModel) { Owner = Window.GetWindow(this) };
        var saved = false;
        editViewModel.Saved += (_, _) => { saved = true; dialog.DialogResult = true; };
        dialog.ShowDialog();
        if (saved) ViewModel.RefreshCommand.Execute(null);
    }
}
