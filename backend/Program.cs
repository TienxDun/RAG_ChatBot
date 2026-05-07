using Backend.Models;
using Backend.Services;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// Load .env file from root directory
var rootDir = builder.Environment.ContentRootPath;
var envPath = Path.GetFullPath(Path.Combine(rootDir, "..", ".env"));

// Fallback to current directory if not found (in case it's run from root)
if (!File.Exists(envPath))
{
    envPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".env"));
}

if (File.Exists(envPath))
{
    DotNetEnv.Env.Load(envPath);
    // Explicitly refresh configuration to include variables loaded by DotNetEnv
    builder.Configuration.AddEnvironmentVariables();
}

var options = VertexAiOptions.FromEnvironment();

builder.Services.AddSingleton(options);
builder.Services.AddHttpClient<VertexAiClient>();
builder.Services.AddSingleton<SqlService>();
builder.Services.AddSingleton<QdrantService>();
builder.Services.AddSingleton<RagOrchestrator>();
builder.Services.AddScoped<DocumentProcessor>();
builder.Services.AddScoped<ExcelReportService>();

var allowedOrigins = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS")?.Split(',') ?? new[] { "http://localhost:3000" };

builder.Services.AddCors(cors =>
{
    cors.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
var fileCache = new ConcurrentDictionary<string, byte[]>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/chat", async (HttpContext context, RagOrchestrator orchestrator, ExcelReportService excelService, CancellationToken ct) =>
    {
        string message = string.Empty;
        IFormFile? file = null;

        // Hỗ trợ đọc cả JSON (chat bình thường) và Form (khi có upload file Excel)
        if (context.Request.HasFormContentType)
        {
            var form = await context.Request.ReadFormAsync(ct);
            message = form.TryGetValue("message", out var m) ? m.ToString() : string.Empty;
            file = form.Files.FirstOrDefault();
        }
        else if (context.Request.HasJsonContentType())
        {
            var request = await context.Request.ReadFromJsonAsync<ChatRequest>(cancellationToken: ct);
            message = request?.Message ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return Results.BadRequest(new { error = "Message is required." });
        }

        // Thiết lập Server-Sent Events
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.Append("Cache-Control", "no-cache");
        context.Response.Headers.Append("Connection", "keep-alive");
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        async Task SendEventAsync(object data)
        {
            var json = JsonSerializer.Serialize(data, serializerOptions);
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
                fileCache[fileId] = excelBytes;
                var downloadUrl = $"/api/download/{fileId}";

                // Gửi kết quả cuối cùng kèm link tải Excel (chuyển downloadUrl xuống cuối cho dễ tìm!)
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
                var response = await orchestrator.ProcessQueryAsync(message, async (step) => 
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
        catch (Exception ex)
        {
            await SendEventAsync(new { type = "error", message = ex.Message });
        }

        return Results.Empty;
    })
    .WithName("Chat")
    .WithOpenApi(operation =>
    {
        operation.RequestBody = new Microsoft.OpenApi.Models.OpenApiRequestBody
        {
            Description = "Nhập câu hỏi và file đính kèm (nếu có)",
            Required = true,
            Content = new System.Collections.Generic.Dictionary<string, Microsoft.OpenApi.Models.OpenApiMediaType>
            {
                ["multipart/form-data"] = new Microsoft.OpenApi.Models.OpenApiMediaType
                {
                    Schema = new Microsoft.OpenApi.Models.OpenApiSchema
                    {
                        Type = "object",
                        Properties = new System.Collections.Generic.Dictionary<string, Microsoft.OpenApi.Models.OpenApiSchema>
                        {
                            ["message"] = new Microsoft.OpenApi.Models.OpenApiSchema { Type = "string", Description = "Câu truy vấn của bạn" },
                            ["file"] = new Microsoft.OpenApi.Models.OpenApiSchema { Type = "string", Format = "binary", Description = "File Excel đính kèm (tùy chọn)" }
                        }
                    }
                }
            }
        };
        return operation;
    });



app.MapPost("/api/embeddings", async (EmbeddingRequest request, VertexAiClient client, CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Results.BadRequest(new { error = "Text is required." });
        }

        var embedding = await client.GetEmbeddingAsync(request.Text, request.TaskType, request.OutputDimensionality, ct);
        return Results.Ok(new EmbeddingResponse(embedding));
    })
    .WithName("Embeddings")
    .WithOpenApi();

app.MapGet("/api/download/{id}", (string id) => 
{
    if (fileCache.TryGetValue(id, out var bytes))
    {
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", id);
    }
    return Results.NotFound(new { error = "File không tồn tại hoặc đã hết hạn." });
})
.WithName("DownloadExcel")
.WithOpenApi(operation => 
{
    operation.Summary = "Tải xuống file Excel";
    operation.Description = "API này dùng để tải trực tiếp file Excel được sinh ra từ quá trình Chat.";
    return operation;
});

app.Run();
