using Mscc.GenerativeAI;
using System.Text;

namespace BoardGameScraper.Api.Services;

/// <summary>
/// Translation service using Google Gemini API
/// Handles text chunking and rate limiting
/// </summary>
public class GeminiTranslatorService
{
    private readonly ILogger<GeminiTranslatorService> _logger;
    private readonly IConfiguration _config;
    private readonly string _apiKey;
    private GoogleAI? _googleAI;
    private GenerativeModel? _model;

    // Chunking configuration
    private const int MaxChunkSize = 4500; // Characters per chunk (safe for Gemini)
    private const int RateLimitDelayMs = 2000; // 2 seconds between API calls

    public GeminiTranslatorService(
        ILogger<GeminiTranslatorService> logger,
        IConfiguration config)
    {
        _logger = logger;
        _config = config;
        _apiKey = _config["Gemini:ApiKey"] ?? throw new ArgumentException("Gemini:ApiKey not configured");
    }

    /// <summary>
    /// Initialize the Gemini model
    /// </summary>
    private async Task EnsureModelInitializedAsync()
    {
        if (_model == null)
        {
            _googleAI = new GoogleAI(apiKey: _apiKey);
            
            // Sử dụng gemini-1.5-flash theo khuyến nghị của bạn (nhanh và ổn định)
            string modelName = _config["Gemini:Model"] ?? "gemini-1.5-flash";
            
            _logger.LogInformation($"Initializing Gemini model: {modelName}...");
            _model = _googleAI.GenerativeModel(model: modelName);
            
            _logger.LogInformation("Gemini model initialized successfully");
            
            // In danh sách model khả dụng để debug (chỉ chạy 1 lần khi init)
            await ListAvailableModelsAsync();
        }
    }

    private async Task ListAvailableModelsAsync()
    {
        try {
            using var client = new HttpClient();
            string url = $"https://generativelanguage.googleapis.com/v1beta/models?key={_apiKey}";
            var response = await client.GetAsync(url);
            string content = await response.Content.ReadAsStringAsync();
            _logger.LogInformation($"Available Gemini Models List: {content.Substring(0, Math.Min(content.Length, 500))}...");
        } catch (Exception ex) {
            _logger.LogWarning($"Could not list models: {ex.Message}");
        }
    }

    /// <summary>
    /// Translate English text to Vietnamese
    /// </summary>
    /// <param name="englishText">Text to translate</param>
    /// <returns>Vietnamese translation</returns>
    public async Task<string> TranslateToVietnameseAsync(string englishText)
    {
        if (string.IsNullOrWhiteSpace(englishText))
        {
            _logger.LogWarning("Empty text provided for translation");
            return string.Empty;
        }

        await EnsureModelInitializedAsync();

        _logger.LogInformation($"Starting translation of {englishText.Length} characters");

        // Split into chunks
        var chunks = SplitTextIntoChunks(englishText, MaxChunkSize);
        _logger.LogInformation($"Split text into {chunks.Count} chunks");

        var translatedChunks = new List<string>();

        int chunkNumber = 1; // Initialize chunkNumber for the foreach loop
        foreach (var chunk in chunks)
        {
            try
            {
                _logger.LogInformation($"Translating chunk {chunkNumber}/{chunks.Count} ({chunk.Length} chars)...");
                var translatedChunk = await TranslateChunkAsync(chunk, chunkNumber, chunks.Count);
                translatedChunks.Add(translatedChunk);

                // Tránh lỗi 429: Nghỉ 2 giây giữa các chunk theo khuyến nghị của bạn
                if (chunkNumber < chunks.Count) // Only delay if it's not the last chunk
                {
                    _logger.LogInformation($"Waiting {RateLimitDelayMs}ms to avoid rate limits...");
                    await Task.Delay(RateLimitDelayMs);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error translating chunk {chunkNumber}/{chunks.Count}");
                translatedChunks.Add($"\n[LỖI DỊCH THUẬT - Chunk {chunkNumber}: {ex.Message}]\n");
            }
            chunkNumber++;
        }

        var result = string.Join("\n\n", translatedChunks);
        _logger.LogInformation($"Translation completed: {result.Length} characters");

        return result;
    }

    /// <summary>
    /// Translate a single chunk of text
    /// </summary>
    private async Task<string> TranslateChunkAsync(string chunk, int chunkNumber, int totalChunks)
    {
        var prompt = BuildTranslationPrompt(chunk, chunkNumber, totalChunks);

        try 
        {
            var response = await _model!.GenerateContent(prompt);

            if (string.IsNullOrWhiteSpace(response.Text))
            {
                _logger.LogWarning($"Empty response from Gemini for chunk {chunkNumber}");
                return $"[LỖI: Gemini trả về nội dung trống cho đoạn {chunkNumber}]";
            }

            _logger.LogInformation($"Received {response.Text.Length} characters for chunk {chunkNumber}");
            return response.Text.Trim();
        }
        catch (Exception ex) when (ex.Message.Contains("404") || ex.Message.Contains("NOT_FOUND"))
        {
            _logger.LogWarning($"Model not found (404). Trying gemini-1.5-flash...");
            
            try {
                var fallbackModel = _googleAI!.GenerativeModel(model: "gemini-1.5-flash");
                var response = await fallbackModel.GenerateContent(prompt);
                return response.Text?.Trim() ?? "[LỖI: Fallback failed]";
            }
            catch (Exception) {
                return $"[LỖI: Gemini API 404 - Model không tồn tại]";
            }
        }
    }

    /// <summary>
    /// Build the translation prompt with specific instructions
    /// </summary>
    private string BuildTranslationPrompt(string text, int chunkNumber, int totalChunks)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("BẠN LÀ MỘT DỊCH GIẢ CHUYÊN NGHIỆP VỀ BOARD GAME.");
        sb.AppendLine();
        sb.AppendLine("NHIỆM VỤ: Dịch văn bản dưới đây từ tiếng Anh sang tiếng Việt.");
        sb.AppendLine();
        sb.AppendLine("QUY TẮC QUAN TRỌNG:");
        sb.AppendLine("1. Output MUST BE STRICTLY in Vietnamese (100% tiếng Việt)");
        sb.AppendLine("2. GIỮ NGUYÊN các thuật ngữ Board Game: Round, Era, Phase, Turn, Token, Meeple, Worker, Resource, Action, VP (Victory Points), Link, Network, Build, Develop, Tile, Card, Marker, Track");
        sb.AppendLine("3. Dịch tự nhiên, dễ hiểu, phù hợp với người chơi Việt Nam");
        sb.AppendLine("4. Giữ nguyên format markdown (nếu có): headers (#), lists (-, *), bold (**), italic (_)");
        sb.AppendLine("5. Giữ nguyên số, tên riêng, tên game");
        sb.AppendLine();
        
        if (totalChunks > 1)
        {
            sb.AppendLine($"ĐÂY LÀ ĐOẠN {chunkNumber}/{totalChunks} - Hãy dịch liền mạch, tự nhiên.");
            sb.AppendLine();
        }
        
        sb.AppendLine("--- VĂN BẢN CẦN DỊCH (TIẾNG ANH) ---");
        sb.AppendLine(text);
        sb.AppendLine();
        sb.AppendLine("--- BẢN DỊCH (CHỈ TIẾNG VIỆT) ---");
        
        return sb.ToString();
    }

    /// <summary>
    /// Split text into chunks for translation
    /// Strategy: Try to split by paragraphs, then by sentences, then by character limit
    /// </summary>
    private List<string> SplitTextIntoChunks(string text, int maxChunkSize)
    {
        if (text.Length <= maxChunkSize)
        {
            return new List<string> { text };
        }

        var chunks = new List<string>();

        // Try splitting by double newline (paragraphs) first
        var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.None);
        
        var currentChunk = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            // If adding this paragraph would exceed the limit
            if (currentChunk.Length + paragraph.Length + 2 > maxChunkSize)
            {
                // Save current chunk if it has content
                if (currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                }

                // If the paragraph itself is too long, split it further
                if (paragraph.Length > maxChunkSize)
                {
                    var subChunks = SplitLongParagraph(paragraph, maxChunkSize);
                    chunks.AddRange(subChunks);
                }
                else
                {
                    currentChunk.Append(paragraph);
                    currentChunk.Append("\n\n");
                }
            }
            else
            {
                currentChunk.Append(paragraph);
                currentChunk.Append("\n\n");
            }
        }

        // Add the last chunk
        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString().Trim());
        }

        return chunks;
    }

    /// <summary>
    /// Split a long paragraph by sentences or character limit
    /// </summary>
    private List<string> SplitLongParagraph(string paragraph, int maxChunkSize)
    {
        var chunks = new List<string>();

        // Try splitting by sentences (. followed by space or newline)
        var sentences = System.Text.RegularExpressions.Regex.Split(paragraph, @"(?<=\.)\s+");
        
        var currentChunk = new StringBuilder();

        foreach (var sentence in sentences)
        {
            if (currentChunk.Length + sentence.Length + 1 > maxChunkSize)
            {
                if (currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                }

                // If a single sentence is too long, split by character limit
                if (sentence.Length > maxChunkSize)
                {
                    for (int i = 0; i < sentence.Length; i += maxChunkSize)
                    {
                        chunks.Add(sentence.Substring(i, Math.Min(maxChunkSize, sentence.Length - i)));
                    }
                }
                else
                {
                    currentChunk.Append(sentence);
                    currentChunk.Append(" ");
                }
            }
            else
            {
                currentChunk.Append(sentence);
                currentChunk.Append(" ");
            }
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString().Trim());
        }

        return chunks;
    }

    /// <summary>
    /// Translate and create bilingual markdown
    /// </summary>
    public async Task<string> CreateBilingualMarkdownAsync(
        string gameName, 
        string rulebookTitle, 
        string englishText)
    {
        var vietnameseText = await TranslateToVietnameseAsync(englishText);

        var sb = new StringBuilder();
        
        sb.AppendLine($"# {gameName} - {rulebookTitle}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 🇬🇧 ENGLISH VERSION");
        sb.AppendLine();
        sb.AppendLine(englishText);
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 🇻🇳 BẢN DỊCH TIẾNG VIỆT");
        sb.AppendLine();
        sb.AppendLine(vietnameseText);
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"*Dịch bởi Google Gemini 1.5 Pro - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");
        
        return sb.ToString();
    }
}
