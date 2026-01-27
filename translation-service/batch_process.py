"""
Batch Translation Processor
Reads bgg_with_rulebooks.jsonl, translates name + description,
processes rulebooks, outputs translated data
"""
import json
import logging
import time
from pathlib import Path
from typing import Dict, List, Set
from datetime import datetime

from tqdm import tqdm

import config
from translator import get_translator
from rulebook_processor import RulebookProcessor

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)


class TranslationState:
    """Manage translation progress state for resume capability"""
    
    def __init__(self, state_file: Path = config.STATE_FILE):
        self.state_file = state_file
        self.processed_games: Set[int] = set()
        self.processed_rulebooks: Set[str] = set()
        self.load()
    
    def load(self):
        """Load state from file"""
        if self.state_file.exists():
            try:
                data = json.loads(self.state_file.read_text())
                self.processed_games = set(data.get('processed_games', []))
                self.processed_rulebooks = set(data.get('processed_rulebooks', []))
                logger.info(f"Loaded state: {len(self.processed_games)} games, {len(self.processed_rulebooks)} rulebooks")
            except Exception as e:
                logger.warning(f"Could not load state: {e}")
    
    def save(self):
        """Save state to file"""
        data = {
            'processed_games': list(self.processed_games),
            'processed_rulebooks': list(self.processed_rulebooks),
            'last_updated': datetime.now().isoformat()
        }
        self.state_file.write_text(json.dumps(data, indent=2))
    
    def mark_game_processed(self, bgg_id: int):
        self.processed_games.add(bgg_id)
        
    def mark_rulebook_processed(self, rulebook_key: str):
        self.processed_rulebooks.add(rulebook_key)
        
    def is_game_processed(self, bgg_id: int) -> bool:
        return bgg_id in self.processed_games
        
    def is_rulebook_processed(self, rulebook_key: str) -> bool:
        return rulebook_key in self.processed_rulebooks


class BatchProcessor:
    """
    Main batch processor for translating game data
    """
    
    def __init__(self):
        self.translator = get_translator()
        self.rulebook_processor = RulebookProcessor()
        self.state = TranslationState()
        
    def run(
        self, 
        translate_games: bool = True,
        process_rulebooks: bool = True,
        max_games: int = None,
        max_rulebooks_per_game: int = 2
    ):
        """
        Run batch translation
        
        Args:
            translate_games: Whether to translate name/description
            process_rulebooks: Whether to process rulebook PDFs
            max_games: Limit number of games to process (None = all)
            max_rulebooks_per_game: Max rulebooks to process per game
        """
        logger.info("=" * 60)
        logger.info("BATCH TRANSLATION PROCESSOR")
        logger.info("=" * 60)
        
        # Load input data
        games = self._load_input()
        if max_games:
            games = games[:max_games]
            
        logger.info(f"Loaded {len(games)} games to process")
        
        # Preload translation model
        logger.info("Loading translation model...")
        self.translator.load()
        logger.info("Model ready!")
        
        # Process games
        processed_count = 0
        
        for game in tqdm(games, desc="Translating games"):
            bgg_id = game.get('bgg_id')
            name = game.get('name', '')
            
            # Skip if already processed
            if self.state.is_game_processed(bgg_id):
                continue
            
            try:
                # Translate name and description
                if translate_games:
                    game = self._translate_game_info(game)
                
                # Process rulebooks
                if process_rulebooks:
                    rulebooks = game.get('rulebook_urls', [])[:max_rulebooks_per_game]
                    rulebook_results = []
                    
                    for rb in rulebooks:
                        rb_key = f"{bgg_id}_{rb.get('url', '')}"
                        
                        if self.state.is_rulebook_processed(rb_key):
                            continue
                            
                        result = self.rulebook_processor.process_rulebook(
                            bgg_id=bgg_id,
                            game_name=name,
                            rulebook_url=rb.get('url', ''),
                            rulebook_title=rb.get('title', 'Rules')
                        )
                        
                        if result:
                            rulebook_results.append(result)
                            self.state.mark_rulebook_processed(rb_key)
                        
                        # Rate limiting
                        time.sleep(config.DOWNLOAD_DELAY)
                    
                    # Add rulebook results to game data
                    if rulebook_results:
                        game['rulebook_translations'] = rulebook_results
                
                # Save translated game
                self._save_game(game)
                self.state.mark_game_processed(bgg_id)
                self.state.save()
                
                processed_count += 1
                
            except Exception as e:
                logger.error(f"Error processing game {bgg_id} ({name}): {e}")
                continue
        
        logger.info("=" * 60)
        logger.info(f"COMPLETED! Processed {processed_count} games")
        logger.info(f"Output: {config.OUTPUT_JSONL}")
        logger.info(f"Rulebooks: {config.RULEBOOKS_OUTPUT_DIR}")
        logger.info("=" * 60)
    
    def _load_input(self) -> List[Dict]:
        """Load games from input JSONL"""
        games = []
        
        if not config.INPUT_JSONL.exists():
            logger.error(f"Input file not found: {config.INPUT_JSONL}")
            return games
        
        with open(config.INPUT_JSONL, 'r', encoding='utf-8') as f:
            for line in f:
                line = line.strip()
                if line:
                    try:
                        game = json.loads(line)
                        games.append(game)
                    except json.JSONDecodeError:
                        continue
        
        return games
    
    def _translate_game_info(self, game: Dict) -> Dict:
        """Translate name and description"""
        name = game.get('name', '')
        description = game.get('description', '')
        
        # Translate name
        if name:
            logger.debug(f"Translating name: {name}")
            game['name_vi'] = self.translator.translate(name)
        
        # Translate description
        if description:
            # Truncate very long descriptions
            desc_to_translate = description[:5000] if len(description) > 5000 else description
            logger.debug(f"Translating description ({len(desc_to_translate)} chars)")
            game['description_vi'] = self.translator.translate(desc_to_translate)
        
        game['translated_at'] = datetime.now().isoformat()
        
        return game
    
    def _save_game(self, game: Dict):
        """Append translated game to output JSONL"""
        with open(config.OUTPUT_JSONL, 'a', encoding='utf-8') as f:
            json_line = json.dumps(game, ensure_ascii=False)
            f.write(json_line + '\n')


def main():
    """Main entry point"""
    import argparse
    
    parser = argparse.ArgumentParser(description='Batch translate board game data')
    parser.add_argument('--games-only', action='store_true', help='Only translate name/description, skip rulebooks')
    parser.add_argument('--rulebooks-only', action='store_true', help='Only process rulebooks, skip game translation')
    parser.add_argument('--max-games', type=int, default=None, help='Maximum number of games to process')
    parser.add_argument('--max-rulebooks', type=int, default=2, help='Max rulebooks per game (default: 2)')
    
    args = parser.parse_args()
    
    processor = BatchProcessor()
    
    processor.run(
        translate_games=not args.rulebooks_only,
        process_rulebooks=not args.games_only,
        max_games=args.max_games,
        max_rulebooks_per_game=args.max_rulebooks
    )


if __name__ == "__main__":
    main()
