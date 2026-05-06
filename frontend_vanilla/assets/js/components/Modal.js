/* Modal.js - File Upload Modal logic */
import { state } from '../core/State.js';
import { SELECTORS, CONFIG } from '../core/Config.js';

export class ModalComponent {
    constructor() {
        this.modal = document.querySelector(SELECTORS.UPLOAD_MODAL);
        this.openBtn = document.querySelector(SELECTORS.OPEN_UPLOAD);
        this.closeBtn = document.querySelector(SELECTORS.CLOSE_MODAL);
        this.startBtn = document.querySelector(SELECTORS.START_UPLOAD);
        
        this.dropzone = document.querySelector(SELECTORS.DROPZONE);
        this.fileInput = document.querySelector(SELECTORS.FILE_INPUT);
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

        if (this.startBtn) {
            this.startBtn.addEventListener('click', () => this.upload());
        }

        window.removeFile = (index) => this.removeFile(index);

        state.subscribe((key) => {
            if (key === 'selectedFiles' || key === 'isUploading') this.updateUI();
        });
    }

    show() { this.modal.classList.remove('hidden'); }
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

    async upload() {
        if (state.selectedFiles.length === 0 || state.isUploading) return;
        
        state.isUploading = true;
        this.progressContainer.classList.remove('hidden');
        this.dropzone.classList.add('hidden');
        
        // Mô phỏng upload (Sau này sẽ thay bằng Service gọi tới .NET)
        for (let i = 0; i <= 100; i += 5) {
            this.updateProgress(i);
            await new Promise(r => setTimeout(r, 100));
        }

        setTimeout(() => {
            alert("Tải lên thành công!");
            state.isUploading = false;
            this.hide();
        }, 500);
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

    updateProgress(val) {
        this.progressBar.style.width = `${val}%`;
        this.progressPercent.innerText = `${val}%`;
        const messageEl = document.getElementById('progress-message');
        
        if (val < 30) {
            this.progressStatus.innerText = "Đang tải file...";
            if (messageEl) messageEl.innerText = "Đang truyền dữ liệu lên server";
        } else if (val < 70) {
            this.progressStatus.innerText = "Đang phân tích...";
            if (messageEl) messageEl.innerText = "Hệ thống đang trích xuất metadata";
        } else if (val < 100) {
            this.progressStatus.innerText = "Đang hoàn tất...";
            if (messageEl) messageEl.innerText = "Đang lưu trữ vào cơ sở dữ liệu vector";
        } else {
            this.progressStatus.innerText = "Hoàn tất!";
            if (messageEl) messageEl.innerText = "Tài liệu đã sẵn sàng để truy vấn";
        }
    }

    resetProgress() {
        this.progressContainer.classList.add('hidden');
        this.dropzone.classList.remove('hidden');
        this.progressBar.style.width = '0%';
    }
}
