using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Backend.Services.Excel;

public interface IExcelMappingService
{
    void SaveMapping(string fileName, Dictionary<string, string> mappings);
    Dictionary<string, string> GetMapping(string fileName);
}

public class ExcelMappingService : IExcelMappingService
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private Dictionary<string, Dictionary<string, string>> _mappingsStore = new(StringComparer.OrdinalIgnoreCase);

    public ExcelMappingService()
    {
        // 1. Thử tìm thư mục gốc dự án (đi lên 4 cấp từ AppContext.BaseDirectory để đến thư mục RAG_ChatBot)
        var rootDir = AppContext.BaseDirectory;
        var projectDir = Path.GetFullPath(Path.Combine(rootDir, "..", "..", "..", ".."));
        var dataDir = Path.Combine(projectDir, "data");

        // 2. Nếu không tìm thấy hoặc không tồn tại (ví dụ chạy ở môi trường Publish / Docker)
        if (!Directory.Exists(dataDir))
        {
            // Quay lại đi lên 3 cấp (thư mục backend)
            var backendDir = Path.GetFullPath(Path.Combine(rootDir, "..", "..", ".."));
            dataDir = Path.Combine(backendDir, "data");
            
            if (!Directory.Exists(dataDir))
            {
                // Fallback về thư mục chạy hiện tại
                dataDir = Path.Combine(Directory.GetCurrentDirectory(), "data");
                if (!Directory.Exists(dataDir))
                {
                    Directory.CreateDirectory(dataDir);
                }
            }
        }

        _filePath = Path.Combine(dataDir, "templates", "excel_mappings.json");
        LoadFromFile();
    }

    private void LoadFromFile()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
                        if (data != null)
                        {
                            _mappingsStore = new Dictionary<string, Dictionary<string, string>>(data, StringComparer.OrdinalIgnoreCase);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Lỗi khi tải dữ liệu ánh xạ Excel: {ex.Message}");
            }
        }
    }

    private void SaveToFile()
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(_mappingsStore, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Lỗi khi lưu dữ liệu ánh xạ Excel: {ex.Message}");
            }
        }
    }

    public void SaveMapping(string fileName, Dictionary<string, string> mappings)
    {
        if (string.IsNullOrWhiteSpace(fileName) || mappings == null) return;

        lock (_lock)
        {
            _mappingsStore[fileName] = mappings;
            SaveToFile();
        }
    }

    public Dictionary<string, string> GetMapping(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return new Dictionary<string, string>();

        lock (_lock)
        {
            if (_mappingsStore.TryGetValue(fileName, out var mappings) && mappings != null)
            {
                return new Dictionary<string, string>(mappings);
            }
            return new Dictionary<string, string>();
        }
    }
}
