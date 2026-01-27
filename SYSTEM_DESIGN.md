# Board Game Cafe - System Design Document

## Mục Tiêu Dự Án

Xây dựng hệ thống backend cho quán cafe kết hợp boardgame:
- Thu thập dữ liệu boardgame từ BoardGameGeek (BGG)
- Dịch thông tin game sang tiếng Việt
- Cung cấp API cho ứng dụng khách hàng tìm kiếm game

---

## Kiến Trúc Tổng Quan

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        BOARD GAME CAFE BACKEND                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────────┐                     ┌──────────────────────┐      │
│  │   C# .NET 9 API     │                     │  Python Translation  │      │
│  │   (Port 5000)       │                     │  Service (Port 5001) │      │
│  │                     │     ┌─────────┐     │                      │      │
│  │  ┌───────────────┐  │     │RabbitMQ │     │  ┌────────────────┐  │      │
│  │  │ Scraper       │  │────▶│         │────▶│  │ Consumer       │  │      │
│  │  │ Service       │  │     │ Queue   │     │  │ (OPUS-MT)      │  │      │
│  │  └───────────────┘  │     └────┬────┘     │  └────────────────┘  │      │
│  │                     │          │          │                      │      │
│  │  ┌───────────────┐  │          │          │  ┌────────────────┐  │      │
│  │  │ Admin API     │  │          │          │  │ Rulebook       │  │      │
│  │  │ (REST)        │  │          │          │  │ Processor      │  │      │
│  │  └───────────────┘  │          │          │  └────────────────┘  │      │
│  │                     │          │          │                      │      │
│  │  ┌───────────────┐  │          │          │                      │      │
│  │  │ Customer API  │  │          │          │                      │      │
│  │  │ (Query only)  │  │          │          │                      │      │
│  │  └───────────────┘  │          │          │                      │      │
│  └──────────┬──────────┘          │          └───────────┬──────────┘      │
│             │                     │                      │                 │
│             └─────────────────────┼──────────────────────┘                 │
│                                   ▼                                        │
│                        ┌──────────────────┐                                │
│                        │   PostgreSQL     │                                │
│                        │   (Port 5432)    │                                │
│                        │                  │                                │
│                        │  ┌────────────┐  │                                │
│                        │  │ games      │  │                                │
│                        │  │ translations│ │                                │
│                        │  │ rulebooks  │  │                                │
│                        │  │ inventory  │  │                                │
│                        │  └────────────┘  │                                │
│                        └──────────────────┘                                │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Services

### 1. C# .NET API (BoardGameScraper.Api)

**Port:** 5000

| Module | Chức năng |
|--------|-----------|
| **Scraper Service** | Cào dữ liệu từ BGG (ranked list, game details) |
| **Rulebook Service** | Cào URLs rulebook từ BGG files section |
| **Admin API** | Quản lý games, activate/deactivate, request translation |
| **Customer API** | Query games, search, filter |
| **RabbitMQ Publisher** | Gửi translation requests |

**Endpoints chính:**

```
GET  /api/games              # List games (filterable)
GET  /api/games/{id}         # Get game details
POST /api/games/{id}/activate    # Activate game
POST /api/games/{id}/translate   # Request translation
POST /api/games/{id}/inventory   # Update inventory

POST /api/scraper/scrape-rank    # Manual scrape trigger
POST /api/scraper/scrape/{bggId} # Scrape specific game

GET  /health                 # Health check
```

### 2. Python Translation Service

**Port:** 5001

| Module | Chức năng |
|--------|-----------|
| **RabbitMQ Consumer** | Lắng nghe translation requests |
| **OPUS-MT Translator** | Dịch text EN → VI |
| **Rulebook Processor** | Download PDF → Extract → Translate |
| **Database Operations** | Update PostgreSQL với kết quả |

**Modes:**

1. **Consumer Mode** (chính): `python consumer.py`
2. **Batch Mode**: `python batch_process.py`
3. **API Mode**: `uvicorn app:app`

---

## Database Schema

### Tables

```
┌──────────────────┐     ┌───────────────────┐
│      games       │────▶│ game_translations │
├──────────────────┤     ├───────────────────┤
│ id (PK)          │     │ id (PK)           │
│ bgg_id           │     │ game_id (FK)      │
│ name             │     │ name_vi           │
│ description      │     │ description_vi    │
│ min/max_players  │     │ status            │
│ min/max_playtime │     │ completed_at      │
│ avg_rating       │     └───────────────────┘
│ bgg_rank         │
│ categories (JSON)│     ┌───────────────────┐
│ mechanics (JSON) │────▶│    rulebooks      │
│ status           │     ├───────────────────┤
│ scraped_at       │     │ id (PK)           │
└──────────────────┘     │ game_id (FK)      │
         │               │ title             │
         │               │ original_url      │
         │               │ content_en        │
         │               │ content_vi        │
         │               │ markdown_path     │
         ▼               │ status            │
┌──────────────────┐     └───────────────────┘
│  cafe_inventory  │
├──────────────────┤     ┌───────────────────┐
│ id (PK)          │     │ translation_queue │
│ game_id (FK)     │     ├───────────────────┤
│ quantity         │     │ id (PK)           │
│ available        │     │ game_id (FK)      │
│ location         │     │ rulebook_id (FK)  │
│ condition        │     │ request_type      │
└──────────────────┘     │ status            │
                         │ queued_at         │
                         └───────────────────┘
```

### Game Status Flow

```
scraped → active → (pending_translation) → active (with translation)
                 ↘ inactive
```

---

## RabbitMQ Message Flow

### Exchange & Queues

```
Exchange: boardgame.exchange (topic)
│
├── Queue: translation.requests
│   └── Routing: translation.request
│
├── Queue: translation.completed
│   └── Routing: translation.completed
│
└── Queue: scraper.completed
    └── Routing: scraper.game.new
```

### Message Schemas

**Translation Request:**
```json
{
  "game_id": 1,
  "bgg_id": 224517,
  "game_name": "Brass: Birmingham",
  "description": "...",
  "translate_info": true,
  "translate_rulebooks": false,
  "rulebooks": [
    {"rulebook_id": 1, "title": "...", "url": "..."}
  ]
}
```

**Translation Completed:**
```json
{
  "game_id": 1,
  "success": true,
  "name_vi": "...",
  "description_vi": "...",
  "rulebooks": [
    {"rulebook_id": 1, "success": true, "markdown_path": "..."}
  ],
  "error_message": null
}
```

---

## Luồng Xử Lý

### 1. Scraping Flow

```
1. Admin triggers scrape (API or scheduled)
2. C# Scraper → BGG Ranked Pages → Get BGG IDs
3. C# Scraper → BGG XML API → Get game details
4. Save to PostgreSQL (status = 'scraped')
5. Scrape rulebook URLs from BGG files section
```

### 2. Activation & Translation Flow

```
1. Admin reviews scraped games via API
2. Admin calls POST /api/games/{id}/activate
3. Admin calls POST /api/games/{id}/translate
4. C# publishes TranslationRequest to RabbitMQ
5. Python Consumer receives request
6. Python translates with OPUS-MT
7. Python updates PostgreSQL
8. Python publishes TranslationCompleted
```

### 3. Customer Query Flow

```
1. Customer App calls GET /api/games?status=active&players=4
2. C# API queries PostgreSQL with filters
3. Returns games with Vietnamese translations
```

---

## Configuration

### C# API (appsettings.json)

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Port=5432;Database=boardgame_cafe;..."
  },
  "RabbitMQ": {
    "Host": "localhost",
    "Port": 5672,
    "Username": "guest",
    "Password": "guest"
  },
  "Scraper": {
    "EnableBackgroundWorkers": false
  }
}
```

### Python Service (.env)

```env
POSTGRES_HOST=localhost
POSTGRES_PORT=5432
RABBITMQ_HOST=localhost
RABBITMQ_PORT=5672
```

---

## Setup Instructions

### Prerequisites

- Docker (for PostgreSQL, RabbitMQ)
- .NET 9 SDK
- Python 3.10+

### Step 1: Start PostgreSQL

```bash
cd board_game_scraper-2.23.1
docker-compose up -d postgres
```

### Step 2: Initialize Database

```bash
# Database will auto-create from init.sql
docker exec -it boardgame_postgres psql -U boardgame -d boardgame_cafe
```

### Step 3: Start C# API

```bash
cd BoardGameScraper.Api
dotnet restore
dotnet run
```

### Step 4: Start Python Translation Service

```bash
cd translation-service
python -m venv venv
venv\Scripts\activate
pip install -r requirements.txt
cp .env.example .env
python consumer.py
```

### Step 5: Test

```bash
# Scrape some games
curl -X POST http://localhost:5000/api/scraper/scrape/224517

# Check games
curl http://localhost:5000/api/games

# Activate a game
curl -X POST http://localhost:5000/api/games/1/activate

# Request translation
curl -X POST http://localhost:5000/api/games/1/translate
```

---

## Scaling Considerations

| Component | Current | Scaling Option |
|-----------|---------|----------------|
| PostgreSQL | Single instance | Add read replicas |
| RabbitMQ | Single instance | Cluster mode |
| Translation Service | Single consumer | Multiple consumers |
| C# API | Single instance | Load balancer + multiple instances |

---

## Future Enhancements

1. **Customer Mobile App**: Flutter/React Native
2. **Admin Dashboard**: React/Angular web app
3. **Game Recommendations**: Based on player count, time, ratings
4. **Booking System**: Reserve games in advance
5. **GPU Acceleration**: For faster translation (CUDA)
6. **Cache Layer**: Redis for frequently accessed games

---

## Version

- **Document Version**: 1.0
- **Date**: 2026-01-25
- **Author**: AI-assisted design
