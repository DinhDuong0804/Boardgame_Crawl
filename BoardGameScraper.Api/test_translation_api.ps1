# Test Rulebook Translation API
# Quick test script to verify the translation service

$apiUrl = "http://localhost:5000"

Write-Host "=== Testing Rulebook Translation API ===" -ForegroundColor Cyan
Write-Host ""

# Test 1: Health Check
Write-Host "Test 1: Health Check..." -ForegroundColor Yellow
try {
    $health = Invoke-RestMethod -Uri "$apiUrl/api/rulebook/health" -Method Get
    Write-Host "✅ Health check passed!" -ForegroundColor Green
    Write-Host "   Status: $($health.status)" -ForegroundColor Gray
    Write-Host "   Gemini Configured: $($health.geminiConfigured)" -ForegroundColor Gray
    Write-Host ""
} catch {
    Write-Host "❌ Health check failed: $_" -ForegroundColor Red
    Write-Host "   Make sure the API is running: dotnet run" -ForegroundColor Yellow
    exit 1
}

# Test 2: Statistics
Write-Host "Test 2: Statistics..." -ForegroundColor Yellow
try {
    $stats = Invoke-RestMethod -Uri "$apiUrl/api/rulebook/statistics" -Method Get
    Write-Host "✅ Statistics retrieved!" -ForegroundColor Green
    Write-Host "   Total Games: $($stats.totalGames)" -ForegroundColor Gray
    Write-Host "   Games with Rulebooks: $($stats.gamesWithRulebooks)" -ForegroundColor Gray
    Write-Host "   Translated: $($stats.translatedGames)" -ForegroundColor Gray
    Write-Host ""
} catch {
    Write-Host "⚠️ Statistics failed (database might be empty): $_" -ForegroundColor Yellow
    Write-Host ""
}

# Test 3: Upload a PDF (if path is provided)
if ($args.Count -gt 0) {
    $pdfPath = $args[0]
    
    if (Test-Path $pdfPath) {
        Write-Host "Test 3: Uploading PDF: $pdfPath" -ForegroundColor Yellow
        Write-Host "   This may take 30-180 seconds depending on PDF size..." -ForegroundColor Gray
        Write-Host ""
        
        try {
            # Create multipart form data
            $boundary = [System.Guid]::NewGuid().ToString()
            $LF = "`r`n"
            
            $fileContent = [System.IO.File]::ReadAllBytes($pdfPath)
            $fileName = [System.IO.Path]::GetFileName($pdfPath)
            
            $bodyLines = @(
                "--$boundary",
                "Content-Disposition: form-data; name=`"file`"; filename=`"$fileName`"",
                "Content-Type: application/pdf$LF",
                [System.Text.Encoding]::Default.GetString($fileContent),
                "--$boundary",
                "Content-Disposition: form-data; name=`"gameName`"$LF",
                "Test Game",
                "--$boundary",
                "Content-Disposition: form-data; name=`"bggId`"$LF",
                "999999",
                "--$boundary--$LF"
            ) -join $LF
            
            $response = Invoke-RestMethod `
                -Uri "$apiUrl/api/rulebook/upload" `
                -Method Post `
                -ContentType "multipart/form-data; boundary=$boundary" `
                -Body $bodyLines `
                -TimeoutSec 300
            
            Write-Host "✅ Translation completed!" -ForegroundColor Green
            Write-Host "   File: $($response.fileName)" -ForegroundColor Gray
            Write-Host "   Words extracted: $($response.extractedWordCount)" -ForegroundColor Gray
            Write-Host "   Processing time: $([math]::Round($response.processingTimeSeconds, 1))s" -ForegroundColor Gray
            Write-Host "   Output: $($response.outputFilePath)" -ForegroundColor Gray
            Write-Host ""
            Write-Host "Vietnamese preview (first 200 chars):" -ForegroundColor Cyan
            Write-Host $response.vietnameseText.Substring(0, [Math]::Min(200, $response.vietnameseText.Length)) -ForegroundColor White
            Write-Host ""
        } catch {
            Write-Host "❌ Upload failed: $_" -ForegroundColor Red
            Write-Host ""
        }
    } else {
        Write-Host "❌ PDF file not found: $pdfPath" -ForegroundColor Red
        Write-Host ""
    }
} else {
    Write-Host "ℹ️ To test PDF upload, run:" -ForegroundColor Cyan
    Write-Host "   .\test_translation_api.ps1 `"path\to\rulebook.pdf`"" -ForegroundColor White
    Write-Host ""
}

Write-Host "=== All tests completed ===" -ForegroundColor Cyan
