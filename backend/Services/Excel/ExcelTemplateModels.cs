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
}
