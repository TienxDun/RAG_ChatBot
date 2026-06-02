using System;
using System.Collections.Generic;

namespace Backend.Services.Excel;

public enum TemplateType
{
    Horizontal,   // Bảng lưới thông thường (1 dòng tiêu đề)
    Hierarchical  // Bảng phân tầng phức tạp (Merged Cells tiêu đề nhóm)
}

public class FlattenedColumn
{
    public int ColumnIndex { get; set; }
    public string UniqueKey { get; set; } = string.Empty;
    public string ParentHeader { get; set; } = string.Empty;
    public string ChildHeader { get; set; } = string.Empty;
}

public class ExcelCellDto
{
    public int Row { get; set; }
    public int Col { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsBold { get; set; }
    public bool IsMerged { get; set; }
    public string? MergedRange { get; set; }
    public int RowSpan { get; set; } = 1;
    public int ColSpan { get; set; } = 1;
    public bool IsMergedChild { get; set; } // Nếu true, ô này bị ẩn vì thuộc ô gộp của ô khác
}

public class TemplateAnalysisResult
{
    public TemplateType Type { get; set; } = TemplateType.Horizontal;
    public int HeaderRowIndex { get; set; } = 1;
    public int StartColumnIndex { get; set; } = 1;
    public int DataStartRowIndex { get; set; } = 2;
    public int DataEndRowIndex { get; set; } = 2;
    public int? TotalRowIndex { get; set; }
    public List<int> FillableRowIndexes { get; set; } = new();
    public List<FlattenedColumn> Columns { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<List<ExcelCellDto>> Grid { get; set; } = new();
}
