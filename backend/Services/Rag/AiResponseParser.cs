using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Backend.Services.Rag;

public interface IAiResponseParser
{
    string CleanSql(string sql);
}

public class AiResponseParser : IAiResponseParser
{
    public string CleanSql(string sql)
    {
        if (string.IsNullOrEmpty(sql)) return string.Empty;
        return sql.Replace("```sql", "").Replace("```", "").Trim(' ', '\n', '\r', '\t', ';');
    }
}
