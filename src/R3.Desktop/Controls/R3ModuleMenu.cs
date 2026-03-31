using R3.Desktop.Branding;
using R3.Desktop.Navigation;
using R3.Desktop.Theme;
using R3.Application.Authentication;

namespace R3.Desktop.Controls;

internal sealed class R3ModuleMenu : Panel
{
    public event EventHandler<string>? ModuleSelected;

    public R3ModuleMenu(UserSessionData session)
    {
        Dock = DockStyle.Top;
        Height = 58;
        BackColor = Color.White;
        Padding = Padding.Empty;

        var brand = new Panel
        {
            Dock = DockStyle.Left,
            Width = 112,
            BackColor = Color.White,
            Padding = new Padding(14, 8, 14, 8),
            Cursor = Cursors.Hand
        };
        var logo = new PictureBox
        {
            Image = R3Branding.Logo,
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            Cursor = Cursors.Hand
        };
        logo.Click += (_, _) => SelectModule("dashboard");
        brand.Click += (_, _) => SelectModule("dashboard");
        brand.Controls.Add(logo);

        var menu = new MenuStrip
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            ForeColor = R3Colors.Text,
            Font = new Font("Segoe UI Semibold", 9F),
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(2, 8, 2, 6),
            AutoSize = false,
            ImageScalingSize = new Size(18, 18),
            Renderer = new R3MenuRenderer()
        };

        foreach (R3NavigationItem item in R3NavigationCatalog.Items)
            menu.Items.Add(CreateMenuItem(item));

        var userMenu = new ToolStripMenuItem
        {
            Text = $"{session.DisplayName}  ▾",
            Alignment = ToolStripItemAlignment.Right,
            ForeColor = R3Colors.Text,
            Image = R3GlyphFactory.Create(R3Glyph.Account, R3Colors.Primary, 18),
            Padding = new Padding(8, 0, 8, 0)
        };
        userMenu.DropDownItems.Add("Yönetici Paneli", null, (_, _) => SelectModule("manager-dashboard"));
        userMenu.DropDownItems.Add("Oturumu Kilitle");
        userMenu.DropDownItems.Add(new ToolStripSeparator());
        userMenu.DropDownItems.Add("R3 Hakkında");
        menu.Items.Add(userMenu);

        menu.Items.Add(new ToolStripLabel
        {
            Text = $"{session.CompanyName}  •  {session.BranchName}",
            Alignment = ToolStripItemAlignment.Right,
            ForeColor = R3Colors.MutedText,
            Padding = new Padding(8, 0, 8, 0)
        });

        Controls.Add(menu);
        Controls.Add(brand);
        Paint += (_, e) =>
        {
            using var pen = new Pen(R3Colors.Border);
            e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        };
    }

    private ToolStripMenuItem CreateMenuItem(R3NavigationItem item)
    {
        var menuItem = new ToolStripMenuItem
        {
            Text = item.Title,
            Tag = item.Key,
            Image = R3GlyphFactory.Create(item.Glyph, ModuleColor(item.Key), 18),
            ImageAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(5, 0, 5, 0),
            Margin = new Padding(0),
            AutoSize = true
        };

        if (item.HasChildren)
        {
            foreach (R3NavigationItem child in item.Children!)
            {
                var childItem = new ToolStripMenuItem
                {
                    Text = child.Title,
                    Tag = child.Key,
                    Image = R3GlyphFactory.Create(child.Glyph, ModuleColor(child.Key), 16),
                    Padding = new Padding(4, 3, 16, 3)
                };
                childItem.Click += (_, _) => SelectModule(child.Key);
                menuItem.DropDownItems.Add(childItem);
            }
        }
        else
        {
            menuItem.Click += (_, _) => SelectModule(item.Key);
        }

        return menuItem;
    }

    private void SelectModule(string key) => ModuleSelected?.Invoke(this, key);

    private static Color ModuleColor(string key) => key switch
    {
        "sales" or "sales-invoices" or "quick-sale" => Color.FromArgb(14,165,233),
        "purchasing" or "purchase-invoices" => Color.FromArgb(249,115,22),
        "stock" or "products" or "variants" => Color.FromArgb(16,185,129),
        "accounts" or "customers" or "suppliers" => Color.FromArgb(139,92,246),
        "finance" or "cash" or "banks" => Color.FromArgb(234,179,8),
        "reports" => Color.FromArgb(236,72,153),
        _ => R3Colors.Primary
    };

    private sealed class R3MenuRenderer : ToolStripProfessionalRenderer
    {
        public R3MenuRenderer() : base(new R3MenuColorTable())
        {
            RoundedEdges = false;
        }
    }

    private sealed class R3MenuColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => R3Colors.RowHover;
        public override Color MenuItemBorder => R3Colors.Border;
        public override Color MenuItemSelectedGradientBegin => R3Colors.RowHover;
        public override Color MenuItemSelectedGradientEnd => R3Colors.RowHover;
        public override Color MenuItemPressedGradientBegin => R3Colors.Selection;
        public override Color MenuItemPressedGradientEnd => R3Colors.Selection;
        public override Color ToolStripDropDownBackground => Color.White;
        public override Color ImageMarginGradientBegin => Color.White;
        public override Color ImageMarginGradientMiddle => Color.White;
        public override Color ImageMarginGradientEnd => Color.White;
    }
}
