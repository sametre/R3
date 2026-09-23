using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace R3.Desktop;

/// <summary>Ortak, görünür yükleme katmanı. Sorgu arka planda çalışırken ekran kabuğu
/// kullanılabilir kalır ve eski bir arama sonucu yeni sonucu ezemez.</summary>
internal sealed class LoadingOverlay : Border
{
    private readonly TextBlock _message = new();

    public LoadingOverlay()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch; VerticalAlignment = VerticalAlignment.Stretch;
        Background = new SolidColorBrush(Color.FromArgb(205, 242, 243, 244));
        BorderBrush = new SolidColorBrush(Color.FromRgb(210, 215, 219)); BorderThickness = new Thickness(1);
        Visibility = Visibility.Collapsed; IsHitTestVisible = true; Panel.SetZIndex(this, 20);
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new ProgressBar { Width = 220, Height = 5, IsIndeterminate = true, Foreground = new SolidColorBrush(Color.FromRgb(42, 126, 132)) });
        _message.Text = "Yükleniyor…"; _message.Foreground = new SolidColorBrush(Color.FromRgb(57, 67, 74)); _message.FontSize = 12; _message.HorizontalAlignment = HorizontalAlignment.Center; _message.Margin = new Thickness(0, 9, 0, 0); panel.Children.Add(_message);
        Child = panel;
    }

    public void ShowLoading(string message = "Yükleniyor…") { _message.Text = message; Visibility = Visibility.Visible; }
    public void HideLoading() => Visibility = Visibility.Collapsed;
}

internal static class AsyncLoading
{
    public static async Task<DataTable?> LoadTableAsync(LoadingOverlay overlay, Func<DataTable> load, Action<DataTable> apply, Func<Exception, Task>? failed = null, string message = "Yükleniyor…")
    {
        overlay.ShowLoading(message);
        try { var table = await Task.Run(load); apply(table); return table; }
        catch (Exception ex) { if (failed != null) await failed(ex); else MessageBox.Show(ex.Message, "Veri yüklenemedi", MessageBoxButton.OK, MessageBoxImage.Warning); return null; }
        finally { overlay.HideLoading(); }
    }
}
