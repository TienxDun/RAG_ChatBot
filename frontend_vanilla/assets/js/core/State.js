// State.js - Simple State Management with IndexedDB Support
import { StorageManager } from './Storage.js';

class State {
    constructor() {
        this._state = {
            theme: localStorage.getItem('theme') || 'light',
            isSidebarOpen: localStorage.getItem('sidebar_state') !== null 
                ? localStorage.getItem('sidebar_state') === 'open' 
                : window.innerWidth > 768,
            chatHistory: [], // Will be loaded from IndexedDB in init()
            selectedFiles: [],
            isUploading: false,
            currentConversationId: null,
            isBackendOnline: true
        };
        this._listeners = [];
        this._saveDebounceTimer = null;
    }

    /**
     * Khởi tạo State, tải dữ liệu từ IndexedDB
     */
    async init() {
        console.log('🔄 Initializing State...');
        
        // 1. Thử tải từ IndexedDB
        let history = await StorageManager.loadHistory();
        
        // 2. Nếu IndexedDB trống, kiểm tra migration từ localStorage
        if (history.length === 0) {
            const localHistory = localStorage.getItem('chat_history');
            if (localHistory) {
                console.log('📦 Migrating history from localStorage to IndexedDB...');
                try {
                    history = JSON.parse(localHistory);
                    await StorageManager.saveHistory(history);
                    // Sau khi migrate thành công, có thể xóa localStorage để tiết kiệm
                    // localStorage.removeItem('chat_history'); 
                } catch (e) {
                    console.error('Migration failed:', e);
                }
            }
        }

        this._state.chatHistory = history;
        this._notify('chatHistory', history);
        console.log(`✅ State initialized with ${history.length} conversations.`);
    }

    get theme() { return this._state.theme; }
    set theme(value) {
        this._state.theme = value;
        localStorage.setItem('theme', value);
        this._notify('theme', value);
    }

    get isSidebarOpen() { return this._state.isSidebarOpen; }
    set isSidebarOpen(value) {
        this._state.isSidebarOpen = value;
        localStorage.setItem('sidebar_state', value ? 'open' : 'closed');
        this._notify('isSidebarOpen', value);
    }

    get chatHistory() { return this._state.chatHistory; }
    set chatHistory(value) {
        this._state.chatHistory = value;
        // Save to IndexedDB in background
        StorageManager.saveHistory(value).catch(err => {
            console.error('Failed to save history to IndexedDB:', err);
        });
        this._notify('chatHistory', value);
    }

    get currentConversationId() { return this._state.currentConversationId; }
    set currentConversationId(value) {
        this._state.currentConversationId = value;
        this._notify('currentConversationId', value);
    }

    addMessageToHistory(id, message) {
        const history = [...this._state.chatHistory];
        const index = history.findIndex(h => String(h.id) === String(id));
        if (index !== -1) {
            if (!history[index].messages) history[index].messages = [];
            history[index].messages.push(message);
            this._state.chatHistory = history;
            this._notify('chatHistory', history);
            this.saveConversationDebounced(id);
        }
    }

    updateHistoryItem(id, updateData) {
        const history = [...this._state.chatHistory];
        const index = history.findIndex(h => String(h.id) === String(id));
        if (index !== -1) {
            history[index] = { ...history[index], ...updateData };
            this._state.chatHistory = history;
            this._notify('chatHistory', history);
            this.saveConversationDebounced(id);
        }
    }

    saveConversationDebounced(convId) {
        if (this._saveDebounceTimer) {
            clearTimeout(this._saveDebounceTimer);
        }
        this._saveDebounceTimer = setTimeout(() => {
            const history = this._state.chatHistory;
            const conv = history.find(h => String(h.id) === String(convId));
            if (conv) {
                StorageManager.saveConversation(conv).catch(err => {
                    console.error('Failed to save conversation to IndexedDB:', err);
                });
            }
            this._saveDebounceTimer = null;
        }, 1500); // Trì hoãn 1.5 giây sau lần cập nhật cuối cùng
    }

    get isBackendOnline() { return this._state.isBackendOnline; }
    set isBackendOnline(value) {
        this._state.isBackendOnline = value;
        this._notify('isBackendOnline', value);
    }

        // isFastPathEnabled đã bị loại bỏ



    get selectedFiles() { return this._state.selectedFiles; }
    set selectedFiles(value) {
        this._state.selectedFiles = value;
        this._notify('selectedFiles', value);
    }

    get isUploading() { return this._state.isUploading; }
    set isUploading(value) {
        this._state.isUploading = value;
        this._notify('isUploading', value);
    }

    clearAllHistory() {
        this.chatHistory = [];
        this.currentConversationId = null;
        localStorage.removeItem('chat_history');
        // IndexedDB is cleared via setter calling StorageManager.saveHistory([])
        console.log('History cleared by user.');
    }

    optimizeHistory() {
        const optimized = this.chatHistory.map(conv => ({
            ...conv,
            messages: conv.messages ? conv.messages.map(msg => {
                const { rawData, steps, ...lightMsg } = msg;
                return lightMsg;
            }) : []
        }));
        this.chatHistory = optimized;
        console.log('History optimized by user.');
        return true;
    }

    subscribe(callback) {
        this._listeners.push(callback);
    }

    _notify(key, value) {
        this._listeners.forEach(callback => callback(key, value, this._state));
    }
}

export const state = new State();
