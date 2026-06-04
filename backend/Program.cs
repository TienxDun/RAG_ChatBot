using Backend.Models;
using Backend.Services;
using Backend.Endpoints;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// Set EPPlus License context globally
OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("My Project");

// Register Memory Cache
builder.Services.AddMemoryCache();

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
var qdrantOptions = Backend.Models.QdrantOptions.FromEnvironment();
var sqlOptions = new Backend.Models.SqlOptions 
{ 
    ConnectionString = builder.Configuration["MSSQL_CONNECTION_STRING"] 
        ?? throw new InvalidOperationException("MSSQL_CONNECTION_STRING is not set in configuration.") 
};

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(qdrantOptions);
builder.Services.AddSingleton(sqlOptions);

builder.Services.AddHttpClient<VertexAiClient>(client => 
{
    client.DefaultRequestVersion = HttpVersion.Version20;
    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
});

// Infrastructure & Data Services
builder.Services.AddSingleton<Backend.Services.Security.ISqlSecurityValidator, Backend.Services.Security.SqlSecurityValidator>();
builder.Services.AddSingleton<SqlService>();
builder.Services.AddSingleton<QdrantService>();
builder.Services.AddSingleton<TemplateCacheService>();

// Excel Refactored Services
builder.Services.AddSingleton<Backend.Services.Excel.ITextUtility, Backend.Services.Excel.TextUtility>();
builder.Services.AddSingleton<Backend.Services.Excel.IExcelTemplateAnalyzer, Backend.Services.Excel.ExcelTemplateAnalyzer>();
builder.Services.AddSingleton<Backend.Services.Excel.IExcelTemplateFiller, Backend.Services.Excel.ExcelTemplateFiller>();
builder.Services.AddSingleton<Backend.Services.Excel.IExcelExporter, Backend.Services.Excel.ExcelExporter>();
builder.Services.AddSingleton<Backend.Services.Excel.IExcelMappingService, Backend.Services.Excel.ExcelMappingService>();

// Document Refactored Services
builder.Services.AddSingleton<Backend.Services.Document.IDbSchemaParser, Backend.Services.Document.DbSchemaParser>();
builder.Services.AddSingleton<Backend.Services.Document.ITextChunker, Backend.Services.Document.TextChunker>();

// Rag Refactored Services
builder.Services.AddSingleton<Backend.Services.Rag.ISqlRuleProvider, Backend.Services.Rag.SqlRuleProvider>();
builder.Services.AddSingleton<Backend.Services.Rag.IAiResponseParser, Backend.Services.Rag.AiResponseParser>();
builder.Services.AddSingleton<Backend.Services.Rag.ISqlPlanExecutor, Backend.Services.Rag.SqlPlanExecutor>();

// Orchestrators & Orchestrated Services
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

app.MapGet("/api/testcases", async () =>
{
    try
    {
        var currentDir = Directory.GetCurrentDirectory();
        var testCasesPath = Path.GetFullPath(Path.Combine(currentDir, "..", "test_cases.md"));

        if (!File.Exists(testCasesPath))
        {
            testCasesPath = Path.GetFullPath(Path.Combine(currentDir, "test_cases.md"));
        }

        if (!File.Exists(testCasesPath))
        {
            return Results.NotFound(new { error = "Không tìm thấy file test_cases.md" });
        }

        var lines = await File.ReadAllLinesAsync(testCasesPath);
        var sections = new List<object>();
        string currentSectionName = "Chưa phân loại";
        var currentQuestions = new List<string>();

        var questionRegex = new System.Text.RegularExpressions.Regex(@"^\d+\.\s+\*\*(.*?)\*\*");
        var questionBackupRegex = new System.Text.RegularExpressions.Regex(@"^\d+\.\s+(.*)");

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine)) continue;

            if (trimmedLine.StartsWith("## "))
            {
                if (currentQuestions.Count > 0)
                {
                    sections.Add(new
                    {
                        section = currentSectionName,
                        questions = currentQuestions.ToList()
                    });
                    currentQuestions.Clear();
                }
                currentSectionName = trimmedLine.Substring(3).Trim();
            }
            else
            {
                var match = questionRegex.Match(trimmedLine);
                if (match.Success)
                {
                    currentQuestions.Add(match.Groups[1].Value.Trim());
                }
                else
                {
                    var backupMatch = questionBackupRegex.Match(trimmedLine);
                    if (backupMatch.Success)
                    {
                        currentQuestions.Add(backupMatch.Groups[1].Value.Trim());
                    }
                }
            }
        }

        if (currentQuestions.Count > 0)
        {
            sections.Add(new
            {
                section = currentSectionName,
                questions = currentQuestions
            });
        }

        return Results.Ok(sections);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapGet("/api/documents/collections", async (QdrantService qdrant) => 
{
    var collections = await qdrant.GetCollectionsAsync();
    return Results.Ok(collections);
});

app.MapPost("/api/chat", (HttpContext context, RagOrchestrator orchestrator, ExcelReportService excelService, Microsoft.Extensions.Caching.Memory.IMemoryCache cache, CancellationToken ct) => ChatEndpoints.HandleChatAsync(context, orchestrator, excelService, cache, ct))
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

app.MapGet("/api/download/{id}", (string id, Microsoft.Extensions.Caching.Memory.IMemoryCache cache) => ChatEndpoints.HandleDownloadAsync(id, cache))
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

app.MapPost("/api/sql/execute", async ([FromBody] ExecuteSqlRequest request, SqlService sqlService, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Sql))
    {
        return Results.BadRequest(new { error = "SQL query is required." });
    }

    try
    {
        var dataTable = await sqlService.ExecuteQueryAsDataTableAsync(request.Sql, ct);
        
        var rows = new List<Dictionary<string, object>>();
        foreach (System.Data.DataRow row in dataTable.Rows)
        {
            var dict = new Dictionary<string, object>();
            foreach (System.Data.DataColumn col in dataTable.Columns)
            {
                dict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
            }
            rows.Add(dict);
        }

        return Results.Ok(new 
        { 
            columns = dataTable.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName).ToList(), 
            data = rows 
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("ExecuteSql")
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

public record ExecuteSqlRequest(string Sql);

