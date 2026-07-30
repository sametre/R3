using Krypton.Toolkit;
using R3.Desktop.Branding;
using R3.Desktop.Theme;

namespace R3.Desktop.Controls;

internal sealed class OutlookNavigation : KryptonPanel
{
    public event EventHandler<string>? ModuleSelected;

    public OutlookNavigation()
    {
        Dock = DockStyle.Left;
        Width = 218;
        Padding = new Padding(10, 16, 10, 10);
        StateCommon.Color1 = Color.FromArgb(24, 39, 58);

        var brand = new Panel
        {
            Dock = DockStyle.Top,
            Height = 90,
            BackColor = Color.White,
            Padding = new Padding(30, 14, 30, 14)
        };
        brand.Controls.Add(new PictureBox
        {
            Image = R3Branding.Logo,
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom
        });

        var subtitle = new Label
        {
            Text = "ÇALIŞMA ALANLARI",
            Dock = DockStyle.Top,
            Height = 38,
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0)
        };

        var items = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };

        foreach ((string icon, string title, string key) in NavigationItems)
            items.Controls.Add(CreateButton(icon, title, key));

        Controls.Add(items);
        Controls.Add(subtitle);
        Controls.Add(brand);
    }

    private Button CreateButton(string icon, string title, string key)
    {
        var button = new Button
        {
            Text = $"  {icon}    {title}",
            Tag = key,
            Width = 190,
            Height = 46,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(24, 39, 58),
            ForeColor = Color.FromArgb(226, 232, 240),
            Font = new Font("Segoe UI", 9.5F),
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand,
            Margin = new Padding(4, 2, 4, 2)
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(38, 58, 80);
        button.Click += (_, _) => ModuleSelected?.Invoke(this, key);
        return button;
    }

    private static readonly (string Icon, string Title, string Key)[] NavigationItems =
    [
        ("▦", "Data Grid", "grid"),
        ("▤", "Spreadsheet", "spreadsheet"),
        ("☷", "Vertical Grid", "vertical"),
        ("▧", "Word Processor", "word"),
        ("▣", "Scheduler", "scheduler"),
        ("⌘", "Tree List", "tree"),
        ("✎", "Data Editors", "editors")
    ];
}
