using System.Windows;
using System.Windows.Controls;
using R3.Desktop.ViewModels;
using R3.Desktop.ContextActions;

namespace R3.Desktop.Views;

public partial class CashAccountsView : UserControl
{
    public event Action<string?>? StatementRequested;
    public event Action<string?>? TransactionsRequested;

    public CashAccountsView(CashAccountsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ErpGridContext.Register(Grid, "cash.accounts", StandardContextActions.Cash(
            () => OpenEditor(false), () => OpenEditor(false),
            () => TransactionsRequested?.Invoke(viewModel.SelectedCashAccount?.Id),
            () => StatementRequested?.Invoke(viewModel.SelectedCashAccount?.Id),
            () => OpenCashInOut(viewModel.CreateCashInViewModel()),
            () => OpenCashInOut(viewModel.CreateCashOutViewModel()),
            TransferButton_ClickAsAction, viewModel.SetSelectedActiveAsync),
            () => { viewModel.RefreshCommand.Execute(null); return Task.CompletedTask; }, "CashAccount");
        Loaded += (_, _) => viewModel.RefreshCommand.Execute(null);
    }

    private CashAccountsViewModel ViewModel => (CashAccountsViewModel)DataContext;

    private void NewButton_Click(object sender, RoutedEventArgs e) => OpenEditor(asNew: true);

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedCashAccount == null) { MessageBox.Show(Window.GetWindow(this), "Önce bir kasa seçin.", "Kasa Kartları"); return; }
        OpenEditor(asNew: false);
    }

    private void Grid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedCashAccount != null) OpenEditor(asNew: false);
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
    private async void ContextActivate_Click(object sender, RoutedEventArgs e) => await ViewModel.SetSelectedActiveAsync(true);
    private async void ContextDeactivate_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedCashAccount is not { } account) return;
        if (MessageBox.Show(Window.GetWindow(this), $"{account.Code} — {account.Name} pasife alınsın mı?", "Kasa durumu", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await ViewModel.SetSelectedActiveAsync(false);
    }

    private void StatementButton_Click(object sender, RoutedEventArgs e) => StatementRequested?.Invoke(ViewModel.SelectedCashAccount?.Id);
    private void TransactionsButton_Click(object sender, RoutedEventArgs e) => TransactionsRequested?.Invoke(ViewModel.SelectedCashAccount?.Id);

    private void CashInButton_Click(object sender, RoutedEventArgs e) => OpenCashInOut(ViewModel.CreateCashInViewModel());
    private void CashOutButton_Click(object sender, RoutedEventArgs e) => OpenCashInOut(ViewModel.CreateCashOutViewModel());

    private void OpenCashInOut(CashInOutViewModel editViewModel)
    {
        var dialog = new CashInOutDialog(editViewModel) { Owner = Window.GetWindow(this) };
        var saved = false;
        editViewModel.Saved += (_, _) => { saved = true; dialog.DialogResult = true; };
        dialog.ShowDialog();
        if (saved) ViewModel.RefreshCommand.Execute(null);
    }

    private void TransferButton_Click(object sender, RoutedEventArgs e)
        => TransferButton_ClickAsAction();

    private void TransferButton_ClickAsAction()
    {
        var editViewModel = ViewModel.CreateTransferViewModel();
        var dialog = new CashTransferDialog(editViewModel) { Owner = Window.GetWindow(this) };
        var saved = false;
        editViewModel.Saved += (_, _) => { saved = true; dialog.DialogResult = true; };
        dialog.ShowDialog();
        if (saved) ViewModel.RefreshCommand.Execute(null);
    }

    private void OpenEditor(bool asNew)
    {
        var editViewModel = ViewModel.CreateEditViewModel(asNew);
        var dialog = new CashAccountEditDialog(editViewModel) { Owner = Window.GetWindow(this) };
        var saved = false;
        editViewModel.Saved += (_, _) => { saved = true; dialog.DialogResult = true; };
        dialog.ShowDialog();
        if (saved) ViewModel.RefreshCommand.Execute(null);
    }
}
