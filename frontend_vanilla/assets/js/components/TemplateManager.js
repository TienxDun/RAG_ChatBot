// TemplateManager.js - Excel template configuration workspace component
import { state } from '../core/State.js';
import { ApiClient } from '../core/ApiClient.js';
import { TemplateCacheService } from '../services/TemplateCacheService.js';
import { Toast } from './Toast.js';

export class TemplateManagerComponent {
    constructor() {
        this.container = document.getElementById('template-manager-page');
        this.templates = [];
        this.selectedTemplate = null;
        this.selectedTemplateData = null; // Chứa dữ liệu phân tích chi tiết (columns, grid, metadata, mappings)
        this.activeTab = 'notes'; // 'notes' | 'grid'
        this.selectedCellAddress = null; // Ô Excel đang được chọn để gán biến
        
        // Dữ liệu cấu hình tạm thời
        this.columnMappings = {};
        this.metadataCellMappings = {};

        this.init();
    }

    init() {
        // Lắng nghe sự thay đổi của activePage trong State
        state.subscribe((key, value) => {
            if (key === 'activePage' && value === 'templates') {
                console.log('🔍 TemplateManager: Navigating to templates page...');
                this.renderLayout();
                this.loadTemplatesList();
            }
        });
    }

    /**
     * Render cấu trúc Layout Split-View chính của trang Template Manager
     */
    renderLayout() {
        if (!this.container) {
            console.error('❌ TemplateManager: container is null!');
            return;
        }
        if (this.container.querySelector('.template-manager-layout')) {
            return; 
        }
        this.container.innerHTML = `
            <div class="template-manager-layout">
                <!-- Panel bên trái: Danh sách file & Kéo thả tải lên -->
                <div class="template-list-panel">
                    <div class="template-panel-header">
                        <h3><i class="ph-bold ph-microsoft-excel-logo"></i> Mẫu báo cáo Excel</h3>
                    </div>

                    <div class="template-upload-area">
                        <div class="template-mini-dropzone" id="template-dropzone">
                            <i class="ph-duotone ph-cloud-arrow-up"></i>
                            <span class="dropzone-text">Kéo thả tệp mẫu vào đây</span>
                            <span class="dropzone-subtext">hoặc click để chọn file Excel (.xlsx)</span>
                            <input type="file" id="template-file-input" hidden accept=".xlsx">
                        </div>
                    </div>

                    <div class="template-items-container" id="template-items-list">
                        <!-- Danh sách các file template sẽ render động -->
                        <div class="template-empty">
                            <i class="ph-duotone ph-folders"></i>
                            <span>Chưa có mẫu Excel nào</span>
                        </div>
                    </div>
                </div>

                <!-- Panel bên phải: Chi tiết cấu hình (Ghi chú cột & Lưới ô trực quan) -->
                <div class="template-detail-panel" id="template-detail-view">
                    <div class="template-detail-empty">
                        <i class="ph-duotone ph-browsers"></i>
                        <h4>Chưa chọn tệp mẫu</h4>
                        <p>Chọn một tệp mẫu Excel bên danh sách trái hoặc tải lên tệp mới để bắt đầu cấu hình vị trí điền dữ liệu.</p>
                    </div>
                </div>
            </div>
        `;

        this.bindUploadEvents();
    }

    /**
     * Gán các sự kiện Kéo thả và click để tải file Excel
     */
    bindUploadEvents() {
        const dropzone = document.getElementById('template-dropzone');
        const fileInput = document.getElementById('template-file-input');

        if (!dropzone || !fileInput) return;

        dropzone.addEventListener('click', () => fileInput.click());

        fileInput.addEventListener('change', (e) => {
            if (e.target.files.length > 0) {
                this.handleUploadFile(e.target.files[0]);
            }
        });

        // Xử lý sự kiện kéo thả
        dropzone.addEventListener('dragover', (e) => {
            e.preventDefault();
            dropzone.style.borderColor = 'var(--primary)';
            dropzone.style.backgroundColor = 'rgba(124, 58, 237, 0.05)';
        });

        dropzone.addEventListener('dragleave', () => {
            dropzone.style.borderColor = '';
            dropzone.style.backgroundColor = '';
        });

        dropzone.addEventListener('drop', (e) => {
            e.preventDefault();
            dropzone.style.borderColor = '';
            dropzone.style.backgroundColor = '';
            if (e.dataTransfer.files.length > 0) {
                this.handleUploadFile(e.dataTransfer.files[0]);
            }
        });
    }

    /**
     * Thực hiện tải tệp Excel lên server cache RAM
     */
    async handleUploadFile(file) {
        if (!file.name.endsWith('.xlsx')) {
            this.showToast('Chỉ hỗ trợ tải lên tệp tin Excel (.xlsx)', 'error');
            return;
        }

        this.showToast('Đang tải lên và phân tích tệp mẫu...', 'info');

        const cached = await TemplateCacheService.cacheTemplate(file);
        if (cached) {
            this.showToast('Tải lên tệp mẫu thành công!', 'success');
            await this.loadTemplatesList();
            
            // Tự động chọn và hiển thị cấu hình cho tệp mới tải lên
            this.selectTemplate(cached.id, cached.fileName);
        } else {
            this.showToast('Tải lên tệp mẫu thất bại. Vui lòng kiểm tra lại.', 'error');
        }
    }

    /**
     * Tải danh sách tệp template hiện có từ cache server
     */
    async loadTemplatesList() {
        const listContainer = document.getElementById('template-items-list');
        if (!listContainer) return;

        this.templates = await TemplateCacheService.getAll();

        if (this.templates.length === 0) {
            listContainer.innerHTML = `
                <div class="template-empty">
                    <i class="ph-duotone ph-folders"></i>
                    <span>Chưa có mẫu Excel nào</span>
                </div>
            `;
            return;
        }

        listContainer.innerHTML = this.templates.map(item => {
            const isActive = this.selectedTemplate && this.selectedTemplate.id === item.id;
            const sizeKB = (item.fileSize / 1024).toFixed(1);
            return `
                <div class="template-card ${isActive ? 'active' : ''}" data-id="${item.id}" data-filename="${item.fileName}">
                    <i class="ph-bold ph-file-xls template-card-icon"></i>
                    <div class="template-card-details">
                        <span class="template-card-name" title="${item.fileName}">${item.fileName}</span>
                        <span class="template-card-size">${sizeKB} KB</span>
                    </div>
                    <button class="btn-delete-template-card" title="Xóa mẫu này" data-id="${item.id}">
                        <i class="ph-bold ph-trash"></i>
                    </button>
                </div>
            `;
        }).join('');

        // Gán sự kiện click cho từng template card
        listContainer.querySelectorAll('.template-card').forEach(card => {
            card.addEventListener('click', (e) => {
                // Nếu click vào nút xóa thì bỏ qua
                if (e.target.closest('.btn-delete-template-card')) return;
                
                const id = card.getAttribute('data-id');
                const filename = card.getAttribute('data-filename');
                this.selectTemplate(id, filename);
            });
        });

        // Gán sự kiện click xóa template card
        listContainer.querySelectorAll('.btn-delete-template-card').forEach(btn => {
            btn.addEventListener('click', async (e) => {
                e.stopPropagation();
                const id = btn.getAttribute('data-id');
                if (confirm('Bạn có chắc chắn muốn xóa tệp mẫu này cùng toàn bộ cấu hình ánh xạ của nó không?')) {
                    const success = await TemplateCacheService.removeTemplate(id);
                    if (success) {
                        this.showToast('Đã xóa tệp mẫu thành công!', 'success');
                        if (this.selectedTemplate && this.selectedTemplate.id === id) {
                            this.selectedTemplate = null;
                            this.selectedTemplateData = null;
                            this.renderEmptyDetails();
                        }
                        this.loadTemplatesList();
                    } else {
                        this.showToast('Không thể xóa tệp mẫu này.', 'error');
                    }
                }
            });
        });
    }

    /**
     * Hiển thị trạng thái chưa chọn tệp ở panel bên phải
     */
    renderEmptyDetails() {
        const detailView = document.getElementById('template-detail-view');
        if (detailView) {
            detailView.innerHTML = `
                <div class="template-detail-empty">
                    <i class="ph-duotone ph-browsers"></i>
                    <h4>Chưa chọn tệp mẫu</h4>
                    <p>Chọn một tệp mẫu Excel bên danh sách trái hoặc tải lên tệp mới để bắt đầu cấu hình vị trí điền dữ liệu.</p>
                </div>
            `;
        }
        const layout = this.container.querySelector('.template-manager-layout');
        if (layout) {
            layout.classList.remove('show-detail');
        }
    }

    /**
     * Chọn và hiển thị cấu hình cho một tệp template
     */
    async selectTemplate(id, filename) {
        this.selectedTemplate = { id, fileName: filename };
        
        // Thêm class active cho card tương ứng
        const cards = document.querySelectorAll('.template-card');
        cards.forEach(card => {
            if (card.getAttribute('data-id') === id) {
                card.classList.add('active');
            } else {
                card.classList.remove('active');
            }
        });

        // Thêm class show-detail cho layout trên di động
        const layout = this.container.querySelector('.template-manager-layout');
        if (layout) {
            layout.classList.add('show-detail');
        }

        await this.analyzeAndLoadWorkspace(id, filename);
    }

    /**
     * Phân tích tệp Excel bằng cách tải file blob về và gửi lên api/templates/analyze
     */
    async analyzeAndLoadWorkspace(id, filename) {
        const detailView = document.getElementById('template-detail-view');
        if (!detailView) return;

        // Render màn hình loading
        detailView.innerHTML = `
            <div class="template-detail-empty">
                <i class="ph-bold ph-spinner animate-spin" style="font-size: 3rem; color: var(--primary); opacity: 1;"></i>
                <h4>Đang phân tích cấu trúc Excel...</h4>
                <p>Hệ thống đang tải dữ liệu và dựng lưới bảng Excel trực quan. Vui lòng đợi giây lát.</p>
            </div>
        `;

        try {
            // Tải file excel blob về từ cache server
            const blob = await TemplateCacheService.downloadTemplate(id);
            if (!blob) {
                this.showToast('Không thể tải file mẫu để phân tích.', 'error');
                this.renderEmptyDetails();
                return;
            }

            // Dựng thành File object gửi lên API analyze
            const file = new File([blob], filename, { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
            
            const formData = new FormData();
            formData.append('file', file);

            const url = ApiClient._resolveUrl('/templates/analyze');
            const response = await fetch(url, {
                method: 'POST',
                body: formData
            });

            if (!response.ok) {
                throw new Error('Analyze failed');
            }

            const result = await response.json();
            this.selectedTemplateData = result;

            // Đồng bộ dữ liệu ánh xạ hiện tại
            this.columnMappings = result.savedMappings || {};
            this.metadataCellMappings = result.metadataCellMappings || {};

            // Render workspace cấu hình
            this.renderWorkspace();

        } catch (error) {
            console.error('❌ Failed to analyze template:', error);
            this.showToast('Lỗi phân tích tệp Excel mẫu.', 'error');
            this.renderEmptyDetails();
        }
    }

    /**
     * Render không gian cấu hình template Excel đầy đủ
     */
    renderWorkspace() {
        const detailView = document.getElementById('template-detail-view');
        if (!detailView || !this.selectedTemplateData) return;

        const data = this.selectedTemplateData;
        const sizeKB = (this.selectedTemplate.fileSize || 0) / 1024;
        
        detailView.innerHTML = `
            <div class="template-workspace">
                <!-- Header Workspace -->
                <div class="template-workspace-header">
                    <button id="btn-back-to-list" class="btn-back-to-list" title="Quay lại danh sách tệp mẫu">
                        <i class="ph-bold ph-arrow-left"></i>
                        <span>Danh sách</span>
                    </button>
                    <div class="template-workspace-info">
                        <h2><i class="ph-bold ph-file-spreadsheet" style="color: #1d6f42;"></i> ${this.selectedTemplate.fileName}</h2>
                        <p>Loại bảng mẫu phát hiện: <strong>${data.type === 'Hierarchical' ? 'Tiêu đề gộp phân tầng (Hierarchical)' : 'Bảng phẳng (Horizontal)'}</strong> | Dòng tiêu đề cột: <strong>Dòng ${data.headerRowIndex}</strong></p>
                    </div>
                    <button class="btn-save-mapping" id="btn-save-template-mapping">
                        <i class="ph-bold ph-floppy-disk"></i> <span>Lưu cấu hình</span>
                    </button>
                </div>

                <!-- Tab bar -->
                <div class="template-tabs-container">
                    <button class="template-tab-btn ${this.activeTab === 'notes' ? 'active' : ''}" data-tab="notes">
                        <i class="ph-bold ph-chat-centered-text"></i> Ghi chú chú thích cột
                    </button>
                    <button class="template-tab-btn ${this.activeTab === 'grid' ? 'active' : ''}" data-tab="grid">
                        <i class="ph-bold ph-grid-four"></i> Thiết lập lưới ô Metadata
                    </button>
                </div>

                <!-- Body chứa nội dung Tab -->
                <div class="template-workspace-body">
                    <!-- Tab 1: Column Notes -->
                    <div class="template-tab-content ${this.activeTab === 'notes' ? 'active' : ''}" id="tab-notes-content" style="position: relative;">
                        <!-- AI Loading Overlay -->
                        <div class="ai-loading-overlay" id="ai-mapping-loading">
                            <div class="ai-loading-spinner">
                                <div class="ai-loading-spinner-circle"></div>
                                <div class="ai-loading-spinner-inner"></div>
                            </div>
                            <div class="ai-loading-text">Trợ lý AI đang lập bản mapping...</div>
                            <div class="ai-loading-subtext">Đang đối chiếu schema Qdrant & phân tích cấu hình cột bằng Gemini</div>
                        </div>

                        <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; gap: 15px; flex-shrink: 0;">
                            <p style="font-size: 0.825rem; color: var(--muted-foreground); line-height: 1.5; margin: 0;">
                                Hãy mô tả hoặc chú thích chi tiết thông tin cho từng cột tiêu đề của file Excel dưới đây. 
                                Điều này giúp Trợ lý AI (RAG) hiểu rõ ý nghĩa của cột để truy vấn dữ liệu chính xác nhất.
                            </p>
                            <button class="btn-auto-map" id="btn-auto-map-columns">
                                <i class="ph-bold ph-sparkle"></i> Tự động điền bằng AI
                            </button>
                        </div>
                        <div class="column-mapping-scroll-container" style="flex: 1; overflow-y: auto; padding-right: 4px;">
                            ${this.renderColumnMappingInputs()}
                        </div>
                    </div>

                    <!-- Tab 2: Spreadsheet Grid -->
                    <div class="template-tab-content ${this.activeTab === 'grid' ? 'active' : ''}" id="tab-grid-content">
                        <div class="spreadsheet-grid-wrapper">
                            <div class="spreadsheet-grid-header">
                                <div class="grid-instructions">
                                    <i class="ph-bold ph-info"></i> Click trực tiếp vào một ô trống bất kỳ trên Lưới để gán biến metadata cần điền dữ liệu.
                                </div>
                            </div>
                            <div class="grid-scrollable-container" id="excel-grid-viewport">
                                ${this.renderExcelHtmlGrid()}
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `;

        this.bindWorkspaceEvents();
    }

    /**
     * Render các trường input cho chú thích cột Excel
     */
    renderColumnMappingInputs() {
        const columns = this.selectedTemplateData.columns || [];
        if (columns.length === 0) {
            return `<div class="template-empty">Không phát hiện cột tiêu đề nào</div>`;
        }

        let html = `
            <table class="column-mapping-table">
                <thead>
                    <tr>
                        <th style="width: 35%;">Tên cột tiêu đề (Excel)</th>
                        <th style="width: 20%;">Vị trí cột</th>
                        <th style="width: 45%;">Ý nghĩa cột dữ liệu (AI Chú thích)</th>
                    </tr>
                </thead>
                <tbody>
        `;

        html += columns.map(col => {
            const displayName = col.parentHeader 
                ? `${col.parentHeader} ➔ ${col.childHeader}` 
                : col.childHeader;
            const savedValue = this.columnMappings[col.uniqueKey] || '';
            const colLetter = this.getColumnLetter(col.columnIndex);
            
            return `
                <tr class="column-mapping-row">
                    <td>
                        <span class="column-name">${displayName}</span>
                    </td>
                    <td>
                        <span class="col-meta-badge">
                            <i class="ph-bold ph-file-spreadsheet col-excel-icon"></i>
                            Cột ${colLetter} (Chỉ số: ${col.columnIndex})
                        </span>
                    </td>
                    <td>
                        <div class="column-input-wrapper">
                            <textarea class="column-note-input" 
                                      data-key="${col.uniqueKey}" 
                                      placeholder="Ví dụ: Tên đầy đủ của nhân viên làm nhiệm vụ kiểm tra chất lượng KCS...">${savedValue}</textarea>
                        </div>
                    </td>
                </tr>
            `;
        }).join('');

        html += '</tbody></table>';
        return html;
    }

    /**
     * Dựng bảng lưới ô Excel trực quan từ Grid 2D gửi về từ server
     */
    renderExcelHtmlGrid() {
        const grid = this.selectedTemplateData.grid || [];
        if (grid.length === 0) return '<div class="template-empty">Bảng Excel trống</div>';

        let html = '<table class="excel-table"><thead><tr>';
        
        // Dòng header A, B, C...
        html += '<th class="row-num-header"></th>'; // ô góc trái trên
        const colCount = grid[0].length;
        for (let c = 1; c <= colCount; c++) {
            html += `<th>${this.getColumnLetter(c)}</th>`;
        }
        html += '</tr></thead><tbody>';

        // Vẽ từng dòng
        grid.forEach((row, rowIndex) => {
            const rowNum = rowIndex + 1;
            html += `<tr><td class="row-num-header">${rowNum}</td>`;
            
            row.forEach(cell => {
                // Nếu là cell con của merged range thì bỏ qua
                if (cell.isMergedChild) return;

                const address = cell.address;
                const value = cell.value || '';
                const isBold = cell.isBold ? 'font-weight: bold;' : '';
                const rowspanAttr = cell.rowSpan > 1 ? `rowspan="${cell.rowSpan}"` : '';
                const colspanAttr = cell.colSpan > 1 ? `colspan="${cell.colSpan}"` : '';
                
                // Kiểm tra xem cell này có đang được map metadata không
                const mappedVarName = this.metadataCellMappings[address];
                const mappedClass = mappedVarName ? 'mapped-cell' : '';
                const badgeHtml = mappedVarName 
                    ? `<span class="mapped-badge" title="${mappedVarName}">${mappedVarName}</span>` 
                    : '';

                html += `<td ${rowspanAttr} ${colspanAttr} style="${isBold}" class="${mappedClass}" data-cell-address="${address}">
                    ${value}
                    ${badgeHtml}
                </td>`;
            });
            html += '</tr>';
        });

        html += '</tbody></table>';
        return html;
    }

    /**
     * Trả về Chữ cái cột Excel (A, B, C... Z, AA, AB...) dựa trên Column Index (1-based)
     */
    getColumnLetter(colIndex) {
        let letter = '';
        while (colIndex > 0) {
            let temp = (colIndex - 1) % 26;
            letter = String.fromCharCode(65 + temp) + letter;
            colIndex = Math.floor((colIndex - temp) / 26);
        }
        return letter;
    }

    /**
     * Đăng ký sự kiện của Workspace sau khi render
     */
    bindWorkspaceEvents() {
        // Nút quay lại danh sách trên di động
        const backBtn = document.getElementById('btn-back-to-list');
        if (backBtn) {
            backBtn.addEventListener('click', () => {
                const layout = this.container.querySelector('.template-manager-layout');
                if (layout) {
                    layout.classList.remove('show-detail');
                }
                // Bỏ chọn active card
                const cards = document.querySelectorAll('.template-card');
                cards.forEach(card => card.classList.remove('active'));
                
                this.selectedTemplate = null;
                this.selectedTemplateData = null;
                this.renderEmptyDetails();
            });
        }

        // Tab switching
        const tabBtns = this.container.querySelectorAll('.template-tab-btn');
        tabBtns.forEach(btn => {
            btn.addEventListener('click', () => {
                const targetTab = btn.getAttribute('data-tab');
                this.activeTab = targetTab;
                
                // Switch button active class
                tabBtns.forEach(b => b.classList.toggle('active', b === btn));
                
                // Toggle tab content visibility
                document.getElementById('tab-notes-content').classList.toggle('active', targetTab === 'notes');
                document.getElementById('tab-grid-content').classList.toggle('active', targetTab === 'grid');
                
                // Tự động căn khít chiều cao khi quay lại tab notes
                if (targetTab === 'notes') {
                    setTimeout(() => this.adjustTextareaHeights(), 50);
                }
            });
        });

        // Ghi nhận thay đổi của input Column Notes & tự động resize chiều cao
        const noteInputs = this.container.querySelectorAll('.column-note-input');
        noteInputs.forEach(input => {
            input.addEventListener('input', () => {
                const key = input.getAttribute('data-key');
                this.columnMappings[key] = input.value.trim();
                
                // Căn khít chiều cao tức thì khi gõ
                input.style.height = 'auto';
                input.style.height = (input.scrollHeight + 2) + 'px';
            });
        });

        // Căn chỉnh chiều cao của các textarea ngay sau khi load xong giao diện
        setTimeout(() => this.adjustTextareaHeights(), 100);

        // Click chọn cell trong Excel Grid để hiển thị Editor gán biến
        const cells = this.container.querySelectorAll('.excel-table tbody td:not(.row-num-header)');
        cells.forEach(cell => {
            cell.addEventListener('click', (e) => {
                e.stopPropagation();
                
                // Gỡ class selected-cell của các ô khác
                cells.forEach(c => c.classList.remove('selected-cell'));
                cell.classList.add('selected-cell');

                const address = cell.getAttribute('data-cell-address');
                this.selectedCellAddress = address;
                this.showCellMappingEditor(cell, address);
            });
        });

        // Lưu cấu hình
        const saveBtn = document.getElementById('btn-save-template-mapping');
        if (saveBtn) {
            saveBtn.addEventListener('click', () => this.handleSaveMappings());
        }

        // Tự động phân tích & gán chú thích cột bằng AI RAG
        const autoMapBtn = document.getElementById('btn-auto-map-columns');
        if (autoMapBtn) {
            autoMapBtn.addEventListener('click', () => this.handleAutoMapColumns());
        }

        // Tự động tắt popup mapping editor khi click ra ngoài
        document.addEventListener('click', (e) => {
            const editor = document.getElementById('grid-cell-mapping-editor');
            if (editor && !editor.contains(e.target)) {
                this.closeCellMappingEditor();
            }
        });
    }

    /**
     * Tự động gọi API phân tích và tạo chú thích cột Excel qua AI dựa trên schema từ Qdrant
     */
    async handleAutoMapColumns() {
        if (!this.selectedTemplate || !this.selectedTemplateData) return;

        const autoMapBtn = document.getElementById('btn-auto-map-columns');
        const loadingOverlay = document.getElementById('ai-mapping-loading');
        
        if (autoMapBtn) {
            autoMapBtn.disabled = true;
            autoMapBtn.innerHTML = `<i class="ph-bold ph-spinner animate-spin"></i> Đang phân tích...`;
        }
        
        if (loadingOverlay) {
            loadingOverlay.classList.add('show');
        }

        try {
            // Lấy danh sách cột hiện có để gửi lên API
            const columns = this.selectedTemplateData.columns.map(col => ({
                uniqueKey: col.uniqueKey,
                childHeader: col.childHeader,
                parentHeader: col.parentHeader
            }));

            // Sử dụng collection hiện tại hoặc mặc định
            const collectionSelect = document.getElementById('chat-collection-select');
            const collectionName = collectionSelect ? collectionSelect.value : 'db_schema';

            const payload = {
                fileName: this.selectedTemplate.fileName,
                collectionName: collectionName,
                columns: columns
            };

            const response = await ApiClient.post('/templates/auto-map', payload);
            
            if (response && response.mappings) {
                // Đổ dữ liệu mapping tự động vào object cục bộ
                const newMappings = response.mappings;
                Object.keys(newMappings).forEach(key => {
                    this.columnMappings[key] = newMappings[key];
                });

                // Render lại phần bảng nhập liệu của Tab Column Notes
                const scrollContainer = this.container.querySelector('.column-mapping-scroll-container');
                if (scrollContainer) {
                    scrollContainer.innerHTML = this.renderColumnMappingInputs();
                    
                    // Gán lại sự kiện input cho các trường mới sinh ra
                    const noteInputs = this.container.querySelectorAll('.column-note-input');
                    noteInputs.forEach(input => {
                        input.addEventListener('input', () => {
                            const key = input.getAttribute('data-key');
                            this.columnMappings[key] = input.value.trim();
                            
                            // Co giãn chiều cao động khi thay đổi thủ công
                            input.style.height = 'auto';
                            input.style.height = (input.scrollHeight + 2) + 'px';
                        });
                    });
                    
                    // Căn chỉnh chiều cao khít với nội dung AI vừa điền
                    setTimeout(() => this.adjustTextareaHeights(), 50);
                }

                this.showToast('AI đã tự động lập bản mapping và giải nghĩa cột thành công!', 'success');
            } else {
                this.showToast('Không nhận được kết quả mapping từ AI.', 'error');
            }
        } catch (error) {
            console.error('❌ Failed to auto map columns:', error);
            this.showToast('Lỗi khi thực hiện tự động ánh xạ bằng AI.', 'error');
        } finally {
            if (loadingOverlay) {
                loadingOverlay.classList.remove('show');
            }
            if (autoMapBtn) {
                autoMapBtn.disabled = false;
                autoMapBtn.innerHTML = `<i class="ph-bold ph-sparkle"></i> Tự động điền bằng AI`;
            }
        }
    }

    /**
     * Hiển thị popup Editor nhỏ nổi trên ô Excel đang chọn để gán biến metadata
     */
    showCellMappingEditor(cellElement, address) {
        this.closeCellMappingEditor(); // Tắt popup cũ nếu có

        // Lấy danh sách các biến metadata có sẵn từ dữ liệu analyze
        const metadataMap = this.selectedTemplateData.metadata || {};
        const metadataOptions = Object.keys(metadataMap).map(key => {
            return `<option value="${key}">${key} (${metadataMap[key]})</option>`;
        }).join('');

        const currentMappedValue = this.metadataCellMappings[address] || '';

        const editor = document.createElement('div');
        editor.id = 'grid-cell-mapping-editor';
        editor.className = 'cell-mapping-editor';
        
        editor.innerHTML = `
            <button class="editor-close-btn" id="editor-btn-close" style="position: absolute; right: 8px; top: 8px; border: none; background: transparent; color: var(--muted-foreground); cursor: pointer; display: flex; align-items: center; justify-content: center; width: 20px; height: 20px; border-radius: 4px; transition: all 0.2s;" onmouseover="this.style.background='rgba(0,0,0,0.05)';" onmouseout="this.style.background='transparent';">
                <i class="ph-bold ph-x" style="font-size: 0.85rem;"></i>
            </button>
            <span class="editor-label">Thiết lập ô: ${address}</span>
            <div style="display:flex; flex-direction:column; gap:6px;">
                <label style="font-size:0.75rem; color:var(--muted-foreground); margin-top:4px;">Nhập biến tùy chỉnh:</label>
                <textarea id="editor-input-custom-var" rows="3" style="resize:vertical; font-family:inherit; padding:8px; border:1px solid var(--border); border-radius:6px; background:var(--background); color:var(--foreground); font-size:0.875rem;" placeholder="Ví dụ: Lấy thông tin Mã hàng và Style...">${currentMappedValue}</textarea>
            </div>
            <div class="editor-actions">
                <button class="editor-btn editor-btn-cancel" id="editor-btn-cancel">Hủy</button>
                ${currentMappedValue ? `<button class="editor-btn editor-btn-clear" id="editor-btn-clear">Xóa</button>` : ''}
                <button class="editor-btn editor-btn-apply" id="editor-btn-apply">Áp dụng</button>
            </div>
        `;

        // Đưa editor vào container cuộn của Grid (thay vì document.body)
        const container = document.getElementById('excel-grid-viewport');
        if (container) {
            container.appendChild(editor);
            editor.style.position = 'absolute';
            
            // Tính toán vị trí tương đối ổn định theo tọa độ offset
            let left = cellElement.offsetLeft;
            let top = cellElement.offsetTop + cellElement.offsetHeight + 6;
            
            const editorWidth = 240;
            const editorHeight = 210;
            
            // Chống tràn biên phải (nếu ô Excel ở sát lề phải)
            if (left + editorWidth > container.scrollWidth) {
                left = Math.max(0, left + cellElement.offsetWidth - editorWidth);
            }
            
            // Chống tràn biên dưới (nếu ô Excel ở sát lề dưới, hiển thị editor lên phía trên ô)
            if (top + editorHeight > container.scrollHeight) {
                top = Math.max(0, cellElement.offsetTop - editorHeight - 6);
            }
            
            editor.style.left = `${left}px`;
            editor.style.top = `${top}px`;
        } else {
            // Phương án dự phòng fixed viewport
            document.body.appendChild(editor);
            const rect = cellElement.getBoundingClientRect();
            editor.style.position = 'fixed';
            editor.style.left = `${rect.left}px`;
            editor.style.top = `${rect.bottom + 6}px`;
        }

        const inputCustom = document.getElementById('editor-input-custom-var');

        // Bắt sự kiện click các nút chức năng trong editor
        document.getElementById('editor-btn-close').addEventListener('click', () => {
            this.closeCellMappingEditor();
        });

        document.getElementById('editor-btn-cancel').addEventListener('click', () => {
            this.closeCellMappingEditor();
        });

        const clearBtn = document.getElementById('editor-btn-clear');
        if (clearBtn) {
            clearBtn.addEventListener('click', () => {
                delete this.metadataCellMappings[address];
                this.closeCellMappingEditor();
                this.refreshExcelTableUi();
            });
        }

        document.getElementById('editor-btn-apply').addEventListener('click', () => {
            const varName = inputCustom.value.trim();
            if (varName) {
                this.metadataCellMappings[address] = varName;
            } else {
                delete this.metadataCellMappings[address];
            }
            this.closeCellMappingEditor();
            this.refreshExcelTableUi();
        });
    }

    /**
     * Tắt popup editor
     */
    closeCellMappingEditor() {
        const oldEditor = document.getElementById('grid-cell-mapping-editor');
        if (oldEditor) {
            oldEditor.remove();
        }
        
        // Gỡ highlight của ô
        const cells = this.container.querySelectorAll('.excel-table tbody td:not(.row-num-header)');
        cells.forEach(c => c.classList.remove('selected-cell'));
    }

    /**
     * Làm mới giao diện bảng Excel sau khi thay đổi cấu hình mà không cần nạp lại toàn bộ trang
     */
    refreshExcelTableUi() {
        const gridViewport = document.getElementById('excel-grid-viewport');
        if (gridViewport) {
            gridViewport.innerHTML = this.renderExcelHtmlGrid();
            this.bindWorkspaceEvents(); // Gán lại các sự kiện click ô
        }
    }

    /**
     * Gửi toàn bộ cấu hình ánh xạ Excel (Column Notes & Grid metadata cell mappings) lên server lưu trữ
     */
    async handleSaveMappings() {
        if (!this.selectedTemplate) return;

        const saveBtn = document.getElementById('btn-save-template-mapping');
        if (saveBtn) saveBtn.disabled = true;

        const payload = {
            fileName: this.selectedTemplate.fileName,
            mappings: this.columnMappings,
            metadataCellMappings: this.metadataCellMappings
        };

        try {
            const response = await ApiClient.post('/templates/save-mapping', payload);
            this.showToast('Lưu cấu hình template Excel thành công!', 'success');
            
            // Cập nhật lại cache phân tích cục bộ
            if (this.selectedTemplateData) {
                this.selectedTemplateData.savedMappings = this.columnMappings;
                this.selectedTemplateData.metadataCellMappings = this.metadataCellMappings;
            }
        } catch (error) {
            console.error('❌ Failed to save template mappings:', error);
            this.showToast('Lưu cấu hình thất bại. Vui lòng kiểm tra lại kết nối.', 'error');
        } finally {
            if (saveBtn) saveBtn.disabled = false;
        }
    }

    /**
     * Helper hiển thị Toast thông báo nhanh chóng
     */
    showToast(message, type = 'info') {
        if (type === 'success') {
            Toast.success(message);
        } else if (type === 'error') {
            Toast.error(message);
        } else if (type === 'warning') {
            Toast.warning(message);
        } else {
            Toast.info(message);
        }
    }

    /**
     * Tự động điều chỉnh chiều cao của các textarea chứa chú thích cho khít với nội dung
     */
    adjustTextareaHeights() {
        if (!this.container) return;
        const textareas = this.container.querySelectorAll('.column-note-input');
        textareas.forEach(textarea => {
            textarea.style.height = 'auto';
            // scrollHeight + 2px để bù trừ phần viền border
            textarea.style.height = (textarea.scrollHeight + 2) + 'px';
        });
    }
}
