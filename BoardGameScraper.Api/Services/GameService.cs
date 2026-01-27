using Microsoft.EntityFrameworkCore;
using BoardGameScraper.Api.Data;
using BoardGameScraper.Api.Data.Entities;
using BoardGameScraper.Api.Models;

namespace BoardGameScraper.Api.Services;

/// <summary>
/// Service for managing games in database
/// </summary>
public class GameService
{
    private readonly BoardGameDbContext _db;
    private readonly ILogger<GameService> _logger;

    public GameService(BoardGameDbContext db, ILogger<GameService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Save or update a game from scraper
    /// </summary>
    public async Task<Game> UpsertGameAsync(GameItem scrapedGame, CancellationToken ct = default)
    {
        var existing = await _db.Games
            .FirstOrDefaultAsync(g => g.BggId == scrapedGame.BggId, ct);

        if (existing != null)
        {
            // Update existing
            UpdateGameFromScraped(existing, scrapedGame);
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            // Create new
            existing = new Game
            {
                BggId = scrapedGame.BggId,
                Status = "scraped",
                ScrapedAt = DateTime.UtcNow
            };
            UpdateGameFromScraped(existing, scrapedGame);
            _db.Games.Add(existing);
        }

        await _db.SaveChangesAsync(ct);
        return existing;
    }

    private void UpdateGameFromScraped(Game game, GameItem scraped)
    {
        game.Name = scraped.Name ?? "";
        game.YearPublished = scraped.Year;
        game.Description = scraped.Description;
        game.MinPlayers = scraped.MinPlayers;
        game.MaxPlayers = scraped.MaxPlayers;
        game.MinPlaytime = scraped.MinTime;
        game.MaxPlaytime = scraped.MaxTime;
        game.AvgRating = scraped.AvgRating.HasValue ? (decimal)scraped.AvgRating.Value : null;
        game.BggRank = scraped.Rank;

        // Images
        if (scraped.ImageUrls.Count > 0)
        {
            game.ImageUrl = scraped.ImageUrls[0];
            game.ThumbnailUrl = scraped.ImageUrls.Count > 1 ? scraped.ImageUrls[1] : scraped.ImageUrls[0];
        }

        // JSON fields
        game.Categories = System.Text.Json.JsonSerializer.Serialize(scraped.Categories);
        game.Mechanics = System.Text.Json.JsonSerializer.Serialize(scraped.Mechanics);
        game.Designers = System.Text.Json.JsonSerializer.Serialize(scraped.Designers);
        game.Artists = System.Text.Json.JsonSerializer.Serialize(scraped.Artists);
        game.Publishers = System.Text.Json.JsonSerializer.Serialize(scraped.Publishers);
    }

    /// <summary>
    /// Save rulebooks for a game
    /// </summary>
    public async Task SaveRulebooksAsync(int gameId, List<RulebookInfo> rulebooks, CancellationToken ct = default)
    {
        foreach (var rb in rulebooks)
        {
            // Check if already exists
            var existing = await _db.Rulebooks
                .FirstOrDefaultAsync(r => r.GameId == gameId && r.OriginalUrl == rb.Url, ct);

            if (existing == null)
            {
                _db.Rulebooks.Add(new Rulebook
                {
                    GameId = gameId,
                    Title = rb.Title,
                    OriginalUrl = rb.Url,
                    FileType = rb.FileType,
                    Language = rb.Language ?? "English",
                    Status = "pending"
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Get games with optional filtering
    /// </summary>
    public async Task<List<Game>> GetGamesAsync(
        string? status = null,
        int? minPlayers = null,
        int? maxPlayers = null,
        int? maxPlaytime = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        var query = _db.Games.AsQueryable();

        if (!string.IsNullOrEmpty(status))
            query = query.Where(g => g.Status == status);

        if (minPlayers.HasValue)
            query = query.Where(g => g.MaxPlayers >= minPlayers.Value);

        if (maxPlayers.HasValue)
            query = query.Where(g => g.MinPlayers <= maxPlayers.Value);

        if (maxPlaytime.HasValue)
            query = query.Where(g => g.MinPlaytime <= maxPlaytime.Value);

        return await query
            .OrderBy(g => g.BggRank ?? int.MaxValue)
            .Skip(skip)
            .Take(take)
            .Include(g => g.Translation)
            .Include(g => g.Inventory)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get game by ID with all related data
    /// </summary>
    public async Task<Game?> GetGameByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.Games
            .Include(g => g.Translation)
            .Include(g => g.Rulebooks)
            .Include(g => g.Inventory)
            .FirstOrDefaultAsync(g => g.Id == id, ct);
    }

    /// <summary>
    /// Get game by BGG ID
    /// </summary>
    public async Task<Game?> GetGameByBggIdAsync(int bggId, CancellationToken ct = default)
    {
        return await _db.Games
            .Include(g => g.Translation)
            .Include(g => g.Rulebooks)
            .FirstOrDefaultAsync(g => g.BggId == bggId, ct);
    }

    /// <summary>
    /// Update game status (activate/deactivate)
    /// </summary>
    public async Task<bool> UpdateGameStatusAsync(int id, string status, CancellationToken ct = default)
    {
        var game = await _db.Games.FindAsync(new object[] { id }, ct);
        if (game == null)
            return false;

        game.Status = status;
        game.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Game {Id} status updated to {Status}", id, status);
        return true;
    }

    /// <summary>
    /// Add or update inventory for a game
    /// </summary>
    public async Task<CafeInventory> UpsertInventoryAsync(
        int gameId,
        int quantity,
        string? location,
        string condition = "good",
        CancellationToken ct = default)
    {
        var inventory = await _db.CafeInventories
            .FirstOrDefaultAsync(i => i.GameId == gameId, ct);

        if (inventory == null)
        {
            inventory = new CafeInventory
            {
                GameId = gameId
            };
            _db.CafeInventories.Add(inventory);
        }

        inventory.Quantity = quantity;
        inventory.Available = quantity;
        inventory.Location = location;
        inventory.Condition = condition;
        inventory.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return inventory;
    }

    /// <summary>
    /// Update translation for a game (called when translation completes)
    /// </summary>
    public async Task UpdateTranslationAsync(
        int gameId,
        string? nameVi,
        string? descriptionVi,
        bool success,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        var translation = await _db.GameTranslations
            .FirstOrDefaultAsync(t => t.GameId == gameId, ct);

        if (translation == null)
        {
            translation = new GameTranslation { GameId = gameId };
            _db.GameTranslations.Add(translation);
        }

        translation.NameVi = nameVi;
        translation.DescriptionVi = descriptionVi;
        translation.Status = success ? "completed" : "failed";
        translation.ErrorMessage = errorMessage;
        translation.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Get statistics
    /// </summary>
    public async Task<GameStats> GetStatsAsync(CancellationToken ct = default)
    {
        return new GameStats
        {
            TotalGames = await _db.Games.CountAsync(ct),
            ScrapedGames = await _db.Games.CountAsync(g => g.Status == "scraped", ct),
            ActiveGames = await _db.Games.CountAsync(g => g.Status == "active", ct),
            TranslatedGames = await _db.GameTranslations.CountAsync(t => t.Status == "completed", ct),
            PendingTranslations = await _db.GameTranslations.CountAsync(t => t.Status == "pending", ct),
            TotalRulebooks = await _db.Rulebooks.CountAsync(ct),
            TranslatedRulebooks = await _db.Rulebooks.CountAsync(r => r.Status == "completed", ct)
        };
    }
}

public class GameStats
{
    public int TotalGames { get; set; }
    public int ScrapedGames { get; set; }
    public int ActiveGames { get; set; }
    public int TranslatedGames { get; set; }
    public int PendingTranslations { get; set; }
    public int TotalRulebooks { get; set; }
    public int TranslatedRulebooks { get; set; }
}
