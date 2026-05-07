/* Toast.js - Logic for displaying UI notifications */

export class Toast {
    static container = null;

    static init() {
        if (this.container) return;
        
        this.container = document.createElement('div');
        this.container.className = 'toast-container';
        document.body.appendChild(this.container);
    }

    /**
     * Show a toast notification
     * @param {Object} options - { title, message, type: 'success'|'error'|'info'|'warning', duration: 3000 }
     */
    static show({ title, message, type = 'info', duration = 3000 }) {
        this.init();

        const toast = document.createElement('div');
        toast.className = `toast toast-${type}`;
        
        const icons = {
            success: 'ph-duotone ph-check-circle',
            error: 'ph-duotone ph-warning-circle',
            info: 'ph-duotone ph-info',
            warning: 'ph-duotone ph-warning'
        };

        toast.innerHTML = `
            <div class="toast-icon">
                <i class="${icons[type] || icons.info}"></i>
            </div>
            <div class="toast-content">
                ${title ? `<div class="toast-title">${title}</div>` : ''}
                <div class="toast-message">${message}</div>
            </div>
            <button class="toast-close">
                <i class="ph-bold ph-x"></i>
            </button>
            <div class="toast-progress">
                <div class="toast-progress-bar" style="animation-duration: ${duration}ms"></div>
            </div>
        `;

        this.container.appendChild(toast);

        // Animation in
        setTimeout(() => toast.classList.add('show'), 10);

        // Auto hide
        const timer = setTimeout(() => this.hide(toast), duration);

        // Close button
        toast.querySelector('.toast-close').onclick = () => {
            clearTimeout(timer);
            this.hide(toast);
        };
    }

    static hide(toast) {
        toast.classList.add('hide');
        toast.addEventListener('transitionend', () => {
            toast.remove();
        });
    }

    // Helper methods
    static success(message, title = 'Thành công') {
        this.show({ title, message, type: 'success' });
    }

    static error(message, title = 'Lỗi') {
        this.show({ title, message, type: 'error' });
    }

    static info(message, title = 'Thông báo') {
        this.show({ title, message, type: 'info' });
    }

    static warning(message, title = 'Cảnh báo') {
        this.show({ title, message, type: 'warning' });
    }
}
