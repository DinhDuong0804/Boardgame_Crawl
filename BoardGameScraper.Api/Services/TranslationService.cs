using System.Text;
using System.Text.Json;

namespace BoardGameScraper.Api.Services;

/// <summary>
/// Service để dịch nội dung game sang tiếng Việt
/// Hỗ trợ nhiều translation backends: Google, DeepL, LibreTranslate, hoặc LLM
/// </summary>
public class TranslationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TranslationService> _logger;
    private readonly IConfiguration _config;

    // Translation provider options
    private readonly string _provider;
    private readonly string _apiKey;

    public TranslationService(
        HttpClient httpClient,
        ILogger<TranslationService> logger,
        IConfiguration config)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config;

        _provider = config["Translation:Provider"] ?? "libretranslate";
        _apiKey = config["Translation:ApiKey"] ?? "";
    }

    /// <summary>
    /// Dịch text từ tiếng Anh sang tiếng Việt
    /// </summary>
    public async Task<string?> TranslateToVietnameseAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        try
        {
            return _provider.ToLowerInvariant() switch
            {
                "google" => await TranslateWithGoogleAsync(text, ct),
                "deepl" => await TranslateWithDeepLAsync(text, ct),
                "libretranslate" => await TranslateWithLibreTranslateAsync(text, ct),
                "openai" => await TranslateWithOpenAIAsync(text, ct),
                _ => await TranslateWithLibreTranslateAsync(text, ct)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Translation failed for text: {TextPreview}...",
                text.Length > 50 ? text[..50] : text);
            return null;
        }
    }

    /// <summary>
    /// Dịch tất cả fields của game sang tiếng Việt
    /// Trả về bản copy đã dịch
    /// </summary>
    public async Task<TranslatedGameData> TranslateGameDataAsync(
        dynamic gameData,
        CancellationToken ct = default)
    {
        var translated = new TranslatedGameData
        {
            BggId = gameData.bgg_id,
            OriginalName = gameData.name,
            OriginalDescription = gameData.description
        };

        // Dịch tên game
        translated.VietnameseName = await TranslateToVietnameseAsync(gameData.name, ct);

        // Dịch mô tả
        if (!string.IsNullOrEmpty(gameData.description))
        {
            translated.VietnameseDescription = await TranslateToVietnameseAsync(gameData.description, ct);
        }

        return translated;
    }

    #region Translation Providers

    /// <summary>
    /// LibreTranslate - Free, self-hosted option
    /// Public instance: https://libretranslate.com
    /// </summary>
    private async Task<string?> TranslateWithLibreTranslateAsync(string text, CancellationToken ct)
    {
        var endpoint = _config["Translation:LibreTranslate:Endpoint"] ?? "https://libretranslate.com/translate";

        var request = new
        {
            q = text,
            source = "en",
            target = "vi",
            format = "text",
            api_key = _apiKey
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync(endpoint, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("LibreTranslate request failed: {Status}", response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("translatedText", out var translated)
            ? translated.GetString()
            : null;
    }

    /// <summary>
    /// Google Cloud Translation API
    /// Requires API key from Google Cloud Console
    /// </summary>
    private async Task<string?> TranslateWithGoogleAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("Google Translate API key not configured");
            return null;
        }

        var url = $"https://translation.googleapis.com/language/translate/v2?key={_apiKey}";

        var request = new
        {
            q = text,
            source = "en",
            target = "vi",
            format = "text"
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync(url, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Google Translate failed: {Status}", response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.TryGetProperty("translations", out var translations) &&
            translations.GetArrayLength() > 0)
        {
            return translations[0].GetProperty("translatedText").GetString();
        }

        return null;
    }

    /// <summary>
    /// DeepL API - High quality translation
    /// Requires API key from DeepL
    /// </summary>
    private async Task<string?> TranslateWithDeepLAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("DeepL API key not configured");
            return null;
        }

        var url = "https://api-free.deepl.com/v2/translate";

        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("auth_key", _apiKey),
            new KeyValuePair<string, string>("text", text),
            new KeyValuePair<string, string>("source_lang", "EN"),
            new KeyValuePair<string, string>("target_lang", "VI")
        });

        var response = await _httpClient.PostAsync(url, formData, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("DeepL translate failed: {Status}", response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("translations", out var translations) &&
            translations.GetArrayLength() > 0)
        {
            return translations[0].GetProperty("text").GetString();
        }

        return null;
    }

    /// <summary>
    /// OpenAI GPT API - Good for contextual translation
    /// Better for game terminology and rulebook content
    /// </summary>
    private async Task<string?> TranslateWithOpenAIAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("OpenAI API key not configured");
            return null;
        }

        var url = "https://api.openai.com/v1/chat/completions";

        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

        var request = new
        {
            model = "gpt-3.5-turbo",
            messages = new[]
            {
                new { role = "system", content = "Bạn là một dịch giả chuyên nghiệp về board game. Hãy dịch văn bản sau từ tiếng Anh sang tiếng Việt, giữ nguyên thuật ngữ board game phổ biến nếu cần." },
                new { role = "user", content = text }
            },
            temperature = 0.3,
            max_tokens = 4000
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync(url, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("OpenAI translate failed: {Status}", response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("choices", out var choices) &&
            choices.GetArrayLength() > 0)
        {
            return choices[0].GetProperty("message").GetProperty("content").GetString();
        }

        return null;
    }

    #endregion
}

/// <summary>
/// Model chứa dữ liệu game đã dịch
/// </summary>
public class TranslatedGameData
{
    public int BggId { get; set; }

    // Tên game
    public string? OriginalName { get; set; }
    public string? VietnameseName { get; set; }

    // Mô tả
    public string? OriginalDescription { get; set; }
    public string? VietnameseDescription { get; set; }

    // Categories, Mechanics có thể thêm sau
    public List<string>? VietnameseCategories { get; set; }
    public List<string>? VietnameseMechanics { get; set; }
}
