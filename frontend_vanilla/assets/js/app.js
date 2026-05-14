// app.js - Main Application Entry Point
import { ThemeComponent } from './components/Theme.js';
import { SidebarComponent } from './components/Sidebar.js';
import { ModalComponent } from './components/Modal.js';
import { ChatAreaComponent } from './components/ChatArea.js';
import { StarfieldComponent } from './components/Starfield.js';
import { state } from './core/State.js';
import { ENDPOINTS } from './core/Config.js';
import { ApiClient } from './core/ApiClient.js';

class App {
    constructor() {
        this.init();
    }

    init() {
        // Khởi tạo các component
        this.theme = new ThemeComponent();
        this.sidebar = new SidebarComponent();
        this.modal = new ModalComponent();
        this.chatArea = new ChatAreaComponent();
        this.starfield = new StarfieldComponent('starfield');

        this.initHealthCheck();
        console.log('✅ DODO AI - App Ready');
    }

    initHealthCheck() {
        const statusDot = document.querySelector('.status-dot');
        const statusText = document.querySelector('.status-text');

        const updateUI = (isOnline) => {
            if (!statusDot) return;
            if (isOnline) {
                statusDot.classList.remove('offline');
                statusDot.classList.add('online');
                if (statusText) statusText.innerText = '.NET API';
            } else {
                statusDot.classList.remove('online');
                statusDot.classList.add('offline');
                if (statusText) statusText.innerText = 'OFFLINE';
            }
        };

        // Đăng ký nhận thông báo từ State
        state.subscribe((key, value) => {
            if (key === 'isBackendOnline') updateUI(value);
        });

        let isFirstHealthCheck = true;

        // Hàm kiểm tra thực tế
        const check = async () => {
            try {
                // Chỉ log lần đầu tiên, các lần sau chạy ngầm (silent)
                await ApiClient.get(ENDPOINTS.HEALTH, { silent: !isFirstHealthCheck });
                state.isBackendOnline = true;
                isFirstHealthCheck = false; 
            } catch (e) {
                state.isBackendOnline = false;
            }
        };

        // Chạy ngay lập tức và sau đó mỗi 30s
        check();
        setInterval(check, 30000);
    }
}

// Khởi chạy ứng dụng khi DOM sẵn sàng
document.addEventListener('DOMContentLoaded', () => {
    window.app = new App();
});
