using Microsoft.AspNetCore.Mvc;
using BoardGameScraper.Api.Services;
using BoardGameScraper.Api.Data.Entities;

namespace BoardGameScraper.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController : ControllerBase
{
    private readonly GameService _gameService;
    private readonly ILogger<GamesController> _logger;

    public GamesController(
        GameService gameService,
        ILogger<GamesController> logger)
    {
        _gameService = gameService;
        _logger = logger;
    }

    /// <summary>
    /// Get list of games with optional filtering
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<object>> GetGames(
        [FromQuery] string? status = null,
        [FromQuery] int? minPlayers = null,
        [FromQuery] int? maxPlayers = null,
        [FromQuery] int? maxPlaytime = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var (games, totalCount) = await _gameService.GetGamesWithCountAsync(
            status, minPlayers, maxPlayers, maxPlaytime, skip, take, ct);

        return Ok(new
        {
            games = games.Select(g => MapToDto(g)).ToList(),
            totalCount = totalCount,
            skip = skip,
            take = take
        });
    }

    /// <summary>
    /// Get game by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<GameDto>> GetGame(int id, CancellationToken ct)
    {
        var game = await _gameService.GetGameByIdAsync(id, ct);
        if (game == null)
            return NotFound();

        return Ok(MapToDto(game, includeRulebooks: true));
    }

    /// <summary>
    /// Get game by BGG ID
    /// </summary>
    [HttpGet("bgg/{bggId}")]
    public async Task<ActionResult<GameDto>> GetGameByBggId(int bggId, CancellationToken ct)
    {
        var game = await _gameService.GetGameByBggIdAsync(bggId, ct);
        if (game == null)
            return NotFound();

        return Ok(MapToDto(game, includeRulebooks: true));
    }

    /// <summary>
    /// Activate a game (make it visible to customers)
    /// </summary>
    [HttpPost("{id}/activate")]
    public async Task<IActionResult> ActivateGame(int id, CancellationToken ct)
    {
        var result = await _gameService.UpdateGameStatusAsync(id, "active", ct);
        if (!result)
            return NotFound();

        return Ok(new { message = "Game activated successfully" });
    }

    /// <summary>
    /// Deactivate a game
    /// </summary>
    [HttpPost("{id}/deactivate")]
    public async Task<IActionResult> DeactivateGame(int id, CancellationToken ct)
    {
        var result = await _gameService.UpdateGameStatusAsync(id, "inactive", ct);
        if (!result)
            return NotFound();

        return Ok(new { message = "Game deactivated successfully" });
    }

    /// <summary>
    /// Update inventory for a game
    /// </summary>
    [HttpPost("{id}/inventory")]
    public async Task<ActionResult<InventoryDto>> UpdateInventory(
        int id,
        [FromBody] InventoryUpdateRequest request,
        CancellationToken ct)
    {
        var game = await _gameService.GetGameByIdAsync(id, ct);
        if (game == null)
            return NotFound();

        var inventory = await _gameService.UpsertInventoryAsync(
            id,
            request.Quantity,
            request.Location,
            request.Condition ?? "good",
            ct);

        return Ok(new InventoryDto
        {
            GameId = inventory.GameId,
            Quantity = inventory.Quantity,
            Available = inventory.Available,
            Location = inventory.Location,
            Condition = inventory.Condition
        });
    }

    /// <summary>
    /// Get statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<GameStats>> GetStats(CancellationToken ct)
    {
        var stats = await _gameService.GetStatsAsync(ct);
        return Ok(stats);
    }

    // DTO Mapping
    private GameDto MapToDto(Game game, bool includeRulebooks = false)
    {
        var dto = new GameDto
        {
            Id = game.Id,
            BggId = game.BggId,
            Name = game.Name,
            YearPublished = game.YearPublished,
            Description = game.Description,
            MinPlayers = game.MinPlayers,
            MaxPlayers = game.MaxPlayers,
            MinPlaytime = game.MinPlaytime,
            MaxPlaytime = game.MaxPlaytime,
            AvgRating = game.AvgRating,
            BggRank = game.BggRank,
            ImageUrl = game.ImageUrl,
            ThumbnailUrl = game.ThumbnailUrl,
            Status = game.Status,
            Quantity = game.Inventory?.Quantity,
            Available = game.Inventory?.Available,
            Location = game.Inventory?.Location
        };

        // Parse JSON fields
        try
        {
            dto.Categories = System.Text.Json.JsonSerializer.Deserialize<List<string>>(game.Categories) ?? new();
            dto.Mechanics = System.Text.Json.JsonSerializer.Deserialize<List<string>>(game.Mechanics) ?? new();
        }
        catch
        {
            dto.Categories = new List<string>();
            dto.Mechanics = new List<string>();
        }

        if (includeRulebooks && game.Rulebooks.Any())
        {
            dto.Rulebooks = game.Rulebooks.Select(r => new RulebookDto
            {
                Id = r.Id,
                Title = r.Title,
                OriginalUrl = r.OriginalUrl,
                FileType = r.FileType,
                Status = r.Status,
                LocalFileName = !string.IsNullOrEmpty(r.LocalFilePath) ? Path.GetFileName(r.LocalFilePath) : null
            }).ToList();
        }

        return dto;
    }
}

// DTOs
public class GameDto
{
    public int Id { get; set; }
    public int BggId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? YearPublished { get; set; }
    public string? Description { get; set; }
    public int? MinPlayers { get; set; }
    public int? MaxPlayers { get; set; }
    public int? MinPlaytime { get; set; }
    public int? MaxPlaytime { get; set; }
    public decimal? AvgRating { get; set; }
    public int? BggRank { get; set; }
    public string? ImageUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? Quantity { get; set; }
    public int? Available { get; set; }
    public string? Location { get; set; }
    public List<string> Categories { get; set; } = new();
    public List<string> Mechanics { get; set; } = new();
    public List<RulebookDto>? Rulebooks { get; set; }
}

public class RulebookDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string OriginalUrl { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? LocalFileName { get; set; }
}

public class InventoryDto
{
    public int GameId { get; set; }
    public int Quantity { get; set; }
    public int Available { get; set; }
    public string? Location { get; set; }
    public string Condition { get; set; } = string.Empty;
}

public class InventoryUpdateRequest
{
    public int Quantity { get; set; } = 1;
    public string? Location { get; set; }
    public string? Condition { get; set; }
}
