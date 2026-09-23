using R3.Desktop.WinForms.Design;

namespace R3.Desktop.WinForms.Shell.Workspace;

/// <summary>
/// Tabbed work area: one tab per screen key (opening an open screen activates it), flat owner-drawn tabs with a
/// close glyph and an accent underline on the active tab. Ctrl+W / middle-click closes; Ctrl+Tab cycles (built in).
/// Views can veto closing (unsaved changes) through <see cref="ICloseGuard"/>.
/// </summary>
public sealed class WorkspaceHost : TabControl
{
    private const int CloseSize = 16;

    public WorkspaceHost()
    {
        Dock = DockStyle.Fill; DrawMode = TabDrawMode.OwnerDrawFixed; SizeMode = TabSizeMode.Normal;
        Padding = new Point(AppSpacing.LG + CloseSize / 2, AppSpacing.XS + 1); Font = AppTypography.Body;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
    }

    public void Open(string key, string title, Func<Control> create)
    {
        foreach (TabPage page in TabPages)
            if (Equals(page.Tag, key)) { SelectedTab = page; FocusContent(page); return; }
        SuspendLayout();
        try
        {
            var content = create(); content.Dock = DockStyle.Fill;
            var tab = new TabPage(title) { Tag = key, BackColor = AppColors.Background, Padding = new Padding(AppSpacing.LG, AppSpacing.MD, AppSpacing.LG, AppSpacing.MD), UseVisualStyleBackColor = false };
            tab.Controls.Add(content);
            TabPages.Add(tab); SelectedTab = tab;
        }
        finally { ResumeLayout(); }
        if (SelectedTab != null) FocusContent(SelectedTab);
    }

    private static void FocusContent(TabPage page) { if (page.Controls.Count > 0) page.Controls[0].Select(); }

    public TabPage? TabOf(Control view)
    {
        for (Control? c = view; c != null; c = c.Parent) if (c is TabPage page && page.Parent == this) return page;
        return null;
    }

    /// <returns>false when the view vetoed closing.</returns>
    public bool Close(TabPage tab)
    {
        if (tab.Controls.Count > 0 && tab.Controls[0] is ICloseGuard guard && !guard.CanClose()) return false;
        var index = TabPages.IndexOf(tab);
        TabPages.Remove(tab); tab.Dispose();
        if (TabCount > 0) SelectedIndex = Math.Min(Math.Max(0, index), TabCount - 1);
        return true;
    }

    public void CloseSelected() { if (SelectedTab != null) Close(SelectedTab); }

    /// <summary>Asks every open view; false if any vetoes (app exit).</summary>
    public bool CanCloseAll() => TabPages.Cast<TabPage>().All(t => t.Controls.Count == 0 || t.Controls[0] is not ICloseGuard g || g.CanClose());

    private Rectangle CloseBox(int index)
    {
        var r = GetTabRect(index);
        return new Rectangle(r.Right - CloseSize - AppSpacing.XS, r.Top + (r.Height - CloseSize) / 2, CloseSize, CloseSize);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= TabCount) return;
        var selected = e.Index == SelectedIndex; var r = GetTabRect(e.Index);
        using (var back = new SolidBrush(selected ? AppColors.Surface : AppColors.SurfaceAlt)) e.Graphics.FillRectangle(back, r);
        if (selected) { using var line = new Pen(AppColors.Accent, 2); e.Graphics.DrawLine(line, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1); }
        var textArea = new Rectangle(r.Left + AppSpacing.SM, r.Top, r.Width - CloseSize - AppSpacing.SM - AppSpacing.XS, r.Height);
        TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, selected ? AppTypography.BodyStrong : AppTypography.Body, textArea,
            selected ? AppColors.TextPrimary : AppColors.TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, AppIcons.Close, AppTypography.IconSmall, CloseBox(e.Index), AppColors.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        for (var i = 0; i < TabCount; i++)
        {
            var hit = e.Button == MouseButtons.Middle ? GetTabRect(i).Contains(e.Location) : e.Button == MouseButtons.Left && CloseBox(i).Contains(e.Location);
            if (hit) { Close(TabPages[i]); return; }
        }
    }
}

/// <summary>Implemented by views with unsaved state; return false to keep the tab open.</summary>
public interface ICloseGuard
{
    bool CanClose();
}
