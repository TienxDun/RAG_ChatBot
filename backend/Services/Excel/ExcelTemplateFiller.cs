using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Backend.Services.Excel;

public static class ExcelTemplateFiller
{
    // Chuyển dữ liệu chuỗi JSON từ kết quả truy vấn thành DataTable và parse định dạng ngày tháng
    public static DataTable ConvertJsonToDataTable(string jsonString, List<string> templateColumns)
    {
        var dataTable = new DataTable();
        
        foreach (var colName in templateColumns)
        {
            var col = dataTable.Columns.Add(colName, typeof(object));
            col.ExtendedProperties["IsNumeric"] = false;
        }

        using var jsonDoc = JsonDocument.Parse(jsonString);
        var root = jsonDoc.RootElement;

        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
        {
            var elements = root.EnumerateArray().ToList();
            var sampleElements = elements.Take(50).ToList();
            foreach (DataColumn col in dataTable.Columns)
            {
                string colName = col.ColumnName;
                bool hasData = false;
                bool allNumbers = true;

                foreach (var el in sampleElements)
                {
                    if (el.TryGetProperty(colName, out var prop))
                    {
                        if (prop.ValueKind == JsonValueKind.Null || prop.ValueKind == JsonValueKind.Undefined)
                            continue;

                        var strVal = prop.ToString().Trim();
                        if (string.IsNullOrWhiteSpace(strVal)) continue;

                        hasData = true;
                        if (!double.TryParse(strVal, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                        {
                            allNumbers = false;
                            break;
                        }
                    }
                }
                col.ExtendedProperties["IsNumeric"] = hasData && allNumbers;
            }

            foreach (var element in elements)
            {
                var row = dataTable.NewRow();
                bool hasAnyData = false;

                foreach (DataColumn col in dataTable.Columns)
                {
                    string colName = col.ColumnName;
                    bool isNumeric = (bool)col.ExtendedProperties["IsNumeric"]!;

                    if (element.TryGetProperty(colName, out var prop) && 
                        prop.ValueKind != JsonValueKind.Null && 
                        prop.ValueKind != JsonValueKind.Undefined)
                    {
                        var strVal = prop.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(strVal))
                        {
                            hasAnyData = true;
                            if (isNumeric && double.TryParse(strVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                            {
                                row[colName] = val;
                            }
                            else if (DateTime.TryParse(strVal, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dtVal))
                            {
                                row[colName] = dtVal;
                            }
                            else
                            {
                                row[colName] = strVal;
                            }
                        }
                        else 
                        {
                            row[colName] = DBNull.Value;
                        }
                    }
                    else
                    {
                        row[colName] = DBNull.Value;
                    }
                }

                if (hasAnyData)
                {
                    dataTable.Rows.Add(row);
                }
            }
        }
        
        return dataTable;
    }

    // Điền dữ liệu từ Markdown vào các ô của Excel Worksheet dựa trên nhãn và cột/hàng động (dành cho template dọc)
    public static void FillVerticalTemplate(ExcelWorksheet worksheet, string markdownText, int headerRowIndex, int labelColumnIndex, int valueColumnIndex)
    {
        var tableRows = MarkdownTableParser.ParseMarkdownTable(markdownText);
        var markdownData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var row in tableRows)
        {
            if (row.Count >= 2)
            {
                string key = row[0].Trim().Replace("**", "");
                markdownData[key] = row[1].Trim().Replace("**", "");
            }
        }

        int endRow = worksheet.Dimension?.End.Row ?? (headerRowIndex + 15);
        for (int row = headerRowIndex + 1; row <= endRow; row++)
        {
            string labelInExcel = worksheet.Cells[row, labelColumnIndex].Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(labelInExcel)) continue;

            if (markdownData.TryGetValue(labelInExcel, out string? value))
            {
                var valueCell = worksheet.Cells[row, valueColumnIndex];
                
                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double num))
                {
                    valueCell.Value = num;
                    valueCell.Style.Numberformat.Format = "#,##0";
                    valueCell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Left;
                }
                else if (MarkdownTableParser.TryParseDateTime(value, out DateTime dt))
                {
                    valueCell.Value = dt;
                    valueCell.Style.Numberformat.Format = "dd/MM/yyyy";
                    valueCell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Left;
                }
                else
                {
                    valueCell.Value = value;
                    valueCell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Left;
                }
            }
        }
    }

    // Điền dữ liệu dạng bảng từ DataTable (dành cho template ngang)
    public static void FillHorizontalTemplate(ExcelWorksheet worksheet, DataTable dataTable, int headerRowIndex, int startColumnIndex, int columnCount)
    {
        int dataStartRow = headerRowIndex + 1;
        if (worksheet.Dimension != null && worksheet.Dimension.End.Row >= dataStartRow)
        {
            // Chỉ xóa dữ liệu trong vùng các cột tiêu đề của báo cáo ngang
            worksheet.Cells[dataStartRow, startColumnIndex, worksheet.Dimension.End.Row, startColumnIndex + columnCount - 1].Value = null;
        }

        // Đổ dữ liệu mới
        if (dataTable.Rows.Count > 0)
        {
            var dataRange = worksheet.Cells[dataStartRow, startColumnIndex];
            dataRange.LoadFromDataTable(dataTable, PrintHeaders: false);

            for (int i = 0; i < dataTable.Columns.Count; i++)
            {
                var column = dataTable.Columns[i];
                var colRange = worksheet.Cells[dataStartRow, startColumnIndex + i, dataStartRow + dataTable.Rows.Count - 1, startColumnIndex + i];

                if (column.ExtendedProperties["IsNumeric"] is bool isNum && isNum)
                {
                    colRange.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
                }

                // Định dạng ngày tháng
                bool isDateColumn = column.DataType == typeof(DateTime) || 
                                    column.DataType == typeof(DateTimeOffset) ||
                                    column.ColumnName.Contains("Ngay", StringComparison.OrdinalIgnoreCase) ||
                                    column.ColumnName.Contains("Date", StringComparison.OrdinalIgnoreCase) ||
                                    column.ColumnName.Contains("Time", StringComparison.OrdinalIgnoreCase);

                if (isDateColumn)
                {
                    colRange.Style.Numberformat.Format = "dd/MM/yyyy";
                    colRange.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                }
            }
        }
    }
}
