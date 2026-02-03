using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace BoardGameScraper.Api.Services;

public class BggPlaywrightService
{
    private readonly ILogger<BggPlaywrightService> _logger;
    private readonly IConfiguration _config;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;

    public BggPlaywrightService(ILogger<BggPlaywrightService> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    private async Task EnsureInitializedAsync()
    {
        if (_playwright == null)
        {
            _logger.LogInformation("Initializing Playwright...");
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false // Tắt chế độ chạy ngầm để USER có thể quan sát
            });
            _context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            });
        }
    }

    public async Task<bool> LoginAsync()
    {
        await EnsureInitializedAsync();

        var username = _config["BggApi:Username"];
        var password = _config["BggApi:Password"];

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            _logger.LogWarning("BGG credentials not found in configuration.");
            return false;
        }

        var page = await _context!.NewPageAsync();
        try
        {
            _logger.LogInformation($"Checking BGG login status...");
            await page.GotoAsync("https://boardgamegeek.com/login", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });

            // Kiểm tra nhiều dấu hiệu đã đăng nhập (Sign Out, hoặc có menu user)
            var isLoggedIn = await page.Locator("text=Sign Out").IsVisibleAsync() || 
                             await page.Locator(".menu-login-username").IsVisibleAsync() ||
                             await page.Locator("a[href*='/logout']").IsVisibleAsync();

            if (isLoggedIn)
            {
                _logger.LogInformation("Already logged in to BGG.");
                return true;
            }

            _logger.LogInformation("Not logged in. Finding login fields...");
            
            // Đợi ô username xuất hiện (nếu không thấy sau 5s thì có thể giao diện khác)
            try {
                await page.WaitForSelectorAsync("input[name='username']", new PageWaitForSelectorOptions { Timeout = 5000 });
            } catch {
                _logger.LogWarning("Username field not found immediately. Checking if we're already on home page...");
                if ((await page.ContentAsync()).Contains("Sign Out")) return true;
            }

            await page.FillAsync("input[name='username']", username);
            await page.FillAsync("input[name='password']", password);
            
            _logger.LogInformation("Submitting login form...");
            await page.ClickAsync("button[type='submit'], .btn-primary");
            
            // Wait for navigation or a sign that we are logged in (faster check)
            try 
            {
                await page.WaitForSelectorAsync("text=Sign Out", new PageWaitForSelectorOptions { Timeout = 10000, State = WaitForSelectorState.Attached });
                _logger.LogInformation("Login verified: 'Sign Out' found.");
                return true;
            }
            catch (Exception)
            {
                _logger.LogWarning("Timeout waiting for 'Sign Out' selector. Checking page content...");
                var content = await page.ContentAsync();
                if (content.Contains("Sign Out") || content.Contains(username))
                {
                    _logger.LogInformation("Found evidence of login in page content.");
                    return true;
                }
                _logger.LogWarning("Login verification failed: Neither username nor 'Sign Out' found.");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during BGG login via Playwright");
            return false;
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    public async Task<byte[]?> DownloadPdfAsync(string url, string? bggFileId = null)
    {
        await EnsureInitializedAsync();
        
        // Ensure logged in
        await LoginAsync();

        var page = await _context!.NewPageAsync();
        try
        {
            _logger.LogInformation($"Navigating to BGG file page: {url}");
            
            // Start listening for download event
            var downloadTask = page.WaitForDownloadAsync(new PageWaitForDownloadOptions { Timeout = 120000 });

            // Navigate to the file page first (important for session/referer)
            _logger.LogInformation($"Navigating to file page: {url}");
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
            
            // Wait a bit more for background JS to settle
            await Task.Delay(3000);
            
            _logger.LogInformation($"Page loaded. Title: {await page.TitleAsync()}");

            // Look for download buttons using selectors from Python script
            var downloadSelectors = new[] { 
                "a[href*='/file/download/']",
                "a[href*='/file/download_redirect/']", 
                "a[href*='/filepage/download/']", 
                "a[href*='/filepage/download_redirect/']",
                "a.btn-primary:has-text('Download')",
                ".btn-primary",
                "a:has-text('Download')",
                "button:has-text('Download')"
            };

            string? foundDownloadUrl = null;
            bool clicked = false;
            foreach (var selector in downloadSelectors)
            {
                var locator = page.Locator(selector);
                if (await locator.CountAsync() > 0)
                {
                    _logger.LogInformation($"Clicking download button: {selector}");
                    foundDownloadUrl = await locator.First.GetAttributeAsync("href");
                    await locator.First.ClickAsync(new LocatorClickOptions { Force = true });
                    clicked = true;
                    break;
                }
            }

            byte[]? capturedPdf = null;
            string capturedUrl = "";

            // Lắng nghe TẤT CẢ các tab mới được mở ra
            _context!.Page += async (sender, newPage) => {
                _logger.LogInformation($"New tab opened: {newPage.Url}");
                newPage.Response += async (s, response) => {
                    if (response.Status == 200 && response.Headers.ContainsKey("content-type") && response.Headers["content-type"].Contains("application/pdf"))
                    {
                        try {
                            var bytes = await response.BodyAsync();
                            if (bytes != null && bytes.Length > 1000) {
                                capturedPdf = bytes;
                                capturedUrl = response.Url;
                                _logger.LogInformation($"Captured PDF from NEW TAB: {capturedUrl} ({bytes.Length} bytes)");
                            }
                        } catch { }
                    }
                };
            };

            // Lắng nghe tab hiện tại
            page.Response += async (sender, response) =>
            {
                if (response.Status == 200 && response.Headers.ContainsKey("content-type") && response.Headers["content-type"].Contains("application/pdf"))
                {
                    try {
                        var bytes = await response.BodyAsync();
                        if (bytes != null && bytes.Length > 1000) {
                            capturedPdf = bytes;
                            capturedUrl = response.Url;
                            _logger.LogInformation($"Captured PDF from MAIN TAB: {capturedUrl} ({bytes.Length} bytes)");
                        }
                    } catch { }
                }
            };

            if (clicked)
            {
                _logger.LogInformation("Waiting up to 30s for PDF (checking all tabs)...");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < 30 && capturedPdf == null)
                {
                    await Task.Delay(1000);
                    // Kiểm tra xem có tab nào đang ở URL PDF không
                    foreach (var p in _context.Pages) {
                        if (p.Url.ToLower().Contains(".pdf")) {
                            _logger.LogInformation($"Found a tab with PDF URL: {p.Url}");
                        }
                    }
                }
            }

            if (capturedPdf != null)
            {
                return capturedPdf;
            }

            _logger.LogWarning("No PDF captured after click. Checking current page URL...");
            if (page.Url.ToLower().Contains(".pdf"))
            {
                _logger.LogInformation("Page is currently showing a PDF. Navigating again to capture...");
                var resp = await page.GotoAsync(page.Url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                var bytes = await resp!.BodyAsync();
                if (bytes != null && bytes.Length > 1000) return bytes;
            }

            // FALLBACK (inspired by Python's APIRequestContext)
            if (string.IsNullOrEmpty(foundDownloadUrl))
            {
                // Try to find ANY link with download
                var link = page.Locator("a[href*='download']").First;
                if (await link.CountAsync() > 0)
                {
                    foundDownloadUrl = await link.GetAttributeAsync("href");
                }
            }

            if (!string.IsNullOrEmpty(foundDownloadUrl))
            {
                if (!foundDownloadUrl.StartsWith("http"))
                    foundDownloadUrl = "https://boardgamegeek.com" + foundDownloadUrl;

                _logger.LogInformation($"Using fallback (Strategy 2): Fetching {foundDownloadUrl} using Browser API Request...");
                
                var requestContext = page.Context.APIRequest;
                var response = await requestContext.GetAsync(foundDownloadUrl);
                
                if (response.Status == 200)
                {
                    var bytes = await response.BodyAsync();
                    if (bytes != null && bytes.Length > 1000)
                    {
                        var contentType = response.Headers.ContainsKey("content-type") ? response.Headers["content-type"] : "";
                        _logger.LogInformation($"Successfully downloaded {bytes.Length} bytes via APIRequest. Content-Type: {contentType}");
                        return bytes;
                    }
                }
                else
                {
                    _logger.LogWarning($"APIRequest failed with status: {response.Status}");
                    
                    // Strategy 3: Redirected navigation (some PDFs open in viewer)
                    _logger.LogInformation("Trying Strategy 3: Direct navigation and body capture...");
                    var gotoResponse = await page.GotoAsync(foundDownloadUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30000 });
                    if (gotoResponse != null && gotoResponse.Status == 200)
                    {
                        var contentType = gotoResponse.Headers.ContainsKey("content-type") ? gotoResponse.Headers["content-type"] : "";
                        if (contentType.ToLower().Contains("application/pdf") || page.Url.ToLower().Contains(".pdf"))
                        {
                            var bytes = await gotoResponse.BodyAsync();
                            _logger.LogInformation($"Successfully captured {bytes.Length} bytes via GotoAsync body.");
                            return bytes;
                        }
                    }
                }
            }
            
            _logger.LogWarning("All download strategies failed.");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = "download_failed_final.png", FullPage = true });
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error downloading PDF via Playwright from {url}");
            try { await page.ScreenshotAsync(new PageScreenshotOptions { Path = "download_error.png" }); } catch {}
            return null;
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_browser != null) await _browser.CloseAsync();
        if (_playwright != null) _playwright.Dispose();
    }
}
