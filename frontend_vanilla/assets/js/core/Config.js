import { env } from '../env.js';

// Config.js - Central configuration and constants

export const CONFIG = {
    API_BASE_URL: env.API_BASE_URL, // Lấy từ biến môi trường
    VALID_FILE_EXTENSIONS: ['.pdf', '.txt', '.json'],
    MAX_FILE_SIZE_MB: 10,
    ANIMATION_SPEED: 200,
};

export const ENDPOINTS = {
    CHAT: '/chat',
    UPLOAD: '/documents/upload',
    EXPORT_EXCEL: '/chat/export-excel',
    HEALTH: '/health',
    COLLECTIONS: '/documents/collections'
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
    LANDING_SUGGESTIONS: '.suggestions',
    MIC_BTN: '#mic-btn',
    MIC_RIPPLE: '#mic-ripple',
    VOICE_VISUALIZER: '#voice-visualizer',
    SEND_BTN: '#send-btn',
    ATTACH_BTN: '#attach-btn',
    CHAT_FILE: '#chat-file',
    FILE_PREVIEW_CONTAINER: '#file-preview-container',
    
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
