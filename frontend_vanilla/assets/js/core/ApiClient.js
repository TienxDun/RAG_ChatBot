/* ApiClient.js - Centralized API handler with streaming support */

export class ApiClient {
    /**
     * Gửi yêu cầu thông thường (JSON)
     */
    static async post(url, data) {
        console.group(`🌐 API POST: ${url}`);
        console.log('📤 Payload:', data);
        try {
            const response = await fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify(data),
            });

            if (!response.ok) {
                const errorData = await response.json().catch(() => ({}));
                throw new Error(errorData.error || `HTTP error! status: ${response.status}`);
            }

            const result = await response.json();
            console.log('📥 Response:', result);
            return result;
        } catch (error) {
            console.error('🔴 API Post Error:', error);
            throw error;
        } finally {
            console.groupEnd();
        }
    }

    /**
     * Gửi yêu cầu và nhận dữ liệu stream (Server-Sent Events)
     * Dùng cho Chat và Upload progress
     */
    static async fetchStream(url, options, onMessage) {
        console.group(`📡 API STREAM: ${url}`);
        console.log('📤 Options:', options);
        
        const headers = options.headers || {};
        
        // Nếu không có headers và body không phải FormData, mới set mặc định là JSON
        if (!options.headers && !(options.body instanceof FormData)) {
            headers['Content-Type'] = 'application/json';
        }

        try {
            const response = await fetch(url, {
                method: 'POST',
                headers: headers,
                body: options.body,
                signal: options.signal
            });

            if (!response.ok) {
                const errorData = await response.json().catch(() => ({}));
                throw new Error(errorData.error || `HTTP error! status: ${response.status}`);
            }

            const reader = response.body.getReader();
            const decoder = new TextDecoder();
            let lineBuffer = '';
            let currentMessageData = '';

            console.log('⏳ Stream started...');

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;

                lineBuffer += decoder.decode(value, { stream: true });
                const lines = lineBuffer.split(/\r?\n/);
                lineBuffer = lines.pop(); // Giữ lại phần chưa hoàn chỉnh

                for (const line of lines) {
                    const trimmedLine = line.trim();
                    
                    if (trimmedLine.startsWith('data: ')) {
                        currentMessageData += trimmedLine.replace('data: ', '');
                    } else if (trimmedLine === '' && currentMessageData) {
                        this.parseAndNotify(currentMessageData, onMessage);
                        currentMessageData = '';
                    } else if (trimmedLine !== '') {
                        // Tích lũy các dòng không có prefix (hỗ trợ multi-line JSON)
                        currentMessageData += trimmedLine;
                    }
                }
            }

            // Xử lý nốt dữ liệu còn sót lại nếu stream đóng mà không có dòng trống cuối cùng
            if (currentMessageData) {
                this.parseAndNotify(currentMessageData, onMessage);
            }

            console.log('🏁 Stream completed');
        } catch (error) {
            if (error.name === 'AbortError') {
                console.log('⏹️ Stream aborted');
            } else {
                console.error('🔴 API Stream Error:', error);
                throw error;
            }
        } finally {
            console.groupEnd();
        }
    }

    /**
     * Tải file và nhận stream tiến trình
     */
    static async uploadFiles(url, files, onProgress) {
        const formData = new FormData();
        files.forEach(file => formData.append('files', file));

        return this.fetchStream(url, {
            body: formData
        }, onProgress);
    }

    /**
     * Helper để parse JSON từ SSE và thông báo cho listener
     */
    static parseAndNotify(jsonString, onMessage) {
        try {
            const data = JSON.parse(jsonString);
            
            // Log chi tiết từng loại event để dễ debug
            if (data.type === 'step') {
                console.log(`  ⚡ [Step]: ${data.step?.title || 'Processing...'}`);
            } else if (data.type === 'progress') {
                console.log(`  📊 [Progress]: ${data.percent}% - ${data.message}`);
            } else if (data.type === 'final' || data.type === 'result') {
                console.log('✅ [Final Received]', data);
            }

            onMessage(data);
        } catch (e) {
            console.warn('❌ Failed to parse SSE JSON:', jsonString);
            console.error(e);
        }
    }
}
