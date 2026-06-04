using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Backend.Models;
using Backend.Services;
using Backend.Services.Rag;
using Backend.Services.Security;
using Microsoft.Data.SqlClient;
using Xunit;
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
    public async Task TestSection1() => await RunSectionTestAsync(1);

    [Fact]
    public async Task TestSection2() => await RunSectionTestAsync(2);

    [Fact]
    public async Task TestSection3() => await RunSectionTestAsync(3);

    [Fact]
    public async Task TestSection4() => await RunSectionTestAsync(4);

    [Fact]
    public async Task TestSection5() => await RunSectionTestAsync(5);

    [Fact]
    public async Task TestSection6() => await RunSectionTestAsync(6);

    [Fact]
    public async Task TestSection7() => await RunSectionTestAsync(7);

    [Fact]
    public async Task TestSection8() => await RunSectionTestAsync(8);

    [Fact]
    public async Task TestSection9() => await RunSectionTestAsync(9);

    private async Task RunSectionTestAsync(int sectionId)
    {
        // 1. Tìm và load .env
        string current = Directory.GetCurrentDirectory();
        string envPath = null;
        while (current != null)
        {
            var testPath = Path.Combine(current, ".env");
            if (File.Exists(testPath))
            {
                envPath = testPath;
                break;
            }
            current = Directory.GetParent(current)?.FullName;
        }

        Assert.NotNull(envPath);
        DotNetEnv.Env.Load(envPath);

        // 2. Setup options
        var options = VertexAiOptions.FromEnvironment();
        var qdrantOptions = QdrantOptions.FromEnvironment();
        var sqlOptions = new SqlOptions
        {
            ConnectionString = Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING")
        };

        // 3. Setup services
        using var httpClient = new HttpClient();
        var aiClient = new VertexAiClient(httpClient, options);
        var qdrantService = new QdrantService(qdrantOptions);
        var securityValidator = new SqlSecurityValidator();
        var sqlService = new SqlService(sqlOptions, securityValidator);
        var ruleProvider = new SqlRuleProvider();
        var responseParser = new AiResponseParser();
        var planExecutor = new SqlPlanExecutor(aiClient, sqlService, responseParser);
        var orchestrator = new RagOrchestrator(aiClient, qdrantService, options, ruleProvider, responseParser, planExecutor);

        // 4. Tìm và parse file test_cases.md
        string rootDir = Directory.GetParent(envPath).FullName;
        string testCasesPath = Path.Combine(rootDir, "test_cases.md");
        Assert.True(File.Exists(testCasesPath), $"Không tìm thấy file: {testCasesPath}");

        var allTestCases = AutoTestRunner.ParseTestCases(testCasesPath);
        var testCases = allTestCases.Where(tc => tc.Section == sectionId).ToList();
        
        _output.WriteLine($"[PHẦN {sectionId}] Tìm thấy {testCases.Count} test cases.");

        var results = new List<TestResult>();
        int passCount = 0;

        foreach (var tc in testCases)
        {
            _output.WriteLine($"[CHẠY TEST] Phần {tc.Section}: \"{tc.Query}\"");
            var sw = Stopwatch.StartNew();
            var result = new TestResult { TestCase = tc };

            try
            {
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var response = await orchestrator.ProcessQueryAsync(
                    tc.Query,
                    null,
                    step => Task.CompletedTask,
                    chunk => Task.CompletedTask,
                    cts.Token
                );
                sw.Stop();

                result.LatencyMs = sw.ElapsedMilliseconds;
                result.ActualResponse = response.Text;

                // Kiểm tra từ khóa mong đợi
                var missing = new List<string>();
                foreach (var kw in tc.ExpectedKeywords)
                {
                    if (!AutoTestRunner.IsKeywordMatch(response.Text, kw))
                    {
                        missing.Add(kw);
                    }
                }

                result.MissingKeywords = missing;
                result.IsPass = (missing.Count == 0);
            }
            catch (Exception ex)
            {
                sw.Stop();
                result.LatencyMs = sw.ElapsedMilliseconds;
                result.IsPass = false;
                result.ActualResponse = $"LỖI: {ex.Message}";
                result.MissingKeywords = tc.ExpectedKeywords.ToList();
            }

            if (result.IsPass)
            {
                passCount++;
                _output.WriteLine($"  => PASS ({result.LatencyMs} ms)");
            }
            else
            {
                _output.WriteLine($"  => FAIL ({result.LatencyMs} ms). Thiếu từ khóa: {string.Join(", ", result.MissingKeywords)}");
            }

            results.Add(result);
        }

        // 5. Xuất báo cáo markdown cho từng section
        string reportPath = Path.Combine(rootDir, $"test_report_section_{sectionId}.md");
        using (var writer = new StreamWriter(reportPath, false, Encoding.UTF8))
        {
            await writer.WriteLineAsync($"# Báo Cáo Kết Quả Kiểm Thử Phần {sectionId}");
            await writer.WriteLineAsync($"*Thời gian thực thi: {DateTime.Now:dd/MM/yyyy HH:mm:ss}*");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync($"## Tóm tắt:");
            await writer.WriteLineAsync($"- **Tổng số test cases:** {testCases.Count}");
            await writer.WriteLineAsync($"- **Số test case đạt (PASS):** {passCount} / {testCases.Count} ({Math.Round((double)passCount / testCases.Count * 100, 2)}%)");
            await writer.WriteLineAsync($"- **Số test case lỗi (FAIL):** {testCases.Count - passCount}");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("## Chi tiết kết quả:");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("| Câu hỏi | Giá trị mong muốn | Kết quả RAG | Thời gian (ms) | Trạng thái |");
            await writer.WriteLineAsync("| :--- | :--- | :--- | :--- | :--- |");

            foreach (var r in results)
            {
                string expectedStr = string.Join(", ", r.TestCase.ExpectedKeywords);
                string statusEmoji = r.IsPass ? "✅ PASS" : "❌ FAIL";
                
                string cleanActual = r.ActualResponse.Replace("\n", " ").Replace("|", "\\|").Trim();
                if (cleanActual.Length > 150)
                {
                    cleanActual = cleanActual.Substring(0, 147) + "...";
                }

                await writer.WriteLineAsync($"| {r.TestCase.Query} | {expectedStr} | {cleanActual} | {r.LatencyMs} | {statusEmoji} |");
            }
        }

        _output.WriteLine($"Đã xuất báo cáo kiểm thử vào file: {reportPath}");
        
        Assert.True(passCount > 0, $"Không có test case nào trong phần {sectionId} vượt qua.");
    }

    [Fact]
    public void TestInspectSchema()
    {
        string connStr = "Server=34.143.255.115; Database=XuongMay; User Id=sqlserver; Password=4TUANKHOADUNGNAM; Encrypt=True; TrustServerCertificate=True; MultipleActiveResultSets=True;";
        using var conn = new SqlConnection(connStr);
        conn.Open();

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
