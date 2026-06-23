/**
 * TemplateCacheService.js - Service frontend để quản lý việc lưu trữ template Excel trống vào bộ nhớ đệm.
 * Service này được kích hoạt khi người dùng gửi prompt kèm theo file Excel.
 */
import { ApiClient } from '../core/ApiClient.js';
import { env } from '../env.js';

export class TemplateCacheService {
    /**
     * Gửi file template trống lên server để lưu vào bộ nhớ đệm (RAM).
     * @param {File} file - Đối tượng file Excel được chọn từ input hoặc drag-drop.
     * @returns {Promise<Object|null>} Trả về thông tin cache (id, fileName,...) hoặc null nếu thất bại.
     */
    static async cacheTemplate(file) {
        if (!file) return null;

        // Chuẩn bị dữ liệu dạng Form để gửi file
        const formData = new FormData();
        formData.append('file', file);

        try {
            // Sử dụng helper _resolveUrl của ApiClient để lấy URL từ cấu hình môi trường
            // Lưu ý: Không dùng ApiClient.post() vì hàm đó mặc định gửi JSON.
            const url = ApiClient._resolveUrl('/templates/cache');
            
            const response = await fetch(url, {
                method: 'POST',
                body: formData
                // Quan trọng: Để trống Content-Type để trình duyệt tự động thiết lập multipart/form-data kèm boundary
            });

            if (!response.ok) {
                const errorData = await response.json().catch(() => ({}));
                if (env.DEBUG) {
                    console.warn('⚠️ Template Cache failed:', errorData.error || response.statusText);
                }
                return null;
            }

            const result = await response.json();
            if (env.DEBUG) {
                console.log('✅ Template cached successfully:', result);
            }
            return result;
        } catch (error) {
            if (env.DEBUG) {
                console.error('❌ TemplateCacheService error:', error);
            }
            return null;
        }
    }

    /**
     * Lấy danh sách tất cả các template hiện đang được lưu trong RAM của Server.
     * @returns {Promise<Array>} Danh sách các template đã cache.
     */
    static async getAll() {
        try {
            // GET request không cần xử lý đặc biệt, có thể dùng ApiClient.get
            return await ApiClient.get('/templates/cache');
        } catch (error) {
            if (env.DEBUG) {
                console.error('❌ Failed to fetch cached templates:', error);
            }
            return [];
        }
    }

    /**
     * Xóa sạch toàn bộ bộ nhớ đệm template (nếu cần thiết).
     */
    static async clearAll() {
        try {
            const url = ApiClient._resolveUrl('/templates/cache');
            const response = await fetch(url, { method: 'DELETE' });
            return response.ok;
        } catch (error) {
            return false;
        }
    }

    /**
     * Xóa một template cụ thể khỏi bộ nhớ đệm
     * @param {string} id - ID của template cần xóa
     * @returns {Promise<boolean>} Trả về true nếu xóa thành công
     */
    static async removeTemplate(id) {
        try {
            const url = ApiClient._resolveUrl(`/templates/cache/${id}`);
            const response = await fetch(url, { method: 'DELETE' });
            return response.ok;
        } catch (error) {
            if (env.DEBUG) console.error('❌ Failed to remove template:', error);
            return false;
        }
    }

    /**
     * Tải về nội dung file template từ bộ nhớ đệm
     * @param {string} id - ID của template
     * @returns {Promise<Blob|null>} Trả về Blob của file hoặc null nếu lỗi
     */
    static async downloadTemplate(id) {
        try {
            const url = ApiClient._resolveUrl(`/templates/cache/${id}`);
            const response = await fetch(url);
            if (!response.ok) return null;
            return await response.blob();
        } catch (error) {
            if (env.DEBUG) console.error('❌ Failed to download template:', error);
            return null;
        }
    }

    /**
     * Đổi tên một template cụ thể
     * @param {string} id - ID của template cần đổi tên
     * @param {string} newName - Tên file mới
     * @returns {Promise<object>} Trả về JSON kết quả
     */
    static async renameTemplate(id, newName) {
        try {
            const url = ApiClient._resolveUrl(`/templates/cache/${id}/rename`);
            const response = await fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ newName })
            });
            if (!response.ok) {
                const errData = await response.json().catch(() => ({}));
                throw new Error(errData.error || 'Lỗi khi đổi tên file mẫu.');
            }
            return await response.json();
        } catch (error) {
            console.error('❌ Failed to rename template:', error);
            throw error;
        }
    }
}
