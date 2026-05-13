/**
 * InteractionService.js - Manages UI interactions like clipboard and message actions
 */
import { Toast } from '../components/Toast.js';

export class InteractionService {
    static async copyToClipboard(text, btn, isFooter = false) {
        if (!text) return;

        try {
            await navigator.clipboard.writeText(text);
            this.showCopyFeedback(btn, isFooter);
            Toast.success("Đã sao chép!");
            return true;
        } catch (err) {
            console.error('Copy failed:', err);
            Toast.error("Không thể sao chép");
            return false;
        }
    }

    static showCopyFeedback(btn, isFooter = false) {
        const icon = btn.querySelector('i');
        if (!icon) return;

        const originalClass = icon.className;
        const originalHTML = btn.innerHTML;

        icon.className = 'ph-bold ph-check text-green-500';
        if (isFooter) btn.innerHTML = '<i class="ph-bold ph-check text-green-500"></i> Copied';

        setTimeout(() => {
            if (isFooter) btn.innerHTML = originalHTML;
            else icon.className = originalClass;
        }, 2000);
    }

    static getMessageContent(btn) {
        const container = btn.closest('.message') || btn.closest('.message__bubble');
        return container?.querySelector('.markdown-content')?.innerText || "";
    }

    static getTerminalCode(btn) {
        return btn.closest('.terminal-code')?.querySelector('code')?.innerText || "";
    }
}
