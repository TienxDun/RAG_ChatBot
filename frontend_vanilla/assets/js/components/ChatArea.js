/* ChatArea.js - Chat area interactions & Messaging */
import { SELECTORS } from '../core/Config.js';
import { MessageRenderer } from './MessageRenderer.js';

export class ChatAreaComponent {
    constructor() {
        this.chatInput = document.querySelector(SELECTORS.CHAT_INPUT);
        this.chatArea = document.querySelector(SELECTORS.CHAT_AREA);
        this.messagesList = document.getElementById('messages-list');
        this.landingView = document.getElementById('landing-view');
        this.scrollTopBtn = document.querySelector(SELECTORS.SCROLL_TOP);
        this.sendBtn = document.querySelector(SELECTORS.SEND_BTN);
        this.newChatBtn = document.querySelector(SELECTORS.NEW_CHAT);
        this.headerNewChatBtn = document.querySelector(SELECTORS.HEADER_NEW_CHAT);
        
        // Voice elements
        this.micBtn = document.querySelector(SELECTORS.MIC_BTN);
        this.micRipple = document.querySelector(SELECTORS.MIC_RIPPLE);
        this.voiceVisualizer = document.querySelector(SELECTORS.VOICE_VISUALIZER);
        
        this.inputWrapper = document.querySelector('.input-wrapper');
        this.isLoading = false;
        this.isListening = false;
        this.recognition = null;
        
        this.init();
        this.initSpeechRecognition();
    }

    init() {
        if (this.chatInput) {
            this.chatInput.addEventListener('input', () => this.handleInput());
            this.chatInput.addEventListener('focus', () => this.updateInputUI());
            this.chatInput.addEventListener('blur', () => this.updateInputUI());
            this.chatInput.addEventListener('keydown', (e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault();
                    this.handleSend();
                }
            });
        }

        if (this.sendBtn) this.sendBtn.addEventListener('click', () => this.handleSend());
        if (this.newChatBtn) this.newChatBtn.addEventListener('click', () => this.resetChat());
        if (this.headerNewChatBtn) this.headerNewChatBtn.addEventListener('click', () => this.resetChat());
        if (this.micBtn) this.micBtn.addEventListener('click', () => this.toggleMic());
        if (this.chatArea) this.chatArea.addEventListener('scroll', () => this.handleScroll());

        if (this.scrollTopBtn) {
            this.scrollTopBtn.addEventListener('click', () => {
                this.chatArea.scrollTo({ top: 0, behavior: 'smooth' });
            });
        }

        document.querySelectorAll('.suggestion-tag').forEach(tag => {
            tag.addEventListener('click', () => {
                this.chatInput.value = tag.getAttribute('data-value');
                this.handleInput();
                this.chatInput.focus();
            });
        });
    }

    initSpeechRecognition() {
        const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
        if (SpeechRecognition) {
            this.recognition = new SpeechRecognition();
            this.recognition.continuous = false;
            this.recognition.interimResults = true;
            this.recognition.lang = 'vi-VN';

            this.recognition.onresult = (event) => {
                const transcript = Array.from(event.results)
                    .map(result => result[0])
                    .map(result => result.transcript)
                    .join('');
                
                this.chatInput.value = transcript;
                this.handleInput();
            };

            this.recognition.onend = () => {
                this.stopListeningUI();
            };

            this.recognition.onerror = (event) => {
                console.error('Speech recognition error', event.error);
                this.stopListeningUI();
            };
        } else {
            if (this.micBtn) this.micBtn.style.display = 'none';
        }
    }

    toggleMic() {
        if (this.isListening) {
            this.recognition?.stop();
        } else {
            try {
                this.recognition?.start();
                this.startListeningUI();
            } catch (err) {
                console.error('Start listening error:', err);
            }
        }
    }

    startListeningUI() {
        this.isListening = true;
        this.micBtn?.classList.add('active');
        this.micBtn?.querySelector('i').classList.replace('ph-microphone', 'ph-microphone-stage');
        this.micRipple?.classList.remove('hidden');
        this.voiceVisualizer?.classList.remove('hidden');
        this.chatInput.placeholder = 'Đang lắng nghe...';
        
        // Ẩn nút gửi khi đang nghe giọng nói
        if (this.sendBtn) {
            this.sendBtn.classList.add('hidden');
        }
    }

    stopListeningUI() {
        this.isListening = false;
        this.micBtn?.classList.remove('active');
        this.micBtn?.querySelector('i').classList.replace('ph-microphone-stage', 'ph-microphone');
        this.micRipple?.classList.add('hidden');
        this.voiceVisualizer?.classList.add('hidden');
        this.chatInput.placeholder = 'Hỏi về cơ sở dữ liệu của bạn...';

        // Hiện lại nút gửi
        if (this.sendBtn) {
            this.sendBtn.classList.remove('hidden');
        }
    }

    handleInput() {
        if (this.chatInput) {
            this.chatInput.style.height = 'auto';
            this.chatInput.style.height = this.chatInput.scrollHeight + 'px';
        }
        
        this.updateInputUI();

        // Hide suggestions when typing to avoid overlapping
        const hasValue = this.chatInput.value.trim() !== '';
        const suggestions = document.querySelector('.suggestions');
        if (suggestions) {
            if (hasValue) {
                suggestions.classList.add('is-hidden');
            } else {
                suggestions.classList.remove('is-hidden');
            }
        }
    }

    updateInputUI() {
        if (!this.chatInput) return;
        
        const hasValue = this.chatInput.value.trim().length > 0;
        const isFocused = document.activeElement === this.chatInput;
        
        if (this.sendBtn) this.sendBtn.disabled = !hasValue;
        
        // Expand width when typing OR focused
        if (this.inputWrapper) {
            if (hasValue || isFocused) {
                this.inputWrapper.classList.add('is-expanded');
            } else {
                this.inputWrapper.classList.remove('is-expanded');
            }
        }

        // Hide mic when typing
        if (this.micBtn) {
            this.micBtn.style.display = hasValue ? 'none' : 'flex';
        }
    }

    async handleSend() {
        const text = this.chatInput.value.trim();
        if (!text || this.isLoading) return;

        this.appendMessage('user', text);
        this.chatInput.value = '';
        this.handleInput();

        this.setLoading(true);
        const typingIndicator = MessageRenderer.createTypingIndicator();
        this.messagesList.appendChild(typingIndicator);
        this.scrollToBottom();

        // Simulate API (Future: call ApiService)
        setTimeout(() => {
            this.messagesList.removeChild(typingIndicator);
            const mockSteps = [
                { title: "Vectorization", content: "Đã chuyển đổi sang vector." },
                { title: "SQL Generation", content: "Đã tạo SQL truy vấn dữ liệu nhà máy." }
            ];
            this.appendMessage('ai', `Kết quả phân tích cho: **${text}**...`, mockSteps, ["Chi tiết doanh thu?", "So sánh với tháng trước"]);
            this.setLoading(false);
        }, 1500);
    }

    appendMessage(role, content, steps, suggestions) {
        if (this.messagesList.classList.contains('hidden')) {
            this.landingView.classList.add('hidden');
            this.messagesList.classList.remove('hidden');
        }

        const msgEl = MessageRenderer.createMessageElement(role, content, steps, suggestions);
        this.messagesList.appendChild(msgEl);
        this.scrollToBottom();
    }

    sendQuickQuestion(text) {
        this.chatInput.value = text;
        this.handleSend();
    }

    editMessage(btn) {
        const content = btn.closest('.message').querySelector('.markdown-content').innerText;
        this.chatInput.value = content;
        this.chatInput.focus();
        this.handleInput();
    }

    copyMessage(btn) {
        const content = btn.closest('.message').querySelector('.markdown-content').innerText;
        navigator.clipboard.writeText(content).then(() => {
            const originalHtml = btn.innerHTML;
            btn.innerHTML = '<i class="ph-bold ph-check" style="color: #22c55e"></i> Đã copy';
            setTimeout(() => btn.innerHTML = originalHtml, 2000);
        });
    }

    resetChat() {
        this.messagesList.innerHTML = '';
        this.messagesList.classList.add('hidden');
        this.landingView.classList.remove('hidden');
    }

    setLoading(val) {
        this.isLoading = val;
        if (this.sendBtn) this.sendBtn.disabled = val;
    }

    handleScroll() {
        if (this.chatArea.scrollTop > 400) this.scrollTopBtn.classList.remove('hidden');
        else this.scrollTopBtn.classList.add('hidden');
    }

    scrollToBottom() {
        this.chatArea.scrollTo({ top: this.chatArea.scrollHeight, behavior: 'smooth' });
    }
}
