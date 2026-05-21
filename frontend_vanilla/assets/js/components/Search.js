// Search.js - Message Search component with fuzzy search and auto-scroll/highlight
import { state } from '../core/State.js';

export class SearchComponent {
    constructor() {
        this.elements = {
            searchSidebar: document.getElementById('search-sidebar'),
            openSearchBtn: document.getElementById('open-search'),
            closeSearchBtn: document.getElementById('close-search'),
            searchInput: document.getElementById('search-input'),
            clearSearchBtn: document.getElementById('clear-search'),
            searchResults: document.getElementById('search-results'),
            sidebarOverlay: document.getElementById('sidebar-overlay')
        };

        this.debounceTimer = null;
        this.init();
    }

    init() {
        const { openSearchBtn, closeSearchBtn, searchInput, clearSearchBtn, sidebarOverlay } = this.elements;

        if (openSearchBtn) {
            openSearchBtn.addEventListener('click', () => this.openSearch());
        }

        if (closeSearchBtn) {
            closeSearchBtn.addEventListener('click', () => this.closeSearch());
        }

        if (sidebarOverlay) {
            // Đóng cả hai sidebar khi click vào overlay
            sidebarOverlay.addEventListener('click', () => this.closeSearch());
        }

        // Đóng sidebar khi click ra ngoài vùng sidebar tìm kiếm
        document.addEventListener('click', (e) => {
            const { searchSidebar, openSearchBtn } = this.elements;
            if (!searchSidebar) return;

            const isActive = searchSidebar.classList.contains('sidebar--active') || searchSidebar.classList.contains('active');
            if (isActive) {
                const isClickInside = searchSidebar.contains(e.target);
                const isClickOnOpenBtn = openSearchBtn && openSearchBtn.contains(e.target);

                if (!isClickInside && !isClickOnOpenBtn) {
                    this.closeSearch();
                }
            }
        });

        if (searchInput) {
            searchInput.addEventListener('input', () => {
                if (clearSearchBtn) {
                    if (searchInput.value.length > 0) {
                        clearSearchBtn.classList.remove('hidden');
                    } else {
                        clearSearchBtn.classList.add('hidden');
                    }
                }
                clearTimeout(this.debounceTimer);
                this.debounceTimer = setTimeout(() => this.performSearch(), 250);
            });
        }

        if (clearSearchBtn && searchInput) {
            clearSearchBtn.addEventListener('click', () => {
                searchInput.value = '';
                clearSearchBtn.classList.add('hidden');
                this.performSearch();
                searchInput.focus();
            });
        }
    }

    openSearch() {
        const { searchSidebar, sidebarOverlay, searchInput, clearSearchBtn } = this.elements;
        if (!searchSidebar) return;

        searchSidebar.classList.add('sidebar--active');
        searchSidebar.classList.add('active'); // mobile support

        if (sidebarOverlay && window.innerWidth <= 768) {
            sidebarOverlay.style.display = 'block';
        }

        // Đồng bộ trạng thái của nút xóa nhanh
        if (searchInput && clearSearchBtn) {
            if (searchInput.value.length > 0) {
                clearSearchBtn.classList.remove('hidden');
            } else {
                clearSearchBtn.classList.add('hidden');
            }
        }

        // Tự động focus vào ô tìm kiếm
        if (searchInput) {
            setTimeout(() => searchInput.focus(), 300);
        }

        // Đóng sidebar trái (nếu đang mở trên mobile)
        if (window.innerWidth <= 768 && state.isSidebarOpen) {
            state.isSidebarOpen = false;
        }
    }

    closeSearch() {
        const { searchSidebar, sidebarOverlay } = this.elements;
        if (!searchSidebar) return;

        searchSidebar.classList.remove('sidebar--active');
        searchSidebar.classList.remove('active'); // mobile support

        if (sidebarOverlay && window.innerWidth <= 768) {
            sidebarOverlay.style.display = 'none';
        }
    }

    performSearch() {
        const { searchInput, searchResults } = this.elements;
        if (!searchInput || !searchResults) return;

        const keyword = searchInput.value.trim();
        if (keyword.length === 0) {
            searchResults.innerHTML = `
                <div class="empty-state animate-fade-in">
                    <i class="ph-duotone ph-magnifying-glass empty-state__icon"></i>
                    <div class="empty-state__title">Tìm kiếm tin nhắn</div>
                    <div class="empty-state__description">Nhập từ khóa để tìm lại tin nhắn trong lịch sử trò chuyện cục bộ.</div>
                </div>
            `;
            return;
        }

        if (keyword.length < 2) {
            searchResults.innerHTML = `
                <div class="empty-state animate-fade-in">
                    <i class="ph-duotone ph-text-cursor empty-state__icon empty-state__icon--typing"></i>
                    <div class="empty-state__title">Đang nhập...</div>
                    <div class="empty-state__description">Nhập ít nhất 2 ký tự để bắt đầu tìm kiếm.</div>
                </div>
            `;
            return;
        }

        const cleanKeyword = this.removeVietnameseTones(keyword);
        const results = [];

        state.chatHistory.forEach(conv => {
            if (!conv.messages || conv.messages.length === 0) return;

            conv.messages.forEach((msg, idx) => {
                if (!msg.content) return;

                const plainContent = this.stripMarkdown(msg.content);
                const cleanContent = this.removeVietnameseTones(plainContent);
                if (cleanContent.includes(cleanKeyword)) {
                    results.push({
                        conversationId: conv.id,
                        conversationTitle: conv.title,
                        conversationDate: conv.date,
                        messageIndex: idx,
                        role: msg.role,
                        content: plainContent
                    });
                }
            });
        });

        results.sort((a, b) => {
            const convA = Number(a.conversationId);
            const convB = Number(b.conversationId);
            if (convA !== convB) return convA - convB;
            return a.messageIndex - b.messageIndex;
        });

        this.renderResults(results, keyword);
    }

    renderResults(results, keyword) {
        const { searchResults } = this.elements;
        if (!searchResults) return;

        if (results.length === 0) {
            searchResults.innerHTML = `
                <div class="empty-state animate-fade-in">
                    <i class="ph-duotone ph-smiley-sad empty-state__icon"></i>
                    <div class="empty-state__title">Không tìm thấy kết quả</div>
                    <div class="empty-state__description">Không tìm thấy tin nhắn nào chứa từ khóa "${this.escapeHtml(keyword)}".</div>
                </div>
            `;
            return;
        }

        const regex = this.makeFuzzyRegex(keyword);

        searchResults.innerHTML = results.map(res => {
            const roleLabel = res.role === 'user' ? 'Bạn' : 'DODO AI';
            const roleClass = res.role === 'user' ? 'user' : 'assistant';
            const snippet = this.getSearchSnippet(res.content, regex);

            return `
                <div class="search-result-item animate-fade-in" 
                     data-conv-id="${res.conversationId}" 
                     data-msg-idx="${res.messageIndex}">
                    <div class="search-result-header">
                        <span class="search-result-role ${roleClass}">${roleLabel}</span>
                        <span class="search-result-date" title="${res.conversationDate}">${this.formatDate(res.conversationDate, res.conversationId)}</span>
                    </div>
                    <div class="search-result-snippet">${snippet}</div>
                    <div class="search-result-meta">
                        <i class="ph-bold ph-chat-circle-dots"></i>
                        <span class="search-result-meta-title">${this.escapeHtml(res.conversationTitle)}</span>
                    </div>
                </div>
            `;
        }).join('');

        this.bindResultClicks(keyword);
    }

    bindResultClicks(keyword) {
        const { searchResults } = this.elements;
        if (!searchResults) return;

        searchResults.querySelectorAll('.search-result-item').forEach(item => {
            item.addEventListener('click', () => {
                const convId = item.getAttribute('data-conv-id');
                const msgIdx = parseInt(item.getAttribute('data-msg-idx'), 10);

                this.navigateToMessage(convId, msgIdx, keyword);
            });
        });
    }

    navigateToMessage(convId, msgIdx, keyword) {
        if (!window.app || !window.app.chatArea) return;

        // 1. Tải cuộc trò chuyện
        window.app.chatArea.loadConversation(convId);

        // 2. Tìm phần tử tin nhắn trong DOM
        const targetSelector = `.message[data-msg-index="${msgIdx}"]`;
        
        // Đợi một khoảng thời gian cực ngắn để bảo đảm DOM đã cập nhật xong
        setTimeout(() => {
            const msgEl = document.querySelector(targetSelector);
            if (!msgEl) {
                console.warn(`Message element with index ${msgIdx} not found in DOM.`);
                return;
            }

            // 3. Cuộn đến phần tử đó
            msgEl.scrollIntoView({ behavior: 'smooth', block: 'center' });

            // 4. Làm nổi bật tin nhắn
            const bubbleEl = msgEl.querySelector('.message__bubble');
            if (bubbleEl) {
                const originalHTML = bubbleEl.innerHTML;

                // Tô sáng từ khóa
                this.highlightTextNodes(bubbleEl, keyword);
                msgEl.classList.add('focused-search-result');

                // Tự động khôi phục lại HTML ban đầu sau 3 giây để tránh làm hỏng cấu trúc tin nhắn
                setTimeout(() => {
                    msgEl.classList.remove('focused-search-result');
                    bubbleEl.innerHTML = originalHTML;
                }, 3000);
            }

            // Đóng sidebar tìm kiếm trên thiết bị di động
            if (window.innerWidth <= 768) {
                this.closeSearch();
            }
        }, 100);
    }

    highlightTextNodes(element, keyword) {
        if (!keyword) return;
        const regex = this.makeFuzzyRegex(keyword);
        // Dùng bản regex không có flag global để test, tránh lỗi lastIndex của RegExp JS
        const testRegex = new RegExp(regex.source, 'i');

        const walk = document.createTreeWalker(element, NodeFilter.SHOW_TEXT, null, false);
        let node;
        const nodesToReplace = [];
        
        while (node = walk.nextNode()) {
            if (node.nodeValue.trim() && testRegex.test(node.nodeValue)) {
                nodesToReplace.push(node);
            }
        }

        nodesToReplace.forEach(textNode => {
            const parent = textNode.parentNode;
            if (!parent) return;
            
            // Bỏ qua các thẻ script, style hoặc các thẻ đã được highlight
            if (parent.classList.contains('message-highlight-match') || 
                parent.tagName === 'SCRIPT' || 
                parent.tagName === 'STYLE') {
                return;
            }

            const matches = textNode.nodeValue.split(regex);
            const fragment = document.createDocumentFragment();

            matches.forEach((text, idx) => {
                // Do regex có đúng 1 capture group bao quanh toàn bộ mẫu tìm kiếm,
                // mảng kết quả split sẽ xen kẽ giữa chuỗi không khớp và chuỗi khớp.
                // Các chuỗi khớp (capture group) luôn nằm ở chỉ số lẻ (1, 3, 5, ...).
                if (idx % 2 === 1) {
                    const span = document.createElement('span');
                    span.className = 'message-highlight-match';
                    span.textContent = text;
                    fragment.appendChild(span);
                } else if (text) {
                    fragment.appendChild(document.createTextNode(text));
                }
            });

            parent.replaceChild(fragment, textNode);
        });
    }

    getSearchSnippet(content, regex) {
        const cleanContent = this.removeVietnameseTones(content);
        const match = regex.exec(content);
        
        let matchIdx = -1;
        let matchLength = 0;
        if (match) {
            matchIdx = match.index;
            matchLength = match[0].length;
        }

        if (matchIdx === -1) {
            return this.escapeHtml(content.substring(0, 100)) + (content.length > 100 ? '...' : '');
        }

        const start = Math.max(0, matchIdx - 40);
        const end = Math.min(content.length, matchIdx + matchLength + 60);

        let snippet = content.substring(start, end);
        if (start > 0) snippet = '...' + snippet;
        if (end < content.length) snippet = snippet + '...';

        // Escaped HTML rồi highlight từ khóa khớp
        const escapedSnippet = this.escapeHtml(snippet);
        // Do regex có chứa capture group ở makeFuzzyRegex nên ta có thể dùng replace trực tiếp
        return escapedSnippet.replace(regex, '<span class="search-highlight">$1</span>');
    }

    stripMarkdown(text) {
        if (!text) return '';
        return text
            .replace(/```[\s\S]*?```/g, '') // Code blocks
            .replace(/`([^`]+)`/g, '$1') // Inline code
            .replace(/!\[\s*.*?\]\(\s*.*?\)/g, '') // Images
            .replace(/\[([\s\S]*?)\]\(\s*.*?\)/g, '$1') // Links
            .replace(/^\s{0,3}#{1,6}\s+(.+)$/gm, '$1') // Headings
            .replace(/([\*_]{1,3})(\S.*?\S)?\1/g, '$2') // Bold/Italic
            .replace(/^\s*>\s+(.+)$/gm, '$1') // Blockquotes
            .replace(/\|/g, ' ') // Tables dividers
            .replace(/^\s*[-:| ]+\s*$/gm, '') // Table header separators
            .replace(/\s+/g, ' ') // Whitespace normalization
            .trim();
    }

    removeVietnameseTones(str) {
        if (!str) return '';
        str = str.replace(/à|á|ạ|ả|ã|â|ầ|ấ|ậ|ẩ|ẫ|ă|ằ|ắ|ặ|ẳ|ẵ/g,"a"); 
        str = str.replace(/è|é|ẹ|ẻ|ẽ|ê|ề|ế|ệ|ể|ễ/g,"e"); 
        str = str.replace(/ì|í|ị|ỉ|ĩ/g,"i"); 
        str = str.replace(/ò|ó|ọ|ỏ|õ|ô|ồ|ố|ộ|ổ|ỗ|ơ|ờ|ớ|ợ|ở|ỡ/g,"o"); 
        str = str.replace(/ù|ú|ụ|ủ|ũ|ư|ừ|ứ|ự|ử|ữ/g,"u"); 
        str = str.replace(/ỳ|ý|ỵ|ỷ|ỹ/g,"y"); 
        str = str.replace(/đ/g,"d");
        str = str.replace(/À|Á|Ạ|Ả|Ã|Â|Ầ|Ấ|Ậ|Ẩ|Ẫ|Ă|Ằ|Ắ|Ặ|Ẳ|Ẵ/g, "A");
        str = str.replace(/È|É|Ẹ|Ẻ|Ẽ|Ê|Ề|Ế|Ệ|Ể|Ễ/g, "E");
        str = str.replace(/Ì|Í|Ị|Bỉ|Ĩ/g, "I");
        str = str.replace(/Ò|Ó|Ọ|Ỏ|Õ|Ô|Ồ|Ố|Ộ|Ổ|Ỗ|Ơ|Ờ|Ớ|Ợ|Ở|Ỡ/g, "O");
        str = str.replace(/Ù|Ú|Ụ|Ủ|Ũ|Ư|Ừ|Ứ|Ự|Ử|Ữ/g, "U");
        str = str.replace(/Ỳ|Ý|Ỵ|Ỷ|Ỹ/g, "Y");
        str = str.replace(/Đ/g, "D");
        return str.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase();
    }

    makeFuzzyRegex(keyword) {
        const escaped = this.escapeRegExp(keyword);
        let pattern = '';
        const charMap = {
            'a': '[aàáạảãâầấậẩẫăằắặẳẵAÀÁẠẢÃÂẦẤẬẨẪĂẰẮẶẲẴ]',
            'e': '[eèéẹẻẽêềếệểễEÈÉẸẺẼÊỀẾỆỂỄ]',
            'i': '[iìíịỉĩIÌÍỊỈĨ]',
            'o': '[oòóọỏõôồốộổỗơờớợởỡOÒÓỌỎÕÔỒỐỘỔỖƠỜỚỢỞỠ]',
            'u': '[uùúụủũưừứựửữUÙÚỤỦŨƯỪỨỰỬỮ]',
            'y': '[yỳýỵỷỹYỲÝỴỶỸ]',
            'd': '[dđDĐ]'
        };

        for (let char of escaped.toLowerCase()) {
            if (charMap[char]) {
                pattern += charMap[char];
            } else {
                pattern += char;
            }
        }
        return new RegExp(`(${pattern})`, 'gi');
    }

    escapeRegExp(string) {
        return string.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    }

    escapeHtml(text) {
        const map = {
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#039;'
        };
        return text.replace(/[&<>"']/g, m => map[m]);
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
            return `<span class="search-result-date-day">${datePart}</span><span class="search-result-date-time">${timePart}</span>`;
        }
        return `<span class="search-result-date-day">${datePart}</span>`;
    }
}
