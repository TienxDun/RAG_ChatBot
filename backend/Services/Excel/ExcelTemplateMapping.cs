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
}

public class ExcelTemplateMapping
{
    public Dictionary<string, string> ColumnMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> MetadataCellMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ExcelTemplateSubtotalConfig? SubtotalConfig { get; set; }
}
