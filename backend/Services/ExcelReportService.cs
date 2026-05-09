using OfficeOpenXml;
using System.Data;
using System.Text.Json;
using Backend.Services;
using Backend.Models;

public class ExcelReportResult
{
    public string ExcelBase64 { get; set; } = string.Empty;
    public List<Dictionary<string, object>> PreviewData { get; set; } = new();
    public string Text { get; set; } = string.Empty;
    public List<string> SuggestedQuestions { get; set; } = new();
}

public class ExcelReportService
{
    // Lần này ta gọi thẳng RagOrchestrator, thay vì gọi lắt nhắt từng service
    private readonly RagOrchestrator _ragOrchestrator;

    public ExcelReportService(RagOrchestrator ragOrchestrator)
    {
        _ragOrchestrator = ragOrchestrator;
        ExcelPackage.License.SetNonCommercialPersonal("My Project"); // Chuẩn bản quyền EPPlus 8
    }

    public async Task<ExcelReportResult> ProcessExcelTemplateAsync(Stream excelStream, string? additionalQuery, Func<RagStep, Task> onStep, CancellationToken ct)
    {
        using var package = new ExcelPackage(excelStream);
        var worksheet = package.Workbook.Worksheets[0];

        // Gửi step thông báo bắt đầu xử lý
        await onStep(new RagStep("Excel Template Analysis", "Đang phân tích cấu trúc file template và trích xuất các cột tiêu đề..."));

        // 1. Tìm Header Row (Quét 15 dòng đầu để tìm dòng có nhiều cột nhất)
        var columns = new List<string>();
        int headerRowIndex = 1;
        int maxHeaderCols = 0;

        if (worksheet.Dimension != null)
        {
            int maxColsToScan = Math.Min(worksheet.Dimension.End.Column, 50);
            int maxRowsToScan = Math.Min(worksheet.Dimension.End.Row, 15); // Tăng lên 15 dòng cho chắc

            for (int r = 1; r <= maxRowsToScan; r++)
            {
                var tempColumns = new List<string>();
                for (int c = 1; c <= maxColsToScan; c++)
                {
                    var val = worksheet.Cells[r, c].Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(val)) 
                    {
                        tempColumns.Add(val);
                    }
                }

                // Nếu dòng này có nhiều cột hơn dòng trước đó -> Coi là Header
                if (tempColumns.Count > maxHeaderCols)
                {
                    maxHeaderCols = tempColumns.Count;
                    columns = tempColumns;
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

        // 2. GỘP CÂU QUERY CỦA USER + YÊU CẦU CỘT EXCEL
        string combinedQuery;
        var mappingInstructions = $"\n\nYÊU CẦU ĐẶC BIỆT CHO BÁO CÁO EXCEL:\n" +
                                   $"- Dữ liệu trả về BẮT BUỘC phải có các cột tiêu đề sau: {columnsStr}.\n" +
                                   $"- Quan trọng: Hãy ánh xạ (map) các tiêu đề này với trường tương ứng trong database (ví dụ: 'TenKhachHang' map với 'KhachHang', 'StepName' map với 'cd_name').\n" +
                                   $"- Nếu không tìm thấy cột tương ứng, hãy để trống hoặc dùng NULL, đừng cố đoán bừa.\n" +
                                   $"- BẮT BUỘC sử dụng ALIAS (AS) để tên cột trong kết quả trả về khớp hoàn toàn với tên tiêu đề Excel (ví dụ: SELECT KhachHang AS [TenKhachHang], cd_name AS [StepName] ...).";

        if (string.IsNullOrWhiteSpace(additionalQuery))
        {
            combinedQuery = $"Hãy lấy toàn bộ dữ liệu cần thiết để điền vào báo cáo theo các cột được yêu cầu." + mappingInstructions;
        }
        else
        {
            combinedQuery = $"{additionalQuery.Trim()}." + mappingInstructions;
        }

        // 3. Chạy toàn bộ luồng RAG (Embeddings -> Qdrant Schema -> Vertex SQL -> Execute)
        var ragResponse = await _ragOrchestrator.ProcessQueryAsync(combinedQuery, null, onStep, ct);

        // 4. Lấy kết quả Json
        var rawJson = ragResponse.RawData;
        if (string.IsNullOrWhiteSpace(rawJson) || rawJson.Contains("[ERROR]"))
        {
            throw new Exception("AI không thể sinh được SQL hợp lệ: " + rawJson);
        }

        // 5. Convert JSON -> DataTable theo đúng thứ tự cột của Template
        // Chúng ta không dùng trực tiếp ragResponse.Data vì nó có thể sai thứ tự cột so với template
        var dataTable = ConvertJsonToDataTable(rawJson, columns);

        // 5. Xóa dữ liệu mẫu (dummy data) và điền data mới
        int dataStartRow = headerRowIndex + 1;
        if (worksheet.Dimension != null && worksheet.Dimension.End.Row >= dataStartRow)
        {
            // Xóa sạch từ dòng sau Header đến hết sheet
            worksheet.Cells[dataStartRow, 1, worksheet.Dimension.End.Row, worksheet.Dimension.End.Column].Clear();
        }

        // Đổ dữ liệu mới
        if (dataTable.Rows.Count > 0)
        {
            // Sử dụng LoadFromDataTable để đổ dữ liệu nhanh
            var dataRange = worksheet.Cells[dataStartRow, 1];
            dataRange.LoadFromDataTable(dataTable, PrintHeaders: false);

            // Căn lề cho cột số (không ép định dạng .00 để tránh lỗi hiển thị ID)
            for (int i = 0; i < dataTable.Columns.Count; i++)
            {
                var column = dataTable.Columns[i];
                if (column.ExtendedProperties["IsNumeric"] is bool isNum && isNum)
                {
                    var colRange = worksheet.Cells[dataStartRow, i + 1, dataStartRow + dataTable.Rows.Count - 1, i + 1];
                    colRange.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
                    // Không set Numberformat.Format ở đây để Excel tự hiển thị tự nhiên
                }
            }
        }
        else 
        {
            await onStep(new RagStep("Excel Export", "⚠️ Không có dữ liệu để điền vào báo cáo."));
        }

        // 5.5 Thêm Border bao quanh vùng dữ liệu (Header + Data)
        if (worksheet.Dimension != null)
        {
            var range = worksheet.Cells[headerRowIndex, 1, Math.Max(headerRowIndex, worksheet.Dimension.End.Row), columns.Count];
            range.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            range.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
        }
        
        // Tối ưu tốc độ: Chỉ AutoFit 50 dòng đầu tiên thay vì toàn bộ sheet (rất chậm nếu file lớn)
        int rowsToFit = Math.Min(50, worksheet.Dimension?.End.Row ?? 1);
        int colsToFit = worksheet.Dimension?.End.Column ?? 1;
        worksheet.Cells[1, 1, rowsToFit, colsToFit].AutoFitColumns();
        var excelBytes = package.GetAsByteArray();

        // 6. Trích xuất TOP 20 dòng làm Preview
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


    // Hàm helper để biến JSON thành DataTable theo đúng cấu trúc cột của Template
    private DataTable ConvertJsonToDataTable(string jsonString, List<string> templateColumns)
    {
        var dataTable = new DataTable();
        
        // 1. Tạo các cột dựa trên template (để đảm bảo đúng thứ tự)
        foreach (var colName in templateColumns)
        {
            var col = dataTable.Columns.Add(colName, typeof(object));
            col.ExtendedProperties["IsNumeric"] = false; // Mặc định là false
        }

        using var jsonDoc = JsonDocument.Parse(jsonString);
        var root = jsonDoc.RootElement;

        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
        {
            var elements = root.EnumerateArray().ToList();
            
            // 2. Xác định kiểu dữ liệu (Numeric vs String) cho từng cột dựa trên sample data
            var sampleElements = elements.Take(50).ToList();
            foreach (DataColumn col in dataTable.Columns)
            {
                string colName = col.ColumnName;
                bool hasData = false;
                bool allNumbers = true;

                foreach (var el in sampleElements)
                {
                    if (el.TryGetProperty(colName, out var prop))
                    {
                        if (prop.ValueKind == JsonValueKind.Null || prop.ValueKind == JsonValueKind.Undefined)
                            continue;

                        var strVal = prop.ToString().Trim();
                        if (string.IsNullOrWhiteSpace(strVal)) continue;

                        hasData = true;
                        if (!double.TryParse(strVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
                        {
                            allNumbers = false;
                            break;
                        }
                    }
                }
                col.ExtendedProperties["IsNumeric"] = hasData && allNumbers;
            }

            // 3. Đổ dữ liệu vào hàng
            foreach (var element in elements)
            {
                var row = dataTable.NewRow();
                bool hasAnyData = false;

                foreach (DataColumn col in dataTable.Columns)
                {
                    string colName = col.ColumnName;
                    bool isNumeric = (bool)col.ExtendedProperties["IsNumeric"]!;

                    if (element.TryGetProperty(colName, out var prop) && 
                        prop.ValueKind != JsonValueKind.Null && 
                        prop.ValueKind != JsonValueKind.Undefined)
                    {
                        var strVal = prop.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(strVal))
                        {
                            hasAnyData = true;
                            if (isNumeric && double.TryParse(strVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                            {
                                row[colName] = val;
                            }
                            else
                            {
                                row[colName] = strVal;
                            }
                        }
                        else 
                        {
                            row[colName] = DBNull.Value; // Để trống ô nếu không có dữ liệu
                        }
                    }
                    else
                    {
                        row[colName] = DBNull.Value; // Để trống ô nếu không có dữ liệu
                    }
                }

                // Chỉ add hàng nếu nó thực sự có dữ liệu (tránh hàng trống do AI sinh bậy)
                if (hasAnyData)
                {
                    dataTable.Rows.Add(row);
                }
            }
        }
        
        return dataTable;
    }
}