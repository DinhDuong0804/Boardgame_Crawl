using Microsoft.AspNetCore.Mvc;

namespace BoardGameScraper.Api.Controllers;

/// <summary>
/// Controller để phục vụ rulebooks đã dịch
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class RulebooksController : ControllerBase
{
    private readonly ILogger<RulebooksController> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly string _rulebooksPath;
    
    public RulebooksController(
        ILogger<RulebooksController> logger,
        IWebHostEnvironment env,
        IConfiguration config)
    {
        _logger = logger;
        _env = env;
        
        // Path to translated rulebooks (from Python translation service)
        _rulebooksPath = config["Translation:RulebooksPath"] 
            ?? Path.Combine(Directory.GetParent(_env.ContentRootPath)!.FullName, 
                           "translation-service", "output", "rulebooks_vi");
    }
    
    /// <summary>
    /// Get list of translated rulebooks available
    /// </summary>
    [HttpGet("translated")]
    public ActionResult<List<RulebookFileInfo>> GetTranslatedRulebooks()
    {
        try
        {
            if (!Directory.Exists(_rulebooksPath))
            {
                _logger.LogWarning("Rulebooks directory not found: {Path}", _rulebooksPath);
                return Ok(new List<RulebookFileInfo>());
            }
            
            var files = Directory.GetFiles(_rulebooksPath, "*.md")
                .Select(f => {
                    var fileName = Path.GetFileName(f);
                    var fileInfo = new FileInfo(f);
                    
                    // Parse filename format: {bggId}_{game_name}_{rulebook_title}.md
                    var parts = Path.GetFileNameWithoutExtension(fileName).Split('_', 3);
                    
                    return new RulebookFileInfo
                    {
                        FileName = fileName,
                        Path = fileName,
                        BggId = int.TryParse(parts.FirstOrDefault(), out var id) ? id : 0,
                        GameName = parts.Length > 1 ? FormatName(parts[1]) : "Unknown",
                        Title = parts.Length > 2 ? FormatName(parts[2]) : "Rulebook",
                        FileSize = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTimeUtc
                    };
                })
                .OrderByDescending(f => f.LastModified)
                .ToList();
            
            return Ok(files);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing rulebooks");
            return StatusCode(500, new { message = "Error listing rulebooks" });
        }
    }
    
    /// <summary>
    /// Get content of a specific translated rulebook
    /// </summary>
    [HttpGet("content/{fileName}")]
    public async Task<IActionResult> GetRulebookContent(string fileName)
    {
        try
        {
            // Sanitize filename to prevent path traversal
            fileName = Path.GetFileName(fileName);
            
            if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".md"))
            {
                return BadRequest(new { message = "Invalid file name" });
            }
            
            var filePath = Path.Combine(_rulebooksPath, fileName);
            
            if (!System.IO.File.Exists(filePath))
            {
                return NotFound(new { message = "Rulebook not found" });
            }
            
            var content = await System.IO.File.ReadAllTextAsync(filePath);
            
            return Content(content, "text/markdown; charset=utf-8");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading rulebook: {FileName}", fileName);
            return StatusCode(500, new { message = "Error reading rulebook" });
        }
    }
    
    /// <summary>
    /// Download a rulebook as file
    /// </summary>
    [HttpGet("download/{fileName}")]
    public IActionResult DownloadRulebook(string fileName)
    {
        try
        {
            fileName = Path.GetFileName(fileName);
            
            if (string.IsNullOrEmpty(fileName))
            {
                return BadRequest(new { message = "Invalid file name" });
            }
            
            var filePath = Path.Combine(_rulebooksPath, fileName);
            
            if (!System.IO.File.Exists(filePath))
            {
                return NotFound(new { message = "Rulebook not found" });
            }
            
            var contentType = "text/markdown";
            return PhysicalFile(filePath, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading rulebook: {FileName}", fileName);
            return StatusCode(500, new { message = "Error downloading rulebook" });
        }
    }
    
    /// <summary>
    /// Search rulebooks by game name or BGG ID
    /// </summary>
    [HttpGet("search")]
    public ActionResult<List<RulebookFileInfo>> SearchRulebooks(
        [FromQuery] string? query = null,
        [FromQuery] int? bggId = null)
    {
        try
        {
            if (!Directory.Exists(_rulebooksPath))
            {
                return Ok(new List<RulebookFileInfo>());
            }
            
            var files = Directory.GetFiles(_rulebooksPath, "*.md")
                .Select(f => {
                    var fileName = Path.GetFileName(f);
                    var parts = Path.GetFileNameWithoutExtension(fileName).Split('_', 3);
                    
                    return new RulebookFileInfo
                    {
                        FileName = fileName,
                        Path = fileName,
                        BggId = int.TryParse(parts.FirstOrDefault(), out var id) ? id : 0,
                        GameName = parts.Length > 1 ? FormatName(parts[1]) : "Unknown",
                        Title = parts.Length > 2 ? FormatName(parts[2]) : "Rulebook",
                        FileSize = new FileInfo(f).Length,
                        LastModified = new FileInfo(f).LastWriteTimeUtc
                    };
                });
            
            // Apply filters
            if (bggId.HasValue)
            {
                files = files.Where(f => f.BggId == bggId.Value);
            }
            
            if (!string.IsNullOrEmpty(query))
            {
                var lowerQuery = query.ToLower();
                files = files.Where(f => 
                    f.GameName.ToLower().Contains(lowerQuery) ||
                    f.Title.ToLower().Contains(lowerQuery));
            }
            
            return Ok(files.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching rulebooks");
            return StatusCode(500, new { message = "Error searching rulebooks" });
        }
    }
    
    /// <summary>
    /// Convert underscore_name to Title Case
    /// </summary>
    private static string FormatName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        
        // Replace underscores with spaces and title case
        var words = name.Replace('_', ' ').Split(' ');
        return string.Join(" ", words.Select(w => 
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..].ToLower() : w));
    }
}

/// <summary>
/// DTO for rulebook file information
/// </summary>
public class RulebookFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int BggId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime LastModified { get; set; }
}
