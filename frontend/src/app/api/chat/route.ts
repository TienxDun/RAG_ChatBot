import { NextResponse } from 'next/server';

export async function POST(req: Request) {
  try {
    const { contents } = await req.json();
    const apiKey = process.env.VERTEX_API_KEY;
    const modelId = process.env.VERTEX_LLM_MODEL || "gemini-3.1-flash-lite-preview";
    const urlTemplate = process.env.VERTEX_API_URL_TEMPLATE || "https://aiplatform.googleapis.com/v1/publishers/google/models/{modelId}:{action}";
    
    // Construct Vertex AI URL from template
    const apiUrl = urlTemplate
      .replace("{modelId}", modelId)
      .replace("{action}", "streamGenerateContent");

    if (!apiKey) {
      return NextResponse.json({ error: 'VERTEX_API_KEY missing in .env' }, { status: 500 });
    }

    const maxTokens = parseInt(process.env.NEXT_PUBLIC_MAX_OUTPUT_TOKENS || '2048');
    const temperature = parseFloat(process.env.NEXT_PUBLIC_TEMPERATURE || '0.7');

    const response = await fetch(`${apiUrl}?key=${apiKey}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ 
        contents,
        generationConfig: {
          maxOutputTokens: maxTokens,
          temperature: temperature
        }
      }),
    });

    if (!response.ok) {
      const errorData = await response.json();
      return NextResponse.json(errorData, { status: response.status });
    }

    // Stream the response back to the client directly
    return new Response(response.body, {
      headers: {
        'Content-Type': 'text/event-stream',
        'Cache-Control': 'no-cache',
        'Connection': 'keep-alive',
      },
    });
  } catch (error: any) {
    return NextResponse.json({ error: 'Internal server error' }, { status: 500 });
  }
}
