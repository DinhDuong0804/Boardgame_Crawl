# Translation Service Migration - C# Implementation

## ✅ Hoàn thành

Đã chuyển toàn bộ logic translation từ Python sang C# thành công!

## 📁 Files đã tạo

### 1. Services
- **`PdfService.cs`** - Đọc và trích xuất text từ PDF sử dụng iText7
- **`GeminiTranslatorService.cs`** - Dịch văn bản bằng Google Gemini API 1.5 Pro
- **`RulebookTranslationService.cs`** - Service tổng hợp toàn bộ workflow

### 2. Controllers
- **`RulebookController.cs`** - API endpoints để upload PDF và nhận bản dịch

### 3. Configuration
- **`appsettings.json`** - Đã thêm Gemini API key
- **`Program.cs`** - Đã register các service mới

## 🎯 Chức năng

### Workflow tự động:
```
Upload PDF → Extract Text (iText7) → Chunk Text → Translate (Gemini API) → Create Bilingual Markdown → Save File
```

### Features chính:
✅ Upload PDF rulebook qua API  
✅ Trích xuất text từ PDF (hỗ trợ 2 cột)  
✅ Tự động chia nhỏ text thành chunks (~4500 ký tự)  
✅ Rate limiting (2 giây giữa mỗi request)  
✅ Dịch sang tiếng Việt bằng Gemini 1.5 Pro  
✅ Tạo file Markdown song ngữ (Anh-Việt)  
✅ Lưu tự động vào thư mục output  

## 📡 API Endpoints

### 1. Upload & Translate PDF
```http
POST /api/rulebook/upload
Content-Type: multipart/form-data

Parameters:
- file: PDF file (required)
- gameName: string (optional)
- bggId: int (optional)

Response:
{
  "success": true,
  "fileName": "rulebook.pdf",
  "gameName": "Brass Birmingham",
  "bggId": 224517,
  "extractedWordCount": 5432,
  "extractedCharCount": 27589,
  "vietnameseText": "...",
  "bilingualMarkdown": "...",
  "outputFilePath": "d:/output/rulebooks_vi/224517_brass_birmingham_20260203_144830.md",
  "processingTimeSeconds": 45.2,
  "completedAt": "2026-02-03T14:48:30Z"
}
```

### 2. Health Check
```http
GET /api/rulebook/health

Response:
{
  "status": "healthy",
  "timestamp": "2026-02-03T14:48:30Z",
  "geminiConfigured": true,
  "maxUploadSizeMB": 50
}
```

### 3. Statistics
```http
GET /api/rulebook/statistics

Response:
{
  "totalGames": 1500,
  "gamesWithRulebooks": 450,
  "translatedGames": 120,
  "failedTranslations": 5,
  "pendingTranslations": 25
}
```

## 🔧 Configuration

### appsettings.json
```json
{
  "Gemini": {
    "ApiKey": "AIzaSyDAtqG0sMSGFX-21cJacBWBRGbpjY3xnCM"
  },
  "Translation": {
    "OutputDirectory": "output/rulebooks_vi"
  }
}
```

## 🚀 Usage Example

### Sử dụng cURL:
```bash
curl -X POST "http://localhost:5000/api/rulebook/upload" \
  -F "file=@rulebook.pdf" \
  -F "gameName=Brass Birmingham" \
  -F "bggId=224517"
```

### Sử dụng Postman:
1. Chọn **POST** method
2. URL: `http://localhost:5000/api/rulebook/upload`
3. Body → form-data:
   - Key: `file` (type: File) → Chọn PDF file
   - Key: `gameName` (type: Text) → Nhập tên game
   - Key: `bggId` (type: Text) → Nhập BGG ID
4. Click **Send**

### Sử dụng JavaScript (fetch):
```javascript
const formData = new FormData();
formData.append('file', pdfFile);
formData.append('gameName', 'Brass Birmingham');
formData.append('bggId', '224517');

const response = await fetch('http://localhost:5000/api/rulebook/upload', {
  method: 'POST',
  body: formData
});

const result = await response.json();
console.log('Translation completed:', result);
```

## 📦 Dependencies

### NuGet Packages đã cài:
- `itext7` (9.0.0) - PDF text extraction
- `Mscc.GenerativeAI` (1.9.0) - Google Gemini API client

## ⚙️ Technical Details

### PdfService
- Sử dụng **iText7** với `LocationTextExtractionStrategy`
- Đọc đúng thứ tự text trong PDF có 2 cột
- Clean text: loại bỏ khoảng trắng thừa, chuẩn hóa newlines

### GeminiTranslatorService
- Model: **Gemini 1.5 Pro**
- Chunk size: **4500 characters** (tối ưu cho Gemini)
- Rate limiting: **2 seconds** giữa mỗi API call
- Prompt engineering: Ép buộc output tiếng Việt, giữ nguyên thuật ngữ board game

### Prompting Strategy
```
BẠN LÀ MỘT DỊCH GIẢ CHUYÊN NGHIỆP VỀ BOARD GAME.

QUY TẮC:
1. Output MUST BE STRICTLY in Vietnamese (100% tiếng Việt)
2. GIỮ NGUYÊN các thuật ngữ: Round, Era, Phase, Turn, Token, Meeple, Worker, Resource, etc.
3. Dịch tự nhiên, dễ hiểu
4. Giữ nguyên format markdown
5. Giữ nguyên số, tên riêng
```

## 🎯 Output Format

### Bilingual Markdown Structure:
```markdown
# Brass Birmingham - Rulebook

---

## 🇬🇧 ENGLISH VERSION

[Original English text...]

---

## 🇻🇳 BẢN DỊCH TIẾNG VIỆT

[Vietnamese translation...]

---

*Dịch bởi Google Gemini 1.5 Pro - 2026-02-03 14:48:30 UTC*
```

## 📁 Output Directory Structure

```
output/
└── rulebooks_vi/
    ├── 224517_brass_birmingham_rulebook_20260203_144830.md
    ├── 161936_pandemic_legacy_season_1_20260203_150230.md
    └── ...
```

### Naming Convention:
`{bggId}_{gameName}_{originalFileName}_{timestamp}.md`

## ✨ Advantages vs Python Version

1. **🚀 Performance** - Native .NET performance, no subprocess calls
2. **🔗 Single Codebase** - Tất cả trong một C# solution
3. **📦 Easier Deployment** - Không cần Python runtime, dependencies riêng
4. **🔄 Better Integration** - Trực tiếp access database, services
5. **🛡️ Type Safety** - Strong typing với C#
6. **📊 Unified Logging** - Cùng logging framework với API
7. **🎯 API-based** - RESTful API thay vì background worker

## 🧪 Testing

### 1. Smoke Test (Quick)
Test với 1 trang PDF (~500 từ) để verify:
- ✅ PDF extraction hoạt động
- ✅ Gemini API kết nối OK
- ✅ File markdown được tạo

### 2. Full Test (Production)
Test với full rulebook (~5000-10000 từ):
- Kiểm tra chunking strategy
- Verify rate limiting (2s delay)
- Check translation quality

### Expected Processing Time:
- **1000 words**: ~15-20 seconds
- **5000 words**: ~60-90 seconds
- **10000 words**: ~120-180 seconds

## 🐛 Troubleshooting

### Lỗi: "Output tiếng Anh thay vì tiếng Việt"
**Fix**: Kiểm tra prompt trong `GeminiTranslatorService.BuildTranslationPrompt()`

### Lỗi: "429 Too Many Requests"
**Fix**: Tăng `RateLimitDelayMs` lên 4000ms (4 giây)

### Lỗi: "PDF không đọc được"
**Fix**: Đảm bảo PDF không bị encrypt/password protected

### Lỗi: "Gemini API key invalid"
**Fix**: Kiểm tra `appsettings.json` → `Gemini:ApiKey`

## 📝 Next Steps

### Tính năng bổ sung có thể implement:
- [ ] Queue system cho batch processing
- [ ] Progress tracking với WebSocket
- [ ] Download markdown file qua API
- [ ] OCR support cho PDF scan
- [ ] Multi-language support (không chỉ Việt)
- [ ] Cache translation results
- [ ] Webhook notification khi hoàn thành

## 🎉 Ready to Use!

Service đã sẵn sàng! Chỉ cần:

1. **Start API**: `dotnet run` (nếu chưa chạy)
2. **Test endpoint**: `GET /api/rulebook/health`
3. **Upload PDF**: `POST /api/rulebook/upload`
4. **Enjoy** bản dịch tiếng Việt! 🇻🇳

---

**Author**: AI Assistant  
**Date**: 2026-02-03  
**Version**: 1.0.0
