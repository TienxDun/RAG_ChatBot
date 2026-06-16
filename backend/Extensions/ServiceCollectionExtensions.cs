using Backend.Models;
using Backend.Services;
using Backend.Services.Security;
using System.Net;

namespace Backend.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, WebApplicationBuilder builder)
    {
        // Set EPPlus License context globally
        OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("My Project");

        // Register Memory Cache
        services.AddMemoryCache();

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

        // Load options from environment variables
        var options = VertexAiOptions.FromEnvironment();
        var qdrantOptions = QdrantOptions.FromEnvironment();
        var sqlOptions = new SqlOptions 
        { 
            ConnectionString = builder.Configuration["MSSQL_CONNECTION_STRING"] 
                ?? throw new InvalidOperationException("MSSQL_CONNECTION_STRING is not set in configuration."),
            VikingConnectionString = builder.Configuration["MSSQL_VIKING_CONNECTION_STRING"]
                ?? throw new InvalidOperationException("MSSQL_VIKING_CONNECTION_STRING is not set in configuration.")
        };

        services.AddSingleton(options);
        services.AddSingleton(qdrantOptions);
        services.AddSingleton(sqlOptions);

        services.AddHttpClient<VertexAiClient>(client => 
        {
            client.DefaultRequestVersion = HttpVersion.Version20;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
        });

        // Infrastructure & Data Services
        services.AddSingleton<ISqlSecurityValidator, SqlSecurityValidator>();
        services.AddSingleton<SqlService>();
        services.AddSingleton<QdrantService>();
        services.AddSingleton<TemplateCacheService>();

        // Excel Refactored Services
        services.AddSingleton<Backend.Services.Excel.ITextUtility, Backend.Services.Excel.TextUtility>();
        services.AddSingleton<Backend.Services.Excel.IExcelTemplateAnalyzer, Backend.Services.Excel.ExcelTemplateAnalyzer>();
        services.AddSingleton<Backend.Services.Excel.IExcelTemplateFiller, Backend.Services.Excel.ExcelTemplateFiller>();
        services.AddSingleton<Backend.Services.Excel.IExcelExporter, Backend.Services.Excel.ExcelExporter>();
        services.AddSingleton<Backend.Services.Excel.IExcelMappingService, Backend.Services.Excel.ExcelMappingService>();

        // Document Refactored Services
        services.AddSingleton<Backend.Services.Document.IDbSchemaParser, Backend.Services.Document.DbSchemaParser>();
        services.AddSingleton<Backend.Services.Document.ITextChunker, Backend.Services.Document.TextChunker>();

        // Rag Refactored Services
        services.AddSingleton<Backend.Services.Rag.ISqlRuleProvider, Backend.Services.Rag.SqlRuleProvider>();
        services.AddSingleton<Backend.Services.Rag.IAiResponseParser, Backend.Services.Rag.AiResponseParser>();
        services.AddSingleton<Backend.Services.Rag.ISqlPlanExecutor, Backend.Services.Rag.SqlPlanExecutor>();

        // Orchestrators & Orchestrated Services
        services.AddSingleton<RagOrchestrator>();
        services.AddScoped<DocumentProcessor>();
        services.AddScoped<ExcelReportService>();

        // CORS
        var allowedOrigins = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS")?.Split(',') ?? new[] { "http://localhost:3000" };
        services.AddCors(cors =>
        {
            cors.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        return services;
    }
}
