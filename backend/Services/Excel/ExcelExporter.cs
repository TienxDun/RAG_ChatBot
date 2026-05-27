using System.Collections.Generic;

namespace Backend.Services.Excel;

public interface IExcelExporter
{
    byte[] ExportGenericExcel(List<Dictionary<string, object>> data);
    byte[] ExportMarkdownToExcelDynamic(string markdownText);
}

public class ExcelExporter : IExcelExporter
{
    public byte[] ExportGenericExcel(List<Dictionary<string, object>> data)
    {
        return ExcelExportHelper.ExportGenericExcel(data);
    }

    public byte[] ExportMarkdownToExcelDynamic(string markdownText)
    {
        return ExcelExportHelper.ExportMarkdownToExcelDynamic(markdownText);
    }
}
