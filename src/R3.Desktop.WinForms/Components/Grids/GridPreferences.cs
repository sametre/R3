using R3.Desktop.WinForms.Design;
using R3.Desktop.WinForms.Infrastructure.State;

namespace R3.Desktop.WinForms.Components.Grids;

public sealed record GridColumnPreference(string Name, int Width, int DisplayIndex, bool Visible);

public sealed record GridPreference(IReadOnlyList<GridColumnPreference> Columns, GridDensity Density = GridDensity.Compact, int PageSize = 50);

/// <summary>
/// Remembers a grid's column widths, order and visibility plus density and page size per user, keyed by screen
/// ("grid.inventory.products"). Unknown/removed columns in a saved file are ignored; new columns keep defaults.
/// </summary>
public sealed class GridPreferences
{
    private readonly R3DataGrid _grid;
    private readonly string _key;
    private readonly System.Windows.Forms.Timer _saveDelay = new() { Interval = 600 };
    private GridPreference? _defaults, _pending;
    private bool _applying;

    public int PageSize { get; set; } = 50;

    public GridPreferences(R3DataGrid grid, string key)
    {
        _grid = grid; _key = "grid." + key;
        _saveDelay.Tick += (_, _) => Flush();
        grid.ColumnWidthChanged += (_, _) => QueueSave();
        grid.ColumnDisplayIndexChanged += (_, _) => QueueSave();
        // By Disposed the grid's columns are already gone: write the snapshot taken when the change happened.
        grid.Disposed += (_, _) => { Flush(); _saveDelay.Dispose(); };
    }

    /// <summary>Call after the columns are created: captures defaults, then applies the user's saved layout.</summary>
    public void Load()
    {
        _defaults = Capture();
        var saved = UserSettings.Load<GridPreference>(_key);
        if (saved != null) Apply(saved);
    }

    public void ResetToDefaults()
    {
        if (_defaults == null) return;
        Apply(_defaults);
        Save();
    }

    public void SetColumnVisible(string name, bool visible)
    {
        if (!_grid.Columns.Contains(name)) return;
        _grid.Columns[name]!.Visible = visible;
        Save();
    }

    public void SetDensity(GridDensity density) { _grid.Density = density; Save(); }

    public GridPreference Capture() => new(
        _grid.Columns.Cast<DataGridViewColumn>().Select(c => new GridColumnPreference(c.Name, c.Width, c.DisplayIndex, c.Visible)).ToList(),
        _grid.Density, PageSize);

    private void Apply(GridPreference preference)
    {
        _applying = true;
        try
        {
            _grid.Density = preference.Density;
            PageSize = preference.PageSize is 50 or 100 or 200 ? preference.PageSize : 50;
            foreach (var column in preference.Columns.Where(c => _grid.Columns.Contains(c.Name)).OrderBy(c => c.DisplayIndex))
            {
                var target = _grid.Columns[column.Name]!;
                target.Visible = column.Visible;
                if (target.AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill && column.Width > 20) target.Width = column.Width;
                if (column.DisplayIndex >= 0 && column.DisplayIndex < _grid.Columns.Count) target.DisplayIndex = column.DisplayIndex;
            }
        }
        finally { _applying = false; }
    }

    private void QueueSave()
    {
        if (_applying || _grid.Columns.Count == 0) return;
        _pending = Capture();
        _saveDelay.Stop(); _saveDelay.Start();
    }

    private void Flush()
    {
        _saveDelay.Stop();
        if (_pending is { } pending) { _pending = null; UserSettings.Save(_key, pending); }
    }

    public void Save()
    {
        if (_grid.Columns.Count == 0) return;
        _pending = null; _saveDelay.Stop();
        UserSettings.Save(_key, Capture());
    }
}
