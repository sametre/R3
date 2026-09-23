using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using R3.Desktop.ViewModels;
using R3.Desktop.Views;
using R3.Infrastructure;

namespace R3.Desktop.Tests;

public sealed class CustomerWorkspaceViewTests
{
    [Fact]
    public void WorkspaceRendersWithDataAndAtCompactWidth()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "R3-customer-view-" + Guid.NewGuid());
            try
            {
                var db = new StoreDatabase(System.IO.Path.Combine(folder, "test.db"));
                const string company = "00000000-0000-0000-0000-000000000001";
                var id = Guid.NewGuid().ToString();
                var accounts = new LocalAccountService(db);
                accounts.Save(new(new AccountEdit(id, company, "M0001", "Örnek Müşteri", Phone: "0212 000 00 00", CreditLimit: 25000), new(), new(), null, null));
                accounts.PostManualEntry(company, "branch", id, "Debit", 12500, "TRY", 1, DateTime.Today, "CR-001", "Açılış bakiyesi");
                accounts.PostReceipt(company, "branch", id, DateTime.Today, 2500, "TRY", 1, "Cash", "TH-001", "Müşteri tahsilatı");
                var notes = new LocalAccountNoteService(db);
                notes.Save(new("", id, "Genel", "Teslimat", "Teslimattan önce telefonla bilgi verilecek.", false, "Test"));
                var services = new AccountServices(accounts, new(db), new(db), new(db), notes, new(db), db, company, "branch");
                var model = new CustomerWorkspaceViewModel(new(db), company, NullLogger<CustomerWorkspaceViewModel>.Instance)
                {
                    Data = new LocalCustomerWorkspaceService(db).Load(company, id, "TRY", DateTime.Today)
                };
                var view = new CustomerWorkspaceView(model, services, "Test", _ => true);
                foreach (var width in new[] { 1440, 1080 })
                {
                    view.Measure(new Size(width, 760)); view.Arrange(new Rect(0, 0, width, 760)); view.UpdateLayout();
                    Assert.Equal(width, view.ActualWidth);
                    var grid = Assert.IsType<DataGrid>(view.FindName("StatementGrid"));
                    Assert.Equal(2, grid.Items.Count);
                    Assert.True(grid.ActualHeight > 150);
                    // Regression: clicking Şube (and other indexer-bound headers) must not
                    // pass "[Sube]" to BindingListCollectionView and terminate the app.
                    foreach (var column in grid.Columns)
                    {
                        Assert.DoesNotContain("[", column.SortMemberPath);
                        foreach (var direction in Enum.GetValues<System.ComponentModel.ListSortDirection>())
                        {
                            grid.Items.SortDescriptions.Clear();
                            grid.Items.SortDescriptions.Add(new(column.SortMemberPath, direction));
                            Assert.Equal(2, grid.Items.Count);
                        }
                    }
                    grid.Items.SortDescriptions.Clear();
                    view.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(width, 760, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(view);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = System.IO.File.Create(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"R3-customer-workspace-{width}.png"));
                    encoder.Save(output);
                }
                foreach (var name in new[] { "ExportButton", "RefreshButton", "OperationsButton", "NewCustomerButton", "ReceiptButton", "SaleButton", "MobileBasketButton", "CloseButton" })
                    Assert.True(Assert.IsType<Button>(view.FindName(name)).IsEnabled, $"{name} kullanıcıya açık olmalı.");
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
