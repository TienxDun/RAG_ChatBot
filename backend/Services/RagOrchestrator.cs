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
        
        var step2 = new RagStep("Schema Retrieval", $"Tìm thấy {schemaContexts.Count} cấu trúc database liên quan.");
        steps.Add(step2);
        await onStep(step2);

        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
        var currentTimeStr = now.ToString("dd/MM/yyyy HH:mm");

        // 3. Planning Phase: AI decides the steps
        await onStep(new RagStep("Execution Planning", "AI đang lập kế hoạch truy vấn dữ liệu theo từng bước..."));
        var planningPrompt = $@"Bạn là chuyên gia lập kế hoạch truy vấn SQL.
            Dựa trên CẤU TRÚC DATABASE:
            {schemaInfo}

            CÂU HỎI: ""{userQuery}""

            NHIỆM VỤ: Chia nhỏ câu hỏi trên thành các bước truy vấn SQL logic (tối đa 3 bước). 
            Mỗi bước phải là một nhiệm vụ ĐƠN LẺ và CỤ THỂ. Không được gộp nhiều nhiệm vụ vào một bước.
            Ví dụ:
            Bước 1: Truy vấn bảng A để tìm ID.
            Bước 2: Dùng ID đó truy vấn bảng B để lấy số liệu.

            YÊU CẦU ĐỊNH DẠNG: Trả về JSON duy nhất:
            {{
                ""steps"": [""mô tả bước 1"", ""mô tả bước 2""]
            }}";

        var planResponse = await _aiClient.GenerateContentAsync(planningPrompt, ct);
        var planJson = planResponse.Replace("```json", "").Replace("```", "").Trim();
        var stepsToExecute = new List<string> { userQuery }; // Default if plan fails
        try {
            var planObj = JsonSerializer.Deserialize<JsonElement>(planJson);
            stepsToExecute = planObj.GetProperty("steps").EnumerateArray().Select(x => x.GetString()!).ToList();
        } catch { /* Fallback to single step if JSON is invalid */ }

        // 4. Execution Phase: Loop through planned steps
        var workingContext = new StringBuilder();
        workingContext.AppendLine("KẾT QUẢ CÁC BƯỚC TRƯỚC ĐÓ:");
        
        string lastStepJson = string.Empty;
        DataTable? lastDataTable = null;

        for (int i = 0; i < stepsToExecute.Count; i++)
        {
            var currentStepDesc = stepsToExecute[i];
            var stepTitle = $"Step {i + 1}/{stepsToExecute.Count}";
            await onStep(new RagStep(stepTitle, $"Đang thực hiện: {currentStepDesc}"));

            string generatedSql = string.Empty;
            string lastError = string.Empty;
            int stepMaxAttempts = 2; // 2 attempts per step

            for (int attempt = 1; attempt <= stepMaxAttempts; attempt++)
            {
                var sqlPrompt = $@"Bạn là chuyên gia SQL Server.
                    Cấu trúc database: {schemaInfo}
                    {workingContext}

                    NHIỆM VỤ HIỆN TẠI: {currentStepDesc}
                    CÂU HỎI GỐC CỦA NGƯỜI DÙNG: ""{userQuery}""

                    YÊU CẦU NGHIÊM NGẶT:
                    1. 🎯 CHỈ thực hiện nhiệm vụ trong 'NHIỆM VỤ HIỆN TẠI'. TUYỆT ĐỐI KHÔNG viết SQL để trả lời toàn bộ câu hỏi gốc nếu nhiệm vụ hiện tại chỉ là một phần.
                    2. 🔗 Sử dụng giá trị thực tế từ 'KẾT QUẢ CÁC BƯỚC TRƯỚC ĐÓ' để điền vào điều kiện WHERE (ví dụ: WHERE Id_cd = 123).
                    3. Trả về mã SQL thô, không markdown, không giải thích.
                    {(string.IsNullOrEmpty(lastError) ? "" : $"\nLỖI LẦN TRƯỚC: {lastError}\nSửa lại SQL dựa trên lỗi này.")}";

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
                    
                    lastStepJson = stepJson; // Lưu lại JSON sạch của bước này
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
                        var failLog = new RagStep(stepTitle, $"Thất bại sau {stepMaxAttempts} lần thử: {lastError}");
                        steps.Add(failLog);
                        await onStep(failLog);
                    }
                }
            }
        }

        // 5. Final Generation
        var finalPrompt = $@"Bạn là trợ lý phân tích dữ liệu chuyên nghiệp.
            Thời gian hệ thống (UTC+7): {currentTimeStr}
            Câu hỏi: ""{userQuery}""
            
            DỮ LIỆU TỔNG HỢP TỪ CÁC BƯỚC TRUY VẤN:
            {workingContext}

            NHIỆM VỤ: Trình bày câu trả lời cực kỳ CHUYÊN NGHIỆP và ĐẸP MẮT theo định dạng Markdown với cấu trúc sau:

            ### 💠 Tổng quan kết quả
            - [Câu trả lời trực tiếp. **BẮT BUỘC nêu rõ mốc thời gian, ngày tháng năm** liên quan đến dữ liệu này dựa trên thời gian hệ thống hoặc dữ liệu truy vấn được]. 
            - In đậm các con số quan trọng. Ngắt dòng hợp lý để dễ đọc.

            ### 📋 Bảng dữ liệu chi tiết
            - [Nếu có danh sách dữ liệu, BẮT BUỘC sử dụng bảng Markdown với các cột tiêu đề tiếng Việt].
            - [Nếu chỉ có 1 vài con số, hãy trình bày dạng danh sách bullet points thay vì bảng].
            - QUAN TRỌNG: Format số có dấu phẩy ngăn cách hàng nghìn (ví dụ: 67,800).

            ### ⚡ Phân tích logic
            - [Giải thích ngắn gọn logic hoặc công thức tính toán đã sử dụng để đưa ra kết quả].

            YÊU CẦU CHO EXPORT EXCEL:
            - Trường hợp A (Bảng tóm tắt/KPI/Ít dữ liệu): Hãy tạo mảng 'excelData' chứa nội dung của bảng trên UI.
            - Trường hợp B (Danh sách dữ liệu rất dài): Hãy trả về 'columnMapping' để ánh xạ tên cột thô sang tiếng Việt.

            Trả về JSON:
            {{
                ""answer"": ""markdown content"",
                ""suggestions"": [""q1"", ""q2"", ""q3""],
                ""excelData"": [ {{ ""Chỉ số"": ""Tổng lỗi"", ""Giá trị"": 10 }}, ... ],
                ""columnMapping"": {{ ""raw_col"": ""Tiêu đề tiếng Việt"" }}
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
