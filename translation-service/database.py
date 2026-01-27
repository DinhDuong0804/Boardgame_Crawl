"""
Database operations for Translation Service
Uses psycopg2 to connect to PostgreSQL
"""
import logging
from typing import Optional, Dict, Any
from datetime import datetime
import psycopg2
from psycopg2.extras import RealDictCursor, Json

import config

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)


class Database:
    """PostgreSQL database connection and operations"""
    
    def __init__(self):
        self.conn = None
        
    def connect(self):
        """Connect to PostgreSQL"""
        if self.conn is not None and not self.conn.closed:
            return
            
        logger.info(f"Connecting to PostgreSQL at {config.POSTGRES_HOST}:{config.POSTGRES_PORT}")
        
        self.conn = psycopg2.connect(
            host=config.POSTGRES_HOST,
            port=config.POSTGRES_PORT,
            database=config.POSTGRES_DB,
            user=config.POSTGRES_USER,
            password=config.POSTGRES_PASSWORD
        )
        self.conn.autocommit = False
        logger.info("PostgreSQL connected successfully")
    
    def close(self):
        """Close connection"""
        if self.conn:
            self.conn.close()
            self.conn = None
    
    def get_game(self, game_id: int) -> Optional[Dict]:
        """Get game by ID"""
        self.connect()
        
        with self.conn.cursor(cursor_factory=RealDictCursor) as cur:
            cur.execute("""
                SELECT * FROM games WHERE id = %s
            """, (game_id,))
            return cur.fetchone()
    
    def get_rulebook(self, rulebook_id: int) -> Optional[Dict]:
        """Get rulebook by ID"""
        self.connect()
        
        with self.conn.cursor(cursor_factory=RealDictCursor) as cur:
            cur.execute("""
                SELECT * FROM rulebooks WHERE id = %s
            """, (rulebook_id,))
            return cur.fetchone()
    
    def update_game_translation(
        self,
        game_id: int,
        name_vi: Optional[str],
        description_vi: Optional[str],
        success: bool,
        error_message: Optional[str] = None
    ):
        """Update or insert game translation"""
        self.connect()
        
        status = "completed" if success else "failed"
        
        with self.conn.cursor() as cur:
            # Upsert translation
            cur.execute("""
                INSERT INTO game_translations 
                    (game_id, name_vi, description_vi, status, error_message, requested_at, completed_at)
                VALUES (%s, %s, %s, %s, %s, %s, %s)
                ON CONFLICT (game_id) 
                DO UPDATE SET
                    name_vi = EXCLUDED.name_vi,
                    description_vi = EXCLUDED.description_vi,
                    status = EXCLUDED.status,
                    error_message = EXCLUDED.error_message,
                    completed_at = EXCLUDED.completed_at
            """, (game_id, name_vi, description_vi, status, error_message, datetime.utcnow(), datetime.utcnow()))
            
            # Update game status
            if success:
                cur.execute("""
                    UPDATE games SET status = 'active', updated_at = %s WHERE id = %s
                """, (datetime.utcnow(), game_id))
            
            self.conn.commit()
            
        logger.info(f"Updated translation for game {game_id}: {status}")
    
    def update_rulebook_translation(
        self,
        rulebook_id: int,
        content_vi: Optional[str],
        markdown_path: Optional[str],
        success: bool,
        error_message: Optional[str] = None
    ):
        """Update rulebook translation"""
        self.connect()
        
        status = "completed" if success else "failed"
        
        with self.conn.cursor() as cur:
            cur.execute("""
                UPDATE rulebooks SET
                    content_vi = %s,
                    markdown_path = %s,
                    status = %s,
                    error_message = %s,
                    processed_at = %s
                WHERE id = %s
            """, (content_vi, markdown_path, status, error_message, datetime.utcnow(), rulebook_id))
            
            self.conn.commit()
            
        logger.info(f"Updated rulebook {rulebook_id}: {status}")
    
    def update_queue_status(
        self,
        game_id: int,
        status: str,
        error_message: Optional[str] = None
    ):
        """Update translation queue status"""
        self.connect()
        
        with self.conn.cursor() as cur:
            if status == "completed":
                cur.execute("""
                    UPDATE translation_queue SET
                        status = %s,
                        completed_at = %s,
                        error_message = %s
                    WHERE game_id = %s AND status IN ('queued', 'processing')
                """, (status, datetime.utcnow(), error_message, game_id))
            else:
                cur.execute("""
                    UPDATE translation_queue SET
                        status = %s,
                        started_at = COALESCE(started_at, %s),
                        error_message = %s
                    WHERE game_id = %s AND status IN ('queued', 'processing')
                """, (status, datetime.utcnow(), error_message, game_id))
            
            self.conn.commit()


# Singleton instance
_db = None

def get_database() -> Database:
    """Get or create database instance"""
    global _db
    if _db is None:
        _db = Database()
    return _db
