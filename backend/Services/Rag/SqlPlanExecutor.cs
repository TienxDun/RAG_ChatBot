using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Backend.Models;

namespace Backend.Services.Rag;

public interface ISqlPlanExecutor
{
    Task<SqlExecutionResult> ExecutePlanStepsAsync(
        List<string> stepsToExecute,
        string userQuery,
        string currentTimeStr,
        string schemaInfo,
        Func<RagStep, Task> onStep,
        CancellationToken ct);

    Task<SqlExecutionResult> ExecuteDirectSqlAsync(
        string directSql,
        string userQuery,
        string currentTimeStr,
        Func<RagStep, Task> onStep,
        CancellationToken ct);
}

public class SqlExecutionResult
{
    public string LastStepJson { get; set; } = string.Empty;
    public string WorkingContext { get; set; } = string.Empty;
    public DataTable? LastDataTable { get; set; }
    public List<RagStep> ExecutedSteps { get; set; } = new();
}

public class SqlPlanExecutor : ISqlPlanExecutor
{
    private readonly VertexAiClient _aiClient;
    private readonly SqlService _sqlService;
    private readonly ISqlRuleProvider _ruleProvider;
    private readonly IAiResponseParser _responseParser;

    public SqlPlanExecutor(
        VertexAiClient aiClient,
        SqlService sqlService,
        ISqlRuleProvider ruleProvider,
        IAiResponseParser responseParser)
    {
        _aiClient = aiClient;
        _sqlService = sqlService;
        _ruleProvider = ruleProvider;
        _responseParser = responseParser;
    }

    public async Task<SqlExecutionResult> ExecutePlanStepsAsync(
        List<string> stepsToExecute,
        string userQuery,
        string currentTimeStr,
        string schemaInfo,
        Func<RagStep, Task> onStep,
        CancellationToken ct)
    {
        var result = new SqlExecutionResult();
        var workingContextBuilder = new StringBuilder();
        workingContextBuilder.AppendLine("KẾT QUẢ CÁC BƯỚC TRƯỚC ĐÓ:");

        var isMultiStep = stepsToExecute.Count > 1;

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
                var globalRules = await _ruleProvider.GetGlobalRulesAsync();
                var sqlPrompt = $@"Bạn là chuyên gia SQL Server cao cấp.
                    Thời gian hệ thống hiện tại: {currentTimeStr} (Việt Nam, UTC+7).
                    Cấu trúc database: {schemaInfo}
                    {workingContextBuilder}

                    NHIỆM VỤ HIỆN TẠI: {currentStepDesc}
                    {(isMultiStep ? "" : $@"CÂU HỎI GỐC: ""{userQuery}""")}

                    {globalRules}

                    QUY TẮC BỔ SUNG & ĐIỀU KIỆN TRUYỀN DỮ LIỆU:
                    0. XỬ LÝ MỐC THỜI GIAN TƯƠNG ĐỐI: Đối với các khoảng thời gian như ""gần đây"", ""gần nhất"", ""mới nhất"", ""hôm nay"", ""tuần này"", ""tháng này"", bạn BẮT BUỘC phải dựa vào 'Thời gian hệ thống hiện tại' ({currentTimeStr}) để tính toán lùi ngày tháng tương ứng trong câu SQL (ví dụ: lọc từ ({currentTimeStr} - 30 ngày) đến {currentTimeStr} nếu là 30 ngày gần nhất).
                    1. CHỈ thực hiện nhiệm vụ trong 'NHIỆM VỤ HIỆN TẠI'. 
                    {(isMultiStep ? "TUYỆT ĐỐI KHÔNG giải quyết toàn bộ yêu cầu của người dùng nếu nó đòi hỏi nhiều bước xử lý. Chỉ tập trung lấy dữ liệu trung gian cho bước này." : "")}
                    
                    2. TRUYỀN THAM SỐ GIỮA CÁC BƯỚC: BẮT BUỘC sử dụng giá trị thực tế lấy từ phần 'KẾT QUẢ CÁC BƯỚC TRƯỚC ĐÓ' bên trên (nhìn vào SampleData) và các TÊN CỘT tương ứng để làm điều kiện lọc (WHERE) cho bước này.
                       - Nếu bước trước trả về danh sách nhiều ID, hãy sử dụng toán tử IN (ví dụ: WHERE MaKhachHang IN ('KH001', 'KH002')) thay vì chỉ lọc một giá trị.

                    3. QUY TẮC ÁNH XẠ NGHIỆP VỤ THÔNG MINH (BẮT BUỘC TUÂN THỦ):
                       - Hãy đọc kỹ phần 'DANH SÁCH Ý NGHĨA & CÔNG THỨC CỘT EXCEL TỰ ĐỊNH NGHĨA BỞI NGƯỜI DÙNG' trong Câu hỏi gốc.
                       - Bạn BẮT BUỘC phải phân tích kỹ mô tả ý nghĩa/công thức của từng cột (UniqueKey) do người dùng cung cấp và đối chiếu ngữ nghĩa (semantic matching) với các cột/bảng thực tế có trong Database để tìm ra trường dữ liệu tương ứng chính xác nhất.
                       - TUYỆT ĐỐI không được gán cột SQL một cách máy móc theo tên UniqueKey kỹ thuật nếu mô tả ý nghĩa của người dùng khác biệt hoặc mâu thuẫn với tên UniqueKey đó.
                       - Ví dụ minh họa: Nếu UniqueKey là `..._SLkiemQuantity` (tên hiển thị là SL kiểm) nhưng người dùng định nghĩa ý nghĩa cột này là 'Số lượng đạt' (lượng sản phẩm đạt tiêu chuẩn), bạn phải tìm cột biểu diễn số lượng đạt thực tế trong database (như `TongDat`, `Quantity`...) để gán cho cột này, TUYỆT ĐỐI không được cộng thêm số lượng sản phẩm lỗi (`SpLoi`) vào đây.
                       - Nếu người dùng cung cấp ghi chú chứa công thức toán học (ví dụ: 'Tỉ lệ lỗi = Số lượng lỗi / (số lượng lỗi + số lượng đạt)' hoặc tương tự), bạn BẮT BUỘC phải chuyển đổi chính xác công thức đó thành biểu thức SQL tương ứng dựa trên các cột database đã ánh xạ ở trên.

                    4. Cú pháp phản hồi: Trả về mã SQL thô, không giải thích, không markdown.
                    {(string.IsNullOrEmpty(lastError) ? "" : $"\nLỖI TRƯỚC ĐÓ: {lastError}\nHãy sửa SQL.")}";

                generatedSql = await _aiClient.GenerateContentAsync(sqlPrompt, ct);
                generatedSql = _responseParser.CleanSql(generatedSql);

                try
                {
                    var dt = await _sqlService.ExecuteQueryAsDataTableAsync(generatedSql, ct);
                    result.LastDataTable = dt;

                    var rows = new List<Dictionary<string, object>>();
                    foreach (DataRow row in dt.Rows)
                    {
                        var dict = new Dictionary<string, object>();
                        foreach (DataColumn col in dt.Columns)
                        {
                            var val = row[col];
                            if (val == DBNull.Value)
                            {
                                dict[col.ColumnName] = null!;
                            }
                            else if (val is DateTime dateTimeVal)
                            {
                                dict[col.ColumnName] = dateTimeVal.TimeOfDay == TimeSpan.Zero
                                    ? dateTimeVal.ToString("dd/MM/yyyy")
                                    : dateTimeVal.ToString("dd/MM/yyyy HH:mm:ss");
                            }
                            else if (val is DateTimeOffset dateTimeOffsetVal)
                            {
                                dict[col.ColumnName] = dateTimeOffsetVal.TimeOfDay == TimeSpan.Zero
                                    ? dateTimeOffsetVal.ToString("dd/MM/yyyy")
                                    : dateTimeOffsetVal.ToString("dd/MM/yyyy HH:mm:ss");
                            }
                            else
                            {
                                dict[col.ColumnName] = val;
                            }
                        }
                        rows.Add(dict);
                    }
                    var stepJson = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

                    result.LastStepJson = stepJson;
                    workingContextBuilder.AppendLine($"\n--- [Kết quả {stepTitle}: {currentStepDesc}] ---\n{GetCompactContext(rows)}");

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
                    result.ExecutedSteps.Add(stepLog);
                    await onStep(stepLog);
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    if (attempt == stepMaxAttempts)
                    {
                        var failLog = new RagStep(stepTitle, $"Thất bại: {lastError}");
                        result.ExecutedSteps.Add(failLog);
                        await onStep(failLog);
                    }
                }
            }
        }

        result.WorkingContext = workingContextBuilder.ToString();
        return result;
    }

    public async Task<SqlExecutionResult> ExecuteDirectSqlAsync(
        string directSql,
        string userQuery,
        string currentTimeStr,
        Func<RagStep, Task> onStep,
        CancellationToken ct)
    {
        var result = new SqlExecutionResult();
        var workingContextBuilder = new StringBuilder();

        var stepTitle = "Step 1/1";
        await onStep(new RagStep(stepTitle, "Đang thực thi câu lệnh SQL trực tiếp từ kế hoạch..."));

        // Clean SQL
        directSql = _responseParser.CleanSql(directSql);

        try
        {
            var dt = await _sqlService.ExecuteQueryAsDataTableAsync(directSql, ct);
            result.LastDataTable = dt;

            var rows = new List<Dictionary<string, object>>();
            foreach (DataRow row in dt.Rows)
            {
                var dict = new Dictionary<string, object>();
                foreach (DataColumn col in dt.Columns)
                {
                    var val = row[col];
                    if (val == DBNull.Value)
                    {
                        dict[col.ColumnName] = null!;
                    }
                    else if (val is DateTime dateTimeVal)
                    {
                        dict[col.ColumnName] = dateTimeVal.TimeOfDay == TimeSpan.Zero
                            ? dateTimeVal.ToString("dd/MM/yyyy")
                            : dateTimeVal.ToString("dd/MM/yyyy HH:mm:ss");
                    }
                    else if (val is DateTimeOffset dateTimeOffsetVal)
                    {
                        dict[col.ColumnName] = dateTimeOffsetVal.TimeOfDay == TimeSpan.Zero
                            ? dateTimeOffsetVal.ToString("dd/MM/yyyy")
                            : dateTimeOffsetVal.ToString("dd/MM/yyyy HH:mm:ss");
                    }
                    else
                    {
                        dict[col.ColumnName] = val;
                    }
                }
                rows.Add(dict);
            }
            var stepJson = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            
            result.LastStepJson = stepJson;
            workingContextBuilder.AppendLine($"\n--- [Kết quả {stepTitle}: SQL trực tiếp] ---\n{GetCompactContext(rows)}");

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

            var stepLog = new RagStep(stepTitle, $"Hoàn thành: Truy vấn SQL trực tiếp\n\n```sql\n{directSql}\n```\n\nKết quả:\n```json\n{stepUiJson}\n```{truncationNotice}");
            result.ExecutedSteps.Add(stepLog);
            await onStep(stepLog);
        }
        catch (Exception ex)
        {
            var failLog = new RagStep(stepTitle, $"Thất bại khi thực thi SQL trực tiếp: {ex.Message}");
            result.ExecutedSteps.Add(failLog);
            await onStep(failLog);
            throw; // ném ngoại lệ lên để RagOrchestrator biết và fallback
        }

        result.WorkingContext = workingContextBuilder.ToString();
        return result;
    }

    private string GetCompactContext(List<Dictionary<string, object>> rows, int threshold = 50)
    {
        if (rows == null || rows.Count == 0) return "[]";

        var serializeOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        if (rows.Count <= threshold)
        {
            return JsonSerializer.Serialize(rows, serializeOptions);
        }

        var sample = rows.Take(5).ToList();

        var summary = new
        {
            TotalRows = rows.Count,
            SampleData = sample,
            WarningRules = "DỮ LIỆU ĐÃ BỊ THU GỌN! Tập dữ liệu 'SampleData' phía trên CHỈ là 5 dòng mẫu đại diện để bạn hiểu cấu trúc cột và kiểu dữ liệu. Tuyệt đối KHÔNG sử dụng tập mẫu này để tự tính toán (Min, Max, Sum, Avg, Group) hoặc tạo câu lệnh SQL giả lập bằng UNION ALL. Nếu câu hỏi yêu cầu phân tích tổng hợp trên toàn bộ dữ liệu, bạn BẮT BUỘC phải sinh câu lệnh SQL truy vấn trực tiếp từ bảng gốc trong cơ sở dữ liệu."
        };

        return JsonSerializer.Serialize(summary, serializeOptions);
    }
}
