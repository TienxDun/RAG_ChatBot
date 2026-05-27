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
            
            if (form.TryGetValue("fastPath", out var fpStr) && bool.TryParse(fpStr, out var fpVal))
            {
                parameters.FastPathEnabled = fpVal;
            }
            
            if (form.TryGetValue("rulesEnabled", out var reStr) && bool.TryParse(reStr, out var reVal))
            {
                parameters.RulesEnabled = reVal;
            }
        }
        else if (context.Request.HasJsonContentType())
        {
            var json = await context.Request.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            parameters.Message = json.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            parameters.CollectionName = json.TryGetProperty("collectionName", out var c) ? c.GetString() : null;
            
            if (json.TryGetProperty("fastPath", out var fpProp) && (fpProp.ValueKind == JsonValueKind.True || fpProp.ValueKind == JsonValueKind.False))
            {
                parameters.FastPathEnabled = fpProp.GetBoolean();
            }
            
            if (json.TryGetProperty("rulesEnabled", out var reProp) && (reProp.ValueKind == JsonValueKind.True || reProp.ValueKind == JsonValueKind.False))
            {
                parameters.RulesEnabled = reProp.GetBoolean();
            }
        }

        return parameters;
    }
}
