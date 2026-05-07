/* ChatArea.js - Chat area interactions & Messaging */
import { SELECTORS, ENDPOINTS } from '../core/Config.js';
import { MessageRenderer } from './MessageRenderer.js';
import { ApiClient } from '../core/ApiClient.js';
import { state } from '../core/State.js';
import { Toast } from './Toast.js';

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
        this.exportBtn = document.getElementById('export-excel');
        
        // Voice elements
        this.micBtn = document.querySelector(SELECTORS.MIC_BTN);
        this.micRipple = document.querySelector(SELECTORS.MIC_RIPPLE);
        this.voiceVisualizer = document.querySelector(SELECTORS.VOICE_VISUALIZER);
        
        this.inputWrapper = document.querySelector('.input-wrapper');
        this.isLoading = false;
        this.isListening = false;
        this.recognition = null;
        this.lastRawData = null; // Lưu dữ liệu bảng gần nhất để export
        
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
        if (this.exportBtn) this.exportBtn.addEventListener('click', () => this.handleExportExcel());
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

        // Khởi tạo conversation mới nếu chưa có (Phải làm trước khi appendMessage để message được lưu vào history)
        if (!state.currentConversationId) {
            const newId = Date.now();
            state.currentConversationId = newId;
            const newHistory = [
                { 
                    id: newId, 
                    title: text || "Cuộc trò chuyện mới", 
                    date: new Date().toLocaleDateString('vi-VN'),
                    messages: []
                },
                ...state.chatHistory
            ];
            state.chatHistory = newHistory;
        }

        this.appendMessage('user', text);

        this.chatInput.value = '';
        this.handleInput();

        this.setLoading(true);
        const typingIndicator = MessageRenderer.createTypingIndicator();
        this.messagesList.appendChild(typingIndicator);
        this.scrollToBottom();

        // Chuẩn bị tin nhắn AI trống để update nội dung stream
        let aiMessageEl = null;
        let aiContent = "";
        let aiSteps = [];
        let aiSuggestions = [];

        try {
            await ApiClient.fetchStream(ENDPOINTS.CHAT, {
                body: JSON.stringify({ message: text })
            }, (data) => {
                // Xóa typing indicator khi bắt đầu nhận dữ liệu đầu tiên
                if (typingIndicator.parentNode) {
                    this.messagesList.removeChild(typingIndicator);
                }

                // Nếu chưa có tin nhắn AI, tạo mới
                if (!aiMessageEl) {
                    aiMessageEl = MessageRenderer.createMessageElement('ai', '');
                    this.messagesList.appendChild(aiMessageEl);
                }

                if (data.type === 'step') {
                    aiSteps.push(data.step);
                    MessageRenderer.updateMessage(aiMessageEl, aiContent, aiSteps);
                } else if (data.type === 'final') {
                    aiContent = data.text;
                    aiSuggestions = data.suggestedQuestions || [];
                    this.lastRawData = data.rawData; // Cập nhật dữ liệu để export
                    MessageRenderer.updateMessage(aiMessageEl, aiContent, aiSteps, aiSuggestions);
                } else if (data.type === 'error') {
                    MessageRenderer.updateMessage(aiMessageEl, `⚠️ Lỗi: ${data.message}`, aiSteps);
                }
                
                this.scrollToBottom();
            });
        } catch (error) {
            console.error('Chat error:', error);
            if (typingIndicator.parentNode) {
                this.messagesList.removeChild(typingIndicator);
            }
            this.appendMessage('ai', `⚠️ Không thể kết nối tới máy chủ: ${error.message}`);
        } finally {
            this.setLoading(false);
            // Lưu tin nhắn AI vào history khi kết thúc stream
            if (aiContent || aiSteps.length > 0) {
                state.addMessageToHistory(state.currentConversationId, { 
                    role: 'ai', 
                    content: aiContent, 
                    steps: aiSteps,
                    suggestions: aiSuggestions
                });
            }
        }
    }

    appendMessage(role, content, steps, suggestions) {
        if (this.messagesList.classList.contains('hidden')) {
            this.landingView.classList.add('hidden');
            this.messagesList.classList.remove('hidden');
        }

        const msgEl = MessageRenderer.createMessageElement(role, content, steps, suggestions);
        this.messagesList.appendChild(msgEl);
        this.scrollToBottom();

        // Lưu tin nhắn của User ngay lập tức (AI lưu ở finally của fetchStream vì là stream)
        if (role === 'user' && state.currentConversationId) {
            state.addMessageToHistory(state.currentConversationId, { role, content, steps, suggestions });
        }
    }

    loadConversation(id) {
        const conversation = state.chatHistory.find(h => String(h.id) === String(id));
        if (!conversation) return;

        state.currentConversationId = id;
        
        // Clear UI
        this.messagesList.innerHTML = '';
        this.landingView.classList.add('hidden');
        this.messagesList.classList.remove('hidden');

        // Render messages
        if (conversation.messages && conversation.messages.length > 0) {
            conversation.messages.forEach(msg => {
                const msgEl = MessageRenderer.createMessageElement(msg.role, msg.content, msg.steps, msg.suggestions);
                this.messagesList.appendChild(msgEl);
            });
        }
        
        this.scrollToBottom();
        
        // Đóng sidebar trên mobile
        if (window.innerWidth <= 768) {
            state.isSidebarOpen = false;
        }
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
        const messageContainer = btn.closest('.message') || btn.closest('.message__bubble');
        const content = messageContainer.querySelector('.markdown-content').innerText;
        
        navigator.clipboard.writeText(content).then(() => {
            // Hiệu ứng "nhấn" nút
            btn.style.transform = 'scale(0.85)';
            setTimeout(() => btn.style.transform = '', 150)

            // Tạo Floating Badge
            const rect = btn.getBoundingClientRect();
            const badge = document.createElement('div');
            badge.className = 'copy-badge';
            badge.innerHTML = '<i class="ph-fill ph-check-circle"></i> Copied';
            
            // Định vị badge ngay trên nút
            badge.style.left = `${rect.left + rect.width / 2}px`;
            badge.style.top = `${rect.top}px`;
            
            document.body.appendChild(badge);
            
            // Tự động xóa badge sau khi animation kết thúc
            setTimeout(() => badge.remove(), 1200);
        });
    }

    async handleExportExcel() {
        if (!this.lastRawData || this.lastRawData.length === 0) {
            Toast.warning("Không có dữ liệu bảng để xuất!");
            return;
        }

        try {
            this.exportBtn.disabled = true;
            this.exportBtn.innerHTML = '<i class="ph-bold ph-spinner-gap animate-spin"></i>';

            const response = await fetch(ENDPOINTS.EXPORT_EXCEL, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(this.lastRawData)
            });

            if (!response.ok) throw new Error("Export failed");

            const blob = await response.blob();
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `data_export_${new Date().getTime()}.xlsx`;
            document.body.appendChild(a);
            a.click();
            window.URL.revokeObjectURL(url);
            a.remove();
        } catch (error) {
            console.error('Export error:', error);
            Toast.error("Lỗi khi xuất file Excel");
        } finally {
            this.exportBtn.disabled = false;
            this.exportBtn.innerHTML = '<i class="ph-bold ph-file-xls"></i>';
        }
    }

    resetChat() {
        this.messagesList.innerHTML = '';
        this.messagesList.classList.add('hidden');
        this.landingView.classList.remove('hidden');
        this.lastRawData = null;
        state.currentConversationId = null;
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
