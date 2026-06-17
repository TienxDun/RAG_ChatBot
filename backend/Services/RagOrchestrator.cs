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
    private readonly DataSourceRegistry _registry;

    public RagOrchestrator(
        VertexAiClient aiClient,
        QdrantService qdrantService,
        VertexAiOptions options,
        ISqlRuleProvider ruleProvider,
        IAiResponseParser responseParser,
        ISqlPlanExecutor planExecutor,
        DataSourceRegistry registry)
    {
        _aiClient = aiClient;
        _qdrantService = qdrantService;
        _options = options;
        _ruleProvider = ruleProvider;
        _responseParser = responseParser;
        _planExecutor = planExecutor;
        _registry = registry;
    }

    // Quy trình điều phối RAG chính
    public async Task<ChatResponse> ProcessQueryAsync(
        string userQuery,
        string? collectionName,
        Func<RagStep, Task> onStep,
        Func<string, Task> onFinalChunk,
        CancellationToken ct,
        bool isExcelTemplate = false)
    {
        var dataSource = _registry.GetByCollection(collectionName) ?? _registry.GetDefault();
        var connectionString = _registry.GetConnectionString(dataSource);

        var steps = new List<RagStep>();
        var tracker = PerformanceContext.Current;
        var totalSw = System.Diagnostics.Stopwatch.StartNew();
        var stepSw = System.Diagnostics.Stopwatch.StartNew();
        TrackPhase(tracker, PerformancePhase.Embedding);

        // Timeout 60s toàn pipeline
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var pipelineCt = linkedCts.Token;

        // 1. Embedding
        await onStep(new RagStep("System Initialization", "Đang khởi tạo luồng xử lý và chuẩn bị kết nối tới AI Engine..."));
        var vector = await GetQueryEmbeddingAsync(userQuery, isExcelTemplate, pipelineCt);
        
        stepSw.Stop();
        TrackLatency(tracker, t => t.EmbeddingMs = stepSw.ElapsedMilliseconds);
        TrackPhase(tracker, PerformancePhase.SchemaRetrieval);
        stepSw.Restart();

        steps.Add(new RagStep("Vectorization", "Câu hỏi đã được chuyển đổi thành vector 3072 chiều."));
        await onStep(steps.Last());

        // 2. Schema Retrieval
        var schemaInfo = await RetrieveSchemaAsync(vector, dataSource.QdrantCollection);
        var step2Content = $"Tìm thấy cấu trúc database liên quan.\n\n" +
                           "**Chi tiết cấu trúc được trích xuất từ Qdrant:**\n" +
                           $"```sql\n{schemaInfo}\n```";
        steps.Add(new RagStep("Schema Retrieval", step2Content));
        await onStep(steps.Last());

        stepSw.Stop();
        TrackLatency(tracker, t => t.SchemaRetrievalMs = stepSw.ElapsedMilliseconds);
        TrackPhase(tracker, PerformancePhase.Planning);
        stepSw.Restart();

        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
        var currentTimeStr = now.ToString("dd/MM/yyyy HH:mm");

        // 3. Planning
        var planResult = await CreatePlanAsync(userQuery, schemaInfo, currentTimeStr, isExcelTemplate, dataSource.RulesFolder, onStep, pipelineCt);

        stepSw.Stop();
        TrackLatency(tracker, t => t.PlanningMs = stepSw.ElapsedMilliseconds);
        TrackPhase(tracker, PerformancePhase.SqlGeneration);
        stepSw.Restart();

        // 4. Execution
        var execResult = await ExecutePlanAsync(planResult, userQuery, currentTimeStr, schemaInfo, connectionString, onStep, pipelineCt);
        steps.AddRange(execResult.ExecutedSteps);

        stepSw.Stop();
        TrackLatency(tracker, t => t.ExecutionMs = stepSw.ElapsedMilliseconds);
        TrackPhase(tracker, PerformancePhase.FinalGeneration);
        stepSw.Restart();

        // 5. Final Generation + Metadata (song song)
        var response = await GenerateFinalResponseAsync(
            userQuery, planResult.IsOutOfScope, planResult.PlanningReason,
            execResult.WorkingContext, currentTimeStr,
            execResult.LastStepJson, execResult.LastDataTable,
            onFinalChunk, pipelineCt);

        stepSw.Stop();
        totalSw.Stop();
        TrackLatency(tracker, t => { t.GenerationMs = stepSw.ElapsedMilliseconds; t.TotalMs = totalSw.ElapsedMilliseconds; });
        TrackPhase(tracker, PerformancePhase.None);

        return new ChatResponse(
            response.FinalText, steps, null, 
            response.RawDataForExport, execResult.LastDataTable,
            Metadata: tracker != null && tracker.IsEnabled ? tracker.ToMetadata() : null);
    }

    // ==================== Private: Embedding ====================

    private async Task<IReadOnlyList<float>> GetQueryEmbeddingAsync(string userQuery, bool isExcelTemplate, CancellationToken ct)
    {
        string embeddingText = userQuery;
        if (isExcelTemplate)
        {
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
        
        if (embeddingText.Length > 2000)
        {
            embeddingText = embeddingText.Substring(0, 2000);
        }

        // Retry embedding tối đa 3 lần với exponential backoff
        IReadOnlyList<float> vector = Array.Empty<float>();
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                vector = await _aiClient.GetEmbeddingAsync(embeddingText, "RETRIEVAL_QUERY", 3072, ct);
                break;
            }
            catch when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
            }
        }
        return vector;
    }

    // ==================== Private: Schema Retrieval ====================

    private async Task<string> RetrieveSchemaAsync(IReadOnlyList<float> vector, string collectionName)
    {
        var schemaContexts = await _qdrantService.SearchSchemaAsync(vector, limit: _options.TopK, collectionName: collectionName);
        var orderedContexts = schemaContexts.OrderBy(s => s).ToList();

        var sb = new StringBuilder();
        for (int i = 0; i < orderedContexts.Count; i++)
        {
            if (i > 0) sb.AppendLine("\n---\n");
            sb.AppendLine($"**[{i + 1}/{orderedContexts.Count}]**");
            sb.AppendLine(CompressSchemaMarkdown(orderedContexts[i]));
        }
        return sb.ToString();
    }

    // ==================== Private: Planning ====================

    private sealed record PlanResult(bool IsOutOfScope, string PlanningReason, List<string> Steps, string? DirectSql, string GlobalRules);

    private async Task<PlanResult> CreatePlanAsync(
        string userQuery, string schemaInfo, string currentTimeStr,
        bool isExcelTemplate, string rulesFolder, Func<RagStep, Task> onStep, CancellationToken ct)
    {
        await onStep(new RagStep("Execution Planning", "AI đang phân tích câu hỏi và lập kế hoạch truy vấn..."));
        
        var globalRules = await _ruleProvider.GetGlobalRulesAsync(rulesFolder, userQuery, isExcelTemplate: isExcelTemplate);
        var planningPrompt = RagPromptBuilder.BuildPlanningPrompt(userQuery, schemaInfo, globalRules, currentTimeStr);
        var planResponse = await _aiClient.GenerateContentAsync(planningPrompt, ct, responseMimeType: "application/json");

        bool isOutOfScope = false;
        string planningReason = string.Empty;
        string? directSql = null;
        var stepsToExecute = new List<string>();

        try
        {
            var planObj = JsonSerializer.Deserialize<JsonElement>(planResponse.Trim());
            isOutOfScope = planObj.GetProperty("isOutOfScope").GetBoolean();
            
            if (planObj.TryGetProperty("reason", out var reasonProp))
                planningReason = reasonProp.GetString() ?? string.Empty;
            if (planObj.TryGetProperty("directSql", out var sqlProp))
                directSql = sqlProp.GetString();
            if (!isOutOfScope)
                stepsToExecute = planObj.GetProperty("steps").EnumerateArray().Select(x => x.GetString()!).ToList();
        }
        catch
        {
            stepsToExecute = new List<string> { userQuery };
        }

        return new PlanResult(isOutOfScope, planningReason, stepsToExecute, directSql, globalRules);
    }

    // ==================== Private: Execution ====================

    private sealed record ExecutionResult(string WorkingContext, string LastStepJson, DataTable? LastDataTable, List<RagStep> ExecutedSteps);

    private async Task<ExecutionResult> ExecutePlanAsync(
        PlanResult plan, string userQuery, string currentTimeStr, string schemaInfo,
        string connectionString, Func<RagStep, Task> onStep, CancellationToken ct)
    {
        if (plan.IsOutOfScope)
        {
            var step = new RagStep("Scope Guarding", "Rất tiếc, câu hỏi của bạn nằm ngoài phạm vi dữ liệu mà tôi có thể truy cập.");
            await onStep(step);
            return new ExecutionResult("", "", null, new List<RagStep> { step });
        }

        SqlExecutionResult execResult;

        if (!string.IsNullOrWhiteSpace(plan.DirectSql))
        {
            try
            {
                execResult = await _planExecutor.ExecuteDirectSqlAsync(
                    plan.DirectSql, userQuery, currentTimeStr, connectionString, onStep, ct);
            }
            catch (Exception ex)
            {
                // Fallback về lập kế hoạch sinh SQL truyền thống nếu directSql lỗi
                await onStep(new RagStep("Direct SQL Execution Failure", 
                    $"Lỗi thực thi SQL trực tiếp: {ex.Message}. Đang tự động chuyển sang luồng phân tích từng bước..."));
                
                execResult = await _planExecutor.ExecutePlanStepsAsync(
                    plan.Steps, userQuery, currentTimeStr, schemaInfo, plan.GlobalRules, connectionString, onStep, ct);
            }
        }
        else
        {
            execResult = await _planExecutor.ExecutePlanStepsAsync(
                plan.Steps, userQuery, currentTimeStr, schemaInfo, plan.GlobalRules, connectionString, onStep, ct);
        }

        return new ExecutionResult(
            execResult.WorkingContext.ToString(), 
            execResult.LastStepJson, 
            execResult.LastDataTable, 
            execResult.ExecutedSteps);
    }

    // ==================== Private: Final Generation ====================

    private sealed record FinalGenerationResult(string FinalText, string RawDataForExport);

    private async Task<FinalGenerationResult> GenerateFinalResponseAsync(
        string userQuery, bool isOutOfScope, string planningReason, string workingContext,
        string currentTimeStr, string lastStepJson, DataTable? lastDataTable,
        Func<string, Task> onFinalChunk, CancellationToken ct)
    {
        var finalPrompt = RagPromptBuilder.BuildFinalPrompt(userQuery, isOutOfScope, planningReason, workingContext, currentTimeStr);

        // Khởi tạo task sinh metadata song song với luồng stream
        var metadataTask = BuildMetadataTaskAsync(userQuery, isOutOfScope, lastDataTable, ct);

        var accumulatedText = new StringBuilder();
        await foreach (var chunk in _aiClient.GenerateContentStreamAsync(finalPrompt, ct))
        {
            accumulatedText.Append(chunk);
            await onFinalChunk(chunk);
        }

        string finalText = accumulatedText.ToString().Trim();
        string rawDataForExport = lastStepJson;

        if (metadataTask != null)
        {
            rawDataForExport = await ProcessMetadataAsync(metadataTask, rawDataForExport, lastDataTable);
        }

        return new FinalGenerationResult(finalText, rawDataForExport);
    }

    // ==================== Private: Metadata ====================

    private Task<string>? BuildMetadataTaskAsync(string userQuery, bool isOutOfScope, DataTable? lastDataTable, CancellationToken ct)
    {
        if (isOutOfScope) return null;
        if (lastDataTable == null || lastDataTable.Columns.Count == 0)
        {
            return Task.FromResult(JsonSerializer.Serialize(new { excelData = Array.Empty<object>(), columnMapping = new Dictionary<string, string>() }));
        }

        // Chỉ gửi tên cột + 2 dòng mẫu đầu tiên — đủ để LLM hiểu cấu trúc
        var columnNames = lastDataTable.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        var sampleRows = new StringBuilder();
        int sampleCount = Math.Min(2, lastDataTable.Rows.Count);
        for (int si = 0; si < sampleCount; si++)
        {
            var rowDict = new Dictionary<string, object?>();
            foreach (DataColumn col in lastDataTable.Columns)
                rowDict[col.ColumnName] = lastDataTable.Rows[si][col] == DBNull.Value ? null : lastDataTable.Rows[si][col];
            sampleRows.AppendLine(JsonSerializer.Serialize(rowDict, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));
        }
        var metadataContext = $"Tên cột: {JsonSerializer.Serialize(columnNames)}\n" +
                              $"Tổng số dòng: {lastDataTable.Rows.Count}\n" +
                              $"2 dòng mẫu:\n{sampleRows}";

        var metadataPrompt = RagPromptBuilder.BuildMetadataPrompt(userQuery, metadataContext);
        return _aiClient.GenerateContentAsync(metadataPrompt, ct, responseMimeType: "application/json");
    }

    private static async Task<string> ProcessMetadataAsync(Task<string> metadataTask, string rawDataForExport, DataTable? lastDataTable)
    {
        try
        {
            var metadataResponse = await metadataTask;
            using var metaDoc = JsonDocument.Parse(metadataResponse);
            var root = metaDoc.RootElement;

            if (root.TryGetProperty("excelData", out var excelProp) && excelProp.ValueKind == JsonValueKind.Array && excelProp.GetArrayLength() > 0)
            {
                rawDataForExport = JsonSerializer.Serialize(excelProp, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            }
            else if (root.TryGetProperty("columnMapping", out var mappingProp) && mappingProp.ValueKind == JsonValueKind.Object && lastDataTable != null)
            {
                var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(mappingProp.GetRawText());
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
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Error generating metadata JSON: {ex.Message}");
        }

        return rawDataForExport;
    }

    // ==================== Private: Utilities ====================

    private static void TrackPhase(PerformanceTracker? tracker, PerformancePhase phase)
    {
        if (tracker != null && tracker.IsEnabled)
            tracker.CurrentPhase = phase;
    }

    private static void TrackLatency(PerformanceTracker? tracker, Action<PerformanceTracker> setter)
    {
        if (tracker != null && tracker.IsEnabled)
            setter(tracker);
    }

    private static string CompressSchemaMarkdown(string schemaMd)
    {
        if (string.IsNullOrWhiteSpace(schemaMd)) return string.Empty;

        var lines = schemaMd.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var sb = new StringBuilder();
        bool inColumnsTable = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            if (trimmed.Contains("Ví dụ SAI:") || trimmed.Contains("wrong_example"))
            {
                continue;
            }

            if (trimmed.Contains("## Cấu trúc cột"))
            {
                inColumnsTable = true;
                sb.AppendLine(line);
                continue;
            }

            if (inColumnsTable)
            {
                if (trimmed.StartsWith("|"))
                {
                    if (trimmed.Contains("Tên cột") || trimmed.Contains("---") || trimmed.Contains("Vai trò"))
                    {
                        continue;
                    }

                    var parts = trimmed.Split('|');
                    if (parts.Length >= 5)
                    {
                        var colName = parts[1].Trim();
                        var colType = parts[2].Trim();
                        var colRole = parts[3].Trim();
                        var colDesc = parts[4].Trim();

                        sb.AppendLine($"- {colName} ({colType}, {colRole}): {colDesc}");
                    }
                    continue;
                }
                else if (trimmed == "" && sb.Length > 0 && sb.ToString().EndsWith("- "))
                {
                    continue;
                }
                else
                {
                    inColumnsTable = false;
                }
            }

            sb.AppendLine(line);
        }

        return sb.ToString().Trim();
    }
}
