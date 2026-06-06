using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Backend.Models;
using Backend.Services.Excel;
using OfficeOpenXml;

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
    private readonly IExcelTemplateAnalyzer _templateAnalyzer;
    private readonly IExcelTemplateFiller _templateFiller;
    private readonly IExcelExporter _excelExporter;
    private readonly ITextUtility _textUtility;
    private readonly IExcelMappingService _mappingService;


    public ExcelReportService(
        RagOrchestrator ragOrchestrator,
        IExcelTemplateAnalyzer templateAnalyzer,
        IExcelTemplateFiller templateFiller,
        IExcelExporter excelExporter,
        ITextUtility textUtility,
        IExcelMappingService mappingService)
    {
        _ragOrchestrator = ragOrchestrator;
        _templateAnalyzer = templateAnalyzer;
        _templateFiller = templateFiller;
        _excelExporter = excelExporter;
        _textUtility = textUtility;
        _mappingService = mappingService;
    }

    // Xử lý upload template Excel mẫu, chạy RAG query để lấy dữ liệu, điền vào template và xuất file
    public async Task<ExcelReportResult> ProcessExcelTemplateAsync(Stream stream, string fileName, string additionalQuery, Func<RagStep, Task> onStep, Func<string, Task> onFinalChunk, CancellationToken ct)
    {
        using var package = new ExcelPackage(stream);
        var worksheet = package.Workbook.Worksheets[0];

        // 1. Phân tích cấu trúc Template thông minh
        var templateInfo = _templateAnalyzer.AnalyzeTemplate(worksheet);

        // Nạp các cấu hình metadata từ MetadataCellMappings của file mapping vào templateInfo.Metadata
        var templateMapping = _mappingService.GetTemplateMapping(fileName);
        if (templateMapping.MetadataCellMappings != null && templateMapping.MetadataCellMappings.Count > 0)
        {
            foreach (var kvp in templateMapping.MetadataCellMappings)
            {
                bool keyIsCellAddress = Regex.IsMatch(kvp.Key.Trim(), @"^[A-Za-z]{1,3}\d{1,7}$");
                string semanticKey = keyIsCellAddress ? kvp.Value : kvp.Key;
                if (!string.IsNullOrWhiteSpace(semanticKey) && !templateInfo.Metadata.ContainsKey(semanticKey))
                {
                    templateInfo.Metadata[semanticKey] = keyIsCellAddress ? kvp.Key : kvp.Value;
                }
            }
        }


        var serializeOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        string excelAnalysisContent = $"Đã phân tích cấu trúc template thành công.\n\n" +
                                     $"* **Loại template**: {(templateInfo.Type == TemplateType.Hierarchical ? "Phân cấp (Hierarchical)" : "Đơn giản (Simple)")}\n" +
                                     $"* **Dòng tiêu đề chính**: {templateInfo.HeaderRowIndex}\n" +
                                     $"* **Cột bắt đầu**: {templateInfo.StartColumnIndex}\n\n" +
                                     $"**Danh sách {templateInfo.Columns.Count} tiêu đề cột đã nhận diện & làm phẳng:**\n" +
                                     $"```json\n{JsonSerializer.Serialize(templateInfo.Columns.Select(c => new { c.ColumnIndex, c.ParentHeader, c.ChildHeader, c.UniqueKey }), serializeOptions)}\n```";

        if (templateInfo.Metadata.Count > 0)
        {
            excelAnalysisContent += $"\n\n**Danh sách {templateInfo.Metadata.Count} nhãn Metadata (thông tin chung ở đầu trang):**\n" +
                                   $"```json\n{JsonSerializer.Serialize(templateInfo.Metadata.Keys, serializeOptions)}\n```";
        }
        await onStep(new RagStep("Excel Template Analysis", excelAnalysisContent));

        // 2. Xây dựng câu query tích hợp các chỉ dẫn mapping cột cho RAG AI
        var savedMappings = _mappingService.GetMapping(fileName);
        string combinedQuery;
        string mappingInstructions;

        if (templateInfo.Type == TemplateType.Hierarchical)
        {
            var colMappings = string.Join("\n", templateInfo.Columns.Select(col =>
                $"- Cột vật lý {col.ColumnIndex}: nhóm '{col.ParentHeader}' -> cột con '{col.ChildHeader}' -> Bạn BẮT BUỘC SELECT alias (AS) là [{col.UniqueKey}]"
            ));

            mappingInstructions = $"\n\nYÊU CẦU ĐẶC BIỆT CHO BÁO CÁO EXCEL PHÂN CẤP (HIERARCHICAL TEMPLATE):\n" +
                                  $"- Hệ thống phát hiện đây là mẫu báo cáo có cấu trúc tiêu đề phân cấp hai tầng (Parent-Child).\n" +
                                  $"- Bạn BẮT BUỘC sử dụng ALIAS (AS) để tên cột trong kết quả SQL SELECT cuối cùng khớp hoàn toàn với các UniqueKey sau đây:\n" +
                                  $"{colMappings}\n" +
                                  $"- CẢNH BÁO BẮT BUỘC: Bạn TUYỆT ĐỐI KHÔNG ĐƯỢC bỏ sót bất kỳ cột nào trong danh sách UniqueKey trên! Câu SELECT cuối cùng của bạn phải chứa ĐẦY ĐỦ tất cả các cột UniqueKey đã liệt kê theo đúng thứ tự. Nếu thiếu dù chỉ 1 cột, file Excel sẽ không thể điền dữ liệu và hệ thống sẽ bị lỗi!\n" +
                                  $"- Nếu không tìm thấy cột tương ứng trong database, hãy trả về NULL có ép kiểu để tránh lỗi SQL (ví dụ: CAST(NULL AS VARCHAR(100)) AS [UniqueKey]), tuyệt đối không được tự ý xóa cột khỏi câu SELECT.\n";
        }
        else
        {
            var columnsStr = string.Join(", ", templateInfo.Columns.Select(c => c.UniqueKey));
            mappingInstructions = $"\n\nYÊU CẦU ĐẶC BIỆT CHO BÁO CÁO EXCEL:\n" +
                                  $"- Dữ liệu SQL SELECT trả về BẮT BUỘC phải có đầy đủ các cột tiêu đề sau: {columnsStr}.\n" +
                                  $"- CẢNH BÁO BẮT BUỘC: Bạn TUYỆT ĐỐI KHÔNG ĐƯỢC bỏ sót bất kỳ cột nào! Câu SELECT cuối cùng phải chứa ĐẦY ĐỦ tất cả các cột UniqueKey theo đúng thứ tự. Nếu thiếu dù chỉ 1 cột, hệ thống sẽ lỗi.\n" +
                                  $"- BẮT BUỘC sử dụng ALIAS (AS) để tên cột trong kết quả trả về khớp hoàn toàn với tên tiêu đề Excel (ví dụ: SELECT KhachHang AS [{templateInfo.Columns.FirstOrDefault()?.UniqueKey ?? "ColName"}] ...).\n" +
                                  $"- Nếu không tìm thấy cột tương ứng trong database, hãy trả về NULL có ép kiểu để tránh lỗi SQL (ví dụ: CAST(NULL AS VARCHAR(100)) AS [UniqueKey]).\n";
        }

        if (templateInfo.Metadata.Count > 0)
        {
            var metadataList = templateInfo.Metadata.Keys.Select(m =>
            {
                string desc = $"- Nhãn '{m}' -> Bạn BẮT BUỘC SELECT cột tương ứng từ database trong câu SELECT cuối cùng và đặt ALIAS (AS) khớp hoàn toàn với tên nhãn này (Dùng MAX/MIN nếu có GROUP BY, ví dụ: MAX(StyleID) AS [{m}] hoặc MAX(PlanCode) AS [{m}]).";
                if (savedMappings.TryGetValue(m, out var customNote) && !string.IsNullOrWhiteSpace(customNote))
                {
                    desc += $" Chi tiết cách lấy/ý nghĩa: {customNote}";
                }
                return desc;
            });
            var metadataInstructions = string.Join("\n", metadataList);
            mappingInstructions += $"\n\nYÊU CẦU BẮT BUỘC TRUY VẤN THÔNG TIN CHUNG (METADATA):\n" +
                                   $"- File Excel template có {templateInfo.Metadata.Count} nhãn thông tin chung ở đầu trang cần được điền dữ liệu:\n" +
                                   $"{metadataInstructions}\n" +
                                   $"- CẢNH BÁO BẮT BUỘC: Trong câu lệnh SQL SELECT cuối cùng, bạn BẮT BUỘC phải truy vấn và trả về các cột này cùng với dữ liệu chi tiết của bảng để hệ thống có thể tự động bóc tách và điền vào đầu trang Excel. Nếu không có dữ liệu phù hợp, hãy trả về NULL có ép kiểu rõ ràng để tránh lỗi MAX(NULL) trong T-SQL (ví dụ: CAST(NULL AS VARCHAR(100)) AS [{templateInfo.Metadata.Keys.First()}]).";
        }

        if (templateMapping.SubtotalConfig != null)
        {
            mappingInstructions += $"\n\nYÊU CẦU VỀ DỮ LIỆU CHI TIẾT (RAW DETAILS FOR SUBTOTAL):\n" +
                                   $"- Mẫu báo cáo này sử dụng cấu hình tự động tính tổng con (Subtotal) theo nhóm cột '{templateMapping.SubtotalConfig.GroupByColumn}'.\n" +
                                   $"- Bạn BẮT BUỘC phải truy vấn và trả về danh sách dữ liệu chi tiết thô (raw details). TUYỆT ĐỐI KHÔNG sử dụng GROUP BY toàn cục hoặc tự gom nhóm/tính tổng các dòng dữ liệu trong câu lệnh SQL, vì hệ thống C# sẽ tự động chèn các dòng tổng con và tính toán chúng ở tầng ứng dụng.";
        }

        // Lấy ghi chú cột Excel được lưu trữ lâu dài của người dùng
        string userNotes = "";
        if (savedMappings.Count > 0)
        {
            var notesList = new List<string>();
            foreach (var col in templateInfo.Columns)
            {
                if (savedMappings.TryGetValue(col.UniqueKey, out var note) && !string.IsNullOrWhiteSpace(note))
                {
                    string colName = string.IsNullOrEmpty(col.ParentHeader) ? col.ChildHeader : $"{col.ParentHeader} -> {col.ChildHeader}";
                    notesList.Add($"- Cột có UniqueKey là '{col.UniqueKey}' (Tên hiển thị: '{colName}'): Có ý nghĩa/Công thức tính là \"{note}\"");
                }
            }
            if (notesList.Count > 0)
            {
                userNotes = $"\n\nDANH SÁCH Ý NGHĨA & CÔNG THỨC CỘT EXCEL TỰ ĐỊNH NGHĨA BỞI NGƯỜI DÙNG (BẮT BUỘC TUÂN THỦ KHI VIẾT SQL):\n" +
                            $"- Khi viết SQL, bạn BẮT BUỘC phải tính toán giá trị của các cột (UniqueKey) tương ứng theo đúng mô tả ý nghĩa/công thức dưới đây:\n" +
                            string.Join("\n", notesList) + "\n";
            }
        }

        combinedQuery = $"{additionalQuery.Trim()}.{userNotes}{mappingInstructions}";

        // 3. Thực thi RAG Orchestrator để lấy dữ liệu từ database
        var ragResponse = await _ragOrchestrator.ProcessQueryAsync(
            combinedQuery,
            null,
            onStep,
            onFinalChunk,
            ct,
            isExcelTemplate: true);

        // 4. Chuyển đổi dữ liệu trả về sang DataTable
        DataTable dataTable = new DataTable();
        if (ragResponse.RawDataTable != null && ragResponse.RawDataTable.Rows.Count > 0)
        {
            // Tạo bản sao DataTable có các cột đúng tên UniqueKey theo thứ tự của Template
            dataTable = new DataTable();
            foreach (var col in templateInfo.Columns)
            {
                dataTable.Columns.Add(col.UniqueKey, typeof(object));
            }

            // Map tên cột SQL mềm dẻo với UniqueKey trong template
            var columnMapping = _textUtility.BuildSoftColumnMapping(ragResponse.RawDataTable, templateInfo.Columns);

            foreach (DataRow sourceRow in ragResponse.RawDataTable.Rows)
            {
                // Bỏ qua dòng tổng từ SQL vì Excel đã có dòng tổng công thức riêng của template
                var firstColVal = sourceRow[0]?.ToString()?.Trim() ?? "";
                if (firstColVal.Equals("Tổng", StringComparison.OrdinalIgnoreCase) ||
                    firstColVal.Equals("Total", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var newRow = dataTable.NewRow();
                foreach (var col in templateInfo.Columns)
                {
                    if (columnMapping.TryGetValue(col.UniqueKey, out var sourceColName) &&
                        ragResponse.RawDataTable.Columns.Contains(sourceColName))
                    {
                        newRow[col.UniqueKey] = sourceRow[sourceColName];
                    }
                    else
                    {
                        newRow[col.UniqueKey] = DBNull.Value;
                    }
                }
                dataTable.Rows.Add(newRow);
            }
        }
        else if (!string.IsNullOrEmpty(ragResponse.RawData))
        {
            var columnsKeys = templateInfo.Columns.Select(c => c.UniqueKey).ToList();
            dataTable = _templateFiller.ConvertJsonToDataTable(ragResponse.RawData, columnsKeys);
        }

        // Sắp xếp DataTable theo ngày tăng dần (từ quá khứ tới gần nhất)
        SortDataTableByDate(dataTable, templateInfo.Columns);

        // 5. Điền Metadata
        var metadataValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ragResponse.Metadata != null)
        {
            foreach (var kvp in ragResponse.Metadata)
            {
                metadataValues[kvp.Key] = kvp.Value;
            }
        }


        // Tự động bổ sung metadata từ dòng đầu tiên của dữ liệu truy vấn thực tế (DataTable)
        if (ragResponse.RawDataTable != null && ragResponse.RawDataTable.Rows.Count > 0)
        {
            var firstRow = ragResponse.RawDataTable.Rows[0];
            foreach (DataColumn col in ragResponse.RawDataTable.Columns)
            {
                var val = firstRow[col]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(val) && !metadataValues.ContainsKey(col.ColumnName))
                {
                    metadataValues[col.ColumnName] = val;
                }
            }
        }

        if (metadataValues.Count > 0)
        {
            _templateFiller.FillMetadata(worksheet, templateInfo.HeaderRowIndex, metadataValues, templateMapping.MetadataCellMappings);
        }

        // 6. Điền dữ liệu bảng (tự chèn dòng và copy style nếu cần)
        if (templateInfo.Type == TemplateType.Hierarchical)
        {
            _templateFiller.FillHierarchicalTemplate(
                worksheet,
                dataTable,
                templateInfo.HeaderRowIndex,
                templateInfo.StartColumnIndex,
                templateInfo.Columns,
                null,
                templateInfo.FillableRowIndexes,
                templateInfo.TotalRowIndex,
                false,
                templateMapping.SubtotalConfig);
        }
        else
        {
            _templateFiller.FillHorizontalTemplate(
                worksheet,
                dataTable,
                templateInfo.HeaderRowIndex,
                templateInfo.StartColumnIndex,
                templateInfo.HeaderRowIndex + 1,
                null,
                templateInfo.FillableRowIndexes,
                templateInfo.TotalRowIndex,
                false,
                templateMapping.SubtotalConfig);
        }

        // 7. Kích hoạt tính năng tính toán công thức của EPPlus để cập nhật kết quả dòng Tổng cộng
        package.Workbook.Calculate();

        var excelBytes = package.GetAsByteArray();

        // 8. Trích xuất TOP 20 dòng làm Preview dữ liệu
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
            Text = ragResponse.Text,
            SuggestedQuestions = ragResponse.SuggestedQuestions ?? new List<string>()
        };
    }

    // Xuất danh sách dữ liệu ra file Excel dạng bảng lưới thông thường
    public byte[] ExportGenericExcel(List<Dictionary<string, object>> data)
    {
        return _excelExporter.ExportGenericExcel(data);
    }

    // Tự sinh file Excel từ bảng Markdown
    public byte[] ExportMarkdownToExcelDynamic(string markdownText)
    {
        return _excelExporter.ExportMarkdownToExcelDynamic(markdownText);
    }



    private static void SortDataTableByDate(DataTable dt, List<FlattenedColumn> columns)
    {
        if (dt == null || dt.Rows.Count <= 1) return;

        var dateCol = columns.FirstOrDefault(c => 
            c.UniqueKey.Equals("Ngay", StringComparison.OrdinalIgnoreCase) ||
            c.UniqueKey.Equals("NgayDate", StringComparison.OrdinalIgnoreCase) ||
            c.UniqueKey.Contains("Ngay", StringComparison.OrdinalIgnoreCase) ||
            c.UniqueKey.Contains("Date", StringComparison.OrdinalIgnoreCase)
        );

        if (dateCol != null && dt.Columns.Contains(dateCol.UniqueKey))
        {
            try
            {
                // Tạo một cột tạm kiểu DateTime để sắp xếp chính xác
                string tempColName = "_TempSortDateCol_" + Guid.NewGuid().ToString("N");
                dt.Columns.Add(tempColName, typeof(DateTime));

                foreach (DataRow row in dt.Rows)
                {
                    var val = row[dateCol.UniqueKey];
                    if (val == null || val == DBNull.Value)
                    {
                        row[tempColName] = DateTime.MinValue;
                    }
                    else if (val is DateTime dtVal)
                    {
                        row[tempColName] = dtVal.Date;
                    }
                    else
                    {
                        string strVal = val.ToString() ?? "";
                        if (DateTime.TryParseExact(strVal, new[] { "dd/MM/yyyy", "yyyy-MM-dd", "d/M/yyyy", "yyyy-M-d" }, 
                            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                        {
                            row[tempColName] = parsedDate;
                        }
                        else if (DateTime.TryParse(strVal, out DateTime parsedAny))
                        {
                            row[tempColName] = parsedAny;
                        }
                        else
                        {
                            row[tempColName] = DateTime.MinValue;
                        }
                    }
                }

                // Sắp xếp theo cột tạm thời
                DataView dv = dt.DefaultView;
                dv.Sort = $"{tempColName} ASC";
                DataTable sortedTable = dv.ToTable();

                // Nạp lại các dòng đã sắp xếp vào DataTable gốc
                dt.Rows.Clear();
                foreach (DataRow row in sortedTable.Rows)
                {
                    dt.ImportRow(row);
                }

                // Xóa cột tạm
                dt.Columns.Remove(tempColName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Lỗi khi sort DataTable theo ngày: {ex.Message}");
            }
        }
    }
}

