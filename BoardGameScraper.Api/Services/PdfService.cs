using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System.Text;

namespace BoardGameScraper.Api.Services;

/// <summary>
/// Service to extract text from PDF files using iText7
/// Properly handles multi-column layouts common in board game rulebooks
/// </summary>
public class PdfService
{
    private readonly ILogger<PdfService> _logger;

    public PdfService(ILogger<PdfService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extract text from a PDF file (binary content)
    /// </summary>
    /// <param name="pdfBytes">The PDF file content as byte array</param>
    /// <returns>Extracted text in markdown-friendly format</returns>
    public string ExtractTextFromPdf(byte[] pdfBytes)
    {
        _logger.LogInformation("Starting PDF text extraction...");
        
        try
        {
            var sb = new StringBuilder();
            
            using (var ms = new MemoryStream(pdfBytes))
            using (var reader = new PdfReader(ms))
            using (var document = new PdfDocument(reader))
            {
                int numberOfPages = document.GetNumberOfPages();
                _logger.LogInformation($"PDF has {numberOfPages} pages");
                
                for (int i = 1; i <= numberOfPages; i++)
                {
                    var page = document.GetPage(i);
                    
                    // Use LocationTextExtractionStrategy for better column handling
                    var strategy = new LocationTextExtractionStrategy();
                    var text = PdfTextExtractor.GetTextFromPage(page, strategy);
                    
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        // Add page separator for better readability
                        sb.AppendLine($"\n--- Page {i} ---\n");
                        sb.AppendLine(text);
                    }
                    
                    _logger.LogInformation($"Extracted {text.Length} characters from page {i}");
                }
            }
            
            var extractedText = sb.ToString();
            
            // Clean up the text
            extractedText = CleanExtractedText(extractedText);
            
            _logger.LogInformation($"Total extracted: {extractedText.Length} characters");
            
            return extractedText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from PDF");
            throw new InvalidOperationException("Failed to extract text from PDF", ex);
        }
    }

    /// <summary>
    /// Clean and normalize extracted text
    /// </summary>
    private string CleanExtractedText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Remove excessive whitespace
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\r\n|\r|\n", "\n");
        
        // Replace multiple spaces with single space
        text = System.Text.RegularExpressions.Regex.Replace(text, @" {2,}", " ");
        
        // Remove multiple blank lines (keep max 2)
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");
        
        return text.Trim();
    }

    /// <summary>
    /// Extract text from PDF and return as structured format
    /// </summary>
    public PdfExtractionResult ExtractTextWithMetadata(byte[] pdfBytes, string fileName)
    {
        var text = ExtractTextFromPdf(pdfBytes);
        
        return new PdfExtractionResult
        {
            FileName = fileName,
            ExtractedText = text,
            CharacterCount = text.Length,
            WordCount = EstimateWordCount(text),
            ExtractedAt = DateTime.UtcNow
        };
    }

    private int EstimateWordCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}

/// <summary>
/// Result of PDF text extraction
/// </summary>
public class PdfExtractionResult
{
    public string FileName { get; set; } = string.Empty;
    public string ExtractedText { get; set; } = string.Empty;
    public int CharacterCount { get; set; }
    public int WordCount { get; set; }
    public DateTime ExtractedAt { get; set; }
}
