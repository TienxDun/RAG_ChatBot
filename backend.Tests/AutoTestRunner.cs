using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Backend.Models;
using Backend.Services;
using Backend.Services.Rag;
using Backend.Services.Security;

namespace backend.Tests;

public class TestCase
{
    public int Section { get; set; }
    public string Query { get; set; } = string.Empty;
    public List<string> ExpectedKeywords { get; set; } = new List<string>();
}

public class TestResult
{
    public TestCase TestCase { get; set; } = null!;
    public bool IsPass { get; set; }
    public string ActualResponse { get; set; } = string.Empty;
    public List<string> MissingKeywords { get; set; } = new List<string>();
    public long LatencyMs { get; set; }
}

public static class AutoTestRunner
{
    public static List<TestCase> ParseTestCases(string filePath)
    {
        var testCases = new List<TestCase>();
        if (!File.Exists(filePath)) return testCases;

        var lines = File.ReadAllLines(filePath);
        int currentSection = 0;
        TestCase? currentTestCase = null;
        bool inQueryBlock = false;
        var queryBuilder = new StringBuilder();
        var expectedBuilder = new StringBuilder();
        bool collectExpected = false;

        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("### 10"))
            {
                break; // Bỏ qua phần 10 hoàn toàn
            }

            if (trimmed.StartsWith("### "))
            {
                if (currentTestCase != null)
                {
                    ProcessExpectedData(currentTestCase, expectedBuilder.ToString());
                    testCases.Add(currentTestCase);
                    currentTestCase = null;
                    collectExpected = false;
                }

                var match = Regex.Match(trimmed, @"###\s*(\d+)");
                if (match.Success)
                {
                    currentSection = int.Parse(match.Groups[1].Value);
                }
                continue;
            }

            if (trimmed.StartsWith("```"))
            {
                if (inQueryBlock)
                {
                    inQueryBlock = false;
                    string queryText = queryBuilder.ToString().Trim().Replace("**", "").Trim();
                    
                    if (currentTestCase != null)
                    {
                        ProcessExpectedData(currentTestCase, expectedBuilder.ToString());
                        testCases.Add(currentTestCase);
                    }

                    currentTestCase = new TestCase { Section = currentSection, Query = queryText };
                    collectExpected = true;
                    expectedBuilder.Clear();
                }
                else
                {
                    inQueryBlock = true;
                    queryBuilder.Clear();
                }
                continue;
            }

            if (inQueryBlock)
            {
                queryBuilder.AppendLine(line);
            }
            else if (collectExpected)
            {
                expectedBuilder.AppendLine(line);
            }
        }

        if (currentTestCase != null)
        {
            ProcessExpectedData(currentTestCase, expectedBuilder.ToString());
            testCases.Add(currentTestCase);
        }

        return testCases.Where(t => !string.IsNullOrWhiteSpace(t.Query)).ToList();
    }

    private static void ProcessExpectedData(TestCase testCase, string expectedText)
    {
        var lines = expectedText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        
        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // 1. Xử lý bảng Markdown
            if (trimmed.StartsWith("|"))
            {
                if (trimmed.Contains("---")) continue;
                
                var cells = trimmed.Split('|')
                                   .Select(c => c.Trim().Replace("**", "").Trim())
                                   .Where(c => !string.IsNullOrEmpty(c))
                                   .ToList();
                                   
                // Bỏ qua tiêu đề bảng
                if (IsHeaderRow(cells)) continue;

                // Nếu là bảng 2 cột, chỉ lấy cột thứ hai (cột giá trị thực tế)
                if (cells.Count == 2)
                {
                    string val = cells[1];
                    if (!testCase.ExpectedKeywords.Contains(val))
                    {
                        testCase.ExpectedKeywords.Add(val);
                    }
                }
                else
                {
                    // Bảng nhiều cột, lấy tất cả các ô
                    foreach (var cell in cells)
                    {
                        if (!testCase.ExpectedKeywords.Contains(cell))
                        {
                            testCase.ExpectedKeywords.Add(cell);
                        }
                    }
                }
                continue;
            }

            // 2. Xử lý dòng danh sách bullet dạng: - **Nhãn:** Giá trị
            if (trimmed.StartsWith("-") || trimmed.StartsWith("*"))
            {
                var match = Regex.Match(trimmed, @"[-*]\s*\*\*[^*]+\*\*:\s*(.+)");
                if (match.Success)
                {
                    string val = match.Groups[1].Value.Trim().Replace("**", "").Trim();
                    if (!string.IsNullOrEmpty(val) && !testCase.ExpectedKeywords.Contains(val))
                    {
                        testCase.ExpectedKeywords.Add(val);
                    }
                }
                else
                {
                    int colonIdx = trimmed.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        string val = trimmed.Substring(colonIdx + 1).Trim().Replace("**", "").Trim();
                        if (!string.IsNullOrEmpty(val) && !testCase.ExpectedKeywords.Contains(val))
                        {
                            testCase.ExpectedKeywords.Add(val);
                        }
                    }
                }
                continue;
            }

            // 3. Xử lý text tự do: quét các cụm từ trong **...**
            var matches = Regex.Matches(trimmed, @"\*\*([^*]+)\*\*");
            foreach (Match m in matches)
            {
                string val = m.Groups[1].Value.Trim();
                if (val.EndsWith(":") || val.Contains("Tổng số lượng") || val.Contains("Tổng sản phẩm") || val.Contains("Tỷ lệ") || val.Contains("Tỉ lệ")) continue;
                if (!string.IsNullOrEmpty(val) && !testCase.ExpectedKeywords.Contains(val))
                {
                    testCase.ExpectedKeywords.Add(val);
                }
            }
        }
    }

    private static bool IsHeaderRow(List<string> cells)
    {
        return cells.Any(c => c.Equals("Chuyền", StringComparison.OrdinalIgnoreCase) || 
                              c.Equals("Tên chuyền", StringComparison.OrdinalIgnoreCase) ||
                              c.Equals("Tổng lỗi", StringComparison.OrdinalIgnoreCase) || 
                              c.Equals("Tổng đạt", StringComparison.OrdinalIgnoreCase) ||
                              c.Equals("Tỉ lệ lỗi (%)", StringComparison.OrdinalIgnoreCase) || 
                              c.Equals("Tỷ lệ lỗi (%)", StringComparison.OrdinalIgnoreCase) ||
                              c.Equals("Nội dung", StringComparison.OrdinalIgnoreCase) || 
                              c.Equals("Thông tin", StringComparison.OrdinalIgnoreCase) ||
                              c.Equals("Giá trị", StringComparison.OrdinalIgnoreCase) ||
                              c.Equals("Số lượng", StringComparison.OrdinalIgnoreCase) ||
                              c.Equals("Mã hàng", StringComparison.OrdinalIgnoreCase) ||
                              c.Equals("Size", StringComparison.OrdinalIgnoreCase) ||
                              c.Equals("Tổng số lượng lỗi", StringComparison.OrdinalIgnoreCase) ||
                              c.Equals("Hạng", StringComparison.OrdinalIgnoreCase) ||
                              c.Equals("Khách hàng", StringComparison.OrdinalIgnoreCase) ||
                              c.Equals("Doanh thu (VNĐ)", StringComparison.OrdinalIgnoreCase) ||
                              c.Equals("Doanh thu (VND)", StringComparison.OrdinalIgnoreCase) ||
                              c.Equals("Mã lỗi", StringComparison.OrdinalIgnoreCase) ||
                              c.Equals("Tên lỗi", StringComparison.OrdinalIgnoreCase) ||
                              c.Equals("Thứ hạng", StringComparison.OrdinalIgnoreCase) ||
                              c.Equals("Số lần xuất hiện", StringComparison.OrdinalIgnoreCase) ||
                              c.Equals("Ngày", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsKeywordMatch(string ragResult, string expectedKeyword)
    {
        if (string.IsNullOrWhiteSpace(ragResult)) return false;
        
        string normalizedResult = NormalizeNumbers(ragResult.Replace("\r\n", "\n"));
        string normalizedExpected = NormalizeNumbers(expectedKeyword);

        // 1. So sánh tỷ lệ phần trăm (chứa %)
        if (normalizedExpected.Contains("%"))
        {
            string numStrExpected = new string(normalizedExpected.Where(c => char.IsDigit(c) || c == '.').ToArray());
            if (double.TryParse(numStrExpected, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double valExpected))
            {
                // Thử tìm số thực đi kèm %
                var mc = Regex.Matches(normalizedResult, @"([0-9]+(?:\.[0-9]+)?)\s*%");
                foreach (Match match in mc)
                {
                    if (double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double valActual))
                    {
                        if (Math.Abs(valExpected - valActual) < 0.15) // Sai số cho phép 0.15%
                        {
                            return true;
                        }
                    }
                }
                
                // Thử tìm số thực đứng một mình
                var mc2 = Regex.Matches(normalizedResult, @"\b([0-9]+(?:\.[0-9]+)?)\b");
                foreach (Match match in mc2)
                {
                    if (double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double valActual))
                    {
                        if (Math.Abs(valExpected - valActual) < 0.15)
                        {
                            return true;
                        }
                    }
                }
            }
        }

        // 2. So sánh số nguyên hoặc số thực
        string cleanExpected = normalizedExpected.Trim();
        if (double.TryParse(cleanExpected, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double valExpectedNum))
        {
            if (!cleanExpected.Contains("."))
            {
                // Số nguyên
                string pattern = $@"\b{cleanExpected}\b";
                if (Regex.IsMatch(normalizedResult, pattern))
                {
                    return true;
                }
            }
            else
            {
                // Số thực
                var mc = Regex.Matches(normalizedResult, @"\b([0-9]+\.[0-9]+)\b");
                foreach (Match match in mc)
                {
                    if (double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double valActual))
                    {
                        if (Math.Abs(valExpectedNum - valActual) < 0.1) // Sai số nhỏ
                        {
                            return true;
                        }
                    }
                }
            }
        }

        // 3. So sánh chuỗi thường mềm dẻo (không dấu, loại bỏ ngoặc đơn)
        return CleanString(normalizedResult).Contains(CleanString(normalizedExpected));
    }

    private static string NormalizeNumbers(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        // Thay thế "7,927" -> "7.927", "16,476" -> "16476"
        string temp = Regex.Replace(text, @"(\d),(\d{1,3})(?!\d)", match => {
            string afterComma = match.Groups[2].Value;
            if (afterComma.Length == 3 && !text.Contains("%"))
            {
                // Phân cách hàng nghìn, loại bỏ dấu phẩy
                return match.Groups[1].Value + afterComma;
            }
            // Số thập phân
            return match.Groups[1].Value + "." + afterComma;
        });

        // Bỏ dấu chấm phân cách hàng nghìn trong số nguyên (chỉ áp dụng khi không có kí hiệu % phía sau)
        temp = Regex.Replace(temp, @"(\d)\.(\d{3})\b(?!\s*%)", "$1$2");
        return temp;
    }

    private static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string CleanString(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        string temp = RemoveDiacritics(s).ToLower();
        return temp.Replace("_", "-")
                   .Replace(" ", "")
                   .Replace(".", "")
                   .Replace(",", "")
                   .Replace("(", "")
                   .Replace(")", "")
                   .Trim();
    }
}
