using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Backend.Services.Rag;

public interface ISqlRuleProvider
{
    Task<string> GetGlobalRulesAsync(string? userQuery = null, bool isExcelTemplate = false);
}

public class SqlRuleProvider : ISqlRuleProvider
{
    private sealed class RuleItem
    {
        public string Id { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Rule { get; set; } = string.Empty;
    }

    private List<RuleItem>? _cachedRules;
    private DateTime _lastRulesReadTime = DateTime.MinValue;
    private readonly object _rulesLock = new();

    public async Task<string> GetGlobalRulesAsync(string? userQuery = null, bool isExcelTemplate = false)
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

        List<RuleItem> rules;
        try
        {
            var lastWrite = File.GetLastWriteTime(path);
            if (_cachedRules == null || lastWrite > _lastRulesReadTime)
            {
                var content = await File.ReadAllTextAsync(path, Encoding.UTF8);
                using var doc = JsonDocument.Parse(content);
                var tempRules = new List<RuleItem>();
                if (doc.RootElement.TryGetProperty("rules", out var rulesProp) && rulesProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var rule in rulesProp.EnumerateArray())
                    {
                        tempRules.Add(new RuleItem
                        {
                            Id = rule.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "",
                            Severity = rule.TryGetProperty("severity", out var sevProp) ? sevProp.GetString() ?? "" : "",
                            Rule = rule.TryGetProperty("rule", out var rProp) ? rProp.GetString() ?? "" : ""
                        });
                    }
                }
                
                lock (_rulesLock)
                {
                    _cachedRules = tempRules;
                    _lastRulesReadTime = lastWrite;
                }
            }
            rules = _cachedRules;
        }
        catch
        {
            rules = _cachedRules ?? new List<RuleItem>();
        }

        if (rules == null || rules.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("## QUY TẮC SQL TOÀN CỤC (GLOBAL RULES - BẮT BUỘC TUÂN THỦ):");

        foreach (var rule in rules)
        {
            // Chỉ loại bỏ các luật Excel khi không phải template Excel
            if (!isExcelTemplate && (rule.Id == "G013" || rule.Id == "G021" || rule.Id == "G022"))
                continue;

            sb.AppendLine($"- [{rule.Id}] [{rule.Severity}]: {rule.Rule}");
        }

        return sb.ToString();
    }

}
