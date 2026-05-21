using OfficeOpenXml;
using System.Drawing;

namespace Backend.Services.Excel;

public static class ExcelStylingHelper
{
    // Tô màu nền xanh pastel nhẹ nhàng, chữ xám đậm đậm và thiết lập phông chữ cho hàng tiêu đề
    public static void ApplyHeaderStyle(ExcelWorksheet worksheet, int headerRowIndex, int startColumnIndex, int columnCount)
    {
        var headerRange = worksheet.Cells[headerRowIndex, startColumnIndex, headerRowIndex, startColumnIndex + columnCount - 1];
        headerRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
        headerRange.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(232, 240, 248)); // Xanh dương pastel thanh lịch
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
            for (int c = 1; c <= totalCols; c++)
            {
                var cell = worksheet.Cells[r, c];
                bool isInTableRange = r >= headerRowIndex && r <= endRowOfData && 
                                     c >= startColumnIndex && c <= startColumnIndex + columnCount - 1;

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
                else
                {
                    // Xóa sạch toàn bộ border thừa thãi ngoài vùng bảng dữ liệu thực tế
                    cell.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.None;
                    cell.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.None;
                    cell.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.None;
                    cell.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.None;
                }
            }
        }
    }
}
