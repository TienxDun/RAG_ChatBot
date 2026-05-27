using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Backend.Services.Excel;

public interface IExcelTemplateFiller
{
    DataTable ConvertJsonToDataTable(string jsonString, List<string> templateColumns);
    void FillHorizontalTemplate(
        ExcelWorksheet worksheet,
        DataTable data,
        int headerRowIndex,
        int startColumnIndex,
        int dataStartRowIndex,
        List<string>? rowLabels = null,
        List<int>? fillableRowIndexes = null,
        int? totalRowIndex = null,
        bool isExplicitFillableOnly = false);
    void FillHierarchicalTemplate(
        ExcelWorksheet worksheet,
        DataTable data,
        int headerRowIndex,
        int startColumnIndex,
        List<FlattenedColumn> columns,
        List<string>? rowLabels = null,
        List<int>? fillableRowIndexes = null,
        int? totalRowIndex = null,
        bool isExplicitFillableOnly = false);
    void FillMetadata(ExcelWorksheet worksheet, int headerRowIndex, Dictionary<string, string> metadataValues);
}

public class ExcelTemplateFiller : IExcelTemplateFiller
{
    private readonly ITextUtility _textUtility;

    public ExcelTemplateFiller(ITextUtility textUtility)
    {
        _textUtility = textUtility;
    }

    private class ColumnMapping
    {
        public string DataTableColumnName { get; set; } = string.Empty;
        public int ExcelColumnIndex { get; set; }
    }

    // Chuyển đổi JSON sang DataTable case-insensitive theo các cột của template
    public DataTable ConvertJsonToDataTable(string jsonString, List<string> templateColumns)
    {
        var dataTable = new DataTable();
        var normalizedColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var col in templateColumns)
        {
            dataTable.Columns.Add(col, typeof(object));
            normalizedColumns[col] = col;
        }

        if (string.IsNullOrWhiteSpace(jsonString))
        {
            return dataTable;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonString);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return dataTable;
            }

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var row = dataTable.NewRow();

                foreach (var prop in element.EnumerateObject())
                {
                    if (normalizedColumns.TryGetValue(prop.Name, out var originalColName))
                    {
                        var valElement = prop.Value;
                        object? value = null;

                        switch (valElement.ValueKind)
                        {
                            case JsonValueKind.Number:
                                value = valElement.GetDouble();
                                break;
                            case JsonValueKind.True:
                            case JsonValueKind.False:
                                value = valElement.GetBoolean();
                                break;
                            case JsonValueKind.String:
                                var str = valElement.GetString();
                                if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                                {
                                    value = dt;
                                }
                                else if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                                {
                                    value = d;
                                }
                                else
                                {
                                    value = str;
                                }
                                break;
                            case JsonValueKind.Null:
                                value = DBNull.Value;
                                break;
                            default:
                                value = valElement.ToString();
                                break;
                        }

                        row[originalColName] = value ?? DBNull.Value;
                    }
                }

                dataTable.Rows.Add(row);
            }
        }
        catch
        {
            // Bỏ qua lỗi parse và trả về table rỗng hoặc phần đã parse được
        }

        return dataTable;
    }

    // Ghi một dòng dữ liệu vào Excel sheet
    private void WriteRowData(ExcelWorksheet worksheet, DataRow dataRow, int excelRowIndex, List<ColumnMapping> mappings)
    {
        foreach (var map in mappings)
        {
            var cell = worksheet.Cells[excelRowIndex, map.ExcelColumnIndex];
            var val = dataRow[map.DataTableColumnName];

            if (val == null || val == DBNull.Value)
            {
                cell.Value = null;
                continue;
            }

            if (val is DateTime dt)
            {
                cell.Value = dt;
                cell.Style.Numberformat.Format = "dd/MM/yyyy";
                cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
            }
            else if (val is double || val is float || val is decimal)
            {
                cell.Value = Convert.ToDouble(val);
                cell.Style.Numberformat.Format = "#,##0.00";
                cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
            }
            else if (val is int || val is long || val is short || val is byte)
            {
                cell.Value = Convert.ToInt64(val);
                cell.Style.Numberformat.Format = "#,##0";
                cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
            }
            else
            {
                string strVal = val.ToString() ?? "";
                if (double.TryParse(strVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double dbl))
                {
                    cell.Value = dbl;
                    cell.Style.Numberformat.Format = "#,##0";
                    cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
                }
                else if (DateTime.TryParse(strVal, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dtParsed))
                {
                    cell.Value = dtParsed;
                    cell.Style.Numberformat.Format = "dd/MM/yyyy";
                    cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                }
                else
                {
                    cell.Value = strVal;
                }
            }
        }
    }

    // Điền dữ liệu dạng Horizontal (Bảng lưới thông thường)
    public void FillHorizontalTemplate(
        ExcelWorksheet worksheet,
        DataTable data,
        int headerRowIndex,
        int startColumnIndex,
        int dataStartRowIndex,
        List<string>? rowLabels = null,
        List<int>? fillableRowIndexes = null,
        int? totalRowIndex = null,
        bool isExplicitFillableOnly = false)
    {
        // 1. Tạo mapping cột
        var mappings = new List<ColumnMapping>();
        int totalCols = worksheet.Dimension?.Columns ?? 0;
        
        for (int c = startColumnIndex; c <= totalCols; c++)
        {
            string headerText = worksheet.Cells[headerRowIndex, c].Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(headerText)) continue;

            string cleanHeader = _textUtility.RemoveDiacritics(headerText);
            foreach (DataColumn dc in data.Columns)
            {
                string cleanDcName = _textUtility.RemoveDiacritics(dc.ColumnName);
                if (string.Equals(cleanHeader, cleanDcName, StringComparison.OrdinalIgnoreCase))
                {
                    mappings.Add(new ColumnMapping
                    {
                        DataTableColumnName = dc.ColumnName,
                        ExcelColumnIndex = c
                    });
                    break;
                }
            }
        }

        FillTemplateCore(
            worksheet,
            data,
            dataStartRowIndex,
            mappings,
            rowLabels,
            fillableRowIndexes,
            totalRowIndex,
            isExplicitFillableOnly);
    }

    // Điền dữ liệu dạng Hierarchical (Bảng phân tầng)
    public void FillHierarchicalTemplate(
        ExcelWorksheet worksheet,
        DataTable data,
        int headerRowIndex,
        int startColumnIndex,
        List<FlattenedColumn> columns,
        List<string>? rowLabels = null,
        List<int>? fillableRowIndexes = null,
        int? totalRowIndex = null,
        bool isExplicitFillableOnly = false)
    {
        // 1. Tạo mapping cột từ FlattenedColumn truyền vào
        var mappings = new List<ColumnMapping>();
        foreach (var col in columns)
        {
            string cleanUniqueKey = _textUtility.RemoveDiacritics(col.UniqueKey);
            foreach (DataColumn dc in data.Columns)
            {
                string cleanDcName = _textUtility.RemoveDiacritics(dc.ColumnName);
                if (string.Equals(cleanUniqueKey, cleanDcName, StringComparison.OrdinalIgnoreCase))
                {
                    mappings.Add(new ColumnMapping
                    {
                        DataTableColumnName = dc.ColumnName,
                        ExcelColumnIndex = col.ColumnIndex
                    });
                    break;
                }
            }
        }

        int dataStartRowIndex = headerRowIndex + 1;

        FillTemplateCore(
            worksheet,
            data,
            dataStartRowIndex,
            mappings,
            rowLabels,
            fillableRowIndexes,
            totalRowIndex,
            isExplicitFillableOnly);
    }

    // Core logic xử lý điền dữ liệu, chèn dòng và copy style
    private void FillTemplateCore(
        ExcelWorksheet worksheet,
        DataTable data,
        int dataStartRowIndex,
        List<ColumnMapping> mappings,
        List<string>? rowLabels = null,
        List<int>? fillableRowIndexes = null,
        int? totalRowIndex = null,
        bool isExplicitFillableOnly = false)
    {
        if (data == null || data.Rows.Count == 0 || mappings.Count == 0)
        {
            return;
        }

        // Trường hợp có rowLabels để map dòng cụ thể (Ghi đè)
        if (rowLabels != null && rowLabels.Count > 0)
        {
            var keyMapping = mappings.FirstOrDefault();
            if (keyMapping != null)
            {
                foreach (DataRow dr in data.Rows)
                {
                    var val = dr[keyMapping.DataTableColumnName];
                    if (val == null || val == DBNull.Value) continue;

                    string searchVal = "";
                    if (val is DateTime dt) searchVal = dt.ToString("yyyy-MM-dd");
                    else searchVal = val.ToString() ?? "";

                    int targetExcelRow = -1;
                    int startScan = dataStartRowIndex;
                    int endScan = totalRowIndex.HasValue ? totalRowIndex.Value - 1 : (worksheet.Dimension?.Rows ?? startScan + 100);

                    for (int r = startScan; r <= endScan; r++)
                    {
                        string cellText = worksheet.Cells[r, keyMapping.ExcelColumnIndex].Text?.Trim() ?? "";

                        // So khớp ngày tháng
                        if (DateTime.TryParse(cellText, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.None, out DateTime cellDate))
                        {
                            if (val is DateTime dtVal && dtVal.Date == cellDate.Date)
                            {
                                targetExcelRow = r;
                                break;
                            }
                            else if (cellDate.ToString("yyyy-MM-dd") == searchVal)
                            {
                                targetExcelRow = r;
                                break;
                            }
                        }

                        if (string.Equals(cellText, searchVal, StringComparison.OrdinalIgnoreCase))
                        {
                            targetExcelRow = r;
                            break;
                        }

                        // So khớp mềm
                        string cleanCellText = _textUtility.RemoveDiacritics(cellText);
                        string cleanSearchVal = _textUtility.RemoveDiacritics(searchVal);
                        if (cleanCellText == cleanSearchVal && !string.IsNullOrEmpty(cleanCellText))
                        {
                            targetExcelRow = r;
                            break;
                        }
                    }

                    if (targetExcelRow != -1)
                    {
                        WriteRowData(worksheet, dr, targetExcelRow, mappings);
                    }
                }
            }
            return;
        }

        // Xử lý ghi dữ liệu tuần tự
        var targetRows = fillableRowIndexes != null && fillableRowIndexes.Count > 0
            ? new List<int>(fillableRowIndexes)
            : new List<int>();

        if (targetRows.Count == 0)
        {
            int endRow = totalRowIndex.HasValue ? totalRowIndex.Value - 1 : dataStartRowIndex + data.Rows.Count - 1;
            for (int r = dataStartRowIndex; r <= Math.Max(dataStartRowIndex, endRow); r++)
            {
                targetRows.Add(r);
            }
        }

        int availableRowsCount = targetRows.Count;
        int dataRowsCount = data.Rows.Count;

        if (dataRowsCount <= availableRowsCount)
        {
            // Điền bình thường không cần chèn dòng
            for (int i = 0; i < dataRowsCount; i++)
            {
                WriteRowData(worksheet, data.Rows[i], targetRows[i], mappings);
            }
        }
        else
        {
            if (isExplicitFillableOnly)
            {
                // Chỉ điền tối đa số dòng trống có sẵn
                for (int i = 0; i < availableRowsCount; i++)
                {
                    WriteRowData(worksheet, data.Rows[i], targetRows[i], mappings);
                }
            }
            else
            {
                // Cần chèn thêm dòng
                int rowsToInsert = dataRowsCount - availableRowsCount;
                int lastRowInTemplate = targetRows.Last();
                int insertAt = lastRowInTemplate + 1;

                // Thực hiện chèn dòng trống trong EPPlus
                worksheet.InsertRow(insertAt, rowsToInsert);

                // Copy style từ dòng template cuối cùng xuống các dòng mới được chèn
                int totalCols = worksheet.Dimension?.Columns ?? 0;
                for (int i = 0; i < rowsToInsert; i++)
                {
                    int newRowIndex = insertAt + i;
                    worksheet.Row(newRowIndex).Height = worksheet.Row(lastRowInTemplate).Height;

                    for (int col = 1; col <= totalCols; col++)
                    {
                        var sourceCell = worksheet.Cells[lastRowInTemplate, col];
                        var destCell = worksheet.Cells[newRowIndex, col];

                        destCell.StyleID = sourceCell.StyleID;
                        destCell.Value = null; // Đảm bảo ô trống
                    }

                    // Thêm dòng mới vào danh sách ghi
                    targetRows.Add(newRowIndex);
                }

                // Ghi dữ liệu tuần tự vào toàn bộ các dòng
                for (int i = 0; i < dataRowsCount; i++)
                {
                    WriteRowData(worksheet, data.Rows[i], targetRows[i], mappings);
                }
            }
        }
    }

    // Điền metadata chung vào phần đầu trang Excel
    public void FillMetadata(ExcelWorksheet worksheet, int headerRowIndex, Dictionary<string, string> metadataValues)
    {
        if (metadataValues == null || metadataValues.Count == 0) return;
        
        int totalCols = worksheet.Dimension?.Columns ?? 0;
        
        for (int r = 1; r < headerRowIndex; r++)
        {
            for (int c = 1; c <= totalCols; c++)
            {
                string val = worksheet.Cells[r, c].Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(val)) continue;

                if (val.Contains(":"))
                {
                    int colonIndex = val.IndexOf(':');
                    string label = val.Substring(0, colonIndex).Trim();
                    
                    string? matchedValue = _textUtility.FindBestMetadataValue(metadataValues, label);
                    if (!string.IsNullOrEmpty(matchedValue))
                    {
                        worksheet.Cells[r, c].Value = $"{label}: {matchedValue}";
                    }
                }
                else
                {
                    if (c + 1 <= totalCols)
                    {
                        if (val.Contains("mã", StringComparison.OrdinalIgnoreCase) || 
                            val.Contains("chuyền", StringComparison.OrdinalIgnoreCase) || 
                            val.Contains("style", StringComparison.OrdinalIgnoreCase) || 
                            val.Contains("line", StringComparison.OrdinalIgnoreCase) ||
                            val.Contains("ngày", StringComparison.OrdinalIgnoreCase) ||
                            val.Contains("tên", StringComparison.OrdinalIgnoreCase) ||
                            val.Contains("khách", StringComparison.OrdinalIgnoreCase) ||
                            val.Contains("customer", StringComparison.OrdinalIgnoreCase))
                        {
                            string? matchedValue = _textUtility.FindBestMetadataValue(metadataValues, val);
                            if (!string.IsNullOrEmpty(matchedValue))
                            {
                                worksheet.Cells[r, c + 1].Value = matchedValue;
                            }
                        }
                    }
                }
            }
        }
    }
}
