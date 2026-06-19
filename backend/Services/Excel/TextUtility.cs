using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace Backend.Services.Excel;

public interface ITextUtility
{
    string RemoveDiacritics(string text);
    string RemoveDiacriticsKeepSpaces(string text);
    string GenerateUniqueKey(string parent, string child);
    string? FindBestMetadataValue(Dictionary<string, string> metadata, string key);
    Dictionary<string, string> BuildSoftColumnMapping(DataTable source, List<FlattenedColumn> templateColumns);
    TemplateAnalysisResult MergeTemplateAnalysis(TemplateAnalysisResult llm, TemplateAnalysisResult ruleBased);
    string? GetMappingValue(Dictionary<string, string> mappings, string key);
}

public class TextUtility : ITextUtility
{
    public string RemoveDiacritics(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        string normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (char c in normalizedString)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                if (c == 'đ') stringBuilder.Append('d');
                else if (c == 'Đ') stringBuilder.Append('D');
                else stringBuilder.Append(c);
            }
        }

        string result = stringBuilder.ToString().Normalize(NormalizationForm.FormC);

        // Chỉ giữ lại chữ cái, chữ số và dấu gạch dưới, loại bỏ khoảng trắng và ký tự đặc biệt
        result = System.Text.RegularExpressions.Regex.Replace(result, @"[^a-zA-Z0-9_]", "");
        return result;
    }

    public string RemoveDiacriticsKeepSpaces(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (char c in normalized)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                if (c == 'đ') sb.Append('d');
                else if (c == 'Đ') sb.Append('D');
                else sb.Append(c);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Loại bỏ nội dung nằm trong dấu ngoặc đơn (...) khỏi chuỗi.
    /// Ví dụ: "Thành Phẩm (Finished)" → "Thành Phẩm"
    /// </summary>
    private static string StripParentheses(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s*\([^)]*\)", "").Trim();
    }

    public string GenerateUniqueKey(string parent, string child)
    {
        // Strategy A: Loại bỏ phần dịch song ngữ trong ngoặc đơn khi tạo prefix từ parent
        // "Thành Phẩm (Finished)" → "ThanhPham"
        string p = RemoveDiacritics(StripParentheses(parent ?? "")).Trim();

        // Strategy B: Với child, giữ nguyên nội dung ngoặc đơn để tạo key phân biệt
        // "Kết Luận (QC)"  → "KetLuanQC"   (không xóa ngoặc → tránh collision với "Kết Luận (Mer)")
        // "SL kiểm (Qty)"  → "SLkiemQty"   (loại ký tự đặc biệt nhưng giữ nội dung)
        string c = RemoveDiacritics(child ?? "").Trim();

        if (string.IsNullOrEmpty(p)) return string.IsNullOrEmpty(c) ? "" : c;
        if (string.IsNullOrEmpty(c)) return p;

        // Nếu tên cha và tên con trùng nhau sau khi bỏ dấu (ví dụ: Ngay và Ngày)
        string cStripped = RemoveDiacritics(StripParentheses(child ?? "")).Trim();
        if (string.Equals(p, cStripped, StringComparison.OrdinalIgnoreCase)) return c;

        return $"{p}_{c}";
    }

    private static readonly Dictionary<string, string[]> _synonymGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        { "lsx", new[] { "plancode", "poid", "malenh", "so lenh", "solenh", "po", "lsx", "tenlenhsx", "tenlenh", "malenhsx" } },
        { "mahang", new[] { "style", "styleid", "stypeid", "mahang", "ma hang", "hang", "mã hàng", "style/ mã hàng", "mã hàng/ style", "tenlenh", "malenh" } },
        { "chuyen", new[] { "line", "chuyen", "chuyenline", "linex", "chuyen/ line", "name", "tenchuyen", "ten chuyen" } },
        { "ngay", new[] { "date", "ngay", "createddate", "checkeddate" } },
        { "name", new[] { "name", "tenchuyen", "ten chuyen", "settinglinex", "tblsettinglinex" } }
    };

    public string? FindBestMetadataValue(Dictionary<string, string> metadata, string key)
    {
        if (metadata == null || string.IsNullOrEmpty(key)) return null;

        // Làm sạch key: Cắt bỏ phần chú dẫn kỹ thuật sau dấu gạch đứng '|' nếu có
        string baseKey = key.Contains('|') ? key.Split('|')[0].Trim() : key;
        string keyNorm = RemoveDiacritics(baseKey).ToLowerInvariant().Replace(" ", "").Replace("/", "").Replace("-", "").Replace("_", "");

        string cleanSearch = RemoveDiacriticsKeepSpaces(baseKey).ToLowerInvariant();
        cleanSearch = System.Text.RegularExpressions.Regex.Replace(cleanSearch, @"([a-z])([A-Z])", "$1 $2");
        var searchTokens = cleanSearch.Split(new[] { ' ', '/', '_', '-', ':' }, StringSplitOptions.RemoveEmptyEntries);

        string? bestMatchValue = null;
        int maxMatchScore = 0;

        foreach (var kvp in metadata)
        {
            // Làm sạch key từ dữ liệu SQL: Cắt bỏ phần chú dẫn sau dấu '|' nếu có
            string baseMetaKey = kvp.Key.Contains('|') ? kvp.Key.Split('|')[0].Trim() : kvp.Key;
            string metaKeyNorm = RemoveDiacritics(baseMetaKey).ToLowerInvariant().Replace(" ", "").Replace("/", "").Replace("-", "").Replace("_", "");

            string cleanMetaKey = RemoveDiacriticsKeepSpaces(baseMetaKey).ToLowerInvariant();
            cleanMetaKey = System.Text.RegularExpressions.Regex.Replace(cleanMetaKey, @"([a-z])([A-Z])", "$1 $2");
            var metaTokens = cleanMetaKey.Split(new[] { ' ', '/', '_', '-', ':' }, StringSplitOptions.RemoveEmptyEntries);

            int score = 0;
            if (string.Equals(keyNorm, metaKeyNorm, StringComparison.OrdinalIgnoreCase))
            {
                score = 1000;
            }
            else
            {
                bool sameSynonymGroup = false;
                foreach (var group in _synonymGroups)
                {
                    bool keyMatches = group.Value.Any(s => keyNorm.Contains(s) || s.Contains(keyNorm));
                    bool metaMatches = group.Value.Any(s => metaKeyNorm.Contains(s) || s.Contains(metaKeyNorm));
                    if (keyMatches && metaMatches)
                    {
                        sameSynonymGroup = true;
                        break;
                    }
                }

                if (sameSynonymGroup)
                {
                    score = 500;
                }
                else
                {
                    foreach (var sToken in searchTokens)
                    {
                        if (sToken.Length <= 1) continue;
                        foreach (var mToken in metaTokens)
                        {
                            if (mToken.Length <= 1) continue;
                            if (sToken == mToken)
                            {
                                score += sToken.Length * 3;
                            }
                            else if (sToken.Contains(mToken))
                            {
                                score += mToken.Length * 2;
                            }
                            else if (mToken.Contains(sToken))
                            {
                                score += sToken.Length * 2;
                            }
                        }
                    }
                }
            }

            if (score > maxMatchScore)
            {
                maxMatchScore = score;
                bestMatchValue = kvp.Value;
            }
        }

        return maxMatchScore > 0 ? bestMatchValue : null;
    }

    private string NormalizeKeyForSoftMatching(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        // Bỏ dấu tiếng Việt, viết thường
        string clean = RemoveDiacritics(key).ToLowerInvariant();

        // Chuẩn hóa y -> i
        clean = clean.Replace("y", "i");

        // Loại bỏ các từ tiếng Anh (bilingual translations) thường thấy trong template
        string[] englishWords = { "date", "finished", "defectrate", "defect", "quantity", "shell", "lining", "notes", "rate" };
        foreach (var word in englishWords)
        {
            clean = clean.Replace(word, "");
        }

        // Loại bỏ ký tự đặc biệt, dấu gạch dưới, khoảng trắng để so khớp chuỗi trơn
        clean = clean.Replace("_", "").Replace("-", "").Replace(" ", "");

        return clean;
    }

    public Dictionary<string, string> BuildSoftColumnMapping(DataTable source, List<FlattenedColumn> templateColumns)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var mappedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tc in templateColumns)
        {
            // 1. So khớp chính xác sau khi làm sạch cơ bản (giữ nguyên logic cũ)
            string tcClean = RemoveDiacritics(tc.UniqueKey)
                .Replace("_", "").Replace("-", "").Replace(" ", "")
                .Replace("y", "i").Replace("Y", "i")
                .ToLowerInvariant();

            bool found = false;
            foreach (DataColumn dc in source.Columns)
            {
                if (mappedColumns.Contains(dc.ColumnName)) continue;

                string dcClean = RemoveDiacritics(dc.ColumnName)
                    .Replace("_", "").Replace("-", "").Replace(" ", "")
                    .Replace("y", "i").Replace("Y", "i")
                    .ToLowerInvariant();

                if (string.Equals(tcClean, dcClean, StringComparison.OrdinalIgnoreCase))
                {
                    mapping[tc.UniqueKey] = dc.ColumnName;
                    mappedColumns.Add(dc.ColumnName);
                    found = true;
                    break;
                }
            }

            if (found) continue;

            // 2. So khớp thông minh / relaxed bằng cách loại bỏ các từ tiếng Anh dịch song ngữ
            string tcNorm = NormalizeKeyForSoftMatching(tc.UniqueKey);
            foreach (DataColumn dc in source.Columns)
            {
                if (mappedColumns.Contains(dc.ColumnName)) continue;

                string dcNorm = NormalizeKeyForSoftMatching(dc.ColumnName);
                if (string.Equals(tcNorm, dcNorm, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(tcNorm))
                {
                    mapping[tc.UniqueKey] = dc.ColumnName;
                    mappedColumns.Add(dc.ColumnName);
                    found = true;
                    break;
                }
            }

            if (found) continue;

            // 3. So khớp chứa / substring cho các trường hợp đặc biệt (như NgayDate với NgayKiem, hoặc TyLeLoi)
            foreach (DataColumn dc in source.Columns)
            {
                if (mappedColumns.Contains(dc.ColumnName)) continue;

                string dcNorm = NormalizeKeyForSoftMatching(dc.ColumnName);
                bool match = false;

                // Trường hợp ngày tháng
                if ((tcNorm.Contains("ngai") || tcNorm.Contains("date")) && (dcNorm.Contains("ngai") || dcNorm.Contains("date")))
                {
                    match = true;
                }
                // Trường hợp tỉ lệ lỗi
                else if (tcNorm.Contains("tileloi") && (dcNorm.Contains("tileloi") || dcNorm.Contains("tile") || dcNorm.Contains("loi")))
                {
                    // Đảm bảo cùng phân khúc (ví dụ: cả hai cùng thuộc ThanhPham hoặc cả hai cùng thuộc Vo/Shell)
                    string tcParent = NormalizeKeyForSoftMatching(tc.ParentHeader ?? "");
                    string dcParent = NormalizeKeyForSoftMatching(dc.ColumnName); // Tên cột SQL có thể tự chứa tên cha (vd: ThanhPham_TyLeLoi)
                    
                    if (string.IsNullOrEmpty(tcParent) || dcParent.Contains(tcParent))
                    {
                        match = true;
                    }
                }

                if (match)
                {
                    mapping[tc.UniqueKey] = dc.ColumnName;
                    mappedColumns.Add(dc.ColumnName);
                    break;
                }
            }
        }

        // Strategy B: Ordinal fallback — map các cột chưa match được theo vị trí thứ tự.
        // Chỉ áp dụng khi đã match được ít nhất 50% cột (đảm bảo thứ tự đáng tin cậy).
        int matchedCount = mapping.Count;
        int totalCount = templateColumns.Count;

        if (matchedCount > 0 && matchedCount >= totalCount / 2)
        {
            var unmappedTemplate = new List<(FlattenedColumn Col, int Index)>();
            for (int i = 0; i < templateColumns.Count; i++)
            {
                if (!mapping.ContainsKey(templateColumns[i].UniqueKey))
                {
                    unmappedTemplate.Add((templateColumns[i], i));
                }
            }

            var unmappedSource = new List<(DataColumn Col, int Index)>();
            for (int i = 0; i < source.Columns.Count; i++)
            {
                if (!mappedColumns.Contains(source.Columns[i].ColumnName))
                {
                    unmappedSource.Add((source.Columns[i], i));
                }
            }

            int pairCount = Math.Min(unmappedTemplate.Count, unmappedSource.Count);
            for (int i = 0; i < pairCount; i++)
            {
                mapping[unmappedTemplate[i].Col.UniqueKey] = unmappedSource[i].Col.ColumnName;
                mappedColumns.Add(unmappedSource[i].Col.ColumnName);
            }
        }

        return mapping;
    }


    public TemplateAnalysisResult MergeTemplateAnalysis(TemplateAnalysisResult llm, TemplateAnalysisResult ruleBased)
    {
        var merged = new TemplateAnalysisResult
        {
            Type = llm.Type,
            HeaderRowIndex = llm.HeaderRowIndex,
            StartColumnIndex = llm.StartColumnIndex,
            DataStartRowIndex = llm.DataStartRowIndex,
            DataEndRowIndex = ruleBased.DataEndRowIndex,
            TotalRowIndex = ruleBased.TotalRowIndex,
            FillableRowIndexes = ruleBased.FillableRowIndexes != null
                ? new List<int>(ruleBased.FillableRowIndexes)
                : new List<int>(),
            Columns = llm.Columns != null
                ? new List<FlattenedColumn>(llm.Columns)
                : new List<FlattenedColumn>(),
            Metadata = llm.Metadata != null
                ? new Dictionary<string, string>(llm.Metadata, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        if (ruleBased.Metadata != null)
        {
            foreach (var kvp in ruleBased.Metadata)
            {
                if (!merged.Metadata.ContainsKey(kvp.Key))
                {
                    merged.Metadata[kvp.Key] = kvp.Value;
                }
            }
        }

        return merged;
    }

    public string? GetMappingValue(Dictionary<string, string> mappings, string key)
    {
        if (mappings == null || string.IsNullOrEmpty(key)) return null;

        // 1. Thử khớp chính xác
        if (mappings.TryGetValue(key, out var val)) return val;

        // 2. Thử khớp mềm dẻo (không phân biệt hoa thường, bỏ dấu và ký tự đặc biệt)
        string normKey = NormalizeKeyForMapping(key);
        foreach (var kvp in mappings)
        {
            if (NormalizeKeyForMapping(kvp.Key) == normKey)
            {
                return kvp.Value;
            }
        }

        return null;
    }

    private string NormalizeKeyForMapping(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        // Bỏ dấu tiếng Việt, viết thường, loại bỏ dấu gạch dưới, khoảng trắng và dấu gạch nối
        string clean = RemoveDiacritics(key).ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(" ", "");
        return clean;
    }
}
