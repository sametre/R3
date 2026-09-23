using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace R3.Desktop;

internal sealed class CompactMenuRenderer : ToolStripProfessionalRenderer
{
    public CompactMenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Enabled || (!e.Item.Selected && !e.Item.Pressed)) return;
        var bounds = new Rectangle(2, 1, Math.Max(1, e.Item.Width - 4), Math.Max(1, e.Item.Height - 2));
        using var path = new GraphicsPath();
        const int diameter = 6;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure();
        var state = e.Graphics.Save(); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var topLevel = e.Item.Owner is MenuStrip;
        using var fill = new SolidBrush(topLevel ? Color.FromArgb(214, 224, 236) : Color.FromArgb(229, 237, 247));
        using var border = new Pen(topLevel ? Color.FromArgb(159, 173, 189) : Color.FromArgb(181, 201, 224));
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); e.Graphics.Restore(state);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var topLevel = e.Item.Owner is MenuStrip;
        e.TextColor = e.Item.Enabled ? (topLevel ? Color.Black : Color.FromArgb(20, 37, 59)) : Color.FromArgb(143, 151, 161);
        base.OnRenderItemText(e);
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(249, 250, 252);
        public override Color ImageMarginGradientBegin => Color.FromArgb(240, 242, 245);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(240, 242, 245);
        public override Color ImageMarginGradientEnd => Color.FromArgb(240, 242, 245);
        public override Color MenuBorder => Color.FromArgb(159, 173, 189);
        public override Color MenuStripGradientBegin => Color.FromArgb(240, 242, 245);
        public override Color MenuStripGradientEnd => MenuStripGradientBegin;
        public override Color SeparatorDark => Color.FromArgb(200, 208, 218);
        public override Color SeparatorLight => Color.FromArgb(249, 250, 252);
    }
}
