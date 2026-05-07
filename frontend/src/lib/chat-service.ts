export type RagStep = {
  title: string;
  content: string;
};

export type Message = {
  role: "user" | "model";
  content: string;
  steps?: RagStep[];
  suggestedQuestions?: string[];
  rawData?: string;
  excelBase64?: string;
};

export class ChatService {
  private static MODE = process.env.NEXT_PUBLIC_API_MODE || 'dotnet';
  private static DOTNET_URL = process.env.NEXT_PUBLIC_DOTNET_API_URL || 'http://localhost:5000/api/chat';

  static async *sendMessage(userMessage: string, history: Message[], file: File | null = null): AsyncGenerator<{ content: string, steps?: RagStep[], suggestedQuestions?: string[], rawData?: string, excelBase64?: string }> {
    if (userMessage.startsWith('/embed ')) {
      const response = await this.sendToEmbedding(userMessage.replace('/embed ', ''));
      yield { content: response };
      return;
    }

    if (this.MODE === 'dotnet') {
      yield* this.sendToDotnet(userMessage, file);
    } else {
      yield* this.sendToDirect(userMessage, history);
    }
  }

  private static async sendToEmbedding(text: string): Promise<string> {
    const response = await fetch('http://localhost:5000/api/embeddings', {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ text }),
    });

    if (!response.ok) {
      throw new Error("Failed to fetch from Embedding API");
    }

    const data = await response.json();
    if (data.values) {
      const vector = data.values;
      const preview = vector.slice(0, 5).join(', ');
      return `VECTOR_GENERATED [DIM: ${vector.length}]\n\nDATA: [${preview}, ...]`;
    }
    return "";
  }

  private static async *sendToDotnet(message: string, file: File | null = null): AsyncGenerator<{ content: string, steps?: RagStep[], suggestedQuestions?: string[], rawData?: string, excelBase64?: string }> {
    let body: BodyInit;
    let headers: Record<string, string> = {};

    if (file) {
      const formData = new FormData();
      formData.append("message", message);
      formData.append("file", file);
      body = formData;
      // Note: Do NOT set Content-Type header when sending FormData
    } else {
      body = JSON.stringify({ message: message });
      headers["Content-Type"] = "application/json";
    }

    const response = await fetch(this.DOTNET_URL, {
      method: "POST",
      headers: headers,
      body: body,
    });

    if (!response.ok) {
      const errorText = await response.text();
      throw new Error(errorText || "Failed to fetch from .NET API");
    }

    const reader = response.body?.getReader();
    const decoder = new TextDecoder();
    let accumulatedSteps: RagStep[] = [];
    let accumulatedText = "";
    let buffer = "";

    if (!reader) return;

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });

      // Phân tách theo dòng để xử lý từng block 'data: '
      const lines = buffer.split("\n");
      // Dòng cuối cùng có thể chưa hoàn chỉnh, giữ lại trong buffer
      buffer = lines.pop() || "";

      for (const line of lines) {
        const trimmedLine = line.trim();
        if (!trimmedLine || !trimmedLine.startsWith("data: ")) continue;

        const jsonStr = trimmedLine.substring(5).trim(); // "data:" có 5 ký tự, cộng 1 khoảng trắng là 6
        if (!jsonStr) continue;

        try {
          const data = JSON.parse(jsonStr);

          if (data.type === "step") {
            accumulatedSteps.push(data.step);
            yield { content: accumulatedText, steps: [...accumulatedSteps] };
          } else if (data.type === "final") {
            accumulatedText = data.text;
            yield { 
              content: accumulatedText, 
              steps: accumulatedSteps, 
              suggestedQuestions: data.suggestedQuestions,
              rawData: data.rawData,
              excelBase64: data.excelBase64
            };
          } else if (data.type === "error") {
            throw new Error(data.message);
          }
        } catch (e) {
          console.error("Error parsing SSE JSON:", jsonStr, e);
        }
      }
    }
  }

  private static async *sendToDirect(userMessage: string, history: Message[]): AsyncGenerator<{ content: string }> {
    const chatHistory = history
      .filter(msg => msg.content.trim() !== "")
      .map(msg => ({
        role: msg.role,
        parts: [{ text: msg.content }]
      }));

    const response = await fetch("/api/chat", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        contents: [
          ...chatHistory,
          { role: "user", parts: [{ text: userMessage }] }
        ]
      }),
    });

    if (!response.ok) throw new Error("Failed to fetch from Direct API");

    const reader = response.body?.getReader();
    const decoder = new TextDecoder();
    let buffer = "";
    let accumulatedText = "";

    if (!reader) return;

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });

      let startIdx = buffer.indexOf('{');
      while (startIdx !== -1) {
        let stack = 0;
        let endIdx = -1;
        for (let i = startIdx; i < buffer.length; i++) {
          if (buffer[i] === '{') stack++;
          else if (buffer[i] === '}') {
            stack--;
            if (stack === 0) {
              endIdx = i;
              break;
            }
          }
        }

        if (endIdx !== -1) {
          const jsonStr = buffer.substring(startIdx, endIdx + 1);
          try {
            const json = JSON.parse(jsonStr);
            const text = json.candidates?.[0]?.content?.parts?.[0]?.text || "";
            if (text) {
              accumulatedText += text;
              yield { content: accumulatedText };
            }
          } catch (e) {
            console.error("Error parsing JSON chunk", e);
          }
          buffer = buffer.substring(endIdx + 1);
          startIdx = buffer.indexOf('{');
        } else {
          break;
        }
      }
    }
  }
}
