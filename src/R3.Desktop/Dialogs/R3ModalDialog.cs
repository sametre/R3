using Krypton.Toolkit;
using R3.Desktop.Branding;
using R3.Desktop.Theme;

namespace R3.Desktop.Dialogs;

internal sealed class R3ModalDialog : KryptonForm
{
    public R3ModalDialog()
    {
        Text = "Yeni Cari Kart";
        Icon = R3Branding.AppIcon;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 360);

        var header = new KryptonPanel { Dock = DockStyle.Top, Height = 68 };
        header.StateCommon.Color1 = R3Theme.Canvas;
        header.Controls.Add(new KryptonLabel
        {
            Text = "Cari kart bilgileri",
            Location = new Point(22, 18),
            StateCommon = { ShortText = { Font = new Font("Segoe UI", 15, FontStyle.Bold), Color1 = R3Theme.Text } }
        });

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 18, 24, 18),
            ColumnCount = 2,
            RowCount = 4
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(form, 0, "Cari Kodu", new KryptonTextBox());
        AddField(form, 1, "Ticari Unvan", new KryptonTextBox());
        AddField(form, 2, "Cari Tipi", new KryptonComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DataSource = new[] { "Müşteri", "Tedarikçi", "Müşteri + Tedarikçi" }
        });
        AddField(form, 3, "Para Birimi", new KryptonComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DataSource = new[] { "TRY", "USD", "EUR", "GBP" }
        });

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 64,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12)
        };
        var save = R3Theme.CreatePrimaryButton("Kaydet");
        save.Click += (_, _) => DialogResult = DialogResult.OK;
        var cancel = new KryptonButton { Text = "Vazgeç", Width = 100, Height = 36, Margin = new Padding(6) };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        footer.Controls.Add(save);
        footer.Controls.Add(cancel);

        Controls.Add(form);
        Controls.Add(footer);
        Controls.Add(header);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private static void AddField(TableLayoutPanel panel, int row, string label, Control editor)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        panel.Controls.Add(new KryptonLabel { Text = label, Anchor = AnchorStyles.Left }, 0, row);
        editor.Dock = DockStyle.Fill;
        editor.Margin = new Padding(4, 8, 4, 8);
        panel.Controls.Add(editor, 1, row);
    }
}
