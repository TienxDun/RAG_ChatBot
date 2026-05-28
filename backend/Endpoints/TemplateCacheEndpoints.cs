using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Endpoints;

/// <summary>
/// Định nghĩa các API endpoints cho việc quản lý bộ nhớ đệm Template Excel.
/// </summary>
public static class TemplateCacheEndpoints
{
    /// <summary>
    /// Đăng ký các route cho Template Cache API.
    /// </summary>
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
    }

    /// <summary>
    /// Xử lý việc upload file Excel template trống.
    /// </summary>
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

    /// <summary>
    /// Trả về danh sách các template đang lưu trong RAM.
    /// </summary>
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

    /// <summary>
    /// Trả về file vật lý từ memory để người dùng tải về.
    /// </summary>
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

    /// <summary>
    /// Xóa 1 bản ghi cache.
    /// </summary>
    public static IResult HandleDelete(string id, TemplateCacheService cacheService)
    {
        var success = cacheService.RemoveTemplate(id);
        return success 
            ? Results.Ok(new { message = $"Đã xóa thành công template: {id}" }) 
            : Results.NotFound(new { error = "Không tìm thấy template để xóa." });
    }

    /// <summary>
    /// Xóa toàn bộ cache.
    /// </summary>
    public static IResult HandleClearAll(TemplateCacheService cacheService)
    {
        cacheService.ClearAll();
        return Results.Ok(new { message = "Đã làm sạch toàn bộ bộ nhớ đệm template." });
    }

    /// <summary>
    /// Xử lý phân tích cấu trúc template Excel trả về danh sách các cột tiêu đề
    /// </summary>
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

            var savedMappings = mappingService.GetMapping(file.FileName);

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
                savedMappings = savedMappings
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
    }

    /// <summary>
    /// Xử lý lưu các chú thích/ánh xạ cột Excel của người dùng
    /// </summary>
    public static async Task<IResult> HandleSaveMappingAsync(SaveMappingRequest request, Backend.Services.Excel.IExcelMappingService mappingService)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.FileName))
        {
            return Results.BadRequest(new { error = "Dữ liệu yêu cầu không hợp lệ hoặc thiếu tên file." });
        }

        try
        {
            mappingService.SaveMapping(request.FileName, request.Mappings);
            return Results.Ok(new { message = "Đã lưu thông tin ánh xạ cột Excel thành công." });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Lỗi khi lưu cấu hình ánh xạ: {ex.Message}");
        }
    }
}
