// Game selection and rulebooks management

let selectedGame = null;
let allGames = [];
let currentRulebooks = [];

/**
 * Load games from database for translation
 */
async function loadGamesForTranslation() {
    const search = document.getElementById('game-search').value.trim();
    const select = document.getElementById('game-select');
    const countText = document.getElementById('game-count');

    try {
        countText.textContent = 'Đang tải...';

        const url = search
            ? `/api/translation/games?pageSize=100&search=${encodeURIComponent(search)}`
            : '/api/translation/games?pageSize=100';

        const response = await fetch(url);
        if (!response.ok) throw new Error('Failed to load games');

        const data = await response.json();
        allGames = data.games || [];

        // Clear and populate select
        select.innerHTML = '<option value="">-- Chọn một game --</option>';

        allGames.forEach((game, index) => {
            const option = document.createElement('option');
            option.value = index;
            option.textContent = `${game.name} (${game.yearPublished || '?'}) ${game.bggRank ? `[Rank #${game.bggRank}]` : ''}`;
            select.appendChild(option);
        });

        countText.textContent = `Tìm thấy ${allGames.length} games`;

        if (allGames.length === 0) {
            countText.textContent = 'Không tìm thấy game nào';
        }

    } catch (error) {
        console.error('Error loading games:', error);
        countText.textContent = 'Lỗi khi tải danh sách games';
        showToast('Không thể tải danh sách games', 'error');
    }
}

/**
 * Handle game selection from dropdown
 */
async function onGameSelected() {
    const select = document.getElementById('game-select');
    const index = parseInt(select.value);

    if (isNaN(index) || index < 0 || index >= allGames.length) {
        selectedGame = null;
        hideSelectedGameInfo();
        hideRulebooksList();
        disableTranslateButtons();
        return;
    }

    selectedGame = allGames[index];
    showSelectedGameInfo(selectedGame);

    // Auto-fill BGG fields
    autofillBggFields(selectedGame);

    // Load rulebooks from database
    await loadRulebooksForGame(selectedGame.bggId);

    // Enable translate button
    enableTranslateButtons();
}

/**
 * Load rulebooks from database for selected game
 */
async function loadRulebooksForGame(bggId) {
    const container = document.getElementById('rulebooks-container');
    const card = document.getElementById('rulebooks-list-card');
    const badge = document.getElementById('rulebooks-count-badge');
    const noRulebooksMsg = document.getElementById('no-rulebooks-message');

    try {
        // Show loading
        container.innerHTML = '<div style="text-align: center; padding: var(--space-4);">⏳ Đang tải danh sách rulebooks...</div>';
        card.style.display = 'block';
        noRulebooksMsg.style.display = 'none';

        const response = await fetch(`/api/translation/game/${bggId}/rulebooks`);
        if (!response.ok) throw new Error('Failed to load rulebooks');

        const data = await response.json();
        const rulebooks = data.rulebooks || [];

        badge.textContent = rulebooks.length;

        if (rulebooks.length === 0) {
            container.innerHTML = '';
            noRulebooksMsg.style.display = 'block';
            return;
        }

        // Render rulebooks list
        container.innerHTML = rulebooks.map((rb, idx) => `
            <div class="rulebook-item" 
                style="padding: var(--space-3); background: var(--bg-secondary); border-radius: var(--radius-sm); cursor: pointer; transition: all 0.2s;" 
                onclick="selectRulebook(${idx})"
                onmouseover="this.style.background='var(--bg-tertiary)'"
                onmouseout="this.style.background='var(--bg-secondary)'">
                <div style="display: flex; justify-content: space-between; align-items: start; gap: var(--space-3);">
                    <div style="flex: 1;">
                        <h4 style="margin: 0 0 var(--space-2) 0; font-size: 1rem;">${escapeHtmlText(rb.title)}</h4>
                        <div style="font-size: 0.85rem; color: var(--text-muted); display: flex; gap: var(--space-3); flex-wrap: wrap;">
                            ${rb.language ? `<span>🌐 ${rb.language}</span>` : ''}
                            ${rb.fileType ? `<span>📄 ${rb.fileType.toUpperCase()}</span>` : ''}
                            ${rb.status ? `<span>🏷️ ${rb.status}</span>` : ''}
                            ${rb.createdAt ? `<span>📅 ${formatDate(rb.createdAt)}</span>` : ''}
                        </div>
                    </div>
                    <button class="btn btn-sm btn-primary" onclick="event.stopPropagation(); translateRulebook(${idx})" style="flex-shrink: 0;">
                        <span>🔗</span> Dịch
                    </button>
                </div>
            </div>
        `).join('');

        // Store rulebooks for later use
        currentRulebooks = rulebooks;

    } catch (error) {
        console.error('Error loading rulebooks:', error);
        container.innerHTML = '<div style="text-align: center; padding: var(--space-4); color: var(--color-danger);">❌ Lỗi khi tải danh sách rulebooks</div>';
    }
}

/**
 * Scrape rulebooks from BGG for currently selected game
 */
async function scrapeRulebooksFromBgg() {
    if (!selectedGame) {
        showToast('Vui lòng chọn một game trước', 'warning');
        return;
    }

    const btn = document.getElementById('scrape-bgg-btn');
    const bggId = selectedGame.bggId;

    try {
        btn.disabled = true;
        btn.innerHTML = '<span>⏳</span> Đang cào...';
        showToast(`Đang quét rulebooks trên BGG cho ${selectedGame.name}...`, 'info');

        const response = await fetch(`/api/translation/game/${bggId}/scrape-rulebooks`, {
            method: 'POST'
        });

        const contentType = response.headers.get("content-type");
        if (!contentType || !contentType.includes("application/json")) {
            const text = await response.text();
            console.error('Non-JSON response:', text);
            throw new Error(`Server trả về kết quả không hợp lệ (HTTP ${response.status})`);
        }

        const result = await response.json();

        if (response.ok) {
            if (result.saved > 0) {
                showToast(`Thành công! Tìm thấy và lưu ${result.saved} rulebooks mới.`, 'success');
            } else if (result.found > 0) {
                showToast(`Đã quét xong. Không có rulebook mới (tìm thấy ${result.found} cái đã có trong DB).`, 'info');
            } else {
                showToast('Không tìm thấy rulebook nào trên BGG cho game này.', 'warning');
            }

            // Reload list
            await loadRulebooksForGame(bggId);
        } else {
            throw new Error(result.error || 'Lỗi khi cào dữ liệu');
        }
    } catch (error) {
        console.error('Scraping error:', error);
        showToast(`Lỗi: ${error.message}`, 'error');
    } finally {
        btn.disabled = false;
        btn.innerHTML = '<span>🔍</span> Cào từ BGG';
    }
}

/**
 * Hide rulebooks list
 */
function hideRulebooksList() {
    const card = document.getElementById('rulebooks-list-card');
    if (card) card.style.display = 'none';
    currentRulebooks = [];
}

/**
 * Select a rulebook (highlight it)
 */
function selectRulebook(index) {
    // Visual feedback - could add highlight CSS
    console.log('Selected rulebook:', currentRulebooks[index]);
}

/**
 * Translate a specific rulebook
 */
async function translateRulebook(index) {
    if (!selectedGame || !currentRulebooks[index]) {
        showToast('Vui lòng chọn game và rulebook', 'error');
        return;
    }

    const rulebook = currentRulebooks[index];

    // Confirm
    if (!confirm(`Dịch rulebook "${rulebook.title}"?\n\nQuá trình này có thể mất 1-3 phút.`)) {
        return;
    }

    // Show progress
    showTranslationProgress();
    updateTranslationStatus('Đang chuẩn bị tải PDF...');

    try {
        const requestBody = {
            url: rulebook.url,
            gameName: selectedGame.name,
            bggId: selectedGame.bggId,
            rulebookTitle: rulebook.title
        };

        updateTranslationStatus('Đang download PDF từ BGG...');

        const response = await fetch('/api/translation/translate-from-bgg', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(requestBody)
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Translation failed');
        }

        updateTranslationStatus('Đang dịch sang tiếng Việt...');

        const result = await response.json();

        // Hide progress, show result
        hideTranslationProgress();
        showTranslationResult(result);
        showToast('Dịch thành công! 🎉', 'success');

    } catch (error) {
        hideTranslationProgress();
        showToast(`Lỗi: ${error.message}`, 'error');
        console.error('Translation error:', error);
    }
}

/**
 * Show selected game information
 */
function showSelectedGameInfo(game) {
    const infoDiv = document.getElementById('selected-game-info');

    document.getElementById('selected-game-name').textContent = game.name;
    document.getElementById('selected-game-bgg-id').textContent = game.bggId;
    document.getElementById('selected-game-year').textContent = game.yearPublished || '-';
    document.getElementById('selected-game-rank').textContent = game.bggRank || '-';

    const img = document.getElementById('selected-game-image');
    if (game.imageUrl) {
        img.src = game.imageUrl;
    } else {
        img.src = 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" width="80" height="80"><rect fill="#ddd" width="80" height="80"/><text x="50%" y="50%" text-anchor="middle" dy=".3em" fill="#999" font-size="24">🎲</text></svg>';
    }

    infoDiv.style.display = 'block';
}

/**
 * Hide selected game info
 */
function hideSelectedGameInfo() {
    document.getElementById('selected-game-info').style.display = 'none';
}

/**
 * Auto-fill BGG fields from selected game
 */
function autofillBggFields(game) {
    document.getElementById('bgg-game-name').value = game.name;
    document.getElementById('bgg-game-id').value = game.bggId;

    // Also fill upload fields
    document.getElementById('upload-game-name').value = game.name;
    document.getElementById('upload-bgg-id').value = game.bggId;
}

/**
 * Enable translate buttons
 */
function enableTranslateButtons() {
    const btn = document.getElementById('translate-from-bgg-btn');
    if (btn) {
        btn.disabled = false;
    }
}

/**
 * Disable translate buttons
 */
function disableTranslateButtons() {
    const btn = document.getElementById('translate-from-bgg-btn');
    if (btn) {
        btn.disabled = true;
    }
}

/**
 * Format file size
 */
function formatFileSize(bytes) {
    if (!bytes) return '-';
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / 1048576).toFixed(1) + ' MB';
}

/**
 * Format date
 */
function formatDate(dateString) {
    if (!dateString) return '-';
    const date = new Date(dateString);
    return date.toLocaleDateString('vi-VN', { year: 'numeric', month: '2-digit', day: '2-digit' });
}

/**
 * Escape HTML for safe display
 */
function escapeHtmlText(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

/**
 * Initialize on page load
 */
document.addEventListener('DOMContentLoaded', () => {
    // Auto-load first 100 games when translation tab is opened
    const translationTab = document.querySelector('[data-tab="translation"]');
    if (translationTab) {
        translationTab.addEventListener('click', () => {
            // Load games if not loaded yet
            if (allGames.length === 0) {
                setTimeout(() => {
                    loadGamesForTranslation();
                }, 100);
            }
        });
    }
});
