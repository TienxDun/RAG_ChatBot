using Backend.Models;
using Backend.Services;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Backend.Endpoints;

public sealed class CachedDownloadFile
{
    public required byte[] Content { get; init; }
    public required string FileName { get; init; }
}

public static class ChatEndpoints
{
    private static readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task<IResult> HandleChatAsync(HttpContext context, RagOrchestrator orchestrator, ExcelReportService excelService, IMemoryCache cache, CancellationToken ct)
    {
        string message = string.Empty;
        string? collectionName = null;
        IFormFile? file = null;
        bool fastPathEnabled = true;
        bool rulesEnabled = true;

        // Hỗ trợ đọc cả JSON (chat bình thường) và Form (khi có upload file Excel)
        if (context.Request.HasFormContentType)
        {
            var form = await context.Request.ReadFormAsync(ct);
            message = form.TryGetValue("message", out var m) ? m.ToString() : string.Empty;
            collectionName = form.TryGetValue("collectionName", out var c) ? c.ToString() : null;
            file = form.Files.FirstOrDefault();
            if (form.TryGetValue("fastPath", out var fpStr) && bool.TryParse(fpStr, out var fpVal))
            {
                fastPathEnabled = fpVal;
            }
            if (form.TryGetValue("rulesEnabled", out var reStr) && bool.TryParse(reStr, out var reVal))
            {
                rulesEnabled = reVal;
            }
        }
        else if (context.Request.HasJsonContentType())
        {
            var json = await context.Request.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            message = json.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            collectionName = json.TryGetProperty("collectionName", out var c) ? c.GetString() : null;
            if (json.TryGetProperty("fastPath", out var fpProp) && (fpProp.ValueKind == JsonValueKind.True || fpProp.ValueKind == JsonValueKind.False))
            {
                fastPathEnabled = fpProp.GetBoolean();
            }
            if (json.TryGetProperty("rulesEnabled", out var reProp) && (reProp.ValueKind == JsonValueKind.True || reProp.ValueKind == JsonValueKind.False))
            {
                rulesEnabled = reProp.GetBoolean();
            }
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
            if (file != null)
            {
                return Results.BadRequest(new { error = "Tính năng tải lên file Excel mẫu và xuất báo cáo bằng AI không còn được hỗ trợ." });
            }
            else
            {
                var response = await orchestrator.ProcessQueryAsync(message, collectionName, async (step) => 
                {
                    await SendEventAsync(new { type = "step", step });
                }, ct, fastPathEnabled, false, rulesEnabled);

                await SendEventAsync(new { 
                    type = "final", 
                    text = response.Text, 
                    suggestedQuestions = response.SuggestedQuestions,
                    rawData = response.RawData,
                    isAmbiguous = response.IsAmbiguous
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

    public static IResult HandleDownloadAsync(string id, IMemoryCache cache)
    {
        if (cache.TryGetValue<CachedDownloadFile>(id, out var file) && file != null)
        {
            return Results.File(file.Content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.FileName);
        }

        if (cache.TryGetValue<byte[]>(id, out var legacyBytes) && legacyBytes != null)
        {
            return Results.File(legacyBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", id);
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

            using var jsonDoc = JsonDocument.Parse(json);
            var root = jsonDoc.RootElement;

            byte[] content;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("markdownText", out var mdProp))
            {
                string markdownText = mdProp.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(markdownText))
                {
                    return Results.BadRequest(new { error = "Markdown text is empty." });
                }
                content = excelService.ExportMarkdownToExcelDynamic(markdownText);
            }
            else
            {
                var data = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);
                if (data == null || data.Count == 0) return Results.BadRequest(new { error = "Invalid or empty data" });

                content = excelService.ExportGenericExcel(data);
            }

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
