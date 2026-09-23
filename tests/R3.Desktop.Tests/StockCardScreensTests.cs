using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using R3.Desktop.ViewModels;
using R3.Desktop.Views;
using R3.Infrastructure;

namespace R3.Desktop.Tests;

/// <summary>Stok Kartları listesi ve Stok Kartı penceresi (ASB düzeni): gerçek XAML/kaynaklarla oluşturma, akışlar, ekran görüntüsü.</summary>
public sealed class StockCardScreensTests
{
    private const string Company = "00000000-0000-0000-0000-000000000001";

    [Fact]
    public void ListAndCardWorkEndToEnd() => RunSta(folder =>
    {
        var db = new StoreDatabase(System.IO.Path.Combine(folder, "test.db"));
        var unit = db.Query("SELECT id FROM units LIMIT 1").Rows[0][0].ToString()!;
        var products = new LocalProductService(db);
        foreach (var (code, name) in new[] { ("0010-100", "ADN ARMİNA 0010 100 LÜK YOLLUK"), ("0010-120", "ADN ARMİNA 0010 120 LİK YOLLUK"), ("REDMİBOOK-15E", "XİAOMİ REDMİ BOOK 15E") })
            products.Save(new ProductAggregateEdit("", Company, code, name, "", "", unit, "Stock", 10, true, [], []));

        // --- liste ---
        var view = new StockCardsView(db, Company, true, new StockCardsLinks(() => { }, () => { }, () => { }, () => { }, () => { }, () => { }, _ => { }), () => { });
        Pump(view.LoadAsync());
        Layout(view, 1500, 800);
        var grid = FindChild<DataGrid>(view)!;
        Assert.Equal(3, grid.Items.Count);
        Assert.Contains(grid.Columns, c => GridOutput.HeaderText(c.Header) == "Tedarikçi Adı");

        // Filtre satırı: "armina" (Türkçe büyük/küçük harf) iki kartı bırakır.
        var nameFilter = ((StackPanel)grid.Columns.First(c => GridOutput.HeaderText(c.Header) == "Stok Adı").Header).Children.OfType<TextBox>().Single();
        nameFilter.Text = "armina";
        Wait(TimeSpan.FromMilliseconds(600));
        Assert.Equal(2, grid.Items.Count);
        Save(view, "R3-stock-cards-list.png");
        nameFilter.Text = "";
        Wait(TimeSpan.FromMilliseconds(600));
        Assert.Equal(3, grid.Items.Count);

        // --- kart: görüntüleme → güncelleme → kaydet ---
        var ids = grid.Items.OfType<DataRowView>().Select(r => r["Id"].ToString()!).ToList();
        var vm = new StockCardViewModel(db, Company, ids[0], ids);
        Assert.False(vm.IsEditing);
        Assert.Equal("Görüntüleme  •  düzenlemek için Kayıt Güncelle (F2)", vm.StatusText);
        var window = new StockCardWindow(vm, db);
        Layout((FrameworkElement)window.Content, 1010, 650);
        Save((FrameworkElement)window.Content, "R3-stock-card-window.png");

        vm.BeginEditCommand.Execute(null);
        vm.ShortName = "YOLLUK 100"; vm.SalesDiscountRate = 5; vm.ShipHeadquarters = true; vm.IsECommerce = true; vm.Notes = "Not";
        Assert.True(vm.IsDirty);
        Assert.True(vm.SaveCard(), vm.ErrorMessage);
        Assert.False(vm.IsEditing);
        var saved = products.GetDetail(ids[0], Company)!.Product;
        Assert.Equal(("YOLLUK 100", 5m, "Headquarters", true), (saved.Details!.ShortName, saved.Details.SalesDiscountRate, saved.Policy.ShipmentLocationType, saved.Details.IsECommerce));

        // Önceki / Sonraki listeyi izler.
        Assert.True(vm.Move(next: true)); Assert.Equal("0010-120", vm.Code);
        Assert.True(vm.Move(next: true)); Assert.False(vm.Move(next: true));
        Assert.Equal("Son karttasınız.", vm.StatusText);

        // Kopyala: kod boş, yeni kayıt; kodu verip kaydet.
        vm.CopyCommand.Execute(null);
        Assert.True(vm.IsNew && vm.IsEditing);
        Assert.Equal("", vm.Code);
        vm.Code = "REDMİBOOK-15E-2";
        Assert.True(vm.SaveCard(), vm.ErrorMessage);
        Assert.NotNull(new LocalStockCardService(db).FindIdByCodeOrBarcode(Company, "REDMİBOOK-15E-2"));

        // Yeni kayıt: zorunlu alanlar.
        vm.NewCommand.Execute(null);
        Assert.False(vm.SaveCard());
        Assert.Equal("Stok kodu ve stok adı zorunludur.", vm.ErrorMessage);

        // Kayıt Sil = pasife al; aktif listeden düşer.
        Assert.True(vm.Fetch("0010-120"));
        vm.Deactivate();
        Assert.False(vm.LoadedIsActive);
        Pump(view.LoadAsync());
        Assert.Equal(3, grid.Items.Count); // 0010-100, REDMİBOOK-15E, kopya
    });

    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
    }

    private static void Save(FrameworkElement element, string file)
    {
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = System.IO.File.Create(System.IO.Path.Combine(System.IO.Path.GetTempPath(), file));
        encoder.Save(output);
    }

    private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }

    /// <summary>Runs the dispatcher until the task finishes (async loads continue on the UI thread).</summary>
    private static void Pump(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    private static void Wait(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void RunSta(Action<string> body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "R3-stock-screens-" + Guid.NewGuid());
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                // App.xaml resources (Wpf.Ui theme, R3 tokens, ProfessionalDataGridStyle) without running startup.
                if (System.Windows.Application.Current == null) new App().InitializeComponent();
                R3.Desktop.Logging.DesktopLogging.Bootstrap(System.IO.Path.Combine(folder, "logs"));
                body(folder);
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true); } catch (System.IO.IOException) { }
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
