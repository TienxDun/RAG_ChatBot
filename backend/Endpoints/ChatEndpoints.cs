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
        var parameters = await ChatRequestParser.ParseAsync(context, ct);

        if (string.IsNullOrWhiteSpace(parameters.Message))
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
            if (parameters.File != null && parameters.File.FileName.EndsWith(".xlsx"))
            {
                using var stream = parameters.File.OpenReadStream();
                var result = await excelService.ProcessExcelTemplateAsync(stream, parameters.File.FileName, parameters.Message, async (step) => 
                {
                    await SendEventAsync(new { type = "step", step });
                }, async (chunk) => 
                {
                    await SendEventAsync(new { type = "chunk", text = chunk });
                }, ct);

                var fileId = Guid.NewGuid().ToString() + ".xlsx";
                cache.Set(fileId, new CachedDownloadFile
                {
                    Content = Convert.FromBase64String(result.ExcelBase64),
                    FileName = parameters.File.FileName
                }, TimeSpan.FromMinutes(30));
                var downloadUrl = $"/api/download/{fileId}";

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
                var response = await orchestrator.ProcessQueryAsync(
                    parameters.Message, 
                    parameters.CollectionName, 
                    async (step) => 
                    {
                        await SendEventAsync(new { type = "step", step });
                    }, 
                    async (chunk) => 
                    {
                        await SendEventAsync(new { type = "chunk", text = chunk });
                    },
                    ct, 
                    false);

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
