using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BoardGameScraper.Api.Services;
using BoardGameScraper.Api.Data;

namespace BoardGameScraper.Api.Controllers;

/// <summary>
/// API Controller for rulebook translation
/// Handles PDF upload and translation to Vietnamese
/// </summary>
[ApiController]
[Route("api/translation")]
public class RulebookController : ControllerBase
{
    private readonly ILogger<RulebookController> _logger;
    private readonly RulebookTranslationService _translationService;
    private readonly BggPdfDownloadService _bggDownloadService;
    private readonly RulebookScraperService _scraperService;
    private readonly BoardGameDbContext _db;
    private readonly IConfiguration _config;

    public RulebookController(
        ILogger<RulebookController> logger,
        RulebookTranslationService translationService,
        BggPdfDownloadService bggDownloadService,
        RulebookScraperService scraperService,
        BoardGameDbContext db,
        IConfiguration config)
    {
        _logger = logger;
        _translationService = translationService;
        _bggDownloadService = bggDownloadService;
        _scraperService = scraperService;
        _db = db;
        _config = config;
    }

    /// <summary>
    /// Upload a PDF rulebook and get Vietnamese translation
    /// </summary>
    /// <param name="file">PDF file to translate</param>
    /// <param name="gameName">Optional game name</param>
    /// <param name="bggId">Optional BGG ID</param>
    /// <returns>Translation result with bilingual markdown</returns>
    [HttpPost("upload")]
    [RequestSizeLimit(52428800)] // 50 MB limit
    public async Task<ActionResult<RulebookTranslationResponse>> UploadRulebook(
        IFormFile file,
        [FromForm] string? gameName = null,
        [FromForm] int? bggId = null)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file uploaded" });
        }

        // Validate file type
        if (!file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) &&
            !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Only PDF files are supported" });
        }

        _logger.LogInformation($"Received PDF upload: {file.FileName} ({file.Length} bytes)");

        try
        {
            // Read file to byte array
            byte[] pdfBytes;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                pdfBytes = ms.ToArray();
            }

            // Process rulebook
            var result = await _translationService.ProcessRulebookAsync(
                pdfBytes,
                file.FileName,
                bggId,
                gameName
            );

            // Return response
            var response = new RulebookTranslationResponse
            {
                Success = result.Success,
                FileName = result.FileName,
                GameName = gameName,
                BggId = bggId,
                
                ExtractedWordCount = result.WordCount,
                ExtractedCharCount = result.CharacterCount,
                
                OriginalText = result.ExtractedText,
                VietnameseText = result.VietnameseText,
                BilingualMarkdown = result.BilingualMarkdown,
                
                OutputFilePath = result.OutputFilePath,
                
                ProcessingTimeSeconds = result.ElapsedSeconds,
                CompletedAt = result.CompletedAt ?? DateTime.UtcNow
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing rulebook upload");
            
            return StatusCode(500, new
            {
                error = "Failed to process rulebook",
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Download PDF from BGG and translate it
    /// </summary>
    /// <param name="request">Request containing BGG file URL and metadata</param>
    /// <returns>Translation result</returns>
    [HttpPost("translate-from-bgg")]
    public async Task<ActionResult<RulebookTranslationResponse>> TranslateFromBgg(
        [FromBody] BggTranslationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest(new { error = "URL is required" });
        }

        if (!_bggDownloadService.IsValidBggUrl(request.Url))
        {
            return BadRequest(new { error = "Invalid BGG URL. Must be from boardgamegeek.com or geekdo-files.com" });
        }

        _logger.LogInformation($"Downloading PDF from BGG: {request.Url}");

        try
        {
            // Step 1: Download PDF from BGG
            var pdfBytes = await _bggDownloadService.DownloadPdfAsync(request.Url, request.BggFileId);
            
            _logger.LogInformation($"Downloaded {pdfBytes.Length / 1024} KB from BGG");

            if (pdfBytes == null || pdfBytes.Length == 0)
            {
                return BadRequest(new { error = "Failed to download PDF from BGG" });
            }

            // Step 2: Extract filename from URL or use provided title
            var fileName = !string.IsNullOrWhiteSpace(request.RulebookTitle)
                ? $"{request.RulebookTitle}.pdf"
                : "rulebook.pdf";

            // Step 3: Process and translate
            var result = await _translationService.ProcessRulebookAsync(
                pdfBytes,
                fileName,
                request.BggId,
                request.GameName
            );

            // Return response
            var response = new RulebookTranslationResponse
            {
                Success = result.Success,
                FileName = result.FileName,
                GameName = request.GameName,
                BggId = request.BggId,

                ExtractedWordCount = result.WordCount,
                ExtractedCharCount = result.CharacterCount,

                OriginalText = result.ExtractedText,
                VietnameseText = result.VietnameseText,
                BilingualMarkdown = result.BilingualMarkdown,

                OutputFilePath = result.OutputFilePath,

                ProcessingTimeSeconds = result.ElapsedSeconds,
                CompletedAt = result.CompletedAt ?? DateTime.UtcNow
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing BGG rulebook");

            return StatusCode(500, new
            {
                error = "Failed to process BGG rulebook",
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Scrape rulebooks from BGG for a game and save to database
    /// </summary>
    [HttpPost("game/{bggId}/scrape-rulebooks")]
    public async Task<ActionResult<object>> ScrapeGameRulebooks(int bggId)
    {
        try
        {
            _logger.LogInformation("Manually triggering rulebook scrape for BGG ID {BggId}", bggId);
            
            // Get game from DB to ensure it exists and get its internal ID
            var game = await _db.Games.FirstOrDefaultAsync(g => g.BggId == bggId);
            if (game == null)
            {
                return NotFound(new { error = $"Game with BGG ID {bggId} not found in database" });
            }

            // Call scraper service
            var scrapedRulebooks = await _scraperService.GetRulebooksForGameAsync(bggId);
            
            if (scrapedRulebooks == null || scrapedRulebooks.Count == 0)
            {
                return Ok(new { 
                    message = "No rulebooks found on BGG for this game", 
                    count = 0 
                });
            }

            int savedCount = 0;
            int updatedCount = 0;
            foreach (var rb in scrapedRulebooks)
            {
                // Check if already exists to avoid duplicates
                var existing = await _db.Rulebooks.FirstOrDefaultAsync(r => 
                    r.GameId == game.Id && r.OriginalUrl == rb.Url);
                
                if (existing == null)
                {
                    var entity = new BoardGameScraper.Api.Data.Entities.Rulebook
                    {
                        GameId = game.Id,
                        Title = rb.Title,
                        OriginalUrl = rb.Url,
                        BggFileId = rb.BggFileId,
                        FileType = rb.FileType,
                        Language = rb.Language ?? "English",
                        Status = "scraped",
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.Rulebooks.Add(entity);
                    savedCount++;
                }
                else if (string.IsNullOrEmpty(existing.BggFileId) && !string.IsNullOrEmpty(rb.BggFileId))
                {
                    existing.BggFileId = rb.BggFileId;
                    updatedCount++;
                }
            }

            if (savedCount > 0 || updatedCount > 0)
            {
                await _db.SaveChangesAsync();
            }

            return Ok(new { 
                message = $"Successfully processed rulebooks from BGG", 
                found = scrapedRulebooks.Count,
                saved = savedCount,
                updated = updatedCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping rulebooks for BGG ID {BggId}", bggId);
            return StatusCode(500, new { error = "Failed to scrape rulebooks from BGG" });
        }
    }

    /// <summary>
    /// Get rulebooks for a game from database
    /// </summary>
    [HttpGet("game/{bggId}/rulebooks")]
    public async Task<ActionResult<List<RulebookInfoDto>>> GetGameRulebooks(int bggId)
    {
        try
        {
            var game = await _db.Games
                .Include(g => g.Rulebooks)
                .FirstOrDefaultAsync(g => g.BggId == bggId);

            if (game == null)
            {
                return NotFound(new { error = $"Game with BGG ID {bggId} not found" });
            }

            var rulebooks = game.Rulebooks
                .Select(r => new RulebookInfoDto
                {
                    Id = r.Id,
                    Title = r.Title,
                    Url = r.OriginalUrl,
                    BggFileId = r.BggFileId,
                    FileType = r.FileType,
                    Language = r.Language,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt
                })
                .ToList();

            return Ok(new
            {
                gameName = game.Name,
                bggId = game.BggId,
                rulebooksCount = rulebooks.Count,
                rulebooks = rulebooks
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting rulebooks for BGG ID {BggId}", bggId);
            return StatusCode(500, new { error = "Failed to get rulebooks" });
        }
    }

    /// <summary>
    /// Get list of games from database for selection
    /// </summary>
    [HttpGet("games")]
    public async Task<ActionResult<List<GameSelectionDto>>> GetGames(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null)
    {
        try
        {
            var query = _db.Games.AsQueryable();

            // Search filter
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(g => g.Name.Contains(search));
            }

            // Order by rank or name
            query = query.OrderBy(g => g.BggRank ?? int.MaxValue)
                        .ThenBy(g => g.Name);

            // Pagination
            var total = await query.CountAsync();
            var games = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(g => new GameSelectionDto
                {
                    Id = g.Id,
                    BggId = g.BggId,
                    Name = g.Name,
                    YearPublished = g.YearPublished,
                    BggRank = g.BggRank,
                    ImageUrl = g.ThumbnailUrl
                })
                .ToListAsync();

            return Ok(new
            {
                total = total,
                page = page,
                pageSize = pageSize,
                games = games
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting games");
            return StatusCode(500, new { error = "Failed to get games" });
        }
    }

    /// <summary>
    /// Get translation statistics
    /// </summary>
    [HttpGet("statistics")]
    public async Task<ActionResult<TranslationStatistics>> GetStatistics()
    {
        var stats = await _translationService.GetStatisticsAsync();
        return Ok(stats);
    }

    /// <summary>
    /// Test endpoint to verify API is working
    /// </summary>
    [HttpGet("health")]
    public ActionResult<object> HealthCheck()
    {
        var geminiConfigured = !string.IsNullOrEmpty(_config["Gemini:ApiKey"]);

        return Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            geminiConfigured = geminiConfigured,
            maxUploadSizeMB = 50
        });
    }
}

/// <summary>
/// Response DTO for rulebook translation
/// </summary>
public class RulebookTranslationResponse
{
    public bool Success { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? GameName { get; set; }
    public int? BggId { get; set; }
    
    public int ExtractedWordCount { get; set; }
    public int ExtractedCharCount { get; set; }
    
    public string OriginalText { get; set; } = string.Empty;
    public string VietnameseText { get; set; } = string.Empty;
    public string BilingualMarkdown { get; set; } = string.Empty;
    
    public string? OutputFilePath { get; set; }
    
    public double ProcessingTimeSeconds { get; set; }
    public DateTime CompletedAt { get; set; }
}

/// <summary>
/// Request DTO for translating from BGG URL
/// </summary>
public class BggTranslationRequest
{
    public string Url { get; set; } = string.Empty;
    public string? GameName { get; set; }
    public int? BggId { get; set; }
    public string? RulebookTitle { get; set; }
    public string? BggFileId { get; set; }
}

/// <summary>
/// DTO for game selection dropdown
/// </summary>
public class GameSelectionDto
{
    public int Id { get; set; }
    public int BggId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? YearPublished { get; set; }
    public int? BggRank { get; set; }
    public string? ImageUrl { get; set; }
}

/// <summary>
/// DTO for rulebook information
/// </summary>
public class RulebookInfoDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? BggFileId { get; set; }
    public string FileType { get; set; } = "pdf";
    public string? Language { get; set; }
    public string Status { get; set; } = "scraped";
    public DateTime CreatedAt { get; set; }
}
