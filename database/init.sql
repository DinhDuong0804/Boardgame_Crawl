-- Board Game Cafe Database Schema
-- PostgreSQL 16

-- Enable UUID extension
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- ============================================
-- GAMES TABLE (Core game data from BGG)
-- ============================================
CREATE TABLE games (
    id                  SERIAL PRIMARY KEY,
    bgg_id              INTEGER UNIQUE NOT NULL,
    name                VARCHAR(500) NOT NULL,
    year_published      INTEGER,
    description         TEXT,
    
    -- Player info
    min_players         INTEGER,
    max_players         INTEGER,
    min_playtime        INTEGER,  -- minutes
    max_playtime        INTEGER,  -- minutes
    
    -- Ratings
    avg_rating          DECIMAL(5,2),
    bgg_rank            INTEGER,
    
    -- Images
    image_url           VARCHAR(1000),
    thumbnail_url       VARCHAR(1000),
    
    -- JSON fields for flexible data
    categories          JSONB DEFAULT '[]',
    mechanics           JSONB DEFAULT '[]',
    designers           JSONB DEFAULT '[]',
    artists             JSONB DEFAULT '[]',
    publishers          JSONB DEFAULT '[]',
    
    -- Status: scraped -> active -> inactive
    status              VARCHAR(50) DEFAULT 'scraped',
    
    -- Timestamps
    scraped_at          TIMESTAMP DEFAULT NOW(),
    updated_at          TIMESTAMP DEFAULT NOW(),
    
    -- Constraints
    CONSTRAINT chk_status CHECK (status IN ('scraped', 'active', 'inactive', 'pending_translation'))
);

-- ============================================
-- GAME TRANSLATIONS (Vietnamese content)
-- ============================================
CREATE TABLE game_translations (
    id                  SERIAL PRIMARY KEY,
    game_id             INTEGER REFERENCES games(id) ON DELETE CASCADE,
    
    -- Translated content
    name_vi             VARCHAR(500),
    description_vi      TEXT,
    
    -- Status tracking
    status              VARCHAR(50) DEFAULT 'pending',
    error_message       TEXT,
    
    -- Timestamps
    requested_at        TIMESTAMP DEFAULT NOW(),
    completed_at        TIMESTAMP,
    
    -- One translation per game
    CONSTRAINT uq_game_translation UNIQUE (game_id),
    CONSTRAINT chk_trans_status CHECK (status IN ('pending', 'processing', 'completed', 'failed'))
);

-- ============================================
-- RULEBOOKS (PDF/DOC files from BGG)
-- ============================================
CREATE TABLE rulebooks (
    id                  SERIAL PRIMARY KEY,
    game_id             INTEGER REFERENCES games(id) ON DELETE CASCADE,
    
    -- BGG file info
    bgg_file_id         VARCHAR(100),
    title               VARCHAR(500) NOT NULL,
    original_url        VARCHAR(1000) NOT NULL,
    file_type           VARCHAR(20) DEFAULT 'pdf',
    language            VARCHAR(50) DEFAULT 'English',
    
    -- Local storage
    local_file_path     VARCHAR(500),  -- Path to downloaded file
    
    -- Extracted content
    content_en          TEXT,          -- Original English text
    content_vi          TEXT,          -- Translated Vietnamese text
    markdown_path       VARCHAR(500),  -- Path to generated markdown
    
    -- Status
    status              VARCHAR(50) DEFAULT 'pending',
    error_message       TEXT,
    
    -- Timestamps
    created_at          TIMESTAMP DEFAULT NOW(),
    processed_at        TIMESTAMP,
    
    CONSTRAINT chk_rb_status CHECK (status IN ('pending', 'downloading', 'downloaded', 'extracting', 'translating', 'completed', 'failed'))
);

-- ============================================
-- CAFE INVENTORY (Physical games in cafe)
-- ============================================
CREATE TABLE cafe_inventory (
    id                  SERIAL PRIMARY KEY,
    game_id             INTEGER REFERENCES games(id) ON DELETE CASCADE,
    
    -- Inventory info
    quantity            INTEGER DEFAULT 1,
    available           INTEGER DEFAULT 1,
    location            VARCHAR(100),      -- "Shelf A", "Cabinet 3"
    condition           VARCHAR(50) DEFAULT 'good',
    
    -- Notes
    notes               TEXT,
    
    -- Timestamps
    added_at            TIMESTAMP DEFAULT NOW(),
    updated_at          TIMESTAMP DEFAULT NOW(),
    
    -- One inventory record per game
    CONSTRAINT uq_inventory_game UNIQUE (game_id),
    CONSTRAINT chk_condition CHECK (condition IN ('excellent', 'good', 'fair', 'poor'))
);

-- ============================================
-- TRANSLATION QUEUE (RabbitMQ tracking)
-- ============================================
CREATE TABLE translation_queue (
    id                  SERIAL PRIMARY KEY,
    
    -- Reference
    game_id             INTEGER REFERENCES games(id) ON DELETE CASCADE,
    rulebook_id         INTEGER REFERENCES rulebooks(id) ON DELETE CASCADE,
    
    -- Request type
    request_type        VARCHAR(50) NOT NULL,  -- 'game_info', 'rulebook'
    
    -- Message tracking
    message_id          UUID DEFAULT uuid_generate_v4(),
    correlation_id      UUID,
    
    -- Status
    status              VARCHAR(50) DEFAULT 'queued',
    priority            INTEGER DEFAULT 0,
    retry_count         INTEGER DEFAULT 0,
    max_retries         INTEGER DEFAULT 3,
    
    -- Error handling
    error_message       TEXT,
    
    -- Timestamps
    queued_at           TIMESTAMP DEFAULT NOW(),
    started_at          TIMESTAMP,
    completed_at        TIMESTAMP,
    
    CONSTRAINT chk_queue_status CHECK (status IN ('queued', 'processing', 'completed', 'failed', 'cancelled'))
);

-- ============================================
-- SCRAPER STATE (Resume capability)
-- ============================================
CREATE TABLE scraper_state (
    id                  SERIAL PRIMARY KEY,
    state_key           VARCHAR(100) UNIQUE NOT NULL,
    state_value         JSONB NOT NULL,
    updated_at          TIMESTAMP DEFAULT NOW()
);

-- ============================================
-- INDEXES
-- ============================================
CREATE INDEX idx_games_bgg_id ON games(bgg_id);
CREATE INDEX idx_games_status ON games(status);
CREATE INDEX idx_games_rank ON games(bgg_rank) WHERE bgg_rank IS NOT NULL;
CREATE INDEX idx_games_players ON games(min_players, max_players);
CREATE INDEX idx_games_playtime ON games(min_playtime, max_playtime);

CREATE INDEX idx_translations_game_id ON game_translations(game_id);
CREATE INDEX idx_translations_status ON game_translations(status);

CREATE INDEX idx_rulebooks_game_id ON rulebooks(game_id);
CREATE INDEX idx_rulebooks_status ON rulebooks(status);

CREATE INDEX idx_inventory_game_id ON cafe_inventory(game_id);

CREATE INDEX idx_queue_status ON translation_queue(status);
CREATE INDEX idx_queue_game_id ON translation_queue(game_id);

-- ============================================
-- VIEWS
-- ============================================

-- Active games with translations (for customer app)
CREATE VIEW v_active_games AS
SELECT 
    g.id,
    g.bgg_id,
    g.name,
    COALESCE(t.name_vi, g.name) as display_name,
    g.description,
    t.description_vi,
    g.year_published,
    g.min_players,
    g.max_players,
    g.min_playtime,
    g.max_playtime,
    g.avg_rating,
    g.bgg_rank,
    g.image_url,
    g.thumbnail_url,
    g.categories,
    g.mechanics,
    CASE WHEN t.status = 'completed' THEN true ELSE false END as has_translation,
    COALESCE(i.quantity, 0) as quantity,
    COALESCE(i.available, 0) as available_count,
    i.location
FROM games g
LEFT JOIN game_translations t ON g.id = t.game_id
LEFT JOIN cafe_inventory i ON g.id = i.game_id
WHERE g.status = 'active';

-- Games pending translation
CREATE VIEW v_pending_translations AS
SELECT 
    g.id,
    g.bgg_id,
    g.name,
    g.bgg_rank,
    t.status as translation_status,
    t.requested_at
FROM games g
LEFT JOIN game_translations t ON g.id = t.game_id
WHERE g.status IN ('active', 'pending_translation')
  AND (t.status IS NULL OR t.status IN ('pending', 'failed'));

-- ============================================
-- INITIAL DATA
-- ============================================

-- Insert initial scraper state
INSERT INTO scraper_state (state_key, state_value) VALUES 
('rank_scraper', '{"last_page": 1, "last_run": null}'),
('sequence_scraper', '{"last_id": 0, "last_run": null}');

-- ============================================
-- FUNCTIONS
-- ============================================

-- Function to update updated_at timestamp
CREATE OR REPLACE FUNCTION update_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Triggers for updated_at
CREATE TRIGGER trg_games_updated_at
    BEFORE UPDATE ON games
    FOR EACH ROW EXECUTE FUNCTION update_updated_at();

CREATE TRIGGER trg_inventory_updated_at
    BEFORE UPDATE ON cafe_inventory
    FOR EACH ROW EXECUTE FUNCTION update_updated_at();
