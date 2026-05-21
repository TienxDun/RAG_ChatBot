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

            // Nếu cột 1 có chữ ở ít nhất 30% số hàng quét, và cột 2 trống phần lớn (số lượng chữ < 40% so với cột 1)
            if (scannedRows > 0 && textCountInCol1 >= 1)
            {
                if (textCountInCol2 == 0 || (double)textCountInCol2 / textCountInCol1 < 0.4)
                {
                    hasVerticalPhysicalTraits = true;
                }
            }
        }

        bool isVerticalTemplate = columns.Count == 2 && (hasVerticalKeywords || hasVerticalPhysicalTraits);

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

        // 4. Lấy kết quả
        DataTable dataTable;
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
            dataTable = ConvertJsonToDataTable(rawJson, columns);
        }

        // 5. Xóa dữ liệu mẫu (dummy data) và điền data mới
        int dataStartRow = headerRowIndex + 1;

        if (isVerticalTemplate)
        {
            // Điền theo dạng dọc từ bảng markdown của ragResponse.Text với vị trí cột/hàng động
            FillTemplateWorksheetFromMarkdown(worksheet, ragResponse.Text ?? string.Empty, headerRowIndex, labelColumnIndex, valueColumnIndex);
            
            // Xây dựng dataTable từ markdown table để phục vụ cho việc tạo PreviewData ở cuối
            var parsedTable = ParseMarkdownTable(ragResponse.Text ?? string.Empty);
            dataTable = new DataTable();
            dataTable.Columns.Add(columns[0], typeof(object));
            dataTable.Columns.Add(columns[1], typeof(object));
            
            // Skip the first row if it contains headers (like "Thông tin" and "Nội dung")
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
            if (worksheet.Dimension != null && worksheet.Dimension.End.Row >= dataStartRow)
            {
                // Chỉ xóa dữ liệu trong vùng các cột tiêu đề của báo cáo ngang
                worksheet.Cells[dataStartRow, startColumnIndex, worksheet.Dimension.End.Row, startColumnIndex + columns.Count - 1].Value = null;
            }

            // Đổ dữ liệu mới
            if (dataTable.Rows.Count > 0)
            {
                var dataRange = worksheet.Cells[dataStartRow, startColumnIndex];
                dataRange.LoadFromDataTable(dataTable, PrintHeaders: false);

                for (int i = 0; i < dataTable.Columns.Count; i++)
                {
                    var column = dataTable.Columns[i];
                    var colRange = worksheet.Cells[dataStartRow, startColumnIndex + i, dataStartRow + dataTable.Rows.Count - 1, startColumnIndex + i];

                    if (column.ExtendedProperties["IsNumeric"] is bool isNum && isNum)
                    {
                        colRange.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
                    }

                    // Định dạng ngày tháng
                    bool isDateColumn = column.DataType == typeof(DateTime) || 
                                        column.DataType == typeof(DateTimeOffset) ||
                                        column.ColumnName.Contains("Ngay", StringComparison.OrdinalIgnoreCase) ||
                                        column.ColumnName.Contains("Date", StringComparison.OrdinalIgnoreCase) ||
                                        column.ColumnName.Contains("Time", StringComparison.OrdinalIgnoreCase);

                    if (isDateColumn)
                    {
                        colRange.Style.Numberformat.Format = "dd/MM/yyyy";
                        colRange.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                    }
                }
            }
            else 
            {
                await onStep(new RagStep("Excel Export", "⚠️ Không có dữ liệu để điền vào báo cáo."));
            }
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

        // 1. Tô màu nền nhẹ nhàng, chuyên nghiệp cho Hàng tiêu đề chính
        var headerRange = worksheet.Cells[headerRowIndex, startColumnIndex, headerRowIndex, startColumnIndex + columns.Count - 1];
        headerRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
        headerRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(232, 240, 248)); // Xanh dương pastel thanh lịch
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.Color.SetColor(System.Drawing.Color.FromArgb(51, 51, 51)); // Màu chữ xám đậm dễ nhìn
        headerRange.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
        headerRange.Style.Font.Name = "Segoe UI";

        // 2. Định dạng cột nhãn cho template dọc để tăng chiều sâu trực quan
        if (isVerticalTemplate && endRowOfData > headerRowIndex)
        {
            var labelRange = worksheet.Cells[headerRowIndex + 1, labelColumnIndex, endRowOfData, labelColumnIndex];
            labelRange.Style.Font.Bold = true;
            labelRange.Style.Font.Color.SetColor(System.Drawing.Color.FromArgb(64, 64, 64)); // Màu nhãn xám đậm
            labelRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            labelRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(250, 250, 250)); // Nền xám cực nhẹ để phân biệt
        }

        // 3. Dọn dẹp border thừa và thiết lập border xám nhạt tinh tế cho vùng bảng dữ liệu thực tế
        if (worksheet.Dimension != null)
        {
            int totalRows = worksheet.Dimension.End.Row;
            int totalCols = worksheet.Dimension.End.Column;
            var borderColor = System.Drawing.Color.FromArgb(208, 215, 222); // Màu xám nhạt chuẩn hiện đại

            for (int r = 1; r <= totalRows; r++)
            {
                for (int c = 1; c <= totalCols; c++)
                {
                    var cell = worksheet.Cells[r, c];
                    bool isInTableRange = r >= headerRowIndex && r <= endRowOfData && 
                                         c >= startColumnIndex && c <= startColumnIndex + columns.Count - 1;

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
        
        int rowsToFit = Math.Min(50, worksheet.Dimension?.End.Row ?? 1);
        int colsToFit = worksheet.Dimension?.End.Column ?? 1;
        worksheet.Cells[1, 1, rowsToFit, colsToFit].AutoFitColumns(12);
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

    // Chuyển dữ liệu chuỗi JSON từ kết quả truy vấn thành DataTable và parse định dạng ngày tháng
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
                            else if (DateTime.TryParse(strVal, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime dtVal))
                            {
                                row[colName] = dtVal;
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

    // Xuất danh sách dữ liệu ra file Excel dạng bảng lưới thông thường, định dạng số và ngày tháng (dd/MM/yyyy)
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
                            else if (DateTime.TryParse(str, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime dtVal))
                            {
                                cell.Value = dtVal;
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
                if (cell.Value is double || cell.Value is float || cell.Value is decimal)
                {
                    cell.Style.Numberformat.Format = "#,##0.00";
                    cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
                }
                else if (cell.Value is long || cell.Value is int)
                {
                    cell.Style.Numberformat.Format = "#,##0";
                    cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
                }
                else if (cell.Value is DateTime || cell.Value is DateTimeOffset || headers[colIndex].Contains("Ngay", StringComparison.OrdinalIgnoreCase) || headers[colIndex].Contains("Date", StringComparison.OrdinalIgnoreCase) || headers[colIndex].Contains("Time", StringComparison.OrdinalIgnoreCase))
                {
                    // Nếu là string dạng ngày, thử chuyển sang DateTime để format chuẩn
                    if (cell.Value is string strDate && DateTime.TryParse(strDate, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                    {
                        cell.Value = parsedDate;
                    }

                    if (cell.Value is DateTime || cell.Value is DateTimeOffset)
                    {
                        cell.Style.Numberformat.Format = "dd/MM/yyyy";
                        cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                    }
                }

                cell.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
            }
        }

        if (headers.Count > 0)
        {
            worksheet.Cells[1, 1, data.Count + 1, headers.Count].AutoFitColumns(12);
        }

        return package.GetAsByteArray();
    }

    // Phân tách văn bản Markdown chứa bảng thành danh sách lưới dữ liệu
    private List<List<string>> ParseMarkdownTable(string markdownText)
    {
        var rows = new List<List<string>>();
        if (string.IsNullOrWhiteSpace(markdownText)) return rows;

        var lines = markdownText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("|") || !trimmed.EndsWith("|")) continue;

            // Bỏ qua dòng phân cách bảng như |---|---|
            if (trimmed.Contains("---")) continue;

            var cells = trimmed.Split('|')
                               .Skip(1)
                               .Take(trimmed.Split('|').Length - 2)
                               .Select(c => c.Trim().Replace("**", ""))
                               .ToList();

            if (cells.Count > 0)
            {
                rows.Add(cells);
            }
        }
        return rows;
    }

    // Tự sinh file Excel từ bảng Markdown với phong cách hiện đại và định dạng chuyên nghiệp
    public byte[] ExportMarkdownToExcelDynamic(string markdownText)
    {
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Data Export");

        var tableRows = ParseMarkdownTable(markdownText);
        if (tableRows.Count == 0) return package.GetAsByteArray();

        bool isVerticalTable = tableRows[0].Count == 2;
        int currentRow = 1;
        for (int r = 0; r < tableRows.Count; r++)
        {
            var rowData = tableRows[r];
            for (int c = 0; c < rowData.Count; c++)
            {
                var cell = worksheet.Cells[currentRow, c + 1];
                string rawValue = rowData[c];

                if (double.TryParse(rawValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double num))
                {
                    cell.Value = num;
                    cell.Style.Numberformat.Format = "#,##0";
                    cell.Style.HorizontalAlignment = isVerticalTable ? 
                        OfficeOpenXml.Style.ExcelHorizontalAlignment.Left : 
                        OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
                }
                else if (TryParseDateTime(rawValue, out DateTime dt))
                {
                    cell.Value = dt;
                    cell.Style.Numberformat.Format = "dd/MM/yyyy";
                    cell.Style.HorizontalAlignment = isVerticalTable ? 
                        OfficeOpenXml.Style.ExcelHorizontalAlignment.Left : 
                        OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                }
                else
                {
                    cell.Value = rawValue;
                    if (isVerticalTable)
                    {
                        cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Left;
                    }
                }

                cell.Style.Font.Name = "Segoe UI";
                if (currentRow == 1)
                {
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(242, 242, 242));
                    cell.Style.Font.Color.SetColor(System.Drawing.Color.FromArgb(51, 51, 51));
                    cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                }
                else if (c == 0)
                {
                    cell.Style.Font.Bold = true;
                }

                cell.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                cell.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                
                var borderColor = System.Drawing.Color.FromArgb(220, 220, 220);
                cell.Style.Border.Top.Color.SetColor(borderColor);
                cell.Style.Border.Bottom.Color.SetColor(borderColor);
                cell.Style.Border.Left.Color.SetColor(borderColor);
                cell.Style.Border.Right.Color.SetColor(borderColor);
            }
            currentRow++;
        }

        worksheet.Cells[1, 1, currentRow - 1, tableRows[0].Count].AutoFitColumns(12);
        return package.GetAsByteArray();
    }

    // Điền dữ liệu từ Markdown vào các ô của Excel Worksheet dựa trên nhãn và cột/hàng động
    private void FillTemplateWorksheetFromMarkdown(ExcelWorksheet worksheet, string markdownText, int headerRowIndex, int labelColumnIndex, int valueColumnIndex)
    {
        var tableRows = ParseMarkdownTable(markdownText);
        var markdownData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var row in tableRows)
        {
            if (row.Count >= 2)
            {
                string key = row[0].Trim().Replace("**", "");
                markdownData[key] = row[1].Trim().Replace("**", "");
            }
        }

        int endRow = worksheet.Dimension?.End.Row ?? (headerRowIndex + 15);
        for (int row = headerRowIndex + 1; row <= endRow; row++)
        {
            string labelInExcel = worksheet.Cells[row, labelColumnIndex].Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(labelInExcel)) continue;

            if (markdownData.TryGetValue(labelInExcel, out string? value))
            {
                var valueCell = worksheet.Cells[row, valueColumnIndex];
                
                if (double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double num))
                {
                    valueCell.Value = num;
                    valueCell.Style.Numberformat.Format = "#,##0";
                    valueCell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Left;
                }
                else if (TryParseDateTime(value, out DateTime dt))
                {
                    valueCell.Value = dt;
                    valueCell.Style.Numberformat.Format = "dd/MM/yyyy";
                    valueCell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Left;
                }
                else
                {
                    valueCell.Value = value;
                    valueCell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Left;
                }
            }
        }
    }

    private static bool TryParseDateTime(string value, out DateTime dt)
    {
        string[] dateFormats = new[] 
        { 
            "dd/MM/yyyy", "yyyy-MM-dd", "MM/dd/yyyy", 
            "dd-MM-yyyy", "yyyy/MM/dd", "dd/MM/yyyy HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss", "dd-MM-yyyy HH:mm:ss"
        };
        
        return DateTime.TryParseExact(value, dateFormats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt) ||
               DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt) ||
               DateTime.TryParse(value, out dt);
    }
}