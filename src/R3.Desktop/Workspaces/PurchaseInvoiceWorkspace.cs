using Krypton.Toolkit;
using R3.Desktop.Controls;
using R3.Desktop.Theme;

namespace R3.Desktop.Workspaces;

internal sealed class PurchaseInvoiceWorkspace : Panel
{
    public PurchaseInvoiceWorkspace()
    {
        Dock = DockStyle.Fill;
        BackColor = R3Colors.Canvas;
        Padding = new Padding(12);

        var header = CreateDocumentHeader();
        var lines = CreateLinesGrid();
        var totals = CreateTotalsPanel();

        Controls.Add(lines);
        Controls.Add(totals);
        Controls.Add(header);
    }

    private static Control CreateDocumentHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 246,
            BackColor = Color.White,
            Padding = new Padding(16, 10, 16, 10)
        };
        var title = new Panel
        {
            Dock = DockStyle.Top,
            Height = 38,
            BackColor = Color.White
        };
        title.Controls.Add(CreateDocumentActions());
        title.Controls.Add(new Label
        {
            Text = "Yeni Alış Faturası",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = R3Colors.Text,
            TextAlign = ContentAlignment.MiddleLeft
        });
        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 6,
            Padding = new Padding(0, 7, 0, 0)
        };
        for (int index = 0; index < 5; index++)
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        for (int index = 0; index < 3; index++)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        }

        AddField(fields, 0, 0, "Fatura Türü", CreateCombo("Mal Alış Faturası"));
        AddField(fields, 1, 0, "Fatura Numarası", new KryptonTextBox());
        AddField(fields, 2, 0, "Belge Tarihi", new KryptonDateTimePicker());
        AddField(fields, 3, 0, "Kayıt Tarihi", new KryptonDateTimePicker());
        AddField(fields, 4, 0, "Tedarikçi", CreateCombo("Tedarikçi seçin"));

        AddField(fields, 0, 1, "Vergi Numarası", new KryptonTextBox());
        AddField(fields, 1, 1, "İrsaliye Numarası", new KryptonTextBox());
        AddField(fields, 2, 1, "İrsaliye Tarihi", new KryptonDateTimePicker());
        AddField(fields, 3, 1, "Depo", CreateCombo("Depo seçin"));
        AddField(fields, 4, 1, "Ödeme Planı", CreateCombo("Ödeme planı seçin"));

        AddField(fields, 0, 2, "Para Birimi", CreateCombo("TRY"));
        AddField(fields, 1, 2, "Döviz Kuru", new KryptonNumericUpDown { DecimalPlaces = 4, Maximum = 1000000, Value = 1 });
        AddField(fields, 2, 2, "Vade Tarihi", new KryptonDateTimePicker());
        AddField(fields, 3, 2, "e-Fatura UUID", new KryptonTextBox());
        AddField(fields, 4, 2, "Açıklama", new KryptonTextBox());

        panel.Controls.Add(fields);
        panel.Controls.Add(title);
        return panel;
    }

    private static Control CreateDocumentActions()
    {
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 510,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 2, 0, 2)
        };
        actions.Controls.Add(CreateActionButton("Kaydet ve Onayla", true, 116));
        actions.Controls.Add(CreateActionButton("Taslak Kaydet", false, 102));
        actions.Controls.Add(CreateActionButton("Mal Kabulden Getir", false, 120));
        actions.Controls.Add(CreateActionButton("e-Fatura Kontrol", false, 112));
        return actions;
    }

    private static Button CreateActionButton(string text, bool primary, int width)
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
            Margin = new Padding(5, 0, 0, 0),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = primary ? R3Colors.Primary : R3Colors.Border;
        button.FlatAppearance.MouseOverBackColor = primary ? R3Colors.PrimaryHover : R3Colors.RowHover;
        return button;
    }

    private static Control CreateLinesGrid()
    {
        var grid = new R3SmartGrid(
        [
            new("LineNumber", "SIRA", 55, DataGridViewContentAlignment.MiddleCenter),
            new("ProductCode", "STOK KODU", 120),
            new("VariantCode", "VARYANT", 155),
            new("Barcode", "BARKOD", 130),
            new("ProductName", "ÜRÜN ADI", 210, Fill: true),
            new("Quantity", "MİKTAR", 90, DataGridViewContentAlignment.MiddleRight, "N3"),
            new("UnitCode", "BİRİM", 70, DataGridViewContentAlignment.MiddleCenter),
            new("UnitPrice", "BİRİM FİYAT", 115, DataGridViewContentAlignment.MiddleRight, "N4"),
            new("DiscountRate", "İSK. 1 %", 80, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("DiscountRate2", "İSK. 2 %", 80, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("DiscountRate3", "İSK. 3 %", 80, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("VatRate", "KDV %", 75, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("TaxAmount", "KDV TUTARI", 105, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("ExciseTaxAmount", "ÖTV", 90, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("WithholdingRate", "TEVKİFAT %", 90, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("LineExpense", "SATIR MASRAFI", 115, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("LineNet", "NET TUTAR", 120, DataGridViewContentAlignment.MiddleRight, "N2"),
            new("WarehouseName", "DEPO", 120),
            new("ShelfCode", "RAF", 70),
            new("LotNumber", "PARTİ / LOT", 105),
            new("SerialNumber", "SERİ NUMARASI", 130),
            new("LineTotal", "SATIR TOPLAMI", 130, DataGridViewContentAlignment.MiddleRight, "N2")
        ])
        {
            Dock = DockStyle.Fill,
            ReadOnly = false
        };
        return grid;
    }

    private static Control CreateTotalsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 72,
            BackColor = Color.White,
            ColumnCount = 6,
            Padding = new Padding(12, 8, 12, 8)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int index = 1; index < 6; index++)
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));

        panel.Controls.Add(CreateTotal("Ara Toplam", "0,00"), 1, 0);
        panel.Controls.Add(CreateTotal("İskonto", "0,00"), 2, 0);
        panel.Controls.Add(CreateTotal("KDV", "0,00"), 3, 0);
        panel.Controls.Add(CreateTotal("Genel Toplam", "0,00", true), 4, 0);
        panel.Controls.Add(CreateTotal("Döviz", "TRY"), 5, 0);
        return panel;
    }

    private static Control CreateTotal(string title, string value, bool emphasis = false)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0) };
        panel.Controls.Add(new Label
        {
            Text = value,
            Dock = DockStyle.Bottom,
            Height = 28,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = emphasis ? R3Colors.Primary : R3Colors.Text,
            Font = new Font("Segoe UI", emphasis ? 12 : 10, FontStyle.Bold)
        });
        panel.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 22,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = R3Colors.MutedText,
            Font = new Font("Segoe UI", 8)
        });
        return panel;
    }

    private static KryptonComboBox CreateCombo(string hint)
    {
        var combo = new KryptonComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.Add(hint);
        combo.SelectedIndex = 0;
        return combo;
    }

    private static void AddField(
        TableLayoutPanel table,
        int column,
        int fieldRow,
        string label,
        Control editor)
    {
        int labelRow = fieldRow * 2;
        table.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            ForeColor = R3Colors.MutedText,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft
        }, column, labelRow);
        editor.Dock = DockStyle.Fill;
        editor.Margin = new Padding(0, 4, 10, 0);
        table.Controls.Add(editor, column, labelRow + 1);
    }
}
