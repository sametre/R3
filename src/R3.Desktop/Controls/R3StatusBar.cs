using System.Globalization;
using R3.Application.Authentication;
using R3.Desktop.Theme;

namespace R3.Desktop.Controls;

internal sealed class R3StatusBar : Panel
{
    private readonly Label _database = CreateMetricLabel(190, ContentAlignment.MiddleLeft);
    private readonly Label _server = CreateMetricLabel(285, ContentAlignment.MiddleLeft);
    private readonly Label _windowsUser = CreateMetricLabel(145, ContentAlignment.MiddleLeft);
    private readonly Label _erpUser = CreateMetricLabel(160, ContentAlignment.MiddleLeft);
    private readonly Label _sessionTime = CreateMetricLabel(125, ContentAlignment.MiddleRight);
    private readonly Label _clock = CreateMetricLabel(145, ContentAlignment.MiddleRight);
    private readonly FlowLayoutPanel _tasks = new();
    private readonly System.Windows.Forms.Timer _clockTimer = new();
    private readonly System.Windows.Forms.Timer _connectionTimer = new();
    private readonly DateTime _sessionStartedAt = DateTime.Now;
    private Func<Task<bool>>? _connectionCheck;
    private bool _checkingConnection;

    public R3StatusBar(UserSessionData session, string serverDisplay, string databaseUser)
    {
        Dock = DockStyle.Bottom;
        Height = 28;
        BackColor = Color.FromArgb(24, 39, 58);
        Padding = new Padding(8, 0, 8, 0);

        _database.Text = "● SQL kontrol ediliyor";
        _database.ForeColor = Color.FromArgb(203, 213, 225);
        _database.Dock = DockStyle.Left;
        _server.Text = $"SQL: {serverDisplay} • {databaseUser}";
        _server.Dock = DockStyle.Left;
        _windowsUser.Text = $"Windows: {Environment.UserName}";
        _windowsUser.Dock = DockStyle.Left;
        _erpUser.Text = $"R3: {session.UserName}";
        _sessionTime.Text = "Oturum: 00:00";

        _tasks.Dock = DockStyle.Fill;
        _tasks.FlowDirection = FlowDirection.LeftToRight;
        _tasks.WrapContents = false;
        _tasks.AutoScroll = true;
        _tasks.Padding = new Padding(5, 2, 0, 0);
        _tasks.Margin = Padding.Empty;

        Controls.Add(_tasks);
        Controls.Add(_clock);
        Controls.Add(_sessionTime);
        Controls.Add(_erpUser);
        Controls.Add(_windowsUser);
        Controls.Add(_server);
        Controls.Add(_database);

        UpdateTelemetry();
        _clockTimer.Interval = 1000;
        _clockTimer.Tick += (_, _) => UpdateTelemetry();
        _clockTimer.Start();

        _connectionTimer.Interval = 15000;
        _connectionTimer.Tick += async (_, _) => await CheckConnectionAsync();
    }

    public void StartConnectionMonitor(Func<Task<bool>> connectionCheck)
    {
        _connectionCheck = connectionCheck;
        _connectionTimer.Start();
    }

    public async Task CheckConnectionAsync()
    {
        if (_connectionCheck is null || _checkingConnection)
            return;
        _checkingConnection = true;
        try { SetDatabaseState(await _connectionCheck()); }
        catch { SetDatabaseState(false); }
        finally { _checkingConnection = false; }
    }

    public void SetDatabaseState(bool connected)
    {
        _database.Text = connected ? "● SQL CANLI • EngineeringERP" : "● SQL BAĞLANTISI YOK";
        _database.ForeColor = connected ? Color.FromArgb(74, 222, 128) : Color.FromArgb(248, 113, 113);
    }

    public void AddOrActivateTask(string key, string title, Action restore)
    {
        Button? existing = _tasks.Controls.OfType<Button>().FirstOrDefault(button => Equals(button.Tag, key));
        if (existing is not null) { SetActiveTask(key); return; }

        var task = new Button
        {
            Text = title, Tag = key, Height = 23, Width = 88, FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(38, 58, 80), ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter, Padding = Padding.Empty,
            Margin = new Padding(2, 0, 2, 0), Font = new Font("Segoe UI", 7.5F)
        };
        task.FlatAppearance.BorderSize = 0;
        task.Click += (_, _) => restore();
        _tasks.Controls.Add(task);
        SetActiveTask(key);
    }

    public void SetActiveTask(string? key)
    {
        foreach (Button task in _tasks.Controls.OfType<Button>())
        {
            bool active = key is not null && Equals(task.Tag, key);
            task.BackColor = active ? R3Colors.Primary : Color.FromArgb(38, 58, 80);
        }
    }

    public void SetMinimized(string key)
    {
        Button? task = _tasks.Controls.OfType<Button>().FirstOrDefault(button => Equals(button.Tag, key));
        if (task is not null) task.BackColor = Color.FromArgb(38, 58, 80);
    }

    public void RemoveTask(string key)
    {
        Control? task = _tasks.Controls.Cast<Control>().FirstOrDefault(control => Equals(control.Tag, key));
        task?.Dispose();
    }

    private void UpdateTelemetry()
    {
        TimeSpan elapsed = DateTime.Now - _sessionStartedAt;
        _sessionTime.Text = elapsed.TotalHours >= 1
            ? $"Oturum: {(int)elapsed.TotalHours}s {elapsed.Minutes:00}dk"
            : $"Oturum: {elapsed.Minutes:00}dk {elapsed.Seconds:00}sn";
        _clock.Text = DateTime.Now.ToString("dd.MM.yyyy • HH:mm:ss", CultureInfo.CurrentCulture);
    }

    private static Label CreateMetricLabel(int width, ContentAlignment alignment) => new()
    {
        Dock = DockStyle.Right,
        Width = width,
        ForeColor = Color.FromArgb(203, 213, 225),
        TextAlign = alignment,
        Font = new Font("Segoe UI", 7.5F),
        AutoEllipsis = true
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _clockTimer.Dispose(); _connectionTimer.Dispose(); }
        base.Dispose(disposing);
    }
}
