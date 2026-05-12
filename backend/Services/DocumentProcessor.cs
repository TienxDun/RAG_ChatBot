using System.Text;
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

        if (mimeType == "text/plain")
        {
            using var reader = new StreamReader(fileStream);
            extractedText = await reader.ReadToEndAsync(ct);
        }
        else if (isJson)
        {
            using var reader = new StreamReader(fileStream);
            var rawJson = await reader.ReadToEndAsync(ct);
            extractedText = await _aiClient.TransformJsonToDescriptionsAsync(rawJson, ct);
        }
        else
        {
            extractedText = await _aiClient.ExtractTextFromFileAsync(fileStream, mimeType, ct);
        }

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            return new DocumentResult(fileName, 0, "No text content extracted.");
        }

        // Debug: Lưu vào thư mục riêng để tránh dotnet-watch restart
        var debugDir = Path.Combine(Directory.GetCurrentDirectory(), ".temp_debug");
        if (!Directory.Exists(debugDir)) Directory.CreateDirectory(debugDir);
        
        var debugPath = Path.Combine(debugDir, $"debug_{fileName}.txt");
        await File.WriteAllTextAsync(debugPath, extractedText, ct);

        // Step 2: Chunking
        await onProgress(30, "Đang phân tích cấu trúc đoạn văn...");
        var separator = isJson ? "\n\n\n" : "\n\n";
        var chunks = ChunkBySeparator(extractedText, separator);

        // Step 3: Embed each chunk
        var points = new List<(IReadOnlyList<float> Vector, string Text, string FileName, int Index)>();
        for (int i = 0; i < chunks.Count; i++)
        {
            int percent = 30 + (int)((i / (float)chunks.Count) * 60);
            await onProgress(percent, $"Đang tạo vector cho đoạn {i + 1}/{chunks.Count}...");
            
            var chunk = chunks[i];
            var vector = await _aiClient.GetEmbeddingAsync(chunk, "RETRIEVAL_DOCUMENT", 3072, ct);
            points.Add((vector, chunk, fileName, i));
        }

        // Step 4: Upsert to Qdrant
        await onProgress(95, "Đang lưu dữ liệu vào Qdrant Cloud...");
        await _qdrantService.UpsertPointsAsync(points, collectionName, ct);

        await onProgress(100, "Hoàn tất!");
        return new DocumentResult(fileName, chunks.Count, "Success");
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
