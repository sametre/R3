using System.ComponentModel;
using System.Data;
using Krypton.Toolkit;
using R3.Desktop.WinForms.Design;

namespace R3.Desktop.WinForms.Components.Grids;

/// <summary>
/// The standard AR3 list grid (Krypton-rendered; screens never use a raw DataGridView):
/// token colors/fonts, density, tr-TR number/money/date columns (right-aligned numerics), server-side sort
/// requests, frozen key columns, multi-select, painted loading/empty/error states, saved column preferences,
/// Enter/double-click → <see cref="RowActivated"/>, right-click selects the row before the context menu opens.
/// Rows are expected one page at a time (the service pages); it never binds an unbounded table.
/// </summary>
public class R3DataGrid : KryptonDataGridView
{
    private string _emptyText = "Kayıt yok.";
    private string? _loadingText, _errorText;
    private GridDensity _density = GridDensity.Compact;
    private string? _sortKey;
    private bool _sortDescending;

    /// <summary>Enter on a row or double-click on a cell.</summary>
    public event EventHandler<DataGridViewRow>? RowActivated;

    /// <summary>A sortable header was clicked: (sort key, descending). The view re-queries the service.</summary>
    public event EventHandler<(string Key, bool Descending)>? SortRequested;

    public R3DataGrid()
    {
        DoubleBuffered = true;
        AutoGenerateColumns = false;
        AllowUserToAddRows = false; AllowUserToDeleteRows = false; AllowUserToResizeRows = false;
        AllowUserToOrderColumns = true; AllowUserToResizeColumns = true;
        ReadOnly = true; MultiSelect = true; SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        RowHeadersVisible = false; StandardTab = true; ShowCellToolTips = true;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        GridStyles.Style = DataGridViewStyle.List;
        HideOuterBorders = false;

        StateCommon.Background.Color1 = AppColors.Surface;
        StateCommon.HeaderColumn.Back.Color1 = AppColors.GridHeader; StateCommon.HeaderColumn.Back.Color2 = AppColors.GridHeader;
        StateCommon.HeaderColumn.Content.Font = AppTypography.BodyStrong;
        StateCommon.HeaderColumn.Content.Color1 = AppColors.GridHeaderText;
        StateCommon.HeaderColumn.Content.Padding = new Padding(AppSpacing.XS, 0, AppSpacing.XS, 0);
        StateCommon.HeaderColumn.Border.Color1 = AppColors.GridLine;
        StateCommon.DataCell.Content.Font = AppTypography.Body;
        StateCommon.DataCell.Content.Color1 = AppColors.GridCellText;
        StateCommon.DataCell.Content.Padding = new Padding(AppSpacing.XS, 0, AppSpacing.XS, 0);
        StateCommon.DataCell.Border.Color1 = AppColors.GridLine;
        StateCommon.DataCell.Border.DrawBorders = PaletteDrawBorders.BottomRight;
        StateSelected.DataCell.Back.Color1 = AppColors.SurfaceSelected; StateSelected.DataCell.Back.Color2 = AppColors.SurfaceSelected;
        StateSelected.DataCell.Content.Color1 = AppColors.TextPrimary;
        AlternatingRowsDefaultCellStyle.BackColor = AppColors.GridRowAlt;
        DefaultCellStyle.FormatProvider = AppFormats.Culture;
        ApplyDensity();
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public GridDensity Density
    {
        get => _density;
        set { _density = value; ApplyDensity(); }
    }

    private void ApplyDensity()
    {
        RowTemplate.Height = AppSizes.GridRowHeight(_density);
        ColumnHeadersHeight = AppSizes.GridHeaderHeight(_density);
        foreach (DataGridViewRow row in Rows) row.Height = RowTemplate.Height;
    }

    // ---- columns --------------------------------------------------------------------------------------------

    /// <param name="sortKey">Service sort column (e.g. "code"); null = not sortable.</param>
    public DataGridViewColumn AddText(string property, string header, int width, string? sortKey = null, bool fill = false) =>
        Add(new KryptonDataGridViewTextBoxColumn(), property, header, width, sortKey, fill);

    /// <summary>Right-aligned tr-TR number (N2 quantities, N0 counts).</summary>
    public DataGridViewColumn AddNumber(string property, string header, int width, string format = AppFormats.Quantity, string? sortKey = null) =>
        Numeric(Add(new KryptonDataGridViewTextBoxColumn(), property, header, width, sortKey, false), format);

    /// <summary>Right-aligned ₺1.500,00.</summary>
    public DataGridViewColumn AddMoney(string property, string header, int width, string? sortKey = null) =>
        Numeric(Add(new KryptonDataGridViewTextBoxColumn(), property, header, width, sortKey, false), AppFormats.Money);

    public DataGridViewColumn AddDate(string property, string header, int width = 90, string format = AppFormats.Date, string? sortKey = null)
    {
        var column = Add(new KryptonDataGridViewTextBoxColumn(), property, header, width, sortKey, false);
        column.DefaultCellStyle.Format = format; column.DefaultCellStyle.FormatProvider = AppFormats.Culture;
        return column;
    }

    private static DataGridViewColumn Numeric(DataGridViewColumn column, string format)
    {
        column.DefaultCellStyle.Format = format; column.DefaultCellStyle.FormatProvider = AppFormats.Culture;
        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        return column;
    }

    private T Add<T>(T column, string property, string header, int width, string? sortKey, bool fill) where T : DataGridViewColumn
    {
        column.DataPropertyName = property; column.Name = property; column.HeaderText = header; column.Tag = sortKey;
        column.SortMode = sortKey == null ? DataGridViewColumnSortMode.NotSortable : DataGridViewColumnSortMode.Programmatic;
        if (column.DefaultCellStyle.Alignment == DataGridViewContentAlignment.NotSet) column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        if (fill) { column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; column.MinimumWidth = width; column.FillWeight = 100; }
        else column.Width = width;
        Columns.Add(column);
        return column;
    }

    /// <summary>Keeps the first <paramref name="count"/> displayed columns in view while scrolling sideways.</summary>
    public void FreezeLeading(int count)
    {
        foreach (DataGridViewColumn column in Columns) column.Frozen = false;
        var ordered = Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).Take(count).ToList();
        if (ordered.Count > 0) ordered[^1].Frozen = true; // freezing a column freezes everything left of it
    }

    // ---- sorting (server-side) ------------------------------------------------------------------------------

    public void SetSortIndicator(string? key, bool descending)
    {
        _sortKey = key; _sortDescending = descending;
        foreach (DataGridViewColumn column in Columns)
            column.HeaderCell.SortGlyphDirection = column.Tag is string k && k == key ? (descending ? SortOrder.Descending : SortOrder.Ascending) : SortOrder.None;
    }

    protected override void OnColumnHeaderMouseClick(DataGridViewCellMouseEventArgs e)
    {
        base.OnColumnHeaderMouseClick(e);
        if (e.Button != MouseButtons.Left || Columns[e.ColumnIndex].Tag is not string key) return;
        var descending = key == _sortKey && !_sortDescending;
        SetSortIndicator(key, descending);
        SortRequested?.Invoke(this, (key, descending));
    }

    // ---- selection helpers ----------------------------------------------------------------------------------

    /// <summary>Selected rows as DataRows, in display order.</summary>
    public IReadOnlyList<DataRow> SelectedDataRows() =>
        SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index).Select(r => (r.DataBoundItem as DataRowView)?.Row).OfType<DataRow>().ToList();

    public DataRow? CurrentDataRow => (CurrentRow?.DataBoundItem as DataRowView)?.Row;

    protected override void OnCellMouseDown(DataGridViewCellMouseEventArgs e)
    {
        // Right-click acts on the row under the cursor (keeps an existing multi-selection if the row is part of it).
        if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && !Rows[e.RowIndex].Selected)
        {
            ClearSelection();
            CurrentCell = Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
            Rows[e.RowIndex].Selected = true;
        }
        base.OnCellMouseDown(e);
    }

    // ---- states ---------------------------------------------------------------------------------------------

    /// <summary>Why the list is empty and what to do next.</summary>
    public void SetEmptyText(string text) { _emptyText = text; Invalidate(); }

    public void SetLoading(string? text) { _loadingText = text; if (text != null) _errorText = null; UseWaitCursor = text != null; Invalidate(); }

    public void SetError(string? text) { _errorText = text; Invalidate(); }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsLoading => _loadingText != null;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var top = ColumnHeadersVisible ? ColumnHeadersHeight : 0;
        var body = new Rectangle(0, top, ClientSize.Width, Math.Max(0, ClientSize.Height - top));
        if (_loadingText != null)
        {
            var band = new Rectangle(body.X, body.Y, body.Width, 34);
            using var fill = new SolidBrush(Color.FromArgb(235, AppColors.SurfaceAlt));
            e.Graphics.FillRectangle(fill, band);
            using var line = new Pen(AppColors.Accent, 2);
            e.Graphics.DrawLine(line, band.Left, band.Bottom - 1, band.Right, band.Bottom - 1);
            TextRenderer.DrawText(e.Graphics, _loadingText, AppTypography.Caption, band, AppColors.TextSecondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        var message = _errorText ?? (Rows.Count == 0 && _loadingText == null ? _emptyText : null);
        if (string.IsNullOrEmpty(message)) return;
        var area = new Rectangle(body.X + AppSpacing.XL, body.Y + AppSpacing.XL + (_loadingText != null ? 34 : 0), Math.Max(0, body.Width - 2 * AppSpacing.XL), 64);
        if (_errorText != null)
            TextRenderer.DrawText(e.Graphics, AppIcons.Warning + "  ", AppTypography.Icon, new Rectangle(area.X, area.Y, area.Width, 20), AppColors.Danger, TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(e.Graphics, message, AppTypography.Body, new Rectangle(area.X, area.Y + (_errorText != null ? 22 : 0), area.Width, area.Height),
            _errorText != null ? AppColors.Danger : AppColors.TextSecondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
    }

    // ---- keyboard -------------------------------------------------------------------------------------------

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Enter && CurrentRow != null) { RowActivated?.Invoke(this, CurrentRow); return true; }
        return base.ProcessDialogKey(keyData);
    }

    protected override bool ProcessDataGridViewKey(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None && CurrentRow != null) { RowActivated?.Invoke(this, CurrentRow); return true; }
        return base.ProcessDataGridViewKey(e);
    }

    protected override void OnCellDoubleClick(DataGridViewCellEventArgs e)
    {
        base.OnCellDoubleClick(e);
        if (e.RowIndex >= 0) RowActivated?.Invoke(this, Rows[e.RowIndex]);
    }
}
