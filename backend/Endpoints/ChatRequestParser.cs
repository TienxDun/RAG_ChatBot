using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Backend.Models;
using Microsoft.AspNetCore.Http;

namespace Backend.Endpoints;

public static class ChatRequestParser
{
    public static async Task<ChatRequestParameters> ParseAsync(HttpContext context, CancellationToken ct)
    {
        var parameters = new ChatRequestParameters();

        if (context.Request.HasFormContentType)
        {
            var form = await context.Request.ReadFormAsync(ct);
            parameters.Message = form.TryGetValue("message", out var m) ? m.ToString() : string.Empty;
            parameters.CollectionName = form.TryGetValue("collectionName", out var c) ? c.ToString() : null;
            parameters.File = form.Files.FirstOrDefault();
        }
        else if (context.Request.HasJsonContentType())
        {
            var json = await context.Request.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            parameters.Message = json.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
            parameters.CollectionName = json.TryGetProperty("collectionName", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        }

        return parameters;
    }
}
