# Kế hoạch Cào Dữ Liệu Rulebook Boardgame

## Mục tiêu
Cào bổ sung dữ liệu rulebook (luật chơi) cho các boardgame đã được cào từ BGG API.
**Yêu cầu**: Chỉ lấy rulebook tiếng Anh + Dịch sang tiếng Việt.

## Các Phase đã triển khai

### Phase 1: BGG Main Scraper (ScraperWorker.cs)
- Cào game từ BGG ranked list
- Sử dụng BGG XML API2
- Output: `bgg_rank.jsonl`, `bgg_all_ids.jsonl`

### Phase 2: Rulebook Scraper (RulebookScraperService.cs)
- Cào rulebook từ BGG Files API: `api.geekdo.com/api/files`
- **Chỉ lấy English rulebooks**
- Nhận diện ngôn ngữ từ API + detection tự động
- URL format: `https://boardgamegeek.com/filepage/{id}/{slug}`

### Phase 3: Rulebook Enrichment (RulebookEnrichmentWorker.cs)
- Đọc games từ `bgg_rank.jsonl`
- Bổ sung rulebook URLs
- Bổ sung Wikidata info (Wikipedia URL, Official Website)
- Output: `bgg_with_rulebooks.jsonl`

### Phase 4: Translation (TranslationService.cs) [MỚI]
- Dịch thông tin game từ tiếng Anh sang tiếng Việt
- Hỗ trợ nhiều providers:
  - **LibreTranslate** (miễn phí, mặc định)
  - **Google Translate API** (có phí)
  - **DeepL API** (chất lượng cao, có phí)
  - **OpenAI GPT** (tốt cho context game, có phí)
- Dịch: Tên game, Mô tả, Categories, Mechanics
- Output: File song ngữ với cả English + Vietnamese

## Cấu trúc dữ liệu Output

```json
{
  "bgg_id": 224517,
  "name": "Brass: Birmingham",
  "name_vi": "Brass: Birmingham",
  "description": "Brass: Birmingham is an economic strategy game...",
  "description_vi": "Brass: Birmingham là một trò chơi chiến lược kinh tế...",
  "rulebook_urls": [
    {
      "url": "https://boardgamegeek.com/filepage/214238/reference-sheet-...",
      "title": "Reference sheet for Brass: Birmingham (English)",
      "language": "English",
      "file_type": "pdf"
    }
  ],
  "wikidata_id": "Q...",
  "wikipedia_url": "https://en.wikipedia.org/wiki/..."
}
```

## Cấu hình (appsettings.json)

```json
{
  "Scraper": {
    "EnableRulebookPhase": true,
    "RulebookBatchSize": 10,
    "EnableTranslation": false
  },
  "Translation": {
    "Provider": "libretranslate",
    "ApiKey": "",
    "LibreTranslate": {
      "Endpoint": "https://libretranslate.com/translate"
    }
  }
}
```

## Translation Providers

| Provider | Giá | Chất lượng | API Key |
|----------|-----|------------|---------|
| LibreTranslate | Miễn phí | Tốt | Không cần |
| Google Translate | ~$20/1M chars | Rất tốt | Google Cloud |
| DeepL | Free: 500K/tháng | Xuất sắc | deepl.com |
| OpenAI | ~$0.002/1K tokens | Tốt (context) | openai.com |

## Rate Limiting
- BGG: 1.5s delay giữa các requests
- Wikidata: 10s delay (SPARQL queries)
- Translation: Tùy provider

## Cách chạy

```bash
# Build
dotnet build

# Chạy scraper (Phase 1-3)
dotnet run

# Bật translation trong appsettings.json:
# "EnableTranslation": true
```

## Files Output
- `bgg_rank.jsonl` - Games với rank
- `bgg_all_ids.jsonl` - Tất cả IDs
- `bgg_with_rulebooks.jsonl` - Games + English rulebooks
- `bgg_translated.jsonl` - Games song ngữ (khi bật translation)

```json
{
  "bgg_id": 224517,
  "name": "Brass: Birmingham",
  "rulebook_urls": [
    {
      "url": "https://cf.geekdo-files.com/...",
      "title": "Official Rules (English)",
      "language": "English",
      "file_type": "pdf"
    }
  ],
  "wikidata_id": "Q...",
  "wikipedia_url": "https://en.wikipedia.org/wiki/...",
  "official_website": "https://..."
}
```

## Rate Limiting
- BGG: 1.5s delay giữa các requests
- Wikidata: 10s delay (SPARQL queries)
- Batch processing: 20 games/batch for API calls

## Cách chạy
1. Build project: `dotnet build`
2. Run scraper: `dotnet run`
3. Output sẽ ở: `bgg_rank.jsonl`, `bgg_all_ids.jsonl`
