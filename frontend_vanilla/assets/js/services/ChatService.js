/**
 * ChatService.js - Handles streaming chat logic and history persistence
/**
 * ChatService.js - Handles streaming chat logic and history persistence
 */
import { ApiClient } from '../core/ApiClient.js';
import { ENDPOINTS } from '../core/Config.js';
import { state } from '../core/State.js';

export class ChatService {
    static async sendMessage(text, file, collectionName, callbacks = {}) {
        const { onStep, onChunk, onFinal, onError, onMessageElementCreated, msgId } = callbacks;
        const body = this._prepareBody(text, file, collectionName);
        
        let aiContent = "";
        let aiSteps = [];
        let aiSuggestions = [];
        let aiDownloadUrl = null;
        let aiDownloadFileName = null;
        let lastRawData = null;
        let aiIsAmbiguous = false;
        let aiMetadata = null;

        try {
            const startTime = Date.now();
            let elementCreated = false;
            await ApiClient.fetchStream(ENDPOINTS.CHAT, { body }, (data) => {
                if (data.type === 'step') {
                    aiSteps.push(data.step);
                    if (onStep) onStep(data.step);
                }

                if (data.type === 'chunk') {
                    aiContent += data.text;
                    if (onChunk) onChunk(data.text, aiContent);
                }

                if (['step', 'chunk', 'final', 'error'].includes(data.type) && onMessageElementCreated && !elementCreated) {
                    onMessageElementCreated();
                    elementCreated = true;
                }

                if (data.type === 'final') {
                    aiContent = data.text;
                    aiSuggestions = data.suggestedQuestions || [];
                    aiDownloadUrl = data.downloadUrl;
                    aiDownloadFileName = data.downloadFileName || null;
                    lastRawData = data.rawData;
                    aiIsAmbiguous = data.isAmbiguous || false;
                    aiMetadata = data.metadata || null;
                    if (onFinal) onFinal(data);
                } else if (data.type === 'error') {
                    aiContent = `⚠️ Lỗi: ${data.message}`;
                    if (onError) onError(data.message);
                }
            });

            const duration = Math.round((Date.now() - startTime) / 1000);
            this._saveToHistory(aiContent, aiSteps, aiSuggestions, aiDownloadUrl, aiDownloadFileName, lastRawData, duration, aiIsAmbiguous, msgId, aiMetadata);
            return { aiContent, aiSteps, aiSuggestions, aiDownloadUrl, aiDownloadFileName, lastRawData, duration, aiIsAmbiguous, msgId, aiMetadata };
        } catch (error) {
            console.error('ChatService error:', error);
            if (onError) onError(error.message);
            throw error;
        }
    }

    static _prepareBody(text, file, collectionName) {
        const isPerfMode = localStorage.getItem('dodo_performance_mode') === 'true';

        if (!file) {
            return JSON.stringify({ 
                message: text, 
                collectionName,
                isTestPerformance: isPerfMode
            });
        }
        const formData = new FormData();
        formData.append('message', text);
        formData.append('file', file);
        if (collectionName) formData.append('collectionName', collectionName);
        formData.append('isTestPerformance', isPerfMode);
        return formData;
    }

    static _saveToHistory(content, steps, suggestions, downloadUrl, downloadFileName, rawData, duration, isAmbiguous = false, msgId = null, metadata = null) {
        if (!content && steps.length === 0) return;
        
        state.addMessageToHistory(state.currentConversationId, { 
            id: msgId,
            role: 'ai', 
            content, 
            steps, 
            suggestions, 
            downloadUrl,
            downloadFileName,
            rawData,
            duration,
            isAmbiguous,
            metadata
        });
    }

    static ensureConversationStarted(title) {
        if (!state.currentConversationId) {
            const newId = Date.now();
            state.currentConversationId = newId;
            const now = new Date();
            const dateStr = `${now.getDate()}/${now.getMonth() + 1}/${now.getFullYear()} ${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`;
            state.chatHistory = [
                { 
                    id: newId, 
                    title: title || "Cuộc trò chuyện mới", 
                    date: dateStr,
                    messages: []
                },
                ...state.chatHistory
            ];
        }
    }
}
