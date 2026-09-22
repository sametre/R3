using System.ComponentModel;
using System.Windows;
using R3.Desktop.ViewModels;

namespace R3.Desktop.Views;

public partial class AccountEditDialog : Window
{
    private readonly AccountEditViewModel _viewModel;

    public AccountEditDialog(AccountEditViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        HostAddresses();
        viewModel.Saved += (_, _) => HostAddresses();
        viewModel.Closed += (_, _) => Close();
        // Keyboard-first: land the cursor in the first field instead of on the Kaydet button, so
        // a new-cari entry is type-code, Enter, type-name, Enter... with no mouse click needed.
        Loaded += (_, _) => CodeBox.Focus();
    }

    private void AddBank_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.BankAccounts == null) return;
        var vm = _viewModel.BankAccounts.CreateEditViewModel(true);
        var dialog = new AccountBankEditDialog(vm) { Owner = this };
        if (dialog.ShowDialog() == true) _viewModel.BankAccounts.RefreshCommand.Execute(null);
    }

    private void EditBank_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.BankAccounts?.Selected == null) return;
        var vm = _viewModel.BankAccounts.CreateEditViewModel(false);
        var dialog = new AccountBankEditDialog(vm) { Owner = this };
        if (dialog.ShowDialog() == true) _viewModel.BankAccounts.RefreshCommand.Execute(null);
    }

    /// <summary>The Adresler &amp; Yetkililer tab hosts the existing AccountAddressesContactsView
    /// (not a DataTemplate) because that control's constructor takes its view model directly - it
    /// only exists once the account has an id, so this re-runs after a new account's first save.</summary>
    private void HostAddresses()
    {
        AddressesContactsHost.Content = _viewModel.AddressesContacts == null ? null : new AccountAddressesContactsView(_viewModel.AddressesContacts);
    }

    private void AddNote_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Notes == null) return;
        var editViewModel = _viewModel.Notes.CreateEditViewModel();
        var dialog = new AccountNoteEditDialog(editViewModel) { Owner = this };
        editViewModel.Saved += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() == true) _viewModel.Notes.RefreshCommand.Execute(null);
    }

    private void ManualEntry_Click(object sender, RoutedEventArgs e)
    {
        var editViewModel = _viewModel.CreateManualEntryViewModel();
        if (editViewModel == null) return;
        var dialog = new ManualLedgerEntryDialog(editViewModel) { Owner = this };
        editViewModel.Saved += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() == true) { _viewModel.Ledger?.RefreshCommand.Execute(null); _viewModel.Documents?.Refresh(); }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.IsDirty) return;
        var result = MessageBox.Show(this, "Kaydedilmemiş değişiklikler var.", "Cari Kartı", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Cancel) { e.Cancel = true; return; }
        if (result == MessageBoxResult.Yes) _viewModel.SaveCommand.Execute(null);
    }
}
