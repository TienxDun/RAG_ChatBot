import { CONFIG } from './Config.js';
import { env } from '../env.js';

/**
 * ApiClient.js - Centralized API handler with environment-awareness and clean architecture
 */
export class ApiClient {
    /**
     * Helper để xây dựng URL đầy đủ nếu cần
     * @param {string} endpoint - Path hoặc URL đầy đủ
     */
    static _resolveUrl(endpoint) {
        if (endpoint.startsWith('http')) return endpoint;
        const cleanPath = endpoint.startsWith('/') ? endpoint : `/${endpoint}`;
        return `${CONFIG.API_BASE_URL}${cleanPath}`;
    }

    /**
     * Helper log theo điều kiện môi trường và tùy chọn silent
     */
    static _log(options, ...args) {
        if (env.DEBUG && !options?.silent) console.log(...args);
    }

    /**
     * Helper log group theo điều kiện môi trường và tùy chọn silent
     */
    static _group(options, label) {
        if (env.DEBUG && !options?.silent) console.group(label);
    }

    /**
     * Helper kết thúc log group
     */
    static _groupEnd(options) {
        if (env.DEBUG && !options?.silent) console.groupEnd();
    }

    /**
     * Xử lý lỗi phản hồi từ Server
     */
    static async _handleResponse(response) {
        if (!response.ok) {
            const errorData = await response.json().catch(() => ({}));
            const errorMessage = errorData.error || `Lỗi hệ thống (${response.status})`;
            throw new Error(errorMessage);
        }
        return response;
    }

    /**
     * Gửi yêu cầu GET thông thường
     * @param {string} endpoint 
     * @param {object} options - { silent: boolean }
     */
    static async get(endpoint, options = {}) {
        const url = this._resolveUrl(endpoint);
        this._group(options, `🌐 API GET: ${url}`);

        try {
            const response = await fetch(url, {
                method: 'GET',
            });

            await this._handleResponse(response);
            const result = await response.json();
            this._log(options, '📥 Response:', result);
            return result;
        } catch (error) {
            if (!options.silent) console.error('🔴 API Get Error:', error);
            throw error;
        } finally {
            this._groupEnd(options);
        }
    }

    /**
     * Gửi yêu cầu POST thông thường (JSON)
     * @param {string} endpoint 
     * @param {object} data 
     * @param {object} options - { silent: boolean }
     */
    static async post(endpoint, data, options = {}) {
        const url = this._resolveUrl(endpoint);
        this._group(options, `🌐 API POST: ${url}`);
        this._log(options, '📤 Payload:', data);

        try {
            const response = await fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });

            await this._handleResponse(response);
            const result = await response.json();
            this._log(options, '📥 Response:', result);
            return result;
        } catch (error) {
            if (!options.silent) console.error('🔴 API Post Error:', error);
            throw error;
        } finally {
            this._groupEnd(options);
        }
    }

    /**
     * Gửi yêu cầu và nhận dữ liệu stream (SSE)
     */
    static async fetchStream(endpoint, options = {}, onMessage) {
        const url = this._resolveUrl(endpoint);
        this._group(options, `📡 API STREAM: ${url}`);
        
        const headers = options.headers || {};
        if (!options.headers && !(options.body instanceof FormData)) {
            headers['Content-Type'] = 'application/json';
        }

        try {
            const response = await fetch(url, {
                method: 'POST',
                headers,
                body: options.body,
                signal: options.signal
            });

            await this._handleResponse(response);

            const reader = response.body.getReader();
            const decoder = new TextDecoder();
            let lineBuffer = '';
            let currentMessageData = '';

            this._log(options, '⏳ Stream started...');

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;

                lineBuffer += decoder.decode(value, { stream: true });
                const lines = lineBuffer.split(/\r?\n/);
                lineBuffer = lines.pop();

                for (const line of lines) {
                    if (line === '' || line === '\r') {
                        if (currentMessageData) {
                            this._parseAndNotify(currentMessageData, onMessage, options);
                            currentMessageData = '';
                        }
                        continue;
                    }

                    if (line.startsWith('data:')) {
                        // Bóc tách nội dung: bỏ "data:" và dấu cách đầu tiên nếu có
                        let content = line.slice(5);
                        if (content.startsWith(' ')) content = content.slice(1);
                        currentMessageData += content;
                    } else if (line.startsWith(':')) {
                        // Bỏ qua comments
                        continue;
                    }
                }
            }

            // Xử lý nốt dữ liệu cuối cùng nếu còn
            if (currentMessageData) {
                this._parseAndNotify(currentMessageData, onMessage, options);
            }

            this._log(options, '🏁 Stream completed');
        } catch (error) {
            if (error.name === 'AbortError') {
                this._log(options, '⏹️ Stream aborted');
            } else {
                if (!options.silent) console.error('🔴 API Stream Error:', error);
                throw error;
            }
        } finally {
            this._groupEnd(options);
        }
    }

    /**
     * Tải file và nhận stream tiến trình
     */
    static async uploadFiles(endpoint, files, onProgress) {
        const formData = new FormData();
        files.forEach(file => formData.append('files', file));

        return this.fetchStream(endpoint, {
            body: formData
        }, onProgress);
    }

    /**
     * Helper parse JSON từ SSE
     */
    static _parseAndNotify(jsonString, onMessage, options = {}) {
        try {
            const data = JSON.parse(jsonString);
            
            if (env.DEBUG && !options.silent) {
                if (data.type === 'step') this._log(options, `  ⚡ [Step]: ${data.step?.title}`);
                else if (data.type === 'progress') this._log(options, `  📊 [Progress]: ${data.percent}%`);
            }

            onMessage(data);
        } catch (e) {
            if (!options.silent) console.warn('❌ Failed to parse SSE JSON:', jsonString);
        }
    }
}
