using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Backend.Services.Excel;

public static class ExcelExportHelper
{
    // Xuất danh sách dữ liệu ra file Excel dạng bảng lưới thông thường, định dạng số và ngày tháng (dd/MM/yyyy)
    public static byte[] ExportGenericExcel(List<Dictionary<string, object>> data)
    {
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Data Export");

        if (data == null || data.Count == 0)
        {
            return package.GetAsByteArray();
        }

        var headers = data[0].Keys.ToList();

        for (int i = 0; i < headers.Count; i++)
        {
            var cell = worksheet.Cells[1, i + 1];
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(157, 186, 217));
            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
            cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
            
            cell.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            cell.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            cell.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            cell.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
        }

        for (int rowIndex = 0; rowIndex < data.Count; rowIndex++)
        {
            for (int colIndex = 0; colIndex < headers.Count; colIndex++)
            {
                var cell = worksheet.Cells[rowIndex + 2, colIndex + 1];
                var val = data[rowIndex][headers[colIndex]];

                if (val is JsonElement element)
                {
                    switch (element.ValueKind)
                    {
                        case JsonValueKind.Number:
                            if (element.TryGetInt64(out long l)) cell.Value = l;
                            else cell.Value = element.GetDouble();
                            break;
                        case JsonValueKind.True:
                        case JsonValueKind.False:
                            cell.Value = element.GetBoolean();
                            break;
                        case JsonValueKind.Null:
                            cell.Value = null;
                            break;
                        case JsonValueKind.String:
                            var str = element.GetString() ?? "";
                            // Thử parse số để định dạng đúng trong Excel nếu chuỗi chỉ chứa số
                            if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out double dbl))
                            {
                                cell.Value = dbl;
                            }
                            else if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dtVal))
                            {
                                cell.Value = dtVal;
                            }
                            else
                            {
                                cell.Value = str;
                            }
                            break;
                        default:
                            cell.Value = element.ToString();
                            break;
                    }
                }
                else
                {
                    cell.Value = val;
                }

                // Căn lề phải và format dấu phẩy cho các cột số cho chuyên nghiệp
                if (cell.Value is double || cell.Value is float || cell.Value is decimal)
                {
                    cell.Style.Numberformat.Format = "#,##0.00";
                    cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
                }
                else if (cell.Value is long || cell.Value is int)
                {
                    cell.Style.Numberformat.Format = "#,##0";
                    cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
                }
                else if (cell.Value is DateTime || cell.Value is DateTimeOffset || headers[colIndex].Contains("Ngay", StringComparison.OrdinalIgnoreCase) || headers[colIndex].Contains("Date", StringComparison.OrdinalIgnoreCase) || headers[colIndex].Contains("Time", StringComparison.OrdinalIgnoreCase))
                {
                    // Nếu là string dạng ngày, thử chuyển sang DateTime để format chuẩn
                    if (cell.Value is string strDate && DateTime.TryParse(strDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
                    {
                        cell.Value = parsedDate;
                    }

                    if (cell.Value is DateTime || cell.Value is DateTimeOffset)
                    {
                        cell.Style.Numberformat.Format = "dd/MM/yyyy";
                        cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                    }
                }

                cell.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            }
        }

        if (headers.Count > 0)
        {
            worksheet.Cells[1, 1, data.Count + 1, headers.Count].AutoFilter = true;
            worksheet.Cells[1, 1, data.Count + 1, headers.Count].AutoFitColumns(12);
        }

        return package.GetAsByteArray();
    }

    // Tự sinh file Excel từ bảng Markdown với phong cách hiện đại và định dạng chuyên nghiệp
    public static byte[] ExportMarkdownToExcelDynamic(string markdownText)
    {
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Data Export");

        var tableRows = MarkdownTableParser.ParseMarkdownTable(markdownText);
        if (tableRows.Count == 0) return package.GetAsByteArray();

        bool isVerticalTable = tableRows[0].Count == 2;
        int currentRow = 1;
        for (int r = 0; r < tableRows.Count; r++)
        {
            var rowData = tableRows[r];
            for (int c = 0; c < rowData.Count; c++)
            {
                var cell = worksheet.Cells[currentRow, c + 1];
                string rawValue = rowData[c];

                if (double.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double num))
                {
                    cell.Value = num;
                    cell.Style.Numberformat.Format = "#,##0";
                    cell.Style.HorizontalAlignment = isVerticalTable ? 
                        OfficeOpenXml.Style.ExcelHorizontalAlignment.Left : 
                        OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
                }
                else if (MarkdownTableParser.TryParseDateTime(rawValue, out DateTime dt))
                {
                    cell.Value = dt;
                    cell.Style.Numberformat.Format = "dd/MM/yyyy";
                    cell.Style.HorizontalAlignment = isVerticalTable ? 
                        OfficeOpenXml.Style.ExcelHorizontalAlignment.Left : 
                        OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                }
                else
                {
                    cell.Value = rawValue;
                    if (isVerticalTable)
                    {
                        cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Left;
                    }
                }

                cell.Style.Font.Name = "Segoe UI";
                if (currentRow == 1)
                {
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(242, 242, 242));
                    cell.Style.Font.Color.SetColor(System.Drawing.Color.FromArgb(51, 51, 51));
                    cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                }
                else if (c == 0)
                {
                    cell.Style.Font.Bold = true;
                }

                cell.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                
                var borderColor = System.Drawing.Color.FromArgb(220, 220, 220);
                cell.Style.Border.Top.Color.SetColor(borderColor);
                cell.Style.Border.Bottom.Color.SetColor(borderColor);
                cell.Style.Border.Left.Color.SetColor(borderColor);
                cell.Style.Border.Right.Color.SetColor(borderColor);
            }
            currentRow++;
        }

        if (tableRows[0].Count > 0 && !isVerticalTable)
        {
            worksheet.Cells[1, 1, currentRow - 1, tableRows[0].Count].AutoFilter = true;
        }
        worksheet.Cells[1, 1, currentRow - 1, tableRows[0].Count].AutoFitColumns(12);
        return package.GetAsByteArray();
    }
}
