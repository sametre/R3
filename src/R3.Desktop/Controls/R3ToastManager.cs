using R3.Desktop.Theme;

namespace R3.Desktop.Controls;

internal enum ToastKind
{
    Success,
    Error,
    Warning,
    Information
}

internal sealed class R3ToastManager(Panel desktop)
{
    private readonly Panel _desktop = desktop;
    private readonly List<Panel> _toasts = [];

    public void Show(ToastKind kind, string title, string message, int durationMilliseconds = 3500)
    {
        (Color color, string symbol) = kind switch
        {
            ToastKind.Success => (Color.FromArgb(22, 163, 74), "✓"),
            ToastKind.Error => (Color.FromArgb(220, 38, 38), "×"),
            ToastKind.Warning => (Color.FromArgb(217, 119, 6), "!"),
            _ => (R3Theme.Accent, "i")
        };

        var toast = new Panel
        {
            Size = new Size(360, 88),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        var accent = new Panel { Dock = DockStyle.Left, Width = 5, BackColor = color };
        var glyph = new Label
        {
            Text = symbol,
            Dock = DockStyle.Left,
            Width = 48,
            ForeColor = color,
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };
        var close = new Button
        {
            Text = "×",
            Dock = DockStyle.Right,
            Width = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = R3Theme.MutedText
        };
        close.FlatAppearance.BorderSize = 0;
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 10, 4, 6) };
        body.Controls.Add(new Label
        {
            Text = message,
            Dock = DockStyle.Fill,
            ForeColor = R3Theme.MutedText,
            Font = new Font("Segoe UI", 9),
            AutoEllipsis = true
        });
        body.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 25,
            ForeColor = R3Theme.Text,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        });

        toast.Controls.Add(body);
        toast.Controls.Add(close);
        toast.Controls.Add(glyph);
        toast.Controls.Add(accent);
        close.Click += (_, _) => Remove(toast);

        _desktop.Controls.Add(toast);
        _toasts.Add(toast);
        Reposition();
        toast.BringToFront();

        var timer = new System.Windows.Forms.Timer { Interval = durationMilliseconds };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            Remove(toast);
        };
        timer.Start();
    }

    public void Reposition()
    {
        int y = _desktop.ClientSize.Height - 18;
        foreach (Panel toast in _toasts.AsEnumerable().Reverse())
        {
            y -= toast.Height;
            toast.Location = new Point(Math.Max(12, _desktop.ClientSize.Width - toast.Width - 18), Math.Max(12, y));
            y -= 10;
        }
    }

    private void Remove(Panel toast)
    {
        if (!_toasts.Remove(toast))
            return;
        toast.Dispose();
        Reposition();
    }
}
