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
        this.attachBtn = document.querySelector(SELECTORS.ATTACH_BTN);
        this.chatFile = document.querySelector(SELECTORS.CHAT_FILE);
        this.filePreviewContainer = document.querySelector(SELECTORS.FILE_PREVIEW_CONTAINER);
        
        this.inputWrapper = document.querySelector('.input-wrapper');
        this.isLoading = false;
        this.isListening = false;
        this.selectedFile = null;
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
        if (this.attachBtn) this.attachBtn.addEventListener('click', () => this.chatFile.click());
        if (this.chatFile) this.chatFile.addEventListener('change', (e) => this.handleFileSelect(e));

        // Drag & Drop events
        const inputContainer = document.querySelector(SELECTORS.CHAT_CONTAINER || '#input-container');
        if (inputContainer) {
            inputContainer.addEventListener('dragover', (e) => {
                e.preventDefault();
                inputContainer.classList.add('dragover');
            });

            inputContainer.addEventListener('dragleave', () => {
                inputContainer.classList.remove('dragover');
            });

            inputContainer.addEventListener('drop', (e) => {
                e.preventDefault();
                inputContainer.classList.remove('dragover');
                const files = e.dataTransfer.files;
                if (files.length > 0) {
                    this.processFile(files[0]);
                }
            });
        }
        if (this.chatArea) this.chatArea.addEventListener('scroll', () => this.handleScroll());

        if (this.scrollTopBtn) {
            this.scrollTopBtn.addEventListener('click', () => {
                this.chatArea.scrollTo({ top: 0, behavior: 'smooth' });
            });
        }

        // Event Delegation cho tin nhắn
        if (this.messagesList) {
            this.messagesList.addEventListener('click', (e) => this.handleMessageAction(e));
        }

        document.querySelectorAll('.suggestion-tag').forEach(tag => {
            tag.addEventListener('click', () => {
                this.chatInput.value = tag.getAttribute('data-value');
                this.handleInput();
                this.chatInput.focus();
            });
        });
    }

    handleMessageAction(e) {
        const btn = e.target.closest('[data-action]');
        if (!btn) return;

        const action = btn.getAttribute('data-action');
        const value = btn.getAttribute('data-value');

        switch (action) {
            case 'copy-msg': this.copyMessage(btn); break;
            case 'edit-msg': this.editMessage(btn); break;
            case 'copy-code': this.copyTerminalCode(btn); break;
            case 'quick-question': this.sendQuickQuestion(value); break;
            case 'toggle-steps': btn.classList.toggle('active'); break;
        }
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
                    .map(result => result[0].transcript)
                    .join('');
                
                this.chatInput.value = transcript;
                this.handleInput();
            };

            this.recognition.onend = () => this.stopListeningUI();
            this.recognition.onerror = (event) => {
                console.error('Speech recognition error', event.error);
                this.stopListeningUI();
            };
        } else if (this.micBtn) {
            this.micBtn.style.display = 'none';
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
        if (this.sendBtn) this.sendBtn.classList.add('hidden');
    }

    stopListeningUI() {
        this.isListening = false;
        this.micBtn?.classList.remove('active');
        this.micBtn?.querySelector('i').classList.replace('ph-microphone-stage', 'ph-microphone');
        this.micRipple?.classList.add('hidden');
        this.voiceVisualizer?.classList.add('hidden');
        this.chatInput.placeholder = 'Hỏi về cơ sở dữ liệu của bạn...';
        if (this.sendBtn) this.sendBtn.classList.remove('hidden');
    }

    handleInput() {
        if (this.chatInput) {
            this.chatInput.style.height = 'auto';
            this.chatInput.style.height = this.chatInput.scrollHeight + 'px';
        }
        
        this.updateInputUI();

        const hasValue = this.chatInput.value.trim() !== '';
        const suggestions = document.querySelector('.suggestions');
        if (suggestions) {
            suggestions.classList.toggle('is-hidden', hasValue);
        }
    }

    updateInputUI() {
        if (!this.chatInput) return;
        
        const hasValue = this.chatInput.value.trim().length > 0;
        const isFocused = document.activeElement === this.chatInput;
        
        if (this.sendBtn) this.sendBtn.disabled = !hasValue;
        
        if (this.inputWrapper) {
            this.inputWrapper.classList.toggle('is-expanded', hasValue || isFocused);
        }

        if (this.micBtn) {
            this.micBtn.style.display = (hasValue || this.selectedFile) ? 'none' : 'flex';
        }
    }

    handleFileSelect(e) {
        const file = e.target.files[0];
        this.processFile(file);
    }

    processFile(file) {
        if (!file) return;
        
        // Kiểm tra định dạng file (chỉ nhận Excel)
        const ext = file.name.split('.').pop().toLowerCase();
        if (ext !== 'xlsx') {
            Toast.error("Chỉ hỗ trợ file Excel (.xlsx)");
            this.chatFile.value = '';
            return;
        }

        this.selectedFile = file;
        this.renderFilePreview();
        this.updateInputUI();
        this.chatInput.focus();
    }

    renderFilePreview() {
        const container = document.querySelector(SELECTORS.FILE_PREVIEW_CONTAINER);
        const suggestions = document.querySelector(SELECTORS.LANDING_SUGGESTIONS);
        if (!container) return;

        if (this.selectedFile) {
            container.classList.remove('hidden');
            if (suggestions) suggestions.classList.add('hidden');
            container.innerHTML = `
                <div class="file-preview-chip animate-in zoom-in duration-300">
                    <i class="ph-fill ph-file-xls"></i>
                    <span class="file-name">${this.selectedFile.name}</span>
                    <button class="btn-remove-preview" id="remove-file-btn" title="Gỡ bỏ file">
                        <i class="ph-bold ph-x"></i>
                    </button>
                </div>
            `;

            document.getElementById('remove-file-btn').addEventListener('click', () => {
                this.selectedFile = null;
                this.chatFile.value = '';
                this.renderFilePreview();
                this.updateInputUI();
            });
        } else {
            container.classList.add('hidden');
            if (suggestions) suggestions.classList.remove('hidden');
            container.innerHTML = '';
        }
    }

    async handleSend() {
        const text = this.chatInput.value.trim();
        if (!text || this.isLoading) return;

        this._ensureConversationStarted(text);
        this.appendMessage('user', text);
        this.chatInput.value = '';
        this.handleInput();

        this.setLoading(true);
        const typingIndicator = MessageRenderer.createTypingIndicator();
        this.messagesList.appendChild(typingIndicator);
        this.scrollToBottom();

        try {
            await this._processStreamResponse(text, this.selectedFile, typingIndicator);
            this.selectedFile = null;
            this.chatFile.value = '';
            this.renderFilePreview();
        } catch (error) {
            console.error('Chat error:', error);
            if (typingIndicator.parentNode) this.messagesList.removeChild(typingIndicator);
            this.appendMessage('ai', `⚠️ Không thể kết nối tới máy chủ: ${error.message}`);
        } finally {
            this.setLoading(false);
        }
    }

    _ensureConversationStarted(title) {
        if (!state.currentConversationId) {
            const newId = Date.now();
            state.currentConversationId = newId;
            state.chatHistory = [
                { 
                    id: newId, 
                    title: title || "Cuộc trò chuyện mới", 
                    date: new Date().toLocaleDateString('vi-VN'),
                    messages: []
                },
                ...state.chatHistory
            ];
        }
    }

    async _processStreamResponse(text, file, typingIndicator) {
        let aiMessageEl = null;
        let aiContent = "";
        let aiSteps = [];
        let aiSuggestions = [];
        let aiDownloadUrl = null;

        let body;
        if (file) {
            body = new FormData();
            body.append('message', text);
            body.append('file', file);
        } else {
            body = JSON.stringify({ message: text });
        }

        await ApiClient.fetchStream(ENDPOINTS.CHAT, {
            body: body
        }, (data) => {
            // Cập nhật text cho typing indicator nếu là bước xử lý
            if (data.type === 'step') {
                aiSteps.push(data.step);
                MessageRenderer.updateTypingText(typingIndicator, `Đang xử lý: ${data.step.title}...`);
            }

            // Chỉ xóa typing indicator khi có nội dung cuối cùng hoặc lỗi
            if (data.type === 'final' || data.type === 'error') {
                if (typingIndicator.parentNode) {
                    this.messagesList.removeChild(typingIndicator);
                }
            }

            // Tạo message element cho AI nếu chưa có (để hiển thị RAG steps ngay)
            if (!aiMessageEl && (data.type === 'step' || data.type === 'final' || data.type === 'error')) {
                aiMessageEl = MessageRenderer.createMessageElement('ai', '');
                this.messagesList.appendChild(aiMessageEl);
            }

            if (data.type === 'final') {
                aiContent = data.text;
                aiSuggestions = data.suggestedQuestions || [];
                aiDownloadUrl = data.downloadUrl;
                this.lastRawData = data.rawData;
            } else if (data.type === 'error') {
                aiContent = `⚠️ Lỗi: ${data.message}`;
            }
            
            if (aiMessageEl) {
                MessageRenderer.updateMessage(aiMessageEl, aiContent, aiSteps, aiSuggestions, aiDownloadUrl);
            }
            this.scrollToBottom();
        });

        if (aiContent || aiSteps.length > 0) {
            state.addMessageToHistory(state.currentConversationId, { 
                role: 'ai', 
                content: aiContent, 
                steps: aiSteps, 
                suggestions: aiSuggestions,
                downloadUrl: aiDownloadUrl
            });
        }
    }

    appendMessage(role, content, steps, suggestions, downloadUrl) {
        if (this.messagesList.classList.contains('hidden')) {
            this.landingView.classList.add('hidden');
            this.messagesList.classList.remove('hidden');
        }

        const msgEl = MessageRenderer.createMessageElement(role, content, steps, suggestions, downloadUrl);
        this.messagesList.appendChild(msgEl);
        this.scrollToBottom();

        if (role === 'user' && state.currentConversationId) {
            state.addMessageToHistory(state.currentConversationId, { role, content, steps, suggestions });
        }
    }

    loadConversation(id) {
        const conversation = state.chatHistory.find(h => String(h.id) === String(id));
        if (!conversation) return;

        state.currentConversationId = id;
        this.messagesList.innerHTML = '';
        this.landingView.classList.add('hidden');
        this.messagesList.classList.remove('hidden');

        if (conversation.messages?.length > 0) {
            conversation.messages.forEach(msg => {
                this.messagesList.appendChild(
                    MessageRenderer.createMessageElement(msg.role, msg.content, msg.steps, msg.suggestions, msg.downloadUrl)
                );
            });
        }
        
        this.scrollToBottom();
        if (window.innerWidth <= 768) state.isSidebarOpen = false;
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

    _showCopyFeedback(btn, isFooter = false) {
        const icon = btn.querySelector('i');
        const originalClass = icon.className;
        const originalHTML = btn.innerHTML;

        icon.className = 'ph-bold ph-check text-green-500';
        if (isFooter) btn.innerHTML = '<i class="ph-bold ph-check text-green-500"></i> Copied';

        setTimeout(() => {
            if (isFooter) btn.innerHTML = originalHTML;
            else icon.className = originalClass;
        }, 2000);

        Toast.success("Đã sao chép!");
    }

    copyMessage(btn) {
        const container = btn.closest('.message') || btn.closest('.message__bubble');
        const content = container.querySelector('.markdown-content').innerText;
        
        navigator.clipboard.writeText(content).then(() => {
            this._showCopyFeedback(btn, btn.classList.contains('footer-copy'));
        });
    }

    copyTerminalCode(btn) {
        const text = btn.closest('.terminal-code').querySelector('code').innerText;
        navigator.clipboard.writeText(text).then(() => this._showCopyFeedback(btn));
    }

    async handleExportExcel() {
        if (!this.lastRawData?.length) {
            Toast.warning("Không có dữ liệu để xuất!");
            return;
        }

        try {
            this.exportBtn.disabled = true;
            this.exportBtn.innerHTML = '<i class="ph-bold ph-spinner-gap animate-spin"></i>';

            const url = ApiClient._resolveUrl(ENDPOINTS.EXPORT_EXCEL);
            const response = await fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(this.lastRawData)
            });

            if (!response.ok) throw new Error("Xuất file thất bại");

            const blob = await response.blob();
            const blobUrl = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = blobUrl;
            a.download = `export_${Date.now()}.xlsx`;
            a.click();
            window.URL.revokeObjectURL(blobUrl);
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
        this.scrollTopBtn?.classList.toggle('hidden', this.chatArea.scrollTop <= 400);
    }

    scrollToBottom() {
        this.chatArea.scrollTo({ top: this.chatArea.scrollHeight, behavior: 'smooth' });
    }
}
