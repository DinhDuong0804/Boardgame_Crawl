using Microsoft.AspNetCore.Mvc;
using BoardGameScraper.Api.Services;
using BoardGameScraper.Api.Data.Entities;

namespace BoardGameScraper.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController : ControllerBase
{
    private readonly GameService _gameService;
    private readonly RabbitMQService _rabbitMQ;
    private readonly ILogger<GamesController> _logger;

    public GamesController(
        GameService gameService,
        RabbitMQService rabbitMQ,
        ILogger<GamesController> logger)
    {
        _gameService = gameService;
        _rabbitMQ = rabbitMQ;
        _logger = logger;
    }

    /// <summary>
    /// Get list of games with optional filtering
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<GameDto>>> GetGames(
        [FromQuery] string? status = null,
        [FromQuery] int? minPlayers = null,
        [FromQuery] int? maxPlayers = null,
        [FromQuery] int? maxPlaytime = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var games = await _gameService.GetGamesAsync(
            status, minPlayers, maxPlayers, maxPlaytime, skip, take, ct);

        return Ok(games.Select(g => MapToDto(g)).ToList());
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
    /// Request translation for a game
    /// </summary>
    [HttpPost("{id}/translate")]
    public async Task<IActionResult> RequestTranslation(
        int id,
        [FromQuery] bool includeRulebooks = false,
        CancellationToken ct = default)
    {
        var game = await _gameService.GetGameByIdAsync(id, ct);
        if (game == null)
            return NotFound();

        // Update status
        await _gameService.UpdateGameStatusAsync(id, "pending_translation", ct);

        // Build translation request
        var request = new TranslationRequest
        {
            GameId = game.Id,
            BggId = game.BggId,
            GameName = game.Name,
            Description = game.Description,
            TranslateInfo = true,
            TranslateRulebooks = includeRulebooks
        };

        if (includeRulebooks && game.Rulebooks.Any())
        {
            request.Rulebooks = game.Rulebooks
                .Where(r => r.Status == "pending" || r.Status == "downloaded")
                .Select(r => new RulebookToTranslate
                {
                    RulebookId = r.Id,
                    Title = r.Title,
                    Url = r.OriginalUrl,
                    LocalFilePath = r.LocalFilePath
                })
                .ToList();
        }

        // Send to RabbitMQ
        await _rabbitMQ.RequestTranslationAsync(request);

        _logger.LogInformation("Translation requested for game {Id}: {Name}", id, game.Name);

        return Accepted(new
        {
            message = "Translation request sent",
            gameId = id,
            includeRulebooks
        });
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
            NameVi = game.Translation?.NameVi,
            YearPublished = game.YearPublished,
            Description = game.Description,
            DescriptionVi = game.Translation?.DescriptionVi,
            MinPlayers = game.MinPlayers,
            MaxPlayers = game.MaxPlayers,
            MinPlaytime = game.MinPlaytime,
            MaxPlaytime = game.MaxPlaytime,
            AvgRating = game.AvgRating,
            BggRank = game.BggRank,
            ImageUrl = game.ImageUrl,
            ThumbnailUrl = game.ThumbnailUrl,
            Status = game.Status,
            HasTranslation = game.Translation?.Status == "completed",
            TranslationStatus = game.Translation?.Status,
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
                HasVietnamese = !string.IsNullOrEmpty(r.ContentVi)
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
    public string? NameVi { get; set; }
    public int? YearPublished { get; set; }
    public string? Description { get; set; }
    public string? DescriptionVi { get; set; }
    public int? MinPlayers { get; set; }
    public int? MaxPlayers { get; set; }
    public int? MinPlaytime { get; set; }
    public int? MaxPlaytime { get; set; }
    public decimal? AvgRating { get; set; }
    public int? BggRank { get; set; }
    public string? ImageUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool HasTranslation { get; set; }
    public string? TranslationStatus { get; set; }
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
    public bool HasVietnamese { get; set; }
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
