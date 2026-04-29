namespace Backend.Models;

public sealed record EmbeddingRequest(string Text, string? TaskType, int? OutputDimensionality);
