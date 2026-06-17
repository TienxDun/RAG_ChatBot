using Backend.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace Backend.Services;

public sealed class DataSourceRegistry : IDisposable
{
    private readonly List<DataSourceConfig> _dataSources = new();
    private readonly IConfiguration _configuration;
    private readonly object _lock = new();
    private string? _configFilePath;
    private FileSystemWatcher? _fileWatcher;
    private bool _isDisposed;

    public DataSourceRegistry(IConfiguration configuration)
    {
        _configuration = configuration;
        ResolveConfigFilePath();
        LoadDataSources();
        InitializeFileWatcher();
    }

    private void ResolveConfigFilePath()
    {
        var possiblePaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "datasources.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "datasources.json"),
            Path.Combine(AppContext.BaseDirectory, "datasources.json")
        };

        var foundPath = possiblePaths.FirstOrDefault(File.Exists);
        if (foundPath == null)
        {
            // Fallback default path if not exists
            foundPath = Path.Combine(Directory.GetCurrentDirectory(), "datasources.json");
        }

        _configFilePath = Path.GetFullPath(foundPath);
    }

    private void LoadDataSources()
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(_configFilePath) || !File.Exists(_configFilePath))
            {
                return;
            }

            try
            {
                // Wait briefly if file is locked by another process (useful with watch/FS events)
                int retries = 3;
                while (retries > 0)
                {
                    try
                    {
                        var jsonContent = File.ReadAllText(_configFilePath);
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };
                        var wrapper = JsonSerializer.Deserialize<DataSourceWrapper>(jsonContent, options);
                        
                        _dataSources.Clear();
                        if (wrapper?.DataSources != null)
                        {
                            _dataSources.AddRange(wrapper.DataSources);
                        }
                        break;
                    }
                    catch (IOException) when (retries > 1)
                    {
                        retries--;
                        System.Threading.Thread.Sleep(100);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load data sources from {_configFilePath}: {ex.Message}");
            }
        }
    }

    private void SaveToFile()
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(_configFilePath)) return;

            try
            {
                // Temporarily disable watcher to prevent self-triggering reload
                if (_fileWatcher != null) _fileWatcher.EnableRaisingEvents = false;

                var wrapper = new DataSourceWrapper { DataSources = _dataSources };
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
                };
                var jsonContent = JsonSerializer.Serialize(wrapper, options);
                File.WriteAllText(_configFilePath, jsonContent);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save data sources to {_configFilePath}", ex);
            }
            finally
            {
                if (_fileWatcher != null && !_isDisposed) _fileWatcher.EnableRaisingEvents = true;
            }
        }
    }

    private void InitializeFileWatcher()
    {
        if (string.IsNullOrEmpty(_configFilePath)) return;

        try
        {
            var directory = Path.GetDirectoryName(_configFilePath);
            var filename = Path.GetFileName(_configFilePath);

            if (directory != null && Directory.Exists(directory))
            {
                _fileWatcher = new FileSystemWatcher(directory, filename)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
                };

                _fileWatcher.Changed += OnConfigFileChanged;
                _fileWatcher.EnableRaisingEvents = true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not initialize file watcher for datasources.json: {ex.Message}");
        }
    }

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        // Add a small delay to let the writing process release the file handle
        System.Threading.Thread.Sleep(100);
        LoadDataSources();
        Console.WriteLine("DataSources config reloaded automatically due to external change.");
    }

    public DataSourceConfig? GetByCollection(string? collectionName)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
            {
                return GetDefault();
            }
            return _dataSources.FirstOrDefault(ds => ds.QdrantCollection.Equals(collectionName, StringComparison.OrdinalIgnoreCase)) 
                   ?? GetDefault();
        }
    }

    public DataSourceConfig? GetById(string id)
    {
        lock (_lock)
        {
            return _dataSources.FirstOrDefault(ds => ds.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public DataSourceConfig GetDefault()
    {
        lock (_lock)
        {
            return _dataSources.FirstOrDefault(ds => ds.IsDefault) 
                   ?? _dataSources.FirstOrDefault() 
                   ?? throw new InvalidOperationException("No data source is registered or marked as default.");
        }
    }

    public List<DataSourceConfig> GetAll()
    {
        lock (_lock)
        {
            return _dataSources.ToList();
        }
    }

    public string GetConnectionString(DataSourceConfig dataSource)
    {
        // First try Env variable
        if (!string.IsNullOrEmpty(dataSource.ConnectionStringEnvVar))
        {
            var envCS = _configuration[dataSource.ConnectionStringEnvVar];
            if (!string.IsNullOrEmpty(envCS))
            {
                return envCS;
            }
        }

        // Fallback to direct connection string
        if (!string.IsNullOrEmpty(dataSource.ConnectionString))
        {
            return dataSource.ConnectionString;
        }

        throw new InvalidOperationException($"Connection string for '{dataSource.DisplayName}' is not configured.");
    }

    // CRUD Methods
    public void AddDataSource(DataSourceConfig newSource)
    {
        lock (_lock)
        {
            if (_dataSources.Any(ds => ds.Id.Equals(newSource.Id, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"DataSource with ID '{newSource.Id}' already exists.");
            }

            if (newSource.IsDefault)
            {
                ResetDefaults();
            }

            // Ensure RulesFolder directory exists
            EnsureRulesFolderExists(newSource.RulesFolder);

            _dataSources.Add(newSource);
            SaveToFile();
        }
    }

    public void UpdateDataSource(string id, DataSourceConfig updated)
    {
        lock (_lock)
        {
            var existing = _dataSources.FirstOrDefault(ds => ds.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                throw new KeyNotFoundException($"DataSource with ID '{id}' was not found.");
            }

            if (updated.IsDefault && !existing.IsDefault)
            {
                ResetDefaults();
            }

            // Ensure RulesFolder directory exists
            EnsureRulesFolderExists(updated.RulesFolder);

            existing.DisplayName = updated.DisplayName;
            existing.Description = updated.Description;
            existing.QdrantCollection = updated.QdrantCollection;
            existing.ConnectionString = updated.ConnectionString;
            existing.RulesFolder = updated.RulesFolder;
            existing.IsDefault = updated.IsDefault;

            SaveToFile();
        }
    }

    public bool RemoveDataSource(string id)
    {
        lock (_lock)
        {
            var existing = _dataSources.FirstOrDefault(ds => ds.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (existing == null) return false;

            if (existing.IsDefault)
            {
                throw new InvalidOperationException("Cannot delete the default DataSource.");
            }

            _dataSources.Remove(existing);
            SaveToFile();
            return true;
        }
    }

    private void ResetDefaults()
    {
        foreach (var ds in _dataSources)
        {
            ds.IsDefault = false;
        }
    }

    private void EnsureRulesFolderExists(string rulesFolder)
    {
        if (string.IsNullOrWhiteSpace(rulesFolder)) return;

        try
        {
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), rulesFolder);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
                Console.WriteLine($"Automatically created rules folder at: {fullPath}");

                // Write a default blank rules file
                var defaultRulesFile = Path.Combine(fullPath, "_global_rules.json");
                if (!File.Exists(defaultRulesFile))
                {
                    var defaultRulesContent = "[]";
                    File.WriteAllText(defaultRulesFile, defaultRulesContent);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to auto-create rules folder '{rulesFolder}': {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_fileWatcher != null)
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Changed -= OnConfigFileChanged;
            _fileWatcher.Dispose();
        }
    }

    private class DataSourceWrapper
    {
        public List<DataSourceConfig>? DataSources { get; set; }
    }
}
