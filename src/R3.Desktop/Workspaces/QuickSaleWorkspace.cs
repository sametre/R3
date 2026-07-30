using Krypton.Toolkit;
using R3.Desktop.Controls;
using R3.Desktop.Theme;

namespace R3.Desktop.Workspaces;

internal sealed class QuickSaleWorkspace : Panel
{
    public QuickSaleWorkspace()
    {
        Dock = DockStyle.Fill;
        BackColor = R3Colors.Canvas;
        Padding = new Padding(12);

        var header = CreateHeader();
        var payments = CreatePaymentPanel();
        var grid = new R3SmartGrid(
        [
            new("Barcode", "BARKOD", 135),
            new("ProductName", "ÜRÜN", 250, Fill: true),
            new("SizeName", "BEDEN", 75, DataGridViewContentAlignment.MiddleCenter),
            new("ColorName", "RENK", 95),
            new("Quantity", "ADET", 75, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("UnitPrice", "FİYAT", 110, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("Discount", "İNDİRİM", 105, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("Stock", "STOK", 80, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("LineTotal", "TOPLAM", 120, DataGridViewContentAlignment.MiddleRight, "N2")
        ])
        {
            Dock = DockStyle.Fill,
            ReadOnly = false
        };

        Controls.Add(grid);
        Controls.Add(payments);
        Controls.Add(header);
    }

    private static Control CreateHeader()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 94, BackColor = Color.White, Padding = new Padding(16, 10, 16, 10) };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 560, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        actions.Controls.Add(Button("ÖDEME AL", true, 105));
        actions.Controls.Add(Button("Askıya Al", false, 88));
        actions.Controls.Add(Button("Askıdan Çağır", false, 100));
        actions.Controls.Add(Button("İade / Değişim", false, 110));
        actions.Controls.Add(Button("Müşteri Seç", false, 92));

        var barcode = new KryptonTextBox
        {
            Dock = DockStyle.Bottom,
            Height = 38,
            CueHint = { CueHintText = "Barkod okutun veya ürün adı yazın..." }
        };
        barcode.StateCommon.Border.Rounding = 5;
        barcode.StateCommon.Content.Font = new Font("Segoe UI", 11);
        panel.Controls.Add(barcode);
        panel.Controls.Add(actions);
        panel.Controls.Add(new Label
        {
            Text = "Hızlı Satış",
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font("Segoe UI Semibold", 16, FontStyle.Bold),
            ForeColor = R3Colors.Text
        });
        return panel;
    }

    private static Control CreatePaymentPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 116,
            BackColor = Color.White,
            ColumnCount = 7,
            Padding = new Padding(12)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 1; i < 7; i++) panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        panel.Controls.Add(Button("Nakit", false, 112), 0, 1);
        panel.Controls.Add(Button("Kredi Kartı", false, 112), 1, 1);
        panel.Controls.Add(Button("Hediye Çeki", false, 112), 2, 1);
        panel.Controls.Add(Total("Ara Toplam", "0,00 TL"), 3, 0);
        panel.Controls.Add(Total("Kampanya", "0,00 TL"), 4, 0);
        panel.Controls.Add(Total("İndirim", "0,00 TL"), 5, 0);
        panel.Controls.Add(Total("GENEL TOPLAM", "0,00 TL", true), 6, 0);
        return panel;
    }

    private static Control Total(string label, string value, bool primary = false)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label { Text = value, Dock = DockStyle.Bottom, Height = 35, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI Semibold", primary ? 15 : 11, FontStyle.Bold), ForeColor = primary ? R3Colors.Primary : R3Colors.Text });
        panel.Controls.Add(new Label { Text = label, Dock = DockStyle.Top, Height = 25, TextAlign = ContentAlignment.MiddleRight, ForeColor = R3Colors.MutedText, Font = new Font("Segoe UI", 8) });
        return panel;
    }

    private static Button Button(string text, bool primary, int width)
    {
        var button = new Button { Text = text, Width = width, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = primary ? R3Colors.Primary : Color.White, ForeColor = primary ? Color.White : R3Colors.Text, Margin = new Padding(4), Font = new Font("Segoe UI Semibold", 8.5F) };
        button.FlatAppearance.BorderColor = primary ? R3Colors.Primary : R3Colors.Border;
        return button;
    }
}
