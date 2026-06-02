/**
 * FileHandler.js - Manages file validation and UI previews
 */
import { Toast } from '../components/Toast.js';

export class FileHandler {
    static validateExcel(file) {
        if (!file) return false;
        const ext = file.name.split('.').pop().toLowerCase();
        if (ext !== 'xlsx') {
            Toast.error("Chỉ hỗ trợ file Excel (.xlsx)");
            return false;
        }
        return true;
    }

    static renderPreview(container, file, options = {}) {
        if (!container || !file) return;
        
        const { onRemove, hideSuggestions } = options;
        if (hideSuggestions) hideSuggestions();

        container.classList.remove('hidden');
        container.innerHTML = `
            <div class="file-preview-chip animate-in zoom-in duration-300">
                <i class="ph-fill ph-microsoft-excel-logo"></i>
                <span class="file-name">${file.name}</span>
                <button class="btn-remove-preview" id="remove-file-btn" title="Gỡ bỏ file">
                    <i class="ph-bold ph-x"></i>
                </button>
            </div>
        `;

        document.getElementById('remove-file-btn').addEventListener('click', () => {
            if (onRemove) onRemove();
        });
    }

    static clearPreview(container, options = {}) {
        if (!container) return;
        const { showSuggestions } = options;
        
        container.classList.add('hidden');
        container.innerHTML = '';
        if (showSuggestions) showSuggestions();
    }
}
