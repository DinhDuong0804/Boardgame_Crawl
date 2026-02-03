// Translation functions for the new C# backend

let currentTranslationResult = null;

/**
 * Upload PDF and Translate
 */
async function uploadPdfAndTranslate() {
    const fileInput = document.getElementById('pdf-file');
    const gameName = document.getElementById('upload-game-name').value.trim();
    const bggId = document.getElementById('upload-bgg-id').value.trim();

    // Validate
    if (!fileInput.files || fileInput.files.length === 0) {
        showToast('Vui lòng chọn file PDF', 'error');
        return;
    }

    const file = fileInput.files[0];

    // Check file size (50MB)
    if (file.size > 52428800) {
        showToast('File quá lớn! Tối đa 50MB', 'error');
        return;
    }

    // Check file type
    if (!file.name.toLowerCase().endsWith('.pdf')) {
        showToast('Chỉ hỗ trợ file PDF', 'error');
        return;
    }

    // Show progress
    showTranslationProgress();
    updateTranslationStatus('Đang upload PDF...');

    try {
        // Create FormData
        const formData = new FormData();
        formData.append('file', file);
        if (gameName) formData.append('gameName', gameName);
        if (bggId) formData.append('bggId', bggId);

        // Upload
        const response = await fetch('/api/translation/upload', {
            method: 'POST',
            body: formData
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Upload failed');
        }

        const result = await response.json();

        // Hide progress, show result
        hideTranslationProgress();
        showTranslationResult(result);
        showToast('Dịch thành công! 🎉', 'success');

        // Clear form
        fileInput.value = '';
        document.getElementById('upload-game-name').value = '';
        document.getElementById('upload-bgg-id').value = '';

    } catch (error) {
        hideTranslationProgress();
        showToast(`Lỗi: ${error.message}`, 'error');
        console.error('Upload error:', error);
    }
}

/**
 * Download from BGG and Translate
 */
async function downloadFromBggAndTranslate() {
    const url = document.getElementById('bgg-url').value.trim();
    const gameName = document.getElementById('bgg-game-name').value.trim();
    const bggId = document.getElementById('bgg-game-id').value.trim();
    const rulebookTitle = document.getElementById('bgg-rulebook-title').value.trim();

    // Validate
    if (!url) {
        showToast('Vui lòng nhập BGG URL', 'error');
        return;
    }

    if (!gameName) {
        showToast('Vui lòng nhập tên game', 'error');
        return;
    }

    // Show progress
    showTranslationProgress();
    updateTranslationStatus('Đang tải PDF từ BGG...');

    try {
        // Prepare request
        const requestBody = {
            url: url,
            gameName: gameName,
            bggId: bggId ? parseInt(bggId) : null,
            rulebookTitle: rulebookTitle || null
        };

        updateTranslationStatus('Đang download PDF...');

        // Call API
        const response = await fetch('/api/translation/translate-from-bgg', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(requestBody)
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Download failed');
        }

        updateTranslationStatus('Đang dịch sang tiếng Việt...');

        const result = await response.json();

        // Hide progress, show result
        hideTranslationProgress();
        showTranslationResult(result);
        showToast('Dịch từ BGG thành công! 🎉', 'success');

        // Clear form
        document.getElementById('bgg-url').value = '';
        document.getElementById('bgg-game-name').value = '';
        document.getElementById('bgg-game-id').value = '';
        document.getElementById('bgg-rulebook-title').value = '';

    } catch (error) {
        hideTranslationProgress();
        showToast(`Lỗi: ${error.message}`, 'error');
        console.error('BGG download error:', error);
    }
}

/**
 * Show translation progress card
 */
function showTranslationProgress() {
    const card = document.getElementById('translation-progress-card');
    card.style.display = 'block';
    card.scrollIntoView({ behavior: 'smooth', block: 'center' });

    // Hide result card if visible
    document.getElementById('translation-result-card').style.display = 'none';
}

/**
 * Hide translation progress card
 */
function hideTranslationProgress() {
    document.getElementById('translation-progress-card').style.display = 'none';
}

/**
 * Update translation status text
 */
function updateTranslationStatus(message) {
    const statusText = document.getElementById('translation-status-text');
    statusText.textContent = message;

    // Add to log
    const log = document.getElementById('translation-log');
    const timestamp = new Date().toLocaleTimeString('vi-VN');
    log.innerHTML += `<div class="log-entry info">[${timestamp}] ${message}</div>`;
    log.scrollTop = log.scrollHeight;
}

/**
 * Show translation result
 */
function showTranslationResult(result) {
    currentTranslationResult = result;

    const card = document.getElementById('translation-result-card');

    // Update result stats
    document.getElementById('result-filename').textContent = result.fileName || '--';
    document.getElementById('result-word-count').textContent = result.extractedWordCount || 0;
    document.getElementById('result-processing-time').textContent =
        `${Math.round(result.processingTimeSeconds)}s`;
    document.getElementById('result-output-path').textContent =
        result.outputFilePath || '--';

    // Show previews
    const originalText = result.originalText || '';
    const originalPreview = originalText.length > 400
        ? originalText.substring(0, 400) + '...'
        : originalText;
    document.getElementById('result-preview-original').textContent = originalPreview;

    const previewText = result.vietnameseText || '';
    const preview = previewText.length > 400
        ? previewText.substring(0, 400) + '...'
        : previewText;
    document.getElementById('result-preview-text').textContent = preview;

    // Show card
    card.style.display = 'block';
    card.scrollIntoView({ behavior: 'smooth', block: 'center' });
}

/**
 * Hide translation result
 */
function hideTranslationResult() {
    document.getElementById('translation-result-card').style.display = 'none';
    currentTranslationResult = null;
}

/**
 * View full translation in modal
 */
function viewFullTranslation() {
    if (!currentTranslationResult) {
        showToast('Không có bản dịch để hiển thị', 'error');
        return;
    }

    // Create modal for full markdown view
    const modalBody = document.getElementById('modal-body');
    const modalTitle = document.getElementById('modal-title');

    modalTitle.textContent = `Bản Dịch: ${currentTranslationResult.fileName}`;

    // Convert markdown to HTML or show as text
    modalBody.innerHTML = `
        <div style="max-height: 70vh; overflow-y: auto; padding: var(--space-4); background: var(--bg-secondary); border-radius: var(--radius-md);">
            <pre style="white-space: pre-wrap; word-wrap: break-word; font-family: 'Courier New', monospace; font-size: 0.9rem;">
${escapeHtml(currentTranslationResult.bilingualMarkdown || currentTranslationResult.vietnameseText)}
            </pre>
        </div>
    `;

    // Show modal
    document.getElementById('game-modal').classList.add('active');
}

/**
 * Download markdown file
 */
function downloadMarkdown() {
    if (!currentTranslationResult) {
        showToast('Không có bản dịch để tải', 'error');
        return;
    }

    const markdown = currentTranslationResult.bilingualMarkdown || currentTranslationResult.vietnameseText;
    const filename = currentTranslationResult.fileName.replace('.pdf', '_translated.md');

    // Create blob and download
    const blob = new Blob([markdown], { type: 'text/markdown;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);

    showToast('Đang tải markdown...', 'success');
}

/**
 * Escape HTML for safe display
 */
function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

/**
 * Show toast notification (reuse existing function from app.js if available)
 */
function showToast(message, type = 'info') {
    // If app.js has showToast, it will be used
    // Otherwise, simple console.log
    if (typeof window.showToast === 'function') {
        window.showToast(message, type);
    } else {
        console.log(`[${type.toUpperCase()}] ${message}`);
        alert(message);
    }
}
