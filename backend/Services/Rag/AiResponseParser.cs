using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Backend.Services.Rag;

public interface IAiResponseParser
{
    string CleanSql(string sql);
    string UnescapeString(string raw);
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
}
