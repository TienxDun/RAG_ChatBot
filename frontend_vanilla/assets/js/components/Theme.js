/* Theme.js - Theme switching logic */
import { state } from '../core/State.js';
import { SELECTORS } from '../core/Config.js';

export class ThemeComponent {
    constructor() {
        this.html = document.querySelector(SELECTORS.HTML);
        this.btn = document.querySelector(SELECTORS.THEME_TOGGLE);
        this.icon = this.btn?.querySelector('i');
        
        this.init();
    }

    init() {
        if (!this.btn) return;
        
        // Initial apply
        this.applyTheme(state.theme);
        
        // Event listener
        this.btn.addEventListener('click', () => {
            state.theme = state.theme === 'light' ? 'dark' : 'light';
        });
        
        // Subscribe to state changes
        state.subscribe((key, value) => {
            if (key === 'theme') this.applyTheme(value);
        });
    }

    applyTheme(theme) {
        this.html.setAttribute('data-theme', theme);
        if (this.icon) {
            this.icon.className = theme === 'dark' ? 'ph ph-sun' : 'ph ph-moon';
        }
    }
}
