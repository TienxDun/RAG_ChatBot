using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Backend.Services.Excel;

public interface IExcelTemplateAnalyzer
{
    string GetCellValue(ExcelWorksheet worksheet, int row, int col);
    TemplateAnalysisResult AnalyzeTemplate(ExcelWorksheet worksheet);
}

public class ExcelTemplateAnalyzer : IExcelTemplateAnalyzer
{
    private readonly ITextUtility _textUtility;

    private static readonly HashSet<string> IgnoredMetadataKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Code",
        "Revision",
        "Issueddate",
        "Version",
        "Form"
    };

    public ExcelTemplateAnalyzer(ITextUtility textUtility)
    {
        _textUtility = textUtility;
    }

    // Đọc giá trị của ô, xử lý trường hợp ô nằm trong vùng gộp (Merged Cells)
    public string GetCellValue(ExcelWorksheet worksheet, int row, int col)
    {
        if (row <= 0 || col <= 0) return string.Empty;
        
        // Tìm xem ô này có thuộc vùng gộp nào không
        var mergedRangeAddress = worksheet.MergedCells[row, col];
        if (string.IsNullOrEmpty(mergedRangeAddress))
        {
            return worksheet.Cells[row, col].Text?.Trim() ?? string.Empty;
        }

        // Lấy ô góc trên bên trái của vùng gộp
        var range = worksheet.Cells[mergedRangeAddress];
        return worksheet.Cells[range.Start.Row, range.Start.Column].Text?.Trim() ?? string.Empty;
    }

    // Phân tích template Excel
    public TemplateAnalysisResult AnalyzeTemplate(ExcelWorksheet worksheet)
    {
        var result = new TemplateAnalysisResult();
        int totalRows = worksheet.Dimension?.Rows ?? 0;
        int totalCols = worksheet.Dimension?.Columns ?? 0;

        if (totalRows == 0 || totalCols == 0)
        {
            return result;
        }

        // Bước 1: Phát hiện HeaderRowIndex và TemplateType
        int headerRowIndex = 1;
        bool isHierarchical = false;
        
        // Quét các dòng từ 1 đến tối đa 30 để tìm dòng tiêu đề
        int maxScanRows = Math.Min(totalRows, 30);
        int maxHeaderCandidateCols = 0;
        
        for (int r = 1; r <= maxScanRows; r++)
        {
            int nonEmtpyCount = 0;
            int boldCount = 0;
            bool containsHeaderKeywords = false;
            
            for (int c = 1; c <= totalCols; c++)
            {
                string val = worksheet.Cells[r, c].Text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(val))
                {
                    nonEmtpyCount++;
                    if (worksheet.Cells[r, c].Style.Font.Bold)
                    {
                        boldCount++;
                    }
                    
                    if (val.Contains("ngày", StringComparison.OrdinalIgnoreCase) || 
                        val.Contains("mã", StringComparison.OrdinalIgnoreCase) || 
                        val.Contains("tên", StringComparison.OrdinalIgnoreCase) || 
                        val.Contains("số lượng", StringComparison.OrdinalIgnoreCase) || 
                        val.Contains("sl", StringComparison.OrdinalIgnoreCase) || 
                        val.Contains("line", StringComparison.OrdinalIgnoreCase) || 
                        val.Contains("style", StringComparison.OrdinalIgnoreCase) ||
                        val.Contains("date", StringComparison.OrdinalIgnoreCase) ||
                        val.Contains("qty", StringComparison.OrdinalIgnoreCase))
                    {
                        containsHeaderKeywords = true;
                    }
                }
            }

            // Nếu dòng có nhiều ô chữ và có in đậm hoặc từ khóa tiêu đề
            if (nonEmtpyCount >= 2 && (boldCount > 0 || containsHeaderKeywords))
            {
                if (nonEmtpyCount > maxHeaderCandidateCols)
                {
                    maxHeaderCandidateCols = nonEmtpyCount;
                    headerRowIndex = r;
                }
            }
        }

        // 1. Kiểm tra xem dòng phía dưới dòng header có phải là dòng con (Child Header) hay không (ví dụ: dòng 6 là Parent, dòng 7 là Child)
        if (headerRowIndex < totalRows)
        {
            int childRow = headerRowIndex + 1;
            bool hasMergeInHeader = false;
            for (int c = 1; c <= totalCols; c++)
            {
                var mergedAddr = worksheet.MergedCells[headerRowIndex, c];
                if (!string.IsNullOrEmpty(mergedAddr))
                {
                    hasMergeInHeader = true;
                    break;
                }
            }

            if (hasMergeInHeader)
            {
                int childNonEmptyCount = 0;
                for (int c = 1; c <= totalCols; c++)
                {
                    if (!string.IsNullOrEmpty(worksheet.Cells[childRow, c].Text?.Trim()))
                    {
                        childNonEmptyCount++;
                    }
                }

                if (childNonEmptyCount >= 2)
                {
                    isHierarchical = true;
                    headerRowIndex = childRow; // Dòng con mới là dòng header chính chứa các child headers
                }
            }
        }

        // 2. Nếu chưa nhận diện được, kiểm tra xem dòng phía trên dòng header có phải là dòng cha (Parent Header) hay không (ví dụ: dòng 6 là Child, dòng 5 là Parent)
        if (!isHierarchical && headerRowIndex > 1)
        {
            int parentRow = headerRowIndex - 1;
            bool hasMergeInParent = false;
            
            for (int c = 1; c <= totalCols; c++)
            {
                var mergedAddr = worksheet.MergedCells[parentRow, c];
                if (!string.IsNullOrEmpty(mergedAddr))
                {
                    hasMergeInParent = true;
                    break;
                }
            }

            if (hasMergeInParent)
            {
                isHierarchical = true;
            }
        }

        result.HeaderRowIndex = headerRowIndex;
        result.Type = isHierarchical ? TemplateType.Hierarchical : TemplateType.Horizontal;

        // Bước 2: Xác định StartColumnIndex và danh sách Columns
        int startCol = 1;
        for (int c = 1; c <= totalCols; c++)
        {
            if (!string.IsNullOrEmpty(GetCellValue(worksheet, headerRowIndex, c)))
            {
                startCol = c;
                break;
            }
        }
        result.StartColumnIndex = startCol;

        // Quét các cột
        for (int c = startCol; c <= totalCols; c++)
        {
            string childHeader = GetCellValue(worksheet, headerRowIndex, c);
            
            // Dừng lại nếu gặp 3 cột trống liên tiếp trên dòng header
            if (string.IsNullOrEmpty(childHeader))
            {
                if (c + 1 <= totalCols && string.IsNullOrEmpty(GetCellValue(worksheet, headerRowIndex, c + 1)) &&
                    c + 2 <= totalCols && string.IsNullOrEmpty(GetCellValue(worksheet, headerRowIndex, c + 2)))
                {
                    break;
                }
                continue;
            }

            string parentHeader = "";
            if (isHierarchical)
            {
                parentHeader = GetCellValue(worksheet, headerRowIndex - 1, c);
                // Nếu tiêu đề cha trùng với tiêu đề con, ta bỏ qua tiêu đề cha
                if (string.Equals(parentHeader, childHeader, StringComparison.OrdinalIgnoreCase))
                {
                    parentHeader = "";
                }
            }

            var colObj = new FlattenedColumn
            {
                ColumnIndex = c,
                ChildHeader = childHeader,
                ParentHeader = parentHeader,
                UniqueKey = _textUtility.GenerateUniqueKey(parentHeader, childHeader)
            };
            result.Columns.Add(colObj);
        }

        // Bước 3: Phát hiện TotalRowIndex, DataStartRowIndex, DataEndRowIndex, và FillableRowIndexes
        result.DataStartRowIndex = headerRowIndex + 1;
        int? totalRowIndex = null;

        for (int r = result.DataStartRowIndex; r <= totalRows; r++)
        {
            bool isTotalRow = false;
            
            // 1. Kiểm tra từ khóa "Tổng", "Cộng", "Total" ở các cột đầu tiên
            for (int c = startCol; c <= Math.Min(startCol + 2, totalCols); c++)
            {
                string val = GetCellValue(worksheet, r, c);
                if (val.Contains("tổng", StringComparison.OrdinalIgnoreCase) || 
                    val.Contains("cộng", StringComparison.OrdinalIgnoreCase) || 
                    val.Contains("total", StringComparison.OrdinalIgnoreCase) ||
                    val.Contains("grand total", StringComparison.OrdinalIgnoreCase))
                {
                    isTotalRow = true;
                    break;
                }
            }

            // 2. Kiểm tra xem có ô nào chứa công thức SUM/SUBTOTAL/AVERAGE không
            if (!isTotalRow)
            {
                for (int c = 1; c <= totalCols; c++)
                {
                    var cell = worksheet.Cells[r, c];
                    if (!string.IsNullOrEmpty(cell.Formula))
                    {
                        string formula = cell.Formula.ToUpperInvariant();
                        if (formula.Contains("SUM") || formula.Contains("SUBTOTAL") || formula.Contains("AVERAGE"))
                        {
                            isTotalRow = true;
                            break;
                        }
                    }
                }
            }

            if (isTotalRow)
            {
                totalRowIndex = r;
                break;
            }
        }

        result.TotalRowIndex = totalRowIndex;
        
        if (totalRowIndex.HasValue)
        {
            result.DataEndRowIndex = totalRowIndex.Value - 1;
        }
        else
        {
            // Nếu không tìm thấy dòng tổng cộng, vùng dữ liệu kéo dài đến dòng cuối cùng có chứa dữ liệu hoặc định dạng viền (Border)
            int lastUsedRow = result.DataStartRowIndex;
            for (int r = totalRows; r >= result.DataStartRowIndex; r--)
            {
                bool isRowEmpty = true;
                for (int c = startCol; c <= totalCols; c++)
                {
                    var cell = worksheet.Cells[r, c];
                    if (!string.IsNullOrEmpty(cell.Text) ||
                        cell.Style.Border.Top.Style != OfficeOpenXml.Style.ExcelBorderStyle.None ||
                        cell.Style.Border.Bottom.Style != OfficeOpenXml.Style.ExcelBorderStyle.None ||
                        cell.Style.Border.Left.Style != OfficeOpenXml.Style.ExcelBorderStyle.None ||
                        cell.Style.Border.Right.Style != OfficeOpenXml.Style.ExcelBorderStyle.None)
                    {
                        isRowEmpty = false;
                        break;
                    }
                }
                if (!isRowEmpty)
                {
                    lastUsedRow = r;
                    break;
                }
            }
            result.DataEndRowIndex = lastUsedRow;
        }

        // Thu thập các dòng có thể điền dữ liệu (Fillable Row Indexes)
        for (int r = result.DataStartRowIndex; r <= result.DataEndRowIndex; r++)
        {
            result.FillableRowIndexes.Add(r);
        }

        // Bước 4: Nhận diện tự động từ File Template Excel đã bị loại bỏ theo yêu cầu.


        // Bước 5: Phân tích cấu trúc lưới ô (Grid Layout) để phục vụ hiển thị UI
        int maxRowsToReturn = Math.Min(totalRows, Math.Max(50, headerRowIndex + 5));
        int maxColsToReturn = totalCols; // Hiển thị đầy đủ tất cả các cột trong template Excel trên UI Workspace

        for (int r = 1; r <= maxRowsToReturn; r++)
        {
            var rowList = new List<ExcelCellDto>();
            for (int c = 1; c <= maxColsToReturn; c++)
            {
                var address = ExcelCellBase.GetAddress(r, c);
                rowList.Add(new ExcelCellDto
                {
                    Row = r,
                    Col = c,
                    Address = address,
                    Value = worksheet.Cells[r, c].Text?.Trim() ?? string.Empty,
                    IsBold = worksheet.Cells[r, c].Style.Font.Bold,
                    IsMerged = false,
                    RowSpan = 1,
                    ColSpan = 1,
                    IsMergedChild = false
                });
            }
            result.Grid.Add(rowList);
        }

        // Xử lý thông tin ô gộp (Merged Cells) cho vùng Grid
        if (worksheet.MergedCells != null)
        {
            foreach (var mergedAddress in worksheet.MergedCells)
            {
                var addr = new ExcelAddress(mergedAddress);
                int mStartRow = addr.Start.Row;
                int mStartCol = addr.Start.Column;
                int mEndRow = addr.End.Row;
                int mEndCol = addr.End.Column;

                if (mStartRow <= maxRowsToReturn && mStartCol <= maxColsToReturn)
                {
                    var mainCell = result.Grid[mStartRow - 1][mStartCol - 1];
                    mainCell.IsMerged = true;
                    mainCell.MergedRange = mergedAddress;
                    mainCell.RowSpan = Math.Min(mEndRow, maxRowsToReturn) - mStartRow + 1;
                    mainCell.ColSpan = Math.Min(mEndCol, maxColsToReturn) - mStartCol + 1;

                    for (int r = mStartRow; r <= Math.Min(mEndRow, maxRowsToReturn); r++)
                    {
                        for (int c = mStartCol; c <= Math.Min(mEndCol, maxColsToReturn); c++)
                        {
                            if (r == mStartRow && c == mStartCol) continue;
                            
                            var childCell = result.Grid[r - 1][c - 1];
                            childCell.IsMergedChild = true;
                            childCell.MergedRange = mergedAddress;
                        }
                    }
                }
            }
        }

        return result;
    }
}
