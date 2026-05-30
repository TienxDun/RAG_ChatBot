using Microsoft.AspNetCore.Http;

namespace Backend.Models;

public sealed class ChatRequestParameters
{
    public string Message { get; set; } = string.Empty;
    public string? CollectionName { get; set; }
    public IFormFile? File { get; set; }
}
