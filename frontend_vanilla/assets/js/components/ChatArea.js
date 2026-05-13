// ChatArea.js - Chat area interactions & Messaging
import { SELECTORS, ENDPOINTS } from '../core/Config.js';
import { MessageRenderer } from './MessageRenderer.js';
import { ApiClient } from '../core/ApiClient.js';
import { state } from '../core/State.js';
import { Toast } from './Toast.js';
import { SpeechService } from '../services/SpeechService.js';
import { FileHandler } from '../services/FileHandler.js';
import { ExportService } from '../services/ExportService.js';
import { ChatService } from '../services/ChatService.js';
import { InteractionService } from '../services/InteractionService.js';
import { TemplateCacheService } from '../services/TemplateCacheService.js';


export class ChatAreaComponent {
    constructor() {
        // 1. Cache DOM Elements
        this.elements = {
            chatInput: document.querySelector(SELECTORS.CHAT_INPUT),
            chatArea: document.querySelector(SELECTORS.CHAT_AREA),
            messagesList: document.getElementById('messages-list'),
            landingView: document.getElementById('landing-view'),
            scrollTopBtn: document.querySelector(SELECTORS.SCROLL_TOP),
            sendBtn: document.querySelector(SELECTORS.SEND_BTN),
            newChatBtn: document.querySelector(SELECTORS.NEW_CHAT),
            headerNewChatBtn: document.querySelector(SELECTORS.HEADER_NEW_CHAT),
            exportBtn: document.getElementById('export-excel'),
            
            // Voice & Attachments
            micBtn: document.querySelector(SELECTORS.MIC_BTN),
            micRipple: document.querySelector(SELECTORS.MIC_RIPPLE),
            voiceVisualizer: document.querySelector(SELECTORS.VOICE_VISUALIZER),
            attachBtn: document.querySelector(SELECTORS.ATTACH_BTN),
            chatFile: document.querySelector(SELECTORS.CHAT_FILE),
            filePreviewContainer: document.querySelector(SELECTORS.FILE_PREVIEW_CONTAINER),
            
            inputWrapper: document.querySelector('.input-wrapper'),
            collectionSelect: document.getElementById('chat-collection-select'),
            inputContainer: document.querySelector(SELECTORS.CHAT_CONTAINER || '#input-container')
        };

        // 2. Initialize UI State
        this.uiState = {
            isLoading: false,
            selectedFile: null,
            lastRawData: null
        };

        this.speechService = null;
        this._init();
        this._initSpeechService();
        this._loadCollections();
    }

    _init() {
        const { chatInput, sendBtn, newChatBtn, headerNewChatBtn, exportBtn, micBtn, attachBtn, chatFile, chatArea, scrollTopBtn, messagesList, inputContainer } = this.elements;

        if (chatInput) {
            chatInput.addEventListener('input', () => this._handleInputAutoResize());
            chatInput.addEventListener('focus', () => this._updateInputUI());
            chatInput.addEventListener('blur', () => this._updateInputUI());
            chatInput.addEventListener('keydown', (e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault();
                    this.handleSend();
                }
            });
        }

        if (sendBtn) sendBtn.addEventListener('click', () => this.handleSend());
        if (newChatBtn) newChatBtn.addEventListener('click', () => this.resetChat());
        if (headerNewChatBtn) headerNewChatBtn.addEventListener('click', () => this.resetChat());
        if (exportBtn) exportBtn.addEventListener('click', () => this.handleExportExcel());
        if (micBtn) micBtn.addEventListener('click', () => this._toggleMic());
        if (attachBtn) attachBtn.addEventListener('click', () => chatFile.click());
        if (chatFile) chatFile.addEventListener('change', (e) => this._handleFileSelect(e));

        this._initDragAndDrop(inputContainer);

        if (chatArea) chatArea.addEventListener('scroll', () => this._handleScroll());

        if (scrollTopBtn) {
            scrollTopBtn.addEventListener('click', () => {
                chatArea.scrollTo({ top: 0, behavior: 'smooth' });
            });
        }

        if (messagesList) {
            messagesList.addEventListener('click', (e) => this._handleMessageAction(e));
        }

        this._bindSuggestionTags();
        this._initTemplateCacheUI();
    }

    _initDragAndDrop(container) {
        if (!container) return;

        container.addEventListener('dragover', (e) => {
            e.preventDefault();
            container.classList.add('dragover');
        });

        container.addEventListener('dragleave', () => {
            container.classList.remove('dragover');
        });

        container.addEventListener('drop', (e) => {
            e.preventDefault();
            container.classList.remove('dragover');
            const files = e.dataTransfer.files;
            if (files.length > 0) {
                this._processFile(files[0]);
            }
        });
    }

    _bindSuggestionTags() {
        document.querySelectorAll('.suggestion-tag').forEach(tag => {
            tag.addEventListener('click', () => {
                this.elements.chatInput.value = tag.getAttribute('data-value');
                this._handleInputAutoResize();
                this.elements.chatInput.focus();
            });
        });
    }

    async _loadCollections() {
        const { collectionSelect } = this.elements;
        if (!collectionSelect) return;

        try {
            const collections = await ApiClient.get(ENDPOINTS.COLLECTIONS);
            if (!Array.isArray(collections)) return;

            const defaultOption = collectionSelect.options[0];
            collectionSelect.innerHTML = '';
            collectionSelect.appendChild(defaultOption);

            collections.forEach(col => {
                if (col !== 'db_schema') {
                    const option = document.createElement('option');
                    option.value = col;
                    option.textContent = col;
                    collectionSelect.appendChild(option);
                }
            });
        } catch (error) {
            console.error('Failed to load collections:', error);
        }
    }

    _handleMessageAction(e) {
        const btn = e.target.closest('[data-action]');
        if (!btn) return;

        const action = btn.getAttribute('data-action');
        const value = btn.getAttribute('data-value');

        switch (action) {
            case 'copy-msg': this._copyMessage(btn); break;
            case 'edit-msg': this._editMessage(btn); break;
            case 'copy-code': this._copyTerminalCode(btn); break;
            case 'quick-question': this._sendQuickQuestion(value); break;
            case 'toggle-steps': btn.classList.toggle('active'); break;
            case 'export-msg-excel': this._handleExportMessageExcel(btn); break;
        }
    }

    _initSpeechService() {
        this.speechService = new SpeechService({
            onResult: (transcript) => {
                this.elements.chatInput.value = transcript;
                this._handleInputAutoResize();
            },
            onEnd: () => this._stopListeningUI(),
            onError: () => this._stopListeningUI()
        });

        if (!this.speechService.isSupported() && this.elements.micBtn) {
            this.elements.micBtn.style.display = 'none';
        }
    }

    _toggleMic() {
        if (this.speechService?.isListening) {
            this.speechService.stop();
        } else {
            this.speechService?.start();
            this._startListeningUI();
        }
    }

    _startListeningUI() {
        const { micBtn, micRipple, voiceVisualizer, chatInput, sendBtn } = this.elements;
        micBtn?.classList.add('active');
        micBtn?.querySelector('i').classList.replace('ph-microphone', 'ph-microphone-stage');
        micRipple?.classList.remove('hidden');
        voiceVisualizer?.classList.remove('hidden');
        if (chatInput) chatInput.placeholder = 'Đang lắng nghe...';
        if (sendBtn) sendBtn.classList.add('hidden');
    }

    _stopListeningUI() {
        const { micBtn, micRipple, voiceVisualizer, chatInput, sendBtn } = this.elements;
        micBtn?.classList.remove('active');
        micBtn?.querySelector('i').classList.replace('ph-microphone-stage', 'ph-microphone');
        micRipple?.classList.add('hidden');
        voiceVisualizer?.classList.add('hidden');
        if (chatInput) chatInput.placeholder = 'Hỏi về cơ sở dữ liệu của bạn...';
        if (sendBtn) sendBtn.classList.remove('hidden');
    }

    _handleInputAutoResize() {
        const { chatInput } = this.elements;
        if (!chatInput) return;

        chatInput.style.height = '0px';
        const scrollHeight = chatInput.scrollHeight;
        
        if (chatInput.value.trim() === '') {
            chatInput.style.height = 'auto';
        } else {
            chatInput.style.height = scrollHeight + 'px';
        }
        
        this._updateInputUI();

        const suggestions = document.querySelector('.suggestions');
        if (suggestions) {
            suggestions.classList.toggle('is-hidden', chatInput.value.trim() !== '');
        }
    }

    _updateInputUI() {
        const { chatInput, sendBtn, inputWrapper, micBtn } = this.elements;
        if (!chatInput) return;
        
        const hasValue = chatInput.value.trim().length > 0;
        const isFocused = document.activeElement === chatInput;
        
        if (sendBtn) sendBtn.disabled = !hasValue;
        
        if (inputWrapper) {
            inputWrapper.classList.toggle('is-expanded', hasValue || isFocused);
        }

        if (micBtn) {
            micBtn.style.display = (hasValue || this.uiState.selectedFile) ? 'none' : 'flex';
        }
    }

    _handleFileSelect(e) {
        this._processFile(e.target.files[0]);
    }

    _processFile(file) {
        if (FileHandler.validateExcel(file)) {
            this.uiState.selectedFile = file;
            this._renderFilePreview();
            this._updateInputUI();
            this.elements.chatInput.focus();
        } else {
            this.elements.chatFile.value = '';
        }
    }

    _renderFilePreview() {
        const { filePreviewContainer: container, chatFile } = this.elements;
        if (!this.uiState.selectedFile) {
            FileHandler.clearPreview(container, {
                showSuggestions: () => document.querySelector(SELECTORS.LANDING_SUGGESTIONS)?.classList.remove('hidden')
            });
            return;
        }

        FileHandler.renderPreview(container, this.uiState.selectedFile, {
            hideSuggestions: () => document.querySelector(SELECTORS.LANDING_SUGGESTIONS)?.classList.add('hidden'),
            onRemove: () => {
                this.uiState.selectedFile = null;
                chatFile.value = '';
                this._renderFilePreview();
                this._updateInputUI();
            }
        });
    }

    _initTemplateCacheUI() {
        const leftActions = this.elements.inputContainer.querySelector('.input-actions-left');
        if (!leftActions) return;

        this.templatePopup = document.createElement('div');
        this.templatePopup.className = 'template-cache-popup';
        this.templatePopup.innerHTML = `
            <div class="template-cache-header">
                <h4><i class="ph-bold ph-clock-counter-clockwise"></i> TEMPLATES CACHED</h4>
            </div>
            <div id="template-cache-list" class="template-list">
                <div class="template-empty">
                    <i class="ph-duotone ph-spinner animate-spin"></i>
                    <span>Đang tải...</span>
                </div>
            </div>
        `;
        leftActions.appendChild(this.templatePopup);

        leftActions.addEventListener('mouseenter', () => this._refreshTemplateList());
    }

    async _refreshTemplateList() {
        const listContainer = document.getElementById('template-cache-list');
        if (!listContainer) return;

        try {
            const templates = await TemplateCacheService.getAll();
            if (!templates || templates.length === 0) {
                listContainer.innerHTML = `
                    <div class="template-empty">
                        <i class="ph-duotone ph-folder-open"></i>
                        <span>Chưa có template nào được lưu.</span>
                    </div>
                `;
                return;
            }

            listContainer.innerHTML = templates.map(t => `
                <div class="template-item" data-id="${t.id}" data-name="${t.fileName}">
                    <i class="ph-fill ph-microsoft-excel-logo"></i>
                    <div class="template-item-info">
                        <span class="template-item-name" title="${t.fileName}">${t.fileName}</span>
                        <span class="template-item-meta">${(t.fileSize / 1024).toFixed(1)} KB • ${new Date(t.cachedAt).toLocaleTimeString()}</span>
                    </div>
                    <button class="btn-remove-template" data-id="${t.id}" title="Xóa khỏi cache">
                        <i class="ph-bold ph-x"></i>
                    </button>
                </div>
            `).join('');

            listContainer.querySelectorAll('.template-item').forEach(item => {
                item.addEventListener('click', (e) => {
                    e.stopPropagation();
                    this._selectTemplateFromCache(item.getAttribute('data-id'), item.getAttribute('data-name'));
                });
            });

            // Gắn sự kiện cho nút xóa
            listContainer.querySelectorAll('.btn-remove-template').forEach(btn => {
                btn.addEventListener('click', async (e) => {
                    e.stopPropagation(); // Ngăn sự kiện chọn template
                    const id = btn.getAttribute('data-id');
                    if (await TemplateCacheService.removeTemplate(id)) {
                        this._refreshTemplateList();
                    } else {
                        Toast.error("Không thể xóa template.");
                    }
                });
            });
        } catch (error) {
            listContainer.innerHTML = '<div class="template-empty">Lỗi khi tải danh sách.</div>';
        }
    }

    async _selectTemplateFromCache(id, fileName) {
        try {
            Toast.info(`Đang lấy file: ${fileName}...`);
            const blob = await TemplateCacheService.downloadTemplate(id);
            if (!blob) {
                Toast.error("Không thể tải file từ bộ nhớ đệm.");
                return;
            }

            const file = new File([blob], fileName, { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
            
            this.uiState.selectedFile = file;
            this._renderFilePreview();
            this._updateInputUI();
            
            Toast.success(`Đã chọn: ${fileName}`);
        } catch (error) {
            console.error('Failed to select template from cache', error);
            Toast.error("Đã xảy ra lỗi.");
        }
    }

    async handleSend() {
        const { chatInput, chatArea, messagesList, chatFile, collectionSelect } = this.elements;
        const text = chatInput.value.trim();
        if (!text || this.uiState.isLoading) return;

        ChatService.ensureConversationStarted(text);
        this.appendMessage('user', text, [], [], null, null, this.uiState.selectedFile);
        
        const currentFile = this.uiState.selectedFile;
        // 🆕 Cache template trống song song (fire-and-forget)
        if (currentFile) TemplateCacheService.cacheTemplate(currentFile);

        chatInput.value = '';

        this._handleInputAutoResize();
        chatArea.classList.remove('is-landing');

        this._setLoading(true);
        const typingIndicator = MessageRenderer.createTypingIndicator();
        messagesList.appendChild(typingIndicator);
        this.scrollToBottom();

        // Bắt đầu đếm thời gian
        let seconds = 0;
        const timerInterval = setInterval(() => {
            seconds++;
            const timerEls = document.querySelectorAll('.loading-timer');
            timerEls.forEach(el => {
                el.innerText = `(${seconds}s)`;
            });
        }, 1000);

        try {
            const collectionName = collectionSelect ? collectionSelect.value : null;
            let aiMessageEl = null;
            let aiSteps = [];

            await ChatService.sendMessage(text, currentFile, collectionName, {
                onStep: (step) => {
                    aiSteps.push(step);
                    const status = `Đang xử lý: ${step.title}...`;
                    if (aiMessageEl) {
                        MessageRenderer.updateMessage(aiMessageEl, "", aiSteps, [], null, null, null, status);
                    } else {
                        MessageRenderer.updateTypingText(typingIndicator, status);
                    }
                    
                    // Cập nhật lại thời gian ngay sau khi render lại message
                    const timerEl = (aiMessageEl || typingIndicator).querySelector('.loading-timer');
                    if (timerEl) timerEl.innerText = `(${seconds}s)`;
                },
                onMessageElementCreated: () => {
                    if (typingIndicator.parentNode) messagesList.removeChild(typingIndicator);
                    if (!aiMessageEl) {
                        const lastStep = aiSteps[aiSteps.length - 1];
                        const status = lastStep ? `Đang xử lý: ${lastStep.title}...` : 'AI đang suy nghĩ...';
                        aiMessageEl = MessageRenderer.createMessageElement('ai', '', aiSteps, [], null, null, null, status);
                        messagesList.appendChild(aiMessageEl);
                        
                        const timerEl = aiMessageEl.querySelector('.loading-timer');
                        if (timerEl) timerEl.innerText = `(${seconds}s)`;
                        
                        this.scrollToBottom();
                    }
                },
                onFinal: (data) => {
                    if (aiMessageEl) {
                        MessageRenderer.updateMessage(aiMessageEl, data.text, aiSteps, data.suggestedQuestions, data.downloadUrl, data.rawData);
                        // Thêm thời gian tổng kết vào cuối nội dung hoặc footer nếu cần
                        const footer = aiMessageEl.querySelector('.ai-label');
                        if (footer) footer.innerText = `AI INSIGHT (${seconds}s)`;
                    }
                    this.uiState.lastRawData = data.rawData;
                },
                onError: (msg) => {
                    if (typingIndicator.parentNode) messagesList.removeChild(typingIndicator);
                    this.appendMessage('ai', `⚠️ Lỗi: ${msg}`);
                }
            });

            this.uiState.selectedFile = null;
            chatFile.value = '';
            this._renderFilePreview();
        } catch (error) {
            console.error('Chat error:', error);
        } finally {
            clearInterval(timerInterval);
            this._setLoading(false);
            this.scrollToBottom();
        }
    }

    _ensureConversationStarted(title) {
        ChatService.ensureConversationStarted(title);
    }


    appendMessage(role, content, steps, suggestions, downloadUrl, rawData, userFile) {
        const { messagesList, landingView, chatArea } = this.elements;

        if (messagesList.classList.contains('hidden')) {
            landingView.classList.add('hidden');
            messagesList.classList.remove('hidden');
            chatArea.classList.remove('is-landing');
        }

        const msgEl = MessageRenderer.createMessageElement(role, content, steps, suggestions, downloadUrl, rawData, userFile);
        messagesList.appendChild(msgEl);
        this.scrollToBottom();

        if (role === 'user' && state.currentConversationId) {
            state.addMessageToHistory(state.currentConversationId, { 
                role, 
                content, 
                steps, 
                suggestions, 
                userFile: userFile ? (typeof userFile === 'string' ? userFile : userFile.name) : null 
            });
        }
    }

    loadConversation(id) {
        const { messagesList, landingView, chatArea } = this.elements;
        const conversation = state.chatHistory.find(h => String(h.id) === String(id));
        if (!conversation) return;

        state.currentConversationId = id;
        messagesList.innerHTML = '';
        landingView.classList.add('hidden');
        messagesList.classList.remove('hidden');
        chatArea.classList.remove('is-landing');

        if (conversation.messages?.length > 0) {
            conversation.messages.forEach(msg => {
                messagesList.appendChild(
                    MessageRenderer.createMessageElement(msg.role, msg.content, msg.steps, msg.suggestions, msg.downloadUrl, msg.rawData, msg.userFile)
                );
            });
        }
        
        this.scrollToBottom();
        if (window.innerWidth <= 768) state.isSidebarOpen = false;
    }

    _sendQuickQuestion(text) {
        this.elements.chatInput.value = text;
        this.handleSend();
    }

    _editMessage(btn) {
        const content = InteractionService.getMessageContent(btn);
        this.elements.chatInput.value = content;
        this.elements.chatInput.focus();
        this._handleInputAutoResize();
    }

    _copyMessage(btn) {
        const content = InteractionService.getMessageContent(btn);
        InteractionService.copyToClipboard(content, btn, btn.classList.contains('footer-copy'));
    }

    _copyTerminalCode(btn) {
        const text = InteractionService.getTerminalCode(btn);
        InteractionService.copyToClipboard(text, btn);
    }

    async handleExportExcel() {
        await ExportService.exportToExcel(this.uiState.lastRawData, this.elements.exportBtn, {
            defaultLabel: '<i class="ph-bold ph-microsoft-excel-logo"></i>'
        });
    }

    async _handleExportMessageExcel(btn) {
        const messageEl = btn.closest('.message');
        const rawDataStr = messageEl?.getAttribute('data-raw');
        
        if (!rawDataStr) {
            Toast.warning("Không tìm thấy dữ liệu để xuất!");
            return;
        }

        try {
            const data = JSON.parse(rawDataStr);
            await ExportService.exportToExcel(data, btn, {
                defaultLabel: '<i class="ph-duotone ph-microsoft-excel-logo"></i> Xuất Excel'
            });
        } catch (e) {
            console.error('Failed to parse message rawData', e);
            Toast.error("Dữ liệu không hợp lệ");
        }
    }

    resetChat() {
        const { messagesList, landingView, chatArea } = this.elements;
        messagesList.innerHTML = '';
        messagesList.classList.add('hidden');
        landingView.classList.remove('hidden');
        chatArea.classList.add('is-landing');
        this.uiState.lastRawData = null;
        state.currentConversationId = null;
    }

    _setLoading(val) {
        this.uiState.isLoading = val;
        if (this.elements.sendBtn) this.elements.sendBtn.disabled = val;
    }

    _handleScroll() {
        const { scrollTopBtn, chatArea } = this.elements;
        scrollTopBtn?.classList.toggle('hidden', chatArea.scrollTop <= 400);
    }

    scrollToBottom() {
        const { chatArea } = this.elements;
        chatArea.scrollTo({ top: chatArea.scrollHeight, behavior: 'smooth' });
    }
}
