// TestingManager.js - Auto Testing Workspace component
import { state } from '../core/State.js';
import { ApiClient } from '../core/ApiClient.js';
import { ENDPOINTS } from '../core/Config.js';
import { Toast } from './Toast.js';
import { MessageRenderer } from './MessageRenderer.js';
import { InteractionService } from '../services/InteractionService.js';


export class TestingManagerComponent {
    constructor() {
        this.container = document.getElementById('testing-page');
        this.testCases = []; // Danh sách toàn bộ test case từ backend
        this.queue = [];     // Stack câu hỏi đang chờ chạy
        this.isRunning = false;
        this.shouldStop = false;
        this.currentAbortController = null;
        this.results = []; // Lưu kết quả thực thi chi tiết
        this.activeResultIndex = -1; // Index câu hỏi đang được xem chi tiết
        this.isEditing = false;
        this.originalTestCases = [];
        this.editingQuestionKey = null;
        
        // Khôi phục hàng đợi từ localStorage nếu có
        try {
            const savedQueue = localStorage.getItem('dodo_testing_queue');
            if (savedQueue) {
                this.queue = JSON.parse(savedQueue);
            }
        } catch (e) {
            console.error('Không thể khôi phục hàng đợi test cases:', e);
        }

        this.init();
    }

    init() {
        // Đăng ký chuyển đổi tab trang Kiểm thử
        state.subscribe((key, value) => {
            if (key === 'activePage') {
                if (value === 'testing') {
                    console.log('🔍 TestingManager: Navigating to testing page...');
                    this.container.classList.remove('hidden');
                    
                    // Khởi tạo các sự kiện và tải test cases lần đầu tiên
                    this.setupUI();
                    this.setupPanelResizers();
                    this.loadCollections();
                    if (this.testCases.length === 0) {
                        this.loadTestCases();
                    }
                } else {
                    this.container.classList.add('hidden');
                    // Nếu đang chạy mà chuyển trang khác thì dừng chạy
                    if (this.isRunning) {
                        this.stopTesting(true);
                    }
                }
            }
        });
    }

    setupUI() {
        // Gán sự kiện cho các nút điều khiển hàng đợi
        const btnRun = document.getElementById('btn-run-testing');
        const btnStop = document.getElementById('btn-stop-testing');
        const btnClear = document.getElementById('btn-clear-queue');
        const btnAddCustom = document.getElementById('btn-add-custom-question');
        const inputCustom = document.getElementById('input-custom-question');
        const btnAddAll = document.getElementById('btn-add-all-testcases');

        if (btnRun && !btnRun.dataset.bound) {
            btnRun.addEventListener('click', () => this.runSequential());
            btnRun.dataset.bound = 'true';
        }

        if (btnStop && !btnStop.dataset.bound) {
            btnStop.addEventListener('click', () => this.stopTesting());
            btnStop.dataset.bound = 'true';
        }

        if (btnClear && !btnClear.dataset.bound) {
            btnClear.addEventListener('click', () => this.clearQueue());
            btnClear.dataset.bound = 'true';
        }

        if (btnAddCustom && !btnAddCustom.dataset.bound) {
            const addAction = () => {
                const question = inputCustom.value.trim();
                if (question) {
                    this.addToQueue(question);
                    inputCustom.value = '';
                }
            };
            btnAddCustom.addEventListener('click', addAction);
            inputCustom.addEventListener('keypress', (e) => {
                if (e.key === 'Enter') addAction();
            });
            btnAddCustom.dataset.bound = 'true';
        }

        if (btnAddAll && !btnAddAll.dataset.bound) {
            btnAddAll.addEventListener('click', () => {
                if (this.testCases.length === 0) return;
                let count = 0;
                this.testCases.forEach(sec => {
                    sec.questions.forEach(q => {
                        this.queue.push(q);
                        count++;
                    });
                });
                this.saveQueue();
                this.renderQueue();
                Toast.show(`Đã thêm toàn bộ ${count} câu hỏi vào hàng đợi`, 'success');
            });
            btnAddAll.dataset.bound = 'true';
        }

        const btnToggleEdit = document.getElementById('btn-toggle-edit-testcases');
        const btnSaveTestCases = document.getElementById('btn-save-testcases');
        const btnCancelEdit = document.getElementById('btn-cancel-edit-testcases');

        if (btnToggleEdit && !btnToggleEdit.dataset.bound) {
            btnToggleEdit.addEventListener('click', () => this.toggleEditMode());
            btnToggleEdit.dataset.bound = 'true';
        }

        if (btnSaveTestCases && !btnSaveTestCases.dataset.bound) {
            btnSaveTestCases.addEventListener('click', () => this.saveTestCasesToServer());
            btnSaveTestCases.dataset.bound = 'true';
        }

        if (btnCancelEdit && !btnCancelEdit.dataset.bound) {
            btnCancelEdit.addEventListener('click', () => this.cancelEditing());
            btnCancelEdit.dataset.bound = 'true';
        }

        this.renderQueue();
        this.setupResponsiveCollapse();
    }

    async loadTestCases() {
        const accordionContainer = document.getElementById('testcase-list-accordion');
        if (!accordionContainer) return;

        try {
            const data = await ApiClient.get('/testcases');
            this.testCases = data;
            this.renderTestCases();
        } catch (error) {
            console.error('Không thể tải test cases:', error);
            accordionContainer.innerHTML = `
                <div class="loading-state text-destructive">
                    <i class="ph-bold ph-x-circle"></i> Lỗi khi tải danh sách câu hỏi: ${error.message}
                </div>
            `;
            Toast.show('Không thể tải danh sách test cases', 'error');
        }
    }

    /**
     * Tải danh sách collections (databases) từ API và render vào dropdown kiểm thử
     */
    async loadCollections() {
        const select = document.getElementById('testing-collection-select');
        if (!select) return;

        try {
            const dataSources = await ApiClient.get(ENDPOINTS.COLLECTIONS);
            if (!Array.isArray(dataSources)) return;

            select.innerHTML = '';

            dataSources.forEach(ds => {
                const option = document.createElement('option');
                option.value = ds.qdrantCollection;
                option.textContent = ds.displayName || ds.qdrantCollection;
                if (ds.isDefault) {
                    option.selected = true;
                }
                select.appendChild(option);
            });
        } catch (error) {
            console.error('TestingManager: Failed to load collections:', error);
            select.innerHTML = '<option value="">Lỗi tải danh sách DB</option>';
        }
    }

    renderTestCases() {
        const accordionContainer = document.getElementById('testcase-list-accordion');
        if (!accordionContainer) return;

        if (this.testCases.length === 0) {
            accordionContainer.innerHTML = '<div class="loading-state">Danh sách test cases trống</div>';
            return;
        }

        // Preserve expanded section
        let activeSectionIndex = -1;
        if (this.editingQuestionKey) {
            const parts = this.editingQuestionKey.split('-');
            if (parts.length > 0) {
                activeSectionIndex = parseInt(parts[0]);
            }
        } else {
            const activeItem = accordionContainer.querySelector('.accordion-item.active');
            if (activeItem) {
                const idMatch = activeItem.id.match(/accordion-sec-(\d+)/);
                if (idMatch) {
                    activeSectionIndex = parseInt(idMatch[1]);
                }
            }
        }

        let accordionHtml = this.testCases.map((sec, secIndex) => {
            const sectionId = `accordion-sec-${secIndex}`;
            const isActive = secIndex === activeSectionIndex;
            return `
                <div class="accordion-item ${isActive ? 'active' : ''}" id="${sectionId}">
                    <div class="accordion-header">
                        <div class="accordion-header-edit-group">
                            <i class="ph-bold ph-caret-right accordion-icon"></i>
                            ${this.isEditing ? `
                                <input type="text" class="section-edit-input" value="${sec.section}" data-index="${secIndex}" placeholder="Tên phần..." />
                            ` : `
                                <span>${sec.section}</span>
                            `}
                        </div>
                        <div class="accordion-actions">
                            ${this.isEditing ? `
                                <button class="btn-icon-action delete btn-delete-section" title="Xóa phần này" data-index="${secIndex}">
                                    <i class="ph-bold ph-trash"></i>
                                </button>
                            ` : `
                                <span class="badge" style="background: var(--muted); color: var(--foreground);">${sec.questions.length}</span>
                                <button class="btn btn-secondary btn-sm btn-add-section" title="Thêm tất cả câu hỏi của phần này" data-index="${secIndex}">
                                    <i class="ph-bold ph-plus"></i>
                                </button>
                            `}
                        </div>
                    </div>
                    <div class="accordion-content">
                        <div class="testcase-list">
                            ${sec.questions.map((q, qIndex) => {
                                const isItemEditing = this.editingQuestionKey === `${secIndex}-${qIndex}`;
                                return `
                                    <div class="testcase-item ${this.isEditing ? 'editing' : ''}" data-sec="${secIndex}" data-q="${qIndex}" data-question="${encodeURIComponent(q)}">
                                        ${isItemEditing ? `
                                            <div class="testcase-edit-wrapper">
                                                <textarea class="testcase-edit-input" placeholder="Nhập câu hỏi...">${q}</textarea>
                                                <div class="testcase-edit-actions-local">
                                                    <button class="btn btn-primary btn-save-tc-local" data-sec="${secIndex}" data-q="${qIndex}" style="padding: 0.25rem 0.5rem; font-size: 0.7rem;">
                                                        <i class="ph-bold ph-check"></i> OK
                                                    </button>
                                                    <button class="btn btn-outline btn-cancel-tc-local" style="padding: 0.25rem 0.5rem; font-size: 0.7rem;">
                                                        <i class="ph-bold ph-x"></i> Hủy
                                                    </button>
                                                </div>
                                            </div>
                                        ` : `
                                            <span class="testcase-text">${qIndex + 1}. ${q}</span>
                                            ${this.isEditing ? `
                                                <div class="testcase-actions-row">
                                                    <button class="btn-icon-action btn-edit-tc-local" title="Sửa câu hỏi" data-sec="${secIndex}" data-q="${qIndex}">
                                                        <i class="ph-bold ph-pencil-simple"></i>
                                                    </button>
                                                    <button class="btn-icon-action delete btn-delete-tc-local" title="Xóa câu hỏi" data-sec="${secIndex}" data-q="${qIndex}">
                                                        <i class="ph-bold ph-trash"></i>
                                                    </button>
                                                </div>
                                            ` : `
                                                <button class="btn-add-tc" title="Thêm vào hàng đợi">
                                                    <i class="ph-bold ph-plus-circle"></i>
                                                </button>
                                            `}
                                        `}
                                    </div>
                                `;
                            }).join('')}
                            
                            ${this.isEditing ? `
                                <button class="btn-add-item-dashed btn-add-tc-new" data-sec="${secIndex}">
                                    <i class="ph-bold ph-plus"></i> Thêm câu hỏi
                                </button>
                            ` : ''}
                        </div>
                    </div>
                </div>
            `;
        }).join('');

        if (this.isEditing) {
            accordionHtml += `
                <button class="btn-add-item-dashed btn-add-section-new" style="margin-top: 0.5rem; padding: 0.75rem;">
                    <i class="ph-bold ph-folder-plus"></i> Thêm phần mới
                </button>
            `;
        }

        accordionContainer.innerHTML = accordionHtml;

        // Gán sự kiện click accordion toggle
        accordionContainer.querySelectorAll('.accordion-header').forEach(header => {
            header.addEventListener('click', (e) => {
                // Nếu click vào nút thêm section, xóa section hoặc input thì bỏ qua việc toggle accordion
                if (e.target.closest('.btn-add-section') || e.target.closest('.btn-delete-section') || e.target.closest('.section-edit-input')) return;

                const item = header.closest('.accordion-item');
                const isActive = item.classList.contains('active');
                
                // Thu gọn các accordion khác
                accordionContainer.querySelectorAll('.accordion-item').forEach(i => {
                    i.classList.remove('active');
                });

                if (!isActive) {
                    item.classList.add('active');
                }
            });
        });

        // Gán sự kiện thêm section (chỉ khi không edit)
        accordionContainer.querySelectorAll('.btn-add-section').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                const index = parseInt(btn.getAttribute('data-index'));
                const section = this.testCases[index];
                if (section) {
                    section.questions.forEach(q => this.queue.push(q));
                    this.saveQueue();
                    this.renderQueue();
                    Toast.show(`Đã thêm ${section.questions.length} câu hỏi của "${section.section}"`, 'success');
                }
            });
        });

        // Gán sự kiện thêm từng câu hỏi vào hàng đợi (khi không edit)
        accordionContainer.querySelectorAll('.testcase-item').forEach(item => {
            item.addEventListener('click', (e) => {
                if (this.isEditing) return; // Không làm gì trong chế độ sửa
                
                // Tránh trigger khi click vào nút icon (mặc dù nút icon nằm trong item)
                if (e.target.closest('.btn-add-tc')) {
                    const question = decodeURIComponent(item.getAttribute('data-question'));
                    this.addToQueue(question);
                } else {
                    // Click vào bất kỳ đâu trên item cũng thêm vào queue
                    const question = decodeURIComponent(item.getAttribute('data-question'));
                    this.addToQueue(question);
                }
            });
        });

        // --- Edit Mode Event Bindings ---
        if (this.isEditing) {
            // Lắng nghe thay đổi tên phần
            accordionContainer.querySelectorAll('.section-edit-input').forEach(input => {
                input.addEventListener('click', (e) => e.stopPropagation());
                input.addEventListener('input', () => {
                    const secIndex = parseInt(input.getAttribute('data-index'));
                    if (this.testCases[secIndex]) {
                        this.testCases[secIndex].section = input.value;
                    }
                });
            });

            // Xóa phần
            accordionContainer.querySelectorAll('.btn-delete-section').forEach(btn => {
                btn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    const secIndex = parseInt(btn.getAttribute('data-index'));
                    if (confirm(`Bạn có chắc chắn muốn xóa phần "${this.testCases[secIndex].section}" và toàn bộ câu hỏi trong đó không?`)) {
                        this.testCases.splice(secIndex, 1);
                        this.editingQuestionKey = null;
                        this.renderTestCases();
                    }
                });
            });

            // Bắt đầu sửa câu hỏi cụ thể
            accordionContainer.querySelectorAll('.btn-edit-tc-local').forEach(btn => {
                btn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    const sec = btn.getAttribute('data-sec');
                    const q = btn.getAttribute('data-q');
                    this.editingQuestionKey = `${sec}-${q}`;
                    this.renderTestCases();
                });
            });

            // Hủy sửa câu hỏi cụ thể
            accordionContainer.querySelectorAll('.btn-cancel-tc-local').forEach(btn => {
                btn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    this.editingQuestionKey = null;
                    this.renderTestCases();
                });
            });

            // Lưu sửa câu hỏi cụ thể
            accordionContainer.querySelectorAll('.btn-save-tc-local').forEach(btn => {
                btn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    const secIndex = parseInt(btn.getAttribute('data-sec'));
                    const qIndex = parseInt(btn.getAttribute('data-q'));
                    const wrapper = btn.closest('.testcase-edit-wrapper');
                    const textarea = wrapper.querySelector('.testcase-edit-input');
                    const newValue = textarea.value.trim();
                    
                    if (newValue) {
                        this.testCases[secIndex].questions[qIndex] = newValue;
                        this.editingQuestionKey = null;
                        this.renderTestCases();
                    } else {
                        Toast.show('Câu hỏi không được để trống', 'warning');
                    }
                });
            });

            // Xóa câu hỏi cụ thể
            accordionContainer.querySelectorAll('.btn-delete-tc-local').forEach(btn => {
                btn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    const secIndex = parseInt(btn.getAttribute('data-sec'));
                    const qIndex = parseInt(btn.getAttribute('data-q'));
                    if (confirm('Bạn có chắc chắn muốn xóa câu hỏi này?')) {
                        this.testCases[secIndex].questions.splice(qIndex, 1);
                        this.editingQuestionKey = null;
                        this.renderTestCases();
                    }
                });
            });

            // Thêm câu hỏi mới vào phần
            accordionContainer.querySelectorAll('.btn-add-tc-new').forEach(btn => {
                btn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    const secIndex = parseInt(btn.getAttribute('data-sec'));
                    this.testCases[secIndex].questions.push("Câu hỏi mới");
                    const newQIndex = this.testCases[secIndex].questions.length - 1;
                    this.editingQuestionKey = `${secIndex}-${newQIndex}`;
                    this.renderTestCases();
                    
                    setTimeout(() => {
                        const textarea = accordionContainer.querySelector('.testcase-edit-input');
                        if (textarea) {
                            textarea.focus();
                            textarea.select();
                        }
                    }, 50);
                });
            });

            // Thêm phần mới
            const btnAddSectionNew = accordionContainer.querySelector('.btn-add-section-new');
            if (btnAddSectionNew) {
                btnAddSectionNew.addEventListener('click', (e) => {
                    e.stopPropagation();
                    this.testCases.push({
                        section: "Phần mới tạo",
                        questions: ["Câu hỏi đầu tiên"]
                    });
                    this.editingQuestionKey = `${this.testCases.length - 1}-0`;
                    this.renderTestCases();
                    
                    setTimeout(() => {
                        const inputs = accordionContainer.querySelectorAll('.section-edit-input');
                        const lastInput = inputs[inputs.length - 1];
                        if (lastInput) {
                            lastInput.focus();
                            lastInput.select();
                        }
                    }, 50);
                });
            }
        }
    }

    toggleEditMode() {
        if (this.isRunning) {
            Toast.show('Không thể chỉnh sửa khi đang chạy kiểm thử', 'warning');
            return;
        }

        this.isEditing = !this.isEditing;
        
        // Lưu backup dữ liệu gốc khi bắt đầu sửa
        if (this.isEditing) {
            this.originalTestCases = JSON.parse(JSON.stringify(this.testCases));
        } else {
            this.editingQuestionKey = null;
        }

        this.updateEditUI();
        this.renderTestCases();
    }

    updateEditUI() {
        const btnToggleEdit = document.getElementById('btn-toggle-edit-testcases');
        const editActions = document.getElementById('testcases-edit-actions');
        const btnAddAll = document.getElementById('btn-add-all-testcases');
        const accordionContainer = document.getElementById('testcase-list-accordion');

        if (this.isEditing) {
            if (btnToggleEdit) {
                btnToggleEdit.innerHTML = '<i class="ph-bold ph-eye"></i> Xem';
                btnToggleEdit.title = 'Thoát chế độ chỉnh sửa';
                btnToggleEdit.classList.add('btn-secondary');
            }
            if (editActions) editActions.classList.remove('hidden');
            if (btnAddAll) btnAddAll.classList.add('hidden');
            if (accordionContainer) accordionContainer.classList.add('editing');
        } else {
            if (btnToggleEdit) {
                btnToggleEdit.innerHTML = '<i class="ph-bold ph-pencil-simple"></i> Sửa';
                btnToggleEdit.title = 'Bật chỉnh sửa';
                btnToggleEdit.classList.remove('btn-secondary');
            }
            if (editActions) editActions.classList.add('hidden');
            if (btnAddAll) btnAddAll.classList.remove('hidden');
            if (accordionContainer) accordionContainer.classList.remove('editing');
        }
    }

    cancelEditing() {
        if (confirm('Bạn có chắc chắn muốn hủy bỏ mọi thay đổi chưa lưu không?')) {
            this.testCases = JSON.parse(JSON.stringify(this.originalTestCases));
            this.isEditing = false;
            this.editingQuestionKey = null;
            this.updateEditUI();
            this.renderTestCases();
            Toast.show('Đã hủy bỏ các thay đổi', 'info');
        }
    }

    async saveTestCasesToServer() {
        if (this.testCases.length === 0) {
            Toast.show('Danh sách test cases trống', 'warning');
            return;
        }

        // Chuẩn hóa dữ liệu gửi lên
        const cleanedData = this.testCases.map(sec => ({
            section: sec.section.trim(),
            questions: sec.questions.map(q => q.trim()).filter(q => q !== '')
        })).filter(sec => sec.section !== '' && sec.questions.length > 0);

        if (cleanedData.length === 0) {
            Toast.show('Không thể lưu danh sách trống hoặc không hợp lệ', 'error');
            return;
        }

        try {
            const btnSave = document.getElementById('btn-save-testcases');
            if (btnSave) btnSave.disabled = true;

            const res = await ApiClient.post('/testcases', cleanedData);
            
            Toast.show('Đã lưu các thay đổi lên máy chủ thành công!', 'success');
            
            this.testCases = cleanedData;
            this.originalTestCases = JSON.parse(JSON.stringify(this.testCases));
            this.isEditing = false;
            this.editingQuestionKey = null;
            this.updateEditUI();
            this.renderTestCases();
        } catch (error) {
            console.error('Lỗi khi lưu test cases:', error);
            Toast.show(`Lỗi khi lưu: ${error.message}`, 'error');
        } finally {
            const btnSave = document.getElementById('btn-save-testcases');
            if (btnSave) btnSave.disabled = false;
        }
    }

    addToQueue(question) {
        this.queue.push(question);
        this.saveQueue();
        this.renderQueue();
        Toast.show('Đã thêm câu hỏi vào hàng đợi', 'success');
    }

    removeFromQueue(index) {
        if (this.isRunning) {
            Toast.show('Không thể xóa câu hỏi khi đang chạy kiểm thử', 'warning');
            return;
        }
        this.queue.splice(index, 1);
        this.saveQueue();
        this.renderQueue();
    }

    clearQueue() {
        if (this.isRunning) {
            Toast.show('Không thể xóa hàng đợi khi đang chạy kiểm thử', 'warning');
            return;
        }
        if (confirm('Bạn có chắc chắn muốn xóa sạch hàng đợi câu hỏi hiện tại không?')) {
            this.queue = [];
            this.saveQueue();
            this.renderQueue();
            Toast.show('Đã xóa sạch hàng đợi', 'success');
        }
    }

    saveQueue() {
        localStorage.setItem('dodo_testing_queue', JSON.stringify(this.queue));
    }

    renderQueue() {
        const countBadge = document.getElementById('queue-count-badge');
        const queueList = document.getElementById('queue-items-list');
        const btnRun = document.getElementById('btn-run-testing');

        if (countBadge) countBadge.innerText = this.queue.length;

        if (!queueList) return;

        if (this.queue.length === 0) {
            queueList.classList.add('empty');
            queueList.innerHTML = `<div class="queue-empty-msg">Chưa có câu hỏi nào trong hàng đợi. Hãy chọn câu hỏi ở cột bên trái hoặc tự nhập câu hỏi.</div>`;
            if (btnRun) btnRun.disabled = true;
            return;
        }

        queueList.classList.remove('empty');
        queueList.innerHTML = this.queue.map((q, index) => `
            <div class="queue-item">
                <span class="result-index">${index + 1}</span>
                <span class="queue-item-text" title="${q}">${q}</span>
                <button class="btn-remove-queue" data-index="${index}" title="Xóa khỏi hàng đợi">
                    <i class="ph-bold ph-trash"></i>
                </button>
            </div>
        `).join('');

        // Gán sự kiện xóa item
        queueList.querySelectorAll('.btn-remove-queue').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                const index = parseInt(btn.getAttribute('data-index'));
                this.removeFromQueue(index);
            });
        });

        if (btnRun) {
            btnRun.disabled = this.isRunning;
        }
    }

    async runSequential() {
        if (this.queue.length === 0 || this.isRunning) return;

        this.isRunning = true;
        this.shouldStop = false;
        this.results = []; // Reset kết quả

        // Cập nhật trạng thái các nút bấm điều khiển
        document.getElementById('btn-run-testing').classList.add('hidden');
        
        const btnStop = document.getElementById('btn-stop-testing');
        if (btnStop) {
            btnStop.classList.remove('hidden');
            btnStop.disabled = false;
            btnStop.innerHTML = `<i class="ph-bold ph-stop"></i> Dừng chạy`;
        }

        document.getElementById('btn-clear-queue').disabled = true;
        document.getElementById('btn-add-all-testcases').disabled = true;
        document.getElementById('btn-add-custom-question').disabled = true;
        document.getElementById('input-custom-question').disabled = true;
        const testingDbSelect = document.getElementById('testing-collection-select');
        if (testingDbSelect) testingDbSelect.disabled = true;
        document.querySelectorAll('.btn-remove-queue').forEach(b => b.disabled = true);

        // Hiển thị thanh tiến trình và tổng quan kết quả
        const progressContainer = document.getElementById('testing-progress-container');
        const resultsSummary = document.getElementById('results-summary');
        const resultsList = document.getElementById('results-list');

        if (progressContainer) progressContainer.classList.remove('hidden');
        if (resultsSummary) resultsSummary.classList.remove('hidden');
        if (resultsList) resultsList.innerHTML = ''; // Clear kết quả cũ

        let successCount = 0;
        let failCount = 0;
        let totalDuration = 0;
        let lastProcessedIndex = 0;
        const totalQuestions = this.queue.length;

        this.updateSummary(0, 0, 0);

        for (let i = 0; i < totalQuestions; i++) {
            if (this.shouldStop) {
                break;
            }

            const question = this.queue[i];

            // Khởi tạo đối tượng kết quả cho câu hỏi này
            this.results[i] = {
                question: question,
                status: 'running',
                duration: 0,
                aiContent: '',
                steps: [],
                error: null
            };

            // 1. Tạo Card kết quả dạng tóm tắt ở cột giữa
            const cardId = `result-card-${i}`;
            const resultCardHtml = `
                <div class="result-card status-running" id="${cardId}">
                    <div class="result-card-header">
                        <div class="result-card-header-left">
                            <span class="result-index">${i + 1}</span>
                            <span class="result-question" title="${question}">${question}</span>
                        </div>
                        <div class="result-card-header-right">
                            <span class="result-duration" id="${cardId}-duration">Đang chạy...</span>
                            <span class="result-status-icon running" id="${cardId}-icon">
                                <i class="ph-bold ph-circle-notch animate-spin"></i>
                            </span>
                        </div>
                    </div>
                </div>
            `;
            resultsList.insertAdjacentHTML('beforeend', resultCardHtml);

            // Tự động cuộn xuống kết quả mới nhất ở cột giữa
            resultsList.scrollTop = resultsList.scrollHeight;

            // Đăng ký sự kiện click để xem chi tiết
            const card = document.getElementById(cardId);
            card.addEventListener('click', () => {
                this.selectResult(i);
            });

            // Tự động chọn và hiển thị chi tiết câu đang chạy ở cột bên phải
            this.selectResult(i);

            // Cập nhật thanh tiến trình tổng quát
            this.updateProgressBar(i, totalQuestions, question);

            const startTime = Date.now();
            let finalData = null;
            let runError = null;

            try {
                // 2. Chạy request với cơ chế Retry 1 lần
                finalData = await this.executeQuestionWithRetry(question, i);
            } catch (err) {
                runError = err;
            }

            const duration = Math.round((Date.now() - startTime) / 1000);
            totalDuration += duration;

            this.results[i].duration = duration;

            // 3. Cập nhật trạng thái kết quả trên Card ở cột giữa
            const durationEl = document.getElementById(`${cardId}-duration`);
            const iconEl = document.getElementById(`${cardId}-icon`);

            if (durationEl) durationEl.innerText = `${duration}s`;

            if (runError) {
                failCount++;
                this.results[i].status = 'error';
                this.results[i].error = runError.message;
                card.classList.remove('status-running');
                card.classList.add('status-error');
                if (iconEl) iconEl.innerHTML = `<i class="ph-bold ph-x-circle" style="color: var(--destructive);"></i>`;
            } else {
                successCount++;
                this.results[i].status = 'success';
                card.classList.remove('status-running');
                card.classList.add('status-success');
                if (iconEl) iconEl.innerHTML = `<i class="ph-bold ph-check-circle" style="color: #22c55e;"></i>`;
            }

            // Nếu đang xem câu hỏi này, render lại để cập nhật Header / Badge trạng thái
            if (this.activeResultIndex === i) {
                this.renderDetailView(i);
            }

            // Cập nhật bảng tổng hợp kết quả
            const avgTime = Math.round(totalDuration / (successCount + failCount));
            this.updateSummary(successCount, failCount, avgTime);

            lastProcessedIndex = i + 1;

            if (this.shouldStop) {
                break;
            }
        }

        // Cập nhật tiến trình
        if (this.shouldStop) {
            this.updateProgressBar(lastProcessedIndex, totalQuestions, null, `Đã dừng kiểm thử: ${lastProcessedIndex}/${totalQuestions}`);
        } else {
            this.updateProgressBar(totalQuestions, totalQuestions, 'Hoàn thành kiểm thử');
        }

        // Khôi phục trạng thái nút bấm
        this.isRunning = false;
        
        const btnRun = document.getElementById('btn-run-testing');
        if (btnRun) {
            btnRun.classList.remove('hidden');
            btnRun.disabled = false;
        }
        if (btnStop) {
            btnStop.classList.add('hidden');
            btnStop.disabled = false;
            btnStop.innerHTML = `<i class="ph-bold ph-stop"></i> Dừng chạy`;
        }

        document.getElementById('btn-clear-queue').disabled = false;
        document.getElementById('btn-add-all-testcases').disabled = false;
        document.getElementById('btn-add-custom-question').disabled = false;
        document.getElementById('input-custom-question').disabled = false;
        const testingDbSelectRestore = document.getElementById('testing-collection-select');
        if (testingDbSelectRestore) testingDbSelectRestore.disabled = false;
        document.querySelectorAll('.btn-remove-queue').forEach(b => b.disabled = false);
        this.renderQueue();

        if (this.shouldStop) {
            Toast.show(`Đã dừng lượt chạy kiểm thử. Đã chạy: ${lastProcessedIndex}/${totalQuestions}. Thành công: ${successCount}, Thất bại: ${failCount}`, 'info');
        } else {
            Toast.show(`Đã hoàn thành lượt chạy kiểm thử. Thành công: ${successCount}, Thất bại: ${failCount}`, successCount > 0 ? 'success' : 'error');
        }
    }

    updateProgressBar(current, total, currentQuestion, customStatusText = null) {
        const percent = Math.round((current / total) * 100);
        const statusText = document.getElementById('progress-status-text');
        const percentText = document.getElementById('progress-percentage-text');
        const progressBar = document.getElementById('testing-progress-bar');

        if (statusText) {
            if (customStatusText) {
                statusText.innerText = customStatusText;
            } else {
                statusText.innerText = current === total 
                    ? `Hoàn thành kiểm thử: ${total}/${total}`
                    : `Đang chạy: ${current + 1}/${total} - ${currentQuestion}`;
            }
        }
        if (percentText) percentText.innerText = `${percent}%`;
        if (progressBar) progressBar.style.width = `${percent}%`;
    }

    updateSummary(success, fail, avgTime) {
        const successEl = document.getElementById('summary-success-count');
        const failEl = document.getElementById('summary-fail-count');
        const avgTimeEl = document.getElementById('summary-avg-time');

        if (successEl) successEl.innerText = success;
        if (failEl) failEl.innerText = fail;
        if (avgTimeEl) avgTimeEl.innerText = `${avgTime}s`;
    }

    /**
     * Thực hiện gửi request và xử lý retry 1 lần nếu xảy ra lỗi
     */
    async executeQuestionWithRetry(question, index, attempt = 1) {
        try {
            return await this.sendChatRequest(question, index);
        } catch (error) {
            // Kiểm tra xem có lệnh dừng từ người dùng hay không
            if (this.shouldStop) {
                throw error;
            }

            if (attempt === 1) {
                console.warn(`Lần 1 lỗi cho câu hỏi "${question}": ${error.message}. Đang thử lại lần 2...`);
                
                // Reset lại kết quả cũ trước khi chạy lại
                const result = this.results[index];
                result.aiContent = '';
                result.steps = [];

                // Cập nhật trạng thái hiển thị đang thử lại trên UI chi tiết nếu đang active
                if (this.activeResultIndex === index) {
                    const answerEl = document.getElementById('detail-answer');
                    if (answerEl) {
                        answerEl.innerHTML = `<span class="animate-pulse" style="color: var(--secondary);">⚠️ Gặp lỗi: ${error.message}. Đang tự động thử lại lần 2 (Attempt 2/2)...</span>`;
                    }
                    const stepsContainer = document.getElementById('detail-steps');
                    if (stepsContainer) {
                        stepsContainer.innerHTML = '';
                    }
                }
                
                // Chờ 1.5 giây trước khi thực hiện thử lại
                await new Promise(resolve => setTimeout(resolve, 1500));
                
                // Chạy lại
                return await this.executeQuestionWithRetry(question, index, 2);
            }
            
            throw error; // Lần 2 vẫn lỗi thì ném lỗi ra ngoài để đánh dấu thất bại
        }
    }

    /**
     * Gửi truy vấn thực tế lên API `/chat` và nhận SSE stream
     */
    sendChatRequest(question, index) {
        return new Promise((resolve, reject) => {
            // Sử dụng dropdown riêng của tab kiểm thử thay vì tab chatbot
            const testingCollectionSelect = document.getElementById('testing-collection-select');
            const collectionName = testingCollectionSelect ? testingCollectionSelect.value : '';
            const isPerfMode = localStorage.getItem('dodo_performance_mode') === 'true';

            const body = JSON.stringify({
                message: question,
                collectionName,
                isTestPerformance: isPerfMode
            });

            const abortController = new AbortController();
            this.currentAbortController = abortController;

            const result = this.results[index];

            ApiClient.fetchStream(ENDPOINTS.CHAT, {
                body,
                signal: abortController.signal,
                silent: true // Tắt log console phiền phức của ApiClient khi chạy số lượng lớn
            }, (data) => {
                // Xử lý stream bước RAG (Step)
                if (data.type === 'step') {
                    result.steps.push(data.step);

                    // Nếu đang xem câu hỏi này, cập nhật DOM chi tiết
                    if (this.activeResultIndex === index) {
                        const stepsContainer = document.getElementById('detail-steps');
                        if (stepsContainer) {
                            this.renderRagStepDOM(stepsContainer, data.step, result.steps.length);
                        }
                    }
                }

                // Xử lý stream nội dung text (Chunk)
                if (data.type === 'chunk') {
                    result.aiContent += data.text;

                    // Nếu đang xem câu hỏi này, cập nhật DOM chi tiết
                    if (this.activeResultIndex === index) {
                        const answerEl = document.getElementById('detail-answer');
                        if (answerEl) {
                            if (window.marked) {
                                answerEl.innerHTML = window.marked.parse(result.aiContent);
                            } else {
                                answerEl.innerText = result.aiContent;
                            }
                        }
                    }
                }

                // Hoàn tất câu trả lời (Final)
                if (data.type === 'final') {
                    result.aiContent = data.text || result.aiContent;

                    // Nếu đang xem câu hỏi này, cập nhật DOM chi tiết
                    if (this.activeResultIndex === index) {
                        const answerEl = document.getElementById('detail-answer');
                        if (answerEl) {
                            if (window.marked) {
                                answerEl.innerHTML = window.marked.parse(result.aiContent);
                            } else {
                                answerEl.innerText = result.aiContent;
                            }
                        }
                    }
                    resolve(data);
                }

                // Xảy ra lỗi từ API
                if (data.type === 'error') {
                    reject(new Error(data.message || 'Lỗi không xác định từ API'));
                }
            }).catch(error => {
                reject(error);
            });
        });
    }

    /**
     * Đánh dấu và hiển thị chi tiết câu hỏi được chọn
     */
    selectResult(index) {
        this.activeResultIndex = index;
        
        // Cập nhật class active cho card ở cột giữa
        document.querySelectorAll('.result-card').forEach((card, idx) => {
            if (idx === index) {
                card.classList.add('active');
            } else {
                card.classList.remove('active');
            }
        });

        // Vẽ cột chi tiết bên phải
        this.renderDetailView(index);
    }

    /**
     * Render bảng chi tiết kết quả chạy kiểm thử ở cột bên phải
     */
    renderDetailView(index) {
        const detailView = document.getElementById('testing-detail-view');
        if (!detailView) return;

        const result = this.results[index];
        if (!result) {
            detailView.innerHTML = `
                <div class="detail-empty-state">
                    <i class="ph-bold ph-browsers"></i>
                    <h4>Chưa chọn câu hỏi</h4>
                    <p>Click vào một câu hỏi bất kỳ trong danh sách kết quả thực thi ở cột giữa để xem câu trả lời chi tiết và các câu lệnh SQL tương ứng tại đây.</p>
                </div>
            `;
            return;
        }

        let statusBadge = '';
        if (result.status === 'running') {
            statusBadge = `<span class="badge" style="background: var(--primary);"><i class="ph-bold ph-circle-notch animate-spin"></i> Đang chạy</span>`;
        } else if (result.status === 'success') {
            statusBadge = `<span class="badge" style="background: #22c55e;"><i class="ph-bold ph-check"></i> Thành công</span>`;
        } else {
            statusBadge = `<span class="badge" style="background: var(--destructive);"><i class="ph-bold ph-x"></i> Lỗi</span>`;
        }

        const isPrevDisabled = index === 0;
        const isNextDisabled = index === this.results.length - 1;

        detailView.innerHTML = `
            <div class="detail-content-wrapper">
                <div class="detail-header" style="display: flex; justify-content: space-between; align-items: center; gap: 0.75rem;">
                    <div style="flex: 1; min-width: 0;">
                        <div class="detail-header-question">${result.question}</div>
                        <div class="detail-meta-row" style="display: flex; justify-content: space-between; align-items: center; width: 100%; flex-wrap: wrap; gap: 0.75rem;">
                            <div style="display: flex; align-items: center; gap: 1rem; flex-wrap: wrap;">
                                <div class="detail-meta-item">
                                    <i class="ph-bold ph-hash"></i>
                                    <span>Câu số ${index + 1}</span>
                                </div>
                                <div class="detail-meta-item">
                                    <i class="ph-bold ph-clock"></i>
                                    <span>Thời gian: ${result.duration ? result.duration + 's' : '---'}</span>
                                </div>
                                <div class="detail-meta-item">
                                    ${statusBadge}
                                </div>
                            </div>
                            <div class="detail-navigation-actions" style="display: flex; align-items: center; gap: 0.4rem;">
                                <button class="btn btn-secondary btn-sm btn-prev-result" ${isPrevDisabled ? 'disabled' : ''} title="Xem câu trước">
                                    <i class="ph-bold ph-caret-left"></i> Lùi
                                </button>
                                <button class="btn btn-secondary btn-sm btn-next-result" ${isNextDisabled ? 'disabled' : ''} title="Xem câu sau">
                                    Tiến <i class="ph-bold ph-caret-right"></i>
                                </button>
                            </div>
                        </div>
                    </div>
                    <i class="ph-bold ph-caret-down toggle-collapse-icon"></i>
                </div>
                
                <div class="testing-answer markdown-content" id="detail-answer">
                    ${result.aiContent ? (window.marked ? window.marked.parse(result.aiContent) : result.aiContent) : '<span class="animate-pulse" style="display: flex; align-items: center; gap: 0.5rem; color: var(--primary);"><i class="ph-bold ph-sparkle animate-pulse"></i> Đang xử lý truy vấn...</span>'}
                </div>

                <div class="testing-steps" id="detail-steps">
                    <!-- Các bước RAG sẽ được thêm vào đây -->
                </div>
            </div>
        `;

        // Gán sự kiện click cho các nút điều hướng tiến/lùi
        const btnPrev = detailView.querySelector('.btn-prev-result');
        const btnNext = detailView.querySelector('.btn-next-result');

        if (btnPrev && !isPrevDisabled) {
            btnPrev.addEventListener('click', (e) => {
                e.stopPropagation();
                this.selectResult(index - 1);
            });
        }

        if (btnNext && !isNextDisabled) {
            btnNext.addEventListener('click', (e) => {
                e.stopPropagation();
                this.selectResult(index + 1);
            });
        }

        // Render lại các bước RAG đã có
        const stepsContainer = document.getElementById('detail-steps');
        if (stepsContainer && result.steps && result.steps.length > 0) {
            result.steps.forEach((step, stepIdx) => {
                this.renderRagStepDOM(stepsContainer, step, stepIdx + 1);
            });
        }

        // Nếu xảy ra lỗi và không có câu trả lời
        if (result.error && !result.aiContent) {
            const answerEl = document.getElementById('detail-answer');
            if (answerEl) {
                answerEl.innerHTML = `
                    <div style="color: var(--destructive); font-weight: 600;">⚠️ Lỗi thực thi (Đã thử lại 2 lần):</div>
                    <pre style="margin-top:0.5rem; background: rgba(239, 68, 68, 0.05); border: 1px solid var(--border); color: var(--foreground); padding: 0.75rem; border-radius: 6px; overflow-x: auto;">${result.error}</pre>
                `;
            }
        }
    }

    /**
     * Render một bước RAG DOM chi tiết với Tiện ích SQL
     */
    renderRagStepDOM(container, step, stepIndex) {
        if (!container) return;

        const stepId = `detail-step-${stepIndex}`;
        
        // 1. Xác định icon dựa trên tiêu đề bước
        const getStepIcon = (title) => {
            if (!title) return '<i class="ph-bold ph-gear"></i>';
            const t = title.toLowerCase();
            const iconMap = {
                'vector': 'ph-file-search',
                'retrieval': 'ph-magnifying-glass',
                'schema': 'ph-database',
                'rules': 'ph-shield-warning',
                'sql': 'ph-code-block',
                'execution': 'ph-code-block',
                'healing': 'ph-magic-wand',
                'system': 'ph-gear'
            };
            
            const key = Object.keys(iconMap).find(k => t.includes(k));
            const iconClass = iconMap[key] || 'ph-lightning';
            
            // Thiết lập màu sắc icon
            let colorStyle = '';
            if (key === 'schema') colorStyle = 'style="color: var(--primary);"';
            else if (key === 'sql' || key === 'execution') colorStyle = 'style="color: var(--accent);"';
            else if (key === 'retrieval' || key === 'vector') colorStyle = 'style="color: #22c55e;"';
            
            return `<i class="ph-bold ${iconClass}" ${colorStyle}></i>`;
        };

        const stepIcon = getStepIcon(step.title);

        // 2. Lấy nội dung chi tiết của bước (hỗ trợ step.content từ backend hoặc fallback step.details)
        let contentText = "";
        if (step.content) {
            contentText = step.content.trim();
        } else if (step.details) {
            if (typeof step.details === 'string') {
                contentText = step.details.trim();
            } else {
                contentText = JSON.stringify(step.details, null, 2).trim();
            }
        } else if (step.sql) {
            contentText = step.sql.trim();
        }

        // 3. Nhận diện câu SQL trong content để hỗ trợ các nút Sao chép & Chạy thử
        let sqlQuery = step.sql || '';
        if (!sqlQuery && step.content) {
            const sqlMatch = step.content.match(/```sql\s*([\s\S]*?)```/i);
            if (sqlMatch) {
                sqlQuery = sqlMatch[1].trim();
            } else if (step.content.toUpperCase().includes('SELECT ')) {
                sqlQuery = step.content.trim();
            }
        }
        if (!sqlQuery && step.type === 'sql_execution' && typeof step.details === 'string') {
            sqlQuery = step.details;
        }

        const hasSql = !!sqlQuery.trim();
        
        // Xác định xem bước này có chi tiết thực sự hay không
        const hasDetails = contentText !== "" && contentText !== "Không có chi tiết bổ sung.";

        let stepHtml = "";
        if (hasDetails) {
            // Render nội dung bằng MessageRenderer để đồng bộ giao diện Dark Terminal và Markdown
            const formattedContent = MessageRenderer.formatRagStepContent(contentText);
            let contentHtml = `<div class="markdown-content text-sm" style="opacity: 0.95;">${formattedContent}</div>`;

            stepHtml = `
                <div class="testing-step-item collapsible" id="${stepId}">
                    <div class="testing-step-header">
                        <i class="ph-bold ph-caret-right caret-icon"></i>
                        ${stepIcon}
                        <span>Bước ${stepIndex}: ${step.title || 'Đang xử lý...'}</span>
                    </div>
                    <div class="testing-step-content" id="${stepId}-content" style="max-height: 0px; overflow: hidden; transition: max-height 0.2s ease-out;">
                        ${contentHtml}
                    </div>
                </div>
            `;
        } else {
            // Không có chi tiết -> Hiển thị dạng tĩnh, không có mũi tên caret
            stepHtml = `
                <div class="testing-step-item static" id="${stepId}">
                    <div class="testing-step-header" style="cursor: default;">
                        <i class="ph-bold ph-circle" style="font-size: 0.4rem; color: var(--muted-foreground); margin: 0 0.4rem;"></i>
                        ${stepIcon}
                        <span>Bước ${stepIndex}: ${step.title || 'Đang xử lý...'}</span>
                    </div>
                </div>
            `;
        }

        container.insertAdjacentHTML('beforeend', stepHtml);

        if (hasDetails) {
            const stepItem = document.getElementById(stepId);
            const stepHeader = stepItem.querySelector('.testing-step-header');
            const stepContent = stepItem.querySelector('.testing-step-content');

            // Toggle mở rộng/thu gọn bước RAG
            stepHeader.addEventListener('click', (e) => {
                e.stopPropagation();
                const isExpanded = stepItem.classList.contains('expanded');
                if (isExpanded) {
                    stepItem.classList.remove('expanded');
                    stepContent.style.maxHeight = '0px';
                } else {
                    stepItem.classList.add('expanded');
                    stepContent.style.maxHeight = 'none'; // Sử dụng none để nội dung tự co giãn
                }
            });
        }
    }



    stopTesting(immediate = false) {
        if (!this.isRunning) return;
        this.shouldStop = true;
        
        if (immediate) {
            if (this.currentAbortController) {
                this.currentAbortController.abort();
            }
            Toast.show('Đã hủy bài kiểm thử ngay lập tức.', 'warning');
        } else {
            // Vô hiệu hóa nút dừng chạy và hiển thị trạng thái đang xử lý dừng câu hiện tại
            const btnStop = document.getElementById('btn-stop-testing');
            if (btnStop) {
                btnStop.disabled = true;
                btnStop.innerHTML = `<i class="ph-bold ph-circle-notch animate-spin"></i> Đang dừng...`;
            }
            Toast.show('Đã nhận lệnh dừng. Sẽ dừng sau khi hoàn thành câu hiện tại.', 'info');
        }
    }

    setupResponsiveCollapse() {
        // Lắng nghe click trên sidebar header
        const sidebarHeader = document.querySelector('.testing-sidebar__header');
        if (sidebarHeader) {
            sidebarHeader.addEventListener('click', (e) => {
                if (e.target.closest('button') || e.target.closest('.btn')) return;
                if (window.innerWidth <= 1024) {
                    const sidebar = document.querySelector('.testing-sidebar');
                    sidebar?.classList.toggle('collapsed');
                }
            });
        }

        // Lắng nghe click trên queue header
        const queueHeader = document.querySelector('.queue-header');
        if (queueHeader) {
            queueHeader.addEventListener('click', () => {
                if (window.innerWidth <= 1024) {
                    const queuePanel = document.querySelector('.queue-control-panel');
                    queuePanel?.classList.toggle('collapsed');
                }
            });
        }

        // Lắng nghe click trên results header
        const resultsHeader = document.querySelector('.results-header');
        if (resultsHeader) {
            resultsHeader.addEventListener('click', () => {
                if (window.innerWidth <= 1024) {
                    const resultsPanel = document.querySelector('.testing-results-panel');
                    resultsPanel?.classList.toggle('collapsed');
                }
            });
        }

        // Lắng nghe click trên detail header và các nút actions (delegation)
        const detailView = document.getElementById('testing-detail-view');
        if (detailView && !detailView.dataset.boundActions) {
            detailView.dataset.boundActions = 'true';
            detailView.addEventListener('click', (e) => {
                const header = e.target.closest('.detail-header');
                if (header && window.innerWidth <= 1024) {
                    detailView.classList.toggle('collapsed');
                    return;
                }

                const btn = e.target.closest('[data-action]');
                if (btn) {
                    const action = btn.getAttribute('data-action');
                    if (action === 'copy-code') {
                        e.stopPropagation();
                        const text = InteractionService.getTerminalCode(btn);
                        InteractionService.copyToClipboard(text, btn);
                    } else if (action === 'copy-rules') {
                        e.stopPropagation();
                        const panel = btn.closest('.rag-step__panel');
                        if (panel) {
                            const contentEl = panel.querySelector('.rag-step__content-inner');
                            if (contentEl) {
                                const text = contentEl.textContent.trim();
                                InteractionService.copyToClipboard(text, btn);
                            }
                        }
                    }
                }
            });
        }
    }

    setupPanelResizers() {
        const container = document.querySelector('.testing-container');
        if (!container || container.dataset.resizersBound) return;
        container.dataset.resizersBound = 'true';

        const sidebar = document.getElementById('testing-sidebar');
        const main = document.getElementById('testing-main');
        const detail = document.getElementById('testing-detail-view');
        const resizer1 = document.getElementById('resizer-1');
        const resizer2 = document.getElementById('resizer-2');

        const sidebarCollapsed = document.getElementById('testing-sidebar-collapsed');
        const mainCollapsed = document.getElementById('testing-main-collapsed');

        const btnCollapseSidebar = document.getElementById('btn-collapse-sidebar');
        const btnCollapseMain = document.getElementById('btn-collapse-main');

        const btnExpandSidebar = sidebarCollapsed?.querySelector('.btn-expand-sidebar');
        const btnExpandMain = mainCollapsed?.querySelector('.btn-expand-main');

        if (!sidebar || !main || !detail) return;

        // Restore sizes from localStorage
        const savedSidebarWidth = localStorage.getItem('dodo_panel_sidebar_width');
        const savedMainWidth = localStorage.getItem('dodo_panel_main_width');
        const savedSidebarCollapsed = localStorage.getItem('dodo_panel_sidebar_collapsed') === 'true';
        const savedMainCollapsed = localStorage.getItem('dodo_panel_main_collapsed') === 'true';

        if (savedSidebarWidth && !savedSidebarCollapsed) {
            sidebar.style.width = savedSidebarWidth;
        }
        if (savedMainWidth && !savedMainCollapsed) {
            main.style.width = savedMainWidth;
            main.style.minWidth = '0px';
            main.style.maxWidth = 'none';
        }

        if (savedSidebarCollapsed) {
            sidebar.classList.add('collapsed');
            if (sidebarCollapsed) sidebarCollapsed.classList.remove('hidden');
            if (resizer1) resizer1.style.display = 'none';
        }
        if (savedMainCollapsed) {
            main.classList.add('collapsed');
            if (mainCollapsed) mainCollapsed.classList.remove('hidden');
            if (resizer2) resizer2.style.display = 'none';
        }

        // --- Collapse/Expand Events ---
        const collapseSidebar = () => {
            sidebar.classList.add('collapsed');
            if (sidebarCollapsed) sidebarCollapsed.classList.remove('hidden');
            if (resizer1) resizer1.style.display = 'none';
            localStorage.setItem('dodo_panel_sidebar_collapsed', 'true');
        };

        const expandSidebar = () => {
            sidebar.classList.remove('collapsed');
            if (sidebarCollapsed) sidebarCollapsed.classList.add('hidden');
            if (resizer1) resizer1.style.display = 'block';
            localStorage.setItem('dodo_panel_sidebar_collapsed', 'false');
        };

        const collapseMain = () => {
            main.classList.add('collapsed');
            if (mainCollapsed) mainCollapsed.classList.remove('hidden');
            if (resizer2) resizer2.style.display = 'none';
            localStorage.setItem('dodo_panel_main_collapsed', 'true');
        };

        const expandMain = () => {
            main.classList.remove('collapsed');
            if (mainCollapsed) mainCollapsed.classList.add('hidden');
            if (resizer2) resizer2.style.display = 'block';
            localStorage.setItem('dodo_panel_main_collapsed', 'false');
        };

        if (btnCollapseSidebar) btnCollapseSidebar.addEventListener('click', collapseSidebar);
        if (btnExpandSidebar) btnExpandSidebar.addEventListener('click', expandSidebar);
        if (btnCollapseMain) btnCollapseMain.addEventListener('click', collapseMain);
        if (btnExpandMain) btnExpandMain.addEventListener('click', expandMain);

        // --- Resizer 1 (Sidebar / Main) ---
        if (resizer1) {
            resizer1.addEventListener('mousedown', (e) => {
                e.preventDefault();
                resizer1.classList.add('resizing');
                container.classList.add('panel-resizing');
                document.body.style.cursor = 'col-resize';

                const onMouseMove = (moveEvent) => {
                    const containerRect = container.getBoundingClientRect();
                    let newWidth = moveEvent.clientX - containerRect.left;
                    
                    // Min/Max constraints
                    if (newWidth < 200) newWidth = 200;
                    if (newWidth > 500) newWidth = 500;

                    sidebar.style.width = `${newWidth}px`;
                };

                const onMouseUp = () => {
                    resizer1.classList.remove('resizing');
                    container.classList.remove('panel-resizing');
                    document.body.style.cursor = 'default';
                    localStorage.setItem('dodo_panel_sidebar_width', sidebar.style.width);
                    document.removeEventListener('mousemove', onMouseMove);
                    document.removeEventListener('mouseup', onMouseUp);
                };

                document.addEventListener('mousemove', onMouseMove);
                document.addEventListener('mouseup', onMouseUp);
            });
        }

        // --- Resizer 2 (Main / Detail) ---
        if (resizer2) {
            resizer2.addEventListener('mousedown', (e) => {
                e.preventDefault();
                resizer2.classList.add('resizing');
                container.classList.add('panel-resizing');
                document.body.style.cursor = 'col-resize';

                const onMouseMove = (moveEvent) => {
                    const sidebarRect = sidebar.getBoundingClientRect();
                    const sidebarWidth = sidebar.classList.contains('collapsed') ? 0 : sidebarRect.width;
                    const resizer1Width = sidebar.classList.contains('collapsed') ? 0 : 6;
                    
                    let newWidth = moveEvent.clientX - sidebarRect.left - sidebarWidth - resizer1Width;
                    
                    // Min/Max constraints
                    if (newWidth < 300) newWidth = 300;
                    if (newWidth > 600) newWidth = 600;

                    main.style.width = `${newWidth}px`;
                    main.style.minWidth = '0px';
                    main.style.maxWidth = 'none';
                };

                const onMouseUp = () => {
                    resizer2.classList.remove('resizing');
                    container.classList.remove('panel-resizing');
                    document.body.style.cursor = 'default';
                    localStorage.setItem('dodo_panel_main_width', main.style.width);
                    document.removeEventListener('mousemove', onMouseMove);
                    document.removeEventListener('mouseup', onMouseUp);
                };

                document.addEventListener('mousemove', onMouseMove);
                document.addEventListener('mouseup', onMouseUp);
            });
        }
    }
}
