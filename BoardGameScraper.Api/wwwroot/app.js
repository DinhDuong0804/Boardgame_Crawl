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
    isLoading: false,
    rulebookFiles: [],
    scraperConnection: null,
    isScrapingBulk: false
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
    setupScraperSignalR();
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
        case 'monitor':
            updateMonitorStats();
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
            const data = await gamesResponse.json();
            // The API now returns { games: [], totalCount: X, ... }
            updateActivityList(data.games || []);
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
    document.getElementById('stat-rulebooks').textContent = animateNumber(stats.totalRulebooks || 0);

    // Animate numbers
    animateStatsNumbers(stats);
}

function animateStatsNumbers(stats) {
    const counters = [
        { el: 'stat-total', target: stats.totalGames || 0 },
        { el: 'stat-active', target: stats.activeGames || 0 },
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
        'inactive': '⏸️'
    };
    return icons[status] || '📝';
}

function getStatusText(status) {
    const texts = {
        'scraped': 'Đã cào dữ liệu',
        'active': 'Đang hoạt động',
        'inactive': 'Không hoạt động'
    };
    return texts[status] || status;
}

// ================================
// Scraper Functions
// ================================
async function scrapeRanked() {
    const startPage = parseInt(document.getElementById('startPage').value) || 1;
    const maxPages = parseInt(document.getElementById('maxPages').value) || 1;
    const batchSize = parseInt(document.getElementById('batchSize').value) || 20;

    showLoading('Đang cào dữ liệu từ BGG...');
    showScraperProgress();
    addLog('info', `Bắt đầu cào từ trang ${startPage}, tổng ${maxPages} trang với batch size ${batchSize}...`);

    try {
        const response = await fetch(
            `${API_BASE_URL}/api/scraper/scrape-rank?startPage=${startPage}&maxPages=${maxPages}&batchSize=${batchSize}`,
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
            <td colspan="10" class="loading-row">
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
        const data = await response.json();

        state.games = data.games;
        state.totalGames = data.totalCount;
        renderGamesTable(data.games);
    } catch (error) {
        console.error('Error loading games:', error);
        tbody.innerHTML = `
            <tr>
                <td colspan="10" class="loading-row text-error">
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
                <td colspan="10" class="loading-row">
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
            <td class="action-cell">
                <button class="btn btn-sm btn-outline" onclick="viewGameDetail(${game.id})" title="Xem chi tiết">
                    👁️
                </button>
                ${game.status !== 'active' ?
            `<button class="btn btn-sm btn-success" onclick="activateGame(${game.id})" title="Kích hoạt">
                        ✓
                    </button>` :
            `<button class="btn btn-sm btn-danger" onclick="deactivateGame(${game.id})" title="Tắt">
                        ✗
                    </button>`
        }
            </td>
        </tr>
    `).join('');

    renderPagination();
}

function renderPagination() {
    const pagination = document.getElementById('games-pagination');
    const totalPages = Math.ceil(state.totalGames / state.pageSize) || 1;
    const currentPage = state.currentPage;
    const maxVisiblePages = 7;

    let html = `
        <button onclick="goToPage(${currentPage - 1})" ${currentPage === 0 ? 'disabled' : ''}>
            ← Trước
        </button>
    `;

    // Calculate page range to display
    let startPage = Math.max(0, currentPage - Math.floor(maxVisiblePages / 2));
    let endPage = Math.min(totalPages, startPage + maxVisiblePages);

    // Adjust if we're near the end
    if (endPage - startPage < maxVisiblePages) {
        startPage = Math.max(0, endPage - maxVisiblePages);
    }

    // First page + ellipsis
    if (startPage > 0) {
        html += `<button onclick="goToPage(0)">1</button>`;
        if (startPage > 1) {
            html += `<span style="padding: 0 8px;">...</span>`;
        }
    }

    // Page buttons
    for (let i = startPage; i < endPage; i++) {
        html += `
            <button onclick="goToPage(${i})" class="${currentPage === i ? 'active' : ''}">
                ${i + 1}
            </button>
        `;
    }

    // Last page + ellipsis
    if (endPage < totalPages) {
        if (endPage < totalPages - 1) {
            html += `<span style="padding: 0 8px;">...</span>`;
        }
        html += `<button onclick="goToPage(${totalPages - 1})">${totalPages}</button>`;
    }

    html += `
        <button onclick="goToPage(${currentPage + 1})" ${currentPage >= totalPages - 1 ? 'disabled' : ''}>
            Sau →
        </button>
        <span style="margin-left: 16px; color: var(--text-secondary);">Trang ${currentPage + 1} / ${totalPages}</span>
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

        // Store in state for PDF viewer access
        state.currentGameDetail = game;

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
            <p style="max-height: 250px; overflow-y: auto; font-size: 0.875rem; line-height: 1.6;">
                ${game.description || 'Chưa có mô tả'}
            </p>
        </div>
        
        <div style="margin-top: var(--space-5);">
            <h4>Thể loại</h4>
            <div style="display: flex; flex-wrap: wrap; gap: var(--space-2); margin-top: var(--space-2);">
                ${(game.categories || []).map(cat => {
        const nameOnly = cat.split(':')[0].trim();
        return `<span class="badge badge-category">${nameOnly}</span>`;
    }).join('') || '<span class="text-muted">Chưa có</span>'}
            </div>
        </div>
        
        <div style="margin-top: var(--space-4);">
            <h4>Mechanics</h4>
            <div style="display: flex; flex-wrap: wrap; gap: var(--space-2); margin-top: var(--space-2);">
                ${(game.mechanics || []).map(mech => {
        const nameOnly = mech.split(':')[0].trim();
        return `<span class="badge badge-mechanic">${nameOnly}</span>`;
    }).join('') || '<span class="text-muted">Chưa có</span>'}
            </div>
        </div>
        
        ${game.rulebooks && game.rulebooks.length > 0 ? `
            <div style="margin-top: var(--space-5);">
                <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: var(--space-2);">
                    <h4>📚 Rulebooks (${game.rulebooks.length})</h4>
                    <button class="btn btn-sm btn-outline" onclick="scrapeRulebooksForGame(${game.bggId})">
                        🔄 Quét lại
                    </button>
                </div>
                <ul style="list-style: none;">
                    ${game.rulebooks.map(rb => `
                        <li style="padding: var(--space-2) 0; border-bottom: 1px solid var(--border-light); display: flex; justify-content: space-between; align-items: center;">
                            <div style="flex: 1;">
                                <div style="font-weight: 500;">${rb.title}</div>
                                <span class="status-badge ${rb.status}" style="font-size: 0.7rem; padding: 1px 6px;">
                                    ${rb.status}
                                </span>
                            </div>
                            <div style="display: flex; gap: var(--space-2);">
                                ${rb.status === 'downloaded' ?
            `<button class="btn btn-sm btn-primary" onclick="viewRulebookPdf(${rb.id})">
                                        👁️ Xem
                                    </button>` :
            `<button class="btn btn-sm btn-secondary" onclick="downloadRulebook(${game.bggId}, ${rb.id}, '${rb.originalUrl}', '${rb.title.replace(/'/g, "\\'")}')">
                                        📥 Tải
                                    </button>`
        }
                            </div>
                        </li>
                    `).join('')}
                </ul>
            </div>
        ` : `
            <div style="margin-top: var(--space-5); text-align: center; padding: var(--space-4); background: var(--bg-tertiary); border-radius: var(--radius-md);">
                <p>Chưa có rulebook nào.</p>
                <button class="btn btn-primary" style="margin-top: var(--space-2);" onclick="scrapeRulebooksForGame(${game.bggId})">
                    🔍 Tìm rulebooks trên BGG
                </button>
            </div>
        `}
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
// PDF Viewer & Rulebooks
// ================================
function openPdfModal(url, title) {
    const modal = document.getElementById('pdf-modal');
    const titleEl = document.getElementById('pdf-modal-title');
    const viewer = document.getElementById('pdf-viewer');

    titleEl.textContent = `Xem Rulebook: ${title}`;
    viewer.src = url;
    modal.classList.add('active');
}

function closePdfModal() {
    const modal = document.getElementById('pdf-modal');
    const viewer = document.getElementById('pdf-viewer');
    viewer.src = '';
    modal.classList.remove('active');
}

async function viewRulebookPdf(rulebookId) {
    showLoading('Đang chuẩn bị xem rulebook...');
    try {
        // Find the rulebook in state.currentGameDetail (from modal)
        let rulebook = null;
        if (state.currentGameDetail && state.currentGameDetail.rulebooks) {
            rulebook = state.currentGameDetail.rulebooks.find(r => r.id === rulebookId);
        }

        if (rulebook && rulebook.status === 'downloaded' && rulebook.localFileName) {
            const viewUrl = `${API_BASE_URL}/api/rulebooks/view/${rulebook.localFileName}`;
            openPdfModal(viewUrl, rulebook.title);
        } else {
            showToast('Rulebook chưa được tải xuống máy chủ', 'warning');
        }
    } catch (error) {
        showToast('Lỗi khi mở rulebook', 'error');
    }
    hideLoading();
}

async function downloadRulebook(bggId, rulebookId, url, title) {
    showLoading(`Đang tải rulebook: ${title}...`);
    try {
        const response = await fetch(`${API_BASE_URL}/api/rulebooks/download-bgg`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                url: url,
                bggId: bggId,
                rulebookTitle: title
            })
        });

        if (response.ok) {
            showToast(`Đã tải xong: ${title}`, 'success');
            // Refresh detail if modal is open
            const game = state.games.find(g => g.bggId === bggId);
            if (game) viewGameDetail(game.id);
        } else {
            const err = await response.json();
            showToast(err.error || 'Lỗi khi tải rulebook', 'error');
        }
    } catch (error) {
        showToast('Lỗi kết nối server', 'error');
    }
    hideLoading();
}

async function scrapeRulebooksForGame(bggId) {
    showLoading('Đang tìm kiếm rulebooks trên BGG...');
    try {
        const response = await fetch(`${API_BASE_URL}/api/rulebooks/game/${bggId}/scrape`, {
            method: 'POST'
        });

        const result = await response.json();
        if (response.ok) {
            showToast(`Thành công! Tìm thấy ${result.found} rulebooks, đã lưu ${result.saved} cái mới.`, 'success');
            // Refresh detail
            const game = state.games.find(g => g.bggId === bggId);
            if (game) viewGameDetail(game.id);
        } else {
            showToast(result.error || 'Lỗi khi cào rulebooks', 'error');
        }
    } catch (error) {
        showToast('Lỗi kết nối server', 'error');
    }
    hideLoading();
}

// ================================
// Bulk Scraper & SignalR (Monitor)
// ================================
function setupScraperSignalR() {
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/scraper")
        .withAutomaticReconnect()
        .build();

    connection.on("ReceiveLog", (data) => {
        addMonitorLog(data.level, data.message, data.timestamp);
    });

    connection.start()
        .then(() => {
            console.log("SignalR Connected to Scraper Hub");
            addMonitorLog('system', 'SignalR đã kết nối thành công!');
        })
        .catch(err => {
            console.error("SignalR Connection Error: ", err);
            addMonitorLog('error', 'Lỗi kết nối SignalR: ' + err.toString());
        });

    state.scraperConnection = connection;
}

async function startBulkScrape() {
    const startPage = document.getElementById('monitor-start-page').value || 1;
    const maxPages = document.getElementById('monitor-max-pages').value || 5;

    try {
        const response = await fetch(`${API_BASE_URL}/api/scraper/bulk-start?startPage=${startPage}&maxPages=${maxPages}`, {
            method: 'POST'
        });

        if (response.ok) {
            showToast('Đã bắt đầu tiến trình cào ngầm', 'success');
            setMonitorScrapingState(true);
            startStatusPolling();
        } else {
            const err = await response.json();
            showToast(err.message || 'Không thể bắt đầu', 'error');
        }
    } catch (error) {
        showToast('Lỗi kết nối', 'error');
    }
}

async function stopBulkScrape() {
    try {
        const response = await fetch(`${API_BASE_URL}/api/scraper/bulk-stop`, { method: 'POST' });
        if (response.ok) {
            showToast('Đã gửi yêu cầu dừng', 'info');
        }
    } catch (error) {
        showToast('Lỗi kết nối', 'error');
    }
}

function setMonitorScrapingState(isScraping) {
    state.isScrapingBulk = isScraping;
    document.getElementById('monitor-start-btn').disabled = isScraping;
    document.getElementById('monitor-stop-btn').disabled = !isScraping;
}

let statusPollInterval = null;
function startStatusPolling() {
    if (statusPollInterval) clearInterval(statusPollInterval);
    updateMonitorStats();
    statusPollInterval = setInterval(updateMonitorStats, 2000);
}

async function updateMonitorStats() {
    try {
        const response = await fetch(`${API_BASE_URL}/api/scraper/bulk-status`);
        if (response.ok) {
            const status = await response.json();
            document.getElementById('monitor-stat-processed').textContent = status.processed;
            document.getElementById('monitor-stat-skipped').textContent = status.skipped;
            document.getElementById('monitor-stat-errors').textContent = status.errors;

            setMonitorScrapingState(status.isScraping);

            if (!status.isScraping && statusPollInterval) {
                clearInterval(statusPollInterval);
                statusPollInterval = null;
            }
        }
    } catch (error) {
        console.error('Error polling status:', error);
    }
}

function addMonitorLog(level, message, timestamp) {
    const consoleEl = document.getElementById('monitor-console');
    if (!consoleEl) return;

    const time = timestamp || new Date().toLocaleTimeString('vi-VN', { hour12: false });

    const logEntry = document.createElement('div');
    logEntry.className = `log-entry ${level}`;
    logEntry.innerHTML = `
        <span class="log-time">[${time}]</span>
        <span class="log-message">${message}</span>
    `;

    consoleEl.appendChild(logEntry);
    consoleEl.scrollTop = consoleEl.scrollHeight;

    // Auto-trim long logs
    if (consoleEl.children.length > 500) {
        consoleEl.removeChild(consoleEl.firstChild);
    }
}

function clearMonitorLogs() {
    const consoleEl = document.getElementById('monitor-console');
    if (consoleEl) {
        consoleEl.innerHTML = '<div class="log-entry system">Đã xóa nhật ký.</div>';
    }
}


