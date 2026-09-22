using System.Windows;
using System.Windows.Controls;
using R3.Desktop.ViewModels;
using R3.Desktop.ContextActions;

namespace R3.Desktop.Views;

public partial class AccountsView : UserControl
{
    public event Action<AccountRowViewModel>? TransactionsRequested;
    public event Action<AccountRowViewModel>? StatementRequested;
    public AccountsView(AccountsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ErpGridContext.Register(Grid, "accounts.list", StandardContextActions.Accounts(
            () => OpenEditor(false), () => OpenEditor(false),
            () => { if (viewModel.SelectedAccount is { } row) StatementRequested?.Invoke(row); },
            () => { if (viewModel.SelectedAccount is { } row) TransactionsRequested?.Invoke(row); },
            () => OpenReceiptOrPayment(viewModel.CreateReceiptViewModel()),
            () => OpenReceiptOrPayment(viewModel.CreatePaymentViewModel()),
            viewModel.SetSelectedActiveAsync, () => OpenEditor(false)),
            () => { viewModel.RefreshCommand.Execute(null); return Task.CompletedTask; }, "Account");
        KeyboardInteractionService.AttachListShortcuts(this, SearchBox, () => OpenEditor(true), () => EditButton_Click(this, new RoutedEventArgs()), () => viewModel.RefreshCommand.Execute(null));
        // Keyboard-first: land in the search box so typing filters immediately, no click needed.
        Loaded += (_, _) => { viewModel.RefreshCommand.Execute(null); SearchBox.Focus(); };
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
    private void ContextTransactions_Click(object sender, RoutedEventArgs e) { if (ViewModel.SelectedAccount is { } account) TransactionsRequested?.Invoke(account); }
    private void ContextStatement_Click(object sender, RoutedEventArgs e) { if (ViewModel.SelectedAccount is { } account) StatementRequested?.Invoke(account); }
    private async void ContextActivate_Click(object sender, RoutedEventArgs e) => await ViewModel.SetSelectedActiveAsync(true);
    private async void ContextDeactivate_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedAccount is not { } account) return;
        if (MessageBox.Show(Window.GetWindow(this), $"{account.Code} — {account.Name} pasife alınsın mı?", "Cari durumu", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await ViewModel.SetSelectedActiveAsync(false);
    }

    private void ContextCopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedAccount != null) Clipboard.SetText(ViewModel.SelectedAccount.Code);
    }
    private void ContextCopyName_Click(object sender, RoutedEventArgs e) { if (ViewModel.SelectedAccount is { } account) Clipboard.SetText(account.Name); }
    private void ContextCopyEmail_Click(object sender, RoutedEventArgs e) { if (ViewModel.SelectedAccount is { Email.Length: > 0 } account) Clipboard.SetText(account.Email); }
    private void ContextFitColumns_Click(object sender, RoutedEventArgs e) { foreach (var column in Grid.Columns) column.Width = new DataGridLength(1, DataGridLengthUnitType.Star); }

    private void NewReceiptButton_Click(object sender, RoutedEventArgs e) => OpenReceiptOrPayment(ViewModel.CreateReceiptViewModel());
    private void NewPaymentButton_Click(object sender, RoutedEventArgs e) => OpenReceiptOrPayment(ViewModel.CreatePaymentViewModel());

    private void OpenReceiptOrPayment(ReceiptPaymentViewModel? editViewModel)
    {
        if (editViewModel == null) { MessageBox.Show(Window.GetWindow(this), "Önce bir cari seçin.", "Cari Kartları"); return; }
        var dialog = new ReceiptPaymentDialog(editViewModel) { Owner = Window.GetWindow(this) };
        var saved = false;
        editViewModel.Saved += (_, _) => { saved = true; dialog.DialogResult = true; };
        dialog.ShowDialog();
        if (saved) ViewModel.RefreshCommand.Execute(null);
    }

    private void OpenEditor(bool asNew)
    {
        var editViewModel = ViewModel.CreateEditViewModel(asNew);
        var dialog = new AccountEditDialog(editViewModel) { Owner = Window.GetWindow(this) };
        var savedAny = false;
        editViewModel.Saved += (_, _) => savedAny = true;
        editViewModel.Closed += (_, _) => dialog.Close();
        dialog.ShowDialog();
        if (savedAny) ViewModel.RefreshCommand.Execute(null);
    }
}
