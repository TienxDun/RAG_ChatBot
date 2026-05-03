using Backend.Models;
using Backend.Services;

var builder = WebApplication.CreateBuilder(args);

var envPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", ".env"));
if (File.Exists(envPath))
{
    DotNetEnv.Env.Load(envPath);
}

var options = VertexAiOptions.FromEnvironment();

builder.Services.AddSingleton(options);
builder.Services.AddHttpClient<VertexAiClient>();
builder.Services.AddSingleton<SqlService>();
builder.Services.AddSingleton<QdrantService>();
builder.Services.AddSingleton<RagOrchestrator>();

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

app.MapPost("/api/chat", async (ChatRequest request, RagOrchestrator orchestrator, CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.BadRequest(new { error = "Message is required." });
        }

        var response = await orchestrator.ProcessQueryAsync(request.Message, ct);
        return Results.Ok(response);
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

app.Run();
