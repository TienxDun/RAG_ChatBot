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

    public ExcelReportService(RagOrchestrator ragOrchestrator)
    {
        _ragOrchestrator = ragOrchestrator;
    }

    // Xử lý upload template Excel mẫu, chạy RAG query để lấy dữ liệu, điền vào template và xuất file
    public async Task<ExcelReportResult> ProcessExcelTemplateAsync(Stream stream, string additionalQuery, Func<RagStep, Task> onStep, CancellationToken ct)
    {
        using var package = new ExcelPackage(stream);
        var worksheet = package.Workbook.Worksheets[0];

        // 1. Phân tích cấu trúc Template thông minh
        var templateInfo = ExcelTemplateAnalyzer.AnalyzeTemplate(worksheet);
        
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
        string combinedQuery;
        string mappingInstructions;

        if (templateInfo.Type == TemplateType.Hierarchical)
        {
            var colMappings = string.Join("\n", templateInfo.Columns.Select(col => 
                $"- Cột vật lý {col.ColumnIndex}: nhóm '{col.ParentHeader}' -> cột con '{col.ChildHeader}' -> Bạn BẮT BUỘC SELECT alias (AS) là [{col.UniqueKey}]"
            ));
            
            mappingInstructions = $"\n\nYÊU CẦU ĐẶC BIỆT CHO BÁO CÁO EXCEL PHÂN CẤP (HIERARCHICAL TEMPLATE):\n" +
                                  $"- Hệ thống phát hiện đây là mẫu báo cáo có cấu trúc tiêu đề phân cấp hai tầng (Parent-Child).\n" +
                                  $"- Bạn BẮT BUỘC sử dụng ALIAS (AS) để tên cột trong kết quả trả về khớp hoàn toàn với các UniqueKey sau đây:\n" +
                                  $"{colMappings}\n" +
                                  $"- Nếu không tìm thấy cột tương ứng, hãy để trống hoặc dùng NULL, đừng cố đoán bừa.\n";
        }
        else
        {
            var columnsStr = string.Join(", ", templateInfo.Columns.Select(c => c.UniqueKey));
            mappingInstructions = $"\n\nYÊU CẦU ĐẶC BIỆT CHO BÁO CÁO EXCEL:\n" +
                                  $"- Dữ liệu trả về BẮT BUỘC phải có các cột tiêu đề sau: {columnsStr}.\n" +
                                  $"- BẮT BUỘC sử dụng ALIAS (AS) để tên cột trong kết quả trả về khớp hoàn toàn với tên tiêu đề Excel (ví dụ: SELECT KhachHang AS [{templateInfo.Columns.FirstOrDefault()?.UniqueKey ?? "ColName"}] ...).";
        }

        if (templateInfo.Metadata.Count > 0)
        {
            var metadataLabels = string.Join("\n", templateInfo.Metadata.Keys.Select(cell => $"- {cell}"));
            mappingInstructions += $"\n- Ngoài ra, bạn BẮT BUỘC phải truy vấn thông tin cho các nhãn thông tin chung (metadata) sau đây từ database và trả về dưới dạng JSON (Key-Value) trong thuộc tính \"metadata\" của kết quả:\n" +
                                   $"{metadataLabels}\n" +
                                   $"- Khóa JSON trả về phải khớp hoàn toàn với tên các nhãn trên.";
        }

        combinedQuery = $"{additionalQuery.Trim()}.{mappingInstructions}";

        // 3. Thực thi RAG Orchestrator để lấy dữ liệu từ database
        var ragResponse = await _ragOrchestrator.ProcessQueryAsync(
            combinedQuery, 
            null, 
            onStep, 
            ct, 
            enableFastPath: true, 
            isExcelTemplate: true, 
            enableRulesExtraction: true);

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
            var columnMapping = BuildSoftColumnMapping(ragResponse.RawDataTable, templateInfo.Columns);

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
            dataTable = ExcelTemplateFiller.ConvertJsonToDataTable(ragResponse.RawData, columnsKeys);
        }

        // 5. Điền Metadata
        var metadataValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ragResponse.Metadata != null)
        {
            foreach (var kvp in ragResponse.Metadata)
            {
                metadataValues[kvp.Key] = kvp.Value;
            }
        }
        if (ragResponse.Steps != null)
        {
            var sqlMetadata = ExtractMetadataFromSqlSteps(ragResponse.Steps);
            foreach (var kvp in sqlMetadata)
            {
                if (!metadataValues.ContainsKey(kvp.Key))
                {
                    metadataValues[kvp.Key] = kvp.Value;
                }
            }
        }

        if (metadataValues.Count > 0)
        {
            ExcelTemplateFiller.FillMetadata(worksheet, templateInfo.HeaderRowIndex, metadataValues);
        }

        // 6. Điền dữ liệu bảng (tự chèn dòng và copy style nếu cần)
        if (templateInfo.Type == TemplateType.Hierarchical)
        {
            ExcelTemplateFiller.FillHierarchicalTemplate(
                worksheet,
                dataTable,
                templateInfo.HeaderRowIndex,
                templateInfo.StartColumnIndex,
                templateInfo.Columns,
                null,
                templateInfo.FillableRowIndexes,
                templateInfo.TotalRowIndex,
                false);
        }
        else
        {
            ExcelTemplateFiller.FillHorizontalTemplate(
                worksheet,
                dataTable,
                templateInfo.HeaderRowIndex,
                templateInfo.StartColumnIndex,
                templateInfo.HeaderRowIndex + 1,
                null,
                templateInfo.FillableRowIndexes,
                templateInfo.TotalRowIndex,
                false);
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
        return ExcelExportHelper.ExportGenericExcel(data);
    }

    // Tự sinh file Excel từ bảng Markdown
    public byte[] ExportMarkdownToExcelDynamic(string markdownText)
    {
        return ExcelExportHelper.ExportMarkdownToExcelDynamic(markdownText);
    }

    // ==========================================
    // CÁC HÀM REFLECTION ĐƯỢC GỌI TỪ UNIT TEST
    // ==========================================

    private static Dictionary<string, string> BuildSoftColumnMapping(DataTable source, List<FlattenedColumn> templateColumns)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tc in templateColumns)
        {
            string tcClean = ExcelTemplateAnalyzer.RemoveDiacritics(tc.UniqueKey)
                .Replace("_", "").Replace("-", "").Replace(" ", "")
                .Replace("y", "i").Replace("Y", "i")
                .ToLowerInvariant();
            
            foreach (DataColumn dc in source.Columns)
            {
                string dcClean = ExcelTemplateAnalyzer.RemoveDiacritics(dc.ColumnName)
                    .Replace("_", "").Replace("-", "").Replace(" ", "")
                    .Replace("y", "i").Replace("Y", "i")
                    .ToLowerInvariant();
                
                if (string.Equals(tcClean, dcClean, StringComparison.OrdinalIgnoreCase))
                {
                    mapping[tc.UniqueKey] = dc.ColumnName;
                    break;
                }
            }
        }
        return mapping;
    }

    private static string RemoveDiacriticsKeepSpaces(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (char c in normalized)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                if (c == 'đ') sb.Append('d');
                else if (c == 'Đ') sb.Append('D');
                else sb.Append(c);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string? FindBestMetadataValue(Dictionary<string, string> metadata, string key)
    {
        if (metadata == null || string.IsNullOrEmpty(key)) return null;

        string cleanSearch = RemoveDiacriticsKeepSpaces(key).ToLowerInvariant();
        cleanSearch = Regex.Replace(cleanSearch, @"([a-z])([A-Z])", "$1 $2");
        var searchTokens = cleanSearch.Split(new[] { ' ', '/', '_', '-', ':' }, StringSplitOptions.RemoveEmptyEntries);

        string? bestMatchValue = null;
        int maxMatchScore = 0;

        foreach (var kvp in metadata)
        {
            string cleanMetaKey = RemoveDiacriticsKeepSpaces(kvp.Key).ToLowerInvariant();
            cleanMetaKey = Regex.Replace(cleanMetaKey, @"([a-z])([A-Z])", "$1 $2");
            var metaTokens = cleanMetaKey.Split(new[] { ' ', '/', '_', '-', ':' }, StringSplitOptions.RemoveEmptyEntries);

            int score = 0;
            foreach (var sToken in searchTokens)
            {
                if (sToken.Length <= 1) continue;
                foreach (var mToken in metaTokens)
                {
                    if (mToken.Length <= 1) continue;
                    if (sToken == mToken)
                    {
                        score += sToken.Length * 3;
                    }
                    else if (sToken.Contains(mToken))
                    {
                        score += mToken.Length * 2;
                    }
                    else if (mToken.Contains(sToken))
                    {
                        score += sToken.Length * 2;
                    }
                }
            }

            if (score > maxMatchScore)
            {
                maxMatchScore = score;
                bestMatchValue = kvp.Value;
            }
        }

        return maxMatchScore > 0 ? bestMatchValue : null;
    }

    private static TemplateAnalysisResult MergeTemplateAnalysis(TemplateAnalysisResult llm, TemplateAnalysisResult ruleBased)
    {
        var merged = new TemplateAnalysisResult
        {
            Type = llm.Type,
            HeaderRowIndex = llm.HeaderRowIndex,
            StartColumnIndex = llm.StartColumnIndex,
            DataStartRowIndex = llm.DataStartRowIndex,
            DataEndRowIndex = ruleBased.DataEndRowIndex,
            TotalRowIndex = ruleBased.TotalRowIndex,
            FillableRowIndexes = ruleBased.FillableRowIndexes != null 
                ? new List<int>(ruleBased.FillableRowIndexes) 
                : new List<int>(),
            Columns = llm.Columns != null 
                ? new List<FlattenedColumn>(llm.Columns) 
                : new List<FlattenedColumn>(),
            Metadata = llm.Metadata != null 
                ? new Dictionary<string, string>(llm.Metadata, StringComparer.OrdinalIgnoreCase) 
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        if (ruleBased.Metadata != null)
        {
            foreach (var kvp in ruleBased.Metadata)
            {
                if (!merged.Metadata.ContainsKey(kvp.Key))
                {
                    merged.Metadata[kvp.Key] = kvp.Value;
                }
            }
        }

        return merged;
    }

    private static Dictionary<string, string> ExtractMetadataFromSqlSteps(List<RagStep> steps)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var patterns = new (string Key, string Pattern)[]
        {
            ("StyleID", @"StyleID\s*=\s*N?'([^']+)'"),
            ("StypeId", @"StypeId\s*=\s*N?'([^']+)'"),
            ("MaHang", @"MaHang\s*(?:=|LIKE)\s*N?'%?([^'%]+)%?'"),
            ("LineX", @"LineX\s*=\s*N?'?([^'\s,)]+)'?"),
        };

        foreach (var step in steps)
        {
            if (string.IsNullOrEmpty(step.Content)) continue;

            var sqlMatch = Regex.Match(step.Content, @"```sql\s*([\s\S]*?)```");
            if (!sqlMatch.Success) continue;
            var sql = sqlMatch.Groups[1].Value;

            foreach (var (key, pattern) in patterns)
            {
                if (result.ContainsKey(key)) continue;
                var m = Regex.Match(sql, pattern, RegexOptions.IgnoreCase);
                if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
                {
                    result[key] = m.Groups[1].Value.Trim();
                }
            }
        }

        return result;
    }
}
