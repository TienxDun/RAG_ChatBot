namespace Backend.Services.Rag;

public interface IQueryClassifier
{
    bool IsSimpleQuery(string query);
}

public class QueryClassifier : IQueryClassifier
{
    public bool IsSimpleQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;

        var q = query.ToLower().Trim();

        // LỚP 1: TỪ CHỐI FAST-PATH 100% NẾU CHỨA TỪ KHÓA THỐNG KÊ/PHÂN TÍCH/SO SÁNH/CỰC TRỊ
        string[] analysisKeywords = { 
            "top", "cao nhất", "thấp nhất", "nhiều nhất", "ít nhất", "tệ nhất", "tốt nhất",
            "thống kê", "so sánh", "tổng hợp", "báo cáo", "biểu đồ", "trung bình", "tỷ lệ", 
            "tỉ lệ", "phần trăm", "%", "lũy kế", "luy ke", "biến động", "xu hướng"
        };
        foreach (var keyword in analysisKeywords)
        {
            if (q.Contains(keyword)) return false; // Ép đi qua AI Planning
        }

        // LỚP 2: TỪ CHỐI FAST-PATH NẾU LIÊN QUAN ĐẾN LỖI/SẢN LƯỢNG MÀ KHÔNG CHỨA ĐỊNH DANH CỤ THỂ
        bool relatesToData = q.Contains("lỗi") || q.Contains("sản lượng") || q.Contains("san luong") || q.Contains("loi");
        if (relatesToData)
        {
            bool hasLineIdentifier = System.Text.RegularExpressions.Regex.IsMatch(q, @"\b\d{3,}\b") || q.Contains("chuyền") || q.Contains("chuyen");
            bool hasStyleIdentifier = System.Text.RegularExpressions.Regex.IsMatch(q, @"[a-zA-Z].*\d|\d.*[a-zA-Z]") || q.Contains("-");

            if (!hasLineIdentifier && !hasStyleIdentifier)
            {
                return false; // Thiếu định danh thực thể -> Ép đi qua AI Planning để làm rõ
            }
        }

        // LỚP 3: CÁC BỘ LỌC CHÀO HỎI & PHỨC TẠP
        string[] generalKeywords = { "chào", "hello", "hi", "bạn là ai", "giúp gì", "thời tiết", "cảm ơn", "thank", "tên gì" };
        foreach (var keyword in generalKeywords)
        {
            if (q == keyword || q.StartsWith(keyword + " ") || q.EndsWith(" " + keyword))
            {
                return false;
            }
        }
        
        string[] complexKeywords = { "sau đó", "sau khi", "rồi mới", "kết quả của", "tổng hợp từ", "kết hợp cả", "sau đó lọc" };
        foreach (var keyword in complexKeywords)
        {
            if (q.Contains(keyword)) return false;
        }

        // Mặc định: Chỉ những câu hỏi ngắn tra cứu tĩnh (< 80 ký tự) mới được đi Fast-path
        return query.Length < 80;
    }
}
