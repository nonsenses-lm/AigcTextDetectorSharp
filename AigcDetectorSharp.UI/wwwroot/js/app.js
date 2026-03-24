// Detect mode: desktop (Photino) or server (HTTP)
let isDesktop = false;

function checkDesktopMode() {
    try {
        return typeof window.external !== 'undefined' && 
               typeof window.external.sendMessage === 'function';
    } catch (e) {
        return false;
    }
}

// API adapter
const api = {
    async send(message) {
        if (isDesktop) {
            if (typeof window.external !== 'undefined' && typeof window.external.sendMessage === 'function') {
                window.external.sendMessage(JSON.stringify(message));
            } else {
                handleResponse({ action: 'error', message: 'Photino not initialized' });
            }
        } else {
            try {
                let response;
                if (message.action === 'detect') {
                    response = await fetch('/api/detect', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ text: message.text, model: message.model })
                    });
                } else if (message.action === 'readFile') {
                    response = await fetch('/api/readFile', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ path: message.path })
                    });
                }

                if (response) {
                    const data = await response.json();
                    if (response.ok) {
                        if (message.action === 'detect') {
                            handleResponse({ action: 'result', ...data });
                        } else if (message.action === 'readFile') {
                            handleResponse({ action: 'fileContent', ...data });
                        }
                    } else {
                        handleResponse({ action: 'error', message: data.error });
                    }
                }
            } catch (err) {
                handleResponse({ action: 'error', message: err.message });
            }
        }
    },

    onReceive(callback) {
        if (isDesktop) {
            if (typeof window.external !== 'undefined' && typeof window.external.receiveMessage === 'function') {
                window.external.receiveMessage(json => {
                    callback(JSON.parse(json));
                });
            }
        }
    }
};

// State
let currentModel = 'zh';
let history = JSON.parse(localStorage.getItem('aigc-history') || '[]');

// Response handler
function handleResponse(data) {
    switch (data.action) {
        case 'result':
            showLoading(false);
            renderResult(data);
            addToHistory(data);
            break;
        case 'fileContent':
            document.getElementById('input-text').value = data.text;
            break;
        case 'error':
            showLoading(false);
            showToast(data.message);
            break;
    }
}

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    // Wait a bit for Photino to inject window.external
    setTimeout(() => {
        isDesktop = checkDesktopMode();
        
        initTheme();
        initDragDrop();
        renderHistory();

        if (isDesktop) {
            api.onReceive(handleResponse);
        }

        console.log(`AIGC Detector UI - ${isDesktop ? 'Desktop' : 'Server'} mode`);
    }, 100);
});

// Theme
function initTheme() {
    const saved = localStorage.getItem('aigc-theme');
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    const theme = saved || (prefersDark ? 'dark' : 'light');
    setTheme(theme);
}

function setTheme(theme) {
    if (theme === 'dark') {
        document.body.classList.add('dark');
        document.getElementById('sun-icon').style.display = 'none';
        document.getElementById('moon-icon').style.display = 'block';
    } else {
        document.body.classList.remove('dark');
        document.getElementById('sun-icon').style.display = 'block';
        document.getElementById('moon-icon').style.display = 'none';
    }
}

function toggleTheme() {
    const isDark = document.body.classList.contains('dark');
    const theme = isDark ? 'light' : 'dark';
    setTheme(theme);
    localStorage.setItem('aigc-theme', theme);
}

// Model switch
function switchModel(model) {
    currentModel = model;
    document.querySelectorAll('.model-tab').forEach(tab => {
        tab.classList.toggle('active', tab.dataset.model === model);
    });
}

// Drag & Drop
function initDragDrop() {
    const textarea = document.getElementById('input-text');
    const dropZone = document.getElementById('drop-zone');

    ['dragenter', 'dragover'].forEach(event => {
        textarea.addEventListener(event, (e) => {
            e.preventDefault();
            dropZone.classList.add('show');
        });
    });

    ['dragleave', 'drop'].forEach(event => {
        dropZone.addEventListener(event, (e) => {
            e.preventDefault();
            dropZone.classList.remove('show');
        });
    });

    dropZone.addEventListener('drop', (e) => {
        const file = e.dataTransfer.files[0];
        if (file) {
            document.getElementById('result-section').classList.remove('show');
            if (isDesktop) {
                api.send({ action: 'readFile', path: file.path || file.name });
            } else {
                uploadFile(file);
            }
        }
    });
}

function handleFile(input) {
    const file = input.files[0];
    if (file) {
        document.getElementById('result-section').classList.remove('show');
        if (isDesktop) {
            api.send({ action: 'readFile', path: file.path || file.name });
        } else {
            uploadFile(file);
        }
        input.value = '';
    }
}

// Upload file in server mode
async function uploadFile(file) {
    document.getElementById('loading').classList.add('show');
    document.getElementById('result-section').classList.remove('show');

    const formData = new FormData();
    formData.append('file', file);

    try {
        const response = await fetch('/api/upload', {
            method: 'POST',
            body: formData
        });

        const data = await response.json();
        document.getElementById('loading').classList.remove('show');
        if (response.ok) {
            document.getElementById('input-text').value = data.text;
        } else {
            showToast(data.error || '文件上传失败');
        }
    } catch (err) {
        document.getElementById('loading').classList.remove('show');
        showToast('文件上传失败: ' + err.message);
    }
}

// Detect
function detect() {
    const text = document.getElementById('input-text').value.trim();
    if (!text) return;

    showLoading(true);
    api.send({ action: 'detect', text, model: currentModel });
}

function clearInput() {
    document.getElementById('input-text').value = '';
    document.getElementById('result-section').classList.remove('show');
}

// UI Updates
function showLoading(show) {
    document.getElementById('loading').classList.toggle('show', show);
    document.getElementById('result-section').classList.toggle('show', !show);
    if (show) {
        document.getElementById('progress-ring').setAttribute('stroke-dasharray', '0, 264');
    }
}

function renderResult(data) {
    const section = document.getElementById('result-section');
    section.classList.add('show');

    // Probability
    const prob = (data.probability * 100).toFixed(1);
    document.getElementById('probability').textContent = prob + '%';

    // Progress ring
    const circumference = 2 * Math.PI * 42;
    const dashArray = (data.probability * circumference).toFixed(0) + ', ' + circumference;
    const ring = document.getElementById('progress-ring');
    ring.setAttribute('stroke-dasharray', dashArray);
    ring.classList.remove('human', 'ai');
    ring.classList.add(data.label === 'Human' ? 'human' : 'ai');

    // Result badge
    const badge = document.getElementById('result-badge');
    badge.textContent = data.label === 'Human' ? '人类撰写' : 'AI 生成';
    badge.className = 'result-badge ' + (data.label === 'Human' ? 'human' : 'ai');

    // Description
    const desc = document.getElementById('result-desc');
    if (data.probability >= 0.8) {
        desc.textContent = data.label === 'Human' ? '高置信度判定为人类撰写' : '高置信度判定为 AI 生成';
    } else if (data.probability >= 0.6) {
        desc.textContent = data.label === 'Human' ? '较可能为人类撰写' : '较可能为 AI 生成';
    } else {
        desc.textContent = '结果不确定，建议结合上下文判断';
    }

    // Meta
    document.getElementById('model-badge').textContent = (data.model || currentModel).toUpperCase();
    document.getElementById('timestamp').textContent = data.timestamp;

    // Chunks
    const container = document.getElementById('chunks-container');
    if (data.chunks.length > 1) {
        container.innerHTML = data.chunks.map((chunk, idx) => {
            const labelClass = chunk.label === 'Human' ? 'human' : 'ai';
            const prob = (chunk.probability * 100).toFixed(1);
            return `
                <div class="chunk ${labelClass}">
                    <div class="chunk-header" onclick="this.parentElement.classList.toggle('open')">
                        <span>Chunk ${chunk.index}</span>
                        <div>
                            <span style="font-size:12px;color:#737373;margin-right:8px">${prob}%</span>
                            <span class="chunk-badge ${labelClass}">${chunk.label}</span>
                        </div>
                    </div>
                    <div class="chunk-content">${escapeHtml(chunk.text)}</div>
                </div>
            `;
        }).join('');
        container.style.display = 'block';
        container.previousElementSibling.style.display = 'block';
    } else {
        container.style.display = 'none';
        container.previousElementSibling.style.display = 'none';
    }
}

// History
function addToHistory(data) {
    const item = {
        id: Date.now(),
        text: data.chunks[0]?.text.slice(0, 60) || '',
        label: data.label,
        probability: data.probability,
        model: data.model || currentModel,
        timestamp: data.timestamp
    };

    history.unshift(item);
    if (history.length > 50) history.pop();
    localStorage.setItem('aigc-history', JSON.stringify(history));
    renderHistory();
}

function renderHistory() {
    const body = document.getElementById('history-body');
    const empty = document.getElementById('history-empty');

    if (history.length === 0) {
        body.innerHTML = '';
        empty.style.display = 'block';
        return;
    }

    empty.style.display = 'none';
    body.innerHTML = history.map(item => `
        <tr>
            <td style="font-size:12px;color:#737373;white-space:nowrap">${item.timestamp.split(' ')[1] || item.timestamp}</td>
            <td style="max-width:200px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${escapeHtml(item.text)}</td>
            <td><span class="badge">${(item.model || 'ZH').toUpperCase()}</span></td>
            <td><span class="status-dot ${item.label === 'Human' ? 'human' : 'ai'}"></span>${item.label === 'Human' ? 'Human' : 'AI'}</td>
            <td style="text-align:right;font-variant-numeric:tabular-nums">${(item.probability * 100).toFixed(1)}%</td>
        </tr>
    `).join('');
}

function clearHistory() {
    history = [];
    localStorage.removeItem('aigc-history');
    renderHistory();
}

// Utilities
function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function showToast(message) {
    const toast = document.getElementById('toast');
    toast.textContent = message;
    toast.classList.add('show');
    setTimeout(() => toast.classList.remove('show'), 3000);
}

// Exit application
async function exitApp() {
    if (!confirm('确定要退出程序吗？')) return;
    
    try {
        await fetch('/api/exit', { method: 'POST' });
    } catch (e) {
        // Server may have already shut down
    }
    
    // Show message and close window after a delay
    document.body.innerHTML = `
        <div style="display:flex;align-items:center;justify-content:center;height:100vh;flex-direction:column;">
            <h2>程序已退出</h2>
            <p style="color:#737373;margin-top:8px;">窗口将自动关闭...</p>
        </div>
    `;
    
    setTimeout(() => {
        window.close();
    }, 1000);
}
