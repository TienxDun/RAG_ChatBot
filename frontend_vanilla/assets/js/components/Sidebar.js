/* Sidebar.js - Sidebar & History logic */
import { state } from '../core/State.js';
import { SELECTORS } from '../core/Config.js';

export class SidebarComponent {
    constructor() {
        this.sidebar = document.querySelector(SELECTORS.SIDEBAR);
        this.overlay = document.querySelector(SELECTORS.SIDEBAR_OVERLAY);
        this.openBtn = document.querySelector(SELECTORS.OPEN_SIDEBAR);
        this.closeBtn = document.querySelector(SELECTORS.CLOSE_SIDEBAR);
        this.appContainer = document.querySelector(SELECTORS.APP_CONTAINER);
        this.historyContainer = document.querySelector(SELECTORS.CHAT_HISTORY);
        
        this.init();
    }

    init() {
        if (this.openBtn) this.openBtn.addEventListener('click', () => this.toggleSidebar());
        if (this.closeBtn) this.closeBtn.addEventListener('click', () => this.toggleSidebar());
        if (this.overlay) this.overlay.addEventListener('click', () => this.toggleSidebar());

        // Khởi tạo trạng thái ban đầu từ State
        this.applySidebarState();

        // Listen for history changes
        state.subscribe((key, value) => {
            if (key === 'chatHistory') this.renderHistory();
            if (key === 'isSidebarOpen') this.applySidebarState();
        });

        this.renderHistory();
        
        // Handle global click for delete (delegation)
        window.deleteHistory = (id) => this.handleDelete(id);
    }

    toggleSidebar() {
        state.isSidebarOpen = !state.isSidebarOpen;
    }

    applySidebarState() {
        const isOpen = state.isSidebarOpen;
        const isDesktop = window.innerWidth > 768;

        if (isDesktop) {
            if (isOpen) {
                this.sidebar.classList.remove('sidebar--collapsed');
                this.appContainer.classList.remove('app-container--expanded');
            } else {
                this.sidebar.classList.add('sidebar--collapsed');
                this.appContainer.classList.add('app-container--expanded');
            }
            if (this.overlay) this.overlay.style.display = 'none';
        } else {
            if (isOpen) {
                this.sidebar.classList.add('active');
                if (this.overlay) this.overlay.style.display = 'block';
            } else {
                this.sidebar.classList.remove('active');
                if (this.overlay) this.overlay.style.display = 'none';
            }
        }
    }

    renderHistory() {
        if (!this.historyContainer) return;
        const history = state.chatHistory;

        if (history.length > 0) {
            this.historyContainer.innerHTML = history.map(item => `
                <div class="history-item animate-fade-in" data-id="${item.id}">
                    <i class="ph-duotone ph-chat-circle-dots"></i>
                    <div class="history-info">
                        <div class="history-title">${item.title}</div>
                        <div class="history-date">${item.date}</div>
                    </div>
                    <button class="history-delete" onclick="event.stopPropagation(); deleteHistory(${item.id})">
                        <i class="ph-duotone ph-trash"></i>
                    </button>
                </div>
            `).join('');
            
            this.bindItemClicks();
        } else {
            this.historyContainer.innerHTML = '<div class="empty-state">Chưa có cuộc trò chuyện nào</div>';
        }
    }

    bindItemClicks() {
        this.historyContainer.querySelectorAll('.history-item').forEach(el => {
            el.addEventListener('click', () => {
                const id = el.getAttribute('data-id');
                const chat = state.chatHistory.find(h => h.id == id);
                if (chat) alert(`Chuyển sang: ${chat.title}`);
            });
        });
    }

    handleDelete(id) {
        state.chatHistory = state.chatHistory.filter(item => item.id !== id);
    }
}
