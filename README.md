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

## Notes
- The backend loads `.env` from the repo root.
- Vertex AI REST is called with the API key in the request URL query string.
