using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Backend.Services.Document;

public interface IDbSchemaParser
{
    bool IsDatabaseSchema(JsonElement rootElement);
    string ParseSchema(JsonElement item, out string embeddingText, out Dictionary<string, string> metadata);
}

public class DbSchemaParser : IDbSchemaParser
{
    public bool IsDatabaseSchema(JsonElement rootElement)
    {
        return rootElement.TryGetProperty("table", out _) 
               && rootElement.TryGetProperty("columns", out var columnsProp) 
               && columnsProp.ValueKind == JsonValueKind.Array;
    }

    public string ParseSchema(JsonElement item, out string embeddingText, out Dictionary<string, string> metadata)
    {
        metadata = new Dictionary<string, string>();
        var sb = new StringBuilder();
        embeddingText = string.Empty;

        if (!item.TryGetProperty("table", out var tableProp) || !item.TryGetProperty("columns", out var columnsProp) || columnsProp.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var tableName = tableProp.GetString() ?? "Unknown";
        
        // Đọc purpose / description
        var purpose = string.Empty;
        if (item.TryGetProperty("purpose", out var purposeProp)) {
            purpose = purposeProp.GetString() ?? "";
        } else if (item.TryGetProperty("description", out var descProp)) {
            purpose = descProp.GetString() ?? "";
        }
        
        sb.AppendLine($"# BẢNG: {tableName}");
        if (!string.IsNullOrWhiteSpace(purpose))
        {
            sb.AppendLine($"**Mục đích:** {purpose}");
        }

        // Đọc when_to_use / when_not_to_use
        if (item.TryGetProperty("when_to_use", out var wtuProp) && !string.IsNullOrWhiteSpace(wtuProp.GetString()))
        {
            sb.AppendLine($"**Khi nào dùng:** {wtuProp.GetString()}");
        }
        if (item.TryGetProperty("when_not_to_use", out var wntuProp) && !string.IsNullOrWhiteSpace(wntuProp.GetString()))
        {
            sb.AppendLine($"**Khi nào KHÔNG dùng:** {wntuProp.GetString()}");
        }
        sb.AppendLine();

        // Đọc table_rules
        if (item.TryGetProperty("table_rules", out var rulesProp) && rulesProp.ValueKind == JsonValueKind.Array)
        {
            var rules = rulesProp.EnumerateArray().ToList();
            if (rules.Count > 0)
            {
                sb.AppendLine("## Quy tắc hoạt động (Table Rules):");
                foreach (var rule in rules)
                {
                    var rId = rule.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                    var rSeverity = rule.TryGetProperty("severity", out var sevProp) ? sevProp.GetString() ?? "" : "";
                    var rText = rule.TryGetProperty("rule", out var ruleProp2) ? ruleProp2.GetString() ?? "" : "";
                    var rCorrect = rule.TryGetProperty("correct_example", out var corProp) ? corProp.GetString() ?? "" : "";
                    var rWrong = rule.TryGetProperty("wrong_example", out var wrgProp) ? wrgProp.GetString() ?? "" : "";

                    sb.AppendLine($"- [{rId}] [{rSeverity}]: {rText}");
                    if (!string.IsNullOrWhiteSpace(rCorrect)) sb.AppendLine($"  * Ví dụ ĐÚNG: `{rCorrect}`");
                    if (!string.IsNullOrWhiteSpace(rWrong)) sb.AppendLine($"  * Ví dụ SAI: `{rWrong}`");
                }
                sb.AppendLine();
            }
        }

        // Đọc columns
        var columns = columnsProp.EnumerateArray().ToList();
        sb.AppendLine($"## Cấu trúc cột ({columns.Count} cột):");
        sb.AppendLine();
        sb.AppendLine("| Tên cột | Kiểu dữ liệu | Vai trò (Role) | Mô tả |");
        sb.AppendLine("|---------|--------------|----------------|-------|");

        foreach (var col in columns)
        {
            var colName = col.TryGetProperty("name", out var cn) ? cn.GetString() ?? "" : "";
            
            // Kiểu dữ liệu (type / data_type)
            var colType = string.Empty;
            if (col.TryGetProperty("type", out var ctProp)) {
                colType = ctProp.GetString() ?? "";
            } else if (col.TryGetProperty("data_type", out var dtProp)) {
                colType = dtProp.GetString() ?? "";
            }
            
            // Vai trò (role)
            var colRole = col.TryGetProperty("role", out var crProp) ? crProp.GetString() ?? "" : "";

            // Mô tả (desc / description)
            var colDesc = string.Empty;
            if (col.TryGetProperty("desc", out var cdProp)) {
                colDesc = cdProp.GetString() ?? "";
            } else if (col.TryGetProperty("description", out var descProp2)) {
                colDesc = descProp2.GetString() ?? "";
            }
            
            colDesc = colDesc.Replace("|", "\\|");
            
            sb.AppendLine($"| {colName} | {colType} | {colRole} | {colDesc} |");
        }
        sb.AppendLine();

        // Đọc relationships
        if (item.TryGetProperty("relationships", out var relsProp) && relsProp.ValueKind == JsonValueKind.Array)
        {
            var rels = relsProp.EnumerateArray().ToList();
            if (rels.Count > 0)
            {
                sb.AppendLine("## Mối quan hệ liên kết (Relationships):");
                foreach (var rel in rels)
                {
                    var targetTable = rel.TryGetProperty("target_table", out var ttProp) ? ttProp.GetString() ?? "" : "";
                    var joinOn = rel.TryGetProperty("join_on", out var joProp) ? joProp.GetString() ?? "" : "";
                    var notes = rel.TryGetProperty("notes", out var ntProp) ? ntProp.GetString() ?? "" : "";

                    sb.AppendLine($"- Liên kết với `{targetTable}` qua `{joinOn}` ({notes})");
                }
            }
        }

        // Lưu metadata đầy đủ
        metadata["table"] = tableName;
        metadata["purpose"] = purpose;
        metadata["columns"] = columnsProp.ToString();
        metadata["column_count"] = columns.Count.ToString();
        
        if (item.TryGetProperty("table_rules", out var rProp))
        {
            metadata["table_rules"] = rProp.ToString();
        }
        if (item.TryGetProperty("relationships", out var reProp))
        {
            metadata["relationships"] = reProp.ToString();
        }

        // Tạo văn bản rút gọn chuyên biệt để tính Embedding (tránh tràn 2048 tokens của Vertex AI)
        var sbEmbed = new StringBuilder();
        sbEmbed.AppendLine($"# BẢNG: {tableName}");
        if (!string.IsNullOrWhiteSpace(purpose))
        {
            sbEmbed.AppendLine($"**Mục đích:** {purpose}");
        }
        if (item.TryGetProperty("when_to_use", out var wtuPropEmbed) && !string.IsNullOrWhiteSpace(wtuPropEmbed.GetString()))
        {
            sbEmbed.AppendLine($"**Khi nào dùng:** {wtuPropEmbed.GetString()}");
        }
        if (item.TryGetProperty("when_not_to_use", out var wntuPropEmbed) && !string.IsNullOrWhiteSpace(wntuPropEmbed.GetString()))
        {
            sbEmbed.AppendLine($"**Khi nào KHÔNG dùng:** {wntuPropEmbed.GetString()}");
        }
        sbEmbed.AppendLine();

        if (item.TryGetProperty("table_rules", out var rulesPropEmbed) && rulesPropEmbed.ValueKind == JsonValueKind.Array)
        {
            var rules = rulesPropEmbed.EnumerateArray().ToList();
            if (rules.Count > 0)
            {
                sbEmbed.AppendLine("## Quy tắc:");
                foreach (var rule in rules)
                {
                    var rId = rule.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                    var rText = rule.TryGetProperty("rule", out var ruleProp2) ? ruleProp2.GetString() ?? "" : "";
                    sbEmbed.AppendLine($"- [{rId}]: {rText}");
                }
                sbEmbed.AppendLine();
            }
        }

        sbEmbed.AppendLine($"## Các cột:");
        foreach (var col in columns)
        {
            var colName = col.TryGetProperty("name", out var cn) ? cn.GetString() ?? "" : "";
            var colDesc = string.Empty;
            if (col.TryGetProperty("desc", out var cdProp)) {
                colDesc = cdProp.GetString() ?? "";
            } else if (col.TryGetProperty("description", out var descProp2)) {
                colDesc = descProp2.GetString() ?? "";
            }
            sbEmbed.AppendLine($"- {colName}: {colDesc}");
        }

        embeddingText = sbEmbed.ToString().Trim();
        return sb.ToString().Trim();
    }
}
