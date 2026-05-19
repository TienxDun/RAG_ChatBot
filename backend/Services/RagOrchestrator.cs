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

    // Khởi tạo RagOrchestrator và tiêm các dịch vụ phụ thuộc cần thiết cho luồng xử lý RAG.
    public RagOrchestrator(VertexAiClient aiClient, QdrantService qdrantService, SqlService sqlService, VertexAiOptions options)
    {
        _aiClient = aiClient;
        _qdrantService = qdrantService;
        _sqlService = sqlService;
        _options = options;
    }

    // Quy trình điều phối RAG chính: Chuyển đổi vector, tìm kiếm schema từ Qdrant, lập kế hoạch, sinh và chạy SQL, tổng hợp câu trả lời cuối cùng.
    public async Task<ChatResponse> ProcessQueryAsync(string userQuery, string? collectionName, Func<RagStep, Task> onStep, CancellationToken ct, bool enableFastPath = true, bool isExcelTemplate = false)
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
        
        var step2Content = $"Tìm thấy {schemaContexts.Count} cấu trúc database liên quan.\n\n" +
                           "**Chi tiết cấu trúc được trích xuất từ Qdrant:**\n" +
                           $"```sql\n{schemaInfo}\n```";
        var step2 = new RagStep("Schema Retrieval", step2Content);
        steps.Add(step2);
        await onStep(step2);

        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
        var currentTimeStr = now.ToString("dd/MM/yyyy HH:mm");

        // 3. Planning Phase: AI đánh giá phạm vi và lập kế hoạch
        var stepsToExecute = new List<string>();
        bool isOutOfScope = false;
        bool isAmbiguous = false;
        string clarificationMessage = string.Empty;
        var suggestedQuestions = new List<string>();

        if (enableFastPath && IsSimpleQuery(userQuery))
        {
            await onStep(new RagStep("Execution Planning", "Fast-path: Phát hiện câu hỏi đơn giản, tối ưu hóa bỏ qua bước AI Planning để tăng tốc phản hồi..."));
            stepsToExecute = new List<string> { userQuery };
        }
        else
        {
            await onStep(new RagStep("Execution Planning", "AI đang phân tích câu hỏi và lập kế hoạch truy vấn..."));
            var planningPrompt = $@"Bạn là chuyên gia phân tích yêu cầu và lập kế hoạch truy vấn SQL.
                Dựa trên CẤU TRÚC DATABASE được cung cấp dưới đây (được trích xuất động từ Qdrant dựa trên ngữ cảnh câu hỏi):
                {schemaInfo}

                CÂU HỎI CỦA NGƯỜI DÙNG: ""{userQuery}""

                NHIỆM VỤ CỦA BẠN:
                1. Kiểm tra xem câu hỏi có liên quan đến dữ liệu trong các bảng trên hay không. Nếu không liên quan đến database, hãy đặt `isOutOfScope: true`.
                2. Nếu câu hỏi liên quan đến database, hãy phân tích xem câu hỏi có bị mơ hồ, thiếu thông tin gom nhóm (GROUP BY) hoặc thống kê cụ thể hay không:
                   - Một câu hỏi bị MƠ HỒ khi yêu cầu thống kê cực trị (top, cao nhất, thấp nhất, nhiều nhất) hoặc tổng hợp chung chung nhưng không chỉ rõ đối tượng phân tích cụ thể (ví dụ: 'top lỗi', 'sản lượng cao nhất').
                   - **NGOẠI LỆ CHO BÁO CÁO EXCEL:** {(isExcelTemplate ? "Đây là yêu cầu xuất báo cáo Excel (chứa phần 'YÊU CẦU ĐẶC BIỆT CHO BÁO CÁO EXCEL'). Bạn TUYỆT ĐỐI KHÔNG ĐƯỢC đặt `isAmbiguous: true` dù câu hỏi có phạm vi rộng hay thiếu thông tin chi tiết (như Chuyền, Mã hàng, Ngày tháng). Hãy đặt `isAmbiguous: false` và lập kế hoạch sinh câu truy vấn SQL để lấy toàn bộ dữ liệu cần thiết điền vào các cột Excel." : "")}
                   - **BẮT BUỘC ĐỐI CHIẾU SCHEMA THỰC TẾ:** Nếu phát hiện mơ hồ, hãy đặt `isAmbiguous: true`. Bạn BẮT BUỘC phải đọc kỹ danh sách bảng và cột thực tế trong CẤU TRÚC DATABASE được cung cấp bên trên để:
                     a. Xác định xem những bảng nào có liên quan đến câu hỏi (ví dụ: bảng QTY_MAHANG_NGAYKIEM chứa thông tin lỗi sản phẩm, bảng ERP_LENHSX chứa thông tin lệnh sản xuất).
                     b. Lựa chọn các cột thực tế thích hợp làm đối tượng gom nhóm từ các bảng đó (ví dụ: cột StyleID - Mã hàng, cột LineX - Chuyền sản xuất trong bảng QTY_MAHANG_NGAYKIEM).
                     c. Soạn thảo `clarificationMessage` bằng tiếng Việt giải thích rõ ràng bạn tìm thấy bảng nào và đề xuất người dùng làm rõ xem muốn thống kê theo cột cụ thể nào của bảng đó.
                     d. Tạo ra 3 gợi ý trong mảng `suggestions` sử dụng chính xác các câu hỏi chuẩn hóa chứa tên bảng và tên cột thực tế.
                   - **TUYỆT ĐỐI CẤM:** Không được sử dụng hoặc tự bịa ra bất kỳ tên bảng hay tên cột nào không xuất hiện trong cấu trúc database được cung cấp phía trên.
                3. Nếu câu hỏi HỢP LỆ và RÕ RÀNG, hãy đặt `isOutOfScope: false`, `isAmbiguous: false` và chia nhỏ câu hỏi thành các bước truy vấn SQL logic.
                   - Với câu hỏi đơn giản hoặc câu hỏi SO SÁNH/THỐNG KÊ (nhiều ngày, nhiều mã hàng): ƯU TIÊN thực hiện trong 1 bước duy nhất bằng cách sử dụng các toán tử IN, BETWEEN hoặc GROUP BY. 
                   Nếu một bước đã lấy đủ dữ liệu cho các đối tượng cần so sánh, TUYỆT ĐỐI KHÔNG tạo thêm bước truy vấn để tính toán lại.
                   - Với câu hỏi phức tạp (cần lấy kết quả bước này làm tham số cho bước sau - ví dụ tìm ID rồi mới lấy chi tiết): Chia tối đa 5 bước.
                4. Mỗi bước phải là một nhiệm vụ TRUY VẤN dữ liệu thực tế. TUYỆT ĐỐI KHÔNG tạo bước chỉ để kết hợp (UNION), định dạng hoặc thực hiện các phép tính so sánh/xếp hạng (RANK, CASE WHEN) mà AI có thể tự suy luận từ kết quả bước trước.

                YÊU CẦU ĐỊNH DẠNG (BẮT BUỘC TRẢ VỀ JSON):
                {{
                    ""isOutOfScope"": false,
                    ""isAmbiguous"": false,
                    ""clarificationMessage"": ""Thông điệp yêu cầu làm rõ động sử dụng tên bảng/cột thực tế (để trống nếu câu hỏi rõ ràng)"",
                    ""suggestions"": [""Câu hỏi gợi ý chuẩn hóa 1"", ""Câu hỏi gợi ý chuẩn hóa 2"", ""Câu hỏi gợi ý chuẩn hóa 3""],
                    ""reason"": ""Lý do tại sao câu hỏi này nằm trong/ngoài phạm vi hoặc mơ hồ"",
                    ""steps"": [""Mô tả bước 1"", ""Mô tả bước 2""]
                }}";

            var planResponse = await _aiClient.GenerateContentAsync(planningPrompt, ct);
            var planJson = planResponse.Replace("```json", "").Replace("```", "").Trim();

            try {
                var planObj = JsonSerializer.Deserialize<JsonElement>(planJson);
                isOutOfScope = planObj.GetProperty("isOutOfScope").GetBoolean();
                
                if (planObj.TryGetProperty("isAmbiguous", out var ambProp))
                {
                    isAmbiguous = ambProp.GetBoolean();
                }

                if (isAmbiguous)
                {
                    if (isExcelTemplate)
                    {
                        isAmbiguous = false;
                        stepsToExecute = new List<string> { userQuery };
                    }
                    else
                    {
                        clarificationMessage = planObj.GetProperty("clarificationMessage").GetString() ?? string.Empty;
                        if (planObj.TryGetProperty("suggestions", out var sugProp) && sugProp.ValueKind == JsonValueKind.Array)
                        {
                            suggestedQuestions = sugProp.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList();
                        }
                    }
                }
                else if (!isOutOfScope)
                {
                    stepsToExecute = planObj.GetProperty("steps").EnumerateArray().Select(x => x.GetString()!).ToList();
                }
            } catch { 
                // Fallback nếu JSON lỗi: Giả định là hợp lệ và chạy 1 bước với câu hỏi gốc
                stepsToExecute = new List<string> { userQuery }; 
            }
        }

        // Nếu ngoài phạm vi hoặc mơ hồ, bỏ qua bước thực thi SQL
        var workingContext = new StringBuilder();
        string lastStepJson = string.Empty;
        DataTable? lastDataTable = null;

        if (isAmbiguous)
        {
            var clarificationStep = new RagStep("Clarification Requested", clarificationMessage);
            steps.Add(clarificationStep);
            await onStep(clarificationStep);

            return new ChatResponse(
                Text: clarificationMessage,
                Steps: steps,
                SuggestedQuestions: suggestedQuestions,
                RawData: string.Empty,
                RawDataTable: null,
                IsAmbiguous: true
            );
        }

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
                int stepMaxAttempts = 3;

                for (int attempt = 1; attempt <= stepMaxAttempts; attempt++)
                {
                    var isMultiStep = stepsToExecute.Count > 1;
                    var sqlPrompt = $@"Bạn là chuyên gia SQL Server cao cấp.
                        Cấu trúc database: {schemaInfo}
                        {workingContext}

                        NHIỆM VỤ HIỆN TẠI: {currentStepDesc}
                        {(isMultiStep ? "" : $@"CÂU HỎI GỐC: ""{userQuery}""")}

                        QUY TẮC VIẾT SQL:
                        0. BẮT BUỘC TUÂN THỦ METADATA: 
                           - Đọc cực kỳ kỹ phần mô tả (Description) của từng bảng và từng cột trong cấu trúc database được cung cấp. Tuyệt đối tuân thủ mọi chỉ dẫn, công thức tính toán và đặc biệt là các 'Cảnh báo', 'Lưu ý' hoặc 'Quy tắc' được viết trong đó (Ví dụ: nếu mô tả cột GhiChu ghi cấm dùng để gom nhóm/GROUP BY khi tìm top lỗi thì TUYỆT ĐỐI KHÔNG được sử dụng).
                           - BẮT BUỘC ĐỐI CHIẾU CHÍNH TẢ: Đối chiếu chính xác từng ký tự viết HOA/thường và sự khác biệt ký tự của tên cột đối với từng bảng cụ thể đang truy vấn (ví dụ: StyleID có chữ 'D' viết hoa trong QTY_MAHANG_NGAYKIEM, StyleId có chữ 'd' viết thường trong SEW_CoefficientSize, và StypeId có chữ 'y' và không có chữ 'l' trong SEW_CoefficientStyle). Tuyệt đối không được viết sai lệch chính tả tên cột của bảng đó để tránh lỗi cú pháp SQL.

                        1. CHỈ thực hiện nhiệm vụ trong 'NHIỆM VỤ HIỆN TẠI'. 
                        {(isMultiStep ? "TUYỆT ĐỐI KHÔNG giải quyết toàn bộ yêu cầu của người dùng nếu nó đòi hỏi nhiều bước xử lý. Chỉ tập trung lấy dữ liệu trung gian cho bước này." : "")}
                        
                        2. TRUYỀN THAM SỐ GIỮA CÁC BƯỚC: BẮT BUỘC sử dụng giá trị thực tế lấy từ phần 'KẾT QUẢ CÁC BƯỚC TRƯỚC ĐÓ' bên trên (nhìn vào SampleData) và các TÊN CỘT tương ứng để làm điều kiện lọc (WHERE) cho bước này.
                           - Nếu bước trước trả về danh sách nhiều ID, hãy sử dụng toán tử IN (ví dụ: WHERE MaKhachHang IN ('KH001', 'KH002')) thay vì chỉ lọc một giá trị.
 
                        3. TRÁNH NHẦM LẪN SCHEMA: Phân biệt rõ ràng giữa cột ID liên kết (ví dụ: SizeId, StyleId, BrandId) và cột hiển thị tên (ví dụ: Size/SizeName, Style/StyleName). Tuyệt đối không dùng chuỗi ký tự (Text) để so sánh trực tiếp với cột ID dạng số và ngược lại.
 
                        4. ĐỊNH DẠNG NGÀY THÁNG: Khi so sánh ngày tháng trong SQL Server, luôn sử dụng định dạng chuẩn ISO 'YYYY-MM-DD' hoặc 'YYYY-MM-DD HH:mm:ss'. Nếu cần lấy ngày hiện tại của hệ thống để tính toán, hãy sử dụng hàm GETDATE() thay vì hardcode ngày cố định.
 
                        5. TỐI ƯU HÓA TRUY VẤN: Để lấy nhiều giá trị cực trị (ví dụ cả sản lượng Cao nhất và Thấp nhất), KHÔNG NÊN dùng UNION ALL. Hãy sử dụng CTE kết hợp với Window Functions (ví dụ: `RANK() OVER(ORDER BY ... DESC)` as RankMax, `RANK() OVER(ORDER BY ... ASC)` as RankMin) để lọc kết quả trong một lần quét duy nhất.
 
                        6. XỬ LÝ ĐỒNG HẠNG: Luôn sử dụng `RANK()` hoặc `DENSE_RANK()` thay vì `TOP 1` để đảm bảo nếu có nhiều kết quả bằng nhau thì sẽ lấy được TẤT CẢ.
 
                        7. Trả về mã SQL thô, không giải thích, không markdown.
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
                        workingContext.AppendLine($"\n--- [Kết quả {stepTitle}: {currentStepDesc}] ---\n{GetCompactContext(stepJson)}");

                        // Logic rút gọn hiển thị kết quả SQL trên UI để tối ưu hiệu năng
                        const int maxRowsForUi = 10;
                        string stepUiJson;
                        string truncationNotice = string.Empty;

                        if (rows.Count > maxRowsForUi)
                        {
                            var truncatedRows = rows.Take(maxRowsForUi).ToList();
                            stepUiJson = JsonSerializer.Serialize(truncatedRows, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                            truncationNotice = $"\n\n*⚠️ Lưu ý: Dữ liệu quá lớn. Hệ thống đã tự động rút gọn hiển thị {maxRowsForUi} trên tổng số {rows.Count} dòng để tối ưu hiệu năng UI.*";
                        }
                        else
                        {
                            stepUiJson = stepJson;
                        }

                        var stepLog = new RagStep(stepTitle, $"Hoàn thành: {currentStepDesc}\n\n```sql\n{generatedSql}\n```\n\nKết quả:\n```json\n{stepUiJson}\n```{truncationNotice}");
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

    // Thu gọn dữ liệu JSON thô (chỉ giữ lại 5 dòng mẫu và tổng số dòng) giúp tối ưu hóa bộ nhớ ngữ cảnh và tiết kiệm token cho LLM.
    // Áp dụng ngưỡng thu gọn động (chỉ nén khi số dòng > 50) và cảnh báo nghiêm ngặt để tránh lỗi ảo giác của AI.
    private string GetCompactContext(string json, int threshold = 50)
    {
        try 
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return json;

            var array = doc.RootElement.EnumerateArray().ToList();
            if (array.Count == 0) return "[]";

            // ĐỀ XUẤT 1: Ngưỡng thu gọn động - Nếu dữ liệu nhỏ hơn hoặc bằng threshold thì giữ nguyên toàn bộ
            if (array.Count <= threshold) return json;

            // Lấy tối đa 5 dòng mẫu để AI hiểu định dạng dữ liệu
            var sample = array.Take(5).ToList();
            
            // ĐỀ XUẤT 2: Cảnh báo nghiêm ngặt ép AI dùng SQL để tính toán trên database
            var summary = new {
                TotalRows = array.Count,
                SampleData = sample,
                WarningRules = "DỮ LIỆU ĐÃ BỊ THU GỌN! Tập dữ liệu 'SampleData' phía trên CHỈ là 5 dòng mẫu đại diện để bạn hiểu cấu trúc cột và kiểu dữ liệu. Tuyệt đối KHÔNG sử dụng tập mẫu này để tự tính toán (Min, Max, Sum, Avg, Group) hoặc tạo câu lệnh SQL giả lập bằng UNION ALL. Nếu câu hỏi yêu cầu phân tích tổng hợp trên toàn bộ dữ liệu, bạn BẮT BUỘC phải sinh câu lệnh SQL truy vấn trực tiếp từ bảng gốc trong cơ sở dữ liệu."
            };

            return JsonSerializer.Serialize(summary, new JsonSerializerOptions { 
                WriteIndented = true, 
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
            });
        }
        catch { return json; }
    }

    // Làm sạch câu lệnh SQL do AI sinh ra bằng cách loại bỏ các cú pháp định dạng Markdown và ký tự dư thừa.
    private string CleanSql(string sql)
    {
        return sql.Replace("```sql", "").Replace("```", "").Trim(' ', '\n', '\r', '\t', ';');
    }

    // Xác định nhanh xem câu hỏi có thuộc dạng đơn giản (chỉ cần truy vấn trực tiếp 1 bước) hay không
    // giúp bỏ qua toàn bộ bước AI Planning kéo dài 2-3 giây.
    private bool IsSimpleQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;

        var q = query.ToLower().Trim();

        // LỚP 1: TỪ CHỐI FAST-PATH 100% NẾU CHỨA TỪ KHÓA THỐNG KÊ/PHÂN TÍCH/SO SÁNH/CỰC TRỊ
        // Bất kỳ câu hỏi nào mang tính chất tổng hợp số liệu đều phải qua AI Planning để kiểm tra tính mơ hồ
        string[] analysisKeywords = { 
            "top", "cao nhất", "thấp nhất", "nhiều nhất", "ít nhất", "tệ nhất", "tốt nhất",
            "thống kê", "so sánh", "tổng hợp", "báo cáo", "biểu đồ", "trung bình", "tỷ lệ", 
            "tỉ lệ", "phần trăm", "%", "lũy kế", "luy ke", "biến động", "xu hướng"
        };
        foreach (var keyword in analysisKeywords)
        {
            if (q.Contains(keyword)) return false; // Ép đi qua AI Planning
        }

        // LỚP 2: TỪ CHỐI FAST-PATH NẾU LIÊN QUAN ĐẾN LỖI/SẢN LƯỢNG MÀ KHÔNG CHỨA ĐỊNH DANH CỤ THỂ
        // Ví dụ: "lỗi thế nào", "tình hình lỗi", "sản lượng đạt không" -> Không có mã chuyền/mã hàng cụ thể
        bool relatesToData = q.Contains("lỗi") || q.Contains("sản lượng") || q.Contains("san luong") || q.Contains("loi");
        if (relatesToData)
        {
            // Kiểm tra xem có chứa định danh chuyền (ví dụ chuyền có dạng số: 101, 102, 105...) 
            // hoặc mã hàng (thường chứa chữ và số hoặc dấu gạch ngang '-') hay không.
            bool hasLineIdentifier = System.Text.RegularExpressions.Regex.IsMatch(q, @"\b\d{3,}\b") || q.Contains("chuyền") || q.Contains("chuyen");
            bool hasStyleIdentifier = System.Text.RegularExpressions.Regex.IsMatch(q, @"[a-zA-Z].*\d|\d.*[a-zA-Z]") || q.Contains("-");

            if (!hasLineIdentifier && !hasStyleIdentifier)
            {
                return false; // Thiếu định danh thực thể -> Ép đi qua AI Planning để làm rõ
            }
        }

        // LỚP 3: CÁC BỘ LỌC CHÀO HỎI & PHỨC TẠP
        // Lọc câu chào hỏi/chung chung
        string[] generalKeywords = { "chào", "hello", "hi", "bạn là ai", "giúp gì", "thời tiết", "cảm ơn", "thank", "tên gì" };
        foreach (var keyword in generalKeywords)
        {
            if (q == keyword || q.StartsWith(keyword + " ") || q.EndsWith(" " + keyword))
            {
                return false;
            }
        }
        
        // Lọc các từ khóa chỉ thứ tự xử lý phức tạp
        string[] complexKeywords = { "sau đó", "sau khi", "rồi mới", "kết quả của", "tổng hợp từ", "kết hợp cả", "sau đó lọc" };
        foreach (var keyword in complexKeywords)
        {
            if (q.Contains(keyword)) return false;
        }

        // Mặc định: Chỉ những câu hỏi ngắn tra cứu tĩnh (< 80 ký tự) mới được đi Fast-path
        return query.Length < 80;
    }
}
