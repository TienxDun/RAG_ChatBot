using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace Backend.Services.Excel;

public interface ITextUtility
{
    string RemoveDiacritics(string text);
    string RemoveDiacriticsKeepSpaces(string text);
    string GenerateUniqueKey(string parent, string child);
    string? FindBestMetadataValue(Dictionary<string, string> metadata, string key);
    Dictionary<string, string> BuildSoftColumnMapping(DataTable source, List<FlattenedColumn> templateColumns);
    TemplateAnalysisResult MergeTemplateAnalysis(TemplateAnalysisResult llm, TemplateAnalysisResult ruleBased);
}

public class TextUtility : ITextUtility
{
    public string RemoveDiacritics(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        string normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (char c in normalizedString)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                if (c == 'đ') stringBuilder.Append('d');
                else if (c == 'Đ') stringBuilder.Append('D');
                else stringBuilder.Append(c);
            }
        }

        string result = stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        
        // Chỉ giữ lại chữ cái, chữ số và dấu gạch dưới, loại bỏ khoảng trắng và ký tự đặc biệt
        result = System.Text.RegularExpressions.Regex.Replace(result, @"[^a-zA-Z0-9_]", "");
        return result;
    }

    public string RemoveDiacriticsKeepSpaces(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (char c in normalized)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                if (c == 'đ') sb.Append('d');
                else if (c == 'Đ') sb.Append('D');
                else sb.Append(c);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public string GenerateUniqueKey(string parent, string child)
    {
        string p = RemoveDiacritics(parent ?? "").Trim();
        string c = RemoveDiacritics(child ?? "").Trim();

        if (string.IsNullOrEmpty(p)) return c;
        if (string.IsNullOrEmpty(c)) return p;
        
        // Nếu tên cha và tên con trùng nhau sau khi bỏ dấu (ví dụ: Ngay và Ngày)
        if (string.Equals(p, c, StringComparison.OrdinalIgnoreCase)) return c;

        return $"{p}_{c}";
    }

    public string? FindBestMetadataValue(Dictionary<string, string> metadata, string key)
    {
        if (metadata == null || string.IsNullOrEmpty(key)) return null;

        string cleanSearch = RemoveDiacriticsKeepSpaces(key).ToLowerInvariant();
        cleanSearch = System.Text.RegularExpressions.Regex.Replace(cleanSearch, @"([a-z])([A-Z])", "$1 $2");
        var searchTokens = cleanSearch.Split(new[] { ' ', '/', '_', '-', ':' }, StringSplitOptions.RemoveEmptyEntries);

        string? bestMatchValue = null;
        int maxMatchScore = 0;

        foreach (var kvp in metadata)
        {
            string cleanMetaKey = RemoveDiacriticsKeepSpaces(kvp.Key).ToLowerInvariant();
            cleanMetaKey = System.Text.RegularExpressions.Regex.Replace(cleanMetaKey, @"([a-z])([A-Z])", "$1 $2");
            var metaTokens = cleanMetaKey.Split(new[] { ' ', '/', '_', '-', ':' }, StringSplitOptions.RemoveEmptyEntries);

            int score = 0;
            foreach (var sToken in searchTokens)
            {
                if (sToken.Length <= 1) continue;
                foreach (var mToken in metaTokens)
                {
                    if (mToken.Length <= 1) continue;
                    if (sToken == mToken)
                    {
                        score += sToken.Length * 3;
                    }
                    else if (sToken.Contains(mToken))
                    {
                        score += mToken.Length * 2;
                    }
                    else if (mToken.Contains(sToken))
                    {
                        score += sToken.Length * 2;
                    }
                }
            }

            if (score > maxMatchScore)
            {
                maxMatchScore = score;
                bestMatchValue = kvp.Value;
            }
        }

        return maxMatchScore > 0 ? bestMatchValue : null;
    }

    public Dictionary<string, string> BuildSoftColumnMapping(DataTable source, List<FlattenedColumn> templateColumns)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tc in templateColumns)
        {
            string tcClean = RemoveDiacritics(tc.UniqueKey)
                .Replace("_", "").Replace("-", "").Replace(" ", "")
                .Replace("y", "i").Replace("Y", "i")
                .ToLowerInvariant();
            
            foreach (DataColumn dc in source.Columns)
            {
                string dcClean = RemoveDiacritics(dc.ColumnName)
                    .Replace("_", "").Replace("-", "").Replace(" ", "")
                    .Replace("y", "i").Replace("Y", "i")
                    .ToLowerInvariant();
                
                if (string.Equals(tcClean, dcClean, StringComparison.OrdinalIgnoreCase))
                {
                    mapping[tc.UniqueKey] = dc.ColumnName;
                    break;
                }
            }
        }
        return mapping;
    }

    public TemplateAnalysisResult MergeTemplateAnalysis(TemplateAnalysisResult llm, TemplateAnalysisResult ruleBased)
    {
        var merged = new TemplateAnalysisResult
        {
            Type = llm.Type,
            HeaderRowIndex = llm.HeaderRowIndex,
            StartColumnIndex = llm.StartColumnIndex,
            DataStartRowIndex = llm.DataStartRowIndex,
            DataEndRowIndex = ruleBased.DataEndRowIndex,
            TotalRowIndex = ruleBased.TotalRowIndex,
            FillableRowIndexes = ruleBased.FillableRowIndexes != null 
                ? new List<int>(ruleBased.FillableRowIndexes) 
                : new List<int>(),
            Columns = llm.Columns != null 
                ? new List<FlattenedColumn>(llm.Columns) 
                : new List<FlattenedColumn>(),
            Metadata = llm.Metadata != null 
                ? new Dictionary<string, string>(llm.Metadata, StringComparer.OrdinalIgnoreCase) 
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        if (ruleBased.Metadata != null)
        {
            foreach (var kvp in ruleBased.Metadata)
            {
                if (!merged.Metadata.ContainsKey(kvp.Key))
                {
                    merged.Metadata[kvp.Key] = kvp.Value;
                }
            }
        }

        return merged;
    }
}
