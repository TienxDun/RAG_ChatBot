using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Backend.Services;

// Cấu trúc dữ liệu cho mỗi template được cache trong bộ nhớ
public class CachedTemplate
{
    public string Id { get; set; } = string.Empty;           // GUID unique
    public string FileName { get; set; } = string.Empty;     // Tên file gốc
    public byte[] FileBytes { get; set; } = Array.Empty<byte>(); // Nội dung file (byte array)
    public DateTime CachedAt { get; set; }                   // Thời điểm lưu cache
    public long FileSize { get; set; }                       // Kích thước (bytes)
    public string ContentHash { get; set; } = string.Empty;  // SHA256 hash — dùng để so sánh trùng lặp O(1) thay vì SequenceEqual O(n)
}

// Service Singleton quản lý bộ nhớ đệm (in-memory) cho các template Excel trống.
public class TemplateCacheService
{
    private readonly List<CachedTemplate> _cache = new();
    private readonly object _lock = new();
    private const int MaxCacheItems = 20; // Giới hạn tối đa 20 templates
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // Giới hạn 10MB mỗi file
    private readonly string _templatesDir;

    public TemplateCacheService()
    {
        // 1. Xác định thư mục templates bền vững nằm tại data/templates ở thư mục gốc hoặc backend
        var rootDir = AppContext.BaseDirectory;
        var projectDir = Path.GetFullPath(Path.Combine(rootDir, "..", "..", "..", ".."));
        var dataDir = Path.Combine(projectDir, "data");

        if (!Directory.Exists(dataDir))
        {
            var backendDir = Path.GetFullPath(Path.Combine(rootDir, "..", "..", ".."));
            dataDir = Path.Combine(backendDir, "data");
            if (!Directory.Exists(dataDir))
            {
                dataDir = Path.Combine(Directory.GetCurrentDirectory(), "data");
                if (!Directory.Exists(dataDir))
                {
                    Directory.CreateDirectory(dataDir);
                }
            }
        }

        _templatesDir = Path.Combine(dataDir, "templates");
        if (!Directory.Exists(_templatesDir))
        {
            Directory.CreateDirectory(_templatesDir);
        }

        LoadTemplatesFromDisk();
    }

    private void LoadTemplatesFromDisk()
    {
        lock (_lock)
        {
            try
            {
                if (Directory.Exists(_templatesDir))
                {
                    var files = Directory.GetFiles(_templatesDir, "*.xlsx");
                    foreach (var file in files)
                    {
                        var fileInfo = new FileInfo(file);
                        var bytes = File.ReadAllBytes(file);
                        var template = new CachedTemplate
                        {
                            Id = Guid.NewGuid().ToString(),
                            FileName = fileInfo.Name,
                            FileBytes = bytes,
                            CachedAt = fileInfo.LastWriteTimeUtc,
                            FileSize = fileInfo.Length,
                            ContentHash = ComputeHash(bytes)
                        };
                        _cache.Add(template);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Lỗi khi nạp danh sách template từ đĩa: {ex.Message}");
            }
        }
    }
    public CachedTemplate StoreTemplate(byte[] bytes, string fileName)
    {
        if (bytes == null || bytes.Length == 0)
            throw new ArgumentException("Nội dung file không được để trống.");

        if (bytes.Length > MaxFileSizeBytes)
            throw new ArgumentException($"Kích thước file vượt quá giới hạn cho phép ({MaxFileSizeBytes / 1024 / 1024}MB).");

        lock (_lock)
        {
            // Kiểm tra trùng lặp: cùng tên file và SHA256 hash khớp — O(1) thay vì SequenceEqual O(n)
            var incomingHash = ComputeHash(bytes);
            var existing = _cache.FirstOrDefault(t =>
                t.FileName == fileName &&
                t.ContentHash == incomingHash);

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
                var oldest = _cache[0];
                try
                {
                    var oldestPath = Path.Combine(_templatesDir, oldest.FileName);
                    if (File.Exists(oldestPath))
                    {
                        File.Delete(oldestPath);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Lỗi khi xóa template cũ nhất theo cơ chế FIFO: {ex.Message}");
                }
                _cache.RemoveAt(0);
            }

            // Ghi file vật lý xuống đĩa
            try
            {
                var filePath = Path.Combine(_templatesDir, fileName);
                File.WriteAllBytes(filePath, bytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Không thể ghi file template xuống đĩa: {ex.Message}");
            }

            var template = new CachedTemplate
            {
                Id = Guid.NewGuid().ToString(),
                FileName = fileName,
                FileBytes = bytes,
                CachedAt = DateTime.UtcNow,
                FileSize = bytes.Length,
                ContentHash = incomingHash
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
                try
                {
                    var filePath = Path.Combine(_templatesDir, item.FileName);
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Không thể xóa file template vật lý: {ex.Message}");
                }
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
            try
            {
                if (Directory.Exists(_templatesDir))
                {
                    var files = Directory.GetFiles(_templatesDir, "*.xlsx");
                    foreach (var file in files)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Không thể làm sạch thư mục template vật lý: {ex.Message}");
            }
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

    // Tính SHA256 hash của byte array và trả về hex string — dùng để so sánh nội dung file O(1)
    private static string ComputeHash(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));
}
