/* main.js - UI Logic & FileManager */

document.addEventListener('DOMContentLoaded', () => {
    // --- Elements ---
    const html = document.documentElement;
    const sidebar = document.getElementById('sidebar');
    const sidebarOverlay = document.getElementById('sidebar-overlay');
    const openSidebarBtn = document.getElementById('open-sidebar');
    const closeSidebarBtn = document.getElementById('close-sidebar');
    const themeToggleBtn = document.getElementById('theme-toggle');
    const uploadBtn = document.getElementById('open-upload');
    const uploadModal = document.getElementById('upload-modal');
    const closeModalBtn = document.getElementById('close-modal');
    const cancelUploadBtn = document.getElementById('cancel-upload');
    const startUploadBtn = document.getElementById('start-upload');
    const chatInput = document.getElementById('chat-input');
    const scrollTopBtn = document.getElementById('scroll-top');
    const chatArea = document.getElementById('chat-area');
    const appContainer = document.querySelector('.app-container');

    // --- Sidebar Logic ---
    const toggleSidebar = () => {
        const isDesktop = window.innerWidth > 768;
        if (isDesktop) {
            sidebar.classList.toggle('sidebar--collapsed');
            appContainer.classList.toggle('app-container--expanded');
            sidebarOverlay.style.display = 'none';
        } else {
            sidebar.classList.toggle('active');
            sidebarOverlay.style.display = sidebar.classList.contains('active') ? 'block' : 'none';
            sidebar.classList.remove('sidebar--collapsed');
            appContainer.classList.remove('app-container--expanded');
        }
    };

    if (openSidebarBtn) openSidebarBtn.addEventListener('click', toggleSidebar);
    if (closeSidebarBtn) closeSidebarBtn.addEventListener('click', toggleSidebar);
    if (sidebarOverlay) sidebarOverlay.addEventListener('click', toggleSidebar);

    // --- Theme Logic ---
    const savedTheme = localStorage.getItem('theme') || 'light';
    html.setAttribute('data-theme', savedTheme);
    updateThemeIcon(savedTheme);

    themeToggleBtn.addEventListener('click', () => {
        const currentTheme = html.getAttribute('data-theme');
        const newTheme = currentTheme === 'light' ? 'dark' : 'light';
        html.setAttribute('data-theme', newTheme);
        localStorage.setItem('theme', newTheme);
        updateThemeIcon(newTheme);
    });

    function updateThemeIcon(theme) {
        const icon = themeToggleBtn.querySelector('i');
        icon.className = theme === 'dark' ? 'ph ph-sun' : 'ph ph-moon';
    }

    // --- FileManager Logic (Upload Modal) ---
    const dropzone = document.getElementById('dropzone');
    const fileInput = document.getElementById('file-input');
    const fileListContainer = document.getElementById('file-list');
    const progressContainer = document.getElementById('upload-progress-container');
    const progressBar = document.getElementById('progress-bar');
    const progressPercent = document.getElementById('progress-percent');
    const progressStatus = document.getElementById('progress-status');
    const modalInfoText = document.getElementById('modal-info-text');

    let selectedFiles = [];
    let isUploading = false;

    const updateUI = () => {
        // Render file list
        if (selectedFiles.length > 0) {
            fileListContainer.classList.remove('hidden');
            fileListContainer.innerHTML = selectedFiles.map((file, index) => `
                <div class="file-item animate-fade-in">
                    <div class="file-info">
                        <i class="ph ph-file-text"></i>
                        <div class="min-w-0">
                            <div class="file-name">${file.name}</div>
                            <div class="file-size">${(file.size / 1024).toFixed(1)} KB</div>
                        </div>
                    </div>
                    <button class="icon-btn text-red" onclick="removeFile(${index})" ${isUploading ? 'disabled' : ''}>
                        <i class="ph ph-trash"></i>
                    </button>
                </div>
            `).join('');
            modalInfoText.innerText = `${selectedFiles.length} file đã chọn`;
            startUploadBtn.disabled = isUploading;
        } else {
            fileListContainer.classList.add('hidden');
            modalInfoText.innerText = `Chưa có file nào được chọn`;
            startUploadBtn.disabled = true;
        }
    };

    window.removeFile = (index) => {
        selectedFiles.splice(index, 1);
        updateUI();
    };

    const handleFiles = (files) => {
        const validExtensions = ['.pdf', '.txt', '.json'];
        const newFiles = Array.from(files).filter(file => {
            const ext = '.' + file.name.split('.').pop().toLowerCase();
            return validExtensions.includes(ext);
        });

        selectedFiles = [...selectedFiles, ...newFiles];
        updateUI();
    };

    // Events
    dropzone.addEventListener('click', () => fileInput.click());
    fileInput.addEventListener('change', (e) => handleFiles(e.target.files));

    dropzone.addEventListener('dragover', (e) => {
        e.preventDefault();
        dropzone.classList.add('dragover');
    });
    dropzone.addEventListener('dragleave', () => dropzone.classList.remove('dragover'));
    dropzone.addEventListener('drop', (e) => {
        e.preventDefault();
        dropzone.classList.remove('dragover');
        handleFiles(e.dataTransfer.files);
    });

    const simulateUpload = async () => {
        if (selectedFiles.length === 0 || isUploading) return;

        isUploading = true;
        updateUI();
        progressContainer.classList.remove('hidden');
        dropzone.classList.add('hidden');
        startUploadBtn.disabled = true;

        for (let i = 0; i <= 100; i += 5) {
            progressBar.style.width = `${i}%`;
            progressPercent.innerText = `${i}%`;
            
            if (i < 30) progressStatus.innerText = "Đang tải file lên...";
            else if (i < 70) progressStatus.innerText = "Đang phân tích dữ liệu...";
            else if (i < 95) progressStatus.innerText = "Đang lưu vào cơ sở dữ liệu vector...";
            else progressStatus.innerText = "Hoàn tất!";

            await new Promise(r => setTimeout(r, 100));
        }

        setTimeout(() => {
            alert("Tải lên thành công!");
            closeModal();
        }, 500);
    };

    const closeModal = () => {
        if (isUploading) return;
        uploadModal.classList.add('hidden');
        selectedFiles = [];
        isUploading = false;
        progressContainer.classList.add('hidden');
        dropzone.classList.remove('hidden');
        progressBar.style.width = '0%';
        updateUI();
    };

    uploadBtn.addEventListener('click', () => uploadModal.classList.remove('hidden'));
    closeModalBtn.addEventListener('click', closeModal);
    cancelUploadBtn.addEventListener('click', closeModal);
    startUploadBtn.addEventListener('click', simulateUpload);

    window.addEventListener('click', (e) => {
        if (e.target === uploadModal) closeModal();
    });

    // --- Chat Input Auto-resize ---
    chatInput.addEventListener('input', function() {
        this.style.height = 'auto';
        this.style.height = (this.scrollHeight) + 'px';
        const sendBtn = document.getElementById('send-btn');
        if (sendBtn) sendBtn.disabled = this.value.trim() === '';
    });

    // --- Scroll Logic ---
    chatArea.addEventListener('scroll', () => {
        if (chatArea.scrollTop > 400) scrollTopBtn.classList.remove('hidden');
        else scrollTopBtn.classList.add('hidden');
    });

    scrollTopBtn.addEventListener('click', () => {
        chatArea.scrollTo({ top: 0, behavior: 'smooth' });
    });

    // --- Suggestion Tags ---
    document.querySelectorAll('.suggestion-tag').forEach(tag => {
        tag.addEventListener('click', () => {
            chatInput.value = tag.getAttribute('data-value');
            chatInput.dispatchEvent(new Event('input'));
            chatInput.focus();
        });
    });

    // --- Chat History Logic ---
    const chatHistoryContainer = document.getElementById('chat-history');
    const dummyHistory = [
        { id: 1, title: "hiện tại mấy giờ", date: "5/5/2026" },
        { id: 2, title: "Tổng số nhân viên", date: "5/5/2026" },
        { id: 3, title: "Cách sử dụng Qdrant cho người mới", date: "4/23/2026" },
        { id: 4, title: "tiến độ hoàn thành dự án", date: "4/23/2026" }
    ];

    const renderHistory = () => {
        if (!chatHistoryContainer) return;
        if (dummyHistory.length > 0) {
            chatHistoryContainer.innerHTML = dummyHistory.map(item => `
                <div class="history-item animate-fade-in" data-id="${item.id}">
                    <i class="ph ph-chat-circle"></i>
                    <div class="history-info">
                        <div class="history-title">${item.title}</div>
                        <div class="history-date">${item.date}</div>
                    </div>
                    <button class="history-delete" onclick="event.stopPropagation(); deleteHistory(${item.id})">
                        <i class="ph ph-trash"></i>
                    </button>
                </div>
            `).join('');
            
            // Re-bind click events for history items
            document.querySelectorAll('.history-item').forEach(el => {
                el.addEventListener('click', () => {
                    const id = el.getAttribute('data-id');
                    const history = dummyHistory.find(h => h.id == id);
                    if (history) {
                        alert(`Chuyển sang cuộc hội thoại: ${history.title}`);
                    }
                });
            });
        } else {
            chatHistoryContainer.innerHTML = '<div class="empty-state">Chưa có cuộc trò chuyện nào</div>';
        }
    };

    window.deleteHistory = (id) => {
        const index = dummyHistory.findIndex(i => i.id === id);
        if (index > -1) {
            dummyHistory.splice(index, 1);
            renderHistory();
        }
    };

    renderHistory();
});
