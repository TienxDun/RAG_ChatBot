/* MessageRenderer.js - Logic for rendering chat messages */
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

    static renderRagSteps(steps) {
        if (!steps || steps.length === 0) return '';

        const getStepIcon = (title) => {
            const t = title.toLowerCase();
            const iconMap = {
                'vector': 'ph-file-search',
                'schema': 'ph-database',
                'sql': 'ph-code-block',
                'execution': 'ph-code-block',
                'healing': 'ph-magic-wand'
            };
            
            const key = Object.keys(iconMap).find(k => t.includes(k));
            return iconMap[key] || 'ph-lightning';
        };

        const stepsHtml = steps.map((step, idx) => `
            <div class="rag-step animate-fade-in" style="animation-delay: ${idx * 0.1}s">
                <div class="rag-step__dot"></div>
                <div class="rag-step__panel">
                    <div class="rag-step__title">
                        <i class="ph-duotone ${getStepIcon(step.title)}"></i>
                        ${step.title}
                    </div>
                    <div class="rag-step__content-inner text-sm opacity-80">${this.formatRagStepContent(step.content)}</div>
                </div>
            </div>
        `).join('');

        return `
            <div class="rag-steps">
                <button class="rag-steps__toggle" data-action="toggle-steps">
                    <i class="ph-fill ph-lightning"></i>
                    <span>RAG TRACE (${steps.length} steps)</span>
                    <i class="ph-bold ph-caret-down"></i>
                </button>
                <div class="rag-steps__content">
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

        // 2. Raw JSON
        extractToPlaceholder(/((?:\[\s*{[\s\S]*?}\s*\])|(?:{[\s\S]*?}))/g, 'JSON', (match) => {
            if (match.startsWith('[[TERMINAL_')) return null;
            try {
                return JSON.stringify(JSON.parse(match), null, 2);
            } catch (e) { return null; }
        });

        // 3. Raw SQL
        extractToPlaceholder(/(SELECT\s+[\s\S]*?)(?:$|\n\n|\r\n\r\n|(?=Kết quả JSON:)|(?=```))/gi, 'SQL', (match) => {
            return match.startsWith('[[TERMINAL_') ? null : match;
        });

        let finalHtml = this.renderContent(processedContent);

        // Trả lại terminal vào vị trí ban đầu
        placeholders.forEach(p => {
            const wrappers = [`<p>${p.id}</p>`, `<strong>${p.id}</strong>`, p.id];
            for (const w of wrappers) {
                if (finalHtml.includes(w)) {
                    finalHtml = finalHtml.split(w).join(p.html);
                    break;
                }
            }
        });

        return finalHtml;
    }

    static createMessageElement(role, content, steps = [], suggestedQuestions = [], downloadUrl = null) {
        const messageEl = document.createElement('div');
        messageEl.className = `message message--${role === 'user' ? 'user' : 'ai'} animate-slide-up`;
        
        let html = role === 'ai' ? `<div class="ai-message-container">` : '';

        html += `
            <div class="message__bubble">
                <div class="markdown-content">
                    ${this.renderContent(content)}
                </div>
                <div class="rag-steps-container">
                    ${steps.length > 0 ? this.renderRagSteps(steps) : ''}
                </div>
        `;

        if (role === 'ai') {
            html += `
                <div class="message__footer">
                    <span class="ai-label">AI INSIGHT</span>
                    <div style="flex: 1"></div>
                    <div class="download-footer-container">
                        ${downloadUrl ? this._renderDownloadSection(downloadUrl) : ''}
                    </div>
                    <button class="footer-copy" data-action="copy-msg">
                        <i class="ph-duotone ph-copy"></i> Copy
                    </button>
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
            this.renderSuggestions(messageEl, suggestedQuestions);
        }

        return messageEl;
    }

    static updateMessage(messageEl, content, steps = [], suggestedQuestions = [], downloadUrl = null) {
        const contentEl = messageEl.querySelector('.markdown-content');
        const stepsContainer = messageEl.querySelector('.rag-steps-container');
        const downloadContainer = messageEl.querySelector('.download-footer-container');
        
        if (contentEl) contentEl.innerHTML = this.renderContent(content);
        if (stepsContainer && steps.length > 0) stepsContainer.innerHTML = this.renderRagSteps(steps);
        if (downloadContainer && downloadUrl) downloadContainer.innerHTML = this._renderDownloadSection(downloadUrl);

        if (suggestedQuestions?.length > 0) {
            this.renderSuggestions(messageEl, suggestedQuestions);
        }
    }

    static _renderDownloadSection(url) {
        if (!url) return '';
        
        const absoluteUrl = url.startsWith('http') ? url : `${CONFIG.API_BASE_URL}${url.startsWith('/') ? '' : '/'}${url}`;

        return `
            <a href="${absoluteUrl}" target="_blank" class="footer-download" title="Tải xuống báo cáo Excel">
                <i class="ph-duotone ph-file-xls"></i> Excel
            </a>
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

    static createTypingIndicator() {
        const div = document.createElement('div');
        div.className = 'message message--ai animate-fade-in';
        div.id = 'typing-indicator';
        div.innerHTML = `
            <div class="message__bubble">
                <div class="typing">
                    <span></span><span></span><span></span>
                </div>
            </div>
        `;
        return div;
    }
}
