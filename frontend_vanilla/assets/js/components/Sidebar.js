// Sidebar.js - Sidebar & History management
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
        
        const cleanBtn = document.getElementById('clean-storage');
        if (cleanBtn) cleanBtn.addEventListener('click', () => this.handleStorageCleanup());

        // Đăng ký và xử lý checkbox Fast-path
        const fastpathCheckbox = document.getElementById('fastpath-checkbox');
        if (fastpathCheckbox) {
            fastpathCheckbox.checked = state.isFastPathEnabled;
            fastpathCheckbox.addEventListener('change', (e) => {
                state.isFastPathEnabled = e.target.checked;
                if (window.app && window.app.toast) {
                    window.app.toast.success(
                        e.target.checked 
                            ? "Đã kích hoạt chế độ Fast-path siêu tốc!" 
                            : "Đã tắt chế độ Fast-path!"
                    );
                }
            });
        }

        // Đăng ký và xử lý checkbox Rules
        const rulesCheckbox = document.getElementById('rules-checkbox');
        if (rulesCheckbox) {
            rulesCheckbox.checked = state.isRulesEnabled;
            rulesCheckbox.addEventListener('change', (e) => {
                state.isRulesEnabled = e.target.checked;
                if (window.app && window.app.toast) {
                    window.app.toast.success(
                        e.target.checked 
                            ? "Đã hiển thị phần trích xuất quy tắc CSDL!" 
                            : "Đã tắt phần trích xuất quy tắc CSDL!"
                    );
                }
            });
        }

        // Khởi tạo trạng thái ban đầu từ State
        this.applySidebarState();

        // Listen for history changes
        state.subscribe((key, value) => {
            if (key === 'chatHistory') {
                this.renderHistory();
                this.updateStorageEstimation();
            }
            if (key === 'currentConversationId') {
                this.updateActiveHistoryItem(value);
            }
            if (key === 'isSidebarOpen') this.applySidebarState();
            if (key === 'isFastPathEnabled' && fastpathCheckbox) {
                fastpathCheckbox.checked = value;
            }
            if (key === 'isRulesEnabled' && rulesCheckbox) {
                rulesCheckbox.checked = value;
            }
        });

        this.renderHistory();
        this.updateStorageEstimation();
        
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
        const currentId = state.currentConversationId;

        if (history.length > 0) {
            this.historyContainer.innerHTML = history.map(item => {
                const isActive = String(item.id) === String(currentId);
                const formattedDate = this.formatDate(item.date, item.id);
                return `
                    <div class="history-item animate-fade-in ${isActive ? 'history-item--active' : ''}" data-id="${item.id}">
                        <i class="ph-duotone ph-chat-circle-dots"></i>
                        <div class="history-info">
                            <div class="history-title">${item.title}</div>
                            <div class="history-date">${formattedDate}</div>
                        </div>
                        <button class="history-delete" onclick="event.stopPropagation(); deleteHistory(${item.id})">
                            <i class="ph-bold ph-trash"></i>
                        </button>
                    </div>
                `;
            }).join('');
            
            this.bindItemClicks();
        } else {
            this.historyContainer.innerHTML = '<div class="empty-state">Chưa có cuộc trò chuyện nào</div>';
        }
    }

    bindItemClicks() {
        this.historyContainer.querySelectorAll('.history-item').forEach(el => {
            el.addEventListener('click', () => {
                const id = el.getAttribute('data-id');
                if (window.app && window.app.chatArea) {
                    window.app.chatArea.loadConversation(id);
                }
            });
        });
    }

    updateActiveHistoryItem(activeId) {
        if (!this.historyContainer) return;
        this.historyContainer.querySelectorAll('.history-item').forEach(el => {
            const id = el.getAttribute('data-id');
            const isActive = String(id) === String(activeId);
            if (isActive) {
                el.classList.add('history-item--active');
            } else {
                el.classList.remove('history-item--active');
            }
        });
    }

    handleDelete(id) {
        state.chatHistory = state.chatHistory.filter(item => String(item.id) !== String(id));
    }

    async handleStorageCleanup() {
        const choice = confirm("Hệ thống sẽ dọn dẹp dữ liệu nặng (JSON, Traces) để web chạy mượt hơn. \n\n- Nhấn OK để TỐI ƯU HÓA (vẫn giữ lại tin nhắn văn bản). \n- Nhấn CANCEL để thoát.");
        
        if (choice) {
            const success = state.optimizeHistory();
            if (success) {
                if (window.app && window.app.toast) {
                    window.app.toast.success("Đã tối ưu hóa bộ nhớ thành công!");
                } else {
                    alert("Đã tối ưu hóa bộ nhớ thành công!");
                }
                this.renderHistory(); // Refresh view
                this.updateStorageEstimation(); // Cập nhật lại dung lượng bộ nhớ
            }
        }
    }

    async updateStorageEstimation() {
        const usageEl = document.getElementById('storage-usage');
        const progressBar = document.getElementById('storage-progress-bar');
        const quotaEl = document.getElementById('storage-quota');

        if (!usageEl || !progressBar || !quotaEl) return;

        try {
            // Sử dụng API ước lượng bộ nhớ của trình duyệt
            if (navigator.storage && navigator.storage.estimate) {
                const estimate = await navigator.storage.estimate();
                const usedBytes = estimate.usage || 0;
                
                // Đặt hạn mức hiển thị tối ưu trực quan là 250 MB
                const limitMB = 250;
                const limitBytes = limitMB * 1024 * 1024;
                const usedMB = usedBytes / (1024 * 1024);
                
                // Cập nhật text hiển thị
                usageEl.textContent = `${usedMB.toFixed(2)} MB`;
                
                // Cập nhật phần trăm tiến độ
                const percent = Math.min((usedBytes / limitBytes) * 100, 100);
                progressBar.style.width = `${percent}%`;
                
                // Đổi màu sắc thanh tiến độ dựa trên mức độ đầy của bộ nhớ tối ưu
                if (percent > 80) {
                    progressBar.style.background = 'linear-gradient(90deg, #ef4444 0%, #b91c1c 100%)'; // Đỏ khi sắp đầy
                } else if (percent > 50) {
                    progressBar.style.background = 'linear-gradient(90deg, #f59e0b 0%, #d97706 100%)'; // Cam
                } else {
                    progressBar.style.background = 'linear-gradient(90deg, var(--primary) 0%, #10b981 100%)'; // Xanh lá/Tím
                }
                
                quotaEl.textContent = `Tối đa: ${limitMB} MB`;
            } else {
                // Phương án dự phòng cho trình duyệt cũ
                const historyStr = JSON.stringify(state.chatHistory);
                const usedBytes = new Blob([historyStr]).size;
                const limitMB = 50;
                const limitBytes = limitMB * 1024 * 1024;
                const usedMB = usedBytes / (1024 * 1024);
                
                usageEl.textContent = `${usedMB.toFixed(2)} MB`;
                const percent = Math.min((usedBytes / limitBytes) * 100, 100);
                progressBar.style.width = `${percent}%`;
                quotaEl.textContent = `Tối đa: ${limitMB} MB`;
            }
        } catch (err) {
            console.error('Failed to estimate storage usage:', err);
        }
    }

    formatDate(dateStr, convId) {
        const timestamp = Number(convId);
        let datePart = dateStr || '';
        let timePart = '';
        
        if (!isNaN(timestamp) && timestamp > 1000000000000) {
            const d = new Date(timestamp);
            const day = d.getDate();
            const month = d.getMonth() + 1;
            const year = d.getFullYear();
            const hours = String(d.getHours()).padStart(2, '0');
            const minutes = String(d.getMinutes()).padStart(2, '0');
            datePart = `${day}/${month}/${year}`;
            timePart = `${hours}:${minutes}`;
        } else if (dateStr && dateStr.includes(' ')) {
            const parts = dateStr.split(' ');
            datePart = parts[0];
            timePart = parts[1];
        }
        
        if (timePart) {
            return `<span class="history-date-day">${datePart}</span><span class="history-date-time">${timePart}</span>`;
        }
        return `<span class="history-date-day">${datePart}</span>`;
    }
}
