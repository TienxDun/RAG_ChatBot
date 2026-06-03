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
    void FillMetadata(
        ExcelWorksheet worksheet, 
        int headerRowIndex, 
        Dictionary<string, string> metadataValues,
        Dictionary<string, string>? metadataCellMappings = null);
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

            // Xóa các dòng trống dư thừa nếu số dòng dữ liệu thực tế ít hơn số dòng trong template
            if (dataRowsCount < availableRowsCount)
            {
                int startDeleteRow = targetRows[dataRowsCount];
                int rowsToDelete = availableRowsCount - dataRowsCount;
                worksheet.DeleteRow(startDeleteRow, rowsToDelete);
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
                int insertAt = lastRowInTemplate; // Chèn bên TRONG dải công thức (tại dòng template cuối cùng) để Excel tự động mở rộng công thức SUM/AVERAGE

                // Thực hiện chèn dòng trống trong EPPlus
                worksheet.InsertRow(insertAt, rowsToInsert);

                // Sau khi chèn, dòng lastRowInTemplate cũ bị dịch chuyển xuống vị trí: lastRowInTemplate + rowsToInsert
                int shiftedSourceRow = lastRowInTemplate + rowsToInsert;

                // Copy style từ dòng template bị dịch chuyển (shiftedSourceRow) lên các dòng mới chèn
                int totalCols = worksheet.Dimension?.Columns ?? 0;
                for (int i = 0; i < rowsToInsert; i++)
                {
                    int newRowIndex = insertAt + i;
                    worksheet.Row(newRowIndex).Height = worksheet.Row(shiftedSourceRow).Height;

                    for (int col = 1; col <= totalCols; col++)
                    {
                        var sourceCell = worksheet.Cells[shiftedSourceRow, col];
                        var destCell = worksheet.Cells[newRowIndex, col];

                        destCell.StyleID = sourceCell.StyleID;
                        destCell.Value = null; // Đảm bảo ô trống
                    }
                }

                // Tái cấu trúc danh sách targetRows tuần tự từ dòng bắt đầu để điền đầy đủ dữ liệu
                targetRows.Clear();
                for (int i = 0; i < dataRowsCount; i++)
                {
                    targetRows.Add(dataStartRowIndex + i);
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
    public void FillMetadata(
        ExcelWorksheet worksheet, 
        int headerRowIndex, 
        Dictionary<string, string> metadataValues,
        Dictionary<string, string>? metadataCellMappings = null)
    {
        if (metadataValues == null || metadataValues.Count == 0) return;

        // Chỉ điền metadata nếu được định nghĩa cấu hình cụ thể trong MetadataCellMappings
        if (metadataCellMappings == null || metadataCellMappings.Count == 0) return;

        foreach (var kvp in metadataCellMappings)
        {
            // Hỗ trợ 2 định dạng trong MetadataCellMappings:
            //   Format 1 (cũ): { "mô_tả_ngữ_nghĩa": "A5" }  → key=mô_tả, value=địa_chỉ_ô
            //   Format 2 (mới): { "A5": "mô_tả_ngữ_nghĩa" }  → key=địa_chỉ_ô, value=mô_tả
            // Tự động nhận diện: nếu key trông giống địa chỉ ô Excel (chữ cái + số, vd: A5, D10) → Format 2
            string cellAddress;
            string semanticKey;

            bool keyIsCellAddress = System.Text.RegularExpressions.Regex.IsMatch(
                kvp.Key.Trim(), @"^[A-Za-z]{1,3}\d{1,7}$");

            if (keyIsCellAddress)
            {
                // Format 2: key = địa chỉ ô, value = mô tả ngữ nghĩa
                cellAddress = kvp.Key.Trim();
                semanticKey = kvp.Value;
            }
            else
            {
                // Format 1: key = mô tả ngữ nghĩa, value = địa chỉ ô
                semanticKey = kvp.Key;
                cellAddress = kvp.Value;
            }

            if (string.IsNullOrWhiteSpace(cellAddress)) continue;

            string? matchedValue = _textUtility.FindBestMetadataValue(metadataValues, semanticKey);
            if (matchedValue != null)
            {
                var cell = worksheet.Cells[cellAddress];
                string currentText = cell.Text?.Trim() ?? "";
                if (currentText.Contains(":"))
                {
                    int colonIndex = currentText.IndexOf(':');
                    string label = currentText.Substring(0, colonIndex).Trim();
                    cell.Value = $"{label}: {matchedValue}";
                }
                else
                {
                    cell.Value = matchedValue;
                }
            }
        }
    }
}
