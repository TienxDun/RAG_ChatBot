using System.Text;
using System.Text.Json;
using Backend.Models;

namespace Backend.Services;

public sealed class VertexAiClient
{
    private readonly HttpClient _httpClient;
    private readonly VertexAiOptions _options;

    public VertexAiClient(HttpClient httpClient, VertexAiOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<string> GenerateContentAsync(string message, CancellationToken ct)
    {
        var url = _options.ApiUrlTemplate
            .Replace("{region}", _options.Region)
            .Replace("{projectId}", _options.ProjectId)
            .Replace("{modelId}", _options.LlmModelId)
            .Replace("{action}", "generateContent") + $"?key={_options.ApiKey}";
        var payload = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new { text = message }
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Vertex AI error ({(int)response.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];
            if (candidate.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var textElement))
            {
                return textElement.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    public async Task<string> TransformJsonToDescriptionsAsync(string jsonContent, CancellationToken ct)
    {
        var prompt = $@"Đây là dữ liệu cấu trúc database dưới dạng JSON:
        {jsonContent}

        Nhiệm vụ của bạn:
        1. Phân tích từng đối tượng (table) trong JSON.
        2. Chuyển đổi mỗi bảng thành một đoạn văn mô tả tiếng Việt mạch lạc và chi tiết.
        3. Trong mỗi đoạn mô tả, phải nêu rõ: Tên bảng, Chức năng của bảng, và Danh sách các cột kèm ý nghĩa của chúng.
        4. QUAN TRỌNG: Phân cách mô tả của mỗi bảng bằng chính xác 3 dấu xuống dòng (\n\n\n) để tôi có thể cắt đoạn.
        5. Không thêm lời dẫn, chỉ trả về các đoạn mô tả.";

        return await GenerateContentAsync(prompt, ct);
    }

    public async Task<IReadOnlyList<float>> GetEmbeddingAsync(
        string text,
        string? taskType,
        int? outputDimensionality,
        CancellationToken ct)
    {
        var url = _options.ApiUrlTemplate
            .Replace("{region}", _options.Region)
            .Replace("{projectId}", _options.ProjectId)
            .Replace("{modelId}", _options.EmbeddingModelId)
            .Replace("{action}", "predict") + $"?key={_options.ApiKey}";
        var resolvedTaskType = string.IsNullOrWhiteSpace(taskType) ? "RETRIEVAL_QUERY" : taskType;
        var parameters = new Dictionary<string, object>
        {
            ["autoTruncate"] = true
        };

        if (outputDimensionality.HasValue)
        {
            parameters["outputDimensionality"] = outputDimensionality.Value;
        }

        var payload = new
        {
            instances = new[]
            {
                new
                {
                    content = text,
                    task_type = resolvedTaskType
                }
            },
            parameters
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Vertex AI error ({(int)response.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("predictions", out var predictions) && predictions.GetArrayLength() > 0)
        {
            var prediction = predictions[0];
            if (prediction.TryGetProperty("embeddings", out var embeddings) &&
                embeddings.TryGetProperty("values", out var valuesElement))
            {
                var values = new List<float>();
                foreach (var value in valuesElement.EnumerateArray())
                {
                    values.Add((float)value.GetDouble());
                }

                return values;
            }
        }

        return Array.Empty<float>();
    }

    public async Task<string> ExtractTextFromFileAsync(Stream fileStream, string mimeType, CancellationToken ct)
    {
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream, ct);
        var base64Data = Convert.ToBase64String(memoryStream.ToArray());

        var url = _options.ApiUrlTemplate
            .Replace("{region}", _options.Region)
            .Replace("{projectId}", _options.ProjectId)
            .Replace("{modelId}", _options.LlmModelId)
            .Replace("{action}", "generateContent") + $"?key={_options.ApiKey}";

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new
                        {
                            inlineData = new
                            {
                                mimeType,
                                data = base64Data
                            }
                        },
                        new
                        {
                            text = @"Hãy đọc và bóc tách toàn bộ nội dung văn bản từ tài liệu này.
                            Yêu cầu:
                            - Giữ nguyên cấu trúc đoạn văn, tiêu đề, danh sách.
                            - Phân tách rõ ràng các phần bằng dấu xuống dòng kép.
                            - Chỉ trả về nội dung text thuần, không thêm bình luận hay giải thích.
                            - Nếu có bảng biểu, chuyển thành dạng text dễ đọc."
                        }
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Vertex AI error ({(int)response.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];
            if (candidate.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var textElement))
            {
                return textElement.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    
}
