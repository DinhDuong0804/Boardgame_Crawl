using Microsoft.AspNetCore.SignalR;
using BoardGameScraper.Api.Hubs;
using BoardGameScraper.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace BoardGameScraper.Api.Services;

public class BackgroundScraperService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<ScraperHub> _hubContext;
    private readonly ILogger<BackgroundScraperService> _logger;

    private bool _isScraping = false;
    private int _processedCount = 0;
    private int _skippedCount = 0;
    private int _errorCount = 0;
    private CancellationTokenSource? _cts;

    public BackgroundScraperService(
        IServiceProvider serviceProvider,
        IHubContext<ScraperHub> hubContext,
        ILogger<BackgroundScraperService> logger)
    {
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
    }

    public bool IsScraping => _isScraping;
    public int ProcessedCount => _processedCount;
    public int SkippedCount => _skippedCount;
    public int ErrorCount => _errorCount;

    public void StartScraping(int startPage, int maxPages, int batchSize = 10)
    {
        if (_isScraping)
            return;

        _isScraping = true;
        _processedCount = 0;
        _skippedCount = 0;
        _errorCount = 0;
        _cts = new CancellationTokenSource();

        // Run in background thread
        _ = Task.Run(() => RunScrapeOperation(startPage, maxPages, batchSize, _cts.Token));
    }

    public async Task StopScraping()
    {
        _cts?.Cancel();
        _isScraping = false;
       await  LogAsync("Scraping stop requested by user.", "info");
    }

    private async Task RunScrapeOperation(int startPage, int maxPages, int batchSize, CancellationToken ct)
    {
        try
        {
            await LogAsync($"=== BẮT ĐẦU CÀO HÀNG LOẠT (Trang {startPage} -> {startPage + maxPages - 1}) ===", "info");

            using var scope = _serviceProvider.CreateScope();
            var discoveryService = scope.ServiceProvider.GetRequiredService<BggDiscoveryService>();
            var apiClient = scope.ServiceProvider.GetRequiredService<BggApiClient>();
            var gameService = scope.ServiceProvider.GetRequiredService<GameService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<BoardGameDbContext>();

            await foreach (var bggId in discoveryService.DiscoverIdsByRankAsync(startPage, maxPages, ct))
            {
                if (ct.IsCancellationRequested)
                    break;

                // 1. Kiểm tra xem game đã có trong DB chưa
                var exists = await dbContext.Games.AnyAsync(g => g.BggId == bggId, ct);
                if (exists)
                {
                    _skippedCount++;
                    await LogAsync($"[Bỏ qua] Game BGG ID {bggId} đã tồn tại trong database.", "debug");
                    continue;
                }

                // 2. Lấy thông tin chi tiết (Lưu ý: API BGG hỗ trợ lấy batch, nhưng ở đây ta cào và lưu từng cái để log chi tiết)
                try
                {
                    await LogAsync($"[Đang cào] Đang lấy thông tin game BGG ID: {bggId}...", "info");
                    var gameDetails = await apiClient.GetGamesDetailsAsync(new List<int> { bggId }, ct);

                    if (gameDetails != null && gameDetails.Count > 0)
                    {
                        var game = gameDetails[0];
                        var savedGame = await gameService.UpsertGameAsync(game, ct);

                        // Cào rulebooks
                        var rulebookService = scope.ServiceProvider.GetRequiredService<RulebookScraperService>();
                        var rulebookUrls = await rulebookService.GetRulebooksForGameAsync(bggId, ct);
                        if (rulebookUrls.Count > 0)
                        {
                            await gameService.SaveRulebooksAsync(savedGame.Id, rulebookUrls, ct);
                            await LogAsync($"[Thành công] {game.Name} (ID: {savedGame.Id}) - Tìm thấy {rulebookUrls.Count} rulebooks.", "success");
                        }
                        else
                        {
                            await LogAsync($"[Thành công] {game.Name} (ID: {savedGame.Id}) - Không tìm thấy rulebooks.", "success");
                        }

                        _processedCount++;
                    }
                    else
                    {
                        await LogAsync($"[Lỗi] Không tìm thấy thông tin cho BGG ID {bggId} từ API.", "warning");
                        _errorCount++;
                    }
                }
                catch (Exception ex)
                {
                    _errorCount++;
                    await LogAsync($"[Lỗi] Lỗi khi xử lý BGG ID {bggId}: {ex.Message}", "error");
                }

                // Rate limiting 
                await Task.Delay(2000, ct);
            }

            await LogAsync($"=== HOÀN THÀNH CÀO HÀNG LOẠT: Đã xử lý {_processedCount}, Bỏ qua {_skippedCount}, Lỗi {_errorCount} ===", "info");
        }
        catch (OperationCanceledException)
        {
            await LogAsync("=== ĐÃ DỪNG TIẾN TRÌNH CÀO ===", "warning");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi nghiêm trọng trong tiến trình cào ngầm");
            await LogAsync($"[LỖI NGHIÊM TRỌNG] {ex.Message}", "error");
        }
        finally
        {
            _isScraping = false;
        }
    }

    private async Task LogAsync(string message, string level = "info")
    {
        _logger.LogInformation($"[Scraper] {message}");
        await _hubContext.Clients.All.SendAsync("ReceiveLog", new
        {
            message,
            level,
            timestamp = DateTime.Now.ToString("HH:mm:ss")
        });
    }
}
