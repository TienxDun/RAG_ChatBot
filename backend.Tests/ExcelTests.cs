using Xunit;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text.Json;
using OfficeOpenXml;
using Backend.Services.Excel;
using Backend.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Backend.Services;
using System.Reflection;

namespace backend.Tests;

public class ExcelTests
{
    public ExcelTests()
    {
        // Kích hoạt giấy phép EPPlus cho môi trường kiểm thử
        ExcelPackage.License.SetNonCommercialPersonal("Test Environment");
    }

    [Fact]
    public void ConvertJsonToDataTable_ShouldBeCaseInsensitive_WhenParsingProperties()
    {
        // Arrange
        // JSON chứa các thuộc tính viết thường/lạc case so với columns trong template
        string jsonString = @"[
            {
                ""tenkhachhang"": ""Công ty A"",
                ""ngaygiao"": ""2026-05-25"",
                ""soluong"": 150.5
            },
            {
                ""TenKhachHang"": ""Công ty B"",
                ""NgayGiao"": ""2026-05-26"",
                ""SoLuong"": 200
            }
        ]";

        var templateColumns = new List<string> { "TenKhachHang", "NgayGiao", "SoLuong" };

        var filler = new ExcelTemplateFiller(new TextUtility());
        DataTable dataTable = filler.ConvertJsonToDataTable(jsonString, templateColumns);

        // Assert
        Assert.Equal(2, dataTable.Rows.Count);

        // Dòng 1: Kiểm tra xem các thuộc tính viết thường có map đúng vào cột hoa thường chuẩn không
        Assert.Equal("Công ty A", dataTable.Rows[0]["TenKhachHang"]);
        Assert.Equal(new DateTime(2026, 5, 25), dataTable.Rows[0]["NgayGiao"]);
        Assert.Equal(150.5, dataTable.Rows[0]["SoLuong"]);

        // Dòng 2: Kiểm tra case-exact khớp chuẩn
        Assert.Equal("Công ty B", dataTable.Rows[1]["TenKhachHang"]);
        Assert.Equal(new DateTime(2026, 5, 26), dataTable.Rows[1]["NgayGiao"]);
        Assert.Equal(200.0, dataTable.Rows[1]["SoLuong"]);
    }

    [Fact]
    public void ExportGenericExcel_ShouldParseVietnameseDateStringCorrectly()
    {
        // Arrange
        var data = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object>
            {
                { "TenKhachHang", "Khách Hàng Việt Nam" },
                { "NgayGiaoDich", "25/05/2026" }, // Định dạng dd/MM/yyyy
                { "DoanhThu", 5000000 }
            }
        };

        // Act
        byte[] excelBytes = ExcelExportHelper.ExportGenericExcel(data);

        // Assert
        Assert.NotNull(excelBytes);
        Assert.True(excelBytes.Length > 0);

        // Đọc lại file Excel để kiểm tra kiểu dữ liệu của ô
        using var stream = new System.IO.MemoryStream(excelBytes);
        using var package = new ExcelPackage(stream);
        var worksheet = package.Workbook.Worksheets[0];

        // Header là dòng 1, Data là dòng 2
        var customerCell = worksheet.Cells[2, 1];
        var dateCell = worksheet.Cells[2, 2];
        var revenueCell = worksheet.Cells[2, 3];

        Assert.Equal("Khách Hàng Việt Nam", customerCell.Value);

        // Ngày giao dịch phải được parse thành kiểu DateTime thành công
        var dateValue = dateCell.GetValue<DateTime>();
        Assert.Equal(new DateTime(2026, 5, 25), dateValue.Date);

        // Định dạng hiển thị ngày trên Excel phải là dd/MM/yyyy
        Assert.Equal("dd/MM/yyyy", dateCell.Style.Numberformat.Format);
        Assert.Equal(OfficeOpenXml.Style.ExcelHorizontalAlignment.Center, dateCell.Style.HorizontalAlignment);

        // Số doanh thu
        Assert.Equal(5000000.0, Convert.ToDouble(revenueCell.Value));
        Assert.Equal("#,##0", revenueCell.Style.Numberformat.Format);
        Assert.Equal(OfficeOpenXml.Style.ExcelHorizontalAlignment.Right, revenueCell.Style.HorizontalAlignment);
    }

    [Fact]
    public void FillHorizontalTemplate_ShouldOverwriteExistingRowsWithoutMovingTotal_WhenRowLabelsExist()
    {
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Report");
        worksheet.Cells[1, 1].Value = "Ngay";
        worksheet.Cells[1, 2].Value = "SoLuong";
        worksheet.Cells[2, 1].Value = "27/05/2026";
        worksheet.Cells[3, 1].Value = "28/05/2026";
        worksheet.Cells[4, 1].Value = "Tổng";
        worksheet.Cells[4, 2].Formula = "SUM(B2:B3)";

        var table = new DataTable();
        table.Columns.Add("Ngay", typeof(DateTime));
        table.Columns.Add("SoLuong", typeof(double));
        table.Rows.Add(new DateTime(2026, 5, 27), 10d);
        table.Rows.Add(new DateTime(2026, 5, 28), 15d);

        var filler = new ExcelTemplateFiller(new TextUtility());
        filler.FillHorizontalTemplate(worksheet, table, 1, 1, 2, new List<string> { "2026-05-27", "2026-05-28" });
        package.Workbook.Calculate();

        Assert.Equal(10d, Convert.ToDouble(worksheet.Cells[2, 2].Value));
        Assert.Equal(15d, Convert.ToDouble(worksheet.Cells[3, 2].Value));
        Assert.Equal("SUM(B2:B3)", worksheet.Cells[4, 2].Formula);
        Assert.Equal(25d, Convert.ToDouble(worksheet.Cells[4, 2].Value));
    }

    [Fact]
    public void FillHorizontalTemplate_ShouldWriteIntoSameWorksheetSequentially_WhenNoRowLabelsExist()
    {
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Report");
        worksheet.Cells[1, 1].Value = "Ngay";
        worksheet.Cells[1, 2].Value = "SoLuong";

        var table = new DataTable();
        table.Columns.Add("Ngay", typeof(DateTime));
        table.Columns.Add("SoLuong", typeof(double));
        table.Rows.Add(new DateTime(2026, 5, 27), 10d);
        table.Rows.Add(new DateTime(2026, 5, 28), 15d);

        var filler = new ExcelTemplateFiller(new TextUtility());
        filler.FillHorizontalTemplate(worksheet, table, 1, 1, 2);

        Assert.Equal(new DateTime(2026, 5, 27), worksheet.Cells[2, 1].GetValue<DateTime>().Date);
        Assert.Equal(10d, Convert.ToDouble(worksheet.Cells[2, 2].Value));
        Assert.Equal(new DateTime(2026, 5, 28), worksheet.Cells[3, 1].GetValue<DateTime>().Date);
        Assert.Equal(15d, Convert.ToDouble(worksheet.Cells[3, 2].Value));
    }

    [Fact]
    public void FillHorizontalTemplate_ShouldWriteOnlyIntoExplicitFillableRows_WhenProvidedByTemplateAnalysis()
    {
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Report");
        worksheet.Cells[8, 1].Value = "Ngay";
        worksheet.Cells[8, 2].Value = "SoLuong";
        worksheet.Cells[9, 1].Value = null;
        worksheet.Cells[10, 1].Value = null;
        worksheet.Cells[11, 1].Value = null;
        worksheet.Cells[12, 1].Value = "Tổng";
        worksheet.Cells[12, 2].Formula = "SUM(B9:B11)";

        var table = new DataTable();
        table.Columns.Add("Ngay", typeof(DateTime));
        table.Columns.Add("SoLuong", typeof(double));
        table.Rows.Add(new DateTime(2026, 5, 27), 10d);
        table.Rows.Add(new DateTime(2026, 5, 28), 15d);

        var filler = new ExcelTemplateFiller(new TextUtility());
        filler.FillHorizontalTemplate(
            worksheet,
            table,
            8,
            1,
            2,
            null,
            new List<int> { 9, 10, 11 },
            12,
            true);

        Assert.Equal(new DateTime(2026, 5, 27), worksheet.Cells[9, 1].GetValue<DateTime>().Date);
        Assert.Equal(10d, Convert.ToDouble(worksheet.Cells[9, 2].Value));
        Assert.Equal(new DateTime(2026, 5, 28), worksheet.Cells[10, 1].GetValue<DateTime>().Date);
        Assert.Equal(15d, Convert.ToDouble(worksheet.Cells[10, 2].Value));
        Assert.Equal("Tổng", worksheet.Cells[11, 1].Value);
        Assert.Equal("SUM(B9:B10)", worksheet.Cells[11, 2].Formula);
    }

    [Fact]
    public void FillHorizontalTemplate_ShouldExpandBeforeTotal_WhenUserRequestsMoreRowsThanTemplateProvides()
    {
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Report");
        worksheet.Cells[8, 1].Value = "Ngay";
        worksheet.Cells[8, 2].Value = "SoLuong";
        worksheet.Cells[12, 1].Value = "Tổng";
        worksheet.Cells[12, 2].Formula = "SUM(B9:B11)";

        var table = new DataTable();
        table.Columns.Add("Ngay", typeof(DateTime));
        table.Columns.Add("SoLuong", typeof(double));
        table.Rows.Add(new DateTime(2026, 5, 27), 10d);
        table.Rows.Add(new DateTime(2026, 5, 28), 15d);
        table.Rows.Add(new DateTime(2026, 5, 29), 20d);
        table.Rows.Add(new DateTime(2026, 5, 30), 25d);
        table.Rows.Add(new DateTime(2026, 5, 31), 30d);

        var filler = new ExcelTemplateFiller(new TextUtility());
        filler.FillHorizontalTemplate(
            worksheet,
            table,
            8,
            1,
            9,
            null,
            new List<int> { 9, 10, 11 },
            12);

        Assert.Equal("27/05/2026", worksheet.Cells[9, 1].Text);
        Assert.Equal("31/05/2026", worksheet.Cells[13, 1].Text);
        Assert.Equal(30d, Convert.ToDouble(worksheet.Cells[13, 2].Value));
        Assert.Equal("Tổng", worksheet.Cells[14, 1].Text);
        Assert.Equal("SUM(B9:B13)", worksheet.Cells[14, 2].Formula);
    }

    [Fact]
    public void FillHierarchicalTemplate_ShouldWriteIntoExplicitFillableRows_WhenNoRowLabelsExist()
    {
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Report");
        worksheet.Cells[7, 1].Value = "Ngày";
        worksheet.Cells[7, 2].Value = "Thành phẩm";
        worksheet.Cells[8, 1].Value = "Ngày";
        worksheet.Cells[8, 2].Value = "SL kiểm";
        worksheet.Cells[8, 3].Value = "SL lỗi";
        worksheet.Cells[9, 1].Value = null;
        worksheet.Cells[10, 1].Value = null;
        worksheet.Cells[11, 1].Value = null;
        worksheet.Cells[12, 1].Value = "Tổng";
        worksheet.Cells[12, 2].Formula = "SUM(B9:B11)";

        var flattenedColumns = new List<FlattenedColumn>
        {
            new() { ColumnIndex = 1, UniqueKey = "Ngay", ParentHeader = "", ChildHeader = "Ngày" },
            new() { ColumnIndex = 2, UniqueKey = "ThanhPham_SLkiem", ParentHeader = "Thành phẩm", ChildHeader = "SL kiểm" },
            new() { ColumnIndex = 3, UniqueKey = "ThanhPham_SLloi", ParentHeader = "Thành phẩm", ChildHeader = "SL lỗi" }
        };

        var table = new DataTable();
        table.Columns.Add("Ngay", typeof(DateTime));
        table.Columns.Add("ThanhPham_SLkiem", typeof(double));
        table.Columns.Add("ThanhPham_SLloi", typeof(double));
        table.Rows.Add(new DateTime(2026, 5, 5), 68d, 64d);
        table.Rows.Add(new DateTime(2026, 5, 4), 188d, 177d);

        var filler = new ExcelTemplateFiller(new TextUtility());
        filler.FillHierarchicalTemplate(
            worksheet,
            table,
            8,
            1,
            flattenedColumns,
            null,
            new List<int> { 9, 10, 11 },
            12,
            true);

        Assert.Equal(new DateTime(2026, 5, 5), worksheet.Cells[9, 1].GetValue<DateTime>().Date);
        Assert.Equal(68d, Convert.ToDouble(worksheet.Cells[9, 2].Value));
        Assert.Equal(64d, Convert.ToDouble(worksheet.Cells[9, 3].Value));
        Assert.Equal(new DateTime(2026, 5, 4), worksheet.Cells[10, 1].GetValue<DateTime>().Date);
        Assert.Equal(188d, Convert.ToDouble(worksheet.Cells[10, 2].Value));
        Assert.Equal(177d, Convert.ToDouble(worksheet.Cells[10, 3].Value));
        Assert.Equal("Tổng", worksheet.Cells[11, 1].Value);
        Assert.Equal("SUM(B9:B10)", worksheet.Cells[11, 2].Formula);
    }

    [Fact]
    public void FillHierarchicalTemplate_ShouldWriteObjectTypedRows_FromExcelReportServiceFlow()
    {
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Report");
        worksheet.Cells[8, 1].Value = "Ngày";
        worksheet.Cells[8, 2].Value = "SL kiểm";
        worksheet.Cells[8, 3].Value = "SL lỗi";
        worksheet.Cells[12, 1].Value = "Tổng";

        var flattenedColumns = new List<FlattenedColumn>
        {
            new() { ColumnIndex = 1, UniqueKey = "Ngay", ParentHeader = "", ChildHeader = "Ngày" },
            new() { ColumnIndex = 2, UniqueKey = "ThanhPham_SLkiem", ParentHeader = "Thành phẩm", ChildHeader = "SL kiểm" },
            new() { ColumnIndex = 3, UniqueKey = "ThanhPham_SLloi", ParentHeader = "Thành phẩm", ChildHeader = "SL lỗi" }
        };

        var table = new DataTable();
        table.Columns.Add("Ngay", typeof(object));
        table.Columns.Add("ThanhPham_SLkiem", typeof(object));
        table.Columns.Add("ThanhPham_SLloi", typeof(object));
        table.Rows.Add(new DateTime(2026, 5, 5), 68, 64);
        table.Rows.Add(new DateTime(2026, 5, 4), 188, 177);

        var filler = new ExcelTemplateFiller(new TextUtility());
        filler.FillHierarchicalTemplate(
            worksheet,
            table,
            8,
            1,
            flattenedColumns,
            null,
            new List<int> { 9, 10, 11 },
            12);

        Assert.Equal(new DateTime(2026, 5, 5), worksheet.Cells[9, 1].GetValue<DateTime>().Date);
        Assert.Equal(68d, Convert.ToDouble(worksheet.Cells[9, 2].Value));
        Assert.Equal(64d, Convert.ToDouble(worksheet.Cells[9, 3].Value));
        Assert.Equal(new DateTime(2026, 5, 4), worksheet.Cells[10, 1].GetValue<DateTime>().Date);
        Assert.Equal(188d, Convert.ToDouble(worksheet.Cells[10, 2].Value));
        Assert.Equal(177d, Convert.ToDouble(worksheet.Cells[10, 3].Value));
    }

    [Fact]
    public void FillHierarchicalTemplate_ShouldExpandBeforeTotal_WhenUserRequestsMoreRowsThanTemplateProvides()
    {
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Report");
        worksheet.Cells[8, 1].Value = "Ngày";
        worksheet.Cells[8, 2].Value = "SL kiểm";
        worksheet.Cells[8, 3].Value = "SL lỗi";
        worksheet.Cells[12, 1].Value = "Tổng";
        worksheet.Cells[12, 2].Formula = "SUM(B9:B11)";

        var flattenedColumns = new List<FlattenedColumn>
        {
            new() { ColumnIndex = 1, UniqueKey = "Ngay", ParentHeader = "", ChildHeader = "Ngày" },
            new() { ColumnIndex = 2, UniqueKey = "ThanhPham_SLkiem", ParentHeader = "Thành phẩm", ChildHeader = "SL kiểm" },
            new() { ColumnIndex = 3, UniqueKey = "ThanhPham_SLloi", ParentHeader = "Thành phẩm", ChildHeader = "SL lỗi" }
        };

        var table = new DataTable();
        table.Columns.Add("Ngay", typeof(object));
        table.Columns.Add("ThanhPham_SLkiem", typeof(object));
        table.Columns.Add("ThanhPham_SLloi", typeof(object));
        table.Rows.Add(new DateTime(2026, 5, 5), 68, 64);
        table.Rows.Add(new DateTime(2026, 5, 4), 188, 177);
        table.Rows.Add(new DateTime(2026, 5, 3), 56, 47);
        table.Rows.Add(new DateTime(2026, 5, 2), 105, 100);
        table.Rows.Add(new DateTime(2026, 5, 1), 125, 108);

        var filler = new ExcelTemplateFiller(new TextUtility());
        filler.FillHierarchicalTemplate(
            worksheet,
            table,
            8,
            1,
            flattenedColumns,
            null,
            new List<int> { 9, 10, 11 },
            12);

        Assert.Equal("05/05/2026", worksheet.Cells[9, 1].Text);
        Assert.Equal("01/05/2026", worksheet.Cells[13, 1].Text);
        Assert.Equal(125d, Convert.ToDouble(worksheet.Cells[13, 2].Value));
        Assert.Equal(108d, Convert.ToDouble(worksheet.Cells[13, 3].Value));
        Assert.Equal("Tổng", worksheet.Cells[14, 1].Text);
        Assert.Equal("SUM(B9:B13)", worksheet.Cells[14, 2].Formula);
    }

    [Fact]
    public async Task HandleDownloadAsync_ShouldReturnOriginalFileNameInContentDisposition()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var fileId = "download-test.xlsx";
        var fileName = "BCEndlineNgay (22)_Empty.xlsx";
        var bytes = new byte[] { 1, 2, 3, 4 };

        cache.Set(fileId, new CachedDownloadFile
        {
            Content = bytes,
            FileName = fileName
        }, TimeSpan.FromMinutes(5));

        var result = ChatEndpoints.HandleDownloadAsync(fileId, cache);
        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = new TestServiceProvider();
        httpContext.Response.Body = new MemoryStream();

        await result.ExecuteAsync(httpContext);

        var disposition = httpContext.Response.Headers.ContentDisposition.ToString();
        Assert.Contains("attachment", disposition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".xlsx", disposition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("filename=", disposition, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSoftColumnMapping_ShouldMatchVietnameseTemplateHeaders_ToNonAccentSqlAliases()
    {
        var source = new DataTable();
        source.Columns.Add("Ngay", typeof(DateTime));
        source.Columns.Add("ThanhPham_SLKiem", typeof(double));
        source.Columns.Add("ThanhPham_SLLoi", typeof(double));
        source.Columns.Add("ThanhPham_TyLeLoi", typeof(double));
        source.Columns.Add("Vo_SLKiem", typeof(double));
        source.Columns.Add("Vo_SLLoi", typeof(double));
        source.Columns.Add("Vo_TyLeLoi", typeof(double));

        var templateColumns = new List<FlattenedColumn>
        {
            new() { UniqueKey = "Ngay", ParentHeader = "", ChildHeader = "Ngày" },
            new() { UniqueKey = "ThanhPham_SLkiem", ParentHeader = "Thành Phẩm", ChildHeader = "SL kiểm" },
            new() { UniqueKey = "ThanhPham_SLloi", ParentHeader = "Thành Phẩm", ChildHeader = "SL lỗi" },
            new() { UniqueKey = "ThanhPham_Tileloi", ParentHeader = "Thành Phẩm", ChildHeader = "Tỉ lệ lỗi" },
            new() { UniqueKey = "Vo_SLkiem", ParentHeader = "Vỏ", ChildHeader = "SL kiểm" },
            new() { UniqueKey = "Vo_SLloi", ParentHeader = "Vỏ", ChildHeader = "SL lỗi" },
            new() { UniqueKey = "Vo_Tileloi", ParentHeader = "Vỏ", ChildHeader = "Tỉ lệ lỗi" }
        };

        var method = typeof(ExcelReportService).GetMethod("BuildSoftColumnMapping", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var mapping = (Dictionary<string, string>?)method!.Invoke(null, new object[] { source, templateColumns });

        Assert.NotNull(mapping);
        Assert.Equal("Ngay", mapping!["Ngay"]);
        Assert.Equal("ThanhPham_SLKiem", mapping["ThanhPham_SLkiem"]);
        Assert.Equal("ThanhPham_SLLoi", mapping["ThanhPham_SLloi"]);
        Assert.Equal("ThanhPham_TyLeLoi", mapping["ThanhPham_Tileloi"]);
        Assert.Equal("Vo_SLKiem", mapping["Vo_SLkiem"]);
        Assert.Equal("Vo_SLLoi", mapping["Vo_SLloi"]);
        Assert.Equal("Vo_TyLeLoi", mapping["Vo_Tileloi"]);
    }

    [Fact]
    public void FindBestMetadataValue_ShouldResolveDynamicMatches_WithoutHardcodedKeyLists()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["StyleID"] = "SA-893-208-133",
            ["LineX"] = "110",
            ["MaLenh"] = "694"
        };

        var method = typeof(ExcelReportService).GetMethod("FindBestMetadataValue", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var styleValue = (string?)method!.Invoke(null, new object[] { metadata, "Mã Hàng/ Style" });
        var lineValue = (string?)method.Invoke(null, new object[] { metadata, "CHUYỀN/ Line" });

        Assert.Equal("SA-893-208-133", styleValue);
        Assert.Equal("110", lineValue);
    }

    [Fact]
    public void MergeTemplateAnalysis_ShouldPreserveLlmColumns_AndBackfillMissingFillAreaFromRuleBased()
    {
        var llm = new TemplateAnalysisResult
        {
            Type = TemplateType.Hierarchical,
            HeaderRowIndex = 8,
            StartColumnIndex = 1,
            DataStartRowIndex = 9,
            Columns = new List<FlattenedColumn>
            {
                new() { ColumnIndex = 1, UniqueKey = "Ngay", ChildHeader = "Ngày" },
                new() { ColumnIndex = 2, UniqueKey = "ThanhPham_SLkiem", ParentHeader = "Thành Phẩm", ChildHeader = "SL kiểm" }
            }
        };

        var ruleBased = new TemplateAnalysisResult
        {
            Type = TemplateType.Hierarchical,
            HeaderRowIndex = 8,
            StartColumnIndex = 1,
            DataStartRowIndex = 9,
            DataEndRowIndex = 14,
            TotalRowIndex = 15,
            FillableRowIndexes = new List<int> { 9, 10, 11, 12, 13, 14 }
        };

        var method = typeof(ExcelReportService).GetMethod("MergeTemplateAnalysis", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var merged = (TemplateAnalysisResult?)method!.Invoke(null, new object[] { llm, ruleBased });

        Assert.NotNull(merged);
        Assert.Equal(2, merged!.Columns.Count);
        Assert.Equal(14, merged.DataEndRowIndex);
        Assert.Equal(15, merged.TotalRowIndex);
        Assert.Equal(new List<int> { 9, 10, 11, 12, 13, 14 }, merged.FillableRowIndexes);
    }

    [Fact]
    public void ExcelMappingService_ShouldHandleBackwardCompatibility_WhenLoadingOldFormat()
    {
        // Arrange
        string tempFilePath = Path.Combine(Path.GetTempPath(), $"excel_mappings_{Guid.NewGuid()}.json");
        try
        {
            // Ghi file ở định dạng cũ (dictionary 2 cấp phẳng string-to-string)
            var oldData = new Dictionary<string, Dictionary<string, string>>
            {
                ["TestFile.xlsx"] = new Dictionary<string, string>
                {
                    ["Col1"] = "Note 1",
                    ["Col2"] = "Note 2"
                }
            };
            File.WriteAllText(tempFilePath, JsonSerializer.Serialize(oldData));

            // Khởi tạo service bằng Reflection để dùng path tạm
            var service = new ExcelMappingService();
            // Sử dụng Reflection để thay thế trường private _filePath
            var filePathField = typeof(ExcelMappingService).GetField("_filePath", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(filePathField);
            filePathField!.SetValue(service, tempFilePath);

            // Force load lại file
            var loadMethod = typeof(ExcelMappingService).GetMethod("LoadFromFile", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(loadMethod);
            loadMethod!.Invoke(service, null);

            // Act
            var mapping = service.GetTemplateMapping("TestFile.xlsx");

            // Assert
            Assert.NotNull(mapping);
            Assert.NotNull(mapping.ColumnMappings);
            Assert.Equal("Note 1", mapping.ColumnMappings["Col1"]);
            Assert.Equal("Note 2", mapping.ColumnMappings["Col2"]);
            Assert.Empty(mapping.MetadataCellMappings); // Metadata cell mappings phải rỗng vì là định dạng cũ
        }
        finally
        {
            if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
        }
    }

    [Fact]
    public void FillMetadata_ShouldPlaceValuesInConfiguredCells_WhenCellMappingsExist()
    {
        // Arrange
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Report");
        worksheet.Cells["C2"].Value = "Chuyền:";
        worksheet.Cells["C3"].Value = "Ngày tháng:";
        worksheet.Cells["D5"].Value = "Style gốc";

        var metadataValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Chuyen"] = "Cherry Chuyền 5",
            ["Ngay"] = "2026-06-02",
            ["StyleID"] = "SA-893-ABC"
        };

        var cellMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Chuyen"] = "C2",
            ["Ngay"] = "C3",
            ["StyleID"] = "D5"
        };

        var filler = new ExcelTemplateFiller(new TextUtility());

        // Act
        filler.FillMetadata(worksheet, 10, metadataValues, cellMappings);

        // Assert
        Assert.Equal("Chuyền: Cherry Chuyền 5", worksheet.Cells["C2"].Value); // Phải ghép với label có sẵn
        Assert.Equal("Ngày tháng: 2026-06-02", worksheet.Cells["C3"].Value); // Phải ghép với label có sẵn
        Assert.Equal("SA-893-ABC", worksheet.Cells["D5"].Value); // Không chứa dấu hai chấm, phải ghi đè hẳn
    }
}

class TestServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(Microsoft.Extensions.Logging.ILoggerFactory))
        {
            return Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        }
        return null;
    }
}
