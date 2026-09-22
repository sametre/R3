using System.Data;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FastReport;

namespace R3.Desktop.OfficeTools;

/// <summary>
/// AR3'nin Office ve raporlama çıkışları için tek giriş noktası.
/// Veritabanı sorguları Dapper katmanına, Excel/OpenXML ve rapor şablonları
/// ise bu servis üzerinden bağlanır; ekranlar üçüncü parti API'lere doğrudan bağlanmaz.
/// </summary>
public sealed class OfficeToolsService
{
    public void ExportExcel(DataTable table, string filePath, string sheetName = "AR3")
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SafeSheetName(sheetName));
        sheet.Cell(1, 1).InsertTable(table, true);
        sheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);
    }

    public void CreateWordDocument(string filePath, string title, string body)
    {
        using var document = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        main.Document = new Document(new Body(
            new Paragraph(new Run(new Text(title))),
            new Paragraph(new Run(new Text(body)))));
        main.Document.Save();
    }

    public void CreateReportTemplate(string filePath, string title)
    {
        using var report = new Report { ReportInfo = { Name = title } };
        report.Save(filePath);
    }

    private static string SafeSheetName(string value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "AR3" : value.Trim();
        foreach (var invalid in new[] { ':', '\\', '/', '?', '*', '[', ']' }) name = name.Replace(invalid, '-');
        return name.Length > 31 ? name[..31] : name;
    }
}
