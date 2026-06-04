// MessageRenderer.js - Logic for rendering chat messages

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
            const stepClass = isRules ? 'rag-step rag-step--rules animate-fade-in' : 'rag-step animate-fade-in';
            return `
            <div class="${stepClass}" style="animation-delay: ${idx * 0.05}s">
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

    static renderPerformanceMetadata(metadata) {
        if (!metadata || metadata.performance_enabled !== 'true') return '';

        const getVal = (key, fallback = 0) => parseInt(metadata[key] || fallback, 10);
        
        const embedding = getVal('embedding_ms');
        const schema = getVal('schema_retrieval_ms');
        const planning = getVal('planning_ms');
        const execution = getVal('execution_ms');
        const generation = getVal('generation_ms');
        const total = getVal('total_ms');

        const planningPrompt = getVal('planning_prompt_tokens');
        const planningCand = getVal('planning_candidates_tokens');
        const sqlPrompt = getVal('sql_prompt_tokens');
        const sqlCand = getVal('sql_candidates_tokens');
        const genPrompt = getVal('generation_prompt_tokens');
        const genCand = getVal('generation_candidates_tokens');

        const totalPrompt = getVal('total_prompt_tokens');
        const totalCand = getVal('total_candidates_tokens');
        const totalTokens = getVal('total_tokens');

        const getPct = (val) => total > 0 ? Math.min((val / total) * 100, 100).toFixed(1) : 0;

        return `
            <div class="perf-metric border-t border-dashed border-gray-200/10 mt-3 pt-3">
                <button class="perf-metric__toggle" data-action="toggle-perf">
                    <i class="ph-bold ph-gauge"></i>
                    <span>Hiệu năng & Token (${total} ms • ${totalTokens} tokens)</span>
                    <i class="ph-bold ph-caret-down perf-caret"></i>
                </button>
                <div class="perf-metric__content hidden">
                    <div class="perf-dashboard">
                        <!-- Phân tích Latency -->
                        <div class="perf-dashboard__section">
                            <h4 class="perf-dashboard__section-title">
                                <i class="ph-bold ph-clock"></i> Phân tích thời gian
                            </h4>
                            <div class="perf-list">
                                <div class="perf-item">
                                    <div class="perf-item__label">
                                        <span>Vector hóa câu hỏi</span>
                                        <span class="perf-item__value">${embedding} ms</span>
                                    </div>
                                    <div class="perf-bar-track">
                                        <div class="perf-bar-fill bg-indigo" style="width: ${getPct(embedding)}%"></div>
                                    </div>
                                </div>
                                <div class="perf-item">
                                    <div class="perf-item__label">
                                        <span>Tìm kiếm Schema (Semantic)</span>
                                        <span class="perf-item__value">${schema} ms</span>
                                    </div>
                                    <div class="perf-bar-track">
                                        <div class="perf-bar-fill bg-blue" style="width: ${getPct(schema)}%"></div>
                                    </div>
                                </div>
                                <div class="perf-item">
                                    <div class="perf-item__label">
                                        <span>Lập kế hoạch (Planning)</span>
                                        <span class="perf-item__value">${planning} ms</span>
                                    </div>
                                    <div class="perf-bar-track">
                                        <div class="perf-bar-fill bg-amber" style="width: ${getPct(planning)}%"></div>
                                    </div>
                                </div>
                                <div class="perf-item">
                                    <div class="perf-item__label">
                                        <span>Sinh & Thực thi SQL (Execution)</span>
                                        <span class="perf-item__value">${execution} ms</span>
                                    </div>
                                    <div class="perf-bar-track">
                                        <div class="perf-bar-fill bg-emerald" style="width: ${getPct(execution)}%"></div>
                                    </div>
                                </div>
                                <div class="perf-item">
                                    <div class="perf-item__label">
                                        <span>Sinh phản hồi final (Synthesis)</span>
                                        <span class="perf-item__value">${generation} ms</span>
                                    </div>
                                    <div class="perf-bar-track">
                                        <div class="perf-bar-fill bg-teal-perf" style="width: ${getPct(generation)}%"></div>
                                    </div>
                                </div>
                                <div class="perf-item font-bold border-t border-gray-200/10 pt-2 mt-2">
                                    <div class="perf-item__label text-primary">
                                        <span>Tổng cộng (E2E Latency)</span>
                                        <span class="perf-item__value">${total} ms</span>
                                    </div>
                                </div>
                            </div>
                        </div>
                        
                        <!-- Phân tích Token -->
                        <div class="perf-dashboard__section">
                            <h4 class="perf-dashboard__section-title">
                                <i class="ph-bold ph-cpu"></i> Tokens tiêu thụ
                            </h4>
                            <div class="perf-list">
                                <div class="perf-item">
                                    <div class="perf-item__label">
                                        <span>Bước Lập kế hoạch (Planning)</span>
                                        <span class="perf-item__value text-xs opacity-70">${planningPrompt} p | ${planningCand} c</span>
                                    </div>
                                    <div class="perf-tokens-bar">
                                        <div class="perf-tokens-fill prompt" style="width: ${totalTokens > 0 ? (planningPrompt / totalTokens * 100) : 0}%" title="Prompt: ${planningPrompt}"></div>
                                        <div class="perf-tokens-fill candidate" style="width: ${totalTokens > 0 ? (planningCand / totalTokens * 100) : 0}%" title="Candidate: ${planningCand}"></div>
                                    </div>
                                </div>
                                <div class="perf-item">
                                    <div class="perf-item__label">
                                        <span>Bước Sinh SQL (SQL Gen)</span>
                                        <span class="perf-item__value text-xs opacity-70">${sqlPrompt} p | ${sqlCand} c</span>
                                    </div>
                                    <div class="perf-tokens-bar">
                                        <div class="perf-tokens-fill prompt" style="width: ${totalTokens > 0 ? (sqlPrompt / totalTokens * 100) : 0}%" title="Prompt: ${sqlPrompt}"></div>
                                        <div class="perf-tokens-fill candidate" style="width: ${totalTokens > 0 ? (sqlCand / totalTokens * 100) : 0}%" title="Candidate: ${sqlCand}"></div>
                                    </div>
                                </div>
                                <div class="perf-item">
                                    <div class="perf-item__label">
                                        <span>Bước Sinh câu trả lời (Synthesis)</span>
                                        <span class="perf-item__value text-xs opacity-70">${genPrompt} p | ${genCand} c</span>
                                    </div>
                                    <div class="perf-tokens-bar">
                                        <div class="perf-tokens-fill prompt" style="width: ${totalTokens > 0 ? (genPrompt / totalTokens * 100) : 0}%" title="Prompt: ${genPrompt}"></div>
                                        <div class="perf-tokens-fill candidate" style="width: ${totalTokens > 0 ? (genCand / totalTokens * 100) : 0}%" title="Candidate: ${genCand}"></div>
                                    </div>
                                </div>
                                
                                <div class="perf-legend mt-2">
                                    <span class="legend-item"><span class="legend-dot prompt"></span>Prompt</span>
                                    <span class="legend-item"><span class="legend-dot candidate"></span>Candidates</span>
                                </div>

                                <div class="perf-item font-bold border-t border-gray-200/10 pt-2 mt-2">
                                    <div class="perf-item__label text-secondary">
                                        <span>Tổng Tokens</span>
                                        <span class="perf-item__value">${totalTokens} (${totalPrompt}p | ${totalCand}c)</span>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    static createMessageElement(role, content, steps = [], suggestedQuestions = [], downloadUrl = null, downloadFileName = null, rawData = null, userFile = null, loadingStatus = null, duration = null, isAmbiguous = false, metadata = null) {
        const messageEl = document.createElement('div');
        messageEl.className = `message message--${role === 'user' ? 'user' : 'ai'} animate-slide-up`;
        
        if (content) {
            messageEl.setAttribute('data-markdown', content);
        }

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

        if (downloadUrl) {
            messageEl.setAttribute('data-download-url', downloadUrl);
        }

        if (downloadFileName) {
            messageEl.setAttribute('data-download-filename', downloadFileName);
        }

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
                <div class="perf-metric-container">
                    ${role === 'ai' ? this.renderPerformanceMetadata(metadata) : ''}
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
                        ${this._renderUnifiedExportButton(downloadUrl, downloadFileName, messageEl.getAttribute('data-raw'))}
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
        return messageEl;
    }

    static updateMessage(messageEl, content, steps = [], suggestedQuestions = [], downloadUrl = null, downloadFileName = null, rawData = null, userFile = null, loadingStatus = null, duration = null, isAmbiguous = false, metadata = null) {
        const contentEl = messageEl.querySelector('.markdown-content');
        const stepsContainer = messageEl.querySelector('.rag-steps-container');
        const footerActionsContainer = messageEl.querySelector('.footer-actions-container');
        const bubbleEl = messageEl.querySelector('.message__bubble');
        
        if (rawData) {
            try {
                const dataObj = typeof rawData === 'string' ? JSON.parse(rawData) : rawData;
                if (Array.isArray(dataObj)) {
                    console.log(`MessageRenderer: Received rawData for Excel export (${dataObj.length} rows)`);
                    messageEl.setAttribute('data-raw', JSON.stringify(dataObj));
                } else {
                    console.log("MessageRenderer: Received rawData for Excel export", dataObj);
                }
            } catch (e) {
                console.error("MessageRenderer: Failed to parse rawData", e);
            }
        }

        if (isAmbiguous) {
            messageEl.setAttribute('data-ambiguous', 'true');
        }

        if (downloadUrl) {
            messageEl.setAttribute('data-download-url', downloadUrl);
        }

        if (downloadFileName) {
            messageEl.setAttribute('data-download-filename', downloadFileName);
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
        
        const perfContainer = messageEl.querySelector('.perf-metric-container');
        if (perfContainer) {
            perfContainer.innerHTML = this.renderPerformanceMetadata(metadata);
        } else if (bubbleEl) {
            const perfHtml = `<div class="perf-metric-container">${this.renderPerformanceMetadata(metadata)}</div>`;
            const ragContainer = bubbleEl.querySelector('.rag-steps-container');
            if (ragContainer) {
                ragContainer.insertAdjacentHTML('afterend', perfHtml);
            } else if (contentEl) {
                contentEl.insertAdjacentHTML('afterend', perfHtml);
            }
        }

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
            actionsHtml += this._renderUnifiedExportButton(downloadUrl, downloadFileName || messageEl.getAttribute('data-download-filename'), messageEl.getAttribute('data-raw'));
            
            footerActionsContainer.innerHTML = actionsHtml;
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
    static _renderUnifiedExportButton(downloadUrl, downloadFileName, rawDataStr) {
        if (!downloadUrl && !rawDataStr) return '';

        // TRƯỜNG HỢP 1: Có mẫu từ server (Ưu tiên số 1)
        if (downloadUrl) {
            return `
                <button class="footer-download" data-action="download-template-excel" data-url="${downloadUrl}" data-filename="${downloadFileName || ''}" title="Tải báo cáo theo mẫu Excel">
                    <i class="ph-duotone ph-microsoft-excel-logo"></i> Xuất Excel
                </button>
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
