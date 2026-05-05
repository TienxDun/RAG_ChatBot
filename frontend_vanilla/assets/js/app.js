/* app.js - Main Application Entry Point */
import { ThemeComponent } from './components/Theme.js';
import { SidebarComponent } from './components/Sidebar.js';
import { ModalComponent } from './components/Modal.js';
import { ChatAreaComponent } from './components/ChatArea.js';
import { StarfieldComponent } from './components/Starfield.js';

class App {
    constructor() {
        this.init();
    }

    init() {
        console.log('🚀 DODO AI - App Initializing...');
        
        // Khởi tạo các component
        this.theme = new ThemeComponent();
        this.sidebar = new SidebarComponent();
        this.modal = new ModalComponent();
        this.chatArea = new ChatAreaComponent();
        this.starfield = new StarfieldComponent('starfield');

        console.log('✅ DODO AI - App Ready');
    }
}

// Khởi chạy ứng dụng khi DOM sẵn sàng
document.addEventListener('DOMContentLoaded', () => {
    window.app = new App();
});
