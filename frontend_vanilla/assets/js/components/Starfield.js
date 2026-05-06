/* Starfield.js - Background animation logic */
import { state } from '../core/State.js';

export class StarfieldComponent {
    constructor(canvasId) {
        this.canvas = document.getElementById(canvasId);
        if (!this.canvas) return;
        
        this.ctx = this.canvas.getContext('2d');
        this.stars = [];
        this.count = 200;
        
        this.init();
    }

    init() {
        this.resize();
        window.addEventListener('resize', () => this.resize());
        
        for (let i = 0; i < this.count; i++) {
            this.stars.push({
                x: Math.random() * this.canvas.width,
                y: Math.random() * this.canvas.height,
                size: Math.random() * 1.5,
                speed: Math.random() * 0.05 + 0.01
            });
        }
        
        this.animate();
    }

    resize() {
        this.canvas.width = window.innerWidth;
        this.canvas.height = window.innerHeight;
    }

    animate() {
        this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
        const isDark = state.theme === 'dark';
        this.ctx.fillStyle = isDark ? 'rgba(255, 255, 255, 0.8)' : 'rgba(124, 58, 237, 0.4)';

        this.stars.forEach(star => {
            this.ctx.beginPath();
            this.ctx.arc(star.x, star.y, star.size, 0, Math.PI * 2);
            this.ctx.fill();

            star.y -= star.speed;
            if (star.y < 0) star.y = this.canvas.height;
        });
        requestAnimationFrame(() => this.animate());
    }
}
