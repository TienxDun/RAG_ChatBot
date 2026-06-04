using System.Threading;

namespace Backend.Models;

public enum PerformancePhase
{
    None,
    Embedding,
    SchemaRetrieval,
    Planning,
    SqlGeneration,
    FinalGeneration
}

public static class PerformanceContext
{
    private static readonly AsyncLocal<PerformanceTracker?> _current = new();

    public static PerformanceTracker? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}

public sealed class PerformanceTracker
{
    public bool IsEnabled { get; set; }
    public PerformancePhase CurrentPhase { get; set; } = PerformancePhase.None;

    // Latencies (ms)
    public long EmbeddingMs { get; set; }
    public long SchemaRetrievalMs { get; set; }
    public long PlanningMs { get; set; }
    public long ExecutionMs { get; set; }
    public long GenerationMs { get; set; }
    public long TotalMs { get; set; }

    // Tokens
    public int PlanningPromptTokens { get; set; }
    public int PlanningCandidatesTokens { get; set; }

    public int SqlPromptTokens { get; set; }
    public int SqlCandidatesTokens { get; set; }

    public int GenerationPromptTokens { get; set; }
    public int GenerationCandidatesTokens { get; set; }

    public Dictionary<string, string> ToMetadata()
    {
        return new Dictionary<string, string>
        {
            ["performance_enabled"] = "true",
            ["embedding_ms"] = EmbeddingMs.ToString(),
            ["schema_retrieval_ms"] = SchemaRetrievalMs.ToString(),
            ["planning_ms"] = PlanningMs.ToString(),
            ["execution_ms"] = ExecutionMs.ToString(),
            ["generation_ms"] = GenerationMs.ToString(),
            ["total_ms"] = TotalMs.ToString(),
            ["planning_prompt_tokens"] = PlanningPromptTokens.ToString(),
            ["planning_candidates_tokens"] = PlanningCandidatesTokens.ToString(),
            ["sql_prompt_tokens"] = SqlPromptTokens.ToString(),
            ["sql_candidates_tokens"] = SqlCandidatesTokens.ToString(),
            ["generation_prompt_tokens"] = GenerationPromptTokens.ToString(),
            ["generation_candidates_tokens"] = GenerationCandidatesTokens.ToString(),
            ["total_prompt_tokens"] = (PlanningPromptTokens + SqlPromptTokens + GenerationPromptTokens).ToString(),
            ["total_candidates_tokens"] = (PlanningCandidatesTokens + SqlCandidatesTokens + GenerationCandidatesTokens).ToString(),
            ["total_tokens"] = (PlanningPromptTokens + SqlPromptTokens + GenerationPromptTokens + 
                                PlanningCandidatesTokens + SqlCandidatesTokens + GenerationCandidatesTokens).ToString()
        };
    }
}
