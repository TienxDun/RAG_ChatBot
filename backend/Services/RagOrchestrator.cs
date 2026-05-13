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

        // 0. Khởi tạo & 1. Get Embeddings song song
        var initTask = onStep(new RagStep("System Initialization", "Đang khởi tạo luồng xử lý và chuẩn bị kết nối tới AI Engine..."));
        
        var vectorTask = _aiClient.GetEmbeddingAsync(userQuery, "RETRIEVAL_QUERY", 3072, ct);
        
        await initTask;
        var vector = await vectorTask;

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
        
        var step2 = new RagStep("Schema Retrieval", $"Tìm thấy {schemaContexts.Count} cấu trúc database liên quan.");
        steps.Add(step2);
        await onStep(step2);

        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
        var currentTimeStr = now.ToString("dd/MM/yyyy HH:mm");

        // 3. Planning Phase: AI đánh giá phạm vi và lập kế hoạch
        await onStep(new RagStep("Execution Planning", "AI đang phân tích câu hỏi và lập kế hoạch truy vấn..."));
        var planningPrompt = $@"Bạn là chuyên gia phân tích yêu cầu và lập kế hoạch truy vấn SQL.
            Dựa trên CẤU TRÚC DATABASE được cung cấp:
            {schemaInfo}

            CÂU HỎI CỦA NGƯỜI DÙNG: ""{userQuery}""

            NHIỆM VỤ CỦA BẠN:
            1. Kiểm tra xem câu hỏi có liên quan đến dữ liệu trong các bảng trên hay không.
            2. Nếu câu hỏi KHÔNG liên quan đến database (hỏi kiến thức chung, thời tiết, linh tinh) hoặc không thể trả lời bằng các bảng này, hãy đặt `isOutOfScope: true`.
            3. Nếu câu hỏi HỢP LỆ, hãy đặt `isOutOfScope: false` và chia nhỏ câu hỏi thành các bước truy vấn SQL logic.
               - Với câu hỏi đơn giản: Chỉ cần 1 bước.
               - Với câu hỏi phức tạp (cần tìm ID trước, hoặc join nhiều bảng): Chia tối đa 5 bước.
            4. Mỗi bước phải là một nhiệm vụ ĐƠN LẺ và CỤ THỂ.

            YÊU CẦU ĐỊNH DẠNG (BẮT BUỘC TRẢ VỀ JSON):
            {{
                ""isOutOfScope"": false,
                ""reason"": ""Lý do tại sao câu hỏi này nằm trong/ngoài phạm vi"",
                ""steps"": [""Mô tả bước 1"", ""Mô tả bước 2""]
            }}";

        var planResponse = await _aiClient.GenerateContentAsync(planningPrompt, ct);
        var planJson = planResponse.Replace("```json", "").Replace("```", "").Trim();
        var stepsToExecute = new List<string>();
        bool isOutOfScope = false;

        try {
            var planObj = JsonSerializer.Deserialize<JsonElement>(planJson);
            isOutOfScope = planObj.GetProperty("isOutOfScope").GetBoolean();
            if (!isOutOfScope)
            {
                stepsToExecute = planObj.GetProperty("steps").EnumerateArray().Select(x => x.GetString()!).ToList();
            }
        } catch { 
            // Fallback nếu JSON lỗi: Giả định là hợp lệ và chạy 1 bước với câu hỏi gốc
            stepsToExecute = new List<string> { userQuery }; 
        }

        // Nếu ngoài phạm vi, bỏ qua bước thực thi SQL
        var workingContext = new StringBuilder();
        string lastStepJson = string.Empty;
        DataTable? lastDataTable = null;

        if (isOutOfScope)
        {
            var outOfScopeStep = new RagStep("Scope Guarding", "Rất tiếc, câu hỏi của bạn nằm ngoài phạm vi dữ liệu mà tôi có thể truy cập.");
            steps.Add(outOfScopeStep);
            await onStep(outOfScopeStep);
        }
        else 
        {
            // 4. Execution Phase: Loop through planned steps
            workingContext.AppendLine("KẾT QUẢ CÁC BƯỚC TRƯỚC ĐÓ:");
            
            for (int i = 0; i < stepsToExecute.Count; i++)
            {
                var currentStepDesc = stepsToExecute[i];
                var stepTitle = $"Step {i + 1}/{stepsToExecute.Count}";
                await onStep(new RagStep(stepTitle, $"Đang thực hiện: {currentStepDesc}"));

                string generatedSql = string.Empty;
                string lastError = string.Empty;
                int stepMaxAttempts = 2;

                for (int attempt = 1; attempt <= stepMaxAttempts; attempt++)
                {
                    var sqlPrompt = $@"Bạn là chuyên gia SQL Server cao cấp.
                        Cấu trúc database: {schemaInfo}
                        {workingContext}

                        NHIỆM VỤ HIỆN TẠI: {currentStepDesc}
                        CÂU HỎI GỐC: ""{userQuery}""

                        QUY TẮC VIẾT SQL:
                        1. 🎯 CHỈ thực hiện nhiệm vụ trong 'NHIỆM VỤ HIỆN TẠI'. 
                        2. 🔗 BẮT BUỘC sử dụng giá trị thực tế từ 'KẾT QUẢ CÁC BƯỚC TRƯỚC ĐÓ' để làm điều kiện lọc (WHERE).
                        3. Nếu nhiệm vụ yêu cầu 'tìm', 'liệt kê', hãy sử dụng TOP để giới hạn nếu cần thiết (mặc định TOP 100 nếu không yêu cầu cụ thể).
                        4. Trả về mã SQL thô, không giải thích, không markdown.
                        {(string.IsNullOrEmpty(lastError) ? "" : $"\nLỖI TRƯỚC ĐÓ: {lastError}\nHãy sửa SQL.")}";

                    generatedSql = await _aiClient.GenerateContentAsync(sqlPrompt, ct);
                    generatedSql = CleanSql(generatedSql);

                    try 
                    {
                        var dt = await _sqlService.ExecuteQueryAsDataTableAsync(generatedSql, ct);
                        lastDataTable = dt;

                        var rows = new List<Dictionary<string, object>>();
                        foreach (DataRow row in dt.Rows)
                        {
                            var dict = new Dictionary<string, object>();
                            foreach (DataColumn col in dt.Columns) dict[col.ColumnName] = row[col] == DBNull.Value ? null! : row[col];
                            rows.Add(dict);
                        }
                        var stepJson = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                        
                        lastStepJson = stepJson;
                        workingContext.AppendLine($"\n--- [Kết quả {stepTitle}: {currentStepDesc}] ---\n{stepJson}");

                        var stepLog = new RagStep(stepTitle, $"Hoàn thành: {currentStepDesc}\n\n```sql\n{generatedSql}\n```\n\nKết quả:\n```json\n{stepJson}\n```");
                        steps.Add(stepLog);
                        await onStep(stepLog);
                        break; 
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                        if (attempt == stepMaxAttempts)
                        {
                            var failLog = new RagStep(stepTitle, $"Thất bại: {lastError}");
                            steps.Add(failLog);
                            await onStep(failLog);
                        }
                    }
                }
            }
        }

        // 5. Final Generation
        var finalPrompt = $@"Bạn là trợ lý ảo phân tích dữ liệu doanh nghiệp thông minh.
            Thời gian hệ thống: {currentTimeStr}
            Câu hỏi: ""{userQuery}""
            Trạng thái ngoài phạm vi: {(isOutOfScope ? "CÓ" : "KHÔNG")}
            
            DỮ LIỆU ĐÃ TRUY VẤN ĐƯỢC:
            {workingContext}

            NHIỆM VỤ:
            1. Nếu `isOutOfScope` là CÓ: Hãy từ chối trả lời một cách lịch sự, giải thích rằng bạn chỉ hỗ trợ các dữ liệu liên quan đến hệ thống quản lý và gợi ý người dùng đặt câu hỏi liên quan.
            2. Nếu KHÔNG CÓ DỮ LIỆU nào được tìm thấy và không bị OutOfScope: Báo rằng không tìm thấy thông tin phù hợp trong hệ thống cho yêu cầu này.
            3. Nếu CÓ DỮ LIỆU: Trình bày câu trả lời chuyên nghiệp bằng Markdown.
               - Sử dụng ### 💠 Tổng quan: Câu trả lời ngắn gọn, trực diện, nêu rõ ngày tháng.
               - Sử dụng ### 📋 Chi tiết: Dùng bảng Markdown (tiếng Việt) nếu có danh sách.
               - Định dạng số: Phân cách hàng nghìn (ví dụ 1.234.567).
               - Đưa ra 3 câu hỏi gợi ý liên quan.

            YÊU CẦU JSON TRẢ VỀ:
            {{
                ""answer"": ""Nội dung Markdown"",
                ""suggestions"": [""gợi ý 1"", ""gợi ý 2"", ""gợi ý 3""],
                ""excelData"": [],
                ""columnMapping"": {{}}
            }}";

        var rawResponse = await _aiClient.GenerateContentAsync(finalPrompt, ct);
        string finalText = rawResponse;
        List<string> suggestions = new();
        string rawDataForExport = lastStepJson; 

        try {
            var jsonString = rawResponse.Replace("```json", "").Replace("```", "").Trim();
            var result = JsonSerializer.Deserialize<JsonElement>(jsonString);
            
            finalText = result.GetProperty("answer").GetString() ?? rawResponse;
            
            if (result.TryGetProperty("suggestions", out var sugProp)) {
                suggestions = sugProp.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList();
            }

            // ƯU TIÊN 1: Nếu AI cung cấp excelData (thường cho bảng tóm tắt/KPI)
            if (result.TryGetProperty("excelData", out var excelProp) && excelProp.ValueKind == JsonValueKind.Array && excelProp.GetArrayLength() > 0) {
                rawDataForExport = JsonSerializer.Serialize(excelProp, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            }
            // ƯU TIÊN 2: Nếu AI cung cấp columnMapping (cho danh sách dài)
            else if (result.TryGetProperty("columnMapping", out var mappingProp) && lastDataTable != null) {
                var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(mappingProp.GetRawText());
                if (mapping != null && mapping.Count > 0) {
                    var friendlyRows = new List<Dictionary<string, object>>();
                    foreach (DataRow row in lastDataTable.Rows) {
                        var friendlyRow = new Dictionary<string, object>();
                        foreach (DataColumn col in lastDataTable.Columns) {
                            string friendlyHeader = mapping.ContainsKey(col.ColumnName) ? mapping[col.ColumnName] : col.ColumnName;
                            friendlyRow[friendlyHeader] = row[col] == DBNull.Value ? null! : row[col];
                        }
                        friendlyRows.Add(friendlyRow);
                    }
                    rawDataForExport = JsonSerializer.Serialize(friendlyRows, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                }
            }
        } catch { finalText = rawResponse; }

        return new ChatResponse(finalText, steps, suggestions, rawDataForExport, lastDataTable);
    }

    private string CleanSql(string sql)
    {
        return sql.Replace("```sql", "").Replace("```", "").Trim(' ', '\n', '\r', '\t', ';');
    }
}
