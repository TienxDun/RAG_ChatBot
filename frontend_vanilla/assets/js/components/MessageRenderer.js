/* MessageRenderer.js - Logic for rendering chat messages */

export class MessageRenderer {
    static renderContent(content) {
        if (typeof marked !== 'undefined') {
            return marked.parse(content);
        }
        return content.replace(/\n/g, '<br>');
    }

    static renderRagSteps(steps) {
        if (!steps || steps.length === 0) return '';

        const stepsHtml = steps.map((step, idx) => `
            <div class="rag-step animate-fade-in" style="animation-delay: ${idx * 0.1}s">
                <div class="rag-step__dot"></div>
                <div class="rag-step__panel">
                    <div class="rag-step__title">
                        <i class="ph ph-lightning"></i>
                        ${step.title}
                    </div>
                    <div class="rag-step__content-inner text-sm opacity-80">
                        ${this.renderContent(step.content)}
                    </div>
                </div>
            </div>
        `).join('');

        return `
            <div class="rag-steps">
                <button class="rag-steps__toggle" onclick="this.classList.toggle('active')">
                    <i class="ph ph-lightning-fill"></i>
                    <span>RAG TRACE (${steps.length} steps)</span>
                    <i class="ph ph-caret-down"></i>
                </button>
                <div class="rag-steps__content">
                    ${stepsHtml}
                </div>
            </div>
        `;
    }

    static createMessageElement(role, content, steps = [], suggestedQuestions = []) {
        const messageEl = document.createElement('div');
        messageEl.className = `message message--${role === 'user' ? 'user' : 'ai'} animate-slide-up`;
        
        let html = `
            <div class="message__bubble">
                <div class="markdown-content">
                    ${this.renderContent(content)}
                </div>
        `;

        if (role === 'ai') {
            if (steps && steps.length > 0) {
                html += this.renderRagSteps(steps);
            }
            
            html += `
                <div class="message__footer">
                    <span class="ai-label">AI INSIGHT</span>
                    <div style="flex: 1"></div>
                    <button class="footer-copy" onclick="window.app.chatArea.copyMessage(this)">
                        <i class="ph ph-copy"></i> Copy
                    </button>
                </div>
            `;
        }

        html += `</div>`; // Close bubble

        if (role === 'user') {
            html += `
                <div class="message__actions">
                    <button class="action-btn" onclick="window.app.chatArea.editMessage(this)" title="Sửa tin nhắn">
                        <i class="ph ph-pencil-simple"></i>
                    </button>
                    <button class="action-btn" onclick="window.app.chatArea.copyMessage(this)" title="Copy tin nhắn">
                        <i class="ph ph-copy"></i>
                    </button>
                </div>
            `;
        }

        messageEl.innerHTML = html;

        if (role === 'ai' && suggestedQuestions && suggestedQuestions.length > 0) {
            const suggestionsDiv = document.createElement('div');
            suggestionsDiv.className = 'suggestions-list';
            suggestionsDiv.innerHTML = suggestedQuestions.map(q => `
                <button class="suggestion-btn" onclick="window.app.chatArea.sendQuickQuestion('${q}')">
                    ${q}
                </button>
            `).join('');
            
            const bubble = messageEl.querySelector('.message__bubble');
            const container = document.createElement('div');
            container.style.display = 'flex';
            container.style.flexDirection = 'column';
            container.style.alignItems = 'flex-start';
            container.style.width = '100%';
            container.appendChild(bubble);
            container.appendChild(suggestionsDiv);
            
            messageEl.innerHTML = '';
            messageEl.appendChild(container);
        }

        return messageEl;
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
