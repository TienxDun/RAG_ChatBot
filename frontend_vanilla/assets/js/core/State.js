// State.js - Simple State Management

class State {
    constructor() {
        this._state = {
            theme: localStorage.getItem('theme') || 'light',
            isSidebarOpen: localStorage.getItem('sidebar_state') !== null 
                ? localStorage.getItem('sidebar_state') === 'open' 
                : window.innerWidth > 768,
            chatHistory: JSON.parse(localStorage.getItem('chat_history')) || [],
            selectedFiles: [],
            isUploading: false,
            currentConversationId: null,
            isBackendOnline: true
        };
        this._listeners = [];
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
        localStorage.setItem('chat_history', JSON.stringify(value));
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
            this.chatHistory = history; // Trigger setter to save
        }
    }

    updateHistoryItem(id, updateData) {
        const history = [...this._state.chatHistory];
        const index = history.findIndex(h => String(h.id) === String(id));
        if (index !== -1) {
            history[index] = { ...history[index], ...updateData };
            this.chatHistory = history;
        }
    }

    get isBackendOnline() { return this._state.isBackendOnline; }
    set isBackendOnline(value) {
        this._state.isBackendOnline = value;
        this._notify('isBackendOnline', value);
    }

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

    subscribe(callback) {
        this._listeners.push(callback);
    }

    _notify(key, value) {
        this._listeners.forEach(callback => callback(key, value, this._state));
    }
}

export const state = new State();
