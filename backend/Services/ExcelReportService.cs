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

        // 1. Quét tìm Header trong Excel
        var columns = new List<string>();
        int colCount = worksheet.Dimension.Columns;
        for (int col = 1; col <= colCount; col++)
        {
            var headerText = worksheet.Cells[1, col].Text;
            if (!string.IsNullOrWhiteSpace(headerText)) columns.Add(headerText);
        }

        var columnsStr = string.Join(", ", columns);

        // 2. GỘP CÂU QUERY CỦA USER + YÊU CẦU CỘT EXCEL
        string combinedQuery;
        if (string.IsNullOrWhiteSpace(additionalQuery))
        {
            combinedQuery = $"Hãy lấy toàn bộ dữ liệu, chỉ bao gồm các cột sau: {columnsStr}";
        }
        else
        {
            combinedQuery = $"{additionalQuery.Trim()}. Lưu ý BẮT BUỘC chỉ SELECT các cột sau: {columnsStr}";
        }

        // 3. Chạy toàn bộ luồng RAG (Embeddings -> Qdrant Schema -> Vertex SQL -> Execute)
        var ragResponse = await _ragOrchestrator.ProcessQueryAsync(combinedQuery, onStep, ct);

        // 4. Lấy kết quả Json
        var rawJson = ragResponse.RawData;
        if (string.IsNullOrWhiteSpace(rawJson) || rawJson.Contains("[ERROR]"))
        {
            throw new Exception("AI không thể sinh được SQL hợp lệ: " + rawJson);
        }

        // 5. Convert JSON -> DataTable và đổ vào Excel (giữ nguyên format)
        var dataTable = ConvertJsonToDataTable(rawJson);

        // Xóa toàn bộ dữ liệu mẫu (dummy data) của template từ dòng 2 trở đi để có 1 file mới hoàn toàn sạch sẽ
        if (worksheet.Dimension != null && worksheet.Dimension.End.Row >= 2)
        {
            worksheet.DeleteRow(2, worksheet.Dimension.End.Row - 1);
        }

        // Đổ dữ liệu mới vào từ dòng A2
        worksheet.Cells["A2"].LoadFromDataTable(dataTable, PrintHeaders: false);

        // 5.5 Thêm Border bao quanh dữ liệu đã điền
        if (worksheet.Dimension != null)
        {
            var range = worksheet.Cells[1, 1, worksheet.Dimension.End.Row, worksheet.Dimension.End.Column];
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


    // Hàm helper nhỏ để biến JSON từ SqlService thành DataTable cho EPPlus
    private DataTable ConvertJsonToDataTable(string jsonString)
    {
        var dataTable = new DataTable();
        using var jsonDoc = JsonDocument.Parse(jsonString);
        var root = jsonDoc.RootElement;

        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
        {
            var elements = root.EnumerateArray().ToList();
            var firstRow = elements[0];
            
            // BƯỚC 1: Quét trước 50 dòng đầu tiên (để tối ưu tốc độ) để xác định xem cột nào 100% là Số, cột nào là Chữ
            // Việc này giúp tránh lỗi "thục ra thục vô" (Ví dụ cột Mã SP có mã "1001" bị hiểu thành số lề phải, còn "A001" bị hiểu thành chữ lề trái)
            var columnTypes = new Dictionary<string, Type>();
            var sampleElements = elements.Take(50).ToList();

            foreach (var property in firstRow.EnumerateObject())
            {
                string colName = property.Name;
                bool allNumbers = true;
                bool hasData = false;

                foreach (var el in sampleElements)
                {
                    if (el.TryGetProperty(colName, out var prop))
                    {
                        if (prop.ValueKind == JsonValueKind.Null || prop.ValueKind == JsonValueKind.Undefined)
                            continue;

                        var strVal = prop.ToString();
                        if (string.IsNullOrWhiteSpace(strVal)) continue;

                        hasData = true;
                        // Kiểm tra xem nó có THẬT SỰ là số không
                        if (!double.TryParse(strVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
                        {
                            allNumbers = false;
                            break;
                        }
                    }
                }

                // Nếu tất cả các dòng của cột này đều là số -> Cột số. Nếu có lẫn lộn chữ -> Cột chữ.
                columnTypes[colName] = (hasData && allNumbers) ? typeof(double) : typeof(string);
                dataTable.Columns.Add(colName, typeof(object)); 
            }

            // BƯỚC 2: Đổ data vào theo đúng type đã thống nhất của cột đó
            foreach (var element in elements)
            {
                var row = dataTable.NewRow();
                foreach (var col in columnTypes)
                {
                    string colName = col.Key;
                    Type colType = col.Value;

                    if (!element.TryGetProperty(colName, out var prop) || 
                        prop.ValueKind == JsonValueKind.Null || 
                        prop.ValueKind == JsonValueKind.Undefined)
                    {
                        row[colName] = colType == typeof(double) ? 0.0 : "";
                        continue;
                    }

                    var strVal = prop.ToString();
                    if (string.IsNullOrWhiteSpace(strVal))
                    {
                        row[colName] = colType == typeof(double) ? 0.0 : ""; // Cột số thì cho bằng 0 thay vì để trống
                        continue;
                    }

                    if (colType == typeof(double))
                    {
                        if (double.TryParse(strVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedDouble))
                        {
                            row[colName] = parsedDouble; // Canh phải thẳng tắp
                        }
                        else
                        {
                            row[colName] = 0.0; // Nếu parse lỗi cũng gán bằng 0
                        }
                    }
                    else
                    {
                        row[colName] = strVal; // Canh trái thẳng tắp
                    }
                }
                dataTable.Rows.Add(row);
            }
        }
        return dataTable;
    }
}
