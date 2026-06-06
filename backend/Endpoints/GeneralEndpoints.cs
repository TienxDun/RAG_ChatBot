using Backend.Models;
using Backend.Services;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Backend.Endpoints;


/// Các endpoint chung: health, testcases, documents, embeddings, sql execute, download, export.
public static class GeneralEndpoints
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void MapRoutes(IEndpointRouteBuilder app, IWebHostEnvironment env)
    {
        MapHealthEndpoint(app);
        MapTestCasesEndpoint(app);
        MapDocumentEndpoints(app);
        MapEmbeddingsEndpoint(app);
        MapChatEndpoints(app);
        MapExcelEndpoints(app);
        
        if (env.IsDevelopment())
        {
            MapSqlExecuteEndpoint(app);
        }
    }

    // ==================== HEALTH ====================

    private static void MapHealthEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { 
            status = "ok", 
            message = "API is ready"
        }));
    }

    // ==================== TEST CASES ====================

    private static void MapTestCasesEndpoint(IEndpointRouteBuilder app)
    {
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

                var questionRegex = new Regex(@"^\d+\.\s+\*\*(.*?)\*\*");
                var questionBackupRegex = new Regex(@"^\d+\.\s+(.*)");

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

        app.MapPost("/api/testcases", async ([FromBody] List<TestCaseSectionDto> sections) =>
        {
            try
            {
                var currentDir = Directory.GetCurrentDirectory();
                var testCasesPath = Path.Combine(currentDir, "test_cases.md");

                if (!File.Exists(testCasesPath))
                {
                    testCasesPath = Path.GetFullPath(Path.Combine(currentDir, "..", "test_cases.md"));
                }

                if (!File.Exists(testCasesPath))
                {
                    testCasesPath = Path.Combine(currentDir, "test_cases.md");
                }

                var sb = new System.Text.StringBuilder();
                int qIndex = 1;
                for (int i = 0; i < sections.Count; i++)
                {
                    var sec = sections[i];
                    if (string.IsNullOrWhiteSpace(sec.Section)) continue;

                    sb.AppendLine($"## {sec.Section.Trim()}");
                    sb.AppendLine();
                    foreach (var q in sec.Questions)
                    {
                        if (string.IsNullOrWhiteSpace(q)) continue;
                        sb.AppendLine($"{qIndex}. **{q.Trim()}**");
                        qIndex++;
                    }

                    if (i < sections.Count - 1)
                    {
                        sb.AppendLine();
                        sb.AppendLine("---");
                        sb.AppendLine();
                    }
                }

                await File.WriteAllTextAsync(testCasesPath, sb.ToString(), System.Text.Encoding.UTF8);

                // Copy to build output folder if we are running in a different dir
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var buildOutputFilePath = Path.Combine(baseDir, "test_cases.md");
                if (Path.GetFullPath(baseDir) != Path.GetFullPath(currentDir))
                {
                    File.Copy(testCasesPath, buildOutputFilePath, true);
                }

                return Results.Ok(new { message = "Lưu test cases thành công" });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });
    }

    // ==================== DOCUMENTS ====================

    private static void MapDocumentEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/documents/collections", async (QdrantService qdrant) => 
        {
            var collections = await qdrant.GetCollectionsAsync();
            return Results.Ok(collections);
        });

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

            async Task SendEventAsync(object data)
            {
                var json = JsonSerializer.Serialize(data, _serializerOptions);
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
    }

    // ==================== EMBEDDINGS ====================

    private static void MapEmbeddingsEndpoint(IEndpointRouteBuilder app)
    {
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
    }

    // ==================== CHAT ====================

    private static void MapChatEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat", (HttpContext context, RagOrchestrator orchestrator, ExcelReportService excelService, IMemoryCache cache, CancellationToken ct) 
            => ChatEndpoints.HandleChatAsync(context, orchestrator, excelService, cache, ct))
            .WithName("Chat")
            .WithOpenApi(operation =>
            {
                operation.RequestBody = new Microsoft.OpenApi.Models.OpenApiRequestBody
                {
                    Description = "Nhập câu hỏi và file đính kèm (nếu có)",
                    Required = true,
                    Content = new Dictionary<string, Microsoft.OpenApi.Models.OpenApiMediaType>
                    {
                        ["multipart/form-data"] = new Microsoft.OpenApi.Models.OpenApiMediaType
                        {
                            Schema = new Microsoft.OpenApi.Models.OpenApiSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, Microsoft.OpenApi.Models.OpenApiSchema>
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
    }

    // ==================== EXCEL ====================

    private static void MapExcelEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/download/{id}", (string id, IMemoryCache cache) => ChatEndpoints.HandleDownloadAsync(id, cache))
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
    }

    // ==================== SQL EXECUTE (Development Only) ====================

    private static void MapSqlExecuteEndpoint(IEndpointRouteBuilder app)
    {
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
                        dict[col.ColumnName] = row[col] == DBNull.Value ? null! : row[col];
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
    }
}

public record ExecuteSqlRequest(string Sql);
public record TestCaseSectionDto(string Section, List<string> Questions);
