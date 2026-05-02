# Hướng dẫn chi tiết thiết lập dự án RAG ChatBot với Vertex AI

Chào mừng bạn đến với dự án RAG ChatBot. Dưới đây là hướng dẫn từng bước từ việc tải mã nguồn về cho đến khi bạn có thể bắt đầu trò chuyện trên giao diện người dùng.

## Bước 1: Clone dự án từ GitHub

Mở terminal hoặc Command Prompt và chạy lệnh sau để tải dự án về máy:

```bash
git clone https://github.com/TienxDun/RAG_ChatBot.git
cd RAG_ChatBot
```

## Bước 2: Thiết lập biến môi trường

Dự án cần một số cấu hình để kết nối với các dịch vụ của Google Vertex AI và thiết lập mật khẩu cho cơ sở dữ liệu.

1. Tạo file `.env` bằng cách sao chép từ file mẫu:
   ```bash
   cp .env.example .env
   ```
2. Mở file `.env` vừa tạo và điền các thông tin cần thiết, đặc biệt là `VERTEX_API_KEY`:
   ```env
   VERTEX_API_KEY=YOUR_API_KEY
   VERTEX_PROJECT_ID=chatbot-494104
   VERTEX_REGION=asia-southeast1
   VERTEX_LLM_MODEL=gemini-3.1-flash-lite-preview
   VERTEX_EMBED_MODEL=gemini-embedding-001
   VERTEX_EXPRESS_MODE=true
   MSSQL_SA_PASSWORD=YourStrong!Passw0rd
   ```

## Bước 3: Khởi động Docker (Cơ sở dữ liệu & VectorDB)

Dự án sử dụng **Qdrant** (lưu trữ vector) và **SQL Server** (cơ sở dữ liệu quan hệ), cùng với **CloudBeaver** để xem dữ liệu. Tất cả đều được đóng gói bằng Docker.

Chạy lệnh sau để tải các image và khởi động container (chạy ngầm):

```bash
docker-compose up -d
```

Hãy đợi khoảng 1-2 phút để đảm bảo SQL Server khởi động thành công trước khi chuyển sang bước tiếp theo.

## Bước 4: Khởi tạo Cơ sở dữ liệu SQL Server

Tiếp theo, chúng ta cần tạo bảng và nạp dữ liệu mẫu vào SQL Server thông qua script đã được cung cấp.

- **Trên Windows (PowerShell):**
  ```powershell
  ./scripts/setup-db.ps1
  ```
- **Trên Linux / macOS / Git Bash:**
  ```bash
  chmod +x scripts/setup-db.sh
  ./scripts/setup-db.sh
  ```

*Lưu ý: Script này sẽ tự động đọc mật khẩu từ file `.env` và thiết lập database `GarmentDB`.*

## Bước 5: Nạp dữ liệu vào VectorDB (Qdrant)

Để ChatBot có thể tìm kiếm được thông tin (Retrieval-Augmented Generation - RAG), ta cần biến đổi (embed) lược đồ dữ liệu thành vector và lưu vào Qdrant.

Chạy lệnh PowerShell sau:
```powershell
./scripts/ingest-schema.ps1
```

*Lệnh này sẽ tạo collection `db_schema` trong Qdrant và gọi API của Vertex AI (Gemini Embedding) để lưu trữ các biểu diễn vector của database.*

## Bước 6: Khởi chạy ứng dụng (Backend & Frontend)

Bạn có thể chạy tự động cả Backend và Frontend chỉ với một script duy nhất nếu đang dùng Windows:

**Dùng Script (Windows PowerShell):**
```powershell
./scripts/start-dev.ps1
```

**Hoặc chạy thủ công theo từng bước:**

1. **Chạy Backend (.NET Core):**
   Mở terminal mới và chạy:
   ```bash
   cd backend
   dotnet run
   ```
   *Backend sẽ chạy ở địa chỉ `http://localhost:5000`.*

2. **Chạy Frontend (React + Vite):**
   Mở thêm một terminal khác và chạy:
   ```bash
   cd frontend
   npm install
   npm run dev
   ```
   *Frontend sẽ chạy ở địa chỉ `http://localhost:5173`.*

## Bước 7: Bắt đầu trò chuyện

Sau khi mọi thứ đã được khởi chạy, hãy mở trình duyệt web của bạn và truy cập vào giao diện của ứng dụng:

- **Giao diện ChatBot:** [http://localhost:3000](http://localhost:3000) (hoặc theo port hiển thị trên terminal).
- **Trình quản lý Cơ sở dữ liệu (CloudBeaver):** [http://localhost:8978](http://localhost:8978)
  - **Host**: `sqlserver-db`
  - **Database**: `GarmentDB`
  - **Username**: `sa`
  - **Password**: Lấy từ giá trị `MSSQL_SA_PASSWORD` trong file `.env`.

- **Quản lý Vector DB (Qdrant Dashboard):** [http://localhost:6333/dashboard](http://localhost:6333/dashboard)

- **Kết nối thông qua chuỗi kết nối (Connection String):**
  Nếu bạn cần kết nối từ ứng dụng backend hoặc các phần mềm quản trị database khác (như SSMS, DataGrip), bạn có thể dùng chuỗi kết nối sau:
  ```text
  Server=localhost,1433;Database=GarmentDB;User Id=sa;Password=YourStrong@Password123;TrustServerCertificate=True;
  ```

Bây giờ bạn đã có thể bắt đầu gõ câu hỏi trên giao diện ChatBot để AI tìm kiếm dữ liệu và trả lời cho bạn!
