/**
 * ExportService.js - Manages Excel data exportation
 */
import { ApiClient } from '../core/ApiClient.js';
import { ENDPOINTS } from '../core/Config.js';
import { Toast } from '../components/Toast.js';

export class ExportService {
    static async exportToExcel(data, triggerBtn, options = {}) {
        if (!data) {
            Toast.warning("Không có dữ liệu để xuất!");
            return;
        }

        // Kiểm tra xem là mảng có dữ liệu hoặc object có chứa markdownText hợp lệ
        const hasData = Array.isArray(data) ? data.length > 0 : (data.markdownText && data.markdownText.trim().length > 0);
        if (!hasData) {
            Toast.warning("Không có dữ liệu để xuất!");
            return;
        }

        const originalHTML = triggerBtn.innerHTML;
        const defaultLabel = options.defaultLabel || originalHTML;
        
        try {
            triggerBtn.disabled = true;
            triggerBtn.innerHTML = '<i class="ph-bold ph-spinner-gap animate-spin"></i>';

            const url = ApiClient._resolveUrl(ENDPOINTS.EXPORT_EXCEL);
            const response = await fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });

            if (!response.ok) throw new Error("Xuất file thất bại");

            const blob = await response.blob();
            this.downloadBlob(blob, `export_${this._getTimestamp()}.xlsx`);
            Toast.success("Export Excel thành công!");
        } catch (error) {
            console.error('Export error:', error);
            Toast.error("Lỗi khi xuất file Excel");
        } finally {
            triggerBtn.disabled = false;
            triggerBtn.innerHTML = defaultLabel;
        }
    }

    static _getTimestamp() {
        const now = new Date();
        const pad = (num) => String(num).padStart(2, '0');
        return `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;
    }

    static downloadBlob(blob, filename) {
        const blobUrl = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = blobUrl;
        a.download = filename;
        a.click();
        window.URL.revokeObjectURL(blobUrl);
    }
}
