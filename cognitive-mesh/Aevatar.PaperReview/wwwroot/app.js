// ═══════════════════════════════════════════════════════════════
//  PAPER REVIEW - FRONTEND APP
//  学术论文评审平台前端
// ═══════════════════════════════════════════════════════════════

// ─────────────────────────────────────────────────────────────
//  STATE
// ─────────────────────────────────────────────────────────────

let sessions = [];
let currentSessionId = null;
let eventSource = null;
let uploadId = null;

// ─────────────────────────────────────────────────────────────
//  INITIALIZATION
// ─────────────────────────────────────────────────────────────

document.addEventListener('DOMContentLoaded', () => {
    loadSessions();
    setupDragAndDrop();
});

async function loadSessions() {
    try {
        const res = await fetch('/api/sessions');
        sessions = await res.json();
        renderSessionList();
        document.getElementById('session-count').textContent = sessions.length;
    } catch (err) {
        console.error('Failed to load sessions:', err);
    }
}

// ─────────────────────────────────────────────────────────────
//  SESSION LIST
// ─────────────────────────────────────────────────────────────

function renderSessionList() {
    const nav = document.getElementById('session-nav');
    
    if (sessions.length === 0) {
        nav.innerHTML = '<div class="nav-empty">No review sessions yet</div>';
        return;
    }

    nav.innerHTML = sessions.map(s => `
        <div class="nav-item ${s.id === currentSessionId ? 'active' : ''}" 
             onclick="selectSession('${s.id}')">
            <div class="nav-item-title">
                <span class="nav-item-status ${s.status.toLowerCase()}"></span>
                ${escapeHtml(s.title || 'Untitled')}
            </div>
            <div class="nav-item-meta">
                <span>${s.type}</span>
                <span>${formatDate(s.createdAt)}</span>
            </div>
        </div>
    `).join('');
}

function selectSession(sessionId) {
    currentSessionId = sessionId;
    const session = sessions.find(s => s.id === sessionId);
    
    if (!session) return;

    // Update UI
    document.getElementById('empty-state').classList.add('hidden');
    document.getElementById('dashboard').classList.remove('hidden');
    
    // Update header
    document.getElementById('paper-title').textContent = session.title || 'Untitled Paper';
    document.getElementById('paper-authors').textContent = session.authors || 'Unknown';
    document.getElementById('review-type').textContent = session.type;
    document.getElementById('venue-type').textContent = session.venueType;
    
    // Update status
    updateStatus(session);
    
    // Re-render session list to show active state
    renderSessionList();
    
    // Connect to event stream if reviewing
    if (session.status === 'Reviewing') {
        connectEventStream(sessionId);
    }
    
    // Load result if completed
    if (session.status === 'Completed') {
        loadResult(sessionId);
    }
}

function updateStatus(session) {
    const statusChip = document.getElementById('chip-status');
    statusChip.textContent = session.status.toUpperCase();
    statusChip.className = 'status-chip ' + session.status.toLowerCase();
    
    document.getElementById('chip-phase').textContent = session.currentPhase || '-';
    document.getElementById('chip-progress').textContent = session.progressPercent + '%';
    document.getElementById('chip-calls').textContent = session.totalLlmCalls + ' calls';
    document.getElementById('chip-tokens').textContent = formatNumber(session.totalTokens) + ' tokens';
    
    // Update progress bar
    document.getElementById('progress-bar').style.width = session.progressPercent + '%';
    
    // Update button state
    const btn = document.getElementById('btn-start');
    if (session.status === 'Reviewing') {
        btn.innerHTML = '<span>⏹</span> STOP';
        btn.onclick = stopReview;
    } else if (session.status === 'Completed' || session.status === 'Failed') {
        btn.innerHTML = '<span>🔄</span> RE-REVIEW';
        btn.onclick = startReview;
    } else {
        btn.innerHTML = '<span>⚡</span> START REVIEW';
        btn.onclick = startReview;
    }
}

// ─────────────────────────────────────────────────────────────
//  CREATE SESSION MODAL
// ─────────────────────────────────────────────────────────────

function showNewSessionModal() {
    document.getElementById('modal-overlay').classList.remove('hidden');
    // Reset form
    document.getElementById('new-title').value = '';
    document.getElementById('new-authors').value = '';
    document.getElementById('paper-content').value = '';
    uploadId = null;
    document.getElementById('uploaded-file').classList.add('hidden');
}

function hideNewSessionModal() {
    document.getElementById('modal-overlay').classList.add('hidden');
}

function switchContentTab(tab) {
    document.querySelectorAll('.content-tabs .tab-btn').forEach(btn => {
        btn.classList.toggle('active', btn.dataset.tab === tab);
    });
    document.querySelectorAll('.tab-content').forEach(el => {
        el.classList.toggle('active', el.id === 'tab-' + tab);
    });
}

async function createSession() {
    const title = document.getElementById('new-title').value.trim();
    const authors = document.getElementById('new-authors').value.trim();
    const reviewType = document.getElementById('new-review-type').value;
    const venueType = document.getElementById('new-venue-type').value;
    const paperContent = document.getElementById('paper-content').value.trim();

    if (!title) {
        alert('Please enter paper title');
        return;
    }

    if (!paperContent && !uploadId) {
        alert('Please provide paper content');
        return;
    }

    try {
        const res = await fetch('/api/sessions', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                title,
                authors,
                reviewType,
                venueType,
                uploadId,
                paperContent: paperContent || null
            })
        });

        const result = await res.json();
        
        if (result.success) {
            hideNewSessionModal();
            await loadSessions();
            selectSession(result.sessionId);
            // Auto-start review
            startReview();
        } else {
            alert('Failed to create session: ' + result.error);
        }
    } catch (err) {
        console.error('Create session error:', err);
        alert('Failed to create session');
    }
}

// ─────────────────────────────────────────────────────────────
//  FILE UPLOAD
// ─────────────────────────────────────────────────────────────

function setupDragAndDrop() {
    const zone = document.getElementById('upload-zone');
    if (!zone) return;

    zone.addEventListener('dragover', e => {
        e.preventDefault();
        zone.style.borderColor = 'var(--accent)';
        zone.style.background = 'var(--accent-glow)';
    });

    zone.addEventListener('dragleave', e => {
        e.preventDefault();
        zone.style.borderColor = '';
        zone.style.background = '';
    });

    zone.addEventListener('drop', e => {
        e.preventDefault();
        zone.style.borderColor = '';
        zone.style.background = '';
        
        const files = e.dataTransfer.files;
        if (files.length > 0) {
            uploadFile(files[0]);
        }
    });
}

function handleFileUpload(e) {
    const file = e.target.files[0];
    if (file) {
        uploadFile(file);
    }
}

async function uploadFile(file) {
    const formData = new FormData();
    formData.append('file', file);

    try {
        const res = await fetch('/api/upload', {
            method: 'POST',
            body: formData
        });

        const result = await res.json();
        
        if (result.success) {
            uploadId = result.uploadId;
            const uploadedEl = document.getElementById('uploaded-file');
            uploadedEl.innerHTML = `
                <span class="uploaded-file-icon">✓</span>
                <span class="uploaded-file-name">${escapeHtml(result.fileName)}</span>
                <span class="uploaded-file-size">${formatBytes(result.size)}</span>
            `;
            uploadedEl.classList.remove('hidden');
        } else {
            alert('Upload failed: ' + result.error);
        }
    } catch (err) {
        console.error('Upload error:', err);
        alert('Upload failed');
    }
}

// ─────────────────────────────────────────────────────────────
//  REVIEW EXECUTION
// ─────────────────────────────────────────────────────────────

async function startReview() {
    if (!currentSessionId) return;

    try {
        const res = await fetch(`/api/sessions/${currentSessionId}/review`, {
            method: 'POST'
        });

        const result = await res.json();
        
        if (result.success) {
            // Update local state
            const session = sessions.find(s => s.id === currentSessionId);
            if (session) {
                session.status = 'Reviewing';
                updateStatus(session);
                renderSessionList();
            }
            
            // Clear previous results
            document.getElementById('result-content').innerHTML = 
                '<div class="result-placeholder">Reviewing in progress...</div>';
            document.getElementById('timeline-list').innerHTML = '';
            
            // Connect to event stream
            connectEventStream(currentSessionId);
        } else {
            alert('Failed to start review: ' + result.error);
        }
    } catch (err) {
        console.error('Start review error:', err);
        alert('Failed to start review');
    }
}

async function stopReview() {
    if (!currentSessionId) return;

    try {
        const res = await fetch(`/api/sessions/${currentSessionId}/stop`, {
            method: 'POST'
        });

        const result = await res.json();
        
        if (result.success) {
            disconnectEventStream();
            await loadSessions();
            selectSession(currentSessionId);
        }
    } catch (err) {
        console.error('Stop review error:', err);
    }
}

// ─────────────────────────────────────────────────────────────
//  EVENT STREAM (SSE)
// ─────────────────────────────────────────────────────────────

function connectEventStream(sessionId) {
    disconnectEventStream();
    
    eventSource = new EventSource(`/api/sessions/${sessionId}/events`);
    
    eventSource.onmessage = (e) => {
        const event = JSON.parse(e.data);
        handleEvent(event);
    };
    
    eventSource.onerror = () => {
        disconnectEventStream();
        // Reload to get final state
        loadSessions().then(() => {
            if (currentSessionId) {
                selectSession(currentSessionId);
            }
        });
    };
}

function disconnectEventStream() {
    if (eventSource) {
        eventSource.close();
        eventSource = null;
    }
}

function handleEvent(event) {
    const session = sessions.find(s => s.id === currentSessionId);
    
    switch (event.type) {
        case 'ProgressEvent':
            if (session) {
                session.progressPercent = event.progressPercent;
                session.currentPhase = event.phase;
                updateStatus(session);
            }
            addTimelineEntry(event.phase, event.message);
            break;
            
        case 'ConsensusEvent':
            addTimelineEntry('Consensus', 
                `Round ${event.round}: ${event.leaderVotes}/${event.totalVotes} votes (need ${event.votesNeeded})`);
            break;
            
        case 'ReviewerEvent':
            addTimelineEntry(event.reviewerRole, 
                `${event.reviewerId}: ${event.content.substring(0, 100)}...`);
            break;
            
        case 'ResultEvent':
            if (session) {
                session.status = event.success ? 'Completed' : 'Failed';
                session.totalLlmCalls = event.totalLlmCalls;
                session.totalTokens = event.totalTokens;
                updateStatus(session);
                renderSessionList();
            }
            if (event.content) {
                renderResult(event.content);
            }
            break;
            
        case 'ErrorEvent':
            addTimelineEntry('ERROR', event.message);
            if (session) {
                session.status = 'Failed';
                updateStatus(session);
                renderSessionList();
            }
            break;
    }
}

function addTimelineEntry(phase, message) {
    const list = document.getElementById('timeline-list');
    const li = document.createElement('li');
    li.className = 'timeline-item';
    li.innerHTML = `
        <div class="timeline-phase">${escapeHtml(phase)}</div>
        <div class="timeline-message">${escapeHtml(message || '')}</div>
        <div class="timeline-time">${new Date().toLocaleTimeString()}</div>
    `;
    list.insertBefore(li, list.firstChild);
}

// ─────────────────────────────────────────────────────────────
//  RESULT
// ─────────────────────────────────────────────────────────────

async function loadResult(sessionId) {
    try {
        const res = await fetch(`/api/sessions/${sessionId}/result`);
        const result = await res.json();
        
        if (result.content) {
            renderResult(result.content);
        }
    } catch (err) {
        console.error('Failed to load result:', err);
    }
}

function renderResult(content) {
    const el = document.getElementById('result-content');
    el.innerHTML = marked.parse(content);
}

function copyResult() {
    const content = document.getElementById('result-content').innerText;
    navigator.clipboard.writeText(content).then(() => {
        alert('Copied to clipboard!');
    });
}

// ─────────────────────────────────────────────────────────────
//  UTILITIES
// ─────────────────────────────────────────────────────────────

function escapeHtml(str) {
    if (!str) return '';
    return str
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

function formatDate(dateStr) {
    const d = new Date(dateStr);
    return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

function formatNumber(n) {
    if (n >= 1000000) return (n / 1000000).toFixed(1) + 'M';
    if (n >= 1000) return (n / 1000).toFixed(1) + 'K';
    return n.toString();
}

function formatBytes(bytes) {
    if (bytes >= 1048576) return (bytes / 1048576).toFixed(1) + ' MB';
    if (bytes >= 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return bytes + ' B';
}
