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
        "XP_CMDSHELL", "XP_",         // toàn bộ xp_ extended procs
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

        var upperSql = sql.Trim().ToUpper();

        // 1. Chỉ cho phép câu lệnh truy vấn dữ liệu (SELECT hoặc CTE)
        // Dùng Regex thay vì StartsWith để chặn bypass bằng SQL comment (-- hoặc /* */)
        if (!Regex.IsMatch(upperSql, @"^\s*(SELECT|WITH)\s", RegexOptions.None))
        {
            throw new InvalidOperationException("Hệ thống chỉ cho phép thực thi các câu lệnh truy vấn dữ liệu (SELECT).");
        }

        // 2. Chặn tất cả multi-statement SQL — bất kỳ dấu ; nào đều là dấu hiệu injection
        // (SQL Server không cần ; để kết thúc câu SELECT hợp lệ)
        if (sql.Contains(";"))
        {
            throw new InvalidOperationException("Không được phép sử dụng dấu chấm phẩy (;) trong câu truy vấn.");
        }

        // 3. Kiểm tra các từ khóa nguy hiểm
        foreach (var keyword in ForbiddenKeywords)
        {
            // Sử dụng Regex để tránh chặn nhầm các từ nằm trong tên cột (ví dụ: UpdateDate)
            var pattern = $@"\b{keyword}\b";
            if (Regex.IsMatch(upperSql, pattern))
            {
                throw new InvalidOperationException($"Phát hiện từ khóa nguy hiểm bị cấm: {keyword}");
            }
        }
    }
}
