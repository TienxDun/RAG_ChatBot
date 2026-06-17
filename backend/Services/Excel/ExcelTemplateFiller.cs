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
        bool isExplicitFillableOnly = false,
        ExcelTemplateSubtotalConfig? subtotalConfig = null,
        Dictionary<string, string>? columnFormats = null,
        Dictionary<string, string>? metadataCellMappings = null);
    void FillHierarchicalTemplate(
        ExcelWorksheet worksheet,
        DataTable data,
        int headerRowIndex,
        int startColumnIndex,
        List<FlattenedColumn> columns,
        List<string>? rowLabels = null,
        List<int>? fillableRowIndexes = null,
        int? totalRowIndex = null,
        bool isExplicitFillableOnly = false,
        ExcelTemplateSubtotalConfig? subtotalConfig = null,
        Dictionary<string, string>? columnFormats = null,
        Dictionary<string, string>? metadataCellMappings = null);
    void FillMetadata(
        ExcelWorksheet worksheet, 
        int headerRowIndex, 
        Dictionary<string, string> metadataValues,
        Dictionary<string, string>? metadataCellMappings = null);
}

public class ColumnMapping
{
    public string DataTableColumnName { get; set; } = string.Empty;
    public int ExcelColumnIndex { get; set; }
}

public class ExcelTemplateFiller : IExcelTemplateFiller
{
    private readonly ITextUtility _textUtility;

    public ExcelTemplateFiller(ITextUtility textUtility)
    {
        _textUtility = textUtility;
    }

    // Chuyển đổi JSON sang DataTable case-insensitive theo các cột của template
    public DataTable ConvertJsonToDataTable(string jsonString, List<string> templateColumns)
    {
        return ExcelDataHelper.ConvertJsonToDataTable(jsonString, templateColumns);
    }

    // Ghi một dòng dữ liệu vào Excel sheet
    private void WriteRowData(ExcelWorksheet worksheet, DataRow dataRow, int excelRowIndex, List<ColumnMapping> mappings, Dictionary<string, string>? columnFormats)
    {
        double maxRowHeight = worksheet.Row(excelRowIndex).Height;
        if (maxRowHeight <= 0) maxRowHeight = 20;

        var numericCols = GetNumericColumns(dataRow.Table, columnFormats);

        foreach (var map in mappings)
        {
            var cell = worksheet.Cells[excelRowIndex, map.ExcelColumnIndex];
            var val = dataRow[map.DataTableColumnName];
            bool isNumericCol = numericCols.TryGetValue(map.DataTableColumnName, out var colFormat);

            if (val == null || val == DBNull.Value)
            {
                cell.Value = null;
                continue;
            }

            if (val is DateTime dt)
            {
                string? customFormat = null;
                columnFormats?.TryGetValue(map.DataTableColumnName, out customFormat);
                WriteDateTimeCell(cell, dt, customFormat);
            }
            else if (isNumericCol)
            {
                double dVal;
                if (val is double || val is float || val is decimal || val is int || val is long || val is short || val is byte)
                {
                    dVal = Convert.ToDouble(val);
                }
                else
                {
                    dVal = double.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0;
                }
                WriteNumericCell(cell, dVal, colFormat);
            }
            else
            {
                string strVal = val.ToString() ?? "";
                bool isExplicitText = columnFormats != null && columnFormats.TryGetValue(map.DataTableColumnName, out var fmt) && fmt == "@";

                if (!isExplicitText && DateTime.TryParse(strVal, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dtParsed))
                {
                    string? customFormat = null;
                    columnFormats?.TryGetValue(map.DataTableColumnName, out customFormat);
                    WriteDateTimeCell(cell, dtParsed, customFormat);
                }
                else
                {
                    if (isExplicitText)
                    {
                        cell.Style.Numberformat.Format = "@";
                    }
                    WriteTextCell(cell, strVal, map.ExcelColumnIndex, ref maxRowHeight, worksheet);
                }
            }
        }

        worksheet.Row(excelRowIndex).Height = maxRowHeight;
    }

    private void WriteDateTimeCell(ExcelRange cell, DateTime dt, string? customFormat)
    {
        cell.Value = dt;
        cell.Style.Numberformat.Format = !string.IsNullOrWhiteSpace(customFormat) ? customFormat : "dd/MM/yyyy";
        cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
    }

    private void WriteNumericCell(ExcelRange cell, double dVal, string? colFormat)
    {
        cell.Value = dVal;
        if (colFormat != null && colFormat.Contains(".#") && dVal == Math.Truncate(dVal))
        {
            cell.Style.Numberformat.Format = colFormat.Replace(".##", "").Replace(".#", "");
        }
        else
        {
            cell.Style.Numberformat.Format = colFormat;
        }
        cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
    }

    private void WriteTextCell(ExcelRange cell, string strVal, int excelColumnIndex, ref double maxRowHeight, ExcelWorksheet worksheet)
    {
        if (strVal.Contains("|"))
        {
            var uniqueItems = strVal.Split('|')
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrEmpty(item))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            strVal = string.Join(" | ", uniqueItems);
        }
        
        cell.Value = strVal;

        string cleanVal = strVal.Trim();
        bool isFail = string.Equals(cleanVal, "Fail", StringComparison.OrdinalIgnoreCase) || 
                      string.Equals(cleanVal, "FAIL", StringComparison.OrdinalIgnoreCase) || 
                      string.Equals(cleanVal, "Fail (QC)", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(cleanVal, "Fail (Mer)", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(cleanVal, "Không đạt", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(cleanVal, "Khong dat", StringComparison.OrdinalIgnoreCase);

        bool isPass = string.Equals(cleanVal, "Pass", StringComparison.OrdinalIgnoreCase) || 
                      string.Equals(cleanVal, "PASS", StringComparison.OrdinalIgnoreCase) || 
                      string.Equals(cleanVal, "Pass (QC)", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(cleanVal, "Pass (Mer)", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(cleanVal, "Đạt", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(cleanVal, "Dat", StringComparison.OrdinalIgnoreCase);

        if (isFail)
        {
            cell.Style.Font.Bold = true;
            cell.Style.Font.Color.SetColor(System.Drawing.Color.Red);
        }
        else if (isPass)
        {
            cell.Style.Font.Bold = true;
            cell.Style.Font.Color.SetColor(System.Drawing.Color.Green);
        }
        else if (cleanVal.Contains("fail", StringComparison.OrdinalIgnoreCase) || 
                 cleanVal.Contains("lỗi", StringComparison.OrdinalIgnoreCase) ||
                 cleanVal.Contains("không đạt", StringComparison.OrdinalIgnoreCase))
        {
            cell.Style.Font.Color.SetColor(System.Drawing.Color.FromArgb(192, 0, 0));
        }

        if (strVal.Length > 20 || strVal.Contains("|"))
        {
            cell.Style.WrapText = true;
            double colWidth = worksheet.Column(excelColumnIndex).Width;
            if (colWidth <= 0) colWidth = 15;

            int charsPerLine = Math.Max(5, (int)(colWidth * 0.9));
            int lineCount = (int)Math.Ceiling((double)strVal.Length / charsPerLine);

            double estimatedHeight = lineCount * 16 + 8;
            if (estimatedHeight > maxRowHeight)
            {
                maxRowHeight = estimatedHeight;
            }
        }
    }

    private void EnsureRowBorders(ExcelWorksheet worksheet, int rowIndex, List<ColumnMapping> mappings)
    {
        if (mappings == null || mappings.Count == 0) return;
        int startCol = mappings.Min(m => m.ExcelColumnIndex);
        int endCol = mappings.Max(m => m.ExcelColumnIndex);
        for (int col = startCol; col <= endCol; col++)
        {
            var cell = worksheet.Cells[rowIndex, col];
            cell.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            cell.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            cell.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            cell.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
        }
    }

    private bool IsValidSizeLoiKiemFormat(string? valStr)
    {
        return ExcelDataHelper.IsValidSizeLoiKiemFormat(valStr);
    }

    private void FillTemplateWithSubtotals(
        ExcelWorksheet worksheet,
        DataTable data,
        int dataStartRowIndex,
        List<ColumnMapping> mappings,
        ExcelTemplateSubtotalConfig subtotalConfig,
        int? totalRowIndex,
        Dictionary<string, string>? columnFormats = null,
        Dictionary<string, string>? metadataCellMappings = null)
    {
        if (data == null || data.Rows.Count == 0 || mappings.Count == 0) return;

        int startCol = mappings.Min(m => m.ExcelColumnIndex);
        int endCol = mappings.Max(m => m.ExcelColumnIndex);

        // 1. Tìm cột gom nhóm trong mappings
        var groupByMapping = mappings.FirstOrDefault(m => 
            m.DataTableColumnName.Equals(subtotalConfig.GroupByColumn, StringComparison.OrdinalIgnoreCase));
            
        if (groupByMapping == null)
        {
            // Fallback nếu không cấu hình đúng cột group by
            FillTemplateCore(worksheet, data, dataStartRowIndex, mappings, null, null, totalRowIndex, false, columnFormats);
            return;
        }

        // 2. Gom nhóm dữ liệu theo cột GroupByColumn
        var groups = new List<(string Key, List<DataRow> Rows)>();
        string currentGroupKey = null!;
        List<DataRow> currentGroupRows = null!;

        foreach (DataRow row in data.Rows)
        {
            var valObj = row[groupByMapping.DataTableColumnName];
            string groupVal = "";
            if (valObj is DateTime dt)
            {
                groupVal = dt.ToString("dd/MM/yyyy");
            }
            else if (valObj != null && valObj != DBNull.Value)
            {
                groupVal = valObj.ToString()?.Trim() ?? "";
                if (DateTime.TryParse(groupVal, out DateTime dtParsed))
                {
                    groupVal = dtParsed.ToString("dd/MM/yyyy");
                }
            }

            if (currentGroupKey == null || !currentGroupKey.Equals(groupVal, StringComparison.OrdinalIgnoreCase))
            {
                if (currentGroupKey != null)
                {
                    groups.Add((currentGroupKey, currentGroupRows));
                }
                currentGroupKey = groupVal;
                currentGroupRows = new List<DataRow>();
            }
            currentGroupRows.Add(row);
        }
        if (currentGroupKey != null)
        {
            groups.Add((currentGroupKey, currentGroupRows));
        }

        // 3. Tính toán tổng số dòng cần thiết (bao gồm cả dòng chi tiết và các dòng subtotal)
        int totalDetailRows = data.Rows.Count;
        int totalSubtotalRows = groups.Count;
        int requiredRows = totalDetailRows + totalSubtotalRows;

        // Xác định số lượng dòng trống có sẵn trong template
        int startRow = dataStartRowIndex;
        int endRow = totalRowIndex.HasValue ? totalRowIndex.Value - 1 : startRow + 100;
        int availableRowsCount = endRow - startRow + 1;

        // Nếu thiếu dòng trong template, chèn thêm dòng
        if (requiredRows > availableRowsCount)
        {
            int rowsToInsert = requiredRows - availableRowsCount;
            int insertAt = endRow; // Chèn tại dòng trống cuối cùng ngay trước dòng tổng lớn
            worksheet.InsertRow(insertAt, rowsToInsert);

            // Copy style từ dòng cũ bị đẩy xuống cho các dòng mới chèn
            int shiftedSourceRow = insertAt + rowsToInsert;
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
                    destCell.Value = null;
                }
            }
        }

        // 4. Bắt đầu điền dữ liệu và chèn các dòng subtotal
        int currentExcelRow = dataStartRowIndex;
        long grandTotalLoi = 0;
        long grandTotalKiem = 0;
        int totalColsCount = worksheet.Dimension?.Columns ?? 0;

        foreach (var group in groups)
        {
            int groupStartRow = currentExcelRow;

            // Điền các dòng chi tiết của nhóm
            foreach (var row in group.Rows)
            {
                WriteRowData(worksheet, row, currentExcelRow, mappings, columnFormats);
                EnsureRowBorders(worksheet, currentExcelRow, mappings);
                currentExcelRow++;
            }

            int groupEndRow = currentExcelRow - 1;

            // Điền dòng subtotal
            int subtotalRowIndex = currentExcelRow;

            // Label dòng tổng ngày
            if (!string.IsNullOrWhiteSpace(subtotalConfig.LabelColumn))
            {
                var labelMapping = mappings.FirstOrDefault(m => 
                    m.DataTableColumnName.Equals(subtotalConfig.LabelColumn, StringComparison.OrdinalIgnoreCase));
                if (labelMapping != null)
                {
                    worksheet.Cells[subtotalRowIndex, labelMapping.ExcelColumnIndex].Value = "";
                }
            }

            // Ghi nội dung cột ngày của dòng tổng (nếu muốn)
            worksheet.Cells[subtotalRowIndex, groupByMapping.ExcelColumnIndex].Value = ""; 

            // Tính toán sẵn tổng lỗi và tổng kiểm từ các cột được cấu hình trong SumColumns có dạng Size/Loi/Kiem
            long groupTotalLoi = 0;
            long groupTotalKiem = 0;
            bool hasSizeLoiKiemData = false;

            if (subtotalConfig.SumColumns != null)
            {
                foreach (var sumColName in subtotalConfig.SumColumns)
                {
                    var sumMapping = mappings.FirstOrDefault(m => 
                        m.DataTableColumnName.Equals(sumColName, StringComparison.OrdinalIgnoreCase));
                    if (sumMapping == null) continue;

                    bool isSizeLoiKiemFormat = false;
                    foreach (var r in group.Rows)
                    {
                        if (IsValidSizeLoiKiemFormat(r[sumMapping.DataTableColumnName]?.ToString()))
                        {
                            isSizeLoiKiemFormat = true;
                            break;
                        }
                    }

                    if (isSizeLoiKiemFormat)
                    {
                        hasSizeLoiKiemData = true;
                        foreach (var r in group.Rows)
                        {
                            var valStr = r[sumMapping.DataTableColumnName]?.ToString();
                            if (IsValidSizeLoiKiemFormat(valStr))
                            {
                                var parts = valStr!.Split('/');
                                if (long.TryParse(parts[parts.Length - 2].Trim(), out long loi) && 
                                    long.TryParse(parts[parts.Length - 1].Trim(), out long kiem))
                                {
                                    groupTotalLoi += loi;
                                    groupTotalKiem += kiem;
                                }
                            }
                        }
                        break;
                    }
                }
            }

            if (!hasSizeLoiKiemData)
            {
                // Thử tính trực tiếp từ cột loi và kiem nếu là các cột số riêng biệt
                double sumLoi = 0;
                double sumKiem = 0;
                var loiCol = mappings.FirstOrDefault(m => m.DataTableColumnName.Contains("Loi", StringComparison.OrdinalIgnoreCase));
                var kiemCol = mappings.FirstOrDefault(m => m.DataTableColumnName.Contains("Kiem", StringComparison.OrdinalIgnoreCase));
                if (loiCol != null && kiemCol != null)
                {
                    foreach (var r in group.Rows)
                    {
                        if (double.TryParse(r[loiCol.DataTableColumnName]?.ToString(), out double l)) sumLoi += l;
                        if (double.TryParse(r[kiemCol.DataTableColumnName]?.ToString(), out double k)) sumKiem += k;
                    }
                    groupTotalLoi = (long)sumLoi;
                    groupTotalKiem = (long)sumKiem;
                }
            }

            grandTotalLoi += groupTotalLoi;
            grandTotalKiem += groupTotalKiem;

            // Tính và điền Subtotals cho các cột được cấu hình
            foreach (var sumColName in subtotalConfig.SumColumns ?? new List<string>())
            {
                var sumMapping = mappings.FirstOrDefault(m => 
                    m.DataTableColumnName.Equals(sumColName, StringComparison.OrdinalIgnoreCase));
                if (sumMapping == null) continue;

                var sumCell = worksheet.Cells[subtotalRowIndex, sumMapping.ExcelColumnIndex];

                // Kiểm tra xem cột này có thuộc định dạng "Size/Loi/Kiem" không
                bool isSizeLoiKiemFormat = false;
                foreach (var r in group.Rows)
                {
                    if (IsValidSizeLoiKiemFormat(r[sumColName]?.ToString()))
                    {
                        isSizeLoiKiemFormat = true;
                        break;
                    }
                }

                if (isSizeLoiKiemFormat)
                {
                    sumCell.Value = $"Tổng kiểm: {groupTotalKiem} | Tổng lỗi: {groupTotalLoi}";
                    sumCell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                }
                else
                {
                    // Numeric Sum
                    double numericSum = 0;
                    bool hasNumeric = false;
                    foreach (var r in group.Rows)
                    {
                        var valObj = r[sumColName];
                        if (valObj != null && valObj != DBNull.Value && double.TryParse(valObj.ToString(), out double num))
                        {
                            numericSum += num;
                            hasNumeric = true;
                        }
                    }

                    if (hasNumeric)
                    {
                        sumCell.Value = numericSum;
                        sumCell.Style.Numberformat.Format = "#,##0";
                        sumCell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
                    }
                    else
                    {
                        sumCell.Value = null;
                    }
                }

                sumCell.Style.Font.Bold = true;
            }

            // Tính và điền Tỷ lệ lỗi cho các cột được cấu hình
            if (subtotalConfig.DefectRateColumns != null)
            {
                foreach (var rateColName in subtotalConfig.DefectRateColumns)
                {
                    var rateMapping = mappings.FirstOrDefault(m => 
                        m.DataTableColumnName.Equals(rateColName, StringComparison.OrdinalIgnoreCase));
                    if (rateMapping == null) continue;

                    var rateCell = worksheet.Cells[subtotalRowIndex, rateMapping.ExcelColumnIndex];
                    if (groupTotalKiem > 0)
                    {
                        double rateVal = (double)groupTotalLoi / groupTotalKiem * 100;
                        rateCell.Value = rateVal;
                        
                        string format = "#,##0.##";
                        if (columnFormats != null && columnFormats.TryGetValue(rateColName, out var customFmt) && !string.IsNullOrWhiteSpace(customFmt))
                        {
                            format = customFmt;
                        }

                        if (format.Contains(".#") && rateVal == Math.Truncate(rateVal))
                        {
                            rateCell.Style.Numberformat.Format = format.Replace(".##", "").Replace(".#", "");
                        }
                        else
                        {
                            rateCell.Style.Numberformat.Format = format;
                        }
                    }
                    else
                    {
                        rateCell.Value = null;
                    }
                    rateCell.Style.Font.Bold = true;
                    rateCell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
                }
            }

            // Định dạng font Bold và viền đầy đủ cho dòng Subtotal
            worksheet.Row(subtotalRowIndex).Style.Font.Bold = true;
            for (int col = startCol; col <= endCol; col++)
            {
                var cell = worksheet.Cells[subtotalRowIndex, col];
                cell.Style.Font.Bold = true;
                cell.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            }

            // Dynamic Cell Merging for this group (if configured)
            if (subtotalConfig.MergeColumns != null && subtotalConfig.MergeColumns.Count > 0)
            {
                foreach (var mergeColName in subtotalConfig.MergeColumns)
                {
                    int colIdx = -1;
                    var mergeMapping = mappings.FirstOrDefault(m => 
                        m.DataTableColumnName.Equals(mergeColName, StringComparison.OrdinalIgnoreCase));
                    
                    if (mergeMapping != null)
                    {
                        colIdx = mergeMapping.ExcelColumnIndex;
                    }
                    else
                    {
                        // Cột tĩnh không có trong data DB, quét tiêu đề của Excel
                        int headerRowIndex = dataStartRowIndex - 1;
                        for (int c = 1; c <= totalColsCount; c++)
                        {
                            string headerText = worksheet.Cells[headerRowIndex, c].Text?.Trim() ?? "";
                            string cleanHeader = _textUtility.RemoveDiacritics(headerText).Replace(" ", "").Replace("_", "").ToLower();
                            string cleanMergeName = _textUtility.RemoveDiacritics(mergeColName).Replace(" ", "").Replace("_", "").ToLower();
                            if (cleanHeader == cleanMergeName || cleanHeader.Contains(cleanMergeName) || cleanMergeName.Contains(cleanHeader))
                            {
                                colIdx = c;
                                break;
                            }
                        }
                    }

                    if (colIdx == -1) continue;

                    // If column is also in DefectRateColumns
                    bool isDefectRateCol = subtotalConfig.DefectRateColumns != null && 
                                           subtotalConfig.DefectRateColumns.Any(c => c.Equals(mergeColName, StringComparison.OrdinalIgnoreCase));

                    bool shouldMergeToSubtotal = subtotalConfig.MergeToSubtotalColumns != null && 
                                                 subtotalConfig.MergeToSubtotalColumns.Any(c => c.Equals(mergeColName, StringComparison.OrdinalIgnoreCase));

                    if (shouldMergeToSubtotal)
                    {
                        if (isDefectRateCol)
                        {
                            double? rateVal = null;
                            if (groupTotalKiem > 0)
                            {
                                rateVal = (double)groupTotalLoi / groupTotalKiem * 100;
                            }

                            // Write computed rate to the cell at groupStartRow
                            var startCell = worksheet.Cells[groupStartRow, colIdx];
                            if (rateVal.HasValue)
                            {
                                double rVal = rateVal.Value;
                                startCell.Value = rVal;
                                
                                string format = "#,##0.##";
                                if (columnFormats != null && columnFormats.TryGetValue(mergeColName, out var customFmt) && !string.IsNullOrWhiteSpace(customFmt))
                                {
                                    format = customFmt;
                                }

                                if (format.Contains(".#") && rVal == Math.Truncate(rVal))
                                {
                                    startCell.Style.Numberformat.Format = format.Replace(".##", "").Replace(".#", "");
                                }
                                else
                                {
                                    startCell.Style.Numberformat.Format = format;
                                }
                            }
                            else
                            {
                                startCell.Value = null;
                            }
                        }

                        // Clear cell at subtotalRowIndex
                        var subtotalCell = worksheet.Cells[subtotalRowIndex, colIdx];
                        subtotalCell.Value = null;

                        // Merge range [groupStartRow, colIdx, subtotalRowIndex, colIdx]
                        if (subtotalRowIndex > groupStartRow)
                        {
                            var mergeRange = worksheet.Cells[groupStartRow, colIdx, subtotalRowIndex, colIdx];
                            mergeRange.Merge = true;
                            mergeRange.Style.VerticalAlignment = OfficeOpenXml.Style.ExcelVerticalAlignment.Center;
                            mergeRange.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                        }
                        else
                        {
                            var startCell = worksheet.Cells[groupStartRow, colIdx];
                            startCell.Style.VerticalAlignment = OfficeOpenXml.Style.ExcelVerticalAlignment.Center;
                            startCell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                        }
                    }
                    else
                    {
                        // Merge range [groupStartRow, colIdx, groupEndRow, colIdx]
                        if (groupEndRow > groupStartRow)
                        {
                            var mergeRange = worksheet.Cells[groupStartRow, colIdx, groupEndRow, colIdx];
                            mergeRange.Merge = true;
                            mergeRange.Style.VerticalAlignment = OfficeOpenXml.Style.ExcelVerticalAlignment.Center;
                            mergeRange.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                        }
                        else
                        {
                            var singleCell = worksheet.Cells[groupStartRow, colIdx];
                            singleCell.Style.VerticalAlignment = OfficeOpenXml.Style.ExcelVerticalAlignment.Center;
                            singleCell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                        }
                    }
                }
            }

            currentExcelRow++;
        }

        // 5. Xóa bớt dòng trống dư thừa
        if (requiredRows < availableRowsCount)
        {
            int startDeleteRow = currentExcelRow;
            int rowsToDelete = availableRowsCount - requiredRows;
            worksheet.DeleteRow(startDeleteRow, rowsToDelete);
        }

        // 6. Điền thông tin dòng Tổng toàn bảng (Grand Total)
        if (totalRowIndex.HasValue)
        {
            int grandTotalRowIndex = currentExcelRow;

            // Tính và điền Grand Totals cho các cột được cấu hình trong SumColumns
            foreach (var sumColName in subtotalConfig.SumColumns ?? new List<string>())
            {
                var sumMapping = mappings.FirstOrDefault(m => 
                    m.DataTableColumnName.Equals(sumColName, StringComparison.OrdinalIgnoreCase));
                if (sumMapping == null) continue;

                var sumCell = worksheet.Cells[grandTotalRowIndex, sumMapping.ExcelColumnIndex];

                // Kiểm tra định dạng Size/Loi/Kiem trong dữ liệu gốc
                bool isSizeLoiKiemFormat = false;
                foreach (DataRow r in data.Rows)
                {
                    if (IsValidSizeLoiKiemFormat(r[sumColName]?.ToString()))
                    {
                        isSizeLoiKiemFormat = true;
                        break;
                    }
                }

                if (isSizeLoiKiemFormat)
                {
                    sumCell.Value = $"Tổng kiểm: {grandTotalKiem} | Tổng lỗi: {grandTotalLoi}";
                    sumCell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                }
                sumCell.Style.Font.Bold = true;
            }

            // Tính và điền Tỷ lệ lỗi cho dòng tổng
            if (subtotalConfig.DefectRateColumns != null)
            {
                foreach (var rateColName in subtotalConfig.DefectRateColumns)
                {
                    var rateMapping = mappings.FirstOrDefault(m => 
                        m.DataTableColumnName.Equals(rateColName, StringComparison.OrdinalIgnoreCase));
                    if (rateMapping == null) continue;

                    var rateCell = worksheet.Cells[grandTotalRowIndex, rateMapping.ExcelColumnIndex];
                    if (grandTotalKiem > 0)
                    {
                        double rateVal = (double)grandTotalLoi / grandTotalKiem * 100;
                        rateCell.Value = rateVal;
                        
                        string format = "#,##0.##";
                        if (columnFormats != null && columnFormats.TryGetValue(rateColName, out var customFmt) && !string.IsNullOrWhiteSpace(customFmt))
                        {
                            format = customFmt;
                        }

                        if (format.Contains(".#") && rateVal == Math.Truncate(rateVal))
                        {
                            rateCell.Style.Numberformat.Format = format.Replace(".##", "").Replace(".#", "");
                        }
                        else
                        {
                            rateCell.Style.Numberformat.Format = format;
                        }
                    }
                    else
                    {
                        rateCell.Value = null;
                    }
                    rateCell.Style.Font.Bold = true;
                    rateCell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
                }
            }

            // Định dạng font Bold và viền đầy đủ cho dòng Grand Total
            worksheet.Row(grandTotalRowIndex).Style.Font.Bold = true;
            for (int col = startCol; col <= endCol; col++)
            {
                var cell = worksheet.Cells[grandTotalRowIndex, col];
                cell.Style.Font.Bold = true;
                cell.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            }

            // Đảm bảo độ rộng tối thiểu cho cột tỉ lệ %, text, ngày và tối ưu ô metadata
            OptimizeExcelLayout(worksheet, mappings, columnFormats, metadataCellMappings, data);
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
        bool isExplicitFillableOnly = false,
        ExcelTemplateSubtotalConfig? subtotalConfig = null,
        Dictionary<string, string>? columnFormats = null,
        Dictionary<string, string>? metadataCellMappings = null)
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

        if (subtotalConfig != null && (rowLabels == null || rowLabels.Count == 0))
        {
            FillTemplateWithSubtotals(worksheet, data, dataStartRowIndex, mappings, subtotalConfig, totalRowIndex, columnFormats, metadataCellMappings);
            return;
        }

        FillTemplateCore(
            worksheet,
            data,
            dataStartRowIndex,
            mappings,
            rowLabels,
            fillableRowIndexes,
            totalRowIndex,
            isExplicitFillableOnly,
            columnFormats,
            metadataCellMappings);
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
        bool isExplicitFillableOnly = false,
        ExcelTemplateSubtotalConfig? subtotalConfig = null,
        Dictionary<string, string>? columnFormats = null,
        Dictionary<string, string>? metadataCellMappings = null)
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

        if (subtotalConfig != null && (rowLabels == null || rowLabels.Count == 0))
        {
            FillTemplateWithSubtotals(worksheet, data, dataStartRowIndex, mappings, subtotalConfig, totalRowIndex, columnFormats, metadataCellMappings);
            return;
        }

        FillTemplateCore(
            worksheet,
            data,
            dataStartRowIndex,
            mappings,
            rowLabels,
            fillableRowIndexes,
            totalRowIndex,
            isExplicitFillableOnly,
            columnFormats,
            metadataCellMappings);
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
        bool isExplicitFillableOnly = false,
        Dictionary<string, string>? columnFormats = null,
        Dictionary<string, string>? metadataCellMappings = null)
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
                        WriteRowData(worksheet, dr, targetExcelRow, mappings, columnFormats);
                        EnsureRowBorders(worksheet, targetExcelRow, mappings);
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
                WriteRowData(worksheet, data.Rows[i], targetRows[i], mappings, columnFormats);
                EnsureRowBorders(worksheet, targetRows[i], mappings);
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
                    WriteRowData(worksheet, data.Rows[i], targetRows[i], mappings, columnFormats);
                    EnsureRowBorders(worksheet, targetRows[i], mappings);
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
                    WriteRowData(worksheet, data.Rows[i], targetRows[i], mappings, columnFormats);
                    EnsureRowBorders(worksheet, targetRows[i], mappings);
                }

                // Đảm bảo độ rộng tối thiểu cho cột tỉ lệ %, text, ngày và tối ưu ô metadata
                OptimizeExcelLayout(worksheet, mappings, columnFormats, metadataCellMappings, data);
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

            var cell = worksheet.Cells[cellAddress];
            if (cell?.Start == null) continue;

            string? matchedValue = _textUtility.FindBestMetadataValue(metadataValues, semanticKey);
            if (matchedValue != null)
            {
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

    private void OptimizeExcelLayout(
        ExcelWorksheet worksheet,
        List<ColumnMapping> mappings,
        Dictionary<string, string>? columnFormats,
        Dictionary<string, string>? metadataCellMappings,
        DataTable dataTable)
    {
        ExcelLayoutOptimizer.OptimizeExcelLayout(worksheet, mappings, columnFormats, metadataCellMappings, dataTable);
    }

    private Dictionary<string, string> GetNumericColumns(DataTable table, Dictionary<string, string>? columnFormats)
    {
        return ExcelDataHelper.GetNumericColumns(table, columnFormats);
    }
}
