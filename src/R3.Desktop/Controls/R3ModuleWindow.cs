using R3.Desktop.Controls;
using R3.Desktop.Theme;

namespace R3.Desktop.Controls;

internal sealed class R3ModuleWindow : Panel
{
    private readonly Panel _desktop;
    private readonly string _moduleKey;
    private Rectangle _normalBounds;
    private bool _isMaximized;

    public event EventHandler? Minimized;
    public event EventHandler? Activated;
    public event EventHandler? Closed;

    public string ModuleKey => _moduleKey;
    public string WindowTitle { get; }

    public R3ModuleWindow(
        Panel desktop,
        string moduleKey,
        string title,
        R3Glyph glyph,
        Control content)
    {
        _desktop = desktop;
        _moduleKey = moduleKey;
        WindowTitle = title;
        BackColor = Color.White;
        BorderStyle = BorderStyle.FixedSingle;

        var titleBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 37,
            BackColor = Color.FromArgb(31, 55, 82),
            Padding = new Padding(12, 0, 4, 0)
        };
        titleBar.DoubleClick += (_, _) => ToggleMaximize();
        titleBar.MouseDown += (_, _) => ActivateWindow();

        var icon = new PictureBox
        {
            Image = R3GlyphFactory.Create(glyph, Color.White, 22),
            Dock = DockStyle.Left,
            Width = 30,
            SizeMode = PictureBoxSizeMode.CenterImage
        };
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        titleLabel.DoubleClick += (_, _) => ToggleMaximize();
        titleLabel.MouseDown += (_, _) => ActivateWindow();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 108,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        buttons.Controls.Add(CreateWindowButton("—", (_, _) => MinimizeWindow()));
        buttons.Controls.Add(CreateWindowButton("□", (_, _) => ToggleMaximize()));
        buttons.Controls.Add(CreateWindowButton("×", (_, _) => CloseWindow(), true));

        titleBar.Controls.Add(titleLabel);
        titleBar.Controls.Add(icon);
        titleBar.Controls.Add(buttons);

        content.Dock = DockStyle.Fill;
        Controls.Add(content);
        Controls.Add(titleBar);
    }

    public void OpenMaximized()
    {
        _normalBounds = new Rectangle(56, 44, Math.Max(760, _desktop.Width - 112), Math.Max(520, _desktop.Height - 88));
        Bounds = _desktop.ClientRectangle;
        Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _isMaximized = true;
        Visible = true;
        BringToFront();
        Activated?.Invoke(this, EventArgs.Empty);
    }

    public void RestoreFromTaskbar()
    {
        Visible = true;
        if (_isMaximized)
            Bounds = _desktop.ClientRectangle;
        BringToFront();
        Activated?.Invoke(this, EventArgs.Empty);
    }

    private static Button CreateWindowButton(string text, EventHandler click, bool closeButton = false)
    {
        var button = new Button
        {
            Text = text,
            Width = 35,
            Height = 35,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 12),
            Margin = Padding.Empty,
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = closeButton
            ? Color.FromArgb(196, 43, 28)
            : Color.FromArgb(55, 82, 110);
        button.Click += click;
        return button;
    }

    private void ToggleMaximize()
    {
        if (_isMaximized)
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left;
            Bounds = _normalBounds;
            _isMaximized = false;
        }
        else
        {
            _normalBounds = Bounds;
            Bounds = _desktop.ClientRectangle;
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _isMaximized = true;
        }
        BringToFront();
        Activated?.Invoke(this, EventArgs.Empty);
    }

    private void ActivateWindow()
    {
        BringToFront();
        Activated?.Invoke(this, EventArgs.Empty);
    }

    private void MinimizeWindow()
    {
        Visible = false;
        Minimized?.Invoke(this, EventArgs.Empty);
    }

    private void CloseWindow()
    {
        Closed?.Invoke(this, EventArgs.Empty);
        Dispose();
    }
}
