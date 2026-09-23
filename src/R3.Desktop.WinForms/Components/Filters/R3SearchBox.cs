using Krypton.Toolkit;
using R3.Desktop.WinForms.Design;

namespace R3.Desktop.WinForms.Components.Filters;

/// <summary>
/// List search box: raises <see cref="SearchRequested"/> 300 ms after typing stops, immediately on Enter
/// (barcode scanners end with Enter), Esc clears, ↓ moves focus to the list via <see cref="MoveToResults"/>.
/// </summary>
public sealed class R3SearchBox : KryptonTextBox
{
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 300 };

    public event EventHandler? SearchRequested;
    public event EventHandler? MoveToResults;

    public R3SearchBox(string cueText)
    {
        Width = AppSizes.SearchBoxWidth; MinimumSize = new Size(0, AppSizes.InputHeight);
        StateCommon.Content.Font = AppTypography.Body;
        CueHint.CueHintText = cueText; CueHint.Color1 = AppColors.TextMuted;
        AccessibleName = cueText;
        _debounce.Tick += (_, _) => Fire();
        TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        KeyDown += (_, e) =>
        {
            switch (e.KeyCode)
            {
                case Keys.Enter: Fire(); e.SuppressKeyPress = true; break;
                case Keys.Escape when TextLength > 0: Clear(); e.SuppressKeyPress = true; break;
                case Keys.Down: MoveToResults?.Invoke(this, EventArgs.Empty); e.SuppressKeyPress = true; break;
            }
        };
    }

    public string Term => Text.Trim();

    private void Fire() { _debounce.Stop(); SearchRequested?.Invoke(this, EventArgs.Empty); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _debounce.Dispose();
        base.Dispose(disposing);
    }
}
