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

    private void Grid_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source != null && source is not DataGridRow) source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        if (source is DataGridRow row) row.IsSelected = true;
    }

    private void ContextNew_Click(object sender, RoutedEventArgs e) => OpenEditor(asNew: true);
    private void ContextEdit_Click(object sender, RoutedEventArgs e) => EditButton_Click(sender, e);
    private void ContextRefresh_Click(object sender, RoutedEventArgs e) => ViewModel.RefreshCommand.Execute(null);

    private void ContextCopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedAccount != null) Clipboard.SetText(ViewModel.SelectedAccount.Code);
    }

    private void OpenEditor(bool asNew)
    {
        var editViewModel = ViewModel.CreateEditViewModel(asNew);
        var dialog = new AccountEditDialog(editViewModel, ViewModel.Database) { Owner = Window.GetWindow(this) };
        var saved = false;
        editViewModel.Saved += (_, _) => { saved = true; dialog.DialogResult = true; };
        dialog.ShowDialog();
        if (saved) ViewModel.RefreshCommand.Execute(null);
    }
}
