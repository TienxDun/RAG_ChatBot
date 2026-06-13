using Backend.Services;
using Backend.Services.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;

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
                metadataCellMappings = templateMapping.MetadataCellMappings
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
            var templateMapping = new ExcelTemplateMapping
            {
                ColumnMappings = request.Mappings ?? new(),
                MetadataCellMappings = request.MetadataCellMappings ?? new()
            };
            mappingService.SaveTemplateMapping(request.FileName, templateMapping);
            return Results.Ok(new { message = "Đã lưu thông tin ánh xạ cột Excel thành công." });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Lỗi khi lưu cấu hình ánh xạ: {ex.Message}");
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
