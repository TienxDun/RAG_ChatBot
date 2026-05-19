using System;
using System.Collections.Generic;
using System.Linq;

namespace Backend.Services;

// Cấu trúc dữ liệu cho mỗi template được cache trong bộ nhớ
public class CachedTemplate
{
    public string Id { get; set; } = string.Empty;           // GUID unique
    public string FileName { get; set; } = string.Empty;     // Tên file gốc
    public byte[] FileBytes { get; set; } = Array.Empty<byte>(); // Nội dung file (byte array)
    public DateTime CachedAt { get; set; }                   // Thời điểm lưu cache
    public long FileSize { get; set; }                       // Kích thước (bytes)
}

// Service Singleton quản lý bộ nhớ đệm (in-memory) cho các template Excel trống.
public class TemplateCacheService
{
    private readonly List<CachedTemplate> _cache = new();
    private readonly object _lock = new();
    private const int MaxCacheItems = 20; // Giới hạn tối đa 20 templates
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // Giới hạn 10MB mỗi file

    // Lưu template vào bộ nhớ đệm. 
    // Nếu cache đầy (20 items), sẽ xóa item cũ nhất (FIFO).
    public CachedTemplate StoreTemplate(byte[] bytes, string fileName)
    {
        if (bytes == null || bytes.Length == 0)
            throw new ArgumentException("Nội dung file không được để trống.");

        if (bytes.Length > MaxFileSizeBytes)
            throw new ArgumentException($"Kích thước file vượt quá giới hạn cho phép ({MaxFileSizeBytes / 1024 / 1024}MB).");

        lock (_lock)
        {
            // Kiểm tra trùng lặp: Cùng tên, cùng kích thước và nội dung byte giống hệt nhau
            var existing = _cache.FirstOrDefault(t => 
                t.FileName == fileName && 
                t.FileSize == bytes.Length && 
                t.FileBytes.SequenceEqual(bytes));

            if (existing != null)
            {
                // Nếu đã tồn tại, cập nhật thời gian và đẩy xuống cuối list (để giữ nó lâu nhất trong FIFO)
                existing.CachedAt = DateTime.UtcNow;
                _cache.Remove(existing);
                _cache.Add(existing);
                return existing;
            }

            // Thực hiện FIFO nếu cache đầy
            if (_cache.Count >= MaxCacheItems)
            {
                _cache.RemoveAt(0);
            }

            var template = new CachedTemplate
            {
                Id = Guid.NewGuid().ToString(),
                FileName = fileName,
                FileBytes = bytes,
                CachedAt = DateTime.UtcNow,
                FileSize = bytes.Length
            };

            _cache.Add(template);
            return template;
        }
    }

    // Lấy một template từ cache theo ID.
    public CachedTemplate? GetTemplate(string id)
    {
        lock (_lock)
        {
            return _cache.FirstOrDefault(t => t.Id == id);
        }
    }

    // Lấy danh sách tất cả các template hiện có trong cache.
    public List<CachedTemplate> GetAllTemplates()
    {
        lock (_lock)
        {
            // Trả về bản sao của list để đảm bảo thread-safety khi duyệt
            return _cache.ToList();
        }
    }

    // Xóa một template khỏi cache theo ID.
    public bool RemoveTemplate(string id)
    {
        lock (_lock)
        {
            var item = _cache.FirstOrDefault(t => t.Id == id);
            if (item != null)
            {
                return _cache.Remove(item);
            }
            return false;
        }
    }

    // Xóa toàn bộ cache.
    public void ClearAll()
    {
        lock (_lock)
        {
            _cache.Clear();
        }
    }

    // Lấy thông tin thống kê về bộ nhớ đệm.
    public object GetCacheStats()
    {
        lock (_lock)
        {
            return new
            {
                Count = _cache.Count,
                TotalSizeDisplay = $"{_cache.Sum(t => (double)t.FileSize) / 1024 / 1024:F2} MB",
                TotalSizeBytes = _cache.Sum(t => t.FileSize),
                MaxItems = MaxCacheItems,
                MaxSizePerFile = MaxFileSizeBytes
            };
        }
    }
}
