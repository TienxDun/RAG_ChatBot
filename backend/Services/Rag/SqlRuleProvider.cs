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
    // Dùng SemaphoreSlim thay vì lock để hỗ trợ await bên trong critical section
    private readonly SemaphoreSlim _rulesLock = new(1, 1);

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

            // Kiểm tra nhanh ngoài lock (read-only, chấp nhận stale check nhẹ)
            if (_cachedRules != null && lastWrite <= _lastRulesReadTime)
            {
                rules = _cachedRules;
            }
            else
            {
                // Vào critical section async-safe: chỉ 1 thread được đọc file và cập nhật cache
                await _rulesLock.WaitAsync();
                try
                {
                    // Double-check sau khi acquire semaphore để tránh reload trùng
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
                        _cachedRules = tempRules;
                        _lastRulesReadTime = lastWrite;
                    }
                    rules = _cachedRules;
                }
                finally
                {
                    _rulesLock.Release();
                }
            }
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
