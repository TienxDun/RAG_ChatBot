namespace Backend.Models;

public sealed record EmbeddingResponse(IReadOnlyList<float> Values);
