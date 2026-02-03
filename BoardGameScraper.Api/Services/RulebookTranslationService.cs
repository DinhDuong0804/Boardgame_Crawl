using BoardGameScraper.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardGameScraper.Api.Services;

/// <summary>
/// High-level service for rulebook translation workflow
/// Coordinates PDF extraction, translation, and storage
/// </summary>
public class RulebookTranslationService
{
    private readonly ILogger<RulebookTranslationService> _logger;
    private readonly PdfService _pdfService;
    private readonly GeminiTranslatorService _translatorService;
    private readonly BoardGameDbContext _db;
    private readonly IConfiguration _config;

    public RulebookTranslationService(
        ILogger<RulebookTranslationService> logger,
        PdfService pdfService,
        GeminiTranslatorService translatorService,
        BoardGameDbContext db,
        IConfiguration config)
    {
        _logger = logger;
        _pdfService = pdfService;
        _translatorService = translatorService;
        _db = db;
        _config = config;
    }

    /// <summary>
    /// Process a PDF rulebook: Extract text -> Translate -> Save
    /// </summary>
    public async Task<RulebookTranslationResult> ProcessRulebookAsync(
        byte[] pdfBytes,
        string fileName,
        int? bggId = null,
        string? gameName = null)
    {
        var result = new RulebookTranslationResult
        {
            FileName = fileName,
            StartedAt = DateTime.UtcNow
        };

        try
        {
            _logger.LogInformation($"Processing rulebook: {fileName}");

            // Step 1: Save original PDF to disk
            _logger.LogInformation("Step 1/4: Saving PDF to disk...");
            var pdfPath = await SavePdfToFileAsync(pdfBytes, fileName, bggId, gameName);
            result.OutputFilePath = pdfPath; // Temporarily store PDF path, will be updated to MD path later

            // Step 2: Extract text from PDF
            _logger.LogInformation("Step 2/4: Extracting text from PDF...");
            var extraction = _pdfService.ExtractTextWithMetadata(pdfBytes, fileName);
            
            result.ExtractedText = extraction.ExtractedText;
            result.CharacterCount = extraction.CharacterCount;
            result.WordCount = extraction.WordCount;

            _logger.LogInformation($"Extracted {extraction.WordCount} words, {extraction.CharacterCount} characters");

            // Step 3: Translate to Vietnamese
            _logger.LogInformation("Step 3/4: Translating to Vietnamese...");
            var vietnameseText = await _translatorService.TranslateToVietnameseAsync(extraction.ExtractedText);
            
            result.VietnameseText = vietnameseText;

            // Step 4: Create bilingual markdown
            _logger.LogInformation("Step 4/4: Creating bilingual markdown...");
            var rulebookTitle = Path.GetFileNameWithoutExtension(fileName);
            var markdown = await _translatorService.CreateBilingualMarkdownAsync(
                gameName ?? "Board Game",
                rulebookTitle,
                extraction.ExtractedText
            );

            result.BilingualMarkdown = markdown;

            // Save to file system
            var outputPath = await SaveMarkdownToFileAsync(markdown, fileName, bggId, gameName);
            result.OutputFilePath = outputPath;

            result.CompletedAt = DateTime.UtcNow;
            result.Success = true;

            _logger.LogInformation($"Translation completed successfully in {(result.CompletedAt.Value - result.StartedAt).TotalSeconds:F1}s");
            _logger.LogInformation($"Output saved to: {outputPath}");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing rulebook {fileName}");
            
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.CompletedAt = DateTime.UtcNow;

            throw;
        }
    }

    /// <summary>
    /// Save PDF to file system
    /// </summary>
    private async Task<string> SavePdfToFileAsync(
        byte[] pdfBytes,
        string originalFileName,
        int? bggId,
        string? gameName)
    {
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output/pdfs");
        Directory.CreateDirectory(outputDir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var safeFileName = SanitizeFileName(Path.GetFileNameWithoutExtension(originalFileName));
        var safeGameName = gameName != null ? SanitizeFileName(gameName) : "unknown";
        
        var fileName = bggId.HasValue
            ? $"{bggId}_{safeGameName}_{safeFileName}_{timestamp}.pdf"
            : $"{safeFileName}_{timestamp}.pdf";

        var filePath = Path.Combine(outputDir, fileName);
        await File.WriteAllBytesAsync(filePath, pdfBytes);

        _logger.LogInformation($"Saved PDF rulebook to: {filePath}");
        return filePath;
    }

    /// <summary>
    /// Save markdown to file system
    /// </summary>
    private async Task<string> SaveMarkdownToFileAsync(
        string markdown, 
        string originalFileName, 
        int? bggId, 
        string? gameName)
    {
        // Create output directory
        var baseDir = _config["Translation:OutputDirectory"] ?? "output/rulebooks_vi";
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), baseDir);
        Directory.CreateDirectory(outputDir);

        // Create filename
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var safeFileName = SanitizeFileName(Path.GetFileNameWithoutExtension(originalFileName));
        var safeGameName = gameName != null ? SanitizeFileName(gameName) : "unknown";
        
        var fileName = bggId.HasValue
            ? $"{bggId}_{safeGameName}_{safeFileName}_{timestamp}.md"
            : $"{safeFileName}_{timestamp}.md";

        var filePath = Path.Combine(outputDir, fileName);

        // Save file
        await File.WriteAllTextAsync(filePath, markdown);

        _logger.LogInformation($"Saved markdown to: {filePath}");

        return filePath;
    }

    /// <summary>
    /// Sanitize filename to remove invalid characters
    /// </summary>
    private string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        
        // Also replace spaces with underscores and convert to lowercase
        sanitized = sanitized.Replace(" ", "_").ToLowerInvariant();
        
        // Limit length
        if (sanitized.Length > 50)
            sanitized = sanitized.Substring(0, 50);

        return sanitized;
    }

    /// <summary>
    /// Get translation statistics
    /// </summary>
    public async Task<TranslationStatistics> GetStatisticsAsync()
    {
        var stats = new TranslationStatistics();

        // Get counts from database
        stats.TotalGames = await _db.Games.CountAsync();
        stats.GamesWithRulebooks = await _db.Games.Include(g => g.Rulebooks).CountAsync(g => g.Rulebooks.Any());
        stats.TranslatedGames = await _db.GameTranslations.CountAsync(t => t.Status == "completed");
        stats.FailedTranslations = await _db.GameTranslations.CountAsync(t => t.Status == "failed");
        stats.PendingTranslations = await _db.Games.CountAsync(g => g.Status == "pending_translation");

        return stats;
    }
}

/// <summary>
/// Result of rulebook translation
/// </summary>
public class RulebookTranslationResult
{
    public string FileName { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    
    public string ExtractedText { get; set; } = string.Empty;
    public string VietnameseText { get; set; } = string.Empty;
    public string BilingualMarkdown { get; set; } = string.Empty;
    public string? OutputFilePath { get; set; }
    
    public int CharacterCount { get; set; }
    public int WordCount { get; set; }
    
    public double ElapsedSeconds => (CompletedAt ?? DateTime.UtcNow).Subtract(StartedAt).TotalSeconds;
}

/// <summary>
/// Translation statistics
/// </summary>
public class TranslationStatistics
{
    public int TotalGames { get; set; }
    public int GamesWithRulebooks { get; set; }
    public int TranslatedGames { get; set; }
    public int FailedTranslations { get; set; }
    public int PendingTranslations { get; set; }
}
