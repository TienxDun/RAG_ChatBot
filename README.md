# DODO AI - Intelligent SQL RAG Chatbot & Excel Reporter

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blueviolet.svg?style=flat-square)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![HTML/CSS/JS](https://img.shields.io/badge/Frontend-Vanilla_HTML_CSS_JS-blue.svg?style=flat-square)](#)
[![Qdrant](https://img.shields.io/badge/Vector_DB-Qdrant-red.svg?style=flat-square)](https://qdrant.tech/)
[![SQL Server](https://img.shields.io/badge/Database-SQL_Server-red.svg?style=flat-square)](#)
[![Vertex AI](https://img.shields.io/badge/AI-Google_Vertex_AI-orange.svg?style=flat-square)](#)

**DODO AI** là một hệ thống chatbot thông minh tích hợp cơ chế **RAG (Retrieval-Augmented Generation)**, giúp người dùng tương tác, truy vấn cơ sở dữ liệu **SQL Server** bằng ngôn ngữ tự nhiên và tự động điền/xuất dữ liệu báo cáo ra các biểu mẫu **Excel** (từ đơn giản đến phức tạp) dựa trên cấu trúc database được lập chỉ mục vector.

---

## 🌟 Tính Năng Nổi Bật

### 1. Phân Tích Cấu Trúc Database & RAG Pipeline
* **Lập chỉ mục cấu trúc (Schema Indexing):** Hệ thống tự động phân tích các tệp đặc tả schema JSON (trong thư mục `rag_schemas`), tạo vector embedding qua model `gemini-embedding-001` và lưu trữ vào **Qdrant Vector Database**.
* **Tìm kiếm ngữ cảnh (Context Retrieval):** Khi nhận câu hỏi, hệ thống truy xuất các bảng và quy tắc liên quan nhất từ Qdrant để làm thông tin nền đầu vào cho mô hình ngôn ngữ lớn (LLM).

### 2. Bộ Định Tuyến Tối Ưu Hóa "Fast-path"
* Tự động phân loại câu hỏi thông qua bộ lọc thông minh (`QueryClassifier.cs`):
  * **Fast-path:** Đối với các câu hỏi tra cứu tĩnh, ngắn gọn (dưới 80 ký tự) không chứa từ khóa thống kê hoặc phân tích, hệ thống sẽ bỏ qua bước lập kế hoạch AI để tối ưu hóa tốc độ phản hồi tối đa.
  * **AI Planning:** Đối với câu hỏi phức tạp (cần thống kê, gom nhóm, so sánh thời gian tương đối), hệ thống sử dụng LLM để lập kế hoạch đa bước, sinh câu truy vấn SQL chuẩn xác, ngăn ngừa hiện tượng ảo giác (hallucination).

### 3. Động Cơ Xuất Excel Động Thông Minh
* **Phân tích Template tự động:** Nhận diện và phân loại biểu mẫu Excel tải lên thành 2 dạng:
  * **Horizontal (Bảng lưới thông thường):** Dữ liệu trải theo dòng.
  * **Hierarchical (Bảng phân cấp):** Bảng có các ô tiêu đề gộp phức tạp (Parent/Child headers).
* **Ánh xạ cột tự động (Auto-Mapping):** Tự động khớp các cột dữ liệu trả về từ SQL với các cột trong Excel bằng cách chuẩn hóa văn bản (loại bỏ dấu tiếng Việt, so khớp không phân biệt hoa thường).
* **Đổ dữ liệu & Đồng bộ Style:** Tự động chèn thêm dòng mới và sao chép định dạng (Border, Font, Height, Alignment) từ dòng mẫu gốc khi số lượng dữ liệu lớn hơn số dòng trống hiện tại của template.
* **Cập nhật Metadata:** Điền chính xác các nhãn thông tin chung nằm phía trên tiêu đề bảng (ví dụ: `Mã hàng`, `Chuyền`, `Ngày tháng`).

### 4. Giao Diện Người Dùng (Frontend Vanilla) Hiện Đại
* **Aesthetic UX:** Giao diện tối giản, hỗ trợ hai chế độ sáng/tối (Dark/Light mode), hiệu ứng nền hạt vũ trụ (`Starfield`) chuyển động mượt mà.
* **Tìm kiếm hội thoại:** Tìm kiếm tin nhắn nhanh chóng trong lịch sử chat cục bộ.
* **Nhập liệu bằng giọng nói:** Tích hợp bộ thu âm Speech-to-Text thân thiện.
* **Tải tài liệu dạng SSE:** Modal kéo thả tài liệu (PDF, TXT, JSON) để nạp thêm dữ liệu vào Vector DB, hiển thị tiến trình xử lý thời gian thực qua Server-Sent Events (SSE).

---

## 🛠️ Công Nghệ Sử Dụng

### Backend (.NET 8.0)
* **ASP.NET Core Minimal API** làm dịch vụ REST API siêu nhẹ và hiệu năng cao.
* **EPPlus** làm thư viện chính để phân tích, ghi và chỉnh sửa các tệp Excel.
* **DotNetEnv** để quản lý các biến môi trường cấu hình linh hoạt.
* **Dapper / Microsoft.Data.SqlClient** để tương tác nhanh với MS SQL Server.
* **Swagger/OpenAPI** hỗ trợ kiểm thử API trực quan.

### Frontend (Vanilla Web Stack)
* **HTML5 / CSS3** thuần túy cấu trúc lưới (CSS Grid, Flexbox) và hiệu ứng chuyển động mượt mà. Không phụ thuộc thư viện CSS bên ngoài.
* **JavaScript (ES6 Modules)** quản lý mã nguồn dạng module cấu trúc rõ ràng (`app.js`, `core`, `components`, `services`).
* **Phosphor Icons & Marked.js** xử lý icon hiện đại và render văn bản Markdown.

### Hạ tầng và Trí Tuệ Nhân Tạo
* **Microsoft SQL Server:** Cơ sở dữ liệu quan hệ lưu trữ dữ liệu nghiệp vụ sản xuất.
* **Qdrant Vector DB:** Lưu trữ dữ liệu vector phục vụ tra cứu ngữ cảnh database.
* **Google Vertex AI:** Sử dụng mô hình `gemini-3.1-flash-lite-preview` (hoặc cấu hình khác) cho suy luận và `gemini-embedding-001` cho nhúng vector.

---

## 📂 Cấu Trúc Thư Mục Dự Án

```text
Demo/
├── backend/                   # Dự án Backend .NET 8.0 API
│   ├── Endpoints/             # Định nghĩa các Endpoint API (Chat, TemplateCache...)
│   ├── Models/                # Cấu trúc dữ liệu và Options cấu hình
│   ├── Services/              # Các dịch vụ xử lý nghiệp vụ
│   │   ├── Document/          # Phân tích schema CSDL, chunking văn bản
│   │   ├── Excel/             # Xử lý phân tích và điền Excel template
│   │   ├── Rag/               # Classifier, Phân tích phản hồi AI, Thực thi SQL
│   │   └── Security/          # Bộ lọc an toàn phòng chống SQL Injection
│   ├── rag_schemas/           # Chứa file JSON mô tả schema database & global rules
│   ├── Program.cs             # Khởi tạo dịch vụ và cấu hình middleware
│   └── appsettings.json       # Cấu hình log hệ thống
│
├── backend.Tests/             # Dự án Unit Test kiểm thử
│   └── ExcelTests.cs          # Test cases cho dịch vụ phân tích & fill Excel
│
├── frontend_vanilla/          # Mã nguồn Frontend HTML/CSS/JS thuần
│   ├── assets/
│   │   ├── css/               # Giao diện chính, component và animation
│   │   └── js/                # Logic ứng dụng chia theo ES6 Components & Services
│   ├── index.html             # Điểm khởi đầu giao diện người dùng
│   └── favicon.svg
│
├── data/                      # Dữ liệu mẫu dạng TXT/Tab-separated để nạp DB
├── .env.example               # Mẫu cấu hình biến môi trường
└── Demo.sln                   # Solution file của dự án
```

---

## 🚀 Hướng Dẫn Cài Đặt & Chạy Dự Án

### 📋 Yêu Cầu Hệ Thống
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* Một máy chủ **MS SQL Server** đang hoạt động.
* Một cluster **Qdrant DB** (Local qua Docker hoặc Qdrant Cloud).
* Google Cloud **Vertex AI API Key** (hoặc tài khoản Service Account có quyền truy cập).

### ⚙️ Cấu Hình Môi Trường
1. Ở thư mục gốc của dự án, sao chép file `.env.example` thành `.env`:
   ```bash
   cp .env.example .env
   ```
2. Mở file `.env` mới tạo và điền các tham số cấu hình:
   * **VERTEX_API_KEY / VERTEX_PROJECT_ID:** Thông tin kết nối AI Engine của Google.
   * **QDRANT_HOST / QDRANT_API_KEY:** Thông tin kết nối Vector Database.
   * **MSSQL_CONNECTION_STRING:** Chuỗi kết nối đến cơ sở dữ liệu SQL Server của bạn.

3. Điền thông tin API cho frontend bằng cách tạo file cấu hình:
   * Sao chép `frontend_vanilla/assets/js/env.example.js` thành `frontend_vanilla/assets/js/env.js`.
   * Cập nhật đường dẫn URL của API backend (mặc định là `http://localhost:5000/api`).

### 🏃 Chạy Backend
Mở terminal tại thư mục `backend/` và thực thi:
```bash
dotnet run
# Hoặc chế độ nóng (Hot reload) khi phát triển:
dotnet watch
```
*Mặc định API sẽ lắng nghe tại cổng `http://localhost:5000` (hoặc cổng được cấu hình trong `launchSettings.json`). Bạn có thể truy cập `http://localhost:5000/swagger` để xem tài liệu API.*

### 🖥️ Chạy Frontend
Vì mã nguồn frontend là HTML/CSS/JS thuần, bạn có thể:
1. Mở trực tiếp tệp `frontend_vanilla/index.html` bằng trình duyệt web.
2. Hoặc khởi chạy thông qua các máy chủ tĩnh tiện ích như Live Server (VS Code Extension), `http-server` (Node.js), hoặc `python -m http.server 3000` từ thư mục `frontend_vanilla`.

### 🧪 Chạy Kiểm Thử (Unit Tests)
Mở terminal tại thư mục gốc và chạy lệnh test để đảm bảo mọi tính năng Excel hoạt động ổn định:
```bash
dotnet test
```

---

## 🔒 An Toàn & Bảo Mật
* Hệ thống tích hợp bộ lọc phòng chống tấn công chèn mã độc **SQL Injection** (`SqlSecurityValidator.cs`). Mọi câu lệnh SQL do AI tạo ra đều được kiểm duyệt nghiêm ngặt qua danh sách các từ khóa cấm (`UNION`, `DROP`, `UPDATE`, `DELETE`...) và định dạng trước khi được thực thi thực tế trên cơ sở dữ liệu SQL Server.
