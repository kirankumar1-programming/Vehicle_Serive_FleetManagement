/**
 * AutoPro RAG Chatbot Client Engine
 */
document.addEventListener("DOMContentLoaded", function () {
    const launcher = document.getElementById("autopro-chat-launcher");
    const windowEl = document.getElementById("autopro-chat-window");
    const closeBtn = document.getElementById("autopro-chat-close");
    const minBtn = document.getElementById("autopro-chat-minimize");
    const clearBtn = document.getElementById("autopro-chat-clear");
    const inputEl = document.getElementById("autopro-chat-input");
    const sendBtn = document.getElementById("autopro-chat-send");
    const micBtn = document.getElementById("autopro-chat-mic");
    const messagesEl = document.getElementById("autopro-chat-messages");
    const typingEl = document.getElementById("autopro-chat-typing");
    const promptsBar = document.getElementById("autopro-chat-prompts");

    if (!launcher || !windowEl) return;

    // Session Management
    let sessionId = localStorage.getItem("autopro_rag_session_id");
    if (!sessionId) {
        sessionId = "sess_" + Math.random().toString(36).substring(2, 11) + "_" + Date.now();
        localStorage.setItem("autopro_rag_session_id", sessionId);
    }

    // Toggle Chat Window
    launcher.addEventListener("click", function () {
        const isHidden = windowEl.classList.contains("d-none");
        if (isHidden) {
            windowEl.classList.remove("d-none");
            inputEl.focus();
            scrollToBottom();
            loadPrompts();
        } else {
            windowEl.classList.add("d-none");
        }
    });

    closeBtn.addEventListener("click", function () {
        windowEl.classList.add("d-none");
    });

    minBtn.addEventListener("click", function () {
        windowEl.classList.add("d-none");
    });

    // Clear History
    clearBtn.addEventListener("click", async function () {
        if (!confirm("Clear your chat conversation history?")) return;
        try {
            await fetch("/Chatbot/ClearHistory", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ sessionId: sessionId })
            });
            sessionId = "sess_" + Math.random().toString(36).substring(2, 11) + "_" + Date.now();
            localStorage.setItem("autopro_rag_session_id", sessionId);
            messagesEl.innerHTML = `
                <div class="chat-bubble-container bot">
                    <div class="bot-icon-circle"><i class="bi bi-robot"></i></div>
                    <div class="chat-bubble bot">
                        <div class="bubble-text">
                            History cleared. How can I help you with our <strong>documents</strong> or <strong>database records</strong>?
                        </div>
                    </div>
                </div>
            `;
            loadPrompts();
        } catch (e) {
            console.error("Failed to clear chat history", e);
        }
    });

    // Send Message Handling
    async function sendMessage(text) {
        const msg = (text || inputEl.value || "").trim();
        if (!msg) return;

        inputEl.value = "";
        appendUserMessage(msg);
        showTyping(true);

        try {
            const resp = await fetch("/Chatbot/Query", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    message: msg,
                    sessionId: sessionId,
                    contextUrl: window.location.pathname
                })
            });

            if (!resp.ok) {
                throw new Error("Chat query failed with status " + resp.status);
            }

            const data = await resp.json();
            showTyping(false);
            appendBotResponse(data);
        } catch (err) {
            showTyping(false);
            appendBotError("Sorry, I encountered an issue connecting to the AI knowledge server. Please try again in a moment.");
            console.error("Chat error:", err);
        }
    }

    sendBtn.addEventListener("click", () => sendMessage());
    inputEl.addEventListener("keydown", function (e) {
        if (e.key === "Enter") {
            e.preventDefault();
            sendMessage();
        }
    });

    // Speech-to-Text Voice Recognition
    if ("webkitSpeechRecognition" in window || "SpeechRecognition" in window) {
        const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
        const recognition = new SpeechRecognition();
        recognition.continuous = false;
        recognition.interimResults = false;
        recognition.lang = "en-US";

        let isListening = false;

        micBtn.addEventListener("click", function () {
            if (isListening) {
                recognition.stop();
            } else {
                try {
                    recognition.start();
                    micBtn.classList.add("listening");
                    isListening = true;
                } catch (e) {
                    console.error("Speech recognition start failed", e);
                }
            }
        });

        recognition.onresult = function (event) {
            const transcript = event.results[0][0].transcript;
            inputEl.value = transcript;
            micBtn.classList.remove("listening");
            isListening = false;
            sendMessage(transcript);
        };

        recognition.onerror = function () {
            micBtn.classList.remove("listening");
            isListening = false;
        };

        recognition.onend = function () {
            micBtn.classList.remove("listening");
            isListening = false;
        };
    } else {
        micBtn.style.display = "none";
    }

    // Load Quick Suggestion Pills
    async function loadPrompts() {
        if (promptsBar.children.length > 0) return;
        try {
            const res = await fetch("/Chatbot/Suggestions");
            const prompts = await res.json();
            if (Array.isArray(prompts) && prompts.length > 0) {
                promptsBar.innerHTML = "";
                prompts.forEach(p => {
                    const pill = document.createElement("button");
                    pill.type = "button";
                    pill.className = "chat-prompt-pill";
                    pill.innerHTML = `<i class="bi ${p.icon || 'bi-arrow-right'} text-primary"></i> ${p.label}`;
                    pill.addEventListener("click", function () {
                        sendMessage(p.label);
                    });
                    promptsBar.appendChild(pill);
                });
            }
        } catch (e) {
            console.error("Failed to load prompt suggestions", e);
        }
    }

    function appendUserMessage(msg) {
        const container = document.createElement("div");
        container.className = "chat-bubble-container user";
        container.innerHTML = `
            <div class="chat-bubble user">
                <div class="bubble-text">${escapeHtml(msg)}</div>
            </div>
        `;
        messagesEl.appendChild(container);
        scrollToBottom();
    }

    function appendBotResponse(data) {
        const container = document.createElement("div");
        container.className = "chat-bubble-container bot";

        let html = `
            <div class="bot-icon-circle"><i class="bi bi-robot"></i></div>
            <div class="chat-bubble bot">
                <div class="bubble-text">${formatMarkdown(data.reply || '')}</div>
        `;

        // Entity Cards
        if (Array.isArray(data.entityCards) && data.entityCards.length > 0) {
            data.entityCards.forEach(card => {
                html += `
                    <div class="chat-entity-card">
                        <div class="d-flex justify-content-between align-items-start">
                            <div>
                                <div class="card-title">${escapeHtml(card.title)}</div>
                                <div class="card-subtitle">${escapeHtml(card.subtitle)}</div>
                            </div>
                            <span class="badge bg-${card.badgeColor || 'primary'}">${escapeHtml(card.badgeText)}</span>
                        </div>
                `;
                if (card.keyValues) {
                    for (const [k, v] of Object.entries(card.keyValues)) {
                        html += `
                            <div class="chat-entity-kv">
                                <span class="text-muted">${escapeHtml(k)}:</span>
                                <span class="fw-semibold">${escapeHtml(v)}</span>
                            </div>
                        `;
                    }
                }
                if (card.actionUrl) {
                    html += `
                        <div class="chat-entity-action">
                            <a href="${card.actionUrl}" class="btn btn-sm btn-outline-primary py-0 px-2" style="font-size: 0.75rem;">
                                ${escapeHtml(card.actionText || 'View Details')} &rarr;
                            </a>
                        </div>
                    `;
                }
                html += `</div>`;
            });
        }

        // Citations / Sources
        if (Array.isArray(data.sources) && data.sources.length > 0) {
            html += `<div class="citation-container">`;
            data.sources.forEach(src => {
                const isDoc = src.sourceType === "Document";
                const badgeClass = isDoc ? "doc" : "db";
                const icon = isDoc ? "bi-file-earmark-text" : "bi-database-check";
                html += `
                    <span class="citation-badge ${badgeClass}" title="${escapeHtml(src.snippet || '')}">
                        <i class="bi ${icon}"></i>
                        ${escapeHtml(src.title)} (${escapeHtml(src.section)})
                    </span>
                `;
            });
            html += `</div>`;
        }

        // Follow-up Suggestions
        if (Array.isArray(data.suggestedQuestions) && data.suggestedQuestions.length > 0) {
            html += `<div class="chat-followups">`;
            data.suggestedQuestions.forEach(q => {
                html += `<button type="button" class="followup-btn"><i class="bi bi-chat-dots me-1"></i> ${escapeHtml(q)}</button>`;
            });
            html += `</div>`;
        }

        html += `</div>`;
        container.innerHTML = html;

        // Add click events to follow-up questions
        container.querySelectorAll(".followup-btn").forEach(btn => {
            btn.addEventListener("click", function () {
                sendMessage(btn.innerText.trim());
            });
        });

        messagesEl.appendChild(container);
        scrollToBottom();
    }

    function appendBotError(errMsg) {
        const container = document.createElement("div");
        container.className = "chat-bubble-container bot";
        container.innerHTML = `
            <div class="bot-icon-circle"><i class="bi bi-exclamation-triangle text-danger"></i></div>
            <div class="chat-bubble bot border-danger">
                <div class="bubble-text text-danger">${escapeHtml(errMsg)}</div>
            </div>
        `;
        messagesEl.appendChild(container);
        scrollToBottom();
    }

    function showTyping(show) {
        if (show) {
            typingEl.classList.remove("d-none");
        } else {
            typingEl.classList.add("d-none");
        }
        scrollToBottom();
    }

    function scrollToBottom() {
        setTimeout(() => {
            messagesEl.scrollTop = messagesEl.scrollHeight;
        }, 50);
    }

    function escapeHtml(text) {
        const div = document.createElement("div");
        div.textContent = text || "";
        return div.innerHTML;
    }

    function formatMarkdown(md) {
        if (!md) return "";
        let html = escapeHtml(md);

        // Bold **text**
        html = html.replace(/\*\*(.*?)\*\*/g, "<strong>$1</strong>");

        // Italic *text*
        html = html.replace(/\*(.*?)\*/g, "<em>$1</em>");

        // Headers ###
        html = html.replace(/^### (.*$)/gim, "<h6>$1</h6>");
        html = html.replace(/^#### (.*$)/gim, "<strong class='d-block mt-2 mb-1'>$1</strong>");

        // Bullet lists
        html = html.replace(/^\- (.*$)/gim, "<li>$1</li>");
        html = html.replace(/(<li>.*<\/li>)/s, "<ul>$1</ul>");

        // Newlines to <br> if not inside tags
        html = html.replace(/\n\n/g, "<p class='mb-2'></p>");
        html = html.replace(/\n/g, "<br>");

        return html;
    }
});
