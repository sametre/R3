using Krypton.Toolkit;
using R3.Desktop.WinForms.Components.Toolbars;
using R3.Desktop.WinForms.Design;

namespace R3.Desktop.WinForms.Components.Grids;

/// <summary>Kolonlar: show/hide grid columns, pick density, or reset to the default layout.</summary>
public sealed class ColumnChooserDialog : Form
{
    public ColumnChooserDialog(R3DataGrid grid, GridPreferences preferences)
    {
        Text = "Kolonlar"; StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false; ClientSize = new Size(320, 460); KeyPreview = true;

        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle, Font = AppTypography.Body, IntegralHeight = false };
        var columns = grid.Columns.Cast<DataGridViewColumn>().OrderBy(c => c.DisplayIndex).ToList();
        foreach (var column in columns) list.Items.Add(new ColumnItem(column), column.Visible);
        list.ItemCheck += (_, e) =>
        {
            // At least one column must stay visible.
            if (e.NewValue == CheckState.Unchecked && list.CheckedItems.Count <= 1) e.NewValue = CheckState.Checked;
        };

        var density = new KryptonComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        density.Items.AddRange(["Sıkışık", "Normal", "Rahat"]);
        density.SelectedIndex = (int)grid.Density;
        density.AccessibleName = "Satır yoğunluğu";

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(0, AppSpacing.SM, 0, 0) };
        var ok = new R3PrimaryButton("Tamam") { DialogResult = DialogResult.OK };
        var reset = new R3SecondaryButton("Varsayılana dön");
        var cancel = new R3SecondaryButton("Vazgeç") { DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([ok, cancel, reset]);
        AcceptButton = ok; CancelButton = cancel;
        reset.Click += (_, _) => { preferences.ResetToDefaults(); DialogResult = DialogResult.Abort; Close(); };

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(AppSpacing.MD), ColumnCount = 1, RowCount = 4 };
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize)); body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.Controls.Add(new Label { Text = "Görünecek kolonlar", AutoSize = true, Font = AppTypography.BodyStrong, Margin = new Padding(0, 0, 0, AppSpacing.XS) });
        body.Controls.Add(list);
        var densityRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, AppSpacing.SM, 0, 0) };
        densityRow.Controls.Add(new Label { Text = "Satır yoğunluğu:", AutoSize = true, Margin = new Padding(0, AppSpacing.XS + 2, AppSpacing.XS, 0) });
        densityRow.Controls.Add(density);
        body.Controls.Add(densityRow);
        body.Controls.Add(buttons);
        Controls.Add(body);

        FormClosing += (_, _) =>
        {
            if (DialogResult != DialogResult.OK) return;
            for (var i = 0; i < list.Items.Count; i++) ((ColumnItem)list.Items[i]).Column.Visible = list.GetItemChecked(i);
            grid.Density = (GridDensity)density.SelectedIndex;
            preferences.Save();
        };
    }

    private sealed record ColumnItem(DataGridViewColumn Column)
    {
        public override string ToString() => Column.HeaderText;
    }
}
