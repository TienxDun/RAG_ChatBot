using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace Backend.Services.Rag;

public interface ISqlRuleProvider
{
    Task<string> GetGlobalRulesAsync(string rulesFolder, string? userQuery = null, bool isExcelTemplate = false);
}

public class SqlRuleProvider : ISqlRuleProvider
{
    private sealed class RuleItem
    {
        public string Id { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Rule { get; set; } = string.Empty;
    }

    private sealed class CachedRulesEntry
    {
        public List<RuleItem> Rules { get; set; } = new();
        public DateTime LastReadTime { get; set; } = DateTime.MinValue;
    }

    private readonly ConcurrentDictionary<string, CachedRulesEntry> _cachedRulesMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _rulesLock = new(1, 1);

    public async Task<string> GetGlobalRulesAsync(string rulesFolder, string? userQuery = null, bool isExcelTemplate = false)
    {
        if (string.IsNullOrWhiteSpace(rulesFolder))
        {
            return string.Empty;
        }

        var path = Path.Combine(Directory.GetCurrentDirectory(), rulesFolder, "_global_rules.json");
        if (!File.Exists(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, rulesFolder, "_global_rules.json");
        }

        if (!File.Exists(path))
        {
            return string.Empty;
        }

        List<RuleItem> rules;
        try
        {
            var lastWrite = File.GetLastWriteTime(path);

            _cachedRulesMap.TryGetValue(rulesFolder, out var cachedEntry);

            // Kiểm tra nhanh ngoài lock (read-only, chấp nhận stale check nhẹ)
            if (cachedEntry != null && lastWrite <= cachedEntry.LastReadTime)
            {
                rules = cachedEntry.Rules;
            }
            else
            {
                // Vào critical section async-safe: chỉ 1 thread được đọc file và cập nhật cache
                await _rulesLock.WaitAsync();
                try
                {
                    // Double-check sau khi acquire semaphore để tránh reload trùng
                    _cachedRulesMap.TryGetValue(rulesFolder, out cachedEntry);
                    if (cachedEntry == null || lastWrite > cachedEntry.LastReadTime)
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
                        
                        cachedEntry = new CachedRulesEntry
                        {
                            Rules = tempRules,
                            LastReadTime = lastWrite
                        };
                        _cachedRulesMap[rulesFolder] = cachedEntry;
                    }
                    rules = cachedEntry.Rules;
                }
                finally
                {
                    _rulesLock.Release();
                }
            }
        }
        catch
        {
            if (_cachedRulesMap.TryGetValue(rulesFolder, out var cachedEntry))
            {
                rules = cachedEntry.Rules;
            }
            else
            {
                rules = new List<RuleItem>();
            }
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
