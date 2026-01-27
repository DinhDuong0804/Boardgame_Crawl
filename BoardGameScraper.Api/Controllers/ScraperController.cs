using Microsoft.AspNetCore.Mvc;
using BoardGameScraper.Api.Services;
using BoardGameScraper.Api.Models;

namespace BoardGameScraper.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ScraperController : ControllerBase
{
    private readonly BggDiscoveryService _discoveryService;
    private readonly BggApiClient _bggApiClient;
    private readonly RulebookScraperService _rulebookService;
    private readonly GameService _gameService;
    private readonly ILogger<ScraperController> _logger;

    public ScraperController(
        BggDiscoveryService discoveryService,
        BggApiClient bggApiClient,
        RulebookScraperService rulebookService,
        GameService gameService,
        ILogger<ScraperController> logger)
    {
        _discoveryService = discoveryService;
        _bggApiClient = bggApiClient;
        _rulebookService = rulebookService;
        _gameService = gameService;
        _logger = logger;
    }

    /// <summary>
    /// Manually trigger scraping of top ranked games
    /// </summary>
    [HttpPost("scrape-rank")]
    public async Task<IActionResult> ScrapeRankedGames(
        [FromQuery] int maxPages = 10,
        [FromQuery] int batchSize = 20,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Starting manual scrape: {MaxPages} pages", maxPages);

        int totalSaved = 0;

        // Collect all IDs from ranked pages using IAsyncEnumerable
        var allIds = new List<int>();
        await foreach (var id in _discoveryService.DiscoverIdsByRankAsync(1, ct))
        {
            allIds.Add(id);
            // Limit to approximate number of games based on pages (100 per page)
            if (allIds.Count >= maxPages * 100)
                break;
        }

        if (allIds.Count == 0)
        {
            _logger.LogWarning("No IDs found from discovery");
            return Ok(new { message = "No games found", gamesProcessed = 0 });
        }

        _logger.LogInformation("Discovered {Count} game IDs", allIds.Count);

        // Fetch details in batches
        foreach (var batch in allIds.Chunk(batchSize))
        {
            if (ct.IsCancellationRequested)
                break;

            var batchIds = batch.ToList();

            // Get game details from BGG API
            var games = await _bggApiClient.GetGamesDetailsAsync(batchIds, ct);

            // Save to database
            foreach (var game in games)
            {
                var saved = await _gameService.UpsertGameAsync(game, ct);

                // Get rulebooks
                if (game.RulebookUrls.Any())
                {
                    await _gameService.SaveRulebooksAsync(saved.Id, game.RulebookUrls, ct);
                }

                totalSaved++;
            }

            // Rate limiting
            await Task.Delay(2000, ct);

            _logger.LogInformation("Processed batch, total saved: {TotalSaved}", totalSaved);
        }

        return Ok(new
        {
            message = "Scraping completed",
            gamesProcessed = totalSaved
        });
    }

    /// <summary>
    /// Scrape a specific game by BGG ID
    /// </summary>
    [HttpPost("scrape/{bggId}")]
    public async Task<IActionResult> ScrapeGame(int bggId, CancellationToken ct)
    {
        _logger.LogInformation("Scraping game BGG ID: {BggId}", bggId);

        // Get game details
        var games = await _bggApiClient.GetGamesDetailsAsync(new List<int> { bggId }, ct);
        if (games.Count == 0)
            return NotFound(new { message = $"Game with BGG ID {bggId} not found" });

        var game = games[0];

        // Get rulebooks
        var rulebooks = await _rulebookService.GetRulebooksForGameAsync(bggId, ct);
        game.RulebookUrls = rulebooks;

        // Save to database
        var saved = await _gameService.UpsertGameAsync(game, ct);
        await _gameService.SaveRulebooksAsync(saved.Id, rulebooks, ct);

        return Ok(new
        {
            message = "Game scraped successfully",
            id = saved.Id,
            bggId = saved.BggId,
            name = saved.Name,
            rulebooksFound = rulebooks.Count
        });
    }

    /// <summary>
    /// Scrape rulebooks for an existing game
    /// </summary>
    [HttpPost("{id}/scrape-rulebooks")]
    public async Task<IActionResult> ScrapeRulebooks(int id, CancellationToken ct)
    {
        var game = await _gameService.GetGameByIdAsync(id, ct);
        if (game == null)
            return NotFound();

        var rulebooks = await _rulebookService.GetRulebooksForGameAsync(game.BggId, ct);
        await _gameService.SaveRulebooksAsync(id, rulebooks, ct);

        return Ok(new
        {
            message = "Rulebooks scraped",
            gameId = id,
            rulebooksFound = rulebooks.Count
        });
    }
}
