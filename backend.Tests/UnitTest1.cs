using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace backend.Tests;

public class UnitTest1
{
    private readonly ITestOutputHelper _output;

    public UnitTest1(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TestInspectSchema()
    {
        string connStr = "Server=34.143.255.115; Database=XuongMay; User Id=sqlserver; Password=4TUANKHOADUNGNAM; Encrypt=True; TrustServerCertificate=True; MultipleActiveResultSets=True;";
        using var conn = new SqlConnection(connStr);
        conn.Open();

        // Find all columns containing 'Dat' or 'Kiem' or 'SL' in their names across all tables
        string sql = @"
            SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE 
            FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE COLUMN_NAME LIKE '%Dat%' OR COLUMN_NAME LIKE '%SL%'
            ORDER BY TABLE_NAME, COLUMN_NAME";

        using var cmd = new SqlCommand(sql, conn);
        using var reader = cmd.ExecuteReader();
        _output.WriteLine("COLUMNS CONTAINING 'Dat' OR 'SL' IN THE DATABASE:");
        while (reader.Read())
        {
            _output.WriteLine($"- {reader.GetString(0)}.{reader.GetString(1)} ({reader.GetString(2)})");
        }
    }
}
