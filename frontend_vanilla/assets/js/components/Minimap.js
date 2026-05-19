/**
 * Minimap Component - Chat Navigation Timeline
 * Quản lý giao diện Minimap bên phải màn hình để theo dõi và cuộn nhanh các câu hỏi.
 */
export default class Minimap {
    /**
     * @param {HTMLElement} minimapEl - Container của Minimap (#chat-minimap)
     * @param {HTMLElement} chatAreaEl - Container cuộn của chat (.chat-area)
     * @param {HTMLElement} messagesListEl - Vùng chứa các tin nhắn (.messages-list)
     */
    constructor(minimapEl, chatAreaEl, messagesListEl) {
        this.minimap = minimapEl;
        this.chatArea = chatAreaEl;
        this.messagesList = messagesListEl;
        this.userMessages = [];
        
        this.init();
    }

    init() {
        if (!this.minimap || !this.chatArea || !this.messagesList) return;
        
        // Đăng ký sự kiện click
        this.minimap.addEventListener('click', (e) => this._handleMinimapClick(e));
        
        // Đăng ký sự kiện cuộn với cơ chế Debounce nhẹ để tối ưu hiệu năng
        let scrollTimeout;
        this.chatArea.addEventListener('scroll', () => {
            if (scrollTimeout) cancelAnimationFrame(scrollTimeout);
            scrollTimeout = requestAnimationFrame(() => this.syncScrollSpy());
        });
    }

    /**
     * Cập nhật danh sách câu hỏi từ DOM và vẽ lại Minimap
     */
    update() {
        if (!this.minimap || !this.messagesList) return;
        
        // Lấy tất cả tin nhắn của user
        this.userMessages = Array.from(this.messagesList.querySelectorAll('.message--user'));
        
        if (this.userMessages.length === 0) {
            this.minimap.classList.add('hidden');
            return;
        }

        this.minimap.classList.remove('hidden');
        
        let html = `<div class="minimap-header">CUỘC HỘI THOẠI</div>`;
        
        this.userMessages.forEach((msg, index) => {
            // Trích xuất văn bản câu hỏi
            const bubbleEl = msg.querySelector('.markdown-content');
            let questionText = bubbleEl ? bubbleEl.innerText.trim() : `Câu hỏi ${index + 1}`;
            
            // Cắt ngắn nếu quá dài
            if (questionText.length > 22) {
                questionText = questionText.substring(0, 20) + '...';
            }
            
            html += `
                <div class="minimap-item" data-index="${index}" title="${bubbleEl ? bubbleEl.innerText.trim() : ''}">
                    <span class="minimap-item__text">${questionText}</span>
                    <span class="minimap-item__line"></span>
                </div>
            `;
        });
        
        this.minimap.innerHTML = html;
        
        // Đồng bộ hóa trạng thái active ngay sau khi vẽ lại
        this.syncScrollSpy();
    }

    /**
     * Xử lý sự kiện click vào các mục trên Minimap
     */
    _handleMinimapClick(e) {
        const item = e.target.closest('.minimap-item');
        if (!item) return;
        
        const index = parseInt(item.getAttribute('data-index'), 10);
        const targetMessage = this.userMessages[index];
        
        if (targetMessage) {
            // Cuộn mượt mà đưa câu hỏi lên đầu khung nhìn
            targetMessage.scrollIntoView({ behavior: 'smooth', block: 'start' });
            
            // Cập nhật active class tức thì
            this._setActiveIndex(index);
        }
    }

    /**
     * Thuật toán ScrollSpy - Xác định tin nhắn nào đang ở tâm màn hình nhất để làm sáng vạch tương ứng
     */
    syncScrollSpy() {
        if (this.userMessages.length === 0) return;
        
        const containerRect = this.chatArea.getBoundingClientRect();
        const containerCenter = containerRect.top + containerRect.height / 2;
        
        let activeIndex = 0;
        let minDistance = Infinity;
        
        this.userMessages.forEach((msg, index) => {
            const rect = msg.getBoundingClientRect();
            const msgCenter = rect.top + rect.height / 2;
            const distance = Math.abs(msgCenter - containerCenter);
            
            if (distance < minDistance) {
                minDistance = distance;
                activeIndex = index;
            }
        });
        
        this._setActiveIndex(activeIndex);
    }

    /**
     * Đặt class active cho vạch tương ứng
     */
    _setActiveIndex(activeIndex) {
        const items = this.minimap.querySelectorAll('.minimap-item');
        items.forEach((item, index) => {
            if (index === activeIndex) {
                item.classList.add('active');
            } else {
                item.classList.remove('active');
            }
        });
    }

    /**
     * Ẩn hoặc Hiện Minimap tùy thuộc vào trạng thái Landing
     */
    toggleVisibility(isLanding) {
        if (isLanding || this.userMessages.length === 0) {
            this.minimap.classList.add('hidden');
        } else {
            this.minimap.classList.remove('hidden');
        }
    }
}
