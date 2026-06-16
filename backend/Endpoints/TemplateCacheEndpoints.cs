using Backend.Services;
using Backend.Services.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Backend.Endpoints;

/// Định nghĩa các API endpoints cho việc quản lý bộ nhớ đệm Template Excel.
public static class TemplateCacheEndpoints
{
    /// Đăng ký các route cho Template Cache API.
    public static void MapRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/templates/cache")
            .WithTags("Template Cache Management")
            .WithOpenApi();

        // POST /api/templates/cache - Upload và lưu file vào memory
        group.MapPost("/", HandleUploadAsync)
            .WithName("UploadTemplateToCache")
            .DisableAntiforgery(); // Cho phép upload file từ các nguồn client khác nhau

        // GET /api/templates/cache - Liệt kê danh sách template đang được cache
        group.MapGet("/", HandleList)
            .WithName("ListCachedTemplates");

        // GET /api/templates/cache/{id} - Tải xuống file template gốc từ memory
        group.MapGet("/{id}", HandleDownload)
            .WithName("DownloadCachedTemplate");

        // DELETE /api/templates/cache/{id} - Xóa 1 template khỏi memory
        group.MapDelete("/{id}", HandleDelete)
            .WithName("DeleteCachedTemplate");

        // DELETE /api/templates/cache - Xóa sạch bộ nhớ đệm
        group.MapDelete("/", HandleClearAll)
            .WithName("ClearTemplateCache");
            
        // GET /api/templates/cache/stats - Xem thông số bộ nhớ đệm
        group.MapGet("/stats", (TemplateCacheService cacheService) => Results.Ok(cacheService.GetCacheStats()))
            .WithName("GetCacheStats");

        // POST /api/templates/analyze - Phân tích tệp Excel trả về các cột
        app.MapPost("/api/templates/analyze", HandleAnalyzeAsync)
            .WithName("AnalyzeTemplate")
            .DisableAntiforgery();

        // POST /api/templates/save-mapping - Lưu chú thích cột Excel
        app.MapPost("/api/templates/save-mapping", HandleSaveMappingAsync)
            .WithName("SaveTemplateMapping")
            .DisableAntiforgery();

        // POST /api/templates/auto-map - Tự động ánh xạ giải nghĩa cột bằng Qdrant + Gemini
        app.MapPost("/api/templates/auto-map", HandleAutoMapAsync)
            .WithName("AutoMapTemplateColumns")
            .DisableAntiforgery();

        // GET /api/templates/params?fileName=X - Lấy parameter definitions theo tên file template
        app.MapGet("/api/templates/params", HandleGetParamsAsync)
            .WithName("GetTemplateParams");

        // GET /api/data/lookup?table=X&column=Y&display=Z - Lấy distinct values cho dropdown form
        app.MapGet("/api/data/lookup", HandleDataLookupAsync)
            .WithName("DataLookup");

        // POST /api/templates/suggest-params - AI gợi ý tham số từ metadata + mappings
        app.MapPost("/api/templates/suggest-params", HandleSuggestParamsAsync)
            .WithName("SuggestTemplateParams")
            .DisableAntiforgery();
    }

    /// Xử lý việc upload file Excel template trống.
    public static async Task<IResult> HandleUploadAsync(HttpRequest request, TemplateCacheService cacheService)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Content-Type phải là multipart/form-data" });
        }

        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("file");

        if (file == null || file.Length == 0)
        {
            return Results.BadRequest(new { error = "Không tìm thấy file upload với key 'file'" });
        }

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { error = "Chỉ hỗ trợ file Excel định dạng .xlsx" });
        }

        try
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var bytes = ms.ToArray();

            // Lưu vào service in-memory
            var cached = cacheService.StoreTemplate(bytes, file.FileName);

            return Results.Ok(new
            {
                id = cached.Id,
                fileName = cached.FileName,
                fileSize = cached.FileSize,
                cachedAt = cached.CachedAt,
                message = "File đã được lưu vào bộ nhớ đệm thành công."
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Lỗi khi xử lý file: {ex.Message}");
        }
    }

    /// Trả về danh sách các template đang lưu trong RAM.
    public static IResult HandleList(TemplateCacheService cacheService)
    {
        var templates = cacheService.GetAllTemplates()
            .Select(t => new 
            { 
                t.Id, 
                t.FileName, 
                t.FileSize, 
                t.CachedAt,
                downloadUrl = $"/api/templates/cache/{t.Id}"
            })
            .OrderByDescending(t => t.CachedAt);

        return Results.Ok(templates);
    }

    
    /// Trả về file vật lý từ memory để người dùng tải về.
    public static IResult HandleDownload(string id, TemplateCacheService cacheService)
    {
        var template = cacheService.GetTemplate(id);
        if (template == null)
        {
            return Results.NotFound(new { error = "Template không tồn tại hoặc đã bị xóa khỏi bộ nhớ đệm (hết hạn)." });
        }

        return Results.File(
            template.FileBytes, 
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 
            template.FileName
        );
    }

    /// Xóa 1 bản ghi cache.
    public static IResult HandleDelete(string id, TemplateCacheService cacheService)
    {
        var success = cacheService.RemoveTemplate(id);
        return success 
            ? Results.Ok(new { message = $"Đã xóa thành công template: {id}" }) 
            : Results.NotFound(new { error = "Không tìm thấy template để xóa." });
    }

    /// Xóa toàn bộ cache.
    public static IResult HandleClearAll(TemplateCacheService cacheService)
    {
        cacheService.ClearAll();
        return Results.Ok(new { message = "Đã làm sạch toàn bộ bộ nhớ đệm template." });
    }

    /// Xử lý phân tích cấu trúc template Excel trả về danh sách các cột tiêu đề
    public static async Task<IResult> HandleAnalyzeAsync(HttpRequest request, Backend.Services.Excel.IExcelTemplateAnalyzer analyzer, Backend.Services.Excel.IExcelMappingService mappingService)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Content-Type phải là multipart/form-data" });
        }

        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("file");

        if (file == null || file.Length == 0)
        {
            return Results.BadRequest(new { error = "Không tìm thấy file upload với key 'file'" });
        }

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { error = "Chỉ hỗ trợ file Excel định dạng .xlsx" });
        }

        try
        {
            using var stream = file.OpenReadStream();
            using var package = new OfficeOpenXml.ExcelPackage(stream);
            var worksheet = package.Workbook.Worksheets[0];
            var result = analyzer.AnalyzeTemplate(worksheet);

            var templateMapping = mappingService.GetTemplateMapping(file.FileName);

            return Results.Ok(new
            {
                type = result.Type.ToString(),
                headerRowIndex = result.HeaderRowIndex,
                startColumnIndex = result.StartColumnIndex,
                columns = result.Columns.Select(c => new
                {
                    columnIndex = c.ColumnIndex,
                    parentHeader = c.ParentHeader,
                    childHeader = c.ChildHeader,
                    uniqueKey = c.UniqueKey
                }).ToList(),
                metadata = result.Metadata,
                grid = result.Grid,
                savedMappings = templateMapping.ColumnMappings,
                metadataCellMappings = templateMapping.MetadataCellMappings,
                columnFormats = templateMapping.ColumnFormats
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Lỗi khi phân tích template: {ex.Message}");
        }
    }

    public sealed class SaveMappingRequest
    {
        public string FileName { get; set; } = string.Empty;
        public Dictionary<string, string> Mappings { get; set; } = new();
        public Dictionary<string, string> MetadataCellMappings { get; set; } = new();
        public Dictionary<string, string>? ColumnFormats { get; set; }
        /// Danh sách tham số động (null = không thay đổi tham số hiện tại)
        public List<TemplateParameter>? Parameters { get; set; }
    }

    /// Xử lý lưu các chú thích/ánh xạ cột Excel của người dùng
    public static async Task<IResult> HandleSaveMappingAsync(SaveMappingRequest request, Backend.Services.Excel.IExcelMappingService mappingService)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.FileName))
        {
            return Results.BadRequest(new { error = "Dữ liệu yêu cầu không hợp lệ hoặc thiếu tên file." });
        }

        try
        {
            // Lấy mapping hiện tại để giữ nguyên SubtotalConfig và Parameters nếu request không gửi lên
            var existing = mappingService.GetTemplateMapping(request.FileName);
            var templateMapping = new ExcelTemplateMapping
            {
                ColumnMappings = request.Mappings ?? new(),
                MetadataCellMappings = request.MetadataCellMappings ?? new(),
                ColumnFormats = request.ColumnFormats ?? existing.ColumnFormats ?? new(),
                SubtotalConfig = existing.SubtotalConfig,
                Parameters = request.Parameters ?? existing.Parameters
            };
            mappingService.SaveTemplateMapping(request.FileName, templateMapping);
            return Results.Ok(new { message = "Đã lưu thông tin ánh xạ cột Excel thành công." });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Lỗi khi lưu cấu hình ánh xạ: {ex.Message}");
        }
    }

    /// Lấy danh sách parameter definitions theo tên file template
    public static IResult HandleGetParamsAsync(
        [Microsoft.AspNetCore.Mvc.FromQuery] string fileName,
        Backend.Services.Excel.IExcelMappingService mappingService)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return Results.BadRequest(new { error = "Thiếu tham số fileName" });

        var mapping = mappingService.GetTemplateMapping(fileName);
        return Results.Ok(new
        {
            fileName,
            parameters = mapping.Parameters ?? new List<TemplateParameter>()
        });
    }

    /// Lấy danh sách distinct values từ bảng SQL Server để populate dropdown form
    public static async Task<IResult> HandleDataLookupAsync(
        [Microsoft.AspNetCore.Mvc.FromQuery] string table,
        [Microsoft.AspNetCore.Mvc.FromQuery] string column,
        [Microsoft.AspNetCore.Mvc.FromQuery] string? display,
        SqlService sqlService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(column))
            return Results.BadRequest(new { error = "Thiếu tham số table hoặc column" });

        // Whitelist các bảng được phép tra cứu để tránh SQL injection qua tên bảng
        var allowedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "tbl_settingLineX", "tbl_SettingLineX", "SettingLine",
            "ERP_LenhSX", "DIC_KhachHang", "TSKFinal", "tbl_Steps"
        };

        if (!allowedTables.Any(t => table.Equals(t, StringComparison.OrdinalIgnoreCase)))
            return Results.BadRequest(new { error = $"Bảng '{table}' không được phép tra cứu." });

        try
        {
            var displayCol = string.IsNullOrWhiteSpace(display) ? column : display;
            var sql = $"SELECT DISTINCT TOP 500 [{column}] AS value, [{displayCol}] AS label FROM [{table}] WHERE [{column}] IS NOT NULL ORDER BY [{displayCol}]";
            var data = await sqlService.QueryAsync(sql, ct);
            return Results.Ok(data);
        }
        catch (Exception ex)
        {
            return Results.Problem($"Lỗi khi tra cứu dữ liệu: {ex.Message}");
        }
    }

    public sealed class SuggestParamsRequest
    {
        public string FileName { get; set; } = string.Empty;
    }

    /// AI phân tích metadata cells + column mappings của template → gợi ý tham số cần thiết
    public static async Task<IResult> HandleSuggestParamsAsync(
        SuggestParamsRequest request,
        Backend.Services.Excel.IExcelMappingService mappingService,
        VertexAiClient aiClient,
        CancellationToken ct)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.FileName))
            return Results.BadRequest(new { error = "Thiếu tên file template." });

        try
        {
            var mapping = mappingService.GetTemplateMapping(request.FileName);

            var metaDesc = mapping.MetadataCellMappings.Count > 0
                ? string.Join(", ", mapping.MetadataCellMappings.Values.Where(v => !string.IsNullOrWhiteSpace(v)))
                : "(Không có metadata cell)";

            var colDesc = mapping.ColumnMappings.Count > 0
                ? string.Join(", ", mapping.ColumnMappings.Take(20).Select(kvp => $"{kvp.Key}: {kvp.Value}"))
                : "(Không có column mappings)";

            var prompt = $@"Bạn là chuyên gia phân tích báo cáo Excel trong nhà máy may.
Dựa vào thông tin template Excel dưới đây, hãy xác định các THAM SỐ mà người dùng cần nhập để lọc và xuất báo cáo.

Tên file template: {request.FileName}

Các ô metadata (thông tin đầu trang báo cáo): {metaDesc}

Các cột chú thích: {colDesc}

YÊU CẦU:
- Phân tích và liệt kê các tham số lọc cần thiết (ví dụ: tên chuyền, ngày, mã kế hoạch, mã PO...)
- Nếu tham số liên quan đến chuyền sản xuất → type=""select"", dataSource=""tbl_settingLineX"", dataColumn=""LineName""
- Nếu liên quan đến mã kế hoạch/PO → type=""text""
- Nếu liên quan đến 1 ngày → type=""date"", defaultValue=""today""
- Nếu liên quan đến khoảng ngày → type=""daterange""
- promptTemplate: cách ghép tham số vào câu hỏi (ví dụ: ""Chuyền: {{value}}"")

TRẢ VỀ JSON ARRAY (KHÔNG bọc trong ```json codeblock):
[
  {{""key"": ""line_name"", ""label"": ""Tên chuyền"", ""type"": ""select"", ""required"": true, ""dataSource"": ""tbl_settingLineX"", ""dataColumn"": ""LineName"", ""placeholder"": ""Chọn chuyền..."", ""promptTemplate"": ""Tên chuyền: {{value}}"", ""order"": 1}},
  {{""key"": ""report_date"", ""label"": ""Ngày báo cáo"", ""type"": ""date"", ""required"": true, ""defaultValue"": ""today"", ""promptTemplate"": ""Ngày {{value}}"", ""order"": 2}}
]";

            var responseText = await aiClient.GenerateContentAsync(prompt, ct);
            responseText = CleanJsonResponse(responseText);

            var parameters = JsonSerializer.Deserialize<List<TemplateParameter>>(
                responseText,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return Results.Ok(new { parameters });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Lỗi khi gợi ý tham số bằng AI: {ex.Message}");
        }
    }


    public sealed class AutoMapColumnDto
    {
        public string UniqueKey { get; set; } = string.Empty;
        public string ChildHeader { get; set; } = string.Empty;
        public string ParentHeader { get; set; } = string.Empty;
    }

    public sealed class AutoMapRequest
    {
        public string FileName { get; set; } = string.Empty;
        public string? CollectionName { get; set; }
        public List<AutoMapColumnDto> Columns { get; set; } = new();
    }

    /// Xử lý tự động ánh xạ giải nghĩa cột bằng Qdrant schema search và LLM sinh text
    public static async Task<IResult> HandleAutoMapAsync(
        AutoMapRequest request,
        QdrantService qdrantService,
        VertexAiClient aiClient,
        CancellationToken ct)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.FileName) || request.Columns.Count == 0)
        {
            return Results.BadRequest(new { error = "Dữ liệu yêu cầu không hợp lệ hoặc thiếu tên file/danh sách cột." });
        }

        try
        {
            var distinctSchemas = new HashSet<string>();
            var targetCollection = string.IsNullOrWhiteSpace(request.CollectionName) ? "db_schema" : request.CollectionName;

            // 1. Tìm các schema liên quan nhất trong Qdrant dựa trên độ tương quan ngữ nghĩa của các cột
            foreach (var col in request.Columns)
            {
                var queryText = string.IsNullOrWhiteSpace(col.ParentHeader) 
                    ? $"Cột {col.ChildHeader}" 
                    : $"Cột {col.ParentHeader} {col.ChildHeader}";
                
                try
                {
                    // Lấy embedding vector 3072 cho query
                    var vector = await aiClient.GetEmbeddingAsync(queryText, "RETRIEVAL_QUERY", 3072, ct);
                    
                    // Tìm kiếm 2 schema liên quan nhất
                    var schemas = await qdrantService.SearchSchemaAsync(vector, limit: 2, collectionName: targetCollection);
                    foreach (var s in schemas)
                    {
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            distinctSchemas.Add(s);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Lỗi khi lấy embedding hoặc search Qdrant cho cột '{col.ChildHeader}': {ex.Message}");
                }
            }

            var schemaContext = string.Join("\n\n", distinctSchemas);

            // 2. Gom các cột Excel và dựng Prompt cho Gemini
            var columnsDescription = string.Join("\n", request.Columns.Select(c => 
                $"- Key: {c.UniqueKey} | Tiêu đề: {(string.IsNullOrWhiteSpace(c.ParentHeader) ? c.ChildHeader : $"{c.ParentHeader} ➔ {c.ChildHeader}")}"));

            var prompt = $@"Bạn là một chuyên gia thiết kế cơ sở dữ liệu và AI RAG.
Nhiệm vụ của bạn là phân tích danh sách các cột trong một file Excel mẫu, đối chiếu với cấu trúc Database thực tế được lưu trong Qdrant, sau đó tự động tạo câu Ghi chú chú thích giải nghĩa cho từng cột Excel đó để giúp Chatbot AI sau này hiểu cột đó chứa dữ liệu gì khi viết câu SQL.

Danh sách các cột trong file Excel mẫu cần chú thích:
{columnsDescription}

Cấu trúc cơ sở dữ liệu thực tế (Được trích xuất từ Qdrant):
{schemaContext}

---
YÊU CẦU:
1. Với mỗi cột Excel, hãy viết 1 câu chú thích giải nghĩa ngắn gọn bằng Tiếng Việt. 
2. Chú thích cần chỉ rõ:
   - Ý nghĩa của cột.
   - Cột đó tương ứng với Column nào, Table nào trong database (ví dụ: ""Lấy dữ liệu từ cột Chuyen của bảng QTY_MaHang_KiemQC_ChiTiet"").
   - Cách tính toán hoặc điều kiện lọc (nếu cột đó là cột tính toán như tỉ lệ lỗi, tổng cộng).
3. ĐẦU RA BẮT BUỘC chỉ chứa một JSON Object duy nhất, trong đó KEY là Key của cột Excel (ví dụ: Cosmos_Chuyen) và VALUE là câu chú thích bằng tiếng Việt.
4. Không kèm theo bất kỳ văn bản giải thích nào khác ngoài JSON, không bọc trong markdown codeblock ```json.

Ví dụ định dạng kết quả trả về:
{{
  ""key_excel_1"": ""Chú thích cột 1"",
  ""key_excel_2"": ""Chú thích cột 2""
}}";

            // 3. Gọi Gemini
            var responseText = await aiClient.GenerateContentAsync(prompt, ct);
            
            // Clean kết quả nếu có markdown codeblock
            responseText = CleanJsonResponse(responseText);

            // Parse kết quả
            var mappings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(responseText, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return Results.Ok(new { mappings });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Lỗi khi tự động phân tích và ánh xạ cột bằng AI: {ex.Message}");
        }
    }

    private static string CleanJsonResponse(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "{}";
        
        var clean = input.Trim();
        if (clean.StartsWith("```"))
        {
            var lines = clean.Split('\n');
            var resultLines = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("```")) continue;
                resultLines.Add(lines[i]);
            }
            clean = string.Join("\n", resultLines).Trim();
        }
        return clean;
    }
}
