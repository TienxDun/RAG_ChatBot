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
    private static readonly string[] ForbiddenKeywords = { 
        "DROP", "DELETE", "TRUNCATE", "ALTER", "UPDATE", "INSERT", 
        "EXEC", "EXECUTE", "CREATE", "GRANT", "REVOKE", "DBCC" 
    };

    public void ValidateSqlSecurity(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new InvalidOperationException("SQL query is empty.");

        var upperSql = sql.Trim().ToUpper();

        // 1. Chỉ cho phép câu lệnh truy vấn dữ liệu (SELECT hoặc CTE)
        if (!upperSql.StartsWith("SELECT") && !upperSql.StartsWith("WITH"))
        {
            throw new InvalidOperationException("Hệ thống chỉ cho phép thực thi các câu lệnh truy vấn dữ liệu (SELECT).");
        }

        // 2. Chặn chạy nhiều câu lệnh nguy hiểm, nhưng cho phép phân tách các câu lệnh SELECT/WITH bằng dấu ;
        if (sql.Contains(";") && (upperSql.Contains("DROP") || upperSql.Contains("DELETE") || upperSql.Contains("UPDATE") || upperSql.Contains("INSERT")))
        {
            throw new InvalidOperationException("Không được phép sử dụng dấu chấm phẩy (;) kết hợp với các lệnh thay đổi dữ liệu.");
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
