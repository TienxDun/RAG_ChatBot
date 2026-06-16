using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Backend.Services.Excel;

public static class ExcelDataHelper
{
    // Chuyển đổi JSON sang DataTable case-insensitive theo các cột của template
    public static DataTable ConvertJsonToDataTable(string jsonString, List<string> templateColumns)
    {
        var dataTable = new DataTable();
        var normalizedColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var col in templateColumns)
        {
            dataTable.Columns.Add(col, typeof(object));
            normalizedColumns[col] = col;
        }

        if (string.IsNullOrWhiteSpace(jsonString))
        {
            return dataTable;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonString);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return dataTable;
            }

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var row = dataTable.NewRow();

                foreach (var prop in element.EnumerateObject())
                {
                    if (normalizedColumns.TryGetValue(prop.Name, out var originalColName))
                    {
                        var valElement = prop.Value;
                        object? value = null;

                        switch (valElement.ValueKind)
                        {
                            case JsonValueKind.Number:
                                value = valElement.GetDouble();
                                break;
                            case JsonValueKind.True:
                            case JsonValueKind.False:
                                value = valElement.GetBoolean();
                                break;
                            case JsonValueKind.String:
                                var str = valElement.GetString();
                                if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                                {
                                    value = dt;
                                }
                                else if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                                {
                                    value = d;
                                }
                                else
                                {
                                    value = str;
                                }
                                break;
                            case JsonValueKind.Null:
                                value = DBNull.Value;
                                break;
                            default:
                                value = valElement.ToString();
                                break;
                        }

                        row[originalColName] = value ?? DBNull.Value;
                    }
                }

                dataTable.Rows.Add(row);
            }
        }
        catch
        {
            // Bỏ qua lỗi parse và trả về table rỗng hoặc phần đã parse được
        }

        return dataTable;
    }

    public static bool IsValidSizeLoiKiemFormat(string? valStr)
    {
        if (string.IsNullOrEmpty(valStr)) return false;
        var parts = valStr.Split('/');
        return parts.Length >= 3 && 
               long.TryParse(parts[parts.Length - 2].Trim(), out _) && 
               long.TryParse(parts[parts.Length - 1].Trim(), out _);
    }

    public static Dictionary<string, string> GetNumericColumns(DataTable table, Dictionary<string, string>? columnFormats)
    {
        const string cacheKey = "NumericColumnsFormatCache";
        if (table.ExtendedProperties.Contains(cacheKey))
        {
            if (table.ExtendedProperties[cacheKey] is Dictionary<string, string> cached)
            {
                return cached;
            }
        }

        var numericCols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataColumn dc in table.Columns)
        {
            string columnName = dc.ColumnName;
            string lowerColName = columnName.ToLowerInvariant();
            
            // Loại trừ các cột định danh/mã số và ngày tháng, ký tên, ghi chú
            if (lowerColName.EndsWith("id") || lowerColName.Contains("id_") || lowerColName.StartsWith("id") ||
                lowerColName.Contains("ma") || lowerColName.Contains("code") || lowerColName.Contains("key") ||
                lowerColName.Contains("ngay") || lowerColName.Contains("date") || lowerColName.Contains("ten") ||
                lowerColName.Contains("name") || lowerColName.Contains("note") || lowerColName.Contains("ghichu") ||
                lowerColName.Contains("kyten") || lowerColName.Contains("signature"))
            {
                continue;
            }

            bool isCustomNumeric = false;
            string? customFormat = null;
            if (columnFormats != null && columnFormats.TryGetValue(columnName, out customFormat) && !string.IsNullOrWhiteSpace(customFormat))
            {
                string fmtLower = customFormat.ToLowerInvariant();
                if (!fmtLower.Contains("d") && !fmtLower.Contains("m") && !fmtLower.Contains("y"))
                {
                    isCustomNumeric = true;
                }
            }

            bool hasNumericValue = false;
            bool isNumeric = true;
            bool hasDecimalValue = false;

            if (isCustomNumeric && customFormat != null)
            {
                hasNumericValue = true;
                isNumeric = true;
            }
            else
            {
                foreach (DataRow row in table.Rows)
                {
                    var val = row[columnName];
                    if (val == null || val == DBNull.Value)
                    {
                        continue;
                    }

                    double dVal;
                    if (val is double || val is float || val is decimal || val is int || val is long || val is short || val is byte)
                    {
                        hasNumericValue = true;
                        dVal = Convert.ToDouble(val);
                        if (dVal != Math.Truncate(dVal))
                        {
                            hasDecimalValue = true;
                        }
                    }
                    else
                    {
                        string strVal = val.ToString() ?? "";
                        if (double.TryParse(strVal, NumberStyles.Any, CultureInfo.InvariantCulture, out dVal))
                        {
                            hasNumericValue = true;
                            if (dVal != Math.Truncate(dVal))
                            {
                                hasDecimalValue = true;
                            }
                        }
                        else
                        {
                            isNumeric = false;
                            break;
                        }
                    }
                }
            }

            if (isNumeric && hasNumericValue)
            {
                string format = "#,##0"; // Mặc định cho số nguyên
                
                if (isCustomNumeric && customFormat != null)
                {
                    format = customFormat;
                }
                else if (hasDecimalValue || lowerColName.Contains("tile") || lowerColName.Contains("rate") || 
                    lowerColName.Contains("percent") || lowerColName.Contains("%"))
                {
                    format = "#,##0.##;-#,##0.##;0";
                }

                numericCols[columnName] = format;
            }
        }

        table.ExtendedProperties[cacheKey] = numericCols;
        return numericCols;
    }
}
