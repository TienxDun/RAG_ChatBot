using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Backend.Models;
using Backend.Services.Document;

namespace Backend.Services;

public sealed class DocumentProcessor
{
    private readonly VertexAiClient _aiClient;
    private readonly QdrantService _qdrantService;
    private readonly VertexAiOptions _options;
    private readonly IDbSchemaParser _dbSchemaParser;
    private readonly ITextChunker _textChunker;

    private static readonly HashSet<string> SupportedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "text/plain",
        "application/json"
    };

    public DocumentProcessor(
        VertexAiClient aiClient, 
        QdrantService qdrantService, 
        VertexAiOptions options,
        IDbSchemaParser dbSchemaParser,
        ITextChunker textChunker)
    {
        _aiClient = aiClient;
        _qdrantService = qdrantService;
        _options = options;
        _dbSchemaParser = dbSchemaParser;
        _textChunker = textChunker;
    }

    public static string ResolveMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".json" => "application/json",
            _ => throw new NotSupportedException($"File type '{ext}' is not supported. Use PDF, TXT, or JSON.")
        };
    }

    public static bool IsSupportedMimeType(string mimeType) => SupportedMimeTypes.Contains(mimeType);

    public async Task<DocumentResult> ProcessFileAsync(
        Stream fileStream, 
        string fileName, 
        string? collectionName,
        Func<int, string, Task> onProgress, 
        CancellationToken ct)
    {
        await onProgress(5, "Đang khởi tạo...");
        var mimeType = ResolveMimeType(fileName);

        // Step 1: Extract text via Vertex AI
        await onProgress(10, "AI đang bóc tách nội dung (có thể mất vài giây)...");
        string extractedText;
        bool isJson = mimeType == "application/json";

        if (mimeType == "text/plain" || isJson)
        {
            // Đối với văn bản thuần hoặc JSON, đọc raw để giữ nguyên cấu trúc gốc
            using var reader = new StreamReader(fileStream);
            extractedText = await reader.ReadToEndAsync(ct);
        }
        else
        {
            extractedText = await _aiClient.ExtractTextFromFileAsync(fileStream, mimeType, ct);
        }

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            return new DocumentResult(fileName, 0, "No text content extracted.");
        }

        // Step 3: Embed each chunk/object
        var points = new List<QdrantService.QdrantPoint>();

        if (isJson)
        {
            using var jsonDoc = JsonDocument.Parse(extractedText);
            var items = new List<JsonElement>();
            if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                items = jsonDoc.RootElement.EnumerateArray().ToList();
            }
            else if (jsonDoc.RootElement.ValueKind == JsonValueKind.Object)
            {
                items.Add(jsonDoc.RootElement);
            }

            if (items.Count > 0)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var metadata = new Dictionary<string, string>();
                    string embeddingText = string.Empty;
                    string descriptiveText = string.Empty;

                    // Bỏ qua file global_rules vì sẽ được nạp trực tiếp từ đĩa trong Orchestrator
                    if (item.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "global_rules")
                    {
                        continue;
                    }

                    bool isDbSchema = _dbSchemaParser.IsDatabaseSchema(item);

                    if (isDbSchema)
                    {
                        descriptiveText = _dbSchemaParser.ParseSchema(item, out embeddingText, out metadata);
                    }
                    else
                    {
                        // Format phẳng cho JSON object không phải schema (backward compatible)
                        var sb = new StringBuilder();
                        foreach (var prop in item.EnumerateObject())
                        {
                            string displayValue;
                            if (prop.Value.ValueKind == JsonValueKind.Array)
                            {
                                var list = new List<string>();
                                foreach (var element in prop.Value.EnumerateArray())
                                {
                                    if (element.ValueKind == JsonValueKind.Object)
                                    {
                                        if (element.TryGetProperty("name", out var nameProp2))
                                        {
                                            var name = nameProp2.GetString();
                                            var desc = element.TryGetProperty("description", out var descProp2) ? descProp2.GetString() : "";
                                            list.Add(string.IsNullOrWhiteSpace(desc) ? name! : $"{name}: {desc}");
                                        }
                                        else
                                        {
                                            list.Add(element.ToString());
                                        }
                                    }
                                    else
                                    {
                                        list.Add(element.ToString());
                                    }
                                }
                                displayValue = list.Count > 0 ? "\n  - " + string.Join("\n  - ", list) : "[]";
                            }
                            else if (prop.Value.ValueKind == JsonValueKind.Object)
                            {
                                displayValue = "\n" + JsonSerializer.Serialize(prop.Value, new JsonSerializerOptions { WriteIndented = true });
                            }
                            else
                            {
                                displayValue = prop.Value.ToString();
                            }

                            metadata[prop.Name] = prop.Value.ToString();
                            sb.AppendLine($"{prop.Name}: {displayValue}");
                        }

                        descriptiveText = sb.ToString().Trim();
                        embeddingText = descriptiveText;
                    }
                    
                    // Giới hạn ký tự tối đa của văn bản embedding như một lớp bảo vệ dự phòng (~1000 tokens)
                    if (embeddingText.Length > 4000)
                    {
                        embeddingText = embeddingText.Substring(0, 4000);
                    }

                    var vector = await _aiClient.GetEmbeddingAsync(embeddingText, "RETRIEVAL_DOCUMENT", 3072, ct);
                    
                    points.Add(new QdrantService.QdrantPoint(vector, descriptiveText, fileName, i, metadata));
                    
                    int percent = 30 + (int)((i / (float)items.Count) * 60);
                    await onProgress(percent, $"Đang xử lý mục JSON {i + 1}/{items.Count}...");
                }
            }
        }
        else
        {
            // Xử lý tài liệu văn bản thông thường (PDF, TXT)
            var chunks = _textChunker.ChunkBySeparator(extractedText, isJson ? "\n\n\n" : "\n\n");
            for (int i = 0; i < chunks.Count; i++)
            {
                int percent = 30 + (int)((i / (float)chunks.Count) * 60);
                await onProgress(percent, $"Đang tạo vector cho đoạn {i + 1}/{chunks.Count}...");
                
                var chunk = chunks[i];
                var vector = await _aiClient.GetEmbeddingAsync(chunk, "RETRIEVAL_DOCUMENT", 3072, ct);
                points.Add(new QdrantService.QdrantPoint(vector, chunk, fileName, i));
            }
        }

        // Step 4: Upsert to Qdrant
        await onProgress(95, "Đang lưu dữ liệu cấu trúc vào Qdrant Cloud...");
        await _qdrantService.UpsertPointsAsync(points, collectionName, ct);

        await onProgress(100, "Hoần tất!");
        return new DocumentResult(fileName, points.Count, "Success");
    }
}

public sealed record DocumentResult(string FileName, int ChunkCount, string Status);
