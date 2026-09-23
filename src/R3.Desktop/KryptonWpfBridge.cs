using System.Drawing;
using System.Windows;
using System.Windows.Forms.Integration;
using Krypton.Toolkit;
using WinForms = System.Windows.Forms;

namespace R3.Desktop;

internal static class KryptonWpfBridge
{
    private static KryptonManager? _manager;
    private static readonly Color Navy = Color.FromArgb(16, 43, 76);
    private static readonly Color NavyDark = Color.FromArgb(8, 27, 51);
    private static readonly Color NavyAccent = Color.FromArgb(23, 59, 103);

    private static void EnsureKryptonTheme() => _manager ??= new KryptonManager
    {
        GlobalPaletteMode = PaletteMode.Microsoft365Silver,
        GlobalApplyToolstrips = true
    };

    public static WindowsFormsHost ModuleMenu(params DesktopMenuEntry[] modules)
    {
        EnsureKryptonTheme();
        var menu = new WinForms.MenuStrip
        {
            Dock = WinForms.DockStyle.Fill,
            AutoSize = false,
            CanOverflow = true,
            Font = new Font("Segoe UI", 8.5F),
            Renderer = new CompactMenuRenderer(),
            BackColor = Color.FromArgb(240, 242, 245),
            ForeColor = Color.Black,
            ShowItemToolTips = true,
            ImageScalingSize = new System.Drawing.Size(16, 16),
            Padding = new WinForms.Padding(5, 2, 5, 2)
        };
        var icons = new Dictionary<DesktopMenuIcons.Kind, Bitmap>();
        menu.Disposed += (_, _) =>
        {
            foreach (var icon in icons.Values) icon.Dispose();
            icons.Clear();
        };
        WinForms.ToolStripMenuItem Build(DesktopMenuEntry entry, bool topLevel = false)
        {
            var kind = DesktopMenuIcons.Select(entry.Text, entry.IsGroup || entry.Children.Length > 0);
            if (!icons.TryGetValue(kind, out var icon))
                icons[kind] = icon = DesktopMenuIcons.Create(kind);
            var item = new WinForms.ToolStripMenuItem(entry.Text)
            {
                Image = icon,
                ImageScaling = WinForms.ToolStripItemImageScaling.SizeToFit,
                ImageAlign = ContentAlignment.MiddleCenter,
                TextImageRelation = topLevel ? WinForms.TextImageRelation.ImageAboveText : WinForms.TextImageRelation.ImageBeforeText,
                ForeColor = topLevel ? Color.Black : Color.FromArgb(20, 37, 59),
                AutoSize = !topLevel
            };
            if (topLevel) { item.Size = new System.Drawing.Size(70, 44); item.Padding = new WinForms.Padding(4, 2, 4, 2); }
            else item.Padding = new WinForms.Padding(7, 4, 7, 4);
            item.DropDown.ImageScalingSize = new System.Drawing.Size(16, 16);
            item.DropDown.Font = menu.Font;
            item.DropDown.Renderer = menu.Renderer;
            if (entry.Children.Length > 0)
                foreach (var child in entry.Children) item.DropDownItems.Add(Build(child));
            else if (entry.Click != null)
                item.Click += (_, _) => entry.Click();
            else if (entry.IsGroup)
                item.DropDownItems.Add(new WinForms.ToolStripMenuItem("Alt menü hazırlanıyor") { Enabled = false });
            else
            {
                item.Enabled = false;
                item.ToolTipText = "Bu ekran henüz hazırlanıyor.";
                item.ShortcutKeyDisplayString = "Hazırlanıyor";
            }
            return item;
        }
        foreach (var module in modules) menu.Items.Add(Build(module, true));
        return new WindowsFormsHost { Child = menu, Height = 50, HorizontalAlignment = HorizontalAlignment.Stretch, Background = System.Windows.Media.Brushes.Transparent };
    }

    public static WindowsFormsHost ActionBar(params (string Text, Action Click, bool Primary)[] actions)
    {
        EnsureKryptonTheme();
        var panel = new KryptonPanel { Dock = WinForms.DockStyle.Fill, Height = 34, Padding = new WinForms.Padding(4), BackColor = Color.FromArgb(240, 242, 245) };
        var flow = new WinForms.FlowLayoutPanel { Dock = WinForms.DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent };
        foreach (var action in actions)
        {
            var button = new KryptonButton { Text = action.Text, AutoSize = true, Height = 27, Margin = new WinForms.Padding(2, 0, 2, 0) };
            button.StateCommon.Content.ShortText.Color1 = action.Primary ? Color.White : Color.FromArgb(20, 37, 59);
            if (action.Primary) { button.StateCommon.Back.Color1 = NavyAccent; button.StateCommon.Back.Color2 = NavyAccent; button.StateTracking.Back.Color1 = NavyDark; button.StateTracking.Back.Color2 = NavyDark; }
            button.Click += (_, _) => action.Click(); flow.Controls.Add(button);
        }
        panel.Controls.Add(flow);
        return new WindowsFormsHost { Child = panel, Height = 38, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
    }
}
