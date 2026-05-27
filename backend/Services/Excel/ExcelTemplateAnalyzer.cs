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

        // Kiểm tra xem dòng phía trên dòng header có phải là dòng cha (Parent Header) hay không
        if (headerRowIndex > 1)
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
                // Nếu là Hierarchical, dòng header chính là dòng headerRowIndex hiện tại,
                // dòng cha là parentRow.
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
            // Nếu không tìm thấy dòng tổng cộng, vùng dữ liệu kéo dài đến dòng cuối cùng có chứa dữ liệu
            int lastUsedRow = result.DataStartRowIndex;
            for (int r = totalRows; r >= result.DataStartRowIndex; r--)
            {
                bool isRowEmpty = true;
                for (int c = startCol; c <= totalCols; c++)
                {
                    if (!string.IsNullOrEmpty(worksheet.Cells[r, c].Text))
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

        // Bước 4: Phân tích Metadata ở phía trên bảng tiêu đề (bỏ qua dòng tiêu đề cha đối với template phân cấp)
        int maxMetadataRow = isHierarchical ? headerRowIndex - 2 : headerRowIndex - 1;
        for (int r = 1; r <= maxMetadataRow; r++)
        {
            for (int c = 1; c <= totalCols; c++)
            {
                string val = worksheet.Cells[r, c].Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(val)) continue;

                // Trường hợp 1: Nhãn và Giá trị nằm chung 1 ô cách nhau bởi dấu hai chấm (ví dụ: "Mã hàng: SA-893")
                if (val.Contains(":"))
                {
                    int colonIndex = val.IndexOf(':');
                    string key = val.Substring(0, colonIndex).Trim();
                    string value = val.Substring(colonIndex + 1).Trim();
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                    {
                        string cleanKey = _textUtility.RemoveDiacritics(key);
                        if (!IgnoredMetadataKeys.Contains(cleanKey) && !result.Metadata.ContainsKey(cleanKey))
                        {
                            result.Metadata[cleanKey] = value;
                        }
                    }
                }
                else
                {
                    // Trường hợp 2: Nhãn nằm ở ô này, Giá trị nằm ở ô bên phải liền kề
                    if (c + 1 <= totalCols)
                    {
                        string nextVal = worksheet.Cells[r, c + 1].Text?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(nextVal) && 
                            (val.Contains("mã", StringComparison.OrdinalIgnoreCase) || 
                             val.Contains("chuyền", StringComparison.OrdinalIgnoreCase) || 
                             val.Contains("style", StringComparison.OrdinalIgnoreCase) || 
                             val.Contains("line", StringComparison.OrdinalIgnoreCase) ||
                             val.Contains("ngày", StringComparison.OrdinalIgnoreCase) ||
                             val.Contains("tên", StringComparison.OrdinalIgnoreCase) ||
                             val.Contains("khách", StringComparison.OrdinalIgnoreCase) ||
                             val.Contains("customer", StringComparison.OrdinalIgnoreCase)))
                        {
                            string cleanKey = _textUtility.RemoveDiacritics(val);
                            if (!IgnoredMetadataKeys.Contains(cleanKey) && !result.Metadata.ContainsKey(cleanKey))
                            {
                                result.Metadata[cleanKey] = nextVal;
                            }
                        }
                    }
                }
            }
        }

        return result;
    }
}
