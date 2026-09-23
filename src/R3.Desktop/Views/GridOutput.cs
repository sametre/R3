using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using ClosedXML.Excel;
using R3.Desktop.Design;

namespace R3.Desktop.Views;

/// <summary>
/// Excel / Print for list grids (ASB "Excel" and "Print" buttons): the visible columns in display order with their
/// header text, over the rows the grid currently shows (after the column filter row). Numbers stay numeric in Excel.
/// </summary>
internal static class GridOutput
{
    internal sealed record OutputColumn(string Path, string Header, string? Format, bool RightAligned);

    /// <summary>Visible bound text columns in display order. Call on the UI thread.</summary>
    public static IReadOnlyList<OutputColumn> Columns(DataGrid grid) => grid.Columns
        .Where(c => c.Visibility == Visibility.Visible && c is DataGridTextColumn { Binding: Binding { Path.Path.Length: > 0 } })
        .OrderBy(c => c.DisplayIndex)
        .Select(c =>
        {
            var binding = (Binding)((DataGridTextColumn)c).Binding;
            return new OutputColumn(binding.Path.Path, HeaderText(c.Header), binding.StringFormat, binding.StringFormat is { Length: > 0 });
        })
        .ToList();

    /// <summary>Header may be a string or a panel whose first TextBlock is the caption (filter-row headers).</summary>
    public static string HeaderText(object? header) => header switch
    {
        string text => text,
        Panel panel => panel.Children.OfType<TextBlock>().FirstOrDefault()?.Text?.Replace("\n", " ") ?? "",
        _ => header?.ToString() ?? ""
    };

    public static void ToExcel(IReadOnlyList<OutputColumn> columns, IReadOnlyList<DataRow> rows, string path, string sheetName)
    {
        using var book = new XLWorkbook();
        var sheet = book.Worksheets.Add(sheetName.Length > 31 ? sheetName[..31] : sheetName);
        for (var c = 0; c < columns.Count; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = columns[c].Header; cell.Style.Font.Bold = true; cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0xEE, 0xEF, 0xF1);
        }
        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < columns.Count; c++)
            {
                if (!rows[r].Table.Columns.Contains(columns[c].Path)) continue;
                var value = rows[r][columns[c].Path]; var cell = sheet.Cell(r + 2, c + 1);
                switch (value)
                {
                    case DBNull or null: break;
                    case decimal or double or float or long or int or short:
                        cell.Value = Convert.ToDouble(value); cell.Style.NumberFormat.Format = columns[c].Format == "N0" ? "#,##0" : "#,##0.00"; break;
                    default: cell.Value = value.ToString(); break;
                }
            }
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 500));
        book.SaveAs(path);
    }

    /// <summary>Landscape table document (title, subtitle, repeated header) through the standard print dialog.</summary>
    public static void Print(IReadOnlyList<OutputColumn> columns, IReadOnlyList<DataRow> rows, string title, string subtitle)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        dialog.PrintTicket.PageOrientation = System.Printing.PageOrientation.Landscape;
        var document = new FlowDocument
        {
            PageWidth = Math.Max(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight), PageHeight = Math.Min(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight),
            PagePadding = new Thickness(36), ColumnWidth = double.PositiveInfinity, FontFamily = new FontFamily("Segoe UI"), FontSize = 8
        };
        document.Blocks.Add(new Paragraph(new Bold(new Run(title))) { FontSize = 13, Margin = new Thickness(0, 0, 0, 2) });
        document.Blocks.Add(new Paragraph(new Run(subtitle)) { Foreground = Ui.TextSecondary, Margin = new Thickness(0, 0, 0, 8) });
        var table = new Table { CellSpacing = 0, BorderBrush = Ui.Border, BorderThickness = new Thickness(0.5) };
        foreach (var _ in columns) table.Columns.Add(new TableColumn());
        var header = new TableRowGroup(); var headerRow = new TableRow { Background = Ui.Brush("R3.Surface.Alt.Brush") };
        foreach (var column in columns) headerRow.Cells.Add(Cell(column.Header, column.RightAligned, bold: true));
        header.Rows.Add(headerRow); table.RowGroups.Add(header);
        var body = new TableRowGroup();
        foreach (var row in rows)
        {
            var line = new TableRow();
            foreach (var column in columns)
            {
                var value = row.Table.Columns.Contains(column.Path) ? row[column.Path] : null;
                line.Cells.Add(Cell(value switch { null or DBNull => "", IFormattable f when column.Format != null => f.ToString(column.Format, Ui.Turkish), _ => value.ToString() ?? "" }, column.RightAligned));
            }
            body.Rows.Add(line);
        }
        table.RowGroups.Add(body);
        document.Blocks.Add(table);
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, title);
    }

    private static TableCell Cell(string text, bool right, bool bold = false) =>
        new(new Paragraph(new Run(text)) { TextAlignment = right ? TextAlignment.Right : TextAlignment.Left, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0) })
        { BorderBrush = Ui.Border, BorderThickness = new Thickness(0, 0, 0.5, 0.5), Padding = new Thickness(3, 1, 3, 1) };
}
