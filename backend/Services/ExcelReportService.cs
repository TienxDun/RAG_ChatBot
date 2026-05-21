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
        await onStep(new RagStep("Excel Template Analysis", "Đang phân tích cấu trúc file template và trích xuất các cột tiêu đề..."));

        // 1. Tìm Header Row (Quét 15 dòng đầu để tìm dòng có nhiều cột nhất)
        var columns = new List<string>();
        var headerColumnIndices = new List<int>();
        int headerRowIndex = 1;
        int maxHeaderCols = 0;

        if (worksheet.Dimension != null)
        {
            int maxColsToScan = Math.Min(worksheet.Dimension.End.Column, 50);
            int maxRowsToScan = Math.Min(worksheet.Dimension.End.Row, 15); // 15 dòng

            for (int r = 1; r <= maxRowsToScan; r++)
            {
                var tempColumns = new List<string>();
                var tempColumnIndices = new List<int>();
                for (int c = 1; c <= maxColsToScan; c++)
                {
                    var val = worksheet.Cells[r, c].Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(val)) 
                    {
                        tempColumns.Add(val);
                        tempColumnIndices.Add(c);
                    }
                }

                // Nếu dòng này có nhiều cột hơn dòng trước đó -> Coi là Header
                if (tempColumns.Count > maxHeaderCols)
                {
                    maxHeaderCols = tempColumns.Count;
                    columns = tempColumns;
                    headerColumnIndices = tempColumnIndices;
                    headerRowIndex = r;
                }
            }
        }

        // Nếu không tìm thấy cột nào, thoát sớm
        if (columns.Count == 0)
        {
            throw new Exception("Không tìm thấy hàng tiêu đề (Header) trong file Excel template.");
        }

        var columnsStr = string.Join(", ", columns);
        int startColumnIndex = headerColumnIndices.Count > 0 ? headerColumnIndices[0] : 1;

        // Phát hiện sớm template dạng dọc bằng từ khóa mở rộng tiếng Việt/tiếng Anh
        var verticalKeywordsColumn1 = new[] { "thông", "thong", "label", "name", "field", "key", "chỉ tiêu", "chi tieu", "tiêu chí", "tieu chi", "nội dung", "noi dung", "danh mục", "danh muc", "mục", "muc", "yêu cầu", "yeu cau", "tên", "ten", "item", "description", "chỉ số", "chi so" };
        var verticalKeywordsColumn2 = new[] { "giá trị", "gia tri", "nội dung", "noi dung", "kết quả", "ket qua", "số liệu", "so lieu", "value", "result", "data", "amount", "quantity", "qty", "thông tin", "thong tin" };

        bool hasVerticalKeywords = false;
        if (columns.Count == 2)
        {
            bool col1Match = verticalKeywordsColumn1.Any(kw => columns[0].Contains(kw, StringComparison.OrdinalIgnoreCase));
            bool col2Match = verticalKeywordsColumn2.Any(kw => columns[1].Contains(kw, StringComparison.OrdinalIgnoreCase));
            hasVerticalKeywords = col1Match || col2Match;
        }

        // Kiểm tra thêm đặc trưng vật lý: Cột 1 có nhiều chữ tĩnh, cột 2 trống phần lớn để điền dữ liệu
        bool hasVerticalPhysicalTraits = false;
        if (columns.Count == 2 && worksheet.Dimension != null && headerColumnIndices.Count >= 2)
        {
            int labelCol = headerColumnIndices[0];
            int valCol = headerColumnIndices[1];
            int textCountInCol1 = 0;
            int textCountInCol2 = 0;
            
            int scanStartRow = headerRowIndex + 1;
            int scanEndRow = Math.Min(worksheet.Dimension.End.Row, headerRowIndex + 15);
            int scannedRows = 0;

            for (int r = scanStartRow; r <= scanEndRow; r++)
            {
                scannedRows++;
                if (!string.IsNullOrWhiteSpace(worksheet.Cells[r, labelCol].Text)) textCountInCol1++;
                if (!string.IsNullOrWhiteSpace(worksheet.Cells[r, valCol].Text)) textCountInCol2++;
            }

            // Nếu cột 1 có chữ ở nhất thiết 30% số hàng quét, và cột 2 trống phần lớn (số lượng chữ < 40% so với cột 1)
            if (scannedRows > 0 && textCountInCol1 >= 1)
            {
                if (textCountInCol2 == 0 || (double)textCountInCol2 / textCountInCol1 < 0.4)
                {
                    hasVerticalPhysicalTraits = true;
                }
            }

            // Kiểm tra thông minh: Nếu cột 1 chủ yếu chứa dữ liệu số (như STT 1, 2, 3...) thì chắc chắn không phải là template dọc
            int numericCount = 0;
            for (int r = scanStartRow; r <= scanEndRow; r++)
            {
                var cellText = worksheet.Cells[r, labelCol].Text?.Trim();
                if (!string.IsNullOrEmpty(cellText) && double.TryParse(cellText, out _))
                {
                    numericCount++;
                }
            }
            if (scannedRows > 0 && (double)numericCount / scannedRows > 0.5)
            {
                hasVerticalPhysicalTraits = false;
            }
        }

        // Loại trừ các trường hợp tiêu đề cột 1 là STT, No, Index, ID,... (đặc trưng của bảng ngang 2 cột)
        bool isHorizontalCol1Header = false;
        if (columns.Count == 2)
        {
            var horizontalKeywordsColumn1 = new[] { "stt", "số tt", "số tt", "no", "no.", "index", "id", "seq" };
            isHorizontalCol1Header = horizontalKeywordsColumn1.Any(kw => columns[0].Equals(kw, StringComparison.OrdinalIgnoreCase) || columns[0].StartsWith(kw + " ", StringComparison.OrdinalIgnoreCase));
        }

        bool isVerticalTemplate = columns.Count == 2 && !isHorizontalCol1Header && (hasVerticalKeywords || hasVerticalPhysicalTraits);

        int labelColumnIndex = isVerticalTemplate && headerColumnIndices.Count > 0 ? headerColumnIndices[0] : 1;
        int valueColumnIndex = isVerticalTemplate && headerColumnIndices.Count > 1 ? headerColumnIndices[1] : 2;

        var verticalLabels = new List<string>();
        if (isVerticalTemplate && worksheet.Dimension != null)
        {
            for (int r = headerRowIndex + 1; r <= worksheet.Dimension.End.Row; r++)
            {
                var val = worksheet.Cells[r, labelColumnIndex].Text?.Trim();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    verticalLabels.Add(val);
                }
            }
        }

        // Gửi step chi tiết các cột/nhãn đã phân tích được từ file Excel
        string excelAnalysisContent;
        if (isVerticalTemplate)
        {
            excelAnalysisContent = $"Đã phát hiện **File mẫu dạng dọc (Vertical Template)** (hàng tiêu đề số **{headerRowIndex}**).\n\n" +
                                   $"**Các cột tiêu đề:** `{columns[0]}` và `{columns[1]}`\n\n" +
                                   $"**Danh sách {verticalLabels.Count} nhãn dữ liệu cần điền phát hiện được ở cột '{columns[0]}':**\n" +
                                   $"```json\n{JsonSerializer.Serialize(verticalLabels, new JsonSerializerOptions { WriteIndented = true })}\n```";
        }
        else
        {
            excelAnalysisContent = $"Đã trích xuất thành công cấu trúc hàng tiêu đề (hàng số **{headerRowIndex}**).\n\n" +
                                   $"**Danh sách {columns.Count} cột tiêu đề phát hiện được:**\n" +
                                   $"```json\n{JsonSerializer.Serialize(columns, new JsonSerializerOptions { WriteIndented = true })}\n```";
        }
        await onStep(new RagStep("Excel Template Analysis", excelAnalysisContent));

        // 2. GỘP CÂU QUERY CỦA USER + YÊU CẦU CỘT EXCEL
        string combinedQuery;
        string mappingInstructions;
        if (isVerticalTemplate)
        {
            var labelsListStr = string.Join("\n", verticalLabels.Select(lbl => $"  - {lbl}"));
            mappingInstructions = $"\n\nYÊU CẦU ĐẶC BIỆT CHO BÁO CÁO EXCEL DẠNG DỌC:\n" +
                                   $"- Hệ thống phát hiện đây là mẫu báo cáo dạng dọc.\n" +
                                   $"- Bạn BẮT BUỘC phải truy vấn thông tin trong database để lấy dữ liệu cho các nhãn (labels) sau đây:\n" +
                                   $"{labelsListStr}\n" +
                                   $"- Định dạng kết quả trả về BẮT BUỘC phải là một bảng Markdown gồm đúng 2 cột: '{columns[0]}' và '{columns[1]}'.\n" +
                                   $"- Cột '{columns[0]}' chứa chính xác các nhãn trên.\n" +
                                   $"- Cột '{columns[1]}' chứa giá trị tương ứng tìm được từ database. Nếu nhãn nào không có dữ liệu, hãy để trống giá trị ở cột '{columns[1]}' (không bỏ sót bất kỳ nhãn nào).\n" +
                                   $"- Hãy đảm bảo tên của nhãn khớp hoàn toàn (ví dụ: 'Ngày kiểm tra', 'Chuyền', 'Mã hàng', 'Size', 'Nhân viên KCS', 'Tổng số lượng lỗi').";
        }
        else
        {
            mappingInstructions = $"\n\nYÊU CẦU ĐẶC BIỆT CHO BÁO CÁO EXCEL:\n" +
                                   $"- Dữ liệu trả về BẮT BUỘC phải có các cột tiêu đề sau: {columnsStr}.\n" +
                                   $"- Quan trọng: Hãy ánh xạ (map) các tiêu đề này với trường tương ứng trong database (ví dụ: 'TenKhachHang' map với 'KhachHang', 'StepName' map với 'cd_name').\n" +
                                   $"- Nếu không tìm thấy cột tương ứng, hãy để trống hoặc dùng NULL, đừng cố đoán bừa.\n" +
                                   $"- BẮT BUỘC sử dụng ALIAS (AS) để tên cột trong kết quả trả về khớp hoàn toàn với tên tiêu đề Excel (ví dụ: SELECT KhachHang AS [TenKhachHang], cd_name AS [StepName] ...).";
        }

        if (string.IsNullOrWhiteSpace(additionalQuery))
        {
            combinedQuery = isVerticalTemplate 
                ? $"Hãy lấy toàn bộ dữ liệu cần thiết để điền vào báo cáo dạng dọc theo các nhãn được yêu cầu.{mappingInstructions}"
                : $"Hãy lấy toàn bộ dữ liệu cần thiết để điền vào báo cáo theo các cột được yêu cầu.{mappingInstructions}";
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
            // Nếu có DataTable gốc, chúng ta cần tạo một bản sao có các cột đúng thứ tự như Template
            dataTable = new DataTable();
            foreach (var colName in columns)
            {
                dataTable.Columns.Add(colName, typeof(object));
            }

            foreach (DataRow sourceRow in ragResponse.RawDataTable.Rows)
            {
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

        // 5. Xóa dữ liệu mẫu (dummy data) và điền data mới
        if (isVerticalTemplate)
        {
            // Điền theo dạng dọc từ bảng markdown của ragResponse.Text với vị trí cột/hàng động
            ExcelTemplateFiller.FillVerticalTemplate(worksheet, ragResponse.Text ?? string.Empty, headerRowIndex, labelColumnIndex, valueColumnIndex);
            
            // Xây dựng dataTable từ markdown table để phục vụ cho việc tạo PreviewData ở cuối
            var parsedTable = MarkdownTableParser.ParseMarkdownTable(ragResponse.Text ?? string.Empty);
            dataTable = new DataTable();
            dataTable.Columns.Add(columns[0], typeof(object));
            dataTable.Columns.Add(columns[1], typeof(object));
            
            int startIdx = 0;
            if (parsedTable.Count > 0 && parsedTable[0].Count == 2 && 
                (parsedTable[0][0].Contains("Thông", StringComparison.OrdinalIgnoreCase) || parsedTable[0][0].Contains("Thong", StringComparison.OrdinalIgnoreCase)))
            {
                startIdx = 1;
            }
            
            for (int i = startIdx; i < parsedTable.Count; i++)
            {
                var row = dataTable.NewRow();
                row[columns[0]] = parsedTable[i][0];
                row[columns[1]] = parsedTable[i].Count > 1 ? parsedTable[i][1] : "";
                dataTable.Rows.Add(row);
            }
        }
        else
        {
            ExcelTemplateFiller.FillHorizontalTemplate(worksheet, dataTable, headerRowIndex, startColumnIndex, columns.Count);
        }

        // Tìm dòng cuối cùng chứa dữ liệu thực tế của bảng
        int endRowOfData = headerRowIndex;
        if (isVerticalTemplate)
        {
            if (worksheet.Dimension != null)
            {
                for (int r = headerRowIndex + 1; r <= worksheet.Dimension.End.Row; r++)
                {
                    if (!string.IsNullOrWhiteSpace(worksheet.Cells[r, labelColumnIndex].Text))
                    {
                        endRowOfData = r;
                    }
                }
            }
        }
        else
        {
            endRowOfData = headerRowIndex + (dataTable?.Rows.Count ?? 0);
        }

        // 6. Áp dụng phong cách làm đẹp thẩm mỹ
        // Tô màu nền xanh pastel nhẹ cho Hàng tiêu đề chính
        ExcelStylingHelper.ApplyHeaderStyle(worksheet, headerRowIndex, startColumnIndex, columns.Count);

        // Định dạng cột nhãn cho template dọc (màu xám nhạt, in đậm) để tăng chiều sâu trực quan
        if (isVerticalTemplate)
        {
            ExcelStylingHelper.ApplyVerticalLabelStyle(worksheet, headerRowIndex, endRowOfData, labelColumnIndex);
        }
        else
        {
            // Áp dụng bộ lọc AutoFilter cho bảng báo cáo dạng ngang truyền thống
            ExcelStylingHelper.ApplyAutoFilter(worksheet, headerRowIndex, endRowOfData, startColumnIndex, columns.Count);
        }

        // Dọn dẹp border thừa và thiết lập border xám nhạt tinh tế cho vùng bảng dữ liệu thực tế
        ExcelStylingHelper.SanitizeBorders(worksheet, headerRowIndex, endRowOfData, startColumnIndex, columns.Count);

        int rowsToFit = Math.Min(50, worksheet.Dimension?.End.Row ?? 1);
        int colsToFit = worksheet.Dimension?.End.Column ?? 1;
        worksheet.Cells[1, 1, rowsToFit, colsToFit].AutoFitColumns(12);
        var excelBytes = package.GetAsByteArray();

        // 7. Trích xuất TOP 20 dòng làm Preview
        var previewList = new List<Dictionary<string, object>>();
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