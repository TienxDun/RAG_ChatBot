/* State.js - Simple State Management */

class State {
    constructor() {
        this._state = {
            theme: localStorage.getItem('theme') || 'light',
            isSidebarOpen: localStorage.getItem('sidebar_state') !== null 
                ? localStorage.getItem('sidebar_state') === 'open' 
                : window.innerWidth > 768,
            chatHistory: [
                { id: 1, title: "hiện tại mấy giờ", date: "5/5/2026" },
                { id: 2, title: "Tổng số nhân viên", date: "5/5/2026" },
                { id: 3, title: "Cách sử dụng Qdrant cho người mới", date: "4/23/2026" },
                { id: 4, title: "tiến độ hoàn thành dự án", date: "4/23/2026" }
            ],
            selectedFiles: [],
            isUploading: false,
            currentConversationId: null
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
        this._notify('chatHistory', value);
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
