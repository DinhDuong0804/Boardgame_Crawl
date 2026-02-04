using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BoardGameScraper.Api.Services;
using BoardGameScraper.Api.Data;

namespace BoardGameScraper.Api.Controllers;

/// <summary>
/// API Controller for rulebook management
/// Handles scraping and downloading rulebooks
/// </summary>
[ApiController]
[Route("api/rulebooks")]
public class RulebookController : ControllerBase
{
    private readonly ILogger<RulebookController> _logger;
    private readonly BggPdfDownloadService _bggDownloadService;
    private readonly RulebookScraperService _scraperService;
    private readonly BoardGameDbContext _db;
    private readonly IConfiguration _config;

    public RulebookController(
        ILogger<RulebookController> logger,
        BggPdfDownloadService bggDownloadService,
        RulebookScraperService scraperService,
        BoardGameDbContext db,
        IConfiguration config)
    {
        _logger = logger;
        _bggDownloadService = bggDownloadService;
        _scraperService = scraperService;
        _db = db;
        _config = config;
    }

    /// <summary>
    /// Download PDF from BGG and save to local disk
    /// </summary>
    [HttpPost("download-bgg")]
    public async Task<ActionResult<object>> DownloadFromBgg([FromBody] BggDownloadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest(new { error = "URL is required" });
        }

        if (!_bggDownloadService.IsValidBggUrl(request.Url))
        {
            return BadRequest(new { error = "Invalid BGG URL." });
        }

        try
        {
            var pdfBytes = await _bggDownloadService.DownloadPdfAsync(request.Url, request.BggFileId);

            if (pdfBytes == null || pdfBytes.Length == 0)
            {
                return BadRequest(new { error = "Failed to download PDF" });
            }

            var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output/pdfs");
            Directory.CreateDirectory(outputDir);

            var fileName = !string.IsNullOrWhiteSpace(request.RulebookTitle)
                ? $"{request.BggId}_{request.RulebookTitle}.pdf"
                : $"{request.BggId}_rulebook_{DateTime.Now.Ticks}.pdf";

            // Sanitize filename
            fileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));

            var filePath = Path.Combine(outputDir, fileName);
            await System.IO.File.WriteAllBytesAsync(filePath, pdfBytes);

            // Update database
            if (request.BggId.HasValue)
            {
                var game = await _db.Games.FirstOrDefaultAsync(g => g.BggId == request.BggId);
                if (game != null)
                {
                    var rulebook = await _db.Rulebooks.FirstOrDefaultAsync(r => r.GameId == game.Id && r.OriginalUrl == request.Url);
                    if (rulebook != null)
                    {
                        rulebook.LocalFilePath = Path.Combine("output/pdfs", fileName);
                        rulebook.Status = "downloaded";
                        await _db.SaveChangesAsync();
                    }
                }
            }

            return Ok(new { success = true, fileName });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading rulebook");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Download a processed rulebook (forces download)
    /// </summary>
    [HttpGet("download/{fileName}")]
    public IActionResult DownloadRulebookFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return BadRequest();

        var safeFileName = Uri.UnescapeDataString(Path.GetFileName(fileName));
        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "output", "pdfs", safeFileName);

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound();
        }

        var contentType = "application/pdf";
        var bytes = System.IO.File.ReadAllBytes(filePath);
        return File(bytes, contentType, safeFileName);
    }

    /// <summary>
    /// View a rulebook PDF inline (without forcing download)
    /// </summary>
    [HttpGet("view/{fileName}")]
    public IActionResult ViewRulebookFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return BadRequest();

        var safeFileName = Uri.UnescapeDataString(Path.GetFileName(fileName));
        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "output", "pdfs", safeFileName);

        _logger.LogInformation("Attempting to view PDF: {FilePath}", filePath);

        if (!System.IO.File.Exists(filePath))
        {
            _logger.LogWarning("PDF file not found: {FilePath}", filePath);
            return NotFound(new { error = "File not found", path = safeFileName });
        }

        return PhysicalFile(filePath, "application/pdf");
    }

    /// <summary>
    /// Scrape rulebooks from BGG for a game and save to database
    /// </summary>
    [HttpPost("game/{bggId}/scrape")]
    public async Task<ActionResult<object>> ScrapeGameRulebooks(int bggId)
    {
        try
        {
            var game = await _db.Games.FirstOrDefaultAsync(g => g.BggId == bggId);
            if (game == null)
            {
                return NotFound(new { error = $"Game with BGG ID {bggId} not found" });
            }

            var scrapedRulebooks = await _scraperService.GetRulebooksForGameAsync(bggId);

            if (scrapedRulebooks == null || scrapedRulebooks.Count == 0)
            {
                return Ok(new { message = "No rulebooks found", count = 0 });
            }

            int savedCount = 0;
            foreach (var rb in scrapedRulebooks)
            {
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
            }

            if (savedCount > 0)
            {
                await _db.SaveChangesAsync();
            }

            return Ok(new
            {
                message = $"Successfully processed rulebooks",
                found = scrapedRulebooks.Count,
                saved = savedCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping rulebooks");
            return StatusCode(500, new { error = "Failed to scrape rulebooks" });
        }
    }

    /// <summary>
    /// Get rulebooks for a game from database
    /// </summary>
    [HttpGet("game/{bggId}")]
    public async Task<ActionResult<object>> GetGameRulebooks(int bggId)
    {
        try
        {
            var game = await _db.Games
                .Include(g => g.Rulebooks)
                .FirstOrDefaultAsync(g => g.BggId == bggId);

            if (game == null)
            {
                return NotFound(new { error = "Game not found" });
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
                    LocalFileName = !string.IsNullOrEmpty(r.LocalFilePath) ? Path.GetFileName(r.LocalFilePath) : null,
                    CreatedAt = r.CreatedAt
                })
                .ToList();

            return Ok(new
            {
                gameName = game.Name,
                bggId = game.BggId,
                rulebooks = rulebooks
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting rulebooks");
            return StatusCode(500, new { error = "Failed to get rulebooks" });
        }
    }
}

public class BggDownloadRequest
{
    public string Url { get; set; } = string.Empty;
    public string? GameName { get; set; }
    public int? BggId { get; set; }
    public string? RulebookTitle { get; set; }
    public string? BggFileId { get; set; }
}

public class RulebookInfoDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? BggFileId { get; set; }
    public string FileType { get; set; } = "pdf";
    public string? Language { get; set; }
    public string? LocalFileName { get; set; }
    public string Status { get; set; } = "scraped";
    public DateTime CreatedAt { get; set; }
}
