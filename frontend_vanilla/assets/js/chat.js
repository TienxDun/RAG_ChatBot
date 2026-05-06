/* chat.js - Messaging & Advanced UI Logic */

document.addEventListener('DOMContentLoaded', () => {
    const chatInput = document.getElementById('chat-input');
    const sendBtn = document.getElementById('send-btn');
    const messagesList = document.getElementById('messages-list');
    const landingView = document.getElementById('landing-view');
    const chatArea = document.getElementById('chat-area');
    const newChatBtn = document.getElementById('new-chat');

    const state = {
        messages: [],
        isLoading: false
    };

    // --- Starfield Background Logic ---
    const initStarfield = () => {
        const canvas = document.getElementById('starfield');
        if (!canvas) return;
        const ctx = canvas.getContext('2d');
        let stars = [];
        const count = 200;

        const resize = () => {
            canvas.width = window.innerWidth;
            canvas.height = window.innerHeight;
        };

        window.addEventListener('resize', resize);
        resize();

        for (let i = 0; i < count; i++) {
            stars.push({
                x: Math.random() * canvas.width,
                y: Math.random() * canvas.height,
                size: Math.random() * 1.5,
                speed: Math.random() * 0.05 + 0.01
            });
        }

        const animate = () => {
            ctx.clearRect(0, 0, canvas.width, canvas.height);
            const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
            ctx.fillStyle = isDark ? 'rgba(255, 255, 255, 0.8)' : 'rgba(124, 58, 237, 0.4)';

            stars.forEach(star => {
                ctx.beginPath();
                ctx.arc(star.x, star.y, star.size, 0, Math.PI * 2);
                ctx.fill();

                star.y -= star.speed;
                if (star.y < 0) star.y = canvas.height;
            });
            requestAnimationFrame(animate);
        };
        animate();
    };

    initStarfield();

    // --- Utilities ---
    const copyToClipboard = (text, btn) => {
        navigator.clipboard.writeText(text).then(() => {
            const originalHtml = btn.innerHTML;
            btn.innerHTML = '<i class="ph ph-check-bold" style="color: #22c55e"></i> <span>Đã copy</span>';
            setTimeout(() => {
                btn.innerHTML = originalHtml;
            }, 2000);
        });
    };

    // --- Content Rendering Logic ---
    const renderMessageContent = (content) => {
        if (typeof marked !== 'undefined') {
            return marked.parse(content);
        }
        return content.replace(/\n/g, '<br>');
    };

    const renderRagSteps = (steps) => {
        if (!steps || steps.length === 0) return '';

        const stepsHtml = steps.map((step, idx) => `
            <div class="rag-step animate-fade-in" style="animation-delay: ${idx * 0.1}s">
                <div class="rag-step__dot"></div>
                <div class="rag-step__panel">
                    <div class="rag-step__title">
                        <i class="ph ph-lightning"></i>
                        ${step.title}
                    </div>
                    <div class="rag-step__content-inner text-sm opacity-80">
                        ${marked.parse(step.content)}
                    </div>
                </div>
            </div>
        `).join('');

        return `
            <div class="rag-steps">
                <button class="rag-steps__toggle" onclick="this.classList.toggle('active')">
                    <i class="ph ph-lightning-fill"></i>
                    <span>RAG TRACE (${steps.length} steps)</span>
                    <i class="ph ph-caret-down"></i>
                </button>
                <div class="rag-steps__content">
                    ${stepsHtml}
                </div>
            </div>
        `;
    };

    const appendMessage = (role, content, steps = [], suggestedQuestions = []) => {
        if (state.messages.length === 0) {
            landingView.classList.add('hidden');
            messagesList.classList.remove('hidden');
        }

        const msgObj = { role, content, steps, suggestedQuestions };
        state.messages.push(msgObj);

        const messageEl = document.createElement('div');
        messageEl.className = `message message--${role === 'user' ? 'user' : 'ai'} animate-slide-up`;
        
        let html = `
            <div class="message__bubble">
                <div class="markdown-content">
                    ${renderMessageContent(content)}
                </div>
        `;

        if (role === 'ai') {
            if (steps && steps.length > 0) {
                html += renderRagSteps(steps);
            }
            
            // AI Footer
            html += `
                <div class="message__footer">
                    <span class="ai-label">AI INSIGHT</span>
                    <div style="flex: 1"></div>
                    <button class="footer-copy" onclick="const text = this.closest('.message__bubble').querySelector('.markdown-content').innerText; navigator.clipboard.writeText(text); this.innerHTML='<i class=\'ph ph-check-bold\' style=\'color:#22c55e\'></i> Đã copy'; setTimeout(()=>this.innerHTML='<i class=\'ph ph-copy\'></i> Copy', 2000)">
                        <i class="ph ph-copy"></i> Copy
                    </button>
                </div>
            `;
        }

        html += `</div>`; // Close bubble

        // Action Buttons for User or AI
        if (role === 'user') {
            html += `
                <div class="message__actions">
                    <button class="action-btn" onclick="document.getElementById('chat-input').value = this.closest('.message').querySelector('.markdown-content').innerText; document.getElementById('chat-input').focus();" title="Sửa tin nhắn">
                        <i class="ph ph-pencil-simple"></i>
                    </button>
                    <button class="action-btn" onclick="navigator.clipboard.writeText(this.closest('.message').querySelector('.markdown-content').innerText)" title="Copy tin nhắn">
                        <i class="ph ph-copy"></i>
                    </button>
                </div>
            `;
        }

        messageEl.innerHTML = html;

        // Suggested Questions outside the bubble
        if (role === 'ai' && suggestedQuestions && suggestedQuestions.length > 0) {
            const suggestionsDiv = document.createElement('div');
            suggestionsDiv.className = 'suggestions-list';
            suggestionsDiv.innerHTML = suggestedQuestions.map(q => `
                <button class="suggestion-btn" onclick="document.getElementById('chat-input').value='${q}'; document.getElementById('send-btn').click();">
                    ${q}
                </button>
            `).join('');
            
            // Append suggestions after the bubble
            const container = document.createElement('div');
            container.style.display = 'flex';
            container.style.flexDirection = 'column';
            container.style.alignItems = 'flex-start';
            container.style.width = '100%';
            container.appendChild(messageEl.querySelector('.message__bubble'));
            container.appendChild(suggestionsDiv);
            
            messageEl.innerHTML = '';
            messageEl.appendChild(container);
            if (role === 'user') messageEl.style.flexDirection = 'row-reverse';
        }

        messagesList.appendChild(messageEl);
        scrollToBottom();
    };

    const scrollToBottom = () => {
        chatArea.scrollTo({
            top: chatArea.scrollHeight,
            behavior: 'smooth'
        });
    };

    const handleSend = async () => {
        const text = chatInput.value.trim();
        if (!text || state.isLoading) return;

        // User message
        appendMessage('user', text);
        chatInput.value = '';
        chatInput.style.height = 'auto';
        sendBtn.disabled = true;

        // Mock AI thinking
        state.isLoading = true;
        const typingEl = createTypingIndicator();
        messagesList.appendChild(typingEl);
        scrollToBottom();

        // Simulate API call
        setTimeout(() => {
            messagesList.removeChild(typingEl);
            
            const mockSteps = [
                { title: "Vectorization", content: "Câu hỏi đã được chuyển đổi thành vector 3072 chiều." },
                { title: "SQL Generation", content: "Đã tạo câu lệnh SQL thành công dựa trên cấu trúc database nhà máy." }
            ];
            const mockSuggestions = ["Cho tôi xem báo cáo doanh thu", "Ai là khách hàng lớn nhất?"];

            appendMessage('ai', `Dựa trên dữ liệu hệ thống, tôi đã phân tích yêu cầu của bạn về **${text}**. \n\n| Chỉ số | Giá trị | Trạng thái |\n| :--- | :--- | :--- |\n| Doanh thu | 1.2 tỷ | Tăng 15% |\n| Tồn kho | 4500 sp | Ổn định |`, mockSteps, mockSuggestions);
            state.isLoading = false;
        }, 2000);
    };

    const createTypingIndicator = () => {
        const div = document.createElement('div');
        div.className = 'message message--ai animate-fade-in';
        div.innerHTML = `
            <div class="message__bubble">
                <div class="typing">
                    <span></span><span></span><span></span>
                </div>
            </div>
        `;
        return div;
    };

    const resetChat = () => {
        state.messages = [];
        messagesList.innerHTML = '';
        messagesList.classList.add('hidden');
        landingView.classList.remove('hidden');
    };

    // --- Listeners ---
    sendBtn.addEventListener('click', handleSend);

    chatInput.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            handleSend();
        }
    });

    if (newChatBtn) {
        newChatBtn.addEventListener('click', resetChat);
    }
});
