using System;
using System.Collections.Generic;

namespace Backend.Services.Excel;

public class ExcelTemplateSubtotalConfig
{
    public string GroupByColumn { get; set; } = string.Empty;
    public List<string> SumColumns { get; set; } = new();
    public string? LabelColumn { get; set; }
    public List<string> DefectRateColumns { get; set; } = new();
    public List<string> MergeColumns { get; set; } = new();
    public List<string> MergeToSubtotalColumns { get; set; } = new();
}

/// Định nghĩa một tham số động cho template Excel.
/// Được lưu trong excel_mappings.json cùng với ColumnMappings, MetadataCellMappings.
public class TemplateParameter
{
    /// Key nội bộ dùng để ghép prompt, ví dụ: "line_name"
    public string Key { get; set; } = string.Empty;

    /// Nhãn hiển thị trên form, ví dụ: "Tên chuyền"
    public string Label { get; set; } = string.Empty;

    /// Loại input: "text" | "select" | "date" | "daterange" | "number"
    public string Type { get; set; } = "text";

    /// Tham số này có bắt buộc không
    public bool Required { get; set; } = true;

    /// Tên bảng SQL Server để lấy danh sách options (chỉ dùng khi Type = "select")
    public string? DataSource { get; set; }

    /// Tên cột dùng làm giá trị trong bảng DataSource
    public string? DataColumn { get; set; }

    /// Nhãn hiển thị trên dropdown (nếu khác DataColumn)
    public string? DisplayColumn { get; set; }

    /// Placeholder text cho input
    public string? Placeholder { get; set; }

    /// Giá trị mặc định — "today" cho date, hoặc giá trị cụ thể
    public string? DefaultValue { get; set; }

    /// Template để ghép vào prompt, ví dụ: "Tên chuyền: {value}"
    /// Nếu để trống, hệ thống dùng "{Label}: {value}"
    public string? PromptTemplate { get; set; }

    /// Thứ tự hiển thị trên form (nhỏ hơn = hiển thị trước)
    public int Order { get; set; } = 0;
}

public class ExcelTemplateMapping
{
    public Dictionary<string, string> ColumnMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> MetadataCellMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ColumnFormats { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ExcelTemplateSubtotalConfig? SubtotalConfig { get; set; }

    /// Danh sách tham số động được admin cấu hình cho template này.
    /// Nếu null/rỗng, hệ thống sử dụng behavior cũ (người dùng tự gõ prompt).
    public List<TemplateParameter>? Parameters { get; set; }
}
