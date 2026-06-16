using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Data;

namespace Backend.Services.Excel;

public static class ExcelLayoutOptimizer
{
    public static void OptimizeExcelLayout(
        ExcelWorksheet worksheet,
        List<ColumnMapping> mappings,
        Dictionary<string, string>? columnFormats,
        Dictionary<string, string>? metadataCellMappings,
        DataTable dataTable)
    {
        if (worksheet == null) return;

        // 1. Tối ưu độ rộng cho các Cột Dữ Liệu (Grid Columns)
        if (mappings != null && mappings.Count > 0)
        {
            foreach (var map in mappings)
            {
                double curWidth = worksheet.Column(map.ExcelColumnIndex).Width;
                if (curWidth <= 0) curWidth = 10; 

                double targetWidth = curWidth;

                string? format = null;
                if (columnFormats != null && columnFormats.TryGetValue(map.DataTableColumnName, out format) && !string.IsNullOrWhiteSpace(format))
                {
                    string fmtLower = format.ToLowerInvariant();
                    if (fmtLower.Contains("%"))
                    {
                        targetWidth = Math.Max(targetWidth, 10.5); // Tỷ lệ %
                    }
                    else if (fmtLower.Contains("d") || fmtLower.Contains("m") || fmtLower.Contains("y"))
                    {
                        targetWidth = Math.Max(targetWidth, 12.0); // Ngày tháng
                    }
                    else if (fmtLower.Contains("#") || fmtLower.Contains("0"))
                    {
                        targetWidth = Math.Max(targetWidth, 10.0); // Số
                    }
                }
                else
                {
                    // Cột văn bản thường: tính theo chữ dài nhất
                    int maxTextLen = 0;
                    foreach (DataRow row in dataTable.Rows)
                    {
                        var val = row[map.DataTableColumnName];
                        if (val != null && val != DBNull.Value)
                        {
                            int len = val.ToString()?.Length ?? 0;
                            if (len > maxTextLen) maxTextLen = len;
                        }
                    }
                    
                    if (maxTextLen > 0)
                    {
                        double dynamicWidth = Math.Min(maxTextLen + 3, 35);
                        targetWidth = Math.Max(targetWidth, dynamicWidth);
                    }
                }

                if (targetWidth > curWidth)
                {
                    worksheet.Column(map.ExcelColumnIndex).Width = targetWidth;
                }
            }
        }

        // 2. Tối ưu độ rộng và Gộp ô an toàn cho Ô Metadata
        if (metadataCellMappings != null && metadataCellMappings.Count > 0)
        {
            foreach (var kvp in metadataCellMappings)
            {
                // Nhận diện địa chỉ ô từ key hoặc value theo Format 1/Format 2
                string cellAddress = System.Text.RegularExpressions.Regex.IsMatch(kvp.Key.Trim(), @"^[A-Za-z]{1,3}\d{1,7}$") 
                    ? kvp.Key.Trim() 
                    : kvp.Value.Trim();

                if (string.IsNullOrWhiteSpace(cellAddress)) continue;

                var cell = worksheet.Cells[cellAddress];
                if (cell == null) continue;

                int row = cell.Start.Row;
                int col = cell.Start.Column;
                string cellText = cell.Text ?? "";
                
                if (!string.IsNullOrEmpty(cellText))
                {
                    double safetyWidth = cellText.Length + 3; // Độ rộng cần thiết

                    if (cell.Merge)
                    {
                        // Trường hợp ô đã được gộp sẵn trong template (Ví dụ: B4:D4)
                        int mergeId = worksheet.GetMergeCellId(row, col);
                        if (mergeId > 0)
                        {
                            var mergedRangeStr = worksheet.MergedCells[mergeId - 1];
                            if (!string.IsNullOrEmpty(mergedRangeStr))
                            {
                                var addr = new ExcelAddress(mergedRangeStr);
                                int startCol = addr.Start.Column;
                                int endCol = addr.End.Column;

                                double totalMergedWidth = 0;
                                for (int c = startCol; c <= endCol; c++)
                                {
                                    double w = worksheet.Column(c).Width;
                                    if (w <= 0) w = 10;
                                    totalMergedWidth += w;
                                }

                                if (safetyWidth > totalMergedWidth)
                                {
                                    // Cộng thêm phần thiếu hụt vào cột cuối cùng của ô gộp
                                    double diff = safetyWidth - totalMergedWidth;
                                    double curEndColWidth = worksheet.Column(endCol).Width;
                                    if (curEndColWidth <= 0) curEndColWidth = 10;
                                    worksheet.Column(endCol).Width = curEndColWidth + diff;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Trường hợp ô đơn lẻ (chưa gộp)
                        double curWidth = worksheet.Column(col).Width;
                        if (curWidth <= 0) curWidth = 10;

                        if (safetyWidth > curWidth)
                        {
                            int mergeExtend = 0;
                            int nextCol = col + 1;
                            int maxCol = worksheet.Dimension?.Columns ?? nextCol;
                            double totalMergedWidth = curWidth;

                            // Chạy vòng lặp kiểm tra và cộng dồn các ô trống bên phải
                            while (nextCol <= maxCol)
                            {
                                var rightCell = worksheet.Cells[row, nextCol];
                                
                                // Điều kiện: Ô bên phải tồn tại, trống hoàn toàn và chưa bị gộp
                                if (rightCell != null && 
                                    (rightCell.Value == null || string.IsNullOrWhiteSpace(rightCell.Text)) && 
                                    !rightCell.Merge)
                                {
                                    double rightWidth = worksheet.Column(nextCol).Width;
                                    if (rightWidth <= 0) rightWidth = 10;
                                    
                                    totalMergedWidth += rightWidth;
                                    mergeExtend++;

                                    if (totalMergedWidth >= safetyWidth)
                                    {
                                        break; // Đã đủ rộng sau khi gộp, dừng vòng lặp
                                    }
                                    nextCol++;
                                }
                                else
                                {
                                    break; // Gặp ô có chữ hoặc nhãn có sẵn -> dừng ngay lập tức
                                }
                            }

                            if (mergeExtend > 0)
                            {
                                // Thực hiện gộp ô an toàn (Không làm mất dữ liệu nhãn bên cạnh)
                                worksheet.Cells[row, col, row, col + mergeExtend].Merge = true;
                            }
                            else
                            {
                                // Fallback: Nếu không gộp được ô nào, bắt buộc phải tăng độ rộng cột
                                worksheet.Column(col).Width = safetyWidth;
                            }
                        }
                    }
                }
            }
        }
    }
}
