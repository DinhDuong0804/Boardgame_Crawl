import re
from pathlib import Path

def test_save(bgg_id, game_name, rulebook_title):
    safe_name = re.sub(r'[^\w\s-]', '', game_name.lower())
    safe_name = re.sub(r'\s+', '_', safe_name)[:50]
    
    safe_title = re.sub(r'[^\w\s-]', '', rulebook_title.lower())
    safe_title = re.sub(r'\s+', '_', safe_title)[:30]
    
    filename = f"{bgg_id}_{safe_name}_{safe_title}.md"
    print(f"Generated filename: {filename}")

test_save(1, "Brass: Birmingham", "Brass Birmingham reference sheet and context notes")
