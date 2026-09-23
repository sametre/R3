using System.Windows;
using R3.Desktop.Logging;
using R3.Desktop.ViewModels;
using R3.Desktop.Views;
using R3.Infrastructure;

namespace R3.Desktop;

public partial class MainWindow
{
    private void OpenCustomerWorkspace() => OpenTab("Müşteri Cari", () =>
    {
        var services = CreateAccountServices();
        var model = new CustomerWorkspaceViewModel(new LocalCustomerWorkspaceService(_db!), services.CompanyId,
            DesktopLogging.CreateLogger<CustomerWorkspaceViewModel>());
        var view = new CustomerWorkspaceView(model, services, _startupSession!.UserName,
            code => _permissions == null || _permissions.HasPermission(code));
        view.SaleRequested += id => OpenNewSalesInvoice(id);
        view.CloseRequested += () => CloseCurrentTab_Click(this, new RoutedEventArgs());
        return view;
    });
}
