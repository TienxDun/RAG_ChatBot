// MessageRenderer.js - Logic for rendering chat messages
import { CONFIG } from '../core/Config.js';

export class MessageRenderer {
    static renderContent(content) {
        if (typeof marked !== 'undefined') {
            let html = marked.parse(content);
            // Bọc table vào div để xử lý scroll mà không làm hỏng display: table
            return html.replace(/<table>/g, '<div class="table-wrapper"><table>').replace(/<\/table>/g, '</table></div>');
        }
        return content.replace(/\n/g, '<br>');
    }

    static renderCodeBlock(content, lang = 'JSON') {
        return `
            <div class="terminal-code">
                <div class="terminal-code__header">
                    <div class="terminal-code__dots">
                        <div class="terminal-code__dot dot--red"></div>
                        <div class="terminal-code__dot dot--yellow"></div>
                        <div class="terminal-code__dot dot--green"></div>
                    </div>
                    <div class="terminal-code__right">
                        <span class="terminal-code__lang">${lang}</span>
                        <button class="terminal-copy-btn" title="Copy code" data-action="copy-code">
                            <i class="ph-bold ph-copy"></i>
                        </button>
                    </div>
                </div>
                <div class="terminal-code__body">
                    <pre><code>${content.trim()}</code></pre>
                </div>
            </div>`;
    }

    static renderRagStepsInner(steps) {
        if (!steps || steps.length === 0) return '';

        const getStepIcon = (title) => {
            const t = title.toLowerCase();
            const iconMap = {
                'vector': 'ph-file-search',
                'retrieval': 'ph-magnifying-glass',
                'schema': 'ph-database',
                'rules': 'ph-shield-warning',
                'sql': 'ph-code-block',
                'execution': 'ph-code-block',
                'healing': 'ph-magic-wand',
                'system': 'ph-gear'
            };
            
            const key = Object.keys(iconMap).find(k => t.includes(k));
            return iconMap[key] || 'ph-lightning';
        };

        return steps.map((step, idx) => {
            const isRules = step.title.toLowerCase().includes('rules');
            const panelClass = isRules ? 'rag-step__panel rag-step__panel--rules' : 'rag-step__panel';
            const dotClass = isRules ? 'rag-step__dot rag-step__dot--rules' : 'rag-step__dot';
            return `
            <div class="rag-step animate-fade-in" style="animation-delay: ${idx * 0.05}s">
                <div class="${dotClass}"></div>
                <div class="${panelClass}">
                    <div class="rag-step__title">
                        <span class="rag-step__title-text" style="display: flex; align-items: center; gap: 0.5rem;">
                            <i class="ph-duotone ${getStepIcon(step.title)}"></i>
                            ${step.title}
                        </span>
                        ${isRules ? `
                        <button class="rag-step__copy-rules" data-action="copy-rules" title="Copy rules">
                            <i class="ph ph-copy"></i>
                        </button>
                        ` : ''}
                    </div>
                    <div class="rag-step__content-inner text-sm opacity-80">${this.formatRagStepContent(step.content)}</div>
                </div>
            </div>
        `}).join('');
    }

    static renderRagSteps(steps, isStreaming = false) {
        if (!steps || steps.length === 0) return '';

        const escapedSteps = encodeURIComponent(JSON.stringify(steps));
        const stepsHtml = isStreaming ? this.renderRagStepsInner(steps) : '';

        return `
            <div class="rag-steps ${isStreaming ? 'rag-steps--streaming is-active' : ''}" data-steps="${escapedSteps}">
                <div class="rag-steps__header">
                    <button class="rag-steps__toggle ${isStreaming ? 'active' : ''}" data-action="toggle-steps">
                        <i class="ph-fill ph-lightning"></i>
                        <span>RAG TRACE (${steps.length} steps)</span>
                        <i class="ph-bold ph-caret-down"></i>
                    </button>
                    <button class="rag-steps__copy" data-action="copy-rag-trace" title="Copy toàn bộ RAG Trace">
                        <i class="ph ph-copy"></i>
                    </button>
                </div>
                <div class="rag-steps__content" ${!isStreaming ? 'style="display: none;"' : ''}>
                    ${stepsHtml}
                </div>
            </div>
        `;
    }

    static formatRagStepContent(content) {
        if (!content) return '';
        
        const placeholders = [];
        let processedContent = content;

        // Tách các trình xử lý Regex để code sạch hơn
        const extractToPlaceholder = (regex, type, contentProcessor) => {
            processedContent = processedContent.replace(regex, (match, ...args) => {
                const code = contentProcessor ? contentProcessor(match, ...args) : match;
                if (!code) return match;

                const placeholder = `[[TERMINAL_${type}_${placeholders.length}]]`;
                const lang = code.trim().toUpperCase().startsWith('SELECT') ? 'SQL' : 'JSON';
                const html = this.renderCodeBlock(code.trim(), lang);
                
                placeholders.push({ id: placeholder, html });
                return placeholder;
            });
        };

        // 1. Markdown code blocks
        extractToPlaceholder(/```(?:json|sql|sqlserver)?\s*([\s\S]*?)```/gi, 'MD', (m, code) => code);

        // 2. Raw JSON (skip nếu content giống Markdown table chứa ký tự |)
        extractToPlaceholder(/((?:\[\s*{[\s\S]*?}\s*\])|(?:{[\s\S]*?}))/g, 'JSON', (match) => {
            if (match.startsWith('[[TERMINAL_')) return null;
            // Bỏ qua nếu là fragment của Markdown table
            if (/^\|.*\|$/m.test(match)) return null;
            try {
                return JSON.stringify(JSON.parse(match), null, 2);
            } catch (e) { return null; }
        });

        // 3. Raw SQL
        extractToPlaceholder(/(SELECT\s+[\s\S]*?)(?:$|\n\n|\r\n\r\n|(?=Kết quả JSON:)|(?=```))/gi, 'SQL', (match) => {
            return match.startsWith('[[TERMINAL_') ? null : match;
        });

        let finalHtml = this.renderContent(processedContent);

        // Trả lại terminal vào vị trí ban đầu (xử lý ngược để giải quyết placeholder lồng nhau)
        for (let i = placeholders.length - 1; i >= 0; i--) {
            const p = placeholders[i];
            const wrappers = [`<p>${p.id}</p>`, `<strong>${p.id}</strong>`, p.id];
            for (const w of wrappers) {
                if (finalHtml.includes(w)) {
                    // Thay thế tất cả các lần xuất hiện của wrapper này bằng p.html
                    finalHtml = finalHtml.split(w).join(p.html);
                    break;
                }
            }
        }

        return finalHtml;
    }

    static createMessageElement(role, content, steps = [], suggestedQuestions = [], downloadUrl = null, rawData = null, userFile = null, loadingStatus = null, duration = null, isAmbiguous = false) {
        const messageEl = document.createElement('div');
        messageEl.className = `message message--${role === 'user' ? 'user' : 'ai'} animate-slide-up`;
        
        // Lưu markdown để hỗ trợ xuất Excel trực tiếp từ Markdown
        if (content) {
            messageEl.setAttribute('data-markdown', content);
        }

        // Lưu rawData vào data attribute để có thể truy xuất khi click export
        if (rawData) {
            try {
                const dataObj = typeof rawData === 'string' ? JSON.parse(rawData) : rawData;
                if (Array.isArray(dataObj)) {
                    messageEl.setAttribute('data-raw', JSON.stringify(dataObj));
                }
            } catch (e) {
                console.warn('Failed to parse rawData in createMessageElement', e);
            }
        }

        if (isAmbiguous) {
            messageEl.setAttribute('data-ambiguous', 'true');
        }

        // Lưu thông tin file vào attribute nếu là tin nhắn user
        if (role === 'user' && userFile) {
            messageEl.setAttribute('data-file', typeof userFile === 'string' ? userFile : userFile.name);
        }
        
        let html = role === 'ai' ? `<div class="ai-message-container">` : '';

        html += `
            <div class="message__bubble">
                ${role === 'user' && (userFile || messageEl.getAttribute('data-file')) ? this._renderFileChip(userFile || messageEl.getAttribute('data-file')) : ''}
                <div class="markdown-content">
                    ${content ? this.renderContent(content) : (steps.length > 0 ? '' : `
                        <div class="typing-container typing-container--small">
                            <div class="typing-dots"><span></span><span></span><span></span></div>
                            <span class="loading-text text-xs opacity-70">${loadingStatus || 'AI đang suy nghĩ...'}</span>
                        </div>
                    `)}
                </div>
                <div class="rag-steps-container">
                    ${steps.length > 0 ? this.renderRagSteps(steps, !content) : ''}
                </div>
        `;

        if (role === 'ai') {
            const displayDuration = duration !== null ? `(${duration}s)` : '(0s)';
            html += `
                <div class="message__footer">
                    <span class="ai-label">AI INSIGHT</span>
                    ${!content ? `<span class="loading-timer ml-1 text-xs opacity-50">${displayDuration}</span>` : `<span class="loading-timer ml-1 text-xs font-bold">${displayDuration}</span>`}
                    <div style="flex: 1"></div>
                    <div class="footer-actions-container">
                        ${content ? this._renderCopyButton() : ''}
                        ${this._renderUnifiedExportButton(downloadUrl, messageEl.getAttribute('data-raw'))}
                    </div>
                </div>
            `;
        }

        html += `</div>`; // Close bubble

        if (role === 'user') {
            html += `
                <div class="message__actions">
                    <button class="action-btn" data-action="edit-msg" title="Sửa tin nhắn">
                        <i class="ph-duotone ph-pencil-simple"></i>
                    </button>
                    <button class="action-btn" data-action="copy-msg" title="Copy tin nhắn">
                        <i class="ph-duotone ph-copy"></i>
                    </button>
                </div>
            `;
        } else {
            html += `<div class="suggestions-list-container"></div></div>`;
        }

        messageEl.innerHTML = html;

        if (role === 'ai' && suggestedQuestions?.length > 0) {
            const checkAmbiguous = isAmbiguous || messageEl.getAttribute('data-ambiguous') === 'true';
            if (checkAmbiguous) {
                this.renderClarificationOptions(messageEl, suggestedQuestions);
            } else {
                this.renderSuggestions(messageEl, suggestedQuestions);
            }
        }

        return messageEl;
    }

    static updateMessage(messageEl, content, steps = [], suggestedQuestions = [], downloadUrl = null, rawData = null, userFile = null, loadingStatus = null, duration = null, isAmbiguous = false) {
        const contentEl = messageEl.querySelector('.markdown-content');
        const stepsContainer = messageEl.querySelector('.rag-steps-container');
        const footerActionsContainer = messageEl.querySelector('.footer-actions-container');
        const bubbleEl = messageEl.querySelector('.message__bubble');
        
        if (rawData) {
            console.log("MessageRenderer: Received rawData for Excel export", rawData);
            try {
                const dataObj = typeof rawData === 'string' ? JSON.parse(rawData) : rawData;
                if (Array.isArray(dataObj)) {
                    messageEl.setAttribute('data-raw', JSON.stringify(dataObj));
                }
            } catch (e) {
                console.error("MessageRenderer: Failed to parse rawData", e);
            }
        }

        if (isAmbiguous) {
            messageEl.setAttribute('data-ambiguous', 'true');
        }

        if (userFile && bubbleEl && !bubbleEl.querySelector('.message-file-chip')) {
            const fileName = typeof userFile === 'string' ? userFile : userFile.name;
            messageEl.setAttribute('data-file', fileName);
            bubbleEl.insertAdjacentHTML('afterbegin', this._renderFileChip(fileName));
        }

        if (contentEl) {
            if (content) {
                contentEl.innerHTML = this.renderContent(content);
                messageEl.setAttribute('data-markdown', content);
            } else if (steps.length > 0) {
                // Nếu đang loading nhưng đã có steps, ẩn typing indicator để show steps rõ hơn
                contentEl.innerHTML = ''; 
            } else {
                contentEl.innerHTML = `
                    <div class="typing-container typing-container--small">
                        <div class="typing-dots"><span></span><span></span><span></span></div>
                        <span class="loading-text text-xs opacity-70">${loadingStatus || 'AI đang suy nghĩ...'}</span>
                    </div>
                `;
            }
        }
        if (stepsContainer && steps.length > 0) stepsContainer.innerHTML = this.renderRagSteps(steps, !content);
        
        const footerEl = messageEl.querySelector('.message__footer');
        if (footerEl && !content) {
            if (!footerEl.querySelector('.loading-timer')) {
                const aiLabel = footerEl.querySelector('.ai-label');
                if (aiLabel) {
                    const displayDuration = duration !== null ? `(${duration}s)` : '(0s)';
                    aiLabel.insertAdjacentHTML('afterend', `<span class="loading-timer ml-1 text-xs opacity-50">${displayDuration}</span>`);
                }
            }
        }
        if (footerActionsContainer) {
            let actionsHtml = '';
            // Nút Sao chép (Chỉ hiển thị khi đã có nội dung)
            if (content) actionsHtml += this._renderCopyButton();
            
            // Nút xuất Excel duy nhất (Ưu tiên mẫu, sau đó đến dữ liệu thô)
            actionsHtml += this._renderUnifiedExportButton(downloadUrl, messageEl.getAttribute('data-raw'));
            
            footerActionsContainer.innerHTML = actionsHtml;
        }

        if (suggestedQuestions?.length > 0) {
            const checkAmbiguous = isAmbiguous || messageEl.getAttribute('data-ambiguous') === 'true';
            if (checkAmbiguous) {
                this.renderClarificationOptions(messageEl, suggestedQuestions);
            } else {
                this.renderSuggestions(messageEl, suggestedQuestions);
            }
        }
    }

    static _renderFileChip(file) {
        const name = typeof file === 'string' ? file : file.name;
        return `
            <div class="message-file-chip">
                <i class="ph-fill ph-microsoft-excel-logo"></i>
                <span class="file-name">${name}</span>
            </div>
        `;
    }

    /**
     * Nút xuất Excel thông minh: Tự động quyết định dùng link download (mẫu) 
     * hay dùng button export (dữ liệu thô).
     */
    static _renderUnifiedExportButton(downloadUrl, rawDataStr) {
        if (!downloadUrl && !rawDataStr) return '';

        // TRƯỜNG HỢP 1: Có mẫu từ server (Ưu tiên số 1)
        if (downloadUrl) {
            let absoluteUrl = downloadUrl;
            if (!downloadUrl.startsWith('http')) {
                const baseUrl = CONFIG.API_BASE_URL.replace(/\/api$/, '');
                absoluteUrl = `${baseUrl}${downloadUrl.startsWith('/') ? '' : '/'}${downloadUrl}`;
            }
            return `
                <a href="${absoluteUrl}" target="_blank" class="footer-download" title="Tải báo cáo theo mẫu Excel">
                    <i class="ph-duotone ph-microsoft-excel-logo"></i> Xuất Excel
                </a>
            `;
        }

        // TRƯỜNG HỢP 2: Không có mẫu nhưng có dữ liệu thô (Dùng Generic Export)
        return `
            <button class="footer-download" data-action="export-msg-excel" title="Xuất dữ liệu này ra file Excel">
                <i class="ph-duotone ph-microsoft-excel-logo"></i> Xuất Excel
            </button>
        `;
    }

    static _renderCopyButton() {
        return `
            <button class="footer-copy" data-action="copy-msg" title="Sao chép nội dung">
                <i class="ph ph-copy"></i> Sao chép
            </button>
        `;
    }

    static renderSuggestions(messageEl, suggestedQuestions) {
        let container = messageEl.querySelector('.suggestions-list-container');
        if (!container) return;

        container.innerHTML = '';
        const listDiv = document.createElement('div');
        listDiv.className = 'suggestions-list';
        listDiv.innerHTML = suggestedQuestions.map(q => `
            <button class="suggestion-btn" data-action="quick-question" data-value="${q}">
                ${q}
            </button>
        `).join('');
        
        container.appendChild(listDiv);
    }

    static renderClarificationOptions(messageEl, suggestedQuestions) {
        let container = messageEl.querySelector('.suggestions-list-container');
        if (!container) return;

        container.innerHTML = '';
        const listDiv = document.createElement('div');
        listDiv.className = 'clarification-hub animate-fade-in';
        
        let html = `
            <div class="clarification-hub__title">
                <span>Ý bạn là một trong các khía cạnh phân tích dưới đây?</span>
            </div>
            <div class="clarification-list">
        `;

        suggestedQuestions.forEach((q, idx) => {
            html += `
                <div class="clarification-item animate-fade-in" data-action="quick-question" data-value="${q}">
                    <span class="clarification-item__index">${idx + 1}.</span>
                    <span class="clarification-item__text">${q}</span>
                    <i class="ph-bold ph-arrow-up-right clarification-item__arrow"></i>
                </div>
            `;
        });

        html += `</div>`;
        listDiv.innerHTML = html;
        container.appendChild(listDiv);
    }

    static createTypingIndicator() {
        const div = document.createElement('div');
        div.className = 'message message--ai animate-fade-in';
        div.id = 'typing-indicator';
        div.innerHTML = `
            <div class="ai-message-container">
                <div class="message__bubble loading-pulse">
                    <div class="typing-container">
                        <div class="typing-dots">
                            <span></span><span></span><span></span>
                        </div>
                        <span class="loading-text">AI đang suy nghĩ...</span>
                        <span class="loading-timer opacity-50 text-sm ml-2">(0s)</span>
                    </div>
                </div>
            </div>
        `;
        return div;
    }

    static updateTypingText(indicatorEl, text) {
        if (!indicatorEl) return;
        const textEl = indicatorEl.querySelector('.loading-text');
        if (textEl) {
            textEl.classList.add('animate-fade-in');
            textEl.innerText = text;
            setTimeout(() => textEl.classList.remove('animate-fade-in'), 500);
        }
    }
}
