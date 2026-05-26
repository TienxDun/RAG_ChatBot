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
                    var sb = new StringBuilder();
                    string embeddingText = string.Empty;

                    // Bỏ qua file global_rules vì sẽ được nạp trực tiếp từ đĩa trong Orchestrator
                    if (item.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "global_rules")
                    {
                        continue;
                    }

                    // Nhận diện cấu trúc database schema: có key "table" + "columns"
                    JsonElement columnsProp = default;
                    bool isDbSchema = item.TryGetProperty("table", out var tableProp) 
                                      && item.TryGetProperty("columns", out columnsProp) 
                                      && columnsProp.ValueKind == JsonValueKind.Array;

                    if (isDbSchema)
                    {
                        var tableName = tableProp.GetString() ?? "Unknown";
                        
                        // Đọc purpose / description
                        var purpose = string.Empty;
                        if (item.TryGetProperty("purpose", out var purposeProp)) {
                            purpose = purposeProp.GetString() ?? "";
                        } else if (item.TryGetProperty("description", out var descProp)) {
                            purpose = descProp.GetString() ?? "";
                        }
                        
                        sb.AppendLine($"# BẢNG: {tableName}");
                        if (!string.IsNullOrWhiteSpace(purpose))
                        {
                            sb.AppendLine($"**Mục đích:** {purpose}");
                        }

                        // Đọc when_to_use / when_not_to_use
                        if (item.TryGetProperty("when_to_use", out var wtuProp) && !string.IsNullOrWhiteSpace(wtuProp.GetString()))
                        {
                            sb.AppendLine($"**Khi nào dùng:** {wtuProp.GetString()}");
                        }
                        if (item.TryGetProperty("when_not_to_use", out var wntuProp) && !string.IsNullOrWhiteSpace(wntuProp.GetString()))
                        {
                            sb.AppendLine($"**Khi nào KHÔNG dùng:** {wntuProp.GetString()}");
                        }
                        sb.AppendLine();

                        // Đọc table_rules
                        if (item.TryGetProperty("table_rules", out var rulesProp) && rulesProp.ValueKind == JsonValueKind.Array)
                        {
                            var rules = rulesProp.EnumerateArray().ToList();
                            if (rules.Count > 0)
                            {
                                sb.AppendLine("## Quy tắc hoạt động (Table Rules):");
                                foreach (var rule in rules)
                                {
                                    var rId = rule.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                                    var rSeverity = rule.TryGetProperty("severity", out var sevProp) ? sevProp.GetString() ?? "" : "";
                                    var rText = rule.TryGetProperty("rule", out var ruleProp2) ? ruleProp2.GetString() ?? "" : "";
                                    var rCorrect = rule.TryGetProperty("correct_example", out var corProp) ? corProp.GetString() ?? "" : "";
                                    var rWrong = rule.TryGetProperty("wrong_example", out var wrgProp) ? wrgProp.GetString() ?? "" : "";

                                    sb.AppendLine($"- [{rId}] [{rSeverity}]: {rText}");
                                    if (!string.IsNullOrWhiteSpace(rCorrect)) sb.AppendLine($"  * Ví dụ ĐÚNG: `{rCorrect}`");
                                    if (!string.IsNullOrWhiteSpace(rWrong)) sb.AppendLine($"  * Ví dụ SAI: `{rWrong}`");
                                }
                                sb.AppendLine();
                            }
                        }

                        // Đọc columns
                        var columns = columnsProp.EnumerateArray().ToList();
                        sb.AppendLine($"## Cấu trúc cột ({columns.Count} cột):");
                        sb.AppendLine();
                        sb.AppendLine("| Tên cột | Kiểu dữ liệu | Vai trò (Role) | Mô tả |");
                        sb.AppendLine("|---------|--------------|----------------|-------|");

                        foreach (var col in columns)
                        {
                            var colName = col.TryGetProperty("name", out var cn) ? cn.GetString() ?? "" : "";
                            
                            // Kiểu dữ liệu (type / data_type)
                            var colType = string.Empty;
                            if (col.TryGetProperty("type", out var ctProp)) {
                                colType = ctProp.GetString() ?? "";
                            } else if (col.TryGetProperty("data_type", out var dtProp)) {
                                colType = dtProp.GetString() ?? "";
                            }
                            
                            // Vai trò (role - chỉ ở file mới)
                            var colRole = col.TryGetProperty("role", out var crProp) ? crProp.GetString() ?? "" : "";

                            // Mô tả (desc / description)
                            var colDesc = string.Empty;
                            if (col.TryGetProperty("desc", out var cdProp)) {
                                colDesc = cdProp.GetString() ?? "";
                            } else if (col.TryGetProperty("description", out var descProp2)) {
                                colDesc = descProp2.GetString() ?? "";
                            }
                            
                            colDesc = colDesc.Replace("|", "\\|");
                            
                            sb.AppendLine($"| {colName} | {colType} | {colRole} | {colDesc} |");
                        }
                        sb.AppendLine();

                        // Đọc relationships
                        if (item.TryGetProperty("relationships", out var relsProp) && relsProp.ValueKind == JsonValueKind.Array)
                        {
                            var rels = relsProp.EnumerateArray().ToList();
                            if (rels.Count > 0)
                            {
                                sb.AppendLine("## Mối quan hệ liên kết (Relationships):");
                                foreach (var rel in rels)
                                {
                                    var targetTable = rel.TryGetProperty("target_table", out var ttProp) ? ttProp.GetString() ?? "" : "";
                                    var joinOn = rel.TryGetProperty("join_on", out var joProp) ? joProp.GetString() ?? "" : "";
                                    var notes = rel.TryGetProperty("notes", out var ntProp) ? ntProp.GetString() ?? "" : "";

                                    sb.AppendLine($"- Liên kết với `{targetTable}` qua `{joinOn}` ({notes})");
                                }
                            }
                        }

                        // Lưu metadata đầy đủ
                        metadata["table"] = tableName;
                        metadata["purpose"] = purpose;
                        metadata["columns"] = columnsProp.ToString();
                        metadata["column_count"] = columns.Count.ToString();
                        
                        if (item.TryGetProperty("table_rules", out var rProp))
                        {
                            metadata["table_rules"] = rProp.ToString();
                        }
                        if (item.TryGetProperty("relationships", out var reProp))
                        {
                            metadata["relationships"] = reProp.ToString();
                        }

                        // Tạo văn bản rút gọn chuyên biệt để tính Embedding (tránh tràn 2048 tokens của Vertex AI)
                        var sbEmbed = new StringBuilder();
                        sbEmbed.AppendLine($"# BẢNG: {tableName}");
                        if (!string.IsNullOrWhiteSpace(purpose))
                        {
                            sbEmbed.AppendLine($"**Mục đích:** {purpose}");
                        }
                        if (item.TryGetProperty("when_to_use", out var wtuPropEmbed) && !string.IsNullOrWhiteSpace(wtuPropEmbed.GetString()))
                        {
                            sbEmbed.AppendLine($"**Khi nào dùng:** {wtuPropEmbed.GetString()}");
                        }
                        if (item.TryGetProperty("when_not_to_use", out var wntuPropEmbed) && !string.IsNullOrWhiteSpace(wntuPropEmbed.GetString()))
                        {
                            sbEmbed.AppendLine($"**Khi nào KHÔNG dùng:** {wntuPropEmbed.GetString()}");
                        }
                        sbEmbed.AppendLine();

                        if (item.TryGetProperty("table_rules", out var rulesPropEmbed) && rulesPropEmbed.ValueKind == JsonValueKind.Array)
                        {
                            var rules = rulesPropEmbed.EnumerateArray().ToList();
                            if (rules.Count > 0)
                            {
                                sbEmbed.AppendLine("## Quy tắc:");
                                foreach (var rule in rules)
                                {
                                    var rId = rule.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                                    var rText = rule.TryGetProperty("rule", out var ruleProp2) ? ruleProp2.GetString() ?? "" : "";
                                    sbEmbed.AppendLine($"- [{rId}]: {rText}");
                                }
                                sbEmbed.AppendLine();
                            }
                        }

                        sbEmbed.AppendLine($"## Các cột:");
                        foreach (var col in columns)
                        {
                            var colName = col.TryGetProperty("name", out var cn) ? cn.GetString() ?? "" : "";
                            var colDesc = string.Empty;
                            if (col.TryGetProperty("desc", out var cdProp)) {
                                colDesc = cdProp.GetString() ?? "";
                            } else if (col.TryGetProperty("description", out var descProp2)) {
                                colDesc = descProp2.GetString() ?? "";
                            }
                            sbEmbed.AppendLine($"- {colName}: {colDesc}");
                        }

                        embeddingText = sbEmbed.ToString().Trim();
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

                        embeddingText = sb.ToString().Trim();
                    }

                    var descriptiveText = sb.ToString().Trim();
                    
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
