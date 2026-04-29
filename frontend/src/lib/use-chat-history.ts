import { useState, useEffect, useCallback } from "react";
import { Message } from "./chat-service";

export interface ChatSession {
  id: string;
  title: string;
  messages: Message[];
  updatedAt: number;
}

interface ChatHistoryState {
  sessions: ChatSession[];
  currentSessionId: string | null;
  isInitialized: boolean;
}

const STORAGE_KEY = "chat_history";

export function useChatHistory() {
  const [state, setState] = useState<ChatHistoryState>({
    sessions: [],
    currentSessionId: null,
    isInitialized: false,
  });

  // Load history from localStorage on mount
  useEffect(() => {
    const saved = localStorage.getItem(STORAGE_KEY);
    let sessions: ChatSession[] = [];
    if (saved) {
      try {
        const parsed = JSON.parse(saved) as ChatSession[];
        sessions = parsed.sort((a, b) => b.updatedAt - a.updatedAt);
      } catch (e) {
        console.error("Failed to parse chat history", e);
      }
    }
    setState(prev => ({
      ...prev,
      sessions,
      isInitialized: true
    }));
  }, []);

  // Save history to localStorage whenever sessions change
  useEffect(() => {
    if (state.isInitialized) {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(state.sessions));
    }
  }, [state.sessions, state.isInitialized]);

  const currentSession = state.sessions.find((s) => s.id === state.currentSessionId);

  const createNewChat = useCallback(() => {
    setState(prev => ({ ...prev, currentSessionId: null }));
  }, []);

  const saveChat = useCallback((messages: Message[]) => {
    if (messages.length === 0) return;

    setState((prev) => {
      const now = Date.now();
      const updatedSessions = [...prev.sessions];
      let newId = prev.currentSessionId;

      // If we're in an existing session
      if (prev.currentSessionId) {
        const existingIdx = updatedSessions.findIndex((s) => s.id === prev.currentSessionId);
        if (existingIdx !== -1) {
          updatedSessions[existingIdx] = {
            ...updatedSessions[existingIdx],
            messages,
            updatedAt: now,
          };
          updatedSessions.sort((a, b) => b.updatedAt - a.updatedAt);
          return { ...prev, sessions: updatedSessions };
        }
      }

      // If it's a new session
      const firstUserMsg = messages.find(m => m.role === 'user')?.content || "Cuộc trò chuyện mới";
      const title = firstUserMsg.length > 30 ? firstUserMsg.substring(0, 30) + "..." : firstUserMsg;
      
      const newSession: ChatSession = {
        id: crypto.randomUUID(),
        title,
        messages,
        updatedAt: now,
      };

      return {
        ...prev,
        currentSessionId: newSession.id,
        sessions: [newSession, ...updatedSessions].sort((a, b) => b.updatedAt - a.updatedAt),
      };
    });
  }, []);

  const deleteSession = useCallback((id: string) => {
    setState((prev) => {
      const filtered = prev.sessions.filter((s) => s.id !== id);
      return {
        ...prev,
        sessions: filtered,
        currentSessionId: prev.currentSessionId === id ? null : prev.currentSessionId
      };
    });
  }, []);

  const selectSession = useCallback((id: string) => {
    setState(prev => ({ ...prev, currentSessionId: id }));
  }, []);

  return {
    sessions: state.sessions,
    currentSession,
    currentSessionId: state.currentSessionId,
    createNewChat,
    saveChat,
    deleteSession,
    selectSession,
  };
}
