using Krypton.Toolkit;
using R3.Desktop.Theme;

namespace R3.Desktop.Controls;

internal sealed record R3TableColumn(
    string Name,
    string Header,
    int Width,
    DataGridViewContentAlignment Alignment = DataGridViewContentAlignment.MiddleLeft,
    string? Format = null,
    bool Fill = false);

internal sealed record R3TableCommand(string Key, string Text, bool Primary = false, int Width = 92);

internal sealed class R3SmartTable : Panel
{
    private readonly R3SmartGrid _grid;
    private readonly Label _recordCount = new();
    private readonly KryptonTextBox _search = new();

    public event EventHandler? NewRequested;
    public event EventHandler? EditRequested;
    public event EventHandler? DeleteRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler<string>? CommandRequested;
    public bool HasSelectedRow => _grid.SelectedRows.Count > 0;
    public object? SelectedDataItem => _grid.SelectedRows.Count == 0
        ? null
        : _grid.SelectedRows[0].DataBoundItem;

    public R3SmartTable(
        string title,
        string description,
        IReadOnlyList<R3TableColumn> columns,
        IReadOnlyList<R3TableCommand>? commands = null)
    {
        Dock = DockStyle.Fill;
        BackColor = R3Colors.Canvas;
        Padding = new Padding(14);

        _grid = new R3SmartGrid(columns) { Dock = DockStyle.Fill };
        IReadOnlyList<R3TableCommand> toolbarCommands = commands ??
        [
            new("new", "+ Yeni", true, 74),
            new("edit", "Düzenle", false, 74),
            new("delete", "Sil", false, 58),
            new("refresh", "Yenile", false, 68)
        ];
        ConfigureContextMenu(toolbarCommands);

        var surface = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(1)
        };
        surface.Controls.Add(_grid);

        var toolbar = CreateToolbar(toolbarCommands);
        var header = CreateHeader(title, description);
        var footer = CreateFooter();

        Controls.Add(surface);
        Controls.Add(footer);
        Controls.Add(toolbar);
        Controls.Add(header);
        UpdateRecordCount();
    }

    public void SetDataSource(object? dataSource)
    {
        _grid.DataSource = dataSource;
        UpdateRecordCount();
    }

    private static Control CreateHeader(string title, string description)
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 68,
            BackColor = Color.White,
            Padding = new Padding(18, 9, 12, 6)
        };
        header.Controls.Add(new Label
        {
            Text = description,
            Dock = DockStyle.Bottom,
            Height = 23,
            ForeColor = R3Colors.MutedText,
            Font = new Font("Segoe UI", 8.5F)
        });
        header.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 30,
            ForeColor = R3Colors.Text,
            Font = new Font("Segoe UI", 15, FontStyle.Bold)
        });
        return header;
    }

    private Control CreateToolbar(IReadOnlyList<R3TableCommand> commands)
    {
        var toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 45,
            BackColor = Color.White,
            Padding = new Padding(14, 6, 14, 6)
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        foreach (R3TableCommand command in commands)
            actions.Controls.Add(CreateSmallButton(command.Text, (_, _) => RaiseCommand(command.Key), command.Primary, command.Width));

        _search.Dock = DockStyle.Right;
        _search.Width = 260;
        _search.CueHint.CueHintText = "Tabloda ara...";
        _search.StateCommon.Border.Rounding = 5;
        _search.StateCommon.Border.Color1 = R3Colors.Border;
        _search.StateCommon.Content.Font = new Font("Segoe UI", 9);

        toolbar.Controls.Add(_search);
        toolbar.Controls.Add(actions);
        return toolbar;
    }

    private Control CreateFooter()
    {
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 29,
            BackColor = Color.White,
            Padding = new Padding(12, 0, 12, 0)
        };
        _recordCount.Dock = DockStyle.Left;
        _recordCount.Width = 160;
        _recordCount.ForeColor = R3Colors.MutedText;
        _recordCount.Font = new Font("Segoe UI", 8.5F);
        _recordCount.TextAlign = ContentAlignment.MiddleLeft;
        footer.Controls.Add(_recordCount);
        footer.Controls.Add(new Label
        {
            Text = "Sağ tık: işlem menüsü",
            Dock = DockStyle.Right,
            Width = 170,
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Segoe UI", 8),
            TextAlign = ContentAlignment.MiddleRight
        });
        return footer;
    }

    private static Button CreateSmallButton(
        string text,
        EventHandler click,
        bool primary = false,
        int width = 74)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 31,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? R3Colors.Primary : Color.White,
            ForeColor = primary ? Color.White : R3Colors.Text,
            Font = new Font("Segoe UI", 8.5F),
            Margin = new Padding(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = primary ? R3Colors.Primary : R3Colors.Border;
        button.FlatAppearance.MouseOverBackColor = primary ? R3Colors.PrimaryHover : R3Colors.RowHover;
        button.Click += click;
        return button;
    }

    private void ConfigureContextMenu(IReadOnlyList<R3TableCommand> commands)
    {
        var menu = new ContextMenuStrip
        {
            Font = new Font("Segoe UI", 9),
            ShowImageMargin = false
        };
        foreach (R3TableCommand command in commands)
            menu.Items.Add(command.Text, null, (_, _) => RaiseCommand(command.Key));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Kolonları varsayılana döndür");
        menu.Items.Add("Excel'e aktar");
        _grid.ContextMenuStrip = menu;
    }

    private void RaiseCommand(string key)
    {
        switch (key)
        {
            case "new": NewRequested?.Invoke(this, EventArgs.Empty); break;
            case "edit": EditRequested?.Invoke(this, EventArgs.Empty); break;
            case "delete": DeleteRequested?.Invoke(this, EventArgs.Empty); break;
            case "refresh": RefreshRequested?.Invoke(this, EventArgs.Empty); break;
            default: CommandRequested?.Invoke(this, key); break;
        }
    }

    private void UpdateRecordCount() =>
        _recordCount.Text = $"{_grid.Rows.Count:N0} kayıt";
}

internal sealed class R3SmartGrid : DataGridView
{
    public R3SmartGrid(IReadOnlyList<R3TableColumn> columns)
    {
        AutoGenerateColumns = false;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToOrderColumns = false;
        AllowUserToResizeRows = false;
        ReadOnly = true;
        MultiSelect = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        RowHeadersVisible = false;
        BorderStyle = BorderStyle.None;
        BackgroundColor = Color.White;
        GridColor = R3Colors.Border;
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        EnableHeadersVisualStyles = false;
        ColumnHeadersHeight = 36;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        RowTemplate.Height = 34;
        DefaultCellStyle.Font = new Font("Segoe UI", 9);
        DefaultCellStyle.ForeColor = R3Colors.Text;
        DefaultCellStyle.BackColor = Color.White;
        DefaultCellStyle.SelectionBackColor = R3Colors.Selection;
        DefaultCellStyle.SelectionForeColor = R3Colors.SelectionText;
        DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
        AlternatingRowsDefaultCellStyle.BackColor = R3Colors.RowAlternate;
        ColumnHeadersDefaultCellStyle.BackColor = R3Colors.Header;
        ColumnHeadersDefaultCellStyle.ForeColor = R3Colors.HeaderText;
        ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        ColumnHeadersDefaultCellStyle.Padding = new Padding(5, 0, 5, 0);

        foreach (R3TableColumn definition in columns)
        {
            var column = new DataGridViewTextBoxColumn
            {
                Name = definition.Name,
                DataPropertyName = definition.Name,
                HeaderText = definition.Header,
                Width = definition.Width,
                MinimumWidth = Math.Min(70, definition.Width),
                Resizable = DataGridViewTriState.False,
                SortMode = DataGridViewColumnSortMode.Automatic,
                AutoSizeMode = definition.Fill
                    ? DataGridViewAutoSizeColumnMode.Fill
                    : DataGridViewAutoSizeColumnMode.None
            };
            column.DefaultCellStyle.Alignment = definition.Alignment;
            if (definition.Format is not null)
                column.DefaultCellStyle.Format = definition.Format;
            Columns.Add(column);
        }

        CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                ClearSelection();
                Rows[e.RowIndex].Selected = true;
                CurrentCell = Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
            }
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Rows.Count != 0)
            return;

        using var titleFont = new Font("Segoe UI", 11, FontStyle.Bold);
        using var detailFont = new Font("Segoe UI", 9);
        TextRenderer.DrawText(
            e.Graphics,
            "Henüz kayıt bulunmuyor",
            titleFont,
            new Rectangle(0, ColumnHeadersHeight + 70, Width, 30),
            R3Colors.MutedText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(
            e.Graphics,
            "Yeni kayıt oluşturabilir veya listeyi yenileyebilirsiniz.",
            detailFont,
            new Rectangle(0, ColumnHeadersHeight + 100, Width, 26),
            Color.FromArgb(148, 163, 184),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
