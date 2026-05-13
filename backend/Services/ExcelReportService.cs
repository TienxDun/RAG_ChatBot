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
            int maxRowsToScan = Math.Min(worksheet.Dimension.End.Row, 15); // 15 dòng

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

        // 4. Lấy kết quả
        DataTable finalDataTable;
        if (ragResponse.RawDataTable != null && ragResponse.RawDataTable.Rows.Count > 0)
        {
            // Nếu có DataTable gốc, chúng ta cần tạo một bản sao có các cột đúng thứ tự như Template
            finalDataTable = new DataTable();
            foreach (var colName in columns)
            {
                finalDataTable.Columns.Add(colName, typeof(object));
            }

            foreach (DataRow sourceRow in ragResponse.RawDataTable.Rows)
            {
                var newRow = finalDataTable.NewRow();
                foreach (var colName in columns)
                {
                    // Tìm cột tương ứng trong data gốc (AI đã được dặn dùng AS để khớp tên)
                    if (ragResponse.RawDataTable.Columns.Contains(colName))
                    {
                        newRow[colName] = sourceRow[colName];
                    }
                    else 
                    {
                        newRow[colName] = DBNull.Value;
                    }
                }
                finalDataTable.Rows.Add(newRow);
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
            finalDataTable = ConvertJsonToDataTable(rawJson, columns);
        }

        var dataTable = finalDataTable; // Sử dụng bảng đã được chuẩn hóa

        // 5. Xóa dữ liệu mẫu (dummy data) và điền data mới

        // 5. Xóa dữ liệu mẫu (dummy data) và điền data mới
        int dataStartRow = headerRowIndex + 1;
        if (worksheet.Dimension != null && worksheet.Dimension.End.Row >= dataStartRow)
        {
            worksheet.Cells[dataStartRow, 1, worksheet.Dimension.End.Row, worksheet.Dimension.End.Column].Clear();
        }

        // Đổ dữ liệu mới
        if (dataTable.Rows.Count > 0)
        {
            var dataRange = worksheet.Cells[dataStartRow, 1];
            dataRange.LoadFromDataTable(dataTable, PrintHeaders: false);

            for (int i = 0; i < dataTable.Columns.Count; i++)
            {
                var column = dataTable.Columns[i];
                if (column.ExtendedProperties["IsNumeric"] is bool isNum && isNum)
                {
                    var colRange = worksheet.Cells[dataStartRow, i + 1, dataStartRow + dataTable.Rows.Count - 1, i + 1];
                    colRange.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
                }
            }
        }
        else 
        {
            await onStep(new RagStep("Excel Export", "⚠️ Không có dữ liệu để điền vào báo cáo."));
        }

        if (worksheet.Dimension != null)
        {
            var range = worksheet.Cells[headerRowIndex, 1, Math.Max(headerRowIndex, worksheet.Dimension.End.Row), columns.Count];
            range.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            range.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
        }
        
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

    private DataTable ConvertJsonToDataTable(string jsonString, List<string> templateColumns)
    {
        var dataTable = new DataTable();
        
        foreach (var colName in templateColumns)
        {
            var col = dataTable.Columns.Add(colName, typeof(object));
            col.ExtendedProperties["IsNumeric"] = false;
        }

        using var jsonDoc = JsonDocument.Parse(jsonString);
        var root = jsonDoc.RootElement;

        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
        {
            var elements = root.EnumerateArray().ToList();
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
                            row[colName] = DBNull.Value;
                        }
                    }
                    else
                    {
                        row[colName] = DBNull.Value;
                    }
                }

                if (hasAnyData)
                {
                    dataTable.Rows.Add(row);
                }
            }
        }
        
        return dataTable;
    }

    public byte[] ExportGenericExcel(List<Dictionary<string, object>> data)
    {
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Data Export");

        if (data == null || data.Count == 0)
        {
            return package.GetAsByteArray();
        }

        var headers = data[0].Keys.ToList();

        for (int i = 0; i < headers.Count; i++)
        {
            var cell = worksheet.Cells[1, i + 1];
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(157, 186, 217));
            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
            cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
            
            cell.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            cell.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            cell.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            cell.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
        }

        for (int rowIndex = 0; rowIndex < data.Count; rowIndex++)
        {
            for (int colIndex = 0; colIndex < headers.Count; colIndex++)
            {
                var cell = worksheet.Cells[rowIndex + 2, colIndex + 1];
                var val = data[rowIndex][headers[colIndex]];

                if (val is JsonElement element)
                {
                    switch (element.ValueKind)
                    {
                        case JsonValueKind.Number:
                            if (element.TryGetInt64(out long l)) cell.Value = l;
                            else cell.Value = element.GetDouble();
                            break;
                        case JsonValueKind.True:
                        case JsonValueKind.False:
                            cell.Value = element.GetBoolean();
                            break;
                        case JsonValueKind.Null:
                            cell.Value = null;
                            break;
                        case JsonValueKind.String:
                            var str = element.GetString() ?? "";
                            // Thử parse số để định dạng đúng trong Excel nếu chuỗi chỉ chứa số
                            if (double.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dbl))
                            {
                                cell.Value = dbl;
                            }
                            else
                            {
                                cell.Value = str;
                            }
                            break;
                        default:
                            cell.Value = element.ToString();
                            break;
                    }
                }
                else
                {
                    cell.Value = val;
                }

                // Căn lề phải và format dấu phẩy cho các cột số cho chuyên nghiệp
                if (cell.Value is double || cell.Value is float || cell.Value is decimal || cell.Value is long || cell.Value is int)
                {
                    cell.Style.Numberformat.Format = "#,##0";
                    cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
                }

                cell.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            }
        }

        if (headers.Count > 0)
        {
            worksheet.Cells[1, 1, data.Count + 1, headers.Count].AutoFitColumns();
        }

        return package.GetAsByteArray();
    }
}