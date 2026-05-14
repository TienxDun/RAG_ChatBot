using Backend.Models;
using Backend.Services;
using Backend.Endpoints;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.Net;

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
builder.Services.AddHttpClient<VertexAiClient>(client => 
{
    client.DefaultRequestVersion = HttpVersion.Version20;
    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
});
builder.Services.AddSingleton<SqlService>();
builder.Services.AddSingleton<QdrantService>();
builder.Services.AddSingleton<RagOrchestrator>();
builder.Services.AddScoped<DocumentProcessor>();
builder.Services.AddScoped<ExcelReportService>();
builder.Services.AddSingleton<TemplateCacheService>();


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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.MapGet("/api/health", () => Results.Ok(new { 
    status = "ok", 
    message = "API is ready"
}));

app.MapGet("/api/documents/collections", async (QdrantService qdrant) => 
{
    var collections = await qdrant.GetCollectionsAsync();
    return Results.Ok(collections);
});

app.MapPost("/api/chat", (HttpContext context, RagOrchestrator orchestrator, ExcelReportService excelService, CancellationToken ct) => ChatEndpoints.HandleChatAsync(context, orchestrator, excelService, ct))
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

app.MapGet("/api/download/{id}", (string id) => ChatEndpoints.HandleDownloadAsync(id))
.WithName("DownloadExcel")
.WithOpenApi(operation => 
{
    operation.Summary = "Tải xuống file Excel";
    operation.Description = "API này dùng để tải trực tiếp file Excel được sinh ra từ quá trình Chat.";
    return operation;
});

app.MapPost("/api/chat/export-excel", ChatEndpoints.HandleExportExcelAsync)
    .WithName("ExportExcel")
    .WithOpenApi();

app.MapPost("/api/documents/upload", async (HttpContext context, DocumentProcessor processor, CancellationToken ct) =>
{
    if (!context.Request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Content-Type must be multipart/form-data" });
    }

    var form = await context.Request.ReadFormAsync(ct);
    var files = form.Files;
    var collectionName = form.ContainsKey("collectionName") ? form["collectionName"].ToString() : null;

    if (files.Count == 0)
    {
        return Results.BadRequest(new { error = "No files uploaded" });
    }

    // Thiết lập SSE
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
        int fileCount = files.Count;
        int currentFileIndex = 0;

        foreach (var file in files)
        {
            currentFileIndex++;
            var fileName = file.FileName;
            using var stream = file.OpenReadStream();

            await processor.ProcessFileAsync(stream, fileName, collectionName, async (percent, message) =>
            {
                // Tính toán tổng tiến trình dựa trên số lượng file
                // Mỗi file chiếm 1/fileCount tổng số phần trăm
                int totalPercent = (int)(((currentFileIndex - 1) * 100.0 / fileCount) + (percent / (double)fileCount));
                
                await SendEventAsync(new { 
                    type = "progress", 
                    percent = totalPercent, 
                    message = $"[File {currentFileIndex}/{fileCount}] {fileName}: {message}" 
                });
            }, ct);
        }

        await SendEventAsync(new { type = "result", status = "success" });
    }
    catch (Exception ex)
    {
        await SendEventAsync(new { type = "error", message = ex.Message });
    }

    return Results.Empty;
})
.WithName("UploadDocuments")
.WithOpenApi();

TemplateCacheEndpoints.MapRoutes(app);



app.Run();

