using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Backend.Services.Excel;

public enum TemplateType
{
    Simple,
    Hierarchical
}

public class TemplateAnalysisResult
{
    public TemplateType Type { get; set; } = TemplateType.Simple;
    public int HeaderRowIndex { get; set; }  // Dòng header chính (con)
    public int? ParentHeaderRowIndex { get; set; }  // Dòng header cha (nếu có)
    public int DataStartRowIndex { get; set; }  // Dòng bắt đầu dữ liệu
    public List<FlattenedColumn> Columns { get; set; } = new();  // Danh sách cột đã "làm phẳng"
    public List<MetadataCell> MetadataCells { get; set; } = new();  // Các ô metadata (Mã Hàng, Chuyền...)
    public List<string> RowLabels { get; set; } = new(); // Danh sách nhãn hàng pre-filled ở cột đầu tiên (ví dụ: ngày 23/05, 22/05...)
    public int StartColumnIndex { get; set; }
}

public class FlattenedColumn
{
    public int ColumnIndex { get; set; }  // Vị trí vật lý trên Excel (1-based)
    public string ParentHeader { get; set; } = string.Empty;  // Tên header cha (VD: "Thành Phẩm")
    public string ChildHeader { get; set; } = string.Empty;  // Tên header con (VD: "SL. kiểm")
    public string UniqueKey { get; set; } = string.Empty;  // Khóa độc nhất = "ThànhPhẩm_SL.kiểm"
    public string FriendlyName { get; set; } = string.Empty;  // Tên hiển thị thân thiện cho AI
}

public class MetadataCell
{
    public string Label { get; set; } = string.Empty;  // VD: "Mã Hàng/Style"
    public int LabelRow { get; set; }
    public int LabelCol { get; set; }
    public int ValueRow { get; set; }
    public int ValueCol { get; set; }
}

public static class ExcelTemplateAnalyzer
{
    /// <summary>
    /// Analyzes an Excel worksheet to detect headers, hierarchical structures, and metadata cells.
    /// </summary>
    public static TemplateAnalysisResult Analyze(ExcelWorksheet worksheet)
    {
        var result = new TemplateAnalysisResult();

        if (worksheet.Dimension == null)
        {
            throw new ArgumentException("Worksheet has no dimension or data.");
        }

        // --- Step 1: Detect Header Row ---
        // Porting the existing logic from ExcelReportService to find the row with the most non-empty columns.
        int maxHeaderCols = 0;
        int headerRowIndex = 1;
        var headerColsList = new List<int>();

        int maxColsToScan = Math.Min(worksheet.Dimension.End.Column, 50);
        int maxRowsToScan = Math.Min(worksheet.Dimension.End.Row, 15);

        for (int r = 1; r <= maxRowsToScan; r++)
        {
            var tempColumnIndices = new List<int>();
            for (int c = 1; c <= maxColsToScan; c++)
            {
                var val = GetCellTextHeaderScan(worksheet, r, c);
                if (!string.IsNullOrWhiteSpace(val))
                {
                    tempColumnIndices.Add(c);
                }
            }

            if (tempColumnIndices.Count > maxHeaderCols)
            {
                maxHeaderCols = tempColumnIndices.Count;
                headerColsList = tempColumnIndices;
                headerRowIndex = r;
            }
        }

        if (headerColsList.Count == 0)
        {
            throw new InvalidOperationException("Could not find any header columns in the first 15 rows of the template.");
        }

        result.HeaderRowIndex = headerRowIndex;
        result.StartColumnIndex = headerColsList[0];

        // --- Step 2: Check for Merged Cells (Hierarchical Template detection) ---
        bool hasParentHeader = false;
        int? parentHeaderRowIndex = null;

        if (headerRowIndex > 1)
        {
            int potentialParentRow = headerRowIndex - 1;
            // Check if there are merged cells on the row above the main header row
            foreach (var colIdx in headerColsList)
            {
                var mergedRange = GetMergedRange(worksheet, potentialParentRow, colIdx);
                if (mergedRange != null)
                {
                    hasParentHeader = true;
                    parentHeaderRowIndex = potentialParentRow;
                    break;
                }
                
                // If there's any non-empty cell in the row above that spans across columns, or is a header grouping
                var cellVal = worksheet.Cells[potentialParentRow, colIdx].Text?.Trim();
                if (!string.IsNullOrWhiteSpace(cellVal))
                {
                    hasParentHeader = true;
                    parentHeaderRowIndex = potentialParentRow;
                    break;
                }
            }
        }

        if (hasParentHeader && parentHeaderRowIndex.HasValue)
        {
            result.Type = TemplateType.Hierarchical;
            result.ParentHeaderRowIndex = parentHeaderRowIndex;
            result.DataStartRowIndex = headerRowIndex + 1;
        }
        else
        {
            result.Type = TemplateType.Simple;
            result.ParentHeaderRowIndex = null;
            result.DataStartRowIndex = headerRowIndex + 1;
        }

        // --- Step 3: Flatten Headers ---
        foreach (var colIdx in headerColsList)
        {
            var childHeader = GetCellTextResolved(worksheet, headerRowIndex, colIdx);
            var parentHeader = string.Empty;
            bool isVerticallyMerged = false;

            if (result.Type == TemplateType.Hierarchical && result.ParentHeaderRowIndex.HasValue)
            {
                int pRow = result.ParentHeaderRowIndex.Value;
                var parentMergedRange = GetMergedRange(worksheet, pRow, colIdx);

                if (parentMergedRange != null)
                {
                    parentHeader = worksheet.Cells[parentMergedRange.Start.Row, parentMergedRange.Start.Column].Text?.Trim() ?? string.Empty;
                    // Nếu ô gộp này kéo dài xuống bao gồm cả dòng tiêu đề con, tức là cột gộp dọc (như cột Ngày)
                    if (parentMergedRange.End.Row >= headerRowIndex)
                    {
                        isVerticallyMerged = true;
                    }
                }
                else
                {
                    parentHeader = worksheet.Cells[pRow, colIdx].Text?.Trim() ?? string.Empty;
                }
            }

            var col = new FlattenedColumn
            {
                ColumnIndex = colIdx,
                ParentHeader = isVerticallyMerged ? string.Empty : parentHeader,
                ChildHeader = childHeader
            };

            // Calculate UniqueKey & FriendlyName
            if (!string.IsNullOrWhiteSpace(col.ParentHeader))
            {
                // Remove spaces and special chars to make a clean UniqueKey
                var cleanParent = string.Concat(col.ParentHeader.Where(char.IsLetterOrDigit));
                var cleanChild = string.Concat(col.ChildHeader.Where(char.IsLetterOrDigit));
                col.UniqueKey = $"{cleanParent}_{cleanChild}";
                col.FriendlyName = $"{col.ParentHeader} - {col.ChildHeader}";
            }
            else
            {
                var cleanChild = string.Concat(col.ChildHeader.Where(char.IsLetterOrDigit));
                col.UniqueKey = cleanChild;
                col.FriendlyName = col.ChildHeader;
            }

            result.Columns.Add(col);
        }

        // --- Step 4: Scan Metadata Cells ---
        // Scan rows from 1 up to the header area to extract pattern like "Label: Value" or "Label:" (with Value next to it)
        int upperLimitRow = result.ParentHeaderRowIndex ?? result.HeaderRowIndex;
        for (int r = 1; r < upperLimitRow; r++)
        {
            for (int c = 1; c <= maxColsToScan; c++)
            {
                var text = worksheet.Cells[r, c].Text?.Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;

                // Support "Label: Value" in one cell
                if (text.Contains(':'))
                {
                    var parts = text.Split(new[] { ':' }, 2);
                    if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
                    {
                        var label = parts[0].Trim();
                        var value = parts[1].Trim();

                        if (!string.IsNullOrWhiteSpace(label))
                        {
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                result.MetadataCells.Add(new MetadataCell
                                {
                                    Label = label,
                                    LabelRow = r,
                                    LabelCol = c,
                                    ValueRow = r,
                                    ValueCol = c // Same cell
                                });
                            }
                            else
                            {
                                // Value is in the cell to the right of the label's cell/merged range
                                var labelMergedRange = GetMergedRange(worksheet, r, c);
                                int valueCol = labelMergedRange != null ? labelMergedRange.End.Column + 1 : c + 1;
                                var valMergedRange = GetMergedRange(worksheet, r, valueCol);
                                if (valMergedRange != null)
                                {
                                    valueCol = valMergedRange.Start.Column;
                                }

                                result.MetadataCells.Add(new MetadataCell
                                {
                                    Label = label,
                                    LabelRow = r,
                                    LabelCol = c,
                                    ValueRow = r,
                                    ValueCol = valueCol
                                });
                            }
                        }
                    }
                }
                // Support "Label:" or "Label/" in one cell with the value cell to the right (c + 1)
                else if (text.EndsWith(":") || text.EndsWith("/"))
                {
                    var label = text.TrimEnd(':', '/', ' ');
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        // Value is in the cell to the right of the label's cell/merged range
                        var labelMergedRange = GetMergedRange(worksheet, r, c);
                        int valueCol = labelMergedRange != null ? labelMergedRange.End.Column + 1 : c + 1;
                        // Resolve if the value cell is merged
                        var valMergedRange = GetMergedRange(worksheet, r, valueCol);
                        if (valMergedRange != null)
                        {
                            valueCol = valMergedRange.Start.Column;
                        }

                        result.MetadataCells.Add(new MetadataCell
                        {
                            Label = label,
                            LabelRow = r,
                            LabelCol = c,
                            ValueRow = r,
                            ValueCol = valueCol
                        });
                    }
                }
            }
        }

        // --- Step 5: Scan Pre-filled Row Labels (e.g., pre-written dates in Column A) ---
        int dataStartRow = result.HeaderRowIndex + 1;
        int totalRowIndex = -1;
        int maxCols = worksheet.Dimension.End.Column;
        int maxRows = worksheet.Dimension.End.Row;

        // Dò dòng Tổng trước dưới header
        for (int r = dataStartRow; r <= maxRows; r++)
        {
            for (int c = result.StartColumnIndex; c <= Math.Min(maxCols, result.StartColumnIndex + 3); c++)
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

        // Đọc các nhãn dòng (ví dụ các ngày được in sẵn)
        int endRowForLabels = totalRowIndex != -1 ? totalRowIndex - 1 : maxRows;
        for (int r = dataStartRow; r <= endRowForLabels; r++)
        {
            string labelText = worksheet.Cells[r, result.StartColumnIndex].Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(labelText))
            {
                if (DateTime.TryParseExact(labelText, new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "MM/dd/yyyy", "dd-MM-yyyy" }, 
                                           System.Globalization.CultureInfo.InvariantCulture, 
                                           System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                {
                    result.RowLabels.Add(parsedDate.ToString("yyyy-MM-dd"));
                }
                else
                {
                    result.RowLabels.Add(labelText);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Helper method to scan cell text during header detection.
    /// Handles horizontally merged titles so they only count as 1 column, while letting vertically merged headers resolve.
    /// </summary>
    private static string GetCellTextHeaderScan(ExcelWorksheet worksheet, int row, int col)
    {
        var merged = GetMergedRange(worksheet, row, col);
        if (merged != null)
        {
            // Nếu là gộp hàng ngang (width > 1), chỉ coi là có giá trị ở cột đầu tiên của vùng gộp
            if (merged.End.Column > merged.Start.Column)
            {
                if (col == merged.Start.Column)
                {
                    return worksheet.Cells[merged.Start.Row, merged.Start.Column].Text?.Trim() ?? string.Empty;
                }
                return string.Empty;
            }
            
            // Nếu chỉ gộp dọc (width == 1, height > 1), trả về giá trị ở ô top-left
            return worksheet.Cells[merged.Start.Row, merged.Start.Column].Text?.Trim() ?? string.Empty;
        }
        return worksheet.Cells[row, col].Text?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Helper method to get the text of a cell, resolving if it is part of a merged range.
    /// </summary>
    private static string GetCellTextResolved(ExcelWorksheet worksheet, int row, int col)
    {
        var merged = GetMergedRange(worksheet, row, col);
        if (merged != null)
        {
            return worksheet.Cells[merged.Start.Row, merged.Start.Column].Text?.Trim() ?? string.Empty;
        }
        return worksheet.Cells[row, col].Text?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Helper method to find if a cell is part of an EPPlus merged range.
    /// </summary>
    private static ExcelAddress? GetMergedRange(ExcelWorksheet worksheet, int row, int col)
    {
        foreach (var addressStr in worksheet.MergedCells)
        {
            var addr = new ExcelAddress(addressStr);
            if (row >= addr.Start.Row && row <= addr.End.Row &&
                col >= addr.Start.Column && col <= addr.End.Column)
            {
                return addr;
            }
        }
        return null;
    }
}
