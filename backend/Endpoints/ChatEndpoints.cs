using Backend.Models;
using Backend.Services;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;

namespace Backend.Endpoints;

public static class ChatEndpoints
{
    // Lưu trữ file Excel tạm thời trong bộ nhớ (Cache)
    private static readonly ConcurrentDictionary<string, byte[]> _fileCache = new();

    private static readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task<IResult> HandleChatAsync(HttpContext context, RagOrchestrator orchestrator, ExcelReportService excelService, CancellationToken ct)
    {
        string message = string.Empty;
        string? collectionName = null;
        IFormFile? file = null;

        // Hỗ trợ đọc cả JSON (chat bình thường) và Form (khi có upload file Excel)
        if (context.Request.HasFormContentType)
        {
            var form = await context.Request.ReadFormAsync(ct);
            message = form.TryGetValue("message", out var m) ? m.ToString() : string.Empty;
            collectionName = form.TryGetValue("collectionName", out var c) ? c.ToString() : null;
            file = form.Files.FirstOrDefault();
        }
        else if (context.Request.HasJsonContentType())
        {
            var json = await context.Request.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            message = json.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            collectionName = json.TryGetProperty("collectionName", out var c) ? c.GetString() : null;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return Results.BadRequest(new { error = "Message is required." });
        }

        // Thiết lập Server-Sent Events
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.Append("Cache-Control", "no-cache");
        context.Response.Headers.Append("Connection", "keep-alive");

        async Task SendEventAsync(object data)
        {
            var json = JsonSerializer.Serialize(data, _serializerOptions);
            await context.Response.WriteAsync($"data: {json}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
        }

        try 
        {
            if (file != null && file.FileName.EndsWith(".xlsx"))
            {
                using var stream = file.OpenReadStream();
                var result = await excelService.ProcessExcelTemplateAsync(stream, message, async (step) => 
                {
                    // Gửi từng bước ngay khi hoàn thành
                    await SendEventAsync(new { type = "step", step });
                }, ct);

                // Lưu file vào cache memory để cho phép download
                var fileId = Guid.NewGuid().ToString() + ".xlsx";
                var excelBytes = Convert.FromBase64String(result.ExcelBase64);
                _fileCache[fileId] = excelBytes;
                var downloadUrl = $"/api/download/{fileId}";

                // Gửi kết quả cuối cùng kèm link tải Excel
                await SendEventAsync(new { 
                    type = "final", 
                    text = result.Text, 
                    suggestedQuestions = result.SuggestedQuestions,
                    previewData = result.PreviewData,
                    excelBase64 = result.ExcelBase64,
                    rawData = result.PreviewData,
                    downloadUrl = downloadUrl
                });
            }
            else
            {
                var response = await orchestrator.ProcessQueryAsync(message, collectionName, async (step) => 
                {
                    await SendEventAsync(new { type = "step", step });
                }, ct);

                await SendEventAsync(new { 
                    type = "final", 
                    text = response.Text, 
                    suggestedQuestions = response.SuggestedQuestions,
                    rawData = response.RawData
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Kết nối bị ngắt bởi người dùng hoặc hệ thống shutdown - không cần xử lý thêm
            Console.WriteLine("⚠️ Chat request was cancelled/connection closed.");
        }
        catch (Exception ex)
        {
            try 
            {
                await SendEventAsync(new { type = "error", message = ex.Message });
            }
            catch { /* Bỏ qua nếu không thể gửi lỗi về client khi kết nối đã đứt */ }
        }

        return Results.Empty;
    }

    public static IResult HandleDownloadAsync(string id)
    {
        if (_fileCache.TryGetValue(id, out var bytes))
        {
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", id);
        }
        return Results.NotFound(new { error = "File không tồn tại hoặc đã hết hạn." });
    }

    public static async Task<IResult> HandleExportExcelAsync(HttpContext context, ExcelReportService excelService)
    {
        try 
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            
            if (string.IsNullOrWhiteSpace(json)) return Results.BadRequest(new { error = "No data provided" });

            var data = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);
            if (data == null || data.Count == 0) return Results.BadRequest(new { error = "Invalid or empty data" });

            var content = excelService.ExportGenericExcel(data);

            return Results.File(
                content, 
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 
                $"data_export_{DateTime.Now:yyyyMMddHHmmss}.xlsx"
            );
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }
}
