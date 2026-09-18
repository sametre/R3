using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using R3.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new App(); app.InitializeComponent();
        var window = new MainWindow();
        window.Measure(new Size(1440, 900)); window.Arrange(new Rect(0, 0, 1440, 900)); window.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1440, 900, 96, 96, PixelFormats.Pbgra32);
        var surface = (FrameworkElement)window.Content; surface.Measure(new Size(1440,900)); surface.Arrange(new Rect(0,0,1440,900)); surface.UpdateLayout(); bitmap.Render(surface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory("artifacts");
        using (var file = File.Create("artifacts/R3-preview.png")) encoder.Save(file);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetMethod("OpenDefinitions", flags)!.Invoke(window, new object[]{true});
        typeof(MainWindow).GetMethod("OpenDefinitions", flags)!.Invoke(window, new object[]{false});
        typeof(MainWindow).GetMethod("OpenLedger", flags)!.Invoke(window, null);
        window.UpdateLayout();
        var tabs = (TabControl)window.FindName("Workspace");
        if (tabs.Items.Count != 4) throw new Exception("Expected home + three module tabs.");
        var dialog = new RecordDialog("Mağaza", null); dialog.Measure(new Size(480, 700)); dialog.Close();
        Console.WriteLine("PASS: Main window, icons, three module tabs and record dialog loaded.");
        window.Close(); app.Shutdown();
    }
}

