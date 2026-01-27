# Board Game Cafe - Translation Service

Dịch dữ liệu board game từ tiếng Anh sang tiếng Việt sử dụng OPUS-MT.

## Kiến Trúc

```
┌──────────────────┐     RabbitMQ      ┌──────────────────┐
│   C# API         │◄────────────────►│   Translation    │
│   (Port 5000)    │                   │   Service (5001) │
└────────┬─────────┘                   └────────┬─────────┘
         │                                      │
         └──────────────┬───────────────────────┘
                        ▼
              ┌──────────────────┐
              │   PostgreSQL     │
              │   (Port 5432)    │
              └──────────────────┘
```

## Tính Năng

1. **RabbitMQ Consumer**: Lắng nghe translation requests
2. **OPUS-MT Translation**: Dịch name, description
3. **Rulebook Processing**: Download PDF → Extract → Dịch → Markdown
4. **PostgreSQL Integration**: Lưu kết quả vào database

## Cài Đặt

### 1. Setup Environment

```bash
cd translation-service

# Tạo virtual environment
python -m venv venv

# Activate (Windows)
venv\Scripts\activate

# Activate (Linux/Mac)
source venv/bin/activate

# Cài đặt dependencies
pip install -r requirements.txt
```

### 2. Cấu Hình

```bash
# Copy file .env mẫu
copy .env.example .env

# Sửa các giá trị trong .env nếu cần
```

### 3. Kiểm Tra Kết Nối

```bash
# Test database connection
python -c "from database import get_database; db = get_database(); db.connect(); print('OK')"

# Test model loading
python translator.py
```

## Chạy Service

### Mode 1: RabbitMQ Consumer (Recommended)

```bash
# Chạy consumer lắng nghe messages từ C# API
python consumer.py
```

Consumer sẽ:
- Kết nối RabbitMQ
- Chờ translation requests
- Xử lý và cập nhật PostgreSQL

### Mode 2: Batch Processing (Standalone)

```bash
# Dịch trực tiếp từ JSONL file
python batch_process.py --games-only --max-games 10
```

### Mode 3: FastAPI Server (Optional)

```bash
# Chạy API server
uvicorn app:app --host 0.0.0.0 --port 5001
```

## RabbitMQ Messages

### Translation Request (from C# API)

```json
{
  "game_id": 1,
  "bgg_id": 224517,
  "game_name": "Brass: Birmingham",
  "description": "...",
  "translate_info": true,
  "translate_rulebooks": false,
  "rulebooks": []
}
```

### Translation Completed (to C# API)

```json
{
  "game_id": 1,
  "success": true,
  "name_vi": "Brass: Birmingham",
  "description_vi": "...",
  "rulebooks": [],
  "error_message": null
}
```

## Cấu Trúc Files

```
translation-service/
├── config.py           # Configuration (env vars)
├── translator.py       # OPUS-MT translation engine
├── database.py         # PostgreSQL operations
├── consumer.py         # RabbitMQ consumer (MAIN)
├── rulebook_processor.py  # PDF/DOC processing
├── batch_process.py    # Standalone batch processing
├── app.py              # FastAPI server (optional)
├── requirements.txt    # Python dependencies
├── .env.example        # Environment template
└── output/
    └── rulebooks_vi/   # Generated markdown files
```

## Thời Gian Xử Lý (CPU)

| Task | Thời gian |
|------|-----------|
| Load model (lần đầu) | 2-3 phút |
| Dịch name | ~0.5 giây |
| Dịch description (500 chars) | ~2-3 giây |
| Xử lý 1 rulebook PDF | ~30-60 giây |

## Troubleshooting

### Model download chậm

```bash
# Tải trước model
python -c "from transformers import MarianMTModel, MarianTokenizer; MarianTokenizer.from_pretrained('Helsinki-NLP/opus-mt-en-vi'); MarianMTModel.from_pretrained('Helsinki-NLP/opus-mt-en-vi')"
```

### Lỗi kết nối RabbitMQ

```bash
# Kiểm tra RabbitMQ đang chạy
docker ps | grep rabbitmq

# Kiểm tra port
netstat -an | findstr 5672
```

### Lỗi kết nối PostgreSQL

```bash
# Kiểm tra PostgreSQL đang chạy
docker ps | grep postgres

# Test connection
psql -h localhost -U boardgame -d boardgame_cafe
```
