# 🔍 Kế hoạch Review Code: Clean Code & Bảo mật thông tin

> **Phạm vi:** Toàn bộ code liên quan đến 2 tính năng Excel Template Integration + Merge Download Button
> **Ngày tạo:** 2026-05-07

---

## 📋 Tổng quan File cần review

| # | Layer | File | Dòng | Vai trò |
|---|-------|------|------|---------|
| 1 | Backend | `backend/Program.cs` | 291 | API endpoints chính |
| 2 | Backend | `backend/Services/RagOrchestrator.cs` | 203 | Luồng RAG chính |
| 3 | Backend | `backend/Services/ExcelReportService.cs` | 217 | Xử lý Excel template |
| 4 | Backend | `backend/Services/SqlService.cs` | 111 | Thực thi SQL |
| 5 | Backend | `backend/Services/VertexAiClient.cs` | 223 | Gọi Vertex AI API |
| 6 | Backend | `backend/Services/QdrantService.cs` | 86 | Tìm kiếm vector |
| 7 | Backend | `backend/Services/DocumentProcessor.cs` | 141 | Xử lý upload document |
| 8 | Backend | `backend/Models/VertexAiOptions.cs` | 60 | Config Vertex AI |
| 9 | Frontend | `frontend/src/lib/chat-service.ts` | 200 | Logic gọi API |
| 10 | Frontend | `frontend/src/components/chat/ChatMessage.tsx` | 228 | Hiển thị tin nhắn + nút download |
| 11 | Frontend | `frontend/src/components/chat/ChatInput.tsx` | 235 | Input + upload file |
| 12 | Frontend | `frontend/src/app/page.tsx` | 349 | Trang chính |
| 13 | Frontend | `frontend/src/app/api/chat/route.ts` | 53 | API route proxy |
| 14 | Frontend | `frontend/src/components/UploadModal.tsx` | 322 | Modal upload document |
| 15 | Config | `.env` | 45 | Biến môi trường |
| 16 | Config | `.env.example` | 43 | Template biến môi trường |
| 17 | Config | `.gitignore` | 84 | Quy tắc Git |

---

## 🚨 PHẦN 1: BẢO MẬT — Thông tin nhạy cảm

### 🔴 CRITICAL-01: File `.env` chứa API keys & mật khẩu THẬT

- **File:** [.env](file:///e:/thuctap/RAG_ChatBot/.env)
- **Mức độ:** 🔴 CRITICAL
- **Vấn đề:** File `.env` chứa **toàn bộ credentials thật** đang hoạt động:

| Dòng | Bí mật | Giá trị bị lộ |
|------|--------|---------------|
| 31 | `VERTEX_API_KEY` | `AQ.Ab8RN6Lj8EfW...` |
| 41 | `QDRANT_API_KEY` | `eyJhbGciOiJIUzI1...` (JWT token) |
| 45 | `MSSQL_CONNECTION_STRING` | Server, DB, User, Password đầy đủ |
| 14 | `ALLOWED_ORIGINS` | URL production Vercel |

- **Rủi ro:**
  - Nếu push lên GitHub public → **toàn bộ hệ thống bị compromise**
  - Bất kỳ ai có API key đều có thể gọi Vertex AI, truy cập Qdrant, và **đọc/ghi database SQL Server**
- **Kiểm tra:** `.gitignore` đã có rule `.env` → ✅ Tốt. Git history không có `.env` → ✅ An toàn.

> [!CAUTION]
> Dù `.env` chưa bị commit, **nên rotate (đổi mới) tất cả API keys** vì chúng đã bị hiển thị trên màn hình review.

**✅ Hành động:**
1. Rotate (tạo mới) `VERTEX_API_KEY` trên Google Cloud Console
2. Rotate `QDRANT_API_KEY` trên Qdrant Cloud Dashboard
3. Đổi mật khẩu `MSSQL_CONNECTION_STRING` trên DatabaseASP.net
4. Xác nhận `.env` KHÔNG BAO GIỜ bị commit (đã an toàn)

---

### 🟡 WARN-02: Hardcoded URL `localhost:5000` ở Frontend

- **File:** [chat-service.ts:34](file:///e:/thuctap/RAG_ChatBot/frontend/src/lib/chat-service.ts#L34)
- **Mức độ:** 🟡 WARNING
- **Vấn đề:** URL embedding API bị **hardcode trực tiếp**, không dùng biến môi trường:

```typescript
// chat-service.ts:34 — HARDCODED!
const response = await fetch('http://localhost:5000/api/embeddings', {
```

Trong khi các URL khác đã dùng `process.env.NEXT_PUBLIC_DOTNET_API_URL` đúng cách.

**✅ Hành động:**
```typescript
// Sửa thành:
const baseUrl = (process.env.NEXT_PUBLIC_DOTNET_API_URL || 'http://localhost:5000/api/chat').replace('/api/chat', '');
const response = await fetch(`${baseUrl}/api/embeddings`, {
```

---

### 🟡 WARN-03: Error message lộ thông tin nội bộ cho client

- **File:** [Program.cs:148](file:///e:/thuctap/RAG_ChatBot/backend/Program.cs#L148), [Program.cs:284](file:///e:/thuctap/RAG_ChatBot/backend/Program.cs#L284)
- **Mức độ:** 🟡 WARNING
- **Vấn đề:** `ex.Message` được gửi trực tiếp đến client, có thể chứa stack trace, tên bảng SQL, hoặc connection string:

```csharp
// Program.cs:148 — Lộ nội bộ qua SSE
await SendEventAsync(new { type = "error", message = ex.Message });

// Program.cs:284 — Lộ nội bộ qua HTTP response
return Results.Problem(ex.Message);
```

**✅ Hành động:**
```csharp
// Thay thế bằng:
catch (Exception ex)
{
    Console.Error.WriteLine($"[Chat Error] {ex}"); // Log đầy đủ ở server
    await SendEventAsync(new { type = "error", message = "Đã xảy ra lỗi khi xử lý yêu cầu." });
}

// Tương tự cho export-excel:
catch (Exception ex)
{
    Console.Error.WriteLine($"[ExportExcel Error] {ex}");
    return Results.Problem("Không thể xuất file Excel. Vui lòng thử lại.");
}
```

---

### 🟡 WARN-04: `excelBase64` được lưu vào localStorage

- **File:** [use-chat-history.ts:48](file:///e:/thuctap/RAG_ChatBot/frontend/src/lib/use-chat-history.ts#L48)
- **Mức độ:** 🟡 WARNING
- **Vấn đề:** Toàn bộ messages (bao gồm `excelBase64` — có thể ~5MB+ nội dung nhạy cảm) được serialize vào `localStorage`:

```typescript
localStorage.setItem(STORAGE_KEY, JSON.stringify(state.sessions));
```

Nếu file Excel chứa dữ liệu doanh thu, thông tin khách hàng → **ai truy cập được browser đều đọc được**.

**✅ Hành động:**
- Lọc bỏ `excelBase64` trước khi lưu localStorage:

```typescript
// Trước khi lưu:
const sanitized = state.sessions.map(s => ({
  ...s,
  messages: s.messages.map(m => {
    const { excelBase64, rawData, ...rest } = m;
    return rest;
  })
}));
localStorage.setItem(STORAGE_KEY, JSON.stringify(sanitized));
```

---

### 🟢 INFO-05: Debug file được ghi ra disk tại production

- **File:** [DocumentProcessor.cs:74-79](file:///e:/thuctap/RAG_ChatBot/backend/Services/DocumentProcessor.cs#L74-L79)
- **Mức độ:** 🟢 LOW (nhưng cần xử lý trước deploy)
- **Vấn đề:** File debug text được lưu vĩnh viễn trên server:

```csharp
var debugDir = Path.Combine(Directory.GetCurrentDirectory(), "temp_debug");
if (!Directory.Exists(debugDir)) Directory.CreateDirectory(debugDir);
var debugPath = Path.Combine(debugDir, $"debug_{fileName}.txt");
await File.WriteAllTextAsync(debugPath, extractedText, ct);
```

**✅ Hành động:**
- Bọc trong `#if DEBUG` hoặc check `Environment.IsDevelopment()`:
```csharp
#if DEBUG
var debugDir = Path.Combine(Directory.GetCurrentDirectory(), "temp_debug");
// ...
#endif
```

---

### 🟢 INFO-06: Swagger được expose ở Development

- **File:** [Program.cs:54-58](file:///e:/thuctap/RAG_ChatBot/backend/Program.cs#L54-L58)
- **Mức độ:** ✅ OK — Chỉ chạy khi `IsDevelopment()`, không lộ ở production.

---

### 🟢 INFO-07: `.env.example` an toàn

- **File:** [.env.example](file:///e:/thuctap/RAG_ChatBot/.env.example)
- **Mức độ:** ✅ OK — Chỉ chứa placeholder values (`YOUR_API_KEY_HERE`, `your-project-id`).

---

## 🧹 PHẦN 2: CLEAN CODE

### CC-01: `Program.cs` — Endpoint quá lớn, cần tách

- **File:** [Program.cs:64-177](file:///e:/thuctap/RAG_ChatBot/backend/Program.cs#L64-L177)
- **Mức độ:** 🟡 MEDIUM
- **Vấn đề:** Endpoint `/api/chat` dài **~113 dòng**, chứa cả logic đọc request, SSE setup, xử lý Excel, và xử lý chat thường trong cùng 1 lambda.

**✅ Hành động:** Tách thành method riêng:
```
Program.cs (endpoint routing only)
└── ChatEndpoints.cs (static class)
    ├── HandleChatAsync()
    ├── HandleExportExcelAsync()
    └── HandleDownloadAsync()
```

---

### CC-02: `Program.cs` — `fileCache` không có TTL (Memory Leak)

- **File:** [Program.cs:52](file:///e:/thuctap/RAG_ChatBot/backend/Program.cs#L52), [Program.cs:117](file:///e:/thuctap/RAG_ChatBot/backend/Program.cs#L117)
- **Mức độ:** 🟡 MEDIUM
- **Vấn đề:** `ConcurrentDictionary<string, byte[]>` lưu file Excel vĩnh viễn, không bao giờ xoá → **memory leak** nếu nhiều người dùng.

**✅ Hành động:** Dùng `IMemoryCache` với expiration:
```csharp
builder.Services.AddMemoryCache();
// ...
cache.Set(fileId, excelBytes, TimeSpan.FromMinutes(10));
```

---

### CC-03: `RagOrchestrator.cs` — Prompt SQL dài, hardcode trong code

- **File:** [RagOrchestrator.cs:56-95](file:///e:/thuctap/RAG_ChatBot/backend/Services/RagOrchestrator.cs#L56-L95)
- **Mức độ:** 🟢 LOW
- **Vấn đề:** SQL generation prompt dài ~35 dòng bị nhúng trực tiếp trong code C#. Khó maintain khi cần thay đổi prompt.

**✅ Hành động:** Chuyển prompt ra file riêng hoặc resource:
```
backend/Prompts/
├── sql_generation.txt
├── sql_healing.txt
└── final_answer.txt
```

---

### CC-04: `RagOrchestrator.cs` — Magic number `3072` lặp lại

- **File:** [RagOrchestrator.cs:31](file:///e:/thuctap/RAG_ChatBot/backend/Services/RagOrchestrator.cs#L31)
- **Mức độ:** 🟢 LOW
- **Vấn đề:** Vector dimension `3072` bị hardcode. Nếu đổi model embedding thì phải sửa nhiều chỗ.

**✅ Hành động:** Thêm vào `VertexAiOptions`:
```csharp
public int EmbeddingDimension { get; init; } = 3072;
```

---

### CC-05: `ExcelReportService.cs` — Exception message chứa raw data

- **File:** [ExcelReportService.cs:68](file:///e:/thuctap/RAG_ChatBot/backend/Services/ExcelReportService.cs#L68)
- **Mức độ:** 🟡 MEDIUM (vừa clean code, vừa security)
- **Vấn đề:**
```csharp
throw new Exception("AI không thể sinh được SQL hợp lệ: " + rawJson);
```
`rawJson` có thể chứa kết quả SQL hoặc lỗi database.

**✅ Hành động:**
```csharp
throw new Exception("AI không thể sinh được SQL hợp lệ từ yêu cầu của bạn.");
// Log riêng: _logger.LogError("SQL generation failed: {Raw}", rawJson);
```

---

### CC-06: `chat-service.ts` — Magic number `5` trong SSE parsing

- **File:** [chat-service.ts:102](file:///e:/thuctap/RAG_ChatBot/frontend/src/lib/chat-service.ts#L102)
- **Mức độ:** 🟢 LOW
- **Vấn đề:**
```typescript
const jsonStr = trimmedLine.substring(5).trim(); // "data:" có 5 ký tự, cộng 1 khoảng trắng là 6
```
Comment nói 5 ký tự nhưng `"data: "` thực sự là 6 ký tự. Kết quả đúng vì có `.trim()` sau, nhưng comment gây nhầm lẫn.

**✅ Hành động:**
```typescript
const SSE_PREFIX = "data: ";
const jsonStr = trimmedLine.substring(SSE_PREFIX.length).trim();
```

---

### CC-07: `ChatMessage.tsx` — Base64 decode nên dùng helper

- **File:** [ChatMessage.tsx:35-47](file:///e:/thuctap/RAG_ChatBot/frontend/src/components/chat/ChatMessage.tsx#L35-L47)
- **Mức độ:** 🟢 LOW
- **Vấn đề:** Logic decode Base64 → Blob → download dài 12 dòng, nên extract thành utility function.

**✅ Hành động:** Tạo helper trong `lib/utils.ts`:
```typescript
export function downloadBase64AsFile(base64: string, filename: string, mimeType: string) {
  const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
  const blob = new Blob([bytes], { type: mimeType });
  const link = document.createElement('a');
  link.href = URL.createObjectURL(blob);
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(link.href); // Cleanup memory
}
```

> [!NOTE]
> Code hiện tại thiếu `URL.revokeObjectURL()` → minor memory leak khi download nhiều lần.

---

### CC-08: `page.tsx` — `key={index}` trong list rendering

- **File:** [page.tsx:307](file:///e:/thuctap/RAG_ChatBot/frontend/src/app/page.tsx#L307)
- **Mức độ:** 🟢 LOW
- **Vấn đề:** Dùng array index làm key cho `ChatMessage` list. Khi edit/delete message, React có thể render sai.

**✅ Hành động:** Tạo unique ID cho mỗi message:
```typescript
// Trong Message type:
export type Message = {
  id: string; // crypto.randomUUID()
  role: "user" | "model";
  // ...
};
```

---

### CC-09: `SqlService.cs` — `ExecuteQueryAsJsonAsync` không được sử dụng

- **File:** [SqlService.cs:41-78](file:///e:/thuctap/RAG_ChatBot/backend/Services/SqlService.cs#L41-L78)
- **Mức độ:** 🟢 LOW
- **Vấn đề:** Method `ExecuteQueryAsJsonAsync` có vẻ là phiên bản cũ, hiện chỉ dùng `ExecuteQueryAsDataTableAsync` trong `RagOrchestrator.cs`.

**✅ Hành động:** Kiểm tra xem có chỗ nào gọi không. Nếu không → xoá dead code.

---

## 📊 Bảng tổng hợp ưu tiên

| Ưu tiên | ID | Vấn đề | Loại | Effort |
|---------|-----|--------|------|--------|
| 🔴 P0 | CRITICAL-01 | API keys thật trong `.env` | Security | 30 phút |
| 🟡 P1 | WARN-03 | Error message lộ nội bộ | Security | 15 phút |
| 🟡 P1 | WARN-04 | excelBase64 lưu localStorage | Security | 20 phút |
| 🟡 P1 | CC-02 | fileCache không có TTL | Clean Code | 20 phút |
| 🟡 P1 | CC-05 | Exception chứa raw data | Sec + Clean | 10 phút |
| 🟡 P2 | WARN-02 | Hardcoded localhost URL | Clean Code | 5 phút |
| 🟡 P2 | CC-01 | Endpoint quá lớn | Clean Code | 45 phút |
| 🟢 P3 | INFO-05 | Debug file ghi disk | Security | 5 phút |
| 🟢 P3 | CC-03 | Prompt hardcode | Clean Code | 30 phút |
| 🟢 P3 | CC-04 | Magic number 3072 | Clean Code | 5 phút |
| 🟢 P3 | CC-06 | Magic number SSE | Clean Code | 5 phút |
| 🟢 P3 | CC-07 | Base64 download helper | Clean Code | 10 phút |
| 🟢 P3 | CC-08 | key={index} | Clean Code | 10 phút |
| 🟢 P3 | CC-09 | Dead code SqlService | Clean Code | 5 phút |

---

## ✅ Thứ tự thực hiện đề xuất

```
Phase 1: Security (Bắt buộc trước deploy)
├── 1.1 Rotate tất cả API keys (CRITICAL-01)
├── 1.2 Che giấu error messages (WARN-03 + CC-05)
├── 1.3 Lọc excelBase64 khỏi localStorage (WARN-04)
└── 1.4 Bọc debug code trong #if DEBUG (INFO-05)

Phase 2: Clean Code (Nâng chất lượng)
├── 2.1 Sửa hardcoded URL (WARN-02)
├── 2.2 Thêm TTL cho fileCache (CC-02)
├── 2.3 Extract download helper (CC-07)
└── 2.4 Sửa SSE magic number (CC-06)

Phase 3: Refactor (Khi có thời gian)
├── 3.1 Tách endpoint ra file riêng (CC-01)
├── 3.2 Chuyển prompt ra file (CC-03)
├── 3.3 Thêm message ID (CC-08)
└── 3.4 Xoá dead code (CC-09)
```

---

> [!IMPORTANT]
> **Phase 1 cần hoàn thành TRƯỚC KHI deploy lên production.** Phase 2 & 3 có thể làm dần.
