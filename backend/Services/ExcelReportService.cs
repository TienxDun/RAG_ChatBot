using OfficeOpenXml;
using System.Data;
using System.Text.Json;
using Backend.Models;
using Backend.Services.Excel;

namespace Backend.Services;

public class ExcelReportResult
{
    public string ExcelBase64 { get; set; } = string.Empty;
    public List<Dictionary<string, object>> PreviewData { get; set; } = new();
    public string Text { get; set; } = string.Empty;
    public List<string> SuggestedQuestions { get; set; } = new();
}

public class ExcelReportService
{
    private readonly RagOrchestrator _ragOrchestrator;

    // Khởi tạo ExcelReportService dùng để xử lý và định dạng báo cáo Excel
    public ExcelReportService(RagOrchestrator ragOrchestrator)
    {
        _ragOrchestrator = ragOrchestrator;
    }

    // Nhận file Excel mẫu, phân tích cấu trúc cột, truy vấn dữ liệu qua RAG và điền dữ liệu đã định dạng vào file
    public async Task<ExcelReportResult> ProcessExcelTemplateAsync(Stream excelStream, string? additionalQuery, Func<RagStep, Task> onStep, CancellationToken ct)
    {
        using var package = new ExcelPackage(excelStream);
        var worksheet = package.Workbook.Worksheets[0];

        // Gửi step thông báo bắt đầu xử lý
        await onStep(new RagStep("Excel Template Analysis", "Đang phân tích cấu trúc file template bằng bộ phân tích thông minh..."));

        // 1. Phân tích cấu trúc Template thông minh
        var templateInfo = ExcelTemplateAnalyzer.Analyze(worksheet);
        var columns = templateInfo.Columns.Select(c => c.UniqueKey).ToList();
        int headerRowIndex = templateInfo.HeaderRowIndex;
        int startColumnIndex = templateInfo.StartColumnIndex;

        // Gửi step chi tiết cấu trúc cột và nhãn metadata đã phân tích được từ template Excel
        string excelAnalysisContent = $"Đã phân tích cấu trúc template thành công.\n\n" +
                                     $"* **Loại template**: {(templateInfo.Type == TemplateType.Hierarchical ? "Phân cấp (Hierarchical)" : "Đơn giản (Simple)")}\n" +
                                     $"* **Dòng tiêu đề chính**: {headerRowIndex}\n" +
                                     $"* **Cột bắt đầu**: {startColumnIndex}\n\n" +
                                     $"**Danh sách {templateInfo.Columns.Count} tiêu đề cột đã nhận diện & làm phẳng:**\n" +
                                     $"```json\n{JsonSerializer.Serialize(templateInfo.Columns.Select(c => new { c.ColumnIndex, c.ParentHeader, c.ChildHeader, c.UniqueKey, c.FriendlyName }), new JsonSerializerOptions { WriteIndented = true })}\n```";
        if (templateInfo.MetadataCells.Count > 0)
        {
            excelAnalysisContent += $"\n\n**Danh sách {templateInfo.MetadataCells.Count} nhãn Metadata (thông tin chung ở đầu trang):**\n" +
                                   $"```json\n{JsonSerializer.Serialize(templateInfo.MetadataCells.Select(m => m.Label), new JsonSerializerOptions { WriteIndented = true })}\n```";
        }
        await onStep(new RagStep("Excel Template Analysis", excelAnalysisContent));

        // 2. GỘP CÂU QUERY CỦA USER + YÊU CẦU CỘT EXCEL & METADATA
        string combinedQuery;
        string mappingInstructions;

        if (templateInfo.Type == TemplateType.Hierarchical)
        {
            var colMappings = string.Join("\n", templateInfo.Columns.Select(col => 
                $"- Cột vật lý {col.ColumnIndex}: nhóm '{col.ParentHeader}' -> cột con '{col.ChildHeader}' -> Bạn BẮT BUỘC SELECT alias (AS) là [{col.UniqueKey}]"
            ));
            var metadataLabels = string.Join("\n", templateInfo.MetadataCells.Select(cell => 
                $"- {cell.Label}"
            ));
            
            mappingInstructions = $"\n\nYÊU CẦU ĐẶC BIỆT CHO BÁO CÁO EXCEL PHÂN CẤP (HIERARCHICAL TEMPLATE):\n" +
                                  $"- Hệ thống phát hiện đây là mẫu báo cáo có cấu trúc tiêu đề phân cấp hai tầng (Parent-Child).\n" +
                                  $"- Bạn BẮT BUỘC sử dụng ALIAS (AS) để tên cột trong kết quả trả về khớp hoàn toàn với các UniqueKey sau đây:\n" +
                                  $"{colMappings}\n" +
                                  $"- Nếu không tìm thấy cột tương ứng, hãy để trống hoặc dùng NULL, đừng cố đoán bừa.\n";
            if (templateInfo.MetadataCells.Count > 0)
            {
                mappingInstructions += $"- Ngoài ra, bạn BẮT BUỘC phải truy vấn thông tin cho các nhãn thông tin chung (metadata) sau đây từ database và trả về dưới dạng JSON (Key-Value) trong thuộc tính \"metadata\" của kết quả:\n" +
                                       $"{metadataLabels}\n" +
                                       $"- Khóa JSON trả về phải khớp hoàn toàn với tên các nhãn trên.";
            }
        }
        else
        {
            var columnsStr = string.Join(", ", columns);
            var metadataLabels = string.Join("\n", templateInfo.MetadataCells.Select(cell => 
                $"- {cell.Label}"
            ));
            
            mappingInstructions = $"\n\nYÊU CẦU ĐẶC BIỆT CHO BÁO CÁO EXCEL:\n" +
                                  $"- Dữ liệu trả về BẮT BUỘC phải có các cột tiêu đề sau: {columnsStr}.\n" +
                                  $"- Quan trọng: Hãy ánh xạ (map) các tiêu đề này với trường tương ứng trong database (ví dụ: 'TenKhachHang' map với 'KhachHang', 'StepName' map với 'cd_name').\n" +
                                  $"- Nếu không tìm thấy cột tương ứng, hãy để trống hoặc dùng NULL, đừng cố đoán bừa.\n" +
                                  $"- BẮT BUỘC sử dụng ALIAS (AS) để tên cột trong kết quả trả về khớp hoàn toàn với tên tiêu đề Excel (ví dụ: SELECT KhachHang AS [TenKhachHang], cd_name AS [StepName] ...).";
            if (templateInfo.MetadataCells.Count > 0)
            {
                mappingInstructions += $"\n- Ngoài ra, bạn BẮT BUỘC phải truy vấn thông tin cho các nhãn thông tin chung (metadata) sau đây từ database và trả về dưới dạng JSON (Key-Value) trong thuộc tính \"metadata\" của kết quả:\n" +
                                       $"{metadataLabels}\n" +
                                       $"- Khóa JSON trả về phải khớp hoàn toàn với tên các nhãn trên.";
            }
        }

        // Bổ sung điều kiện giới hạn ngày/nhãn từ hàng dọc
        if (templateInfo.RowLabels.Count > 0)
        {
            var labelsListStr = string.Join(", ", templateInfo.RowLabels.Select(l => $"'{l}'"));
            mappingInstructions += $"\n- BẮT BUỘC chỉ lọc lấy dữ liệu phát sinh trong các Ngày/Nhãn sau ở điều kiện lọc: {labelsListStr}.\n" +
                                   $"- Tuyệt đối không lấy thêm bất kỳ ngày nào khác ngoài danh sách này để tránh làm tràn hoặc thừa dữ liệu ngoài bảng của template mẫu.\n";
        }

        if (string.IsNullOrWhiteSpace(additionalQuery))
        {
            combinedQuery = $"Hãy lấy toàn bộ dữ liệu cần thiết để điền vào báo cáo theo các cột và thông tin được yêu cầu.{mappingInstructions}";
        }
        else
        {
            combinedQuery = $"{additionalQuery.Trim()}.{mappingInstructions}";
        }

        // 3. Chạy toàn bộ luồng RAG (Embeddings -> Qdrant Schema -> Vertex SQL -> Execute)
        var ragResponse = await _ragOrchestrator.ProcessQueryAsync(combinedQuery, null, onStep, ct, isExcelTemplate: true);

        // 4. Lấy kết quả dưới dạng DataTable
        DataTable dataTable = new DataTable();
        if (ragResponse.RawDataTable != null && ragResponse.RawDataTable.Rows.Count > 0)
        {
            // Tạo bản sao DataTable có các cột đúng tên UniqueKey theo thứ tự của Template
            dataTable = new DataTable();
            foreach (var colName in columns)
            {
                dataTable.Columns.Add(colName, typeof(object));
            }

            foreach (DataRow sourceRow in ragResponse.RawDataTable.Rows)
            {
                // Kiểm tra xem dòng này có phải là dòng Tổng từ SQL không
                var firstColVal = sourceRow[0]?.ToString()?.Trim() ?? "";
                if (firstColVal.Equals("Tổng", StringComparison.OrdinalIgnoreCase) || 
                    firstColVal.Equals("Total", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // Bỏ qua dòng tổng từ SQL vì Excel đã có dòng tổng công thức riêng của template
                }

                var newRow = dataTable.NewRow();
                foreach (var colName in columns)
                {
                    if (ragResponse.RawDataTable.Columns.Contains(colName))
                    {
                        newRow[colName] = sourceRow[colName];
                    }
                    else 
                    {
                        newRow[colName] = DBNull.Value;
                    }
                }
                dataTable.Rows.Add(newRow);
            }
        }
        else 
        {
            // Fallback về JSON nếu không có DataTable
            var rawJson = ragResponse.RawData;
            if (string.IsNullOrWhiteSpace(rawJson) || rawJson.Contains("[ERROR]"))
            {
                throw new Exception("AI không thể sinh được dữ liệu hợp lệ.");
            }
            dataTable = ExcelTemplateFiller.ConvertJsonToDataTable(rawJson, columns);
        }

        // 5. Điền dữ liệu chính (bảng lỗi) và điền metadata đầu trang
        if (templateInfo.Type == TemplateType.Hierarchical)
        {
            ExcelTemplateFiller.FillHierarchicalTemplate(worksheet, dataTable, headerRowIndex, startColumnIndex, templateInfo.Columns, templateInfo.RowLabels);
        }
        else
        {
            ExcelTemplateFiller.FillHorizontalTemplate(worksheet, dataTable, headerRowIndex, startColumnIndex, columns.Count, templateInfo.RowLabels);
        }

        // Điền Metadata nếu AI trả về dữ liệu tương ứng
        if (templateInfo.MetadataCells.Count > 0 && ragResponse.Metadata != null && ragResponse.Metadata.Count > 0)
        {
            ExcelTemplateFiller.FillMetadataCells(worksheet, templateInfo.MetadataCells, ragResponse.Metadata);
        }

        // Tìm dòng cuối cùng chứa dữ liệu thực tế của bảng
        int endRowOfData = headerRowIndex + (dataTable?.Rows.Count ?? 0);

        // 6. Áp dụng phong cách làm đẹp thẩm mỹ
        // Tô màu nền xanh pastel nhẹ cho các Hàng tiêu đề
        if (templateInfo.Type == TemplateType.Hierarchical && templateInfo.ParentHeaderRowIndex.HasValue)
        {
            ExcelStylingHelper.ApplyHierarchicalHeaderStyle(worksheet, templateInfo.ParentHeaderRowIndex.Value, headerRowIndex, startColumnIndex, columns.Count);
        }
        else
        {
            ExcelStylingHelper.ApplyHeaderStyle(worksheet, headerRowIndex, startColumnIndex, columns.Count);
        }

        // Áp dụng bộ lọc AutoFilter cho bảng báo cáo
        ExcelStylingHelper.ApplyAutoFilter(worksheet, headerRowIndex, endRowOfData, startColumnIndex, columns.Count);

        // Dọn dẹp border thừa và thiết lập border xám nhạt tinh tế cho vùng bảng dữ liệu thực tế (Bao gồm cả hàng tiêu đề cha nếu có)
        int startRowForBorders = templateInfo.ParentHeaderRowIndex ?? headerRowIndex;
        ExcelStylingHelper.SanitizeBorders(worksheet, startRowForBorders, endRowOfData, startColumnIndex, columns.Count);

        int rowsToFit = Math.Min(50, worksheet.Dimension?.End.Row ?? 1);
        int colsToFit = worksheet.Dimension?.End.Column ?? 1;
        worksheet.Cells[1, 1, rowsToFit, colsToFit].AutoFitColumns(12);

        // Kích hoạt tính năng tính toán công thức của EPPlus để tự động điền giá trị cho hàng Tổng
        package.Workbook.Calculate();

        var excelBytes = package.GetAsByteArray();

        // 7. Trích xuất TOP 20 dòng làm Preview
        var previewList = new List<Dictionary<string, object>>();
        if (dataTable != null)
        {
            int rowsToPreview = Math.Min(20, dataTable.Rows.Count);

            for (int i = 0; i < rowsToPreview; i++)
            {
                var rowDict = new Dictionary<string, object>();
                foreach (DataColumn col in dataTable.Columns)
                {
                    rowDict[col.ColumnName] = dataTable.Rows[i][col];
                }
                previewList.Add(rowDict);
            }
        }

        return new ExcelReportResult
        {
            ExcelBase64 = Convert.ToBase64String(excelBytes),
            PreviewData = previewList,
            Text = ragResponse.Text ?? string.Empty,
            SuggestedQuestions = ragResponse.SuggestedQuestions ?? new List<string>()
        };
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