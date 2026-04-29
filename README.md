# Vertex AI Chatbot Demo

This repo contains:
- `backend/`: ASP.NET Core Web API that calls Vertex AI (Gemini generateContent + embeddings) using an API key.
- `frontend/`: React + Vite chat UI that calls the backend.

## Prerequisites
- .NET 8 SDK
- Node.js 18+
- Vertex AI API enabled for project `chatbot-494104`

## Configure environment
Copy `.env.example` to `.env` and set your API key.

```
VERTEX_API_KEY=YOUR_API_KEY
VERTEX_PROJECT_ID=chatbot-494104
VERTEX_REGION=asia-southeast1
VERTEX_LLM_MODEL=gemini-3.1-flash-lite-preview
VERTEX_EMBED_MODEL=gemini-embedding-001
```

## Run the backend

```
cd backend

dotnet run
```

API runs on `http://localhost:5000`.

## Run the frontend

```
cd frontend

npm install
npm run dev
```

Open `http://localhost:5173`.

## API endpoints
- `POST /api/chat` { `message`: string }
- `POST /api/embeddings` { `text`: string, `taskType`: string?, `outputDimensionality`: number? }

- Vertex AI REST is called with the API key in the request URL query string.

## Docker & Database Setup

This project uses Docker to run Qdrant and SQL Server.

1. **Start containers**:
   ```bash
   docker-compose up -d
   ```

2. **Initialize SQL Server Database**:
   Bạn có thể sử dụng script đã tạo sẵn để tự động tạo database và nạp dữ liệu:

   **Trên Windows (PowerShell):**
   ```powershell
   ./scripts/setup-db.ps1
   ```

   **Trên Linux/Mac/Git Bash:**
   ```bash
   chmod +x scripts/setup-db.sh
   ./scripts/setup-db.sh
   ```

   *(Script sẽ tự động đọc mật khẩu từ file `.env` và đợi SQL Server khởi động hoàn tất trước khi chạy lệnh SQL)*

## Database Viewer (UI Riêng biệt)

Bạn có thể xem và quản lý database trực tiếp qua giao diện Web:

- **Địa chỉ**: `http://localhost:8978`
- **Hướng dẫn kết nối**:
    1. Khi mở lần đầu, hãy làm theo các bước setup cơ bản của CloudBeaver.
    2. Chọn **Connection** -> **SQL Server**.
    3. **Host**: `sqlserver-db` (tên container).
    4. **Database**: `GarmentDB`.
    5. **Username**: `sa`.
    6. **Password**: (Mật khẩu trong file `.env`).
