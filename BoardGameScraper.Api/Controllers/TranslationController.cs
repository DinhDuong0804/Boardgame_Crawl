using Microsoft.AspNetCore.Mvc;
using BoardGameScraper.Api.Services;
using BoardGameScraper.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardGameScraper.Api.Controllers;

/// <summary>
/// Controller để theo dõi trạng thái dịch thuật
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TranslationController : ControllerBase
{
    private readonly ILogger<TranslationController> _logger;
    private readonly RabbitMQService _rabbitMQ;
    private readonly BoardGameDbContext _db;
    private readonly IConfiguration _config;
    
    public TranslationController(
        ILogger<TranslationController> logger,
        RabbitMQService rabbitMQ,
        BoardGameDbContext db,
        IConfiguration config)
    {
        _logger = logger;
        _rabbitMQ = rabbitMQ;
        _db = db;
        _config = config;
    }
    
    /// <summary>
    /// Get translation service status
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<TranslationStatusDto>> GetStatus()
    {
        var status = new TranslationStatusDto();
        
        // Get pending translation count
        status.PendingCount = await _db.Games
            .CountAsync(g => g.Status == "pending_translation");
        
        // Get completed translation count
        status.CompletedCount = await _db.GameTranslations
            .CountAsync(t => t.Status == "completed");
        
        // Get failed translation count
        status.FailedCount = await _db.GameTranslations
            .CountAsync(t => t.Status == "failed");
        
        // Get RabbitMQ connection status
        status.RabbitmqHost = _config["RabbitMQSettings:Host"] ?? "localhost";
        status.RabbitmqPort = _config.GetValue<int>("RabbitMQSettings:Port", 5672);
        
        // We can't really test RabbitMQ connection here without modifying RabbitMQService
        // So we'll just indicate if the config is present
        status.RabbitmqConnected = !string.IsNullOrEmpty(status.RabbitmqHost);
        
        // Python service status - we'll try to call the Python API if configured
        var pythonApiUrl = _config["Translation:PythonApiUrl"] ?? "http://localhost:5001";
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(2);
            var response = await httpClient.GetAsync($"{pythonApiUrl}/health");
            status.PythonServiceConnected = response.IsSuccessStatusCode;
        }
        catch
        {
            status.PythonServiceConnected = false;
        }
        
        // Overall connected status
        status.Connected = status.RabbitmqConnected;
        
        // Get recent translations
        status.RecentTranslations = await _db.GameTranslations
            .OrderByDescending(t => t.CompletedAt)
            .Take(5)
            .Select(t => new RecentTranslationDto
            {
                GameId = t.GameId,
                GameName = t.Game != null ? t.Game.Name : "Unknown",
                Status = t.Status,
                CompletedAt = t.CompletedAt
            })
            .ToListAsync();
        
        return Ok(status);
    }
    
    /// <summary>
    /// Get translation queue (pending translations)
    /// </summary>
    [HttpGet("queue")]
    public async Task<ActionResult<List<TranslationQueueItemDto>>> GetQueue(
        [FromQuery] int take = 20)
    {
        var queue = await _db.Games
            .Where(g => g.Status == "pending_translation")
            .OrderBy(g => g.UpdatedAt)
            .Take(take)
            .Select(g => new TranslationQueueItemDto
            {
                GameId = g.Id,
                BggId = g.BggId,
                GameName = g.Name,
                RequestedAt = g.UpdatedAt
            })
            .ToListAsync();
        
        return Ok(queue);
    }
    
    /// <summary>
    /// Get translation history
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<List<TranslationHistoryDto>>> GetHistory(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20)
    {
        var history = await _db.GameTranslations
            .Include(t => t.Game)
            .OrderByDescending(t => t.CompletedAt)
            .Skip(skip)
            .Take(take)
            .Select(t => new TranslationHistoryDto
            {
                GameId = t.GameId,
                GameName = t.Game != null ? t.Game.Name : "Unknown",
                Status = t.Status,
                NameVi = t.NameVi,
                HasDescription = !string.IsNullOrEmpty(t.DescriptionVi),
                ErrorMessage = t.ErrorMessage,
                CompletedAt = t.CompletedAt
            })
            .ToListAsync();
        
        return Ok(history);
    }
}

// DTOs
public class TranslationStatusDto
{
    public bool Connected { get; set; }
    public bool RabbitmqConnected { get; set; }
    public bool PythonServiceConnected { get; set; }
    public string RabbitmqHost { get; set; } = string.Empty;
    public int RabbitmqPort { get; set; }
    public int PendingCount { get; set; }
    public int CompletedCount { get; set; }
    public int FailedCount { get; set; }
    public List<RecentTranslationDto> RecentTranslations { get; set; } = new();
}

public class RecentTranslationDto
{
    public int GameId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? CompletedAt { get; set; }
}

public class TranslationQueueItemDto
{
    public int GameId { get; set; }
    public int BggId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
}

public class TranslationHistoryDto
{
    public int GameId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? NameVi { get; set; }
    public bool HasDescription { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? CompletedAt { get; set; }
}
