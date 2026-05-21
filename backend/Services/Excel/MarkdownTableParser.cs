using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Backend.Services.Excel;

public static class MarkdownTableParser
{
    // Phân tách văn bản Markdown chứa bảng thành danh sách lưới dữ liệu
    public static List<List<string>> ParseMarkdownTable(string markdownText)
    {
        var rows = new List<List<string>>();
        if (string.IsNullOrWhiteSpace(markdownText)) return rows;

        var lines = markdownText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("|") || !trimmed.EndsWith("|")) continue;

            // Bỏ qua dòng phân cách bảng như |---|---|
            if (trimmed.Contains("---")) continue;

            var cells = trimmed.Split('|')
                               .Skip(1)
                               .Take(trimmed.Split('|').Length - 2)
                               .Select(c => c.Trim().Replace("**", ""))
                               .ToList();

            if (cells.Count > 0)
            {
                rows.Add(cells);
            }
        }
        return rows;
    }

    // Đọc định dạng ngày tháng linh hoạt
    public static bool TryParseDateTime(string value, out DateTime dt)
    {
        string[] dateFormats = new[] 
        { 
            "dd/MM/yyyy", "yyyy-MM-dd", "MM/dd/yyyy", 
            "dd-MM-yyyy", "yyyy/MM/dd", "dd/MM/yyyy HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss", "dd-MM-yyyy HH:mm:ss"
        };
        
        return DateTime.TryParseExact(value, dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt) ||
               DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt) ||
               DateTime.TryParse(value, out dt);
    }
}
