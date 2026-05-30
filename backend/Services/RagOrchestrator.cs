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
    private readonly IAiResponseParser _responseParser;
    private readonly ISqlPlanExecutor _planExecutor;

    public RagOrchestrator(
        VertexAiClient aiClient,
        QdrantService qdrantService,
        VertexAiOptions options,
        ISqlRuleProvider ruleProvider,
        IAiResponseParser responseParser,
        ISqlPlanExecutor planExecutor)
    {
        _aiClient = aiClient;
        _qdrantService = qdrantService;
        _options = options;
        _ruleProvider = ruleProvider;
        _responseParser = responseParser;
        _planExecutor = planExecutor;
    }

    // Quy trình điều phối RAG chính: Chuyển đổi vector, tìm kiếm schema từ Qdrant, lập kế hoạch, sinh và chạy SQL, tổng hợp câu trả lời cuối cùng.
    public async Task<ChatResponse> ProcessQueryAsync(
        string userQuery,
        string? collectionName,
        Func<RagStep, Task> onStep,
        Func<string, Task> onFinalChunk,
        CancellationToken ct,
        bool isExcelTemplate = false)
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



        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
        var currentTimeStr = now.ToString("dd/MM/yyyy HH:mm");

        // 3. Planning Phase: AI đánh giá phạm vi và lập kế hoạch
        var stepsToExecute = new List<string>();
        bool isOutOfScope = false;
        bool isAmbiguous = false;
        string clarificationMessage = string.Empty;
        var suggestedQuestions = new List<string>();
        string planningReason = string.Empty;
        string? directSql = null;

        {
            await onStep(new RagStep("Execution Planning", "AI đang phân tích câu hỏi và lập kế hoạch truy vấn..."));
            var globalRules = await _ruleProvider.GetGlobalRulesAsync();
            var planningPrompt = $@"Bạn là chuyên gia phân tích yêu cầu và lập kế hoạch truy vấn SQL.
                Thời gian hệ thống hiện tại: {currentTimeStr} (Việt Nam, UTC+7).
                Dựa trên CẤU TRÚC DATABASE được cung cấp dưới đây (được trích xuất động từ Qdrant dựa trên ngữ cảnh câu hỏi):
                {schemaInfo}

                {globalRules}

                CÂU HỎI CỦA NGƯỜI DÙNG: ""{userQuery}""

                NHIỆM VỤ CỦA BẠN:
                0. QUAN TRỌNG VỀ THỜI GIAN TRUY VẤN: Nếu người dùng hỏi về các khoảng thời gian tương đối/mơ hồ như ""gần đây"", ""gần nhất"", ""mới nhất"", ""hôm nay"", ""tuần này"", ""tháng này"":
                   - Hãy kết hợp với 'Thời gian hệ thống hiện tại' ({currentTimeStr}) để xác định khoảng thời gian cụ thể (ví dụ: ""gần đây/gần nhất"" -> tính ngược từ {currentTimeStr} khoảng 7 ngày hoặc 30 ngày tùy loại dữ liệu).
                   - Nêu rõ mốc thời gian lọc cụ thể này trong phần mô tả bước để bước SQL kế tiếp thực thi đúng.
                1. Kiểm tra xem câu hỏi có liên quan đến dữ liệu trong các bảng trên hay không. Nếu không liên quan đến database, hãy đặt `isOutOfScope: true`.
                2. Nếu câu hỏi liên quan đến database, hãy phân tích xem câu hỏi có bị mơ hồ, thiếu thông tin gom nhóm (GROUP BY) hoặc thống kê cụ thể hay không (ví dụ: 'top lỗi', 'sản lượng cao nhất'):
                   - Hãy tự động đưa ra quyết định hoặc giả định hợp lý nhất dựa trên cấu trúc CSDL thực tế được cung cấp bên trên 
                   (ví dụ: tự động chọn cột phân tích thích hợp như StyleID hoặc LineX từ các bảng liên quan làm đối tượng gom nhóm GROUP BY).
                   - Đặt `isAmbiguous: false`.
                   - Lập kế hoạch sinh câu truy vấn SQL để thực thi theo giả định mặc định đó ngay lập tức.
                   - Giải trình rõ lý do tự động quyết định và giả định bạn đã chọn trong trường ""reason"".
                   - **TUYỆT ĐỐI CẤM:** Không được sử dụng hoặc tự bịa ra bất kỳ tên bảng hay tên cột nào không xuất hiện trong cấu trúc database được cung cấp phía trên.
                3. Nếu câu hỏi hợp lệ, hãy đặt `isOutOfScope: false`, `isAmbiguous: false` và chia nhỏ câu hỏi thành các bước truy vấn SQL logic.
                   - BẮT BUỘC GỘP THÀNH 1 BƯỚC DUY NHẤT đối với các câu hỏi thống kê, so sánh, xếp hạng (Ví dụ: Top lỗi, Top chuyền, Chênh lệch sản lượng, Xếp hạng lỗi của chuyền...). 
                   TUYỆT ĐỐI CẤM chia nhỏ việc JOIN bảng, GROUP BY gom nhóm, hay dùng DENSE_RANK() xếp hạng thành các bước truy vấn riêng lẻ. Tạo 1 câu SQL duy nhất có thể giải quyết đồng thời các tác vụ này.
                   - CHỈ ĐƯỢC PHÉP CHIA LÀM NHIỀU BƯỚC (tối đa 3 bước) khi và chỉ khi: Bước sau bắt buộc phải sử dụng giá trị dữ liệu động trả về từ bước trước làm tham số điều kiện lọc 
                   (Ví dụ: Bước 1 tìm MaLenh của một mã hàng, Bước 2 dùng MaLenh đó làm tham số lọc để truy vấn sản lượng).
                4. Mỗi bước phải là một nhiệm vụ TRUY VẤN dữ liệu thực tế. TUYỆT ĐỐI KHÔNG tạo bước chỉ để kết hợp (UNION), định dạng hoặc thực hiện các phép tính so sánh/xếp hạng (RANK, CASE WHEN) 
                mà AI có thể tự suy luận từ kết quả bước trước.

                YÊU CẦU ĐỊNH DẠNG (BẮT BUỘC TRẢ VỀ JSON):
                {{
                    ""isOutOfScope"": true/false,
                    ""isAmbiguous"": true/false,
                    ""reason"": ""Giải thích lý do lập kế hoạch hoặc giả định/quyết định ngầm định được chọn khi gặp câu mơ hồ"",
                    ""steps"": [""Mô tả bước 1"", ""Mô tả bước 2""],
                    ""directSql"": ""Câu lệnh SQL Server duy nhất nếu câu hỏi chỉ cần 1 bước truy vấn duy nhất để trả về kết quả, ngược lại để trống """" ""
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

                if (planObj.TryGetProperty("directSql", out var sqlProp))
                {
                    directSql = sqlProp.GetString();
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
            // 4. Execution Phase: Delegate steps execution to SqlPlanExecutor
            if (!string.IsNullOrWhiteSpace(directSql))
            {
                try
                {
                    var execResult = await _planExecutor.ExecuteDirectSqlAsync(
                        directSql,
                        userQuery,
                        currentTimeStr,
                        onStep,
                        ct);

                    lastStepJson = execResult.LastStepJson;
                    lastDataTable = execResult.LastDataTable;
                    workingContext.Append(execResult.WorkingContext);
                    steps.AddRange(execResult.ExecutedSteps);
                }
                catch (Exception ex)
                {
                    // Fallback về cách lập kế hoạch sinh SQL truyền thống nếu directSql lỗi
                    await onStep(new RagStep("Direct SQL Execution Failure", $"Lỗi thực thi SQL trực tiếp: {ex.Message}. Đang tự động chuyển sang luồng phân tích từng bước..."));
                    
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
            }
            else
            {
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
                 * ĐẶC BIỆT QUAN TRỌNG: Khi dữ liệu truy vấn được chứa nhiều thông tin chi tiết (ví dụ: danh sách nhiều chuyền sản xuất, nhiều mã hàng, nhiều ngày...), bạn BẮT BUỘC phải tự động tính toán tổng hợp các số liệu toàn cục để người dùng nắm bắt nhanh ngay trong phần này. Thay vào đó, chỉ nhận xét ngắn gọn xu hướng, tỷ trọng % hoặc chỉ ra đối tượng nổi bật nhất/thấp nhất dưới dạng đúc rút thông tin (insight) nhanh. Các phép tính và tỷ lệ phải chính xác 100% dựa trên dữ liệu thực tế.
                 * Nếu câu hỏi ban đầu mơ hồ/thiếu thông tin gom nhóm hoặc thống kê cụ thể, hãy dựa vào phần 'Giả định/Lý do lập kế hoạch ban đầu' để thuyết minh/giải thích rõ ràng cho người dùng biết hệ thống đã tự động quyết định chọn chiều phân tích, bộ lọc hoặc gom nhóm nào để truy xuất dữ liệu.
               - Sử dụng ### 📋 Chi tiết: Dùng bảng Markdown (tiếng Việt) nếu có danh sách.
               - Định dạng số: Phân cách hàng nghìn (ví dụ: 1.234.567). Đối với số tiền, doanh thu, sản lượng, số lượng, tỷ lệ (%) hoặc các số thập phân khác: Chỉ hiển thị phần thập phân khi con số thực sự có phần lẻ (lẻ thực tế). TUYỆT ĐỐI không thêm phần thập phân rỗng (như .000, ,000 hoặc .00) cho các số nguyên hoặc số tròn. Đối với số lẻ thực tế, chỉ làm tròn tối đa 3 chữ số sau dấu phẩy và không ghi các số 0 thừa ở cuối (ví dụ: 25.71428 -> hiển thị 25,714; 27.5 -> hiển thị 27,5%).
               - Quy tắc định dạng ngày tháng: Hiển thị đầy đủ thông tin ngày, tháng, năm, giờ, phút, giây một cách rõ ràng và nhất quán theo định dạng Việt Nam (ví dụ: '13/01/2026 14:30:15' hoặc '13/01/2026' nếu không có giờ phút) trên giao diện và trong bảng kết quả.
                - Quy tắc nhất quán hiển thị tỷ lệ (BẮT BUỘC):
                    * Nếu kết quả SQL đã có cột tỷ lệ như `TiLeLoi`/`TyLeLoi`, bạn PHẢI dùng đúng giá trị gốc trong cột đó để hiển thị. TUYỆT ĐỐI KHÔNG tự nhân 100, không tự chia 100.
                    * Khi đã có `TiLeLoi`/`TyLeLoi`, TUYỆT ĐỐI KHÔNG được tính lại tỷ lệ từ `TongLoi`, `TongDat` hoặc bất kỳ cột nào khác.
                    * Nếu thêm ký hiệu `%`, vẫn phải giữ nguyên đơn vị gốc từ SQL, làm tròn tối đa 3 chữ số sau dấu phẩy và TUYỆT ĐỐI KHÔNG viết thêm các số 0 vô nghĩa ở cuối phần thập phân (ví dụ: SQL trả 0.196335 -> hiển thị 0,196%; SQL trả 19.63 -> hiển thị 19,63% chứ không viết 19,630%; SQL trả 40 -> hiển thị 40% chứ không viết 40,000%).
                    * Số liệu trong phần `### 💠 Tổng quan` và bảng `### 📋 Chi tiết` phải đồng nhất tuyệt đối với `DỮ LIỆU ĐÃ TRUY VẤN ĐƯỢC`.

            QUY TẮC QUAN TRỌNG VỀ DỮ LIỆU EXCEL:
            - Nếu dữ liệu đã truy vấn được là một danh sách dài/bảng dữ liệu gốc từ database:
              * BẮT BUỘC để `excelData` là mảng rỗng `[]`.
              * Cung cấp `columnMapping` để dịch toàn bộ các cột từ tiếng Anh sang tiếng Việt thân thiện (ví dụ: {{""MaLenh"": ""Mã Lệnh"", ""TenLenh"": ""Tên Lệnh""}}).
            - Chỉ điền dữ liệu vào `excelData` khi bạn tự tính toán/tổng hợp ra một bảng số liệu tóm tắt mới. Khi đó, `columnMapping` để trống `{{}}`.

            ĐỊNH DẠNG ĐẦU RA BẮT BUỘC:
            1. Trước tiên, hãy viết trực tiếp câu trả lời bằng Markdown tiếng Việt (không bọc trong JSON, không sử dụng thẻ code block JSON ở ngoài cùng).
            2. Ngay sau khi kết thúc câu trả lời Markdown, hãy xuống dòng và in ra chính xác chuỗi phân cách:
            ===METADATA===
            3. Sau chuỗi phân cách đó, hãy viết một đối tượng JSON chứa các thông tin bổ sung với cấu trúc sau (không dùng markdown code block cho phần JSON này):
            {{
                ""excelData"": [],
                ""columnMapping"": {{}},
                ""metadata"": {{""key"": ""value""}}
            }}";

        string separator = "===METADATA===";
        int sepLen = separator.Length;
        int sentLength = 0;
        var accumulatedText = new StringBuilder();
        var metadataBuilder = new StringBuilder();
        bool foundSeparator = false;
        int separatorIndex = -1;

        await foreach (var chunk in _aiClient.GenerateContentStreamAsync(finalPrompt, ct))
        {
            accumulatedText.Append(chunk);
            
            if (!foundSeparator)
            {
                var currentText = accumulatedText.ToString();
                separatorIndex = currentText.IndexOf(separator);
                if (separatorIndex >= 0)
                {
                    foundSeparator = true;
                    int textToSendLength = separatorIndex - sentLength;
                    if (textToSendLength > 0)
                    {
                        var textChunk = currentText.Substring(sentLength, textToSendLength);
                        await onFinalChunk(textChunk);
                    }
                    sentLength = separatorIndex + sepLen;
                    metadataBuilder.Append(currentText.Substring(sentLength));
                }
                else
                {
                    int safeLength = currentText.Length - sepLen;
                    if (safeLength > sentLength)
                    {
                        var textChunk = currentText.Substring(sentLength, safeLength - sentLength);
                        await onFinalChunk(textChunk);
                        sentLength = safeLength;
                    }
                }
            }
            else
            {
                metadataBuilder.Append(chunk);
            }
        }

        if (!foundSeparator)
        {
            var currentText = accumulatedText.ToString();
            if (currentText.Length > sentLength)
            {
                var finalChunk = currentText.Substring(sentLength);
                await onFinalChunk(finalChunk);
            }
        }

        string finalText = foundSeparator ? accumulatedText.ToString().Substring(0, separatorIndex).Trim() : accumulatedText.ToString().Trim();
        string metadataJson = metadataBuilder.ToString().Trim();
        
        List<string> suggestions = new();
        string rawDataForExport = lastStepJson; 
        Dictionary<string, string>? metadata = null;

        if (!string.IsNullOrEmpty(metadataJson))
        {
            try 
            {
                var cleanedMetaJson = metadataJson.Replace("```json", "").Replace("```", "").Trim();
                var result = JsonSerializer.Deserialize<JsonElement>(cleanedMetaJson);
                
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
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error parsing stream metadata JSON: {ex.Message}. Raw metadata was: {metadataJson}");
                // Fallback trích xuất thủ công nếu JSON bị lỗi nhẹ
                if (_responseParser.TryExtractInvalidJsonFields(metadataJson, out var extractedAnswer, out var extractedSuggestions, out var extractedExcelData, out var extractedColumnMapping))
                {
                    suggestions = extractedSuggestions;
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
                    else if (!string.IsNullOrEmpty(extractedColumnMapping) && lastDataTable != null)
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
            }
        }

        return new ChatResponse(finalText, steps, suggestions, rawDataForExport, lastDataTable, Metadata: metadata);
    }
}
