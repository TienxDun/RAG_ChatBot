using System.Text;
using System.Text.Json;
using Backend.Models;

namespace Backend.Services;

public sealed class VertexAiClient
{
    private readonly HttpClient _httpClient;
    private readonly VertexAiOptions _options;

    // Cấu hình JsonSerializer để hỗ trợ UTF-8 và tránh escape ký tự
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // Khởi tạo VertexAiClient với HttpClient và cấu hình VertexAiOptions.
    public VertexAiClient(HttpClient httpClient, VertexAiOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    // Gửi yêu cầu sinh nội dung văn bản tới mô hình Gemini của Vertex AI dựa trên prompt đầu vào.
    public async Task<string> GenerateContentAsync(string message, CancellationToken ct)
    {
        // 1. Tạo URL endpoint API bằng cách thay thế các placeholder trong cấu hình template và đính kèm khóa API
        var url = _options.ApiUrlTemplate
            .Replace("{modelId}", _options.LlmModelId)
            .Replace("{action}", "generateContent") + $"?key={_options.ApiKey}";

        // 2. Thiết lập cấu trúc dữ liệu JSON payload để gửi tới API (theo chuẩn đặc tả của Google Vertex AI / Gemini API)
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
            },
            // Cấu hình tham số sinh văn bản: temperature bằng 0 để đảm bảo kết quả sinh ra ổn định và chính xác nhất
            generationConfig = new
            {
                temperature = 0.0,
                topP = 0.95,
                topK = 40,
                maxOutputTokens = 8192,
                responseMimeType = "text/plain"
            }
        };

        // 3. Khởi tạo HttpRequestMessage dạng POST với dữ liệu payload được tuần tự hóa thành chuỗi JSON dạng UTF-8
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json")
        };

        // 4. Gửi yêu cầu HTTP POST bất đồng bộ tới API Vertex AI và đọc kết quả trả về dưới dạng chuỗi
        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        // 5. Kiểm tra mã trạng thái HTTP, nếu không thành công (không thuộc dải 2xx) thì ném ngoại lệ kèm thông báo chi tiết
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Vertex AI error ({(int)response.StatusCode}): {body}");
        }

        // 6. Phân tích chuỗi JSON phản hồi từ API để trích xuất văn bản câu trả lời sinh ra từ mô hình
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];
            if (candidate.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var textElement))
            {
                // Trả về chuỗi văn bản kết quả đã bóc tách thành công từ cấu trúc JSON
                return textElement.GetString() ?? string.Empty;
            }
        }

        // Trả về chuỗi rỗng nếu không tìm thấy nội dung phản hồi hợp lệ trong cấu trúc JSON
        return string.Empty;
    }

    // Phân tích và chuyển đổi dữ liệu cấu trúc database dạng JSON thành các mô tả văn bản chi tiết bằng tiếng Việt.
    public async Task<string> TransformJsonToDescriptionsAsync(string jsonContent, CancellationToken ct)
    {
        var prompt = $@"Đây là dữ liệu cấu trúc database dưới dạng JSON:
        {jsonContent}

        Nhiệm vụ của bạn:
        1. Phân tích TẤT CẢ các đối tượng (table) trong JSON, không được bỏ sót bất kỳ bảng nào.
        2. Chuyển đổi mỗi bảng thành một đoạn văn mô tả tiếng Việt chi tiết và đầy đủ.
        3. Trong mỗi đoạn mô tả, phải liệt kê ĐẦY ĐỦ: Tên bảng, Chức năng, và một bảng Markdown liệt kê toàn bộ các cột kèm kiểu dữ liệu/ý nghĩa.
        4. QUAN TRỌNG: Phân cách mô tả của mỗi bảng bằng chính xác 3 dấu xuống dòng (\n\n\n).
        5. Giữ nguyên các thuật ngữ kỹ thuật, không tóm tắt làm mất thông tin. Không thêm lời dẫn.";

        return await GenerateContentAsync(prompt, ct);
    }

    // Gọi API Vertex AI để chuyển đổi đoạn văn bản thành vector (Embedding) phục vụ cho việc tìm kiếm tương đồng.
    public async Task<IReadOnlyList<float>> GetEmbeddingAsync(
        string text,
        string? taskType,
        int? outputDimensionality,
        CancellationToken ct)
    {
        var url = _options.ApiUrlTemplate
            .Replace("{modelId}", _options.EmbeddingModelId)
            .Replace("{action}", "predict") + $"?key={_options.ApiKey}";
        var resolvedTaskType = string.IsNullOrWhiteSpace(taskType) ? "RETRIEVAL_QUERY" : taskType;
        var parameters = new Dictionary<string, object>
        {
            ["autoTruncate"] = false
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
            Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json")
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

    // Đọc và bóc tách toàn bộ nội dung văn bản cũng như bảng biểu từ tệp tài liệu và chuyển sang định dạng Markdown.
    public async Task<string> ExtractTextFromFileAsync(Stream fileStream, string mimeType, CancellationToken ct)
    {
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream, ct);
        var base64Data = Convert.ToBase64String(memoryStream.ToArray());

        var url = _options.ApiUrlTemplate
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
                            text = @"Hãy đọc và bóc tách ĐẦY ĐỦ toàn bộ nội dung văn bản từ tài liệu này sang định dạng Markdown. 
                            Yêu cầu cực kỳ quan trọng: 
                            - KHÔNG ĐƯỢC tóm tắt, không được bỏ sót bất kỳ đoạn văn nào.
                            - Giữ nguyên cấu trúc phân cấp (Tiêu đề #, ##, ###).
                            - ĐẶC BIỆT: Nếu có bảng biểu, hãy chuyển đổi chính xác sang định dạng Markdown Table (ví dụ: | Header | Header |). Không được bỏ sót bất kỳ ô dữ liệu nào.
                            - Phân tách các đoạn văn hoặc các phần bằng chính xác 2 dấu xuống dòng (\n\n).
                            - Chỉ trả về nội dung văn bản gốc, không thêm lời bình luận."
                        }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.0,
                topP = 0.95,
                topK = 40,
                maxOutputTokens = 8192,
                responseMimeType = "text/plain"
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json")
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
