using ClosedXML.Excel;
using SalesApp.Core.Contracts.Reports;

namespace SalesApp.Infrastructure.Reporting;

/// <summary>Turns a <see cref="ReportResult"/> into an .xlsx workbook: title, period, header, data, totals.</summary>
public static class ExcelReportWriter
{
    /// <summary>Indian digit grouping (12,34,56,789.00), which Excel can't do with a plain "#,##0".</summary>
    internal const string IndianMoney = "[>=10000000]##\\,##\\,##\\,##0.00;[>=100000]##\\,##\\,##0.00;##,##0.00";
    private const string Quantity = "#,##0.###";
    private const string Percent = "0.0%";
    private const string Date = "dd-mmm-yyyy";

    public static byte[] Write(ReportResult report, string companyName)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName(report.Title));
        var columns = report.Columns;

        sheet.Cell(1, 1).Value = $"{companyName}: {report.Title}";
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        sheet.Cell(2, 1).Value = report.Subtitle;
        sheet.Cell(2, 1).Style.Font.SetItalic();

        const int headerRow = 4;
        for (var c = 0; c < columns.Count; c++)
        {
            var cell = sheet.Cell(headerRow, c + 1);
            cell.Value = columns[c].Header;
            cell.Style.Font.SetBold();
            cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F1F5F9"));
            cell.Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
            if (IsNumeric(columns[c].Type))
            {
                cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            }
        }

        var row = headerRow + 1;
        foreach (var data in report.Rows)
        {
            WriteRow(sheet, row++, columns, data);
        }

        WriteRow(sheet, row, columns, report.Totals);
        sheet.Row(row).Style.Font.SetBold();
        sheet.Row(row).Style.Border.SetTopBorder(XLBorderStyleValues.Thin);

        sheet.SheetView.FreezeRows(headerRow);
        sheet.Columns(1, columns.Count).AdjustToContents(headerRow, row);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteRow(IXLWorksheet sheet, int row, IReadOnlyList<ReportColumn> columns, IReadOnlyDictionary<string, object?> data)
    {
        for (var c = 0; c < columns.Count; c++)
        {
            var cell = sheet.Cell(row, c + 1);
            var value = data.GetValueOrDefault(columns[c].Key);
            switch (value)
            {
                case null:
                    continue;
                case decimal d:
                    cell.Value = d;
                    break;
                case int i:
                    cell.Value = i;
                    break;
                case DateOnly date:
                    cell.Value = date.ToDateTime(TimeOnly.MinValue);
                    break;
                default:
                    cell.Value = value.ToString();
                    break;
            }

            cell.Style.NumberFormat.Format = columns[c].Type switch
            {
                ReportColumnType.Money => IndianMoney,
                ReportColumnType.Quantity => Quantity,
                ReportColumnType.Percent => Percent,
                ReportColumnType.Date => Date,
                ReportColumnType.Count => "0",
                _ => "@",
            };
        }
    }

    private static bool IsNumeric(ReportColumnType type) =>
        type is ReportColumnType.Money or ReportColumnType.Quantity or ReportColumnType.Percent or ReportColumnType.Count;

    /// <summary>Excel sheet names: max 31 characters, no []:*?/\.</summary>
    private static string SheetName(string title)
    {
        var clean = new string(title.Where(ch => !"[]:*?/\\".Contains(ch, StringComparison.Ordinal)).ToArray());
        return clean.Length <= 31 ? clean : clean[..31];
    }
}
