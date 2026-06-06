using System;
using System.Text.RegularExpressions;

namespace Backend.Services.Security;

public interface ISqlSecurityValidator
{
    void ValidateSqlSecurity(string sql);
}

public class SqlSecurityValidator : ISqlSecurityValidator
{
    // Danh sách các từ khóa nguy hiểm bị cấm tuyệt đối
    private static readonly string[] ForbiddenKeywords =
    {
        // DML / DDL
        "DROP", "DELETE", "TRUNCATE", "ALTER", "UPDATE", "INSERT",
        "CREATE", "GRANT", "REVOKE",
        // Execution
        "EXEC", "EXECUTE", "SP_EXECUTESQL",
        // Dangerous system features
        "OPENROWSET", "OPENQUERY", "OPENDATASOURCE",
        "BULK",                        // BULK INSERT
        "DBCC",
        // DoS / timing attacks
        "WAITFOR", "SHUTDOWN",
    };

    public void ValidateSqlSecurity(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new InvalidOperationException("SQL query is empty.");

        // 0. Strip SQL comments trước khi validate để chặn bypass bằng comment injection
        var cleanedSql = StripSqlComments(sql);
        var upperSql = cleanedSql.Trim().ToUpperInvariant();

        // 1. Chỉ cho phép câu lệnh truy vấn dữ liệu (SELECT hoặc CTE)
        if (!Regex.IsMatch(upperSql, @"^\s*(SELECT|WITH)\s", RegexOptions.None))
        {
            throw new InvalidOperationException("Hệ thống chỉ cho phép thực thi các câu lệnh truy vấn dữ liệu (SELECT).");
        }

        // 2. Chặn tất cả multi-statement SQL — bất kỳ dấu ; nào đều là dấu hiệu injection
        if (cleanedSql.Contains(";"))
        {
            throw new InvalidOperationException("Không được phép sử dụng dấu chấm phẩy (;) trong câu truy vấn.");
        }

        // 3. Chặn batch separator GO (SQL Server batch separator)
        if (Regex.IsMatch(upperSql, @"\bGO\b"))
        {
            throw new InvalidOperationException("Không được phép sử dụng lệnh GO trong câu truy vấn.");
        }

        // 4. Kiểm tra các từ khóa nguy hiểm
        foreach (var keyword in ForbiddenKeywords)
        {
            var pattern = $@"\b{Regex.Escape(keyword)}\b";
            if (Regex.IsMatch(upperSql, pattern))
            {
                throw new InvalidOperationException($"Phát hiện từ khóa nguy hiểm bị cấm: {keyword}");
            }
        }

        // 5. Chặn toàn bộ XP_ extended stored procedures bằng Contains (word boundary không bắt được XP_CMDSHELL)
        if (upperSql.Contains("XP_"))
        {
            throw new InvalidOperationException("Phát hiện từ khóa nguy hiểm bị cấm: XP_ extended procedures");
        }
    }

    // Loại bỏ SQL comments (block /* */ và line --) để chặn bypass bằng comment injection
    private static string StripSqlComments(string sql)
    {
        // Xóa block comments /* ... */ (bao gồm nested)
        var result = Regex.Replace(sql, @"/\*[\s\S]*?\*/", " ", RegexOptions.None);
        // Xóa line comments -- ... (đến cuối dòng)
        result = Regex.Replace(result, @"--[^\r\n]*", " ", RegexOptions.None);
        return result;
    }
}
