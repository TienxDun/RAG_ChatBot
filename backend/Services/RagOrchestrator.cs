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

    private static string? _cachedGlobalRules;
    private static DateTime _lastRulesReadTime = DateTime.MinValue;
    private static readonly object _rulesLock = new();

    private static async Task<string> GetGlobalRulesAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rag_schemas", "_global_rules.json");
        if (!File.Exists(path))
        {
            path = Path.Combine(Directory.GetCurrentDirectory(), "rag_schemas", "_global_rules.json");
        }

        if (!File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            var lastWrite = File.GetLastWriteTime(path);
            if (_cachedGlobalRules == null || lastWrite > _lastRulesReadTime)
            {
                var content = await File.ReadAllTextAsync(path, Encoding.UTF8);
                using var doc = JsonDocument.Parse(content);
                var sb = new StringBuilder();
                if (doc.RootElement.TryGetProperty("rules", out var rulesProp) && rulesProp.ValueKind == JsonValueKind.Array)
                {
                    sb.AppendLine("## QUY TẮC SQL TOÀN CỤC (GLOBAL RULES - BẮT BUỘC TUÂN THỦ):");
                    foreach (var rule in rulesProp.EnumerateArray())
                    {
                        var id = rule.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                        var severity = rule.TryGetProperty("severity", out var sevProp) ? sevProp.GetString() ?? "" : "";
                        var text = rule.TryGetProperty("rule", out var rProp) ? rProp.GetString() ?? "" : "";
                        var correct = rule.TryGetProperty("correct_example", out var corProp) ? corProp.GetString() ?? "" : "";
                        var wrong = rule.TryGetProperty("wrong_example", out var wrgProp) ? wrgProp.GetString() ?? "" : "";

                        sb.AppendLine($"- [{id}] [{severity}]: {text}");
                        if (!string.IsNullOrWhiteSpace(correct)) sb.AppendLine($"  * Ví dụ ĐÚNG: `{correct}`");
                        if (!string.IsNullOrWhiteSpace(wrong)) sb.AppendLine($"  * Ví dụ SAI: `{wrong}`");
                    }
                }
                
                lock (_rulesLock)
                {
                    _cachedGlobalRules = sb.ToString();
                    _lastRulesReadTime = lastWrite;
                }
            }
        }
        catch
        {
            return _cachedGlobalRules ?? string.Empty;
        }

        return _cachedGlobalRules ?? string.Empty;
    }

    // Quy trình điều phối RAG chính: Chuyển đổi vector, tìm kiếm schema từ Qdrant, lập kế hoạch, sinh và chạy SQL, tổng hợp câu trả lời cuối cùng.
    public async Task<ChatResponse> ProcessQueryAsync(string userQuery, string? collectionName, Func<RagStep, Task> onStep, CancellationToken ct, bool enableFastPath = true, bool isExcelTemplate = false, bool enableRulesExtraction = true)
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

        // Khởi động task trích xuất các quy tắc, công thức và cảnh báo CSDL song song
        Task<string>? rulesTask = null;
        if (schemaContexts.Count > 0 && enableRulesExtraction)
        {
            var rulesPrompt = $@"Bạn là chuyên gia phân tích cấu trúc dữ liệu. 
            Dưới đây là cấu trúc CSDL được trích xuất từ hệ thống:
            {schemaInfo}

            NHIỆM VỤ:
            Hãy đọc kỹ cấu trúc trên và trích xuất/liệt kê toàn bộ các:
            1. Công thức tính toán (Doanh thu, Tỉ lệ lỗi, Sản lượng đạt...).
            2. Cảnh báo quan trọng (Tránh nhân đôi dòng, Tránh nhầm lẫn cột/bảng...).
            3. Quy tắc lọc & So sánh (Bắt buộc dùng CAST, LIKE, thêm tiền tố 'SIZE_', viết hoa/thường của tên cột...).

            Yêu cầu trình bày:
            - Trình bày ngắn gọn, rõ ràng bằng các gạch đầu dòng Markdown tiếng Việt.
            - Sử dụng các ký hiệu cảnh báo như ⚠️, 📌, ⚙️ để người dùng dễ theo dõi.";

            rulesTask = _aiClient.GenerateContentAsync(rulesPrompt, ct);
        }

        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
        var currentTimeStr = now.ToString("dd/MM/yyyy HH:mm");

        // 3. Planning Phase: AI đánh giá phạm vi và lập kế hoạch
        var stepsToExecute = new List<string>();
        bool isOutOfScope = false;
        bool isAmbiguous = false;
        string clarificationMessage = string.Empty;
        var suggestedQuestions = new List<string>();
        string planningReason = string.Empty;

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
                2. Nếu câu hỏi liên quan đến database, hãy phân tích xem câu hỏi có bị mơ hồ, thiếu thông tin gom nhóm (GROUP BY) hoặc thống kê cụ thể hay không (ví dụ: 'top lỗi', 'sản lượng cao nhất'):
                   - Bạn TUYỆT ĐỐI KHÔNG ĐƯỢC đặt `isAmbiguous: true` và không được yêu cầu người dùng làm rõ.
                   - Hãy tự động đưa ra quyết định hoặc giả định hợp lý nhất dựa trên cấu trúc CSDL thực tế được cung cấp bên trên (ví dụ: tự động chọn cột phân tích thích hợp như StyleID hoặc LineX từ các bảng liên quan làm đối tượng gom nhóm GROUP BY).
                   - Đặt `isAmbiguous: false` và `clarificationMessage: ""` (để trống).
                   - Lập kế hoạch sinh câu truy vấn SQL để thực thi theo giả định mặc định đó ngay lập tức.
                   - Giải trình rõ lý do tự động quyết định và giả định bạn đã chọn trong trường ""reason"".
                   - **TUYỆT ĐỐI CẤM:** Không được sử dụng hoặc tự bịa ra bất kỳ tên bảng hay tên cột nào không xuất hiện trong cấu trúc database được cung cấp phía trên.
                3. Nếu câu hỏi hợp lệ, hãy đặt `isOutOfScope: false`, `isAmbiguous: false` và chia nhỏ câu hỏi thành các bước truy vấn SQL logic.
                   - BẮT BUỘC GỘP THÀNH 1 BƯỚC DUY NHẤT đối với các câu hỏi thống kê, so sánh, xếp hạng (Ví dụ: Top lỗi, Top chuyền, Chênh lệch sản lượng, Xếp hạng lỗi của chuyền...). TUYỆT ĐỐI CẤM chia nhỏ việc JOIN bảng, GROUP BY gom nhóm, hay dùng DENSE_RANK() xếp hạng thành các bước truy vấn riêng lẻ. Một câu SQL duy nhất có thể giải quyết đồng thời các tác vụ này.
                   - CHỈ ĐƯỢC PHÉP CHIA LÀM NHIỀU BƯỚC (tối đa 3 bước) khi và chỉ khi: Bước sau bắt buộc phải sử dụng giá trị dữ liệu động trả về từ bước trước làm tham số điều kiện lọc (Ví dụ: Bước 1 tìm MaLenh của một mã hàng, Bước 2 dùng MaLenh đó làm tham số lọc để truy vấn sản lượng).
                4. Mỗi bước phải là một nhiệm vụ TRUY VẤN dữ liệu thực tế. TUYỆT ĐỐI KHÔNG tạo bước chỉ để kết hợp (UNION), định dạng hoặc thực hiện các phép tính so sánh/xếp hạng (RANK, CASE WHEN) mà AI có thể tự suy luận từ kết quả bước trước.

                YÊU CẦU ĐỊNH DẠNG (BẮT BUỘC TRẢ VỀ JSON):
                {{
                    ""isOutOfScope"": false,
                    ""isAmbiguous"": false,
                    ""clarificationMessage"": """",
                    ""suggestions"": [""Câu hỏi gợi ý chuẩn hóa 1"", ""Câu hỏi gợi ý chuẩn hóa 2"", ""Câu hỏi gợi ý chuẩn hóa 3""],
                    ""reason"": ""Giải thích lý do lập kế hoạch hoặc giả định/quyết định ngầm định được chọn khi gặp câu mơ hồ"",
                    ""steps"": [""Mô tả bước 1"", ""Mô tả bước 2""]
                }}";

            var planResponse = await _aiClient.GenerateContentAsync(planningPrompt, ct);
            var planJson = planResponse.Replace("```json", "").Replace("```", "").Trim();
            
            try {
                var planObj = JsonSerializer.Deserialize<JsonElement>(planJson);
                isOutOfScope = planObj.GetProperty("isOutOfScope").GetBoolean();
                
                if (planObj.TryGetProperty("reason", out var reasonProp))
                {
                    planningReason = reasonProp.GetString() ?? string.Empty;
                }

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

        // Await và hiển thị các quy tắc/công thức CSDL đã trích xuất
        if (rulesTask != null)
        {
            try
            {
                var rulesText = await rulesTask;
                var rulesStep = new RagStep("Database Rules & Highlights", rulesText);
                steps.Add(rulesStep);
                await onStep(rulesStep);
            }
            catch (Exception ex)
            {
                await onStep(new RagStep("Database Rules & Highlights", $"Không thể trích xuất quy tắc: {ex.Message}"));
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
                    var globalRules = await GetGlobalRulesAsync();
                    var sqlPrompt = $@"Bạn là chuyên gia SQL Server cao cấp.
                        Cấu trúc database: {schemaInfo}
                        {workingContext}

                        NHIỆM VỤ HIỆN TẠI: {currentStepDesc}
                        {(isMultiStep ? "" : $@"CÂU HỎI GỐC: ""{userQuery}""")}

                        {globalRules}

                        QUY TẮC BỔ SUNG & ĐIỀU KIỆN TRUYỀN DỮ LIỆU:
                        1. CHỈ thực hiện nhiệm vụ trong 'NHIỆM VỤ HIỆN TẠI'. 
                        {(isMultiStep ? "TUYỆT ĐỐI KHÔNG giải quyết toàn bộ yêu cầu của người dùng nếu nó đòi hỏi nhiều bước xử lý. Chỉ tập trung lấy dữ liệu trung gian cho bước này." : "")}
                        
                        2. TRUYỀN THAM SỐ GIỮA CÁC BƯỚC: BẮT BUỘC sử dụng giá trị thực tế lấy từ phần 'KẾT QUẢ CÁC BƯỚC TRƯỚC ĐÓ' bên trên (nhìn vào SampleData) và các TÊN CỘT tương ứng để làm điều kiện lọc (WHERE) cho bước này.
                           - Nếu bước trước trả về danh sách nhiều ID, hãy sử dụng toán tử IN (ví dụ: WHERE MaKhachHang IN ('KH001', 'KH002')) thay vì chỉ lọc một giá trị.

                        3. Cú pháp phản hồi: Trả về mã SQL thô, không giải thích, không markdown.
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
            Giả định/Lý do lập kế hoạch ban đầu: ""{planningReason}""
            
            DỮ LIỆU ĐÃ TRUY VẤN ĐƯỢC:
            {workingContext}

            NHIỆM VỤ & NGUYÊN TẮC BẮT BUỘC CHỐNG ẢO GIÁC (HALLUCINATION):
            1. Nếu `isOutOfScope` là CÓ: Hãy từ chối trả lời một cách lịch sự, giải thích rằng bạn chỉ hỗ trợ các dữ liệu liên quan đến hệ thống quản lý và gợi ý người dùng đặt câu hỏi liên quan.
            2. CẤM TỰ BỊA SỐ LIỆU: Mọi con số, mã hàng, tên chuyền, số lượng lỗi, năng suất trong câu trả lời cuối cùng BẮT BUỘC phải lấy trực tiếp từ phần 'DỮ LIỆU ĐÃ TRUY VẤN ĐƯỢC' ở trên. Tuyệt đối không tự bịa ra bất kỳ con số hoặc thông tin giả lập nào không xuất hiện trong kết quả truy vấn SQL thực tế.
            3. Nếu dữ liệu SQL trống hoặc không có dòng nào: Báo cáo rõ ràng cho người dùng rằng không tìm thấy thông tin phù hợp trong hệ thống cho yêu cầu này. TUYỆT ĐỐI KHÔNG tự phỏng đoán số liệu để trả lời.
            4. CẢNH BÁO NÉN DỮ LIỆU: Nếu trong dữ liệu có dòng 'WarningRules: DỮ LIỆU ĐÃ BỊ THU GỌN', bạn phải hiểu rằng danh sách hiển thị chỉ là 5 dòng mẫu. Tuyệt đối không tự đếm số dòng trong danh sách mẫu đó để đưa vào câu trả lời. Hãy sử dụng giá trị tổng số dòng 'TotalRows' hoặc các kết quả tính toán tổng hợp (SUM, COUNT) đã được tính sẵn bởi câu lệnh SQL.
            5. Trình bày câu trả lời chuyên nghiệp bằng Markdown:
               - Sử dụng ### 💠 Tổng quan: Câu trả lời ngắn gọn, trực diện, nêu rõ ngày tháng.
                 * ĐẶC BIỆT QUAN TRỌNG: Khi dữ liệu truy vấn được chứa nhiều thông tin chi tiết (ví dụ: danh sách nhiều chuyền sản xuất, nhiều mã hàng, nhiều ngày...), bạn BẮT BUỘC phải tự động tính toán tổng hợp các số liệu toàn cục để người dùng nắm bắt nhanh ngay trong phần này (ví dụ: tổng cộng dồn của tất cả các dòng, giá trị trung bình nếu có ý nghĩa). Tuy nhiên, TUYỆT ĐỐI KHÔNG liệt kê lại tên cụ thể và số liệu chi tiết của từng đối tượng y hệt như trong bảng bên dưới (tránh lặp lại thông tin thừa thãi). Thay vào đó, chỉ nhận xét ngắn gọn xu hướng, tỷ trọng % hoặc chỉ ra đối tượng nổi bật nhất/thấp nhất dưới dạng đúc rút thông tin (insight) nhanh (ví dụ: ""chuyền 109 đóng góp lớn nhất với hơn 30% tổng sản lượng"", hoặc ""lỗi Đứt chỉ chiếm tỷ trọng lớn nhất với hơn 50% tổng số lỗi""). Các phép tính và tỷ lệ phải chính xác 100% dựa trên dữ liệu thực tế.
                 * Nếu câu hỏi ban đầu mơ hồ/thiếu thông tin gom nhóm hoặc thống kê cụ thể, hãy dựa vào phần 'Giả định/Lý do lập kế hoạch ban đầu' để thuyết minh/giải thích rõ ràng cho người dùng biết hệ thống đã tự động quyết định chọn chiều phân tích, bộ lọc hoặc gom nhóm nào để truy xuất dữ liệu.
               - Sử dụng ### 📋 Chi tiết: Dùng bảng Markdown (tiếng Việt) nếu có danh sách.
               - Định dạng số: Phân cách hàng nghìn (ví dụ 1.234.567).
               - Đưa ra 3 câu hỏi gợi ý liên quan.

            QUY TẮC QUAN TRỌNG VỀ DỮ LIỆU EXCEL:
            - Nếu dữ liệu đã truy vấn được là một danh sách dài/bảng dữ liệu gốc từ database (ví dụ: danh sách lệnh sản xuất, danh sách lỗi, chi tiết kiểm QC...):
              * BẮT BUỘC để `excelData` là mảng rỗng `[]`. Tuyệt đối không tự điền vài dòng mẫu vào đây.
              * Cung cấp `columnMapping` để dịch toàn bộ các cột từ tiếng Anh sang tiếng Việt thân thiện (ví dụ: {{""MaLenh"": ""Mã Lệnh"", ""TenLenh"": ""Tên Lệnh""}}). Hệ thống sẽ tự động dùng mapping này để xuất toàn bộ danh sách gốc ra Excel.
            - Chỉ điền dữ liệu vào `excelData` khi bạn tự tính toán/tổng hợp ra một bảng số liệu tóm tắt mới (ví dụ: bảng so sánh, bảng tổng số lượng theo chuyền tự tính, bảng KPI...) mà không thể xuất trực tiếp từ database gốc được. Khi đó, `columnMapping` để trống `{{}}`.

            YÊU CẦU JSON TRẢ VỀ:
            - Định dạng đầu ra BẮT BUỘC phải là một đối tượng JSON hợp lệ như cấu trúc dưới đây.
            - Quan trọng: Hãy ESCAPE (thêm dấu gạch chéo ngược) cho tất cả các dấu nháy kép bên trong chuỗi Markdown của trường ""answer"" (ví dụ: viết là ""khối lượng công việc"" thay vì ""khối lượng công việc"") để tránh làm hỏng cấu trúc JSON.
            
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
        } 
        catch 
        { 
            // Fallback: Nếu JSON lỗi do chứa unescaped quotes hoặc sai cú pháp, thử phân tích thủ công bằng Regex
            if (TryExtractInvalidJsonFields(rawResponse, out var extractedAnswer, out var extractedSuggestions, out var extractedExcelData, out var extractedColumnMapping))
            {
                finalText = extractedAnswer;
                suggestions = extractedSuggestions;

                // Xử lý dữ liệu Excel nếu có
                if (!string.IsNullOrEmpty(extractedExcelData))
                {
                    try
                    {
                        var excelProp = JsonSerializer.Deserialize<JsonElement>(extractedExcelData);
                        if (excelProp.ValueKind == JsonValueKind.Array && excelProp.GetArrayLength() > 0)
                        {
                            rawDataForExport = JsonSerializer.Serialize(excelProp, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                        }
                    }
                    catch { }
                }

                // Xử lý columnMapping nếu có
                if (string.IsNullOrEmpty(extractedExcelData) && !string.IsNullOrEmpty(extractedColumnMapping) && lastDataTable != null)
                {
                    try
                    {
                        var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(extractedColumnMapping);
                        if (mapping != null && mapping.Count > 0)
                        {
                            var friendlyRows = new List<Dictionary<string, object>>();
                            foreach (DataRow row in lastDataTable.Rows)
                            {
                                var friendlyRow = new Dictionary<string, object>();
                                foreach (DataColumn col in lastDataTable.Columns)
                                {
                                    string friendlyHeader = mapping.ContainsKey(col.ColumnName) ? mapping[col.ColumnName] : col.ColumnName;
                                    friendlyRow[friendlyHeader] = row[col] == DBNull.Value ? null! : row[col];
                                }
                                friendlyRows.Add(friendlyRow);
                            }
                            rawDataForExport = JsonSerializer.Serialize(friendlyRows, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                        }
                    }
                    catch { }
                }
            }
            else
            {
                finalText = rawResponse;
            }
        }

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

    private static string UnescapeString(string raw)
    {
        try
        {
            return System.Text.RegularExpressions.Regex.Unescape(raw);
        }
        catch
        {
            return raw.Replace("\\n", "\n")
                      .Replace("\\r", "\r")
                      .Replace("\\t", "\t")
                      .Replace("\\\"", "\"")
                      .Replace("\\\\", "\\");
        }
    }

    private static bool TryExtractInvalidJsonFields(string json, out string answer, out List<string> suggestions, out string? excelData, out string? columnMapping)
    {
        answer = "";
        suggestions = new List<string>();
        excelData = null;
        columnMapping = null;

        if (string.IsNullOrWhiteSpace(json)) return false;

        // Làm sạch tag markdown code block json nếu AI bọc nó
        var cleanJson = json.Replace("```json", "").Replace("```", "").Trim();

        try
        {
            // 1. Trích xuất answer
            // Regex này tìm từ "answer" : " đến dấu nháy kép đóng ngay trước dấu phẩy và từ khóa tiếp theo.
            var answerMatch = System.Text.RegularExpressions.Regex.Match(cleanJson, @"""answer""\s*:\s*""([\s\S]*?)""\s*,\s*""(?:suggestions|excelData|columnMapping)""");
            if (answerMatch.Success)
            {
                answer = UnescapeString(answerMatch.Groups[1].Value);
            }
            else
            {
                // Fallback trích xuất thủ công nếu regex trên không khớp
                int answerKeyIdx = cleanJson.IndexOf("\"answer\"");
                if (answerKeyIdx >= 0)
                {
                    int startQuoteIdx = cleanJson.IndexOf('"', answerKeyIdx + 8);
                    if (startQuoteIdx >= 0)
                    {
                        // Tìm vị trí của từ khóa tiếp theo
                        int nextKeyIdx = cleanJson.IndexOf("\"suggestions\"");
                        if (nextKeyIdx < 0) nextKeyIdx = cleanJson.IndexOf("\"excelData\"");
                        if (nextKeyIdx < 0) nextKeyIdx = cleanJson.IndexOf("\"columnMapping\"");

                        if (nextKeyIdx > startQuoteIdx)
                        {
                            // Tìm dấu nháy kép đóng cuối cùng ngay trước nextKeyIdx
                            int endQuoteIdx = cleanJson.LastIndexOf('"', nextKeyIdx);
                            // Lùi lại để đảm bảo đó là dấu nháy đóng chuỗi (bỏ qua khoảng trắng, dấu phẩy)
                            while (endQuoteIdx > startQuoteIdx && cleanJson[endQuoteIdx] != '"')
                            {
                                endQuoteIdx--;
                            }
                            if (endQuoteIdx > startQuoteIdx)
                            {
                                string rawAnswer = cleanJson.Substring(startQuoteIdx + 1, endQuoteIdx - startQuoteIdx - 1);
                                answer = UnescapeString(rawAnswer);
                            }
                        }
                    }
                }
            }

            // 2. Trích xuất suggestions
            var suggestionsMatch = System.Text.RegularExpressions.Regex.Match(cleanJson, @"""suggestions""\s*:\s*\[([\s\S]*?)\]");
            if (suggestionsMatch.Success)
            {
                var sugContent = suggestionsMatch.Groups[1].Value;
                var matches = System.Text.RegularExpressions.Regex.Matches(sugContent, @"""([\s\S]*?)""");
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    suggestions.Add(UnescapeString(m.Groups[1].Value));
                }
            }

            // 3. Trích xuất excelData
            var excelDataMatch = System.Text.RegularExpressions.Regex.Match(cleanJson, @"""excelData""\s*:\s*(\[[\s\S]*?\])");
            if (excelDataMatch.Success)
            {
                excelData = excelDataMatch.Groups[1].Value;
            }

            // 4. Trích xuất columnMapping
            var columnMappingMatch = System.Text.RegularExpressions.Regex.Match(cleanJson, @"""columnMapping""\s*:\s*(\{[\s\S]*?\})");
            if (columnMappingMatch.Success)
            {
                columnMapping = columnMappingMatch.Groups[1].Value;
            }

            return !string.IsNullOrEmpty(answer);
        }
        catch
        {
            return false;
        }
    }
}
