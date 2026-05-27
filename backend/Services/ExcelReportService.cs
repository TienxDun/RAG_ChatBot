using System.Collections.Generic;
using Backend.Services.Excel;

namespace Backend.Services;

public class ExcelReportService
{
    // Khởi tạo ExcelReportService
    public ExcelReportService()
    {
    }

    // Xuất danh sách dữ liệu ra file Excel dạng bảng lưới thông thường (Chuyển sang ExcelExportHelper)
    public byte[] ExportGenericExcel(List<Dictionary<string, object>> data)
    {
        return ExcelExportHelper.ExportGenericExcel(data);
    }

    // Tự sinh file Excel từ bảng Markdown (Chuyển sang ExcelExportHelper)
    public byte[] ExportMarkdownToExcelDynamic(string markdownText)
    {
        return ExcelExportHelper.ExportMarkdownToExcelDynamic(markdownText);
    }
}
