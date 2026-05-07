/* Config.js - Central configuration and constants */

export const CONFIG = {
    API_BASE_URL: 'http://localhost:5000/api', // Cấu hình gốc cho API
    VALID_FILE_EXTENSIONS: ['.pdf', '.txt', '.json'],
    MAX_FILE_SIZE_MB: 10,
    ANIMATION_SPEED: 200,
};

export const ENDPOINTS = {
    CHAT: `${CONFIG.API_BASE_URL}/chat`,
    UPLOAD: `${CONFIG.API_BASE_URL}/documents/upload`,
    EXPORT_EXCEL: `${CONFIG.API_BASE_URL}/chat/export-excel`,
    HEALTH: `${CONFIG.API_BASE_URL}/health`
};

export const SELECTORS = {
    HTML: 'html',
    SIDEBAR: '#sidebar',
    SIDEBAR_OVERLAY: '#sidebar-overlay',
    OPEN_SIDEBAR: '#open-sidebar',
    CLOSE_SIDEBAR: '#close-sidebar',
    THEME_TOGGLE: '#theme-toggle',
    CHAT_INPUT: '#chat-input',
    CHAT_AREA: '#chat-area',
    SCROLL_TOP: '#scroll-top',
    CHAT_HISTORY: '#chat-history',
    APP_CONTAINER: '.app-container',
    
    // Actions
    HEADER_NEW_CHAT: '#header-new-chat',
    NEW_CHAT: '#new-chat',
    MIC_BTN: '#mic-btn',
    MIC_RIPPLE: '#mic-ripple',
    VOICE_VISUALIZER: '#voice-visualizer',
    SEND_BTN: '#send-btn',
    
    // Modal
    OPEN_UPLOAD: '#open-upload',
    UPLOAD_MODAL: '#upload-modal',
    CLOSE_MODAL: '#close-modal',
    CANCEL_UPLOAD: '#cancel-upload',
    START_UPLOAD: '#start-upload',
    DROPZONE: '#dropzone',
    FILE_INPUT: '#file-input',
    FILE_LIST: '#file-list',
    PROGRESS_BAR: '#progress-bar',
    PROGRESS_PERCENT: '#progress-percent',
    PROGRESS_STATUS: '#progress-status',
    UPLOAD_PROGRESS_CONTAINER: '#upload-progress-container',
    MODAL_INFO_TEXT: '#modal-info-text'
};
