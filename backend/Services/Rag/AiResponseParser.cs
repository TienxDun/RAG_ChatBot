using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Backend.Services.Rag;

public interface IAiResponseParser
{
    string CleanSql(string sql);
    string UnescapeString(string raw);
    bool TryExtractInvalidJsonFields(string json, out string answer, out List<string> suggestions, out string? excelData, out string? columnMapping);
}

public class AiResponseParser : IAiResponseParser
{
    public string CleanSql(string sql)
    {
        if (string.IsNullOrEmpty(sql)) return string.Empty;
        return sql.Replace("```sql", "").Replace("```", "").Trim(' ', '\n', '\r', '\t', ';');
    }

    public string UnescapeString(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        try
        {
            return Regex.Unescape(raw);
        }
        catch
        {
            return raw.Replace("\\n", "\n")
                      .Replace("\\r", "\r")
                      .Replace("\\t", "\t")
                      .Replace("\\\"", "\"")
                      .Replace("\\\\", "\\");
        }
    }

    public bool TryExtractInvalidJsonFields(string json, out string answer, out List<string> suggestions, out string? excelData, out string? columnMapping)
    {
        answer = "";
        suggestions = new List<string>();
        excelData = null;
        columnMapping = null;

        if (string.IsNullOrWhiteSpace(json)) return false;

        var cleanJson = json.Replace("```json", "").Replace("```", "").Trim();

        try
        {
            // 1. Trích xuất answer
            var answerMatch = Regex.Match(cleanJson, @"""answer""\s*:\s*""([\s\S]*?)""\s*,\s*""(?:suggestions|excelData|columnMapping)""");
            if (answerMatch.Success)
            {
                answer = UnescapeString(answerMatch.Groups[1].Value);
            }
            else
            {
                int answerKeyIdx = cleanJson.IndexOf("\"answer\"");
                if (answerKeyIdx >= 0)
                {
                    int startQuoteIdx = cleanJson.IndexOf('"', answerKeyIdx + 8);
                    if (startQuoteIdx >= 0)
                    {
                        int nextKeyIdx = cleanJson.IndexOf("\"suggestions\"");
                        if (nextKeyIdx < 0) nextKeyIdx = cleanJson.IndexOf("\"excelData\"");
                        if (nextKeyIdx < 0) nextKeyIdx = cleanJson.IndexOf("\"columnMapping\"");

                        if (nextKeyIdx > startQuoteIdx)
                        {
                            int endQuoteIdx = cleanJson.LastIndexOf('"', nextKeyIdx);
                            while (endQuoteIdx > startQuoteIdx && cleanJson[endQuoteIdx] != '"')
                            {
                                endQuoteIdx--;
                            }
                            if (endQuoteIdx > startQuoteIdx)
                            {
                                string rawAnswer = cleanJson.Substring(startQuoteIdx + 1, endQuoteIdx - startQuoteIdx - 1);
                                answer = UnescapeString(rawAnswer);
                            }
                        }
                    }
                }
            }

            // 2. Trích xuất suggestions
            var suggestionsMatch = Regex.Match(cleanJson, @"""suggestions""\s*:\s*\[([\s\S]*?)\]");
            if (suggestionsMatch.Success)
            {
                var sugContent = suggestionsMatch.Groups[1].Value;
                var matches = Regex.Matches(sugContent, @"""([\s\S]*?)""");
                foreach (Match m in matches)
                {
                    suggestions.Add(UnescapeString(m.Groups[1].Value));
                }
            }

            // 3. Trích xuất excelData
            var excelDataMatch = Regex.Match(cleanJson, @"""excelData""\s*:\s*(\[[\s\S]*?\])");
            if (excelDataMatch.Success)
            {
                excelData = excelDataMatch.Groups[1].Value;
            }

            // 4. Trích xuất columnMapping
            var columnMappingMatch = Regex.Match(cleanJson, @"""columnMapping""\s*:\s*(\{[\s\S]*?\})");
            if (columnMappingMatch.Success)
            {
                columnMapping = columnMappingMatch.Groups[1].Value;
            }

            return !string.IsNullOrEmpty(answer);
        }
        catch
        {
            return false;
        }
    }
}
