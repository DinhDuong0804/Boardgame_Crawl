/**
 * Board Game Cafe - Admin Dashboard
 * Frontend JavaScript Application
 */

// ================================
// Configuration
// ================================
const API_BASE_URL = window.location.origin;  // Use same origin

// ================================
// State Management
// ================================
const state = {
    games: [],
    stats: {},
    currentPage: 0,
    pageSize: 20,
    totalGames: 0,
    isLoading: false,
    rulebookFiles: []
};

// ================================
// DOM Ready
// ================================
document.addEventListener('DOMContentLoaded', () => {
    initializeApp();
});

async function initializeApp() {
    setupTabNavigation();
    setupEventListeners();
    await checkApiConnection();
    await loadDashboardData();
}

// ================================
// Tab Navigation
// ================================
function setupTabNavigation() {
    const tabButtons = document.querySelectorAll('.tab-btn');

    tabButtons.forEach(btn => {
        btn.addEventListener('click', () => {
            const tabName = btn.dataset.tab;
            switchTab(tabName);
        });
    });
}

function switchTab(tabName) {
    // Update button states
    document.querySelectorAll('.tab-btn').forEach(btn => {
        btn.classList.toggle('active', btn.dataset.tab === tabName);
    });

    // Update content visibility
    document.querySelectorAll('.tab-content').forEach(content => {
        content.classList.toggle('active', content.id === `${tabName}-tab`);
    });

    // Load data based on tab
    switch (tabName) {
        case 'dashboard':
            loadDashboardData();
            break;
        case 'games':
            loadGames();
            break;
        case 'translation':
            refreshRulebookList();
            break;
    }
}

// ================================
// Event Listeners
// ================================
function setupEventListeners() {
    // Refresh button
    document.getElementById('refresh-btn').addEventListener('click', () => {
        const activeTab = document.querySelector('.tab-content.active').id.replace('-tab', '');
        switchTab(activeTab);
        showToast('Đã làm mới dữ liệu!', 'success');
    });
}

// ================================
// API Connection Check
// ================================
async function checkApiConnection() {
    const statusEl = document.getElementById('connection-status');
    const dot = statusEl.querySelector('.status-dot');
    const text = statusEl.querySelector('.status-text');

    try {
        const response = await fetch(`${API_BASE_URL}/health`);
        if (response.ok) {
            dot.classList.add('connected');
            dot.classList.remove('error');
            text.textContent = 'Đã kết nối';
        } else {
            throw new Error('API không phản hồi');
        }
    } catch (error) {
        dot.classList.add('error');
        dot.classList.remove('connected');
        text.textContent = 'Mất kết nối';
        showToast('Không thể kết nối đến API server!', 'error');
    }
}

// ================================
// Dashboard
// ================================
async function loadDashboardData() {
    showLoading();

    try {
        // Load statistics
        const statsResponse = await fetch(`${API_BASE_URL}/api/games/stats`);
        if (statsResponse.ok) {
            const stats = await statsResponse.json();
            updateStats(stats);
        }

        // Load recent games for activity
        const gamesResponse = await fetch(`${API_BASE_URL}/api/games?take=5`);
        if (gamesResponse.ok) {
            const games = await gamesResponse.json();
            updateActivityList(games);
        }
    } catch (error) {
        console.error('Error loading dashboard:', error);
        showToast('Lỗi khi tải dữ liệu dashboard', 'error');
    }

    hideLoading();
}

function updateStats(stats) {
    document.getElementById('stat-total').textContent = animateNumber(stats.totalGames || 0);
    document.getElementById('stat-active').textContent = animateNumber(stats.activeGames || 0);
    document.getElementById('stat-translated').textContent = animateNumber(stats.translatedGames || 0);
    document.getElementById('stat-rulebooks').textContent = animateNumber(stats.totalRulebooks || 0);

    // Animate numbers
    animateStatsNumbers(stats);
}

function animateStatsNumbers(stats) {
    const counters = [
        { el: 'stat-total', target: stats.totalGames || 0 },
        { el: 'stat-active', target: stats.activeGames || 0 },
        { el: 'stat-translated', target: stats.translatedGames || 0 },
        { el: 'stat-rulebooks', target: stats.totalRulebooks || 0 }
    ];

    counters.forEach(counter => {
        const element = document.getElementById(counter.el);
        animateValue(element, 0, counter.target, 1000);
    });
}

function animateValue(element, start, end, duration) {
    const range = end - start;
    const increment = range / (duration / 16);
    let current = start;

    const timer = setInterval(() => {
        current += increment;
        if ((increment > 0 && current >= end) || (increment < 0 && current <= end)) {
            clearInterval(timer);
            current = end;
        }
        element.textContent = Math.floor(current);
    }, 16);
}

function updateActivityList(games) {
    const list = document.getElementById('activity-list');

    if (!games || games.length === 0) {
        list.innerHTML = `
            <li class="activity-item">
                <span class="activity-icon">📝</span>
                <span class="activity-text">Chưa có hoạt động nào</span>
            </li>
        `;
        return;
    }

    list.innerHTML = games.map(game => {
        const icon = getStatusIcon(game.status);
        return `
            <li class="activity-item">
                <span class="activity-icon">${icon}</span>
                <span class="activity-text">
                    <strong>${game.name}</strong> - ${getStatusText(game.status)}
                </span>
            </li>
        `;
    }).join('');
}

function getStatusIcon(status) {
    const icons = {
        'scraped': '🔍',
        'active': '✅',
        'inactive': '⏸️',
        'pending_translation': '🌐'
    };
    return icons[status] || '📝';
}

function getStatusText(status) {
    const texts = {
        'scraped': 'Đã cào dữ liệu',
        'active': 'Đang hoạt động',
        'inactive': 'Không hoạt động',
        'pending_translation': 'Đang dịch'
    };
    return texts[status] || status;
}

// ================================
// Scraper Functions
// ================================
async function scrapeRanked() {
    const maxPages = parseInt(document.getElementById('maxPages').value) || 1;
    const batchSize = parseInt(document.getElementById('batchSize').value) || 20;

    showLoading('Đang cào dữ liệu từ BGG...');
    showScraperProgress();
    addLog('info', `Bắt đầu cào ${maxPages} trang với batch size ${batchSize}...`);

    try {
        const response = await fetch(
            `${API_BASE_URL}/api/scraper/scrape-rank?maxPages=${maxPages}&batchSize=${batchSize}`,
            { method: 'POST' }
        );

        const result = await response.json();

        if (response.ok) {
            addLog('success', `Thành công! Đã xử lý ${result.gamesProcessed} games.`);
            showToast(`Đã cào thành công ${result.gamesProcessed} games!`, 'success');
            updateProgress(100);
        } else {
            addLog('error', `Lỗi: ${result.message || 'Không xác định'}`);
            showToast('Có lỗi xảy ra khi cào dữ liệu', 'error');
        }
    } catch (error) {
        addLog('error', `Lỗi kết nối: ${error.message}`);
        showToast('Không thể kết nối đến server', 'error');
    }

    hideLoading();
}

async function scrapeSingleGame() {
    const bggId = document.getElementById('bggId').value;

    if (!bggId) {
        showToast('Vui lòng nhập BGG ID', 'warning');
        return;
    }

    showLoading(`Đang cào game BGG ID: ${bggId}...`);

    try {
        const response = await fetch(
            `${API_BASE_URL}/api/scraper/scrape/${bggId}`,
            { method: 'POST' }
        );

        const result = await response.json();

        if (response.ok) {
            showToast(`Đã cào thành công: ${result.name} (${result.rulebooksFound} rulebooks)`, 'success');
        } else {
            showToast(result.message || 'Không tìm thấy game', 'error');
        }
    } catch (error) {
        showToast('Không thể kết nối đến server', 'error');
    }

    hideLoading();
}

async function scrapeRulebooks() {
    const gameId = document.getElementById('gameIdRulebook').value;

    if (!gameId) {
        showToast('Vui lòng nhập Game ID', 'warning');
        return;
    }

    showLoading(`Đang cào rulebooks cho Game ID: ${gameId}...`);

    try {
        const response = await fetch(
            `${API_BASE_URL}/api/scraper/${gameId}/scrape-rulebooks`,
            { method: 'POST' }
        );

        const result = await response.json();

        if (response.ok) {
            showToast(`Đã tìm thấy ${result.rulebooksFound} rulebooks!`, 'success');
        } else {
            showToast('Không tìm thấy game hoặc rulebooks', 'error');
        }
    } catch (error) {
        showToast('Không thể kết nối đến server', 'error');
    }

    hideLoading();
}

function showScraperProgress() {
    const progressEl = document.getElementById('scraper-progress');
    progressEl.style.display = 'block';
    updateProgress(0);
}

function updateProgress(percent) {
    document.getElementById('scraper-progress-bar').style.width = `${percent}%`;
    document.getElementById('scraper-progress-text').textContent = `${percent}%`;
}

function addLog(type, message) {
    const logsEl = document.getElementById('scraper-logs');
    const time = new Date().toLocaleTimeString();
    logsEl.innerHTML += `
        <div class="log-entry ${type}">
            [${time}] ${message}
        </div>
    `;
    logsEl.scrollTop = logsEl.scrollHeight;
}

// ================================
// Games Management
// ================================
async function loadGames() {
    const tbody = document.getElementById('games-tbody');
    tbody.innerHTML = `
        <tr>
            <td colspan="11" class="loading-row">
                <div class="spinner-small"></div>
                <span>Đang tải danh sách games...</span>
            </td>
        </tr>
    `;

    const status = document.getElementById('filter-status').value;
    const players = document.getElementById('filter-players').value;
    const playtime = document.getElementById('filter-playtime').value;

    let url = `${API_BASE_URL}/api/games?skip=${state.currentPage * state.pageSize}&take=${state.pageSize}`;
    if (status) url += `&status=${status}`;
    if (players) url += `&minPlayers=${players}&maxPlayers=${players}`;
    if (playtime) url += `&maxPlaytime=${playtime}`;

    try {
        const response = await fetch(url);
        const games = await response.json();

        state.games = games;
        renderGamesTable(games);
    } catch (error) {
        console.error('Error loading games:', error);
        tbody.innerHTML = `
            <tr>
                <td colspan="11" class="loading-row text-error">
                    Lỗi khi tải dữ liệu. Vui lòng thử lại.
                </td>
            </tr>
        `;
    }
}

function renderGamesTable(games) {
    const tbody = document.getElementById('games-tbody');

    if (!games || games.length === 0) {
        tbody.innerHTML = `
            <tr>
                <td colspan="11" class="loading-row">
                    Không tìm thấy games nào
                </td>
            </tr>
        `;
        return;
    }

    tbody.innerHTML = games.map(game => `
        <tr>
            <td>${game.id}</td>
            <td>
                <img src="${game.thumbnailUrl || 'https://via.placeholder.com/50'}" 
                     alt="${game.name}" 
                     class="game-image"
                     onerror="this.src='https://via.placeholder.com/50?text=No+Image'">
            </td>
            <td>
                <div class="game-name" title="${game.name}">${game.name}</div>
                ${game.nameVi ? `<small style="color: var(--text-muted)">${game.nameVi}</small>` : ''}
            </td>
            <td>${game.yearPublished || '-'}</td>
            <td>${game.minPlayers}-${game.maxPlayers}</td>
            <td>${game.minPlaytime || '-'}-${game.maxPlaytime || '-'} phút</td>
            <td>${game.avgRating ? game.avgRating.toFixed(1) : '-'}</td>
            <td>${game.bggRank || '-'}</td>
            <td>
                <span class="status-badge ${game.status}">
                    ${getStatusText(game.status)}
                </span>
            </td>
            <td>
                <span class="translation-badge ${game.hasTranslation ? 'yes' : 'no'}">
                    ${game.hasTranslation ? '✓ Có' : '✗ Chưa'}
                </span>
            </td>
            <td class="action-cell">
                <button class="btn btn-sm btn-outline" onclick="viewGameDetail(${game.id})">
                    👁️
                </button>
                ${game.status !== 'active' ?
            `<button class="btn btn-sm btn-success" onclick="activateGame(${game.id})">
                        ✓
                    </button>` :
            `<button class="btn btn-sm btn-danger" onclick="deactivateGame(${game.id})">
                        ✗
                    </button>`
        }
                <button class="btn btn-sm btn-accent" onclick="translateGame(${game.id})">
                    🌐
                </button>
            </td>
        </tr>
    `).join('');

    renderPagination();
}

function renderPagination() {
    const pagination = document.getElementById('games-pagination');
    const totalPages = Math.ceil(state.totalGames / state.pageSize) || 5;

    let html = `
        <button onclick="goToPage(${state.currentPage - 1})" ${state.currentPage === 0 ? 'disabled' : ''}>
            ← Trước
        </button>
    `;

    for (let i = 0; i < Math.min(totalPages, 5); i++) {
        html += `
            <button onclick="goToPage(${i})" class="${state.currentPage === i ? 'active' : ''}">
                ${i + 1}
            </button>
        `;
    }

    html += `
        <button onclick="goToPage(${state.currentPage + 1})" ${state.currentPage >= totalPages - 1 ? 'disabled' : ''}>
            Sau →
        </button>
    `;

    pagination.innerHTML = html;
}

function goToPage(page) {
    if (page < 0) return;
    state.currentPage = page;
    loadGames();
}

function resetFilters() {
    document.getElementById('filter-status').value = '';
    document.getElementById('filter-players').value = '';
    document.getElementById('filter-playtime').value = '';
    state.currentPage = 0;
    loadGames();
}

async function viewGameDetail(gameId) {
    showLoading();

    try {
        const response = await fetch(`${API_BASE_URL}/api/games/${gameId}`);
        const game = await response.json();

        showGameModal(game);
    } catch (error) {
        showToast('Không thể tải thông tin game', 'error');
    }

    hideLoading();
}

function showGameModal(game) {
    const modal = document.getElementById('game-modal');
    const title = document.getElementById('modal-title');
    const body = document.getElementById('modal-body');

    title.textContent = game.name;

    body.innerHTML = `
        <div class="game-preview">
            <img src="${game.imageUrl || game.thumbnailUrl || 'https://via.placeholder.com/150'}" 
                 alt="${game.name}" 
                 class="game-preview-image">
            <div class="game-preview-info">
                <h4>${game.name}</h4>
                ${game.nameVi ? `<p style="color: var(--primary-400)">${game.nameVi}</p>` : ''}
                <div class="meta">
                    <span>📅 ${game.yearPublished || 'N/A'}</span>
                    <span>👥 ${game.minPlayers}-${game.maxPlayers} người</span>
                    <span>⏱️ ${game.minPlaytime}-${game.maxPlaytime} phút</span>
                    <span>⭐ ${game.avgRating ? game.avgRating.toFixed(1) : 'N/A'}</span>
                    <span>🏆 #${game.bggRank || 'N/A'}</span>
                </div>
            </div>
        </div>
        
        <div style="margin-top: var(--space-5);">
            <h4>Mô tả</h4>
            <p style="max-height: 150px; overflow-y: auto; font-size: 0.875rem; line-height: 1.6;">
                ${game.description || 'Chưa có mô tả'}
            </p>
            
            ${game.descriptionVi ? `
                <h4 style="margin-top: var(--space-4); color: var(--primary-400);">Mô tả (Tiếng Việt)</h4>
                <p style="max-height: 150px; overflow-y: auto; font-size: 0.875rem; line-height: 1.6;">
                    ${game.descriptionVi}
                </p>
            ` : ''}
        </div>
        
        <div style="margin-top: var(--space-5);">
            <h4>Thể loại</h4>
            <div style="display: flex; flex-wrap: wrap; gap: var(--space-2); margin-top: var(--space-2);">
                ${(game.categories || []).map(cat =>
        `<span class="badge" style="background: var(--primary-700)">${cat}</span>`
    ).join('') || '<span class="text-muted">Chưa có</span>'}
            </div>
        </div>
        
        <div style="margin-top: var(--space-4);">
            <h4>Mechanics</h4>
            <div style="display: flex; flex-wrap: wrap; gap: var(--space-2); margin-top: var(--space-2);">
                ${(game.mechanics || []).map(mech =>
        `<span class="badge" style="background: var(--accent-500)">${mech}</span>`
    ).join('') || '<span class="text-muted">Chưa có</span>'}
            </div>
        </div>
        
        ${game.rulebooks && game.rulebooks.length > 0 ? `
            <div style="margin-top: var(--space-5);">
                <h4>📚 Rulebooks (${game.rulebooks.length})</h4>
                <ul style="list-style: none; margin-top: var(--space-2);">
                    ${game.rulebooks.map(rb => `
                        <li style="padding: var(--space-2) 0; border-bottom: 1px solid var(--border-light);">
                            <span>${rb.title}</span>
                            <span class="status-badge ${rb.status}" style="margin-left: var(--space-2);">
                                ${rb.status}
                            </span>
                            ${rb.hasVietnamese ?
            '<span class="translation-badge yes">✓ Đã dịch</span>' :
            '<span class="translation-badge no">✗ Chưa dịch</span>'
        }
                        </li>
                    `).join('')}
                </ul>
            </div>
        ` : ''}
    `;

    modal.classList.add('active');
}

function closeModal() {
    document.getElementById('game-modal').classList.remove('active');
}

async function activateGame(gameId) {
    showLoading();

    try {
        const response = await fetch(`${API_BASE_URL}/api/games/${gameId}/activate`, {
            method: 'POST'
        });

        if (response.ok) {
            showToast('Đã kích hoạt game thành công!', 'success');
            loadGames();
        } else {
            showToast('Không thể kích hoạt game', 'error');
        }
    } catch (error) {
        showToast('Lỗi kết nối', 'error');
    }

    hideLoading();
}

async function deactivateGame(gameId) {
    if (!confirm('Bạn có chắc muốn tắt game này?')) return;

    showLoading();

    try {
        const response = await fetch(`${API_BASE_URL}/api/games/${gameId}/deactivate`, {
            method: 'POST'
        });

        if (response.ok) {
            showToast('Đã tắt game thành công!', 'success');
            loadGames();
        } else {
            showToast('Không thể tắt game', 'error');
        }
    } catch (error) {
        showToast('Lỗi kết nối', 'error');
    }

    hideLoading();
}

async function translateGame(gameId) {
    // Switch to translation tab first
    switchTab('translation');

    // Load games if not loaded yet
    if (typeof allGames === 'undefined' || allGames.length === 0) {
        await loadGamesForTranslation();
    }

    // Try to find and select the game in dropdown
    try {
        const response = await fetch(`${API_BASE_URL}/api/games/${gameId}`);
        if (response.ok) {
            const game = await response.json();

            // Auto-fill fields
            document.getElementById('bgg-game-name').value = game.name;
            document.getElementById('bgg-game-id').value = game.bggId;
            document.getElementById('upload-game-name').value = game.name;
            document.getElementById('upload-bgg-id').value = game.bggId;

            // Try to select game in dropdown
            const select = document.getElementById('game-select');
            if (select) {
                // Search for game by bggId
                for (let i = 0; i < allGames.length; i++) {
                    if (allGames[i].bggId === game.bggId) {
                        select.value = i;
                        onGameSelected();
                        break;
                    }
                }
            }

            // Also load rulebooks
            if (game.bggId) {
                await loadRulebooksForGame(game.bggId);
            }

            showToast(`Đã chọn game: ${game.name}`, 'success');
        }
    } catch (error) {
        console.error('Error loading game for translation:', error);
        // Fallback: just fill the old input if exists
        const oldInput = document.getElementById('translate-game-id');
        if (oldInput) {
            oldInput.value = gameId;
            loadGamePreview();
        }
    }
}

// ================================
// Translation Functions
// ================================
async function loadGamePreview() {
    const gameId = document.getElementById('translate-game-id').value;
    const container = document.getElementById('game-preview-container');

    if (!gameId) {
        container.innerHTML = `
            <div class="empty-state">
                <span class="empty-icon">🎮</span>
                <p>Nhập Game ID và nhấn "Tải Game" để xem thông tin</p>
            </div>
        `;
        return;
    }

    container.innerHTML = `
        <div class="empty-state">
            <div class="spinner-small"></div>
            <p>Đang tải...</p>
        </div>
    `;

    try {
        const response = await fetch(`${API_BASE_URL}/api/games/${gameId}`);

        if (!response.ok) {
            throw new Error('Game không tồn tại');
        }

        const game = await response.json();

        container.innerHTML = `
            <div class="game-preview">
                <img src="${game.thumbnailUrl || 'https://via.placeholder.com/120'}" 
                     alt="${game.name}" 
                     class="game-preview-image"
                     style="width: 120px; height: 120px;">
                <div class="game-preview-info">
                    <h4>${game.name}</h4>
                    <div class="meta">
                        <span>📅 ${game.yearPublished || 'N/A'}</span>
                        <span>👥 ${game.minPlayers}-${game.maxPlayers}</span>
                        <span>⭐ ${game.avgRating ? game.avgRating.toFixed(1) : 'N/A'}</span>
                    </div>
                    <div style="margin-top: var(--space-2);">
                        <span class="status-badge ${game.status}">${getStatusText(game.status)}</span>
                        <span class="translation-badge ${game.hasTranslation ? 'yes' : 'no'}">
                            ${game.hasTranslation ? '✓ Đã dịch' : '✗ Chưa dịch'}
                        </span>
                    </div>
                    <div class="description" style="margin-top: var(--space-3);">
                        ${(game.description || '').substring(0, 200)}...
                    </div>
                </div>
            </div>
        `;
    } catch (error) {
        container.innerHTML = `
            <div class="empty-state text-error">
                <span class="empty-icon">❌</span>
                <p>${error.message || 'Không tìm thấy game'}</p>
            </div>
        `;
    }
}

async function requestTranslation() {
    const gameId = document.getElementById('translate-game-id').value;
    const includeRulebooks = document.getElementById('include-rulebooks').checked;

    if (!gameId) {
        showToast('Vui lòng nhập Game ID', 'warning');
        return;
    }

    showLoading('Đang gửi yêu cầu dịch thuật...');

    try {
        const response = await fetch(
            `${API_BASE_URL}/api/games/${gameId}/translate?includeRulebooks=${includeRulebooks}`,
            { method: 'POST' }
        );

        const result = await response.json();

        if (response.ok || response.status === 202) {
            showToast(`Đã gửi yêu cầu dịch cho Game ID ${gameId}!`, 'success');

            // Reload preview
            loadGamePreview();
        } else {
            showToast(result.message || 'Có lỗi xảy ra', 'error');
        }
    } catch (error) {
        showToast('Không thể kết nối đến server', 'error');
    }

    hideLoading();
}

async function refreshRulebookList() {
    const select = document.getElementById('rulebook-select');
    select.innerHTML = '<option value="">Đang tải...</option>';

    try {
        // Fetch available translated rulebooks from API
        const response = await fetch(`${API_BASE_URL}/api/rulebooks/translated`);

        if (response.ok) {
            const rulebooks = await response.json();

            if (rulebooks.length === 0) {
                select.innerHTML = '<option value="">-- Chưa có rulebook nào được dịch --</option>';
            } else {
                select.innerHTML = '<option value="">-- Chọn Rulebook --</option>' +
                    rulebooks.map(rb =>
                        `<option value="${rb.path}">${rb.gameName} - ${rb.title}</option>`
                    ).join('');
            }
        } else {
            // Fallback: try to list local files
            select.innerHTML = `
                <option value="">-- Chọn Rulebook --</option>
                <option value="224517_brass_birmingham_brass_birmingham_reference_she.md">
                    Brass: Birmingham - Reference Sheet
                </option>
                <option value="342942_ark_nova_ark_nova_-_a_plain_and_simple_.md">
                    Ark Nova - A Plain and Simple Guide
                </option>
            `;
        }
    } catch (error) {
        // Fallback with example files
        select.innerHTML = `
            <option value="">-- Chọn Rulebook --</option>
            <option value="224517_brass_birmingham_brass_birmingham_reference_she.md">
                Brass: Birmingham - Reference Sheet
            </option>
            <option value="342942_ark_nova_ark_nova_-_a_plain_and_simple_.md">
                Ark Nova - A Plain and Simple Guide
            </option>
        `;
    }
}

async function loadRulebookPreview() {
    const select = document.getElementById('rulebook-select');
    const viewer = document.getElementById('rulebook-viewer');
    const selectedFile = select.value;

    if (!selectedFile) {
        viewer.innerHTML = `
            <div class="empty-state">
                <span class="empty-icon">📖</span>
                <p>Chọn một rulebook để xem preview</p>
            </div>
        `;
        viewer.classList.remove('has-content');
        return;
    }

    viewer.innerHTML = `
        <div class="empty-state">
            <div class="spinner-small"></div>
            <p>Đang tải nội dung...</p>
        </div>
    `;

    try {
        // Try to fetch from API first
        const response = await fetch(`${API_BASE_URL}/api/rulebooks/content/${encodeURIComponent(selectedFile)}`);

        let content = '';

        if (response.ok) {
            content = await response.text();
        } else {
            // Fallback: try to fetch directly from translation-service output
            const directResponse = await fetch(`/rulebooks/${selectedFile}`);
            if (directResponse.ok) {
                content = await directResponse.text();
            }
        }

        if (content) {
            // Parse markdown and render
            viewer.innerHTML = parseMarkdown(content);
            viewer.classList.add('has-content');
        } else {
            throw new Error('Không thể tải nội dung');
        }
    } catch (error) {
        viewer.innerHTML = `
            <div class="empty-state text-error">
                <span class="empty-icon">❌</span>
                <p>Không thể tải nội dung rulebook</p>
                <small>File: ${selectedFile}</small>
            </div>
        `;
        viewer.classList.remove('has-content');
    }
}

// Simple Markdown Parser
function parseMarkdown(text) {
    // Headers
    text = text.replace(/^### (.*$)/gim, '<h3>$1</h3>');
    text = text.replace(/^## (.*$)/gim, '<h2>$1</h2>');
    text = text.replace(/^# (.*$)/gim, '<h1>$1</h1>');

    // Bold
    text = text.replace(/\*\*(.*?)\*\*/gim, '<strong>$1</strong>');

    // Italic
    text = text.replace(/\*(.*?)\*/gim, '<em>$1</em>');

    // Links
    text = text.replace(/\[(.*?)\]\((.*?)\)/gim, '<a href="$2" target="_blank">$1</a>');

    // Horizontal rule
    text = text.replace(/^---$/gim, '<hr>');

    // Code inline
    text = text.replace(/`([^`]+)`/gim, '<code>$1</code>');

    // Line breaks
    text = text.replace(/\n\n/gim, '</p><p>');
    text = text.replace(/\n/gim, '<br>');

    // Wrap in paragraphs
    text = '<p>' + text + '</p>';

    // Clean up empty paragraphs
    text = text.replace(/<p><\/p>/g, '');
    text = text.replace(/<p>(<h[1-6]>)/g, '$1');
    text = text.replace(/(<\/h[1-6]>)<\/p>/g, '$1');
    text = text.replace(/<p>(<hr>)/g, '$1');
    text = text.replace(/(<hr>)<\/p>/g, '$1');

    return text;
}

// ================================
// UI Utilities
// ================================
function showLoading(message = 'Đang xử lý...') {
    const overlay = document.getElementById('loading-overlay');
    const text = overlay.querySelector('.loading-text');
    text.textContent = message;
    overlay.classList.remove('hidden');
}

function hideLoading() {
    document.getElementById('loading-overlay').classList.add('hidden');
}

function showToast(message, type = 'info') {
    const container = document.getElementById('toast-container');
    const toast = document.createElement('div');
    toast.className = `toast ${type}`;

    const icons = {
        success: '✅',
        error: '❌',
        warning: '⚠️',
        info: 'ℹ️'
    };

    toast.innerHTML = `
        <span class="toast-icon">${icons[type] || icons.info}</span>
        <span class="toast-message">${message}</span>
        <button class="toast-close" onclick="this.parentElement.remove()">&times;</button>
    `;

    container.appendChild(toast);

    // Auto remove after 5 seconds
    setTimeout(() => {
        if (toast.parentElement) {
            toast.style.animation = 'slideIn 0.3s ease-out reverse';
            setTimeout(() => toast.remove(), 300);
        }
    }, 5000);
}

// ================================
// Keyboard Shortcuts
// ================================
document.addEventListener('keydown', (e) => {
    // Escape to close modal
    if (e.key === 'Escape') {
        closeModal();
    }

    // Ctrl+R to refresh
    if (e.ctrlKey && e.key === 'r') {
        e.preventDefault();
        document.getElementById('refresh-btn').click();
    }
});

// ================================
// Helper function for number animation
// ================================
function animateNumber(num) {
    return num.toString();
}

// ================================
// Monitor Tab Functions
// ================================
let monitorInterval = null;

// Initialize monitor when tab is opened
function initMonitor() {
    loadMonitorStatus();
    loadTranslationQueue();

    // Start auto-refresh if checkbox is checked
    const autoRefresh = document.getElementById('auto-refresh-logs');
    if (autoRefresh && autoRefresh.checked) {
        startMonitorAutoRefresh();
    }
}

function startMonitorAutoRefresh() {
    if (monitorInterval) clearInterval(monitorInterval);

    monitorInterval = setInterval(() => {
        const autoRefresh = document.getElementById('auto-refresh-logs');
        if (autoRefresh && autoRefresh.checked) {
            loadMonitorStatus();
            loadTranslationQueue();
        }
    }, 5000); // Refresh every 5 seconds
}

function stopMonitorAutoRefresh() {
    if (monitorInterval) {
        clearInterval(monitorInterval);
        monitorInterval = null;
    }
}

async function loadMonitorStatus() {
    // Check API Status
    try {
        const response = await fetch(`${API_BASE_URL}/health`);
        const apiStatus = document.getElementById('monitor-api-status');
        if (response.ok) {
            apiStatus.textContent = 'Online';
            apiStatus.className = 'stat-value status-online';
        } else {
            apiStatus.textContent = 'Error';
            apiStatus.className = 'stat-value status-offline';
        }
    } catch (error) {
        document.getElementById('monitor-api-status').textContent = 'Offline';
        document.getElementById('monitor-api-status').className = 'stat-value status-offline';
    }

    // Check Translation Service (Python)
    try {
        const response = await fetch(`${API_BASE_URL}/api/translation/status`);
        const pythonStatus = document.getElementById('monitor-python-status');
        if (response.ok) {
            const data = await response.json();
            pythonStatus.textContent = data.connected ? 'Running' : 'Stopped';
            pythonStatus.className = 'stat-value ' + (data.connected ? 'status-online' : 'status-offline');

            // Update RabbitMQ status
            document.getElementById('monitor-rabbitmq-status').textContent =
                data.rabbitmqConnected ? 'Connected' : 'Disconnected';
        } else {
            pythonStatus.textContent = 'Unknown';
            pythonStatus.className = 'stat-value status-pending';
        }
    } catch (error) {
        document.getElementById('monitor-python-status').textContent = 'Không có API';
        document.getElementById('monitor-python-status').className = 'stat-value status-offline';
        document.getElementById('monitor-rabbitmq-status').textContent = 'Unknown';
    }
}

async function loadTranslationQueue() {
    const tbody = document.getElementById('queue-tbody');

    try {
        const response = await fetch(`${API_BASE_URL}/api/games?status=pending_translation&take=20`);

        if (response.ok) {
            const games = await response.json();

            // Update pending count
            document.getElementById('monitor-queue-pending').textContent = games.length;

            if (games.length === 0) {
                tbody.innerHTML = `
                    <tr>
                        <td colspan="4" class="loading-row">
                            <span>✅ Không có yêu cầu dịch đang chờ</span>
                        </td>
                    </tr>
                `;
            } else {
                tbody.innerHTML = games.map(game => `
                    <tr>
                        <td>${game.id}</td>
                        <td>${game.name}</td>
                        <td>
                            <span class="status-badge pending_translation">Đang chờ</span>
                        </td>
                        <td>${new Date(game.updatedAt || Date.now()).toLocaleString()}</td>
                    </tr>
                `).join('');
            }
        }
    } catch (error) {
        console.error('Error loading queue:', error);
        tbody.innerHTML = `
            <tr>
                <td colspan="4" class="loading-row text-error">
                    Không thể tải queue
                </td>
            </tr>
        `;
    }
}

function refreshMonitorQueue() {
    loadTranslationQueue();
    addMonitorLog('info', 'Đã refresh translation queue');
    showToast('Đã refresh queue!', 'success');
}

function addMonitorLog(type, message) {
    const logsEl = document.getElementById('monitor-logs');
    if (!logsEl) return;

    const time = new Date().toLocaleTimeString();
    const logEntry = document.createElement('div');
    logEntry.className = `log-entry ${type}`;
    logEntry.textContent = `[${time}] ${message}`;

    logsEl.appendChild(logEntry);
    logsEl.scrollTop = logsEl.scrollHeight;

    // Limit logs to 100 entries
    while (logsEl.children.length > 100) {
        logsEl.removeChild(logsEl.firstChild);
    }
}

function clearMonitorLogs() {
    const logsEl = document.getElementById('monitor-logs');
    if (logsEl) {
        logsEl.innerHTML = '<div class="log-entry info">[--:--:--] Logs đã được xóa</div>';
    }
}

// Update switchTab to initialize monitor
const originalSwitchTab = switchTab;
switchTab = function (tabName) {
    originalSwitchTab(tabName);

    if (tabName === 'monitor') {
        initMonitor();
    } else {
        stopMonitorAutoRefresh();
    }
};

// Cleanup on page unload
window.addEventListener('beforeunload', () => {
    stopMonitorAutoRefresh();
});

