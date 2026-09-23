using CefSharp;
using R3.Desktop.Design;
using CefSharp.Wpf;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace R3.Desktop.Views;

public sealed class EmbeddedBrowserView : UserControl
{
    private readonly ChromiumWebBrowser _browser;
    private readonly TextBox _address = new();
    private readonly TextBlock _status = new();
    private readonly Button _back;
    private readonly Button _forward;

    public EmbeddedBrowserView()
    {
        CefSharpBootstrapper.EnsureInitialized();

        Background = Ui.Brush("R3.Surface.Brush");
        var root = new DockPanel();
        Content = root;

        var header = new Border
        {
            Background = Ui.Brush("R3.Surface.Alt.Brush"),
            BorderBrush = Ui.Brush("R3.Border.Strong.Brush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 7, 8, 7)
        };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var toolbar = new Grid();
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition());
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Child = toolbar;

        _back = ToolButton("←", "Geri");
        _forward = ToolButton("→", "İleri");
        var refresh = ToolButton("↻", "Yenile");
        var home = ToolButton("⌂", "AR3 Web Merkezi");
        Add(toolbar, _back, 0); Add(toolbar, _forward, 1); Add(toolbar, refresh, 2); Add(toolbar, home, 3);

        _address.Height = 30;
        _address.Margin = new Thickness(7, 0, 7, 0);
        _address.Padding = new Thickness(9, 4, 9, 4);
        _address.VerticalContentAlignment = VerticalAlignment.Center;
        _address.BorderBrush = Ui.Brush("R3.Border.Strong.Brush");
        _address.Background = Ui.Brush("R3.Surface.Brush");
        _address.FontSize = 10.5;
        Grid.SetColumn(_address, 4);
        toolbar.Children.Add(_address);

        var go = ToolButton("Git", "Adresi aç", 48);
        Add(toolbar, go, 5);

        _status.Text = "Hazır";
        _status.FontSize = 10;
        _status.Foreground = Ui.Brush("R3.Text.Secondary.Brush");
        _status.Padding = new Thickness(9, 4, 9, 4);
        _status.Background = Ui.Brush("R3.Surface.Alt.Brush");
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);

        _browser = new ChromiumWebBrowser();
        root.Children.Add(_browser);

        _back.Click += (_, _) => { if (_browser.CanGoBack) _browser.Back(); };
        _forward.Click += (_, _) => { if (_browser.CanGoForward) _browser.Forward(); };
        refresh.Click += (_, _) => _browser.Reload();
        home.Click += (_, _) => ShowHome();
        go.Click += (_, _) => Navigate();
        _address.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Navigate(); e.Handled = true; } };

        _browser.AddressChanged += (_, _) => Dispatcher.BeginInvoke(() => _address.Text = _browser.Address ?? string.Empty);
        _browser.LoadingStateChanged += (_, e) => Dispatcher.BeginInvoke(() =>
        {
            _back.IsEnabled = e.CanGoBack;
            _forward.IsEnabled = e.CanGoForward;
            _status.Text = e.IsLoading ? "Sayfa yükleniyor…" : "● Hazır";
            _status.Foreground = new SolidColorBrush(e.IsLoading ? Color.FromRgb(84, 97, 108) : Color.FromRgb(37, 128, 94));
        });
        Loaded += (_, _) => ShowHome();
    }

    private void Navigate()
    {
        var target = _address.Text.Trim();
        if (string.IsNullOrWhiteSpace(target)) return;
        if (!Uri.TryCreate(target, UriKind.Absolute, out _)) target = "https://" + target;
        _browser.LoadUrl(target);
    }

    private void ShowHome()
    {
        const string html = """
        <!doctype html><html lang="tr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
        <style>
        :root{font-family:'Segoe UI',Arial,sans-serif;color:#353535;background:#F4F4F4}*{box-sizing:border-box}
        body{margin:0;min-height:100vh;display:grid;place-items:center;background:linear-gradient(135deg,#F2F2F2,#fff)}
        main{width:min(820px,90vw);background:#fff;border:1px solid #DEDEDE;border-radius:16px;padding:42px;box-shadow:0 18px 50px #35353518}
        .brand{display:flex;align-items:center;gap:16px}.logo{width:58px;height:58px;border-radius:13px;background:#373737;color:#fff;display:grid;place-items:center;font-size:24px;font-weight:700}
        h1{margin:0;font-size:25px}p{color:#767676;margin:6px 0 26px}.cards{display:grid;grid-template-columns:repeat(3,1fr);gap:12px}
        a{display:block;text-decoration:none;color:#414141;border:1px solid #E2E2E2;border-radius:11px;padding:18px;background:#FAFAFA;transition:.15s}
        a:hover{border-color:#8C8C8C;transform:translateY(-2px);box-shadow:0 8px 22px #5151511a}b{display:block;margin-bottom:5px}.hint{font-size:12px;color:#8E8E8E}
        </style></head><body><main><div class="brand"><div class="logo">AR3</div><div><h1>AR3 Web Merkezi</h1><p>Web servisleri ve resmî portallara uygulamadan erişin.</p></div></div>
        <div class="cards"><a href="https://www.gib.gov.tr"><b>Gelir İdaresi</b><span class="hint">Mevzuat ve duyurular</span></a>
        <a href="https://ebelge.gib.gov.tr"><b>E-Belge Portalı</b><span class="hint">E-Fatura ve e-Arşiv</span></a>
        <a href="https://www.turkiye.gov.tr"><b>e-Devlet</b><span class="hint">Kurumsal hizmetler</span></a></div></main></body></html>
        """;
        _browser.LoadHtml(html, "https://r3.local/");
        _address.Text = "AR3 Web Merkezi";
    }

    private static Button ToolButton(string content, string tooltip, double width = 34) => new()
    {
        Content = content, ToolTip = tooltip, Width = width, Height = 30, Margin = new Thickness(0, 0, 4, 0),
        Background = Ui.Brush("R3.Surface.Brush"), BorderBrush = Ui.Brush("R3.Border.Strong.Brush"),
        BorderThickness = new Thickness(1), Foreground = Ui.Brush("R3.Text.Primary.Brush"),
        FontSize = 13, FontWeight = FontWeights.SemiBold
    };

    private static void Add(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }
}
