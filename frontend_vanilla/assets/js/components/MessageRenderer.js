/* MessageRenderer.js - Logic for rendering chat messages */

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
        return `<div class="terminal-code"><div class="terminal-code__header"><div class="terminal-code__dots"><div class="terminal-code__dot dot--red"></div><div class="terminal-code__dot dot--yellow"></div><div class="terminal-code__dot dot--green"></div></div><div class="terminal-code__lang">${lang}</div></div><div class="terminal-code__body"><pre><code>${content.trim()}</code></pre></div></div>`;
    }

    static renderRagSteps(steps) {
        if (!steps || steps.length === 0) return '';

        const getStepIcon = (title) => {
            const t = title.toLowerCase();
            if (t.includes('vector')) return 'ph-file-search';
            if (t.includes('schema')) return 'ph-database';
            if (t.includes('sql') || t.includes('execution')) return 'ph-code-block';
            if (t.includes('healing')) return 'ph-magic-wand';
            return 'ph-lightning';
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
                <button class="rag-steps__toggle" onclick="this.classList.toggle('active')">
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

        // 1. Xử lý các khối Markdown code blocks (có dấu ```) trước
        const markdownRegex = /```(?:json|sql|sqlserver)?\s*([\s\S]*?)```/gi;
        processedContent = processedContent.replace(markdownRegex, (match, code) => {
            const lang = code.trim().toUpperCase().startsWith('SELECT') ? 'SQL' : 'JSON';
            const html = this.renderCodeBlock(code.trim(), lang);
            const placeholder = `[[TERMINAL_MD_${placeholders.length}]]`;
            placeholders.push({ id: placeholder, html });
            return placeholder;
        });

        // 2. Xử lý các khối JSON thô (không có backticks)
        const jsonRegex = /((?:\[\s*{[\s\S]*?}\s*\])|(?:{[\s\S]*?}))/g;
        processedContent = processedContent.replace(jsonRegex, (match) => {
            if (match.startsWith('[[TERMINAL_')) return match;
            try {
                const obj = JSON.parse(match);
                const html = this.renderCodeBlock(JSON.stringify(obj, null, 2), 'JSON');
                const placeholder = `[[TERMINAL_JSON_${placeholders.length}]]`;
                placeholders.push({ id: placeholder, html });
                return placeholder;
            } catch (e) {
                return match;
            }
        });

        // 3. Xử lý các khối SQL thô (không có backticks)
        const sqlRegex = /(SELECT\s+[\s\S]*?)(?:$|\n\n|\r\n\r\n|(?=Kết quả JSON:)|(?=```))/gi;
        processedContent = processedContent.replace(sqlRegex, (match) => {
            if (match.startsWith('[[TERMINAL_')) return match;
            const html = this.renderCodeBlock(match.trim(), 'SQL');
            const placeholder = `[[TERMINAL_SQL_${placeholders.length}]]`;
            placeholders.push({ id: placeholder, html });
            return placeholder;
        });

        // 4. Render Markdown cho phần văn bản còn lại
        let finalHtml = this.renderContent(processedContent);

        // 5. Trả lại các khung Terminal vào vị trí ban đầu
        placeholders.forEach(p => {
            // Thay thế cả trường hợp bị bọc trong thẻ <p> hoặc <strong>
            const pWrapped = `<p>${p.id}</p>`;
            const bWrapped = `<strong>${p.id}</strong>`;
            
            if (finalHtml.includes(pWrapped)) {
                finalHtml = finalHtml.split(pWrapped).join(p.html);
            } else if (finalHtml.includes(bWrapped)) {
                finalHtml = finalHtml.split(bWrapped).join(p.html);
            } else {
                finalHtml = finalHtml.split(p.id).join(p.html);
            }
        });

        return finalHtml;
    }

    static createMessageElement(role, content, steps = [], suggestedQuestions = []) {
        const messageEl = document.createElement('div');
        messageEl.className = `message message--${role === 'user' ? 'user' : 'ai'} animate-slide-up`;
        
        let html = '';
        
        if (role === 'ai') {
            html += `<div class="ai-message-container">`;
        }

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
                    <button class="footer-copy" onclick="window.app.chatArea.copyMessage(this)">
                        <i class="ph-duotone ph-copy"></i> Copy
                    </button>
                </div>
            `;
        }

        html += `</div>`; // Close bubble

        if (role === 'user') {
            html += `
                <div class="message__actions">
                    <button class="action-btn" onclick="window.app.chatArea.editMessage(this)" title="Sửa tin nhắn">
                        <i class="ph-duotone ph-pencil-simple"></i>
                    </button>
                    <button class="action-btn" onclick="window.app.chatArea.copyMessage(this)" title="Copy tin nhắn">
                        <i class="ph-duotone ph-copy"></i>
                    </button>
                </div>
            `;
        } else {
            // Placeholder for suggestions
            html += `<div class="suggestions-list-container"></div>`;
            html += `</div>`; // Close ai-message-container
        }

        messageEl.innerHTML = html;

        if (role === 'ai' && suggestedQuestions && suggestedQuestions.length > 0) {
            this.renderSuggestions(messageEl, suggestedQuestions);
        }

        return messageEl;
    }

    static updateMessage(messageEl, content, steps = [], suggestedQuestions = []) {
        const contentEl = messageEl.querySelector('.markdown-content');
        const stepsContainer = messageEl.querySelector('.rag-steps-container');
        
        if (contentEl) {
            contentEl.innerHTML = this.renderContent(content);
        }
        
        if (stepsContainer && steps.length > 0) {
            stepsContainer.innerHTML = this.renderRagSteps(steps);
        }

        if (suggestedQuestions.length > 0) {
            this.renderSuggestions(messageEl, suggestedQuestions);
        }
    }

    static renderSuggestions(messageEl, suggestedQuestions) {
        let container = messageEl.querySelector('.suggestions-list-container');
        if (!container) return;

        container.innerHTML = '';
        const listDiv = document.createElement('div');
        listDiv.className = 'suggestions-list';
        listDiv.innerHTML = suggestedQuestions.map(q => `
            <button class="suggestion-btn" onclick="window.app.chatArea.sendQuickQuestion('${q}')">
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
