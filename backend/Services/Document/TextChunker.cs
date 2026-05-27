using System;
using System.Collections.Generic;
using System.Text;

namespace Backend.Services.Document;

public interface ITextChunker
{
    List<string> ChunkBySeparator(string text, string separator, int maxChunkLength = 1500, int overlap = 200);
}

public class TextChunker : ITextChunker
{
    public List<string> ChunkBySeparator(string text, string separator, int maxChunkLength = 1500, int overlap = 200)
    {
        var chunks = new List<string>();
        var paragraphs = text.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);

        var currentChunk = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            var trimmed = paragraph.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Nếu đoạn văn đơn lẻ quá dài, cần xé nhỏ nó ra theo maxChunkLength
            if (trimmed.Length > maxChunkLength)
            {
                // Trước khi xử lý đoạn văn khổng lồ này, hãy lưu chunk hiện tại nếu có
                if (currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                }

                // Xé nhỏ đoạn văn khổng lồ
                int start = 0;
                while (start < trimmed.Length)
                {
                    int length = Math.Min(maxChunkLength, trimmed.Length - start);
                    var subChunk = trimmed.Substring(start, length);
                    chunks.Add(subChunk.Trim());
                    
                    start += (maxChunkLength - overlap); // Di chuyển bước nhảy để tạo overlap
                    if (start >= trimmed.Length - overlap) break; // Tránh lặp vô hạn hoặc đoạn cuối quá ngắn
                }
                continue;
            }

            // Kiểm tra xem thêm đoạn văn này vào chunk hiện tại có bị quá dài không
            if (currentChunk.Length + trimmed.Length + separator.Length > maxChunkLength && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
                
                // Giữ lại một phần cuối của chunk trước làm overlap cho chunk sau
                var previousText = currentChunk.ToString();
                var overlapText = previousText.Length > overlap 
                    ? previousText.Substring(previousText.Length - overlap) 
                    : previousText;
                
                currentChunk.Clear();
                currentChunk.Append(overlapText);
            }

            currentChunk.Append(trimmed).Append(separator);
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString().Trim());
        }

        return chunks;
    }
}
