using System.Windows;
using R3.Desktop.ViewModels;
namespace R3.Desktop.Views;
public partial class AccountBankEditDialog : Window
{
    public AccountBankEditDialog(AccountBankEditViewModel vm) { InitializeComponent(); DataContext=vm; vm.Saved += (_,_) => { DialogResult=true; Close(); }; }
}
