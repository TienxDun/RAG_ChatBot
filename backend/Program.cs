using Backend.Models;
using Backend.Services;
using System.Text.Json;

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

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/chat", async (ChatRequest request, RagOrchestrator orchestrator, HttpContext context, CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.BadRequest(new { error = "Message is required." });
        }

        // Thiết lập Server-Sent Events
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.Append("Cache-Control", "no-cache");
        context.Response.Headers.Append("Connection", "keep-alive");

        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        // Hàm helper để gửi event
        async Task SendEventAsync(object data)
        {
            var json = JsonSerializer.Serialize(data, serializerOptions);
            await context.Response.WriteAsync($"data: {json}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
        }

        try 
        {
            var response = await orchestrator.ProcessQueryAsync(request.Message, async (step) => 
            {
                // Gửi từng bước ngay khi hoàn thành
                await SendEventAsync(new { type = "step", step });
            }, ct);

            // Gửi kết quả cuối cùng
            await SendEventAsync(new { type = "final", text = response.Text, suggestedQuestions = response.SuggestedQuestions });
        }
        catch (Exception ex)
        {
            await SendEventAsync(new { type = "error", message = ex.Message });
        }

        return Results.Empty;
    })
    .WithName("Chat")
    .WithOpenApi();

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

app.MapPost("/api/documents/upload", async (HttpContext context, DocumentProcessor processor, CancellationToken ct) =>
    {
        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Request must be multipart/form-data." });
        }

        var form = await context.Request.ReadFormAsync(ct);
        var files = form.Files;

        if (files.Count == 0)
        {
            return Results.BadRequest(new { error = "No files uploaded." });
        }

        // Thiết lập Streaming Response (Server-Sent Events)
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.Append("Cache-Control", "no-cache");
        context.Response.Headers.Append("Connection", "keep-alive");

        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var results = new List<DocumentResult>();
        
        foreach (var file in files)
        {
            try
            {
                using var stream = file.OpenReadStream();
                var result = await processor.ProcessFileAsync(stream, file.FileName, async (percent, message) => 
                {
                    var progressUpdate = new { fileName = file.FileName, percent, message, type = "progress" };
                    await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(progressUpdate, serializerOptions)}\n\n", ct);
                    await context.Response.Body.FlushAsync(ct);
                }, ct);
                results.Add(result);
            }
            catch (Exception ex)
            {
                results.Add(new DocumentResult(file.FileName, 0, $"Error: {ex.Message}"));
            }
        }

        // Gửi kết quả cuối cùng
        var finalResult = new { results, type = "result" };
        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(finalResult, serializerOptions)}\n\n", ct);
        await context.Response.Body.FlushAsync(ct);

        return Results.Empty;
    })
    .WithName("UploadDocuments")
    .WithOpenApi()
    .DisableAntiforgery();

app.Run();
