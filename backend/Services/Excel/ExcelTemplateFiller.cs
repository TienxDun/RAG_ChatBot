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

    // Tìm kiếm dòng "Tổng" hoặc "Total" dưới header
    private static int FindTotalRowIndex(ExcelWorksheet worksheet, int dataStartRow, int startColumnIndex, int maxColIdx)
    {
        int totalRowIndex = -1;
        int maxCols = worksheet.Dimension?.End.Column ?? (startColumnIndex + 20);
        int maxRows = worksheet.Dimension?.End.Row ?? (dataStartRow + 100);

        for (int r = dataStartRow; r <= maxRows; r++)
        {
            for (int c = startColumnIndex; c <= Math.Min(maxCols, startColumnIndex + 3); c++)
            {
                string cellText = worksheet.Cells[r, c].Text?.Trim() ?? "";
                if (cellText.Equals("Tổng", StringComparison.OrdinalIgnoreCase) || 
                    cellText.Equals("Total", StringComparison.OrdinalIgnoreCase))
                {
                    totalRowIndex = r;
                    break;
                }
            }
            if (totalRowIndex != -1) break;
        }
        return totalRowIndex;
    }

    // Điền dữ liệu dạng bảng từ DataTable (dành cho template ngang)
    public static void FillHorizontalTemplate(ExcelWorksheet worksheet, DataTable dataTable, int headerRowIndex, int startColumnIndex, int columnCount, List<string>? rowLabels = null)
    {
        int dataStartRow = headerRowIndex + 1;
        int maxColIdx = startColumnIndex + columnCount - 1;
        
        // Tìm dòng "Tổng" để dịch chuyển và xóa an toàn
        int totalRowIndex = FindTotalRowIndex(worksheet, dataStartRow, startColumnIndex, maxColIdx);
        int endRow = totalRowIndex != -1 ? totalRowIndex - 1 : (worksheet.Dimension?.End.Row ?? (dataStartRow + 100));

        if (rowLabels != null && rowLabels.Count > 0 && totalRowIndex != -1)
        {
            // 1. CHẾ ĐỘ DÒ DÒNG (Row-by-Row Matching): Đổ dữ liệu khớp theo nhãn ở cột đầu tiên (Cột A)
            // Không xóa toàn bộ, không dịch chuyển dòng Tổng. Giữ nguyên công thức và nhãn ở cột A.
            
            var columns = dataTable.Columns.Cast<DataColumn>().ToList();
            if (columns.Count > 0)
            {
                string keyColName = columns[0].ColumnName; // Cột đầu tiên chứa ngày/nhãn để khớp

                for (int r = dataStartRow; r <= endRow; r++)
                {
                    string labelInExcel = worksheet.Cells[r, startColumnIndex].Text?.Trim() ?? "";
                    if (string.IsNullOrEmpty(labelInExcel)) continue;

                    // Dò tìm dòng phù hợp trong DataTable
                    DataRow? matchedRow = null;
                    foreach (DataRow row in dataTable.Rows)
                    {
                        if (IsRowMatch(row[keyColName], labelInExcel))
                        {
                            matchedRow = row;
                            break;
                        }
                    }

                    if (matchedRow != null)
                    {
                        // Điền các cột bên phải
                        for (int colIdx = 1; colIdx < columns.Count; colIdx++)
                        {
                            var column = columns[colIdx];
                            int physicalColIdx = startColumnIndex + colIdx;
                            var cell = worksheet.Cells[r, physicalColIdx];
                            var dbVal = matchedRow[column.ColumnName];

                            if (dbVal == null || dbVal == DBNull.Value)
                            {
                                cell.Value = null;
                            }
                            else
                            {
                                cell.Value = dbVal;
                                
                                // Định dạng hiển thị căn lề
                                if (column.ExtendedProperties["IsNumeric"] is bool isNum && isNum)
                                {
                                    cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
                                }
                                
                                // Nếu là cột ngày tháng
                                bool isDate = column.DataType == typeof(DateTime) || 
                                              column.DataType == typeof(DateTimeOffset) ||
                                              column.ColumnName.Contains("Ngay", StringComparison.OrdinalIgnoreCase) ||
                                              column.ColumnName.Contains("Date", StringComparison.OrdinalIgnoreCase);
                                if (isDate)
                                {
                                    cell.Style.Numberformat.Format = "dd/MM/yyyy";
                                    cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Nếu không tìm thấy dữ liệu cho ngày này trong DB, xóa sạch các ô bên phải dòng này
                        for (int colIdx = 1; colIdx < columns.Count; colIdx++)
                        {
                            worksheet.Cells[r, startColumnIndex + colIdx].Value = null;
                        }
                    }
                }
            }
        }
        else
        {
            // 2. CHẾ ĐỘ TUẦN TỰ (Sequential Fill): Giữ nguyên logic cũ khi không có nhãn pre-filled
            if (totalRowIndex != -1)
            {
                // Xóa dữ liệu cũ chỉ trong vùng dữ liệu (không xóa dòng Tổng và các dòng bên dưới)
                worksheet.Cells[dataStartRow, startColumnIndex, totalRowIndex - 1, maxColIdx].Value = null;
                
                // Dịch chuyển dòng Tổng xuống nếu số dòng dữ liệu thực tế nhiều hơn số dòng trống sẵn có
                int availableSpace = totalRowIndex - dataStartRow;
                if (dataTable.Rows.Count > availableSpace)
                {
                    int rowsToInsert = dataTable.Rows.Count - availableSpace;
                    worksheet.InsertRow(totalRowIndex, rowsToInsert, copyStylesFromRow: dataStartRow);
                    totalRowIndex += rowsToInsert; // Cập nhật vị trí mới của dòng Tổng
                }
            }
            else
            {
                // Nếu không tìm thấy dòng Tổng, xóa đến hết sheet như cũ
                if (worksheet.Dimension != null && worksheet.Dimension.End.Row >= dataStartRow)
                {
                    worksheet.Cells[dataStartRow, startColumnIndex, worksheet.Dimension.End.Row, maxColIdx].Value = null;
                }
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

    // Điền dữ liệu cho template phân cấp dựa trên index cột vật lý
    public static void FillHierarchicalTemplate(ExcelWorksheet worksheet, DataTable dataTable, int headerRowIndex, int startColumnIndex, List<FlattenedColumn> flattenedColumns, List<string>? rowLabels = null)
    {
        int dataStartRow = headerRowIndex + 1;
        int maxColIdx = flattenedColumns.Max(c => c.ColumnIndex);
        
        // 1. Tìm dòng "Tổng" để dịch chuyển và xóa an toàn
        int totalRowIndex = FindTotalRowIndex(worksheet, dataStartRow, startColumnIndex, maxColIdx);
        int endRow = totalRowIndex != -1 ? totalRowIndex - 1 : (worksheet.Dimension?.End.Row ?? (dataStartRow + 100));

        // Định hình lại thứ tự các cột của DataTable khớp với thứ tự các cột vật lý trên Excel
        var orderedTable = new DataTable();
        var orderedCols = flattenedColumns.OrderBy(c => c.ColumnIndex).ToList();
        
        foreach (var col in orderedCols)
        {
            orderedTable.Columns.Add(col.UniqueKey, typeof(object));
        }
        
        foreach (DataRow row in dataTable.Rows)
        {
            var newRow = orderedTable.NewRow();
            foreach (var col in orderedCols)
            {
                if (dataTable.Columns.Contains(col.UniqueKey))
                {
                    newRow[col.UniqueKey] = row[col.UniqueKey];
                }
                else
                {
                    newRow[col.UniqueKey] = DBNull.Value;
                }
            }
            orderedTable.Rows.Add(newRow);
        }

        if (rowLabels != null && rowLabels.Count > 0 && totalRowIndex != -1)
        {
            // 2. CHẾ ĐỘ DÒ DÒNG (Row-by-Row Matching) cho Hierarchical Template
            if (orderedCols.Count > 0)
            {
                string keyColName = orderedCols[0].UniqueKey; // Cột đầu tiên chứa nhãn để khớp

                for (int r = dataStartRow; r <= endRow; r++)
                {
                    string labelInExcel = worksheet.Cells[r, startColumnIndex].Text?.Trim() ?? "";
                    if (string.IsNullOrEmpty(labelInExcel)) continue;

                    // Dò tìm dòng phù hợp trong orderedTable
                    DataRow? matchedRow = null;
                    foreach (DataRow row in orderedTable.Rows)
                    {
                        if (IsRowMatch(row[keyColName], labelInExcel))
                        {
                            matchedRow = row;
                            break;
                        }
                    }

                    if (matchedRow != null)
                    {
                        // Ghi dữ liệu vào các cột bên phải
                        for (int i = 1; i < orderedCols.Count; i++)
                        {
                            var col = orderedCols[i];
                            int physicalColIdx = col.ColumnIndex;
                            var cell = worksheet.Cells[r, physicalColIdx];
                            var dbVal = matchedRow[col.UniqueKey];

                            if (dbVal == null || dbVal == DBNull.Value)
                            {
                                cell.Value = null;
                            }
                            else
                            {
                                cell.Value = dbVal;

                                // Tự động nhận diện cột số để căn lề phải
                                bool isNumeric = double.TryParse(dbVal.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out _);
                                if (isNumeric)
                                {
                                    cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
                                }

                                // Định dạng ngày tháng
                                bool isDate = col.ChildHeader.Contains("Ngay", StringComparison.OrdinalIgnoreCase) || 
                                              col.ChildHeader.Contains("Date", StringComparison.OrdinalIgnoreCase);
                                if (isDate)
                                {
                                    cell.Style.Numberformat.Format = "dd/MM/yyyy";
                                    cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Nếu không tìm thấy dữ liệu, xóa sạch các ô bên phải dòng này
                        for (int i = 1; i < orderedCols.Count; i++)
                        {
                            worksheet.Cells[r, orderedCols[i].ColumnIndex].Value = null;
                        }
                    }
                }
            }
        }
        else
        {
            // 3. CHẾ ĐỘ TUẦN TỰ (Sequential Fill): Giữ nguyên logic cũ khi không có nhãn pre-filled
            if (totalRowIndex != -1)
            {
                // Xóa dữ liệu cũ chỉ trong vùng dữ liệu (không xóa dòng Tổng và các dòng bên dưới)
                worksheet.Cells[dataStartRow, startColumnIndex, totalRowIndex - 1, maxColIdx].Value = null;
                
                // Dịch chuyển dòng Tổng xuống nếu số dòng dữ liệu thực tế nhiều hơn số dòng trống sẵn có
                int availableSpace = totalRowIndex - dataStartRow;
                if (orderedTable.Rows.Count > availableSpace)
                {
                    int rowsToInsert = orderedTable.Rows.Count - availableSpace;
                    worksheet.InsertRow(totalRowIndex, rowsToInsert, copyStylesFromRow: dataStartRow);
                    totalRowIndex += rowsToInsert; // Cập nhật vị trí mới của dòng Tổng
                }
            }
            else
            {
                // Nếu không tìm thấy dòng Tổng, xóa đến hết sheet như cũ
                if (worksheet.Dimension != null && worksheet.Dimension.End.Row >= dataStartRow)
                {
                    worksheet.Cells[dataStartRow, startColumnIndex, worksheet.Dimension.End.Row, maxColIdx].Value = null;
                }
            }
            
            if (orderedTable.Rows.Count == 0) return;
            
            // Load dữ liệu hiệu năng cao bằng LoadFromDataTable
            var dataRange = worksheet.Cells[dataStartRow, startColumnIndex];
            dataRange.LoadFromDataTable(orderedTable, PrintHeaders: false);
            
            // Áp dụng căn lề và định dạng cho từng cột
            for (int i = 0; i < orderedCols.Count; i++)
            {
                var col = orderedCols[i];
                int physicalColIdx = col.ColumnIndex;
                var colRange = worksheet.Cells[dataStartRow, physicalColIdx, dataStartRow + orderedTable.Rows.Count - 1, physicalColIdx];
                
                // Tự động nhận diện cột số để căn lề phải
                bool isNumeric = true;
                bool hasData = false;
                for (int r = 0; r < Math.Min(50, orderedTable.Rows.Count); r++)
                {
                    var val = orderedTable.Rows[r][col.UniqueKey]?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(val)) continue;
                    hasData = true;
                    if (!double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                    {
                        isNumeric = false;
                        break;
                    }
                }
                
                if (hasData && isNumeric)
                {
                    colRange.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
                }
                
                // Định dạng ngày tháng
                bool isDate = col.ChildHeader.Contains("Ngay", StringComparison.OrdinalIgnoreCase) || 
                              col.ChildHeader.Contains("Date", StringComparison.OrdinalIgnoreCase) ||
                              col.ChildHeader.Contains("Time", StringComparison.OrdinalIgnoreCase);
                if (isDate)
                {
                    colRange.Style.Numberformat.Format = "dd/MM/yyyy";
                    colRange.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                }
            }
        }
    }

    // Điền dữ liệu metadata (thông tin chung ở đầu trang)
    public static void FillMetadataCells(ExcelWorksheet worksheet, List<MetadataCell> metadataCells, Dictionary<string, string> metadataValues)
    {
        if (metadataCells == null || metadataCells.Count == 0 || metadataValues == null || metadataValues.Count == 0)
            return;

        foreach (var cell in metadataCells)
        {
            // So khớp mềm không phân biệt hoa thường và khoảng trắng
            var match = metadataValues.FirstOrDefault(kv => 
                string.Equals(kv.Key.Trim(), cell.Label.Trim(), StringComparison.OrdinalIgnoreCase) ||
                cell.Label.Trim().Contains(kv.Key.Trim(), StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Trim().Contains(cell.Label.Trim(), StringComparison.OrdinalIgnoreCase)
            );

            if (match.Value != null)
            {
                var valCell = worksheet.Cells[cell.ValueRow, cell.ValueCol];
                var value = match.Value;

                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double num))
                {
                    valCell.Value = num;
                }
                else if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                {
                    valCell.Value = dt;
                    valCell.Style.Numberformat.Format = "dd/MM/yyyy";
                }
                else
                {
                    valCell.Value = value;
                }
            }
        }
    }

    // So khớp nhãn hàng dọc thông minh
    private static bool IsRowMatch(object? dbValue, string excelLabel)
    {
        if (dbValue == null || dbValue == DBNull.Value) return false;
        string labelClean = excelLabel.Trim();
        if (string.IsNullOrEmpty(labelClean)) return false;

        // 1. Nếu dbValue đã là DateTime
        if (dbValue is DateTime dt)
        {
            // Thử parse nhãn excel theo các định dạng thông dụng
            if (DateTime.TryParseExact(labelClean, new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "MM/dd/yyyy" }, 
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime excelDate))
            {
                return dt.Date == excelDate.Date;
            }
            // Dự phòng so sánh chuỗi định dạng ngày
            return dt.ToString("dd/MM/yyyy") == labelClean || dt.ToString("yyyy-MM-dd") == labelClean;
        }

        // 2. Nếu là chuỗi
        string dbStr = dbValue.ToString()?.Trim() ?? "";
        if (string.Equals(dbStr, labelClean, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Thử parse cả hai về DateTime để so khớp ngày
        bool dbParsed = DateTime.TryParse(dbStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dbDate) ||
                        DateTime.TryParseExact(dbStr, new[] { "dd/MM/yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out dbDate);
        bool excelParsed = DateTime.TryParseExact(labelClean, new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "MM/dd/yyyy" }, 
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime excelDate2) ||
                           DateTime.TryParse(labelClean, CultureInfo.InvariantCulture, DateTimeStyles.None, out excelDate2);

        if (dbParsed && excelParsed)
        {
            return dbDate.Date == excelDate2.Date;
        }

        return false;
    }
}
