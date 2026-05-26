using OfficeOpenXml;
using System.Drawing;

namespace Backend.Services.Excel;

public static class ExcelStylingHelper
{
    // Tô màu chữ và thiết lập phông chữ cho hàng tiêu đề (bỏ qua tô nền xanh pastel để giữ nguyên mẫu gốc)
    public static void ApplyHeaderStyle(ExcelWorksheet worksheet, int headerRowIndex, int startColumnIndex, int columnCount)
    {
        var headerRange = worksheet.Cells[headerRowIndex, startColumnIndex, headerRowIndex, startColumnIndex + columnCount - 1];
        // headerRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
        // headerRange.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(232, 240, 248)); // Xanh dương pastel thanh lịch
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.Color.SetColor(Color.FromArgb(51, 51, 51)); // Màu chữ xám đậm dễ nhìn
        headerRange.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
        headerRange.Style.Font.Name = "Segoe UI";
    }

    // Tô màu nền xám nhẹ, chữ xám đậm, in đậm cho cột nhãn (dành cho template dọc)
    public static void ApplyVerticalLabelStyle(ExcelWorksheet worksheet, int headerRowIndex, int endRowOfData, int labelColumnIndex)
    {
        if (endRowOfData > headerRowIndex)
        {
            var labelRange = worksheet.Cells[headerRowIndex + 1, labelColumnIndex, endRowOfData, labelColumnIndex];
            labelRange.Style.Font.Bold = true;
            labelRange.Style.Font.Color.SetColor(Color.FromArgb(64, 64, 64)); // Màu nhãn xám đậm
            labelRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            labelRange.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(250, 250, 250)); // Nền xám cực nhẹ để phân biệt
        }
    }

    // Vẽ viền xám mỏng tinh tế cho vùng bảng dữ liệu và làm sạch toàn bộ border thừa thãi bên ngoài
    public static void SanitizeBorders(ExcelWorksheet worksheet, int headerRowIndex, int endRowOfData, int startColumnIndex, int columnCount)
    {
        if (worksheet.Dimension == null) return;

        int totalRows = worksheet.Dimension.End.Row;
        int totalCols = worksheet.Dimension.End.Column;
        var borderColor = Color.FromArgb(208, 215, 222); // Màu xám nhạt chuẩn hiện đại

        for (int r = 1; r <= totalRows; r++)
        {
            // Bảo toàn hoàn toàn định dạng và borders của các dòng phía trên bảng (Tiêu đề, logo, metadata đầu trang)
            if (r < headerRowIndex) continue;

            for (int c = 1; c <= totalCols; c++)
            {
                var cell = worksheet.Cells[r, c];
                bool isInTableRange = r <= endRowOfData && 
                                     c >= startColumnIndex && c <= startColumnIndex + columnCount - 1;

                // Hàng Tổng dịch chuyển nằm ngay sau hàng cuối của bảng dữ liệu
                bool isTotalRow = r == endRowOfData + 1;

                if (isInTableRange)
                {
                    cell.Style.Font.Name = "Segoe UI";
                    cell.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                    cell.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                    cell.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                    cell.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                    
                    cell.Style.Border.Top.Color.SetColor(borderColor);
                    cell.Style.Border.Bottom.Color.SetColor(borderColor);
                    cell.Style.Border.Left.Color.SetColor(borderColor);
                    cell.Style.Border.Right.Color.SetColor(borderColor);
                }
                else if (isTotalRow)
                {
                    // Giữ nguyên hoàn toàn định dạng/border gốc của dòng Tổng, không chỉnh sửa gì
                    continue;
                }
                else
                {
                    // Chỉ xóa border cho các ô nằm hẳn bên dưới dòng Tổng để dọn sạch tàn dư của placeholder cũ
                    if (r > endRowOfData + 1)
                    {
                        cell.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.None;
                        cell.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.None;
                        cell.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.None;
                        cell.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.None;
                    }
                }
            }
        }
    }

    // Áp dụng bộ lọc (AutoFilter) cho vùng bảng dữ liệu dạng ngang
    public static void ApplyAutoFilter(ExcelWorksheet worksheet, int headerRowIndex, int endRowOfData, int startColumnIndex, int columnCount)
    {
        if (endRowOfData >= headerRowIndex && columnCount > 0)
        {
            // Thiết lập AutoFilter cho vùng từ Header đến dòng cuối cùng của bảng dữ liệu
            worksheet.Cells[headerRowIndex, startColumnIndex, endRowOfData, startColumnIndex + columnCount - 1].AutoFilter = true;
        }
    }

    // Tô màu chữ, in đậm cho cả 2 dòng header đối với template phân cấp (bỏ qua tô nền xanh để giữ nguyên mẫu gốc)
    public static void ApplyHierarchicalHeaderStyle(ExcelWorksheet worksheet, int parentRowIndex, int childRowIndex, int startColumnIndex, int columnCount)
    {
        // 1. Dòng tiêu đề cha: chữ bold, căn giữa, giữ nguyên nền mẫu gốc
        var parentRange = worksheet.Cells[parentRowIndex, startColumnIndex, parentRowIndex, startColumnIndex + columnCount - 1];
        // parentRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
        // parentRange.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(184, 204, 228)); // Xanh dương pastel đậm (#B8CCE4)
        parentRange.Style.Font.Bold = true;
        parentRange.Style.Font.Color.SetColor(Color.FromArgb(51, 51, 51));
        parentRange.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
        parentRange.Style.Font.Name = "Segoe UI";

        // 2. Dòng tiêu đề con: chữ bold, căn giữa, giữ nguyên nền mẫu gốc
        var childRange = worksheet.Cells[childRowIndex, startColumnIndex, childRowIndex, startColumnIndex + columnCount - 1];
        // childRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
        // childRange.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(232, 240, 248)); // Xanh dương pastel nhẹ (#E8F0F8)
        childRange.Style.Font.Bold = true;
        childRange.Style.Font.Color.SetColor(Color.FromArgb(51, 51, 51));
        childRange.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
        childRange.Style.Font.Name = "Segoe UI";
    }
}
