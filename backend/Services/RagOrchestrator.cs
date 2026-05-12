using System.Data;
using System.Text;
using System.Text.Json;
using Backend.Models;

namespace Backend.Services;

public sealed class RagOrchestrator
{
    private readonly VertexAiClient _aiClient;
    private readonly QdrantService _qdrantService;
    private readonly SqlService _sqlService;
    private readonly VertexAiOptions _options;

    public RagOrchestrator(VertexAiClient aiClient, QdrantService qdrantService, SqlService sqlService, VertexAiOptions options)
    {
        _aiClient = aiClient;
        _qdrantService = qdrantService;
        _sqlService = sqlService;
        _options = options;
    }

    public async Task<ChatResponse> ProcessQueryAsync(string userQuery, string? collectionName, Func<RagStep, Task> onStep, CancellationToken ct)
    {
        var steps = new List<RagStep>();

        // 0. Khởi tạo
        await onStep(new RagStep("System Initialization", "Đang khởi tạo luồng xử lý và chuẩn bị kết nối tới AI Engine..."));

        // 1. Get Embeddings for the question
        var vector = await _aiClient.GetEmbeddingAsync(userQuery, "RETRIEVAL_QUERY", 3072, ct);
        var step1 = new RagStep("Vectorization", $"Câu hỏi đã được chuyển đổi thành vector 3072 chiều.");
        steps.Add(step1);
        await onStep(step1);

        // 2. Search Qdrant for relevant schema context
        var schemaContexts = await _qdrantService.SearchSchemaAsync(vector, limit: _options.TopK, collectionName: collectionName);
        
        // Sắp xếp context theo bảng chữ cái để đảm bảo Prompt luôn nhất quán
        var orderedContexts = schemaContexts.OrderBy(s => s).ToList();

        // Format kết quả retrieval với phân cách và đánh số rõ ràng
        var schemaInfoBuilder = new StringBuilder();
        for (int i = 0; i < orderedContexts.Count; i++)
        {
            if (i > 0) schemaInfoBuilder.AppendLine("\n---\n");
            schemaInfoBuilder.AppendLine($"**[{i + 1}/{orderedContexts.Count}]**");
            schemaInfoBuilder.AppendLine(orderedContexts[i]);
        }
        var schemaInfo = schemaInfoBuilder.ToString();
        
        var step2 = new RagStep("Schema Retrieval", $"Tìm thấy {schemaContexts.Count} cấu trúc database liên quan nhất:\n\n{schemaInfo}");
        steps.Add(step2);
        await onStep(step2);

        // 3 & 4. SQL Generation & Execution Loop (Self-Healing)
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
        var currentTimeStr = now.ToString("dd/MM/yyyy HH:mm");
        
        string generatedSql = string.Empty;
        string sqlResultJson = string.Empty;
        DataTable? sqlResultDataTable = null;
        string lastError = string.Empty;
        int maxAttempts = 3;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var sqlPrompt = string.Empty;
            if (attempt == 1)
            {
                sqlPrompt = $@"Bạn là chuyên gia SQL Server hàng đầu cho ngành may mặc.
                    Thời gian hệ thống (UTC+7): {currentTimeStr}

                    DỰA TRÊN CẤU TRÚC DATABASE:
                    {schemaInfo}

                    NHIỆM VỤ: Viết câu lệnh SQL duy nhất cho câu hỏi: ""{userQuery}""

                    LƯU Ý QUAN TRỌNG (BẮT BUỘC TUÂN THỦ):

                    1. ĐỊNH DẠNG OUTPUT:
                       - CHỈ trả về mã SQL thô. KHÔNG giải thích, KHÔNG markdown, KHÔNG dấu chấm phẩy cuối.

                    2. QUY TẮC XỬ LÝ DỮ LIỆU (ÁP DỤNG THEO NGỮ CẢNH):
                       - 🛡️ Lọc Ngày: Cột ngày thường chứa giờ, nên BẮT BUỘC dùng CAST(Tên_Cột AS DATE) = 'YYYY-MM-DD'. Nếu so sánh trực tiếp sẽ trả về NULL.
                       - 🛡️ Lọc Chuyền: Cột LineX là kiểu INT. KHÔNG được dùng nháy đơn (viết LineX = 101, KHÔNG viết LineX = '101').
                       - 🛡️ Phân luồng dữ liệu: 
                         1. NĂNG SUẤT: Dùng SEW_CoefficientStyle + tbl_Steps. Lọc mã hàng tại S.StypeId. Công thức: SUM(Quantity), SUM(DefectNo).
                         2. TỈ LỆ LỖI %: Dùng QTY_MAHANG_NGAYKIEM (Lỗi) + SEW_CoefficientSize (Đạt). Lọc mã hàng tại cột MaHang hoặc StyleId. BẮT BUỘC dùng công thức % = Lỗi / (Lỗi + Đạt).
                       - 🛡️ Quy tắc ""Chống nhầm lẫn"": TUYỆT ĐỐI KHÔNG tính 'Tỉ lệ lỗi %' bằng bảng SEW_CoefficientStyle. Bảng đó chỉ dùng cho Năng suất.

                    3. RÀNG BUỘC HỆ THỐNG:
                       - LUÔN thêm tiền tố N trước các chuỗi giá trị Tiếng Việt. Dùng mốc {currentTimeStr} cho các câu hỏi về thời gian.
                       - ĐƯỢC PHÉP dùng CTE (WITH), DECLARE, #Temp. TUYỆT ĐỐI CẤM các lệnh thay đổi dữ liệu (UPDATE, DELETE, DROP...).
                       - Ưu tiên xuất ra cả các giá trị thành phần (ví dụ: Tổng lỗi, Tổng đạt) kèm kết quả cuối để đối soát.";
            }
            else
            {
                sqlPrompt = $@"Câu lệnh SQL bạn vừa tạo đã gặp lỗi: ""{lastError}""
                    
                    Câu lệnh lỗi:
                    ```sql
                    {generatedSql}
                    ```
                    
                    YÊU CẦU:
                    1. Phân tích lỗi trên (thường là do sai tên cột hoặc bảng).
                    2. Kiểm tra lại cấu trúc database thật kỹ:
                    {schemaInfo}
                    3. Viết lại câu lệnh SQL CHÍNH XÁC. Nếu không chắc chắn về một cột nào đó, hãy để NULL thay vì đoán bừa.
                    
                    Lưu ý: Chỉ trả về mã SQL mới, không giải thích.";
                
                var healingStep = new RagStep($"Self-Healing (Lần {attempt - 1})", $"AI đang sửa lỗi SQL...\nLỗi vừa gặp: {lastError}");
                steps.Add(healingStep);
                await onStep(healingStep);
            }

            generatedSql = await _aiClient.GenerateContentAsync(sqlPrompt, ct);
            generatedSql = CleanSql(generatedSql);
            
            try 
            {
                sqlResultDataTable = await _sqlService.ExecuteQueryAsDataTableAsync(generatedSql, ct);
                
                // Convert DataTable to Json for UI steps and Final Answer generation
                var results = new List<Dictionary<string, object>>();
                foreach (DataRow row in sqlResultDataTable.Rows)
                {
                    var dict = new Dictionary<string, object>();
                    foreach (DataColumn col in sqlResultDataTable.Columns)
                    {
                        dict[col.ColumnName] = row[col] == DBNull.Value ? null! : row[col];
                    }
                    results.Add(dict);
                }
                sqlResultJson = JsonSerializer.Serialize(results, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                var stepSuccess = new RagStep($"SQL Generation & Execution (Lần {attempt})", $"Mã SQL chạy thành công:\n\n```sql\n{generatedSql}\n```\n\nKết quả JSON:\n\n```json\n{sqlResultJson}\n```");
                steps.Add(stepSuccess);
                await onStep(stepSuccess);
                break; // Thành công thì thoát vòng lặp
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                if (attempt == maxAttempts)
                {
                    var finalFailStep = new RagStep("SQL Final Failure", $"Đã thử {maxAttempts} lần nhưng vẫn gặp lỗi: {lastError}");
                    steps.Add(finalFailStep);
                    await onStep(finalFailStep);
                    sqlResultJson = $"[ERROR] Đã thử {maxAttempts} lần nhưng không thể tạo truy vấn SQL chính xác. Lỗi cuối cùng: {lastError}";
                }
            }
        }

        // 5. Generate final natural language answer + suggestions
        var finalPrompt = $@"Bạn là một trợ lý ảo phân tích dữ liệu sản xuất chuyên nghiệp. 
            Thời gian hệ thống hiện tại (UTC+7): {currentTimeStr}
            Câu hỏi của người dùng: ""{userQuery}""
            Dữ liệu thực tế từ hệ thống (JSON):
            {sqlResultJson}

            NHIỆM VỤ:
            1. Trình bày câu trả lời cực kỳ CHUYÊN NGHIỆP và ĐẸP MẮT theo định dạng Markdown:
               - ### 💠 Tổng quan kết quả
                 [Câu trả lời trực tiếp. **BẮT BUỘC nêu rõ mốc thời gian, ngày tháng năm** liên quan đến dữ liệu này dựa trên thời gian hệ thống hoặc dữ liệu truy vấn được. In đậm các con số quan trọng]. Ngắt dòng hợp lý.

               - ### 📋 Bảng dữ liệu chi tiết
                 [Nếu có danh sách dữ liệu, BẮT BUỘC sử dụng bảng Markdown với các cột rõ ràng. Nếu chỉ có 1 con số, hãy trình bày dạng danh sách bullet points].
                 Lưu ý: Format số có dấu phẩy ngăn cách hàng nghìn (ví dụ: 67,800).

               - ### ⚡ Phân tích logic
                 [Giải thích ngắn gọn logic hoặc công thức tính toán].

            2. Đề xuất 3 câu hỏi tiếp theo (đa dạng, thực tế).

            YÊU CẦU ĐỊNH DẠNG: Trả về JSON duy nhất:
            {{
                ""answer"": ""nội dung markdown ở đây"",
                ""suggestions"": [""câu hỏi 1"", ""câu hỏi 2"", ""câu hỏi 3""]
            }}

            LƯU Ý QUAN TRỌNG:
            - Nếu dữ liệu JSON có thông báo [ERROR], hãy trả lời: ""Rất tiếc, hệ thống gặp khó khăn khi truy vấn dữ liệu này. Vui lòng thử lại hoặc diễn đạt câu hỏi theo cách khác.""
            - Luôn sử dụng \n cho các lần xuống dòng trong JSON string.
            - Không sử dụng quá nhiều chữ, tập trung vào số liệu và bảng.";

        var rawResponse = await _aiClient.GenerateContentAsync(finalPrompt, ct);
        
        string finalText = rawResponse;
        List<string> suggestions = new();

        try 
        {
            // Làm sạch chuỗi JSON nếu AI thêm markdown block
            var jsonString = rawResponse.Replace("```json", "").Replace("```", "").Trim();
            var result = JsonSerializer.Deserialize<JsonElement>(jsonString);
            
            finalText = result.GetProperty("answer").GetString() ?? rawResponse;
            suggestions = result.GetProperty("suggestions").EnumerateArray()
                                .Select(x => x.GetString() ?? "")
                                .Where(x => !string.IsNullOrEmpty(x))
                                .ToList();
        }
        catch
        {
            // Fallback nếu JSON parse lỗi
            finalText = rawResponse;
        }

        return new ChatResponse(finalText, steps, suggestions, sqlResultJson, sqlResultDataTable);
    }

    private string CleanSql(string sql)
    {
        // Loại bỏ markdown code blocks nếu AI lỡ tay thêm vào, đồng thời xóa luôn dấu chấm phẩy thừa ở cuối câu
        return sql.Replace("```sql", "").Replace("```", "").Trim(' ', '\n', '\r', '\t', ';');
    }
}
