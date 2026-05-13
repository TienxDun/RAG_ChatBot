using System.Text;
using System.Text.Json;
using Backend.Models;

namespace Backend.Services;

public sealed class DocumentProcessor
{
    private readonly VertexAiClient _aiClient;
    private readonly QdrantService _qdrantService;
    private readonly VertexAiOptions _options;

    private static readonly HashSet<string> SupportedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "text/plain",
        "application/json"
    };

    public DocumentProcessor(VertexAiClient aiClient, QdrantService qdrantService, VertexAiOptions options)
    {
        _aiClient = aiClient;
        _qdrantService = qdrantService;
        _options = options;
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
            // Với JSON, không cần qua AI Client Transform ở đây vì sẽ xử lý structured bên dưới
        }
        else
        {
            extractedText = await _aiClient.ExtractTextFromFileAsync(fileStream, mimeType, ct);
        }

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            return new DocumentResult(fileName, 0, "No text content extracted.");
        }

        // Step 1.5: Debug - Lưu kết quả bóc tách ra file để kiểm tra
        try
        {
            var debugDir = Path.Combine(Directory.GetCurrentDirectory(), "temp_debug");
            if (!Directory.Exists(debugDir)) Directory.CreateDirectory(debugDir);

            // Thêm timestamp để phân biệt các lần bóc tách
            var safeFileName = string.Concat(fileName.Split(Path.GetInvalidFileNameChars()));
            var debugPath = Path.Combine(debugDir, $"debug_{DateTime.Now:yyyyMMdd_HHmmss}_{safeFileName}.txt");
            
            await File.WriteAllTextAsync(debugPath, extractedText, ct);
        }
        catch
        {
            // Không chặn tiến trình chính nếu lưu debug lỗi
        }

        // Step 3: Embed each chunk/object
        var points = new List<QdrantService.QdrantPoint>();

        if (isJson)
        {
            using var jsonDoc = JsonDocument.Parse(extractedText);
            if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var items = jsonDoc.RootElement.EnumerateArray().ToList();
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var metadata = new Dictionary<string, string>();
                    var sb = new StringBuilder();

                    // Nhận diện cấu trúc database schema: có key "table" + "columns"
                    JsonElement columnsProp = default;
                    bool isDbSchema = item.TryGetProperty("table", out var tableProp) 
                                      && item.TryGetProperty("columns", out columnsProp) 
                                      && columnsProp.ValueKind == JsonValueKind.Array;

                    if (isDbSchema)
                    {
                        // Format Markdown chuyên biệt cho database schema
                        var tableName = tableProp.GetString() ?? "Unknown";
                        var tableDesc = item.TryGetProperty("description", out var descProp) 
                            ? descProp.GetString() ?? "" : "";
                        
                        sb.AppendLine($"## Bảng: {tableName}");
                        sb.AppendLine();
                        sb.AppendLine($"**Mô tả:** {tableDesc}");
                        sb.AppendLine();

                        var columns = columnsProp.EnumerateArray().ToList();
                        sb.AppendLine($"### Danh sách cột ({columns.Count} cột):");
                        sb.AppendLine();
                        sb.AppendLine("| Tên cột | Kiểu dữ liệu | Mô tả |");
                        sb.AppendLine("|---------|---------------|-------|");

                        foreach (var col in columns)
                        {
                            var colName = col.TryGetProperty("name", out var cn) ? cn.GetString() ?? "" : "";
                            var colType = col.TryGetProperty("data_type", out var ct2) ? ct2.GetString() ?? "" : "";
                            var colDesc = col.TryGetProperty("description", out var cd) ? cd.GetString() ?? "" : "";
                            
                            // Escape ký tự pipe trong description để không phá Markdown table
                            colDesc = colDesc.Replace("|", "\\|");
                            
                            sb.AppendLine($"| {colName} | {colType} | {colDesc} |");
                        }

                        // Lưu metadata đầy đủ
                        metadata["table"] = tableName;
                        metadata["description"] = tableDesc;
                        metadata["columns"] = columnsProp.ToString();
                        metadata["column_count"] = columns.Count.ToString();
                    }
                    else
                    {
                        // Format phẳng cho JSON object không phải schema (backward compatible)
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
                    }

                    var descriptiveText = sb.ToString().Trim();
                    var vector = await _aiClient.GetEmbeddingAsync(descriptiveText, "RETRIEVAL_DOCUMENT", 3072, ct);
                    
                    points.Add(new QdrantService.QdrantPoint(vector, descriptiveText, fileName, i, metadata));
                    
                    int percent = 30 + (int)((i / (float)items.Count) * 60);
                    await onProgress(percent, $"Đang xử lý mục JSON {i + 1}/{items.Count}...");
                }
            }
        }
        else
        {
            // Xử lý tài liệu văn bản thông thường (PDF, TXT)
            var chunks = ChunkBySeparator(extractedText, isJson ? "\n\n\n" : "\n\n");
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

        await onProgress(100, "Hoàn tất!");
        return new DocumentResult(fileName, points.Count, "Success");

    }

    private static List<string> ChunkBySeparator(string text, string separator, int maxChunkLength = 1500, int overlap = 200)
    {
        var chunks = new List<string>();
        var paragraphs = text.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);

        var currentChunk = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            var trimmed = paragraph.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Nếu đoạn văn đơn lẻ quá dài, cần xé nhỏ nó ra theo maxChunkLength
            if (trimmed.Length > maxChunkLength)
            {
                // Trước khi xử lý đoạn văn khổng lồ này, hãy lưu chunk hiện tại nếu có
                if (currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                }

                // Xé nhỏ đoạn văn khổng lồ
                int start = 0;
                while (start < trimmed.Length)
                {
                    int length = Math.Min(maxChunkLength, trimmed.Length - start);
                    var subChunk = trimmed.Substring(start, length);
                    chunks.Add(subChunk.Trim());
                    
                    start += (maxChunkLength - overlap); // Di chuyển bước nhảy để tạo overlap
                    if (start >= trimmed.Length - overlap) break; // Tránh lặp vô hạn hoặc đoạn cuối quá ngắn
                }
                continue;
            }

            // Kiểm tra xem thêm đoạn văn này vào chunk hiện tại có bị quá dài không
            if (currentChunk.Length + trimmed.Length + separator.Length > maxChunkLength && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
                
                // Giữ lại một phần cuối của chunk trước làm overlap cho chunk sau
                var previousText = currentChunk.ToString();
                var overlapText = previousText.Length > overlap 
                    ? previousText.Substring(previousText.Length - overlap) 
                    : previousText;
                
                currentChunk.Clear();
                currentChunk.Append(overlapText);
            }

            currentChunk.Append(trimmed).Append(separator);
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString().Trim());
        }

        return chunks;
    }
}

public sealed record DocumentResult(string FileName, int ChunkCount, string Status);
