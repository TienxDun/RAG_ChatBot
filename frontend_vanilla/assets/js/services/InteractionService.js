/**
 * InteractionService.js - Manages UI interactions like clipboard and message actions
 */
import { Toast } from '../components/Toast.js';

export class InteractionService {
    static copyToClipboard(text, btn, isFooter = false) {
        if (!text) return false;

        // Phản hồi UI lập tức để loại bỏ hoàn toàn cảm giác lag
        this.showCopyFeedback(btn, isFooter);
        Toast.success("Đã sao chép!");

        // Thực hiện ghi vào clipboard bất đồng bộ
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).catch(err => {
                console.error('Async clipboard copy failed:', err);
                Toast.error("Không thể sao chép");
            });
        } else {
            try {
                const textarea = document.createElement('textarea');
                textarea.value = text;
                textarea.style.position = 'fixed';
                textarea.style.opacity = '0';
                document.body.appendChild(textarea);
                textarea.select();
                const success = document.execCommand('copy');
                document.body.removeChild(textarea);
                if (!success) throw new Error('execCommand copy returned false');
            } catch (err) {
                console.error('Fallback copy failed:', err);
                Toast.error("Không thể sao chép");
            }
        }
        return true;
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
        return btn.closest('.terminal-code')?.querySelector('code')?.textContent || "";
    }
}
