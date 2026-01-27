"""
Configuration for Translation Service with RabbitMQ and PostgreSQL
Synced with BoardGameScraper.Api/appsettings.json
"""
import os
from pathlib import Path
from dotenv import load_dotenv

# Load .env file if exists
load_dotenv()

# Paths
BASE_DIR = Path(__file__).parent
PROJECT_ROOT = BASE_DIR.parent
OUTPUT_DIR = BASE_DIR / "output"
RULEBOOKS_OUTPUT_DIR = OUTPUT_DIR / "rulebooks_vi"

# Create output directories
OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
RULEBOOKS_OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

# ============================================
# POSTGRESQL (synced with appsettings.json)
# ============================================
POSTGRES_HOST = os.getenv("POSTGRES_HOST", "localhost")
POSTGRES_PORT = os.getenv("POSTGRES_PORT", "5432")
POSTGRES_DB = os.getenv("POSTGRES_DB", "boardgame_cafe")
POSTGRES_USER = os.getenv("POSTGRES_USER", "postgres")
POSTGRES_PASSWORD = os.getenv("POSTGRES_PASSWORD", "080423")

DATABASE_URL = f"postgresql://{POSTGRES_USER}:{POSTGRES_PASSWORD}@{POSTGRES_HOST}:{POSTGRES_PORT}/{POSTGRES_DB}"

# ============================================
# RABBITMQ (synced with appsettings.json)
# ============================================
RABBITMQ_HOST = os.getenv("RABBITMQ_HOST", "localhost")
RABBITMQ_PORT = int(os.getenv("RABBITMQ_PORT", "5672"))
RABBITMQ_USERNAME = os.getenv("RABBITMQ_USERNAME", "admin")
RABBITMQ_PASSWORD = os.getenv("RABBITMQ_PASSWORD", "admin123")
RABBITMQ_VHOST = os.getenv("RABBITMQ_VHOST", "/")

# Exchange and Queues
EXCHANGE_NAME = "boardgame.exchange"
QUEUE_TRANSLATION_REQUESTS = "translation.requests"
QUEUE_TRANSLATION_COMPLETED = "translation.completed"

ROUTING_KEY_REQUEST = "translation.request"
ROUTING_KEY_COMPLETED = "translation.completed"

# ============================================
# BGG ACCOUNT (for downloading rulebooks)
# ============================================
BGG_USERNAME = os.getenv("BGG_USERNAME", "duong0804")
BGG_PASSWORD = os.getenv("BGG_PASSWORD", "Duong08004*")

# ============================================
# TRANSLATION MODEL
# ============================================
# Provider: 'local' (Helsinki-NLP) or 'gemini' (Google)
TRANSLATION_PROVIDER = os.getenv("TRANSLATION_PROVIDER", "local").lower()
GEMINI_API_KEY = os.getenv("GEMINI_API_KEY", "AIzaSyDAImpiQeYyIyBfnOOr8rDy0HfLmqlA9Ek")

# Local model settings
MODEL_NAME = os.getenv("TRANSLATION_MODEL", "Helsinki-NLP/opus-mt-en-vi")
DEVICE = "cpu"  # No GPU

# Translation settings
MAX_LENGTH = 512
BATCH_SIZE = 1
CHUNK_SIZE = 400

# ============================================
# PROCESSING
# ============================================
DOWNLOAD_TIMEOUT = 60
MAX_PDF_PAGES = 50
DOWNLOAD_DELAY = 2.0

# ============================================
# API SERVER
# ============================================
API_HOST = os.getenv("API_HOST", "0.0.0.0")
API_PORT = int(os.getenv("API_PORT", "5001"))
