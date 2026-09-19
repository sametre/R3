using System.Windows;
using System.Windows.Controls;
using R3.Desktop.ViewModels;

namespace R3.Desktop.Views;

public partial class CashTransactionsView : UserControl
{
    public CashTransactionsView(CashTransactionsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private CashTransactionsViewModel ViewModel => (CashTransactionsViewModel)DataContext;

    private void Reverse_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected == null) { MessageBox.Show(Window.GetWindow(this), "Önce bir hareket seçin.", "Kasa Hareketleri"); return; }
        if (MessageBox.Show(Window.GetWindow(this), $"{ViewModel.Selected.TransactionType} hareketi iptal edilsin mi? Bu işlem ters kayıt oluşturur, fiziksel silme yapılmaz.",
            "Hareketi İptal Et", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            ViewModel.ReverseSelectedCommand.Execute(null);
    }
}
