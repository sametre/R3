using System.Data;
using ClosedXML.Excel;

namespace R3.Desktop.WinForms.Components.Grids;

/// <summary>A column to export: data property, header text, display format.</summary>
public sealed record ExportColumn(string Property, string Header, string? Format);

/// <summary>
/// Excel export of what the user sees: visible grid columns in display order with their header text; numbers
/// stay numeric (with a tr-friendly number format), dates stay dates. <see cref="ColumnsOf"/> runs on the UI thread,
/// <see cref="ToExcel"/> can run in the background (it touches no controls).
/// </summary>
public static class GridExporter
{
    public static IReadOnlyList<ExportColumn> ColumnsOf(R3DataGrid grid) =>
        grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible && !string.IsNullOrEmpty(c.DataPropertyName)).OrderBy(c => c.DisplayIndex)
            .Select(c => new ExportColumn(c.DataPropertyName, c.HeaderText, c.DefaultCellStyle.Format)).ToList();

    public static void ToExcel(IReadOnlyList<ExportColumn> exportColumns, DataTable rows, string path, string sheetName)
    {
        var columns = exportColumns.Where(c => rows.Columns.Contains(c.Property)).ToList();
        using var book = new XLWorkbook();
        var sheet = book.Worksheets.Add(sheetName.Length > 31 ? sheetName[..31] : sheetName);
        for (var c = 0; c < columns.Count; c++)
        {
            var header = sheet.Cell(1, c + 1);
            header.Value = columns[c].Header; header.Style.Font.Bold = true; header.Style.Fill.BackgroundColor = XLColor.FromArgb(0xEE, 0xEF, 0xF1);
        }
        for (var r = 0; r < rows.Rows.Count; r++)
            for (var c = 0; c < columns.Count; c++)
            {
                var value = rows.Rows[r][columns[c].Property];
                var cell = sheet.Cell(r + 2, c + 1);
                switch (value)
                {
                    case DBNull or null: break;
                    case decimal or double or float or long or int or short:
                        cell.Value = Convert.ToDouble(value);
                        cell.Style.NumberFormat.Format = columns[c].Format is "N0" ? "#,##0" : "#,##0.00";
                        break;
                    case DateTime date: cell.Value = date; cell.Style.DateFormat.Format = "dd.mm.yyyy"; break;
                    default: cell.Value = value.ToString(); break;
                }
            }
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, Math.Min(rows.Rows.Count + 1, 500));
        book.SaveAs(path);
    }
}
