from database import get_database
from rulebook_processor import RulebookProcessor
import logging
from pathlib import Path
import time

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("batch_downloader")

def download_pending_rulebooks():
    db = get_database()
    db.connect()
    
    # Get all rulebooks that don't have a local file path
    with db.conn.cursor() as cur:
        # Join with games to get BGG ID
        cur.execute("""
            SELECT r.id, r.title, r.original_url, r.bgg_file_id, g.bgg_id 
            FROM rulebooks r
            JOIN games g ON r.game_id = g.id
            WHERE r.local_file_path IS NULL OR r.local_file_path = ''
        """)
        tasks = cur.fetchall()

    if not tasks:
        logger.info("No pending downloads found.")
        return

    logger.info(f"Found {len(tasks)} rulebooks to download.")
    
    processor = RulebookProcessor()
    processor._start_browser()
    
    for task in tasks:
        r_id = task[0]
        title = task[1]
        url = task[2]
        bgg_id = task[4]
        
        logger.info(f"Downloading: {title} (ID: {r_id})")
        
        try:
            content, ftype = processor._browser_download(url)
            
            if content:
                # Save
                save_dir = Path("d:/Downloads/board_game_scraper-2.23.1/translation-service/downloads") / str(bgg_id)
                save_dir.mkdir(parents=True, exist_ok=True)
                
                safe_title = "".join([c for c in title if c.isalnum() or c in (' ', '-', '_')]).rstrip()
                filename = f"{r_id}_{safe_title}.{ftype}"
                local_path = save_dir / filename
                
                local_path.write_bytes(content)
                
                # Update DB
                with db.conn.cursor() as cur:
                    cur.execute("UPDATE rulebooks SET local_file_path = %s WHERE id = %s", (str(local_path), r_id))
                    db.conn.commit()
                
                logger.info(f"SUCCESS: Saved to {local_path}")
            else:
                logger.error(f"FAILED to download: {title}")
                
        except Exception as e:
            logger.error(f"Error processing {title}: {e}")
            # Try to restart browser on error
            try:
                processor._close_browser()
                processor._start_browser()
            except:
                pass

    processor._close_browser()
    logger.info("Batch download completed.")

if __name__ == "__main__":
    download_pending_rulebooks()
