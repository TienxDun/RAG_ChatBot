namespace Backend.Models;

public sealed record VertexAiOptions(
    string ApiKey,
    string ProjectId,
    string Region,
    string LlmModelId,
    string EmbeddingModelId,
    bool ExpressMode,
    string ApiUrlTemplate)
{
    public static VertexAiOptions FromEnvironment()
    {
        var apiKey = Environment.GetEnvironmentVariable("VERTEX_API_KEY");
        var projectId = Environment.GetEnvironmentVariable("VERTEX_PROJECT_ID");
        var region = Environment.GetEnvironmentVariable("VERTEX_REGION");
        var llmModelId = Environment.GetEnvironmentVariable("VERTEX_LLM_MODEL");
        var embeddingModelId = Environment.GetEnvironmentVariable("VERTEX_EMBED_MODEL");
        var expressModeStr = Environment.GetEnvironmentVariable("VERTEX_EXPRESS_MODE");
        var expressMode = expressModeStr?.ToLowerInvariant() == "true";
        var apiUrlTemplate = Environment.GetEnvironmentVariable("VERTEX_API_URL_TEMPLATE");

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(apiKey)) missing.Add("VERTEX_API_KEY");
        if (string.IsNullOrWhiteSpace(projectId)) missing.Add("VERTEX_PROJECT_ID");
        if (string.IsNullOrWhiteSpace(region)) missing.Add("VERTEX_REGION");
        if (string.IsNullOrWhiteSpace(llmModelId)) missing.Add("VERTEX_LLM_MODEL");
        if (string.IsNullOrWhiteSpace(embeddingModelId)) missing.Add("VERTEX_EMBED_MODEL");

        if (string.IsNullOrWhiteSpace(apiUrlTemplate))
        {
            // Default template if not provided
            apiUrlTemplate = expressMode
                ? "https://aiplatform.googleapis.com/v1/publishers/google/models/{modelId}:{action}"
                : "https://{region}-aiplatform.googleapis.com/v1/projects/{projectId}/locations/{region}/publishers/google/models/{modelId}:{action}";
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Missing required environment variables: {string.Join(", ", missing)}");
        }

        return new VertexAiOptions(
            apiKey!,
            projectId!,
            region!,
            llmModelId!,
            embeddingModelId!,
            expressMode,
            apiUrlTemplate);
    }
}
