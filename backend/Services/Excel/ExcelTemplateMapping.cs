using System;
using System.Collections.Generic;

namespace Backend.Services.Excel;

public class ExcelTemplateMapping
{
    public Dictionary<string, string> ColumnMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> MetadataCellMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
