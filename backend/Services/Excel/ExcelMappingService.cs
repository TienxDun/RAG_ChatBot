using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Backend.Services.Excel;

public interface IExcelMappingService
{
    void SaveMapping(string fileName, Dictionary<string, string> mappings);
    Dictionary<string, string> GetMapping(string fileName);
    void SaveTemplateMapping(string fileName, ExcelTemplateMapping mapping);
    ExcelTemplateMapping GetTemplateMapping(string fileName);
}

public class ExcelMappingService : IExcelMappingService
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private Dictionary<string, ExcelTemplateMapping> _mappingsStore = new(StringComparer.OrdinalIgnoreCase);

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
                        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                        if (data != null)
                        {
                            var newStore = new Dictionary<string, ExcelTemplateMapping>(StringComparer.OrdinalIgnoreCase);
                            foreach (var kvp in data)
                            {
                                newStore[kvp.Key] = ParseMapping(kvp.Value);
                            }
                            _mappingsStore = newStore;
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

    private static ExcelTemplateMapping ParseMapping(JsonElement element)
    {
        var mapping = new ExcelTemplateMapping();
        if (element.ValueKind == JsonValueKind.Object)
        {
            bool hasColumnMappings = element.TryGetProperty("columnMappings", out var colProp) || 
                                     element.TryGetProperty("ColumnMappings", out colProp);
            bool hasMetadataCellMappings = element.TryGetProperty("metadataCellMappings", out var metaProp) || 
                                           element.TryGetProperty("MetadataCellMappings", out metaProp);

            if (hasColumnMappings || hasMetadataCellMappings)
            {
                if (colProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in colProp.EnumerateObject())
                    {
                        mapping.ColumnMappings[prop.Name] = prop.Value.GetString() ?? "";
                    }
                }
                if (metaProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in metaProp.EnumerateObject())
                    {
                        mapping.MetadataCellMappings[prop.Name] = prop.Value.GetString() ?? "";
                    }
                }
            }
            else
            {
                // Định dạng cũ (tất cả là column mappings)
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        mapping.ColumnMappings[prop.Name] = prop.Value.GetString() ?? "";
                    }
                }
            }
        }
        return mapping;
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
            var templateMapping = GetTemplateMapping(fileName);
            templateMapping.ColumnMappings = mappings;
            SaveTemplateMapping(fileName, templateMapping);
        }
    }

    public Dictionary<string, string> GetMapping(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return new Dictionary<string, string>();

        lock (_lock)
        {
            LoadFromFile();
            var templateMapping = GetTemplateMapping(fileName);
            return templateMapping.ColumnMappings;
        }
    }

    public void SaveTemplateMapping(string fileName, ExcelTemplateMapping mapping)
    {
        if (string.IsNullOrWhiteSpace(fileName) || mapping == null) return;

        lock (_lock)
        {
            _mappingsStore[fileName] = mapping;
            SaveToFile();
        }
    }

    public ExcelTemplateMapping GetTemplateMapping(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return new ExcelTemplateMapping();

        lock (_lock)
        {
            LoadFromFile();
            if (_mappingsStore.TryGetValue(fileName, out var mapping) && mapping != null)
            {
                return mapping;
            }
            return new ExcelTemplateMapping();
        }
    }
}
