using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Backend.Services.Rag;

public interface ISqlRuleProvider
{
    Task<string> GetGlobalRulesAsync();
}

public class SqlRuleProvider : ISqlRuleProvider
{
    private string? _cachedGlobalRules;
    private DateTime _lastRulesReadTime = DateTime.MinValue;
    private readonly object _rulesLock = new();

    public async Task<string> GetGlobalRulesAsync()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "rag_schemas", "_global_rules.json");
        if (!File.Exists(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, "rag_schemas", "_global_rules.json");
        }

        if (!File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            var lastWrite = File.GetLastWriteTime(path);
            if (_cachedGlobalRules == null || lastWrite > _lastRulesReadTime)
            {
                var content = await File.ReadAllTextAsync(path, Encoding.UTF8);
                using var doc = JsonDocument.Parse(content);
                var sb = new StringBuilder();
                if (doc.RootElement.TryGetProperty("rules", out var rulesProp) && rulesProp.ValueKind == JsonValueKind.Array)
                {
                    sb.AppendLine("## QUY TẮC SQL TOÀN CỤC (GLOBAL RULES - BẮT BUỘC TUÂN THỦ):");
                    foreach (var rule in rulesProp.EnumerateArray())
                    {
                        var id = rule.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                        var severity = rule.TryGetProperty("severity", out var sevProp) ? sevProp.GetString() ?? "" : "";
                        var text = rule.TryGetProperty("rule", out var rProp) ? rProp.GetString() ?? "" : "";
                        var correct = rule.TryGetProperty("correct_example", out var corProp) ? corProp.GetString() ?? "" : "";
                        var wrong = rule.TryGetProperty("wrong_example", out var wrgProp) ? wrgProp.GetString() ?? "" : "";

                        sb.AppendLine($"- [{id}] [{severity}]: {text}");
                        if (!string.IsNullOrWhiteSpace(correct)) sb.AppendLine($"  * Ví dụ ĐÚNG: `{correct}`");
                        if (!string.IsNullOrWhiteSpace(wrong)) sb.AppendLine($"  * Ví dụ SAI: `{wrong}`");
                    }
                }
                
                lock (_rulesLock)
                {
                    _cachedGlobalRules = sb.ToString();
                    _lastRulesReadTime = lastWrite;
                }
            }
        }
        catch
        {
            return _cachedGlobalRules ?? string.Empty;
        }

        return _cachedGlobalRules ?? string.Empty;
    }
}
