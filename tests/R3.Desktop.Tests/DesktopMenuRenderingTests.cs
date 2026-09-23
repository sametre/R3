using System.Drawing;
using WinForms = System.Windows.Forms;

namespace R3.Desktop.Tests;

public sealed class DesktopMenuRenderingTests
{
    [Fact]
    public void EveryMenuEntryHasACompactIconAndMenuRenders()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var host = KryptonWpfBridge.ModuleMenu(DesktopMenuCatalog.Create());
                var menu = Assert.IsType<WinForms.MenuStrip>(host.Child);
                Assert.IsType<CompactMenuRenderer>(menu.Renderer);
                Assert.Equal(new Size(16, 16), menu.ImageScalingSize);
                void Check(WinForms.ToolStripItemCollection items)
                {
                    foreach (var item in items.OfType<WinForms.ToolStripMenuItem>())
                    {
                        if (item.Text == "Alt menü hazırlanıyor") continue;
                        Assert.NotNull(item.Image);
                        Assert.Equal(new Size(16, 16), item.DropDown.ImageScalingSize);
                        Check(item.DropDownItems);
                    }
                }
                Check(menu.Items);
                menu.Size = new Size(1200, 38); menu.CreateControl(); menu.PerformLayout();
                using var bitmap = new Bitmap(1200, 38);
                menu.DrawToBitmap(bitmap, new Rectangle(0, 0, 1200, 38));
                bitmap.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "R3-compact-menu.png"));
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
