using System.Drawing;
using System.Windows;
using System.Windows.Forms.Integration;
using Krypton.Toolkit;
using WinForms = System.Windows.Forms;

namespace R3.Desktop;

internal static class KryptonWpfBridge
{
    public static WindowsFormsHost ModuleMenu(params DesktopMenuEntry[] modules)
    {
        var menu = new WinForms.MenuStrip
        {
            Dock = WinForms.DockStyle.Fill,
            AutoSize = false,
            CanOverflow = true,
            Font = new Font("Segoe UI", 9.5F),
            BackColor = Color.FromArgb(242, 243, 245),
            ShowItemToolTips = true,
            Padding = new WinForms.Padding(5, 6, 5, 3)
        };
        WinForms.ToolStripMenuItem Build(DesktopMenuEntry entry)
        {
            var item = new WinForms.ToolStripMenuItem(entry.Text);
            if (entry.Children.Length > 0)
                foreach (var child in entry.Children) item.DropDownItems.Add(Build(child));
            else if (entry.IsGroup)
                item.DropDownItems.Add(new WinForms.ToolStripMenuItem("Alt men? haz?rlan?yor") { Enabled = false });
            else if (entry.Click != null)
                item.Click += (_, _) => entry.Click();
            else
            {
                item.Enabled = false;
                item.ToolTipText = "Bu ekran hen?z haz?rlan?yor.";
                item.ShortcutKeyDisplayString = "Haz?rlan?yor";
            }
            return item;
        }
        foreach (var module in modules) menu.Items.Add(Build(module));
        return new WindowsFormsHost { Child = menu, Height = 46, HorizontalAlignment = HorizontalAlignment.Stretch, Background = System.Windows.Media.Brushes.White };
    }

    private static Bitmap CreateModuleIcon(string module)
    {
        var (text, color) = module switch
        {
            "Giriş" => ("⌂", Color.FromArgb(0, 120, 212)),
            "Cari" => ("C", Color.FromArgb(119, 85, 166)),
            "Stok" => ("S", Color.FromArgb(0, 153, 102)),
            "Satınalma" => ("P", Color.FromArgb(218, 119, 0)),
            "Satış" => ("₺", Color.FromArgb(193, 72, 72)),
            "Finans" => ("₺", Color.FromArgb(0, 132, 137)),
            "E-Belge" => ("E", Color.FromArgb(44, 112, 180)),
            "Muhasebe" => ("M", Color.FromArgb(91, 91, 91)),
            "Raporlar" => ("R", Color.FromArgb(79, 129, 189)),
            "Ayarlar" => ("⚙", Color.FromArgb(102, 102, 102)),
            "Araçlar" => ("A", Color.FromArgb(128, 96, 0)),
            _ => ("•", Color.FromArgb(102, 102, 102))
        };
        var bitmap = new Bitmap(18, 18);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(color)) graphics.FillEllipse(brush, 1, 1, 16, 16);
        using var font = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        var bounds = graphics.MeasureString(text, font);
        graphics.DrawString(text, font, textBrush, (18 - bounds.Width) / 2, (18 - bounds.Height) / 2 - 1);
        return bitmap;
    }

    public static WindowsFormsHost ActionBar(params (string Text, Action Click, bool Primary)[] actions)
    {
        var panel = new KryptonPanel { Dock = WinForms.DockStyle.Fill, Height = 34, Padding = new WinForms.Padding(4), BackColor = Color.FromArgb(245, 246, 247) };
        var flow = new WinForms.FlowLayoutPanel { Dock = WinForms.DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent };
        foreach (var action in actions)
        {
            var button = new KryptonButton { Text = action.Text, AutoSize = true, Height = 27, Margin = new WinForms.Padding(2, 0, 2, 0) };
            button.StateCommon.Content.ShortText.Color1 = action.Primary ? Color.White : Color.FromArgb(38, 52, 61);
            if (action.Primary) { button.StateCommon.Back.Color1 = Color.FromArgb(22, 124, 130); button.StateCommon.Back.Color2 = Color.FromArgb(22, 124, 130); }
            button.Click += (_, _) => action.Click(); flow.Controls.Add(button);
        }
        panel.Controls.Add(flow);
        return new WindowsFormsHost { Child = panel, Height = 38, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
    }
}
