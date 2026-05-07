using System.Data;

namespace Backend.Models;

public sealed record RagStep(string Title, string Content);

public sealed record ChatResponse(
    string Text, 
    List<RagStep>? Steps = null,
    List<string>? SuggestedQuestions = null,
    string? RawData = null,
    DataTable? Data = null
);
