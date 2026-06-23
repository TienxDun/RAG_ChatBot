// Modal.js - File Upload Modal logic
import { state } from '../core/State.js';
import { SELECTORS, CONFIG, ENDPOINTS } from '../core/Config.js';
import { ApiClient } from '../core/ApiClient.js';
import { Toast } from './Toast.js';

export class ModalComponent {
    constructor() {
        this.modal = document.querySelector(SELECTORS.UPLOAD_MODAL);
        this.openBtn = document.querySelector(SELECTORS.OPEN_UPLOAD);
        this.closeBtn = document.querySelector(SELECTORS.CLOSE_MODAL);
        this.startBtn = document.querySelector(SELECTORS.START_UPLOAD);
        
        this.dropzone = document.querySelector(SELECTORS.DROPZONE);
        this.fileInput = document.querySelector(SELECTORS.FILE_INPUT);
        this.collectionSelect = document.getElementById('collection-name-select');
        this.customCollectionWrapper = document.getElementById('custom-collection-wrapper');
        this.collectionInput = document.getElementById('collection-name-input');
        this.fileList = document.querySelector(SELECTORS.FILE_LIST);
        this.infoText = document.querySelector(SELECTORS.MODAL_INFO_TEXT);
        
        this.progressContainer = document.querySelector(SELECTORS.UPLOAD_PROGRESS_CONTAINER);
        this.progressBar = document.querySelector(SELECTORS.PROGRESS_BAR);
        this.progressPercent = document.querySelector(SELECTORS.PROGRESS_PERCENT);
        this.progressStatus = document.querySelector(SELECTORS.PROGRESS_STATUS);

        this.init();
    }

    init() {
        if (this.openBtn) this.openBtn.addEventListener('click', () => this.show());
        if (this.closeBtn) this.closeBtn.addEventListener('click', () => this.hide());
        
        if (this.dropzone) {
            this.dropzone.addEventListener('click', () => this.fileInput.click());
            this.dropzone.addEventListener('dragover', (e) => { e.preventDefault(); this.dropzone.classList.add('dragover'); });
            this.dropzone.addEventListener('dragleave', () => this.dropzone.classList.remove('dragover'));
            this.dropzone.addEventListener('drop', (e) => { e.preventDefault(); this.dropzone.classList.remove('dragover'); this.handleFiles(e.dataTransfer.files); });
        }

        if (this.fileInput) {
            this.fileInput.addEventListener('change', (e) => this.handleFiles(e.target.files));
        }

        if (this.collectionSelect) {
            this.collectionSelect.addEventListener('change', () => {
                if (this.collectionSelect.value === '__custom__') {
                    if (this.customCollectionWrapper) this.customCollectionWrapper.classList.remove('hidden');
                    if (this.collectionInput) this.collectionInput.focus();
                } else {
                    if (this.customCollectionWrapper) this.customCollectionWrapper.classList.add('hidden');
                }
            });
        }

        if (this.startBtn) {
            this.startBtn.addEventListener('click', (e) => {
                e.preventDefault();
                this.upload();
            });
        }

        window.removeFile = (index) => this.removeFile(index);

        state.subscribe((key) => {
            if (key === 'selectedFiles' || key === 'isUploading') this.updateUI();
        });
    }

    show() { 
        this.modal.classList.remove('hidden'); 
        this.loadCollections();
    }
    hide() { 
        if (state.isUploading) return;
        this.modal.classList.add('hidden');
        state.selectedFiles = [];
        this.resetProgress();
    }

    handleFiles(files) {
        const newFiles = Array.from(files).filter(file => {
            const ext = '.' + file.name.split('.').pop().toLowerCase();
            return CONFIG.VALID_FILE_EXTENSIONS.includes(ext);
        });
        state.selectedFiles = [...state.selectedFiles, ...newFiles];
    }

    removeFile(index) {
        const files = [...state.selectedFiles];
        files.splice(index, 1);
        state.selectedFiles = files;
    }

    async loadCollections() {
        if (!this.collectionSelect) return;
        try {
            const dataSources = await ApiClient.get(ENDPOINTS.COLLECTIONS);
            if (Array.isArray(dataSources)) {
                let optionsHtml = '';
                dataSources.forEach(ds => {
                    if (ds.qdrantCollection) {
                        optionsHtml += `<option value="${ds.qdrantCollection}">${ds.qdrantCollection} (${ds.displayName || ds.id})</option>`;
                    }
                });
                optionsHtml += '<option value="__custom__">Khác (Nhập thủ công...)</option>';
                this.collectionSelect.innerHTML = optionsHtml;
            }
        } catch (error) {
            console.error('Failed to load collections for select:', error);
            this.collectionSelect.innerHTML = `
                <option value="db_schema">db_schema (VIKING_1)</option>
                <option value="__custom__">Khác (Nhập thủ công...)</option>
            `;
        }
    }

    async upload() {
        if (state.selectedFiles.length === 0 || state.isUploading) return;
        
        state.isUploading = true;
        this.progressContainer.classList.remove('hidden');
        this.dropzone.classList.add('hidden');
        if (this.collectionSelect) this.collectionSelect.closest('.modal-input-group').classList.add('hidden');
        
        let collectionName = '';
        if (this.collectionSelect) {
            if (this.collectionSelect.value === '__custom__') {
                collectionName = this.collectionInput ? this.collectionInput.value.trim() : '';
            } else {
                collectionName = this.collectionSelect.value;
            }
        }

        try {
            await ApiClient.uploadFiles(ENDPOINTS.UPLOAD, state.selectedFiles, collectionName, (data) => {
                if (data.type === 'progress') {
                    this.updateProgress(data.percent, data.message);
                } else if (data.type === 'error') {
                    throw new Error(data.message || "Lỗi xử lý file từ server.");
                } else if (data.type === 'result') {
                    this.updateProgress(100, "Tất cả file đã được xử lý!");
                    setTimeout(() => {
                        Toast.success("Tải lên và xử lý thành công!");
                        state.isUploading = false;
                        if (window.app && window.app.chatArea) {
                            window.app.chatArea.loadCollections();
                        }
                        this.hide();
                    }, 1000);
                }
            });
        } catch (error) {
            console.error('Upload error:', error);
            Toast.error(`Lỗi khi tải lên: ${error.message}`);
            state.isUploading = false;
            this.resetProgress();
        }
    }

    updateUI() {
        const files = state.selectedFiles;
        if (files.length > 0) {
            this.fileList.classList.remove('hidden');
            this.fileList.innerHTML = files.map((file, idx) => `
                <div class="file-item animate-in fade-in" style="animation-delay: ${idx * 50}ms">
                    <div class="file-info">
                        <div class="file-icon">
                            <i class="ph-duotone ph-file-text"></i>
                        </div>
                        <div class="file-details">
                            <div class="file-name">${file.name}</div>
                            <div class="file-meta">${(file.size / 1024).toFixed(1)} KB</div>
                        </div>
                    </div>
                    ${!state.isUploading ? `
                        <button class="btn-remove-file" onclick="removeFile(${idx})">
                            <i class="ph-bold ph-trash"></i>
                        </button>
                    ` : `
                        <div class="file-status-icon">
                            <i class="ph-bold ph-spinner-gap animate-spin text-primary"></i>
                        </div>
                    `}
                </div>
            `).join('');
            this.infoText.innerText = `${files.length} file đã chọn`;
        } else {
            this.fileList.classList.add('hidden');
            this.infoText.innerText = 'Chưa có file nào được chọn';
        }
        this.startBtn.disabled = files.length === 0 || state.isUploading;
    }

    updateProgress(val, message) {
        this.progressBar.style.width = `${val}%`;
        this.progressPercent.innerText = `${val}%`;
        const messageEl = document.getElementById('progress-message');
        
        if (message) {
            this.progressStatus.innerText = val === 100 ? "Hoàn tất!" : "Đang xử lý...";
            if (messageEl) messageEl.innerText = message;
        } else {
            // Fallback nếu không có message từ server
            if (val < 30) {
                this.progressStatus.innerText = "Đang tải file...";
            } else if (val < 70) {
                this.progressStatus.innerText = "Đang phân tích...";
            } else if (val < 100) {
                this.progressStatus.innerText = "Đang hoàn tất...";
            } else {
                this.progressStatus.innerText = "Hoàn tất!";
            }
        }
    }

    resetProgress() {
        this.progressContainer.classList.add('hidden');
        this.dropzone.classList.remove('hidden');
        if (this.collectionSelect) {
            const inputGroup = this.collectionSelect.closest('.modal-input-group');
            if (inputGroup) inputGroup.classList.remove('hidden');
            this.collectionSelect.selectedIndex = 0;
        }
        if (this.customCollectionWrapper) {
            this.customCollectionWrapper.classList.add('hidden');
        }
        if (this.collectionInput) {
            this.collectionInput.value = '';
        }
        this.progressBar.style.width = '0%';
    }
}
