// Header.js - Header dropdown component
export class HeaderComponent {
    constructor() {
        this.container = document.querySelector('.header__dropdown-container');
        this.trigger = document.getElementById('header-dropdown-trigger');
        this.menu = document.getElementById('header-dropdown-menu');
        this.items = this.menu ? this.menu.querySelectorAll('.dropdown-item') : [];
        
        this.isOpen = false;
        this.init();
    }

    init() {
        if (!this.trigger || !this.menu) return;

        // Toggle click event
        this.trigger.addEventListener('click', (e) => {
            e.stopPropagation();
            if (this.isOpen) {
                this.close();
            } else {
                this.open();
            }
        });

        // Click outside event
        document.addEventListener('click', (e) => {
            if (this.isOpen && !this.container.contains(e.target)) {
                this.close();
            }
        });

        // Close on ESC key press
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && this.isOpen) {
                this.close();
            }
        });

        // Close dropdown when any item inside is clicked
        this.items.forEach(item => {
            item.addEventListener('click', () => {
                // Đóng menu sau một khoảng delay nhỏ để người dùng thấy feedback click
                setTimeout(() => this.close(), 150);
            });
        });
    }

    open() {
        if (this.isOpen) return;
        this.isOpen = true;

        this.container.classList.add('active');
        this.menu.classList.remove('hidden');
        
        // Force reflow to ensure CSS transitions trigger
        void this.menu.offsetWidth;
        
        this.menu.classList.add('show');
    }

    close() {
        if (!this.isOpen) return;
        this.isOpen = false;

        this.container.classList.remove('active');
        this.menu.classList.remove('show');

        // Wait for CSS transition to finish before adding hidden back
        const onTransitionEnd = (e) => {
            if (e.propertyName === 'opacity' && !this.isOpen) {
                this.menu.classList.add('hidden');
                this.menu.removeEventListener('transitionend', onTransitionEnd);
            }
        };
        this.menu.addEventListener('transitionend', onTransitionEnd);

        // Fallback in case transitionend does not fire
        setTimeout(() => {
            if (!this.isOpen) {
                this.menu.classList.add('hidden');
            }
        }, 350);
    }
}
