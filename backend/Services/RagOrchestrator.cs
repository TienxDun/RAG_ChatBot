using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Backend.Models;
using Backend.Services.Rag;

namespace Backend.Services;

public sealed class RagOrchestrator
{
    private readonly VertexAiClient _aiClient;
    private readonly QdrantService _qdrantService;
    private readonly VertexAiOptions _options;
    
    private readonly ISqlRuleProvider _ruleProvider;
    private readonly IQueryClassifier _queryClassifier;
    private readonly IAiResponseParser _responseParser;
    private readonly ISqlPlanExecutor _planExecutor;

    public RagOrchestrator(
        VertexAiClient aiClient,
        QdrantService qdrantService,
        VertexAiOptions options,
        ISqlRuleProvider ruleProvider,
        IQueryClassifier queryClassifier,
        IAiResponseParser responseParser,
        ISqlPlanExecutor planExecutor)
    {
        _aiClient = aiClient;
        _qdrantService = qdrantService;
        _options = options;
        _ruleProvider = ruleProvider;
        _queryClassifier = queryClassifier;
        _responseParser = responseParser;
        _planExecutor = planExecutor;
    }

    // Quy trình điều phối RAG chính: Chuyển đổi vector, tìm kiếm schema từ Qdrant, lập kế hoạch, sinh và chạy SQL, tổng hợp câu trả lời cuối cùng.
    public async Task<ChatResponse> ProcessQueryAsync(
        string userQuery,
        string? collectionName,
        Func<RagStep, Task> onStep,
        CancellationToken ct,
        bool enableFastPath = true,
        bool isExcelTemplate = false,
        bool enableRulesExtraction = true)
    {
        var steps = new List<RagStep>();

        // 0. Khởi tạo & 1. Get Embeddings song song
        var initTask = onStep(new RagStep("System Initialization", "Đang khởi tạo luồng xử lý và chuẩn bị kết nối tới AI Engine..."));

        string embeddingText = userQuery;
        if (isExcelTemplate)
        {
            // Trích xuất phần câu hỏi gốc (nằm trước phần danh sách ý nghĩa và yêu cầu đặc biệt)
            int idxNotes = userQuery.IndexOf("DANH SÁCH Ý NGHĨA");
            int idxReq = userQuery.IndexOf("YÊU CẦU ĐẶC BIỆT");
            
            int cutIdx = -1;
            if (idxNotes >= 0 && idxReq >= 0) cutIdx = Math.Min(idxNotes, idxReq);
            else if (idxNotes >= 0) cutIdx = idxNotes;
            else if (idxReq >= 0) cutIdx = idxReq;
            
            if (cutIdx > 0)
            {
                embeddingText = userQuery.Substring(0, cutIdx).Trim();
                if (embeddingText.EndsWith('.'))
                {
                    embeddingText = embeddingText.Substring(0, embeddingText.Length - 1).Trim();
                }
            }
        }
        
        // Giới hạn ký tự an toàn dự phòng (~400-500 tokens), bảo vệ 100% khỏi giới hạn 2048 tokens của Vertex AI
        if (embeddingText.Length > 2000)
        {
            embeddingText = embeddingText.Substring(0, 2000);
        }

        var vectorTask = _aiClient.GetEmbeddingAsync(embeddingText, "RETRIEVAL_QUERY", 3072, ct);
        
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

        if (enableFastPath && _queryClassifier.IsSimpleQuery(userQuery))
        {
            await onStep(new RagStep("Execution Planning", "Fast-path: Phát hiện câu hỏi đơn giản, tối ưu hóa bỏ qua bước AI Planning để tăng tốc phản hồi..."));
            stepsToExecute = new List<string> { userQuery };
        }
        else
        {
            await onStep(new RagStep("Execution Planning", "AI đang phân tích câu hỏi và lập kế hoạch truy vấn..."));
            var planningPrompt = $@"Bạn là chuyên gia phân tích yêu cầu và lập kế hoạch truy vấn SQL.
                Thời gian hệ thống hiện tại: {currentTimeStr} (Việt Nam, UTC+7).
                Dựa trên CẤU TRÚC DATABASE được cung cấp dưới đây (được trích xuất động từ Qdrant dựa trên ngữ cảnh câu hỏi):
                {schemaInfo}

                CÂU HỎI CỦA NGƯỜI DÙNG: ""{userQuery}""

                NHIỆM VỤ CỦA BẠN:
                0. QUAN TRỌNG VỀ THỜI GIAN TRUY VẤN: Nếu người dùng hỏi về các khoảng thời gian tương đối/mơ hồ như ""gần đây"", ""gần nhất"", ""mới nhất"", ""hôm nay"", ""tuần này"", ""tháng này"":
                   - Hãy kết hợp với 'Thời gian hệ thống hiện tại' ({currentTimeStr}) để xác định khoảng thời gian cụ thể (ví dụ: ""gần đây/gần nhất"" -> tính ngược từ {currentTimeStr} khoảng 7 ngày hoặc 30 ngày tùy loại dữ liệu).
                   - Nêu rõ mốc thời gian lọc cụ thể này trong phần mô tả bước để bước SQL kế tiếp thực thi đúng.
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
            // 4. Execution Phase: Delegate steps execution to SqlPlanExecutor
            var execResult = await _planExecutor.ExecutePlanStepsAsync(
                stepsToExecute,
                userQuery,
                currentTimeStr,
                schemaInfo,
                onStep,
                ct);

            lastStepJson = execResult.LastStepJson;
            lastDataTable = execResult.LastDataTable;
            workingContext.Append(execResult.WorkingContext);
            steps.AddRange(execResult.ExecutedSteps);
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
               - Sử dụng ### 💠 Tổng quan: Câu trả lời ngắn gọn, trực diện. BẮT BUỘC phải phân tích điều kiện lọc ngày tháng (WHERE) từ các câu lệnh SQL thực tế đã chạy trong phần 'DỮ LIỆU ĐÃ TRUY VẤN ĐƯỢC' ở trên để xác định và ghi rõ khoảng thời gian dữ liệu thực tế (ví dụ: ""Dữ liệu được thống kê trong khoảng thời gian từ ngày 01/01/2025 đến ngày 31/12/2025""). Tuyệt đối KHÔNG sử dụng thời gian hệ thống hiện tại làm khoảng thời gian của dữ liệu nếu dữ liệu đó thuộc về một khoảng thời gian khác trong quá khứ.
                 * ĐẶC BIỆT QUAN TRỌNG: Khi dữ liệu truy vấn được chứa nhiều thông tin chi tiết (ví dụ: danh sách nhiều chuyền sản xuất, nhiều mã hàng, nhiều ngày...), bạn BẮT BUỘC phải tự động tính toán tổng hợp các số liệu toàn cục để người dùng nắm bắt nhanh ngay trong phần này (ví dụ: tổng cộng dồn của tất cả các dòng, giá trị trung bình nếu có ý nghĩa). Tuy nhiên, TUYỆT ĐỐI KHÔNG liệt kê lại tên cụ thể và số liệu chi tiết của từng đối tượng y hệt như trong bảng bên dưới (tránh lặp lại thông tin thừa thãi). Thay vào đó, chỉ nhận xét ngắn gọn xu hướng, tỷ trọng % hoặc chỉ ra đối tượng nổi bật nhất/thấp nhất dưới dạng đúc rút thông tin (insight) nhanh (ví dụ: ""chuyền 109 đóng góp lớn nhất với hơn 30% tổng sản lượng"", hoặc ""lỗi Đứt chỉ chiếm tỷ trọng lớn nhất với hơn 50% tổng số lỗi""). Các phép tính và tỷ lệ phải chính xác 100% dựa trên dữ liệu thực tế.
                 * Nếu câu hỏi ban đầu mơ hồ/thiếu thông tin gom nhóm hoặc thống kê cụ thể, hãy dựa vào phần 'Giả định/Lý do lập kế hoạch ban đầu' để thuyết minh/giải thích rõ ràng cho người dùng biết hệ thống đã tự động quyết định chọn chiều phân tích, bộ lọc hoặc gom nhóm nào để truy xuất dữ liệu.
               - Sử dụng ### 📋 Chi tiết: Dùng bảng Markdown (tiếng Việt) nếu có danh sách.
               - Định dạng số: Phân cách hàng nghìn (ví dụ 1.234.567).
               - Quy tắc định dạng ngày tháng: Hiển thị đầy đủ thông tin ngày, tháng, năm, giờ, phút, giây một cách rõ ràng và nhất quán theo định dạng Việt Nam (ví dụ: '13/01/2026 14:30:15' hoặc '13/01/2026' nếu không có giờ phút) trên giao diện và trong bảng kết quả.
               - Quy tắc định dạng tỉ lệ lỗi / phần trăm (%): Đối với các giá trị tỉ lệ phần trăm thu được từ kết quả SQL (như cột TyLeLoi), đây là các con số đã được nhân 100 ở câu lệnh SQL (ví dụ: kết quả SQL trả về 0.19 tức là 0.19%, 15.5 tức là 15.5%). Bạn TUYỆT ĐỐI KHÔNG ĐƯỢC nhân thêm 100 hay chia cho 100 một lần nữa khi viết câu trả lời hoặc khi tạo dữ liệu Excel. Hãy giữ nguyên giá trị số đó và chỉ định dạng hiển thị kèm ký tự % (ví dụ: 0,20% hoặc 15,50%).
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
        Dictionary<string, string>? metadata = null;

        try {
            var jsonString = rawResponse.Replace("```json", "").Replace("```", "").Trim();
            var result = JsonSerializer.Deserialize<JsonElement>(jsonString);
            
            finalText = result.GetProperty("answer").GetString() ?? rawResponse;
            
            if (result.TryGetProperty("suggestions", out var sugProp)) {
                suggestions = sugProp.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList();
            }

            // Trích xuất metadata nếu có
            if (result.TryGetProperty("metadata", out var metaProp) && metaProp.ValueKind == JsonValueKind.Object) {
                try {
                    metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metaProp.GetRawText());
                } catch { }
            }

            // ƯU TIÊN 1: Nếu AI cung cấp excelData
            if (result.TryGetProperty("excelData", out var excelProp) && excelProp.ValueKind == JsonValueKind.Array && excelProp.GetArrayLength() > 0) {
                rawDataForExport = JsonSerializer.Serialize(excelProp, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            }
            // ƯU TIÊN 2: Nếu AI cung cấp columnMapping
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
            // Fallback
            if (_responseParser.TryExtractInvalidJsonFields(rawResponse, out var extractedAnswer, out var extractedSuggestions, out var extractedExcelData, out var extractedColumnMapping))
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

        return new ChatResponse(finalText, steps, suggestions, rawDataForExport, lastDataTable, Metadata: metadata);
    }
}
