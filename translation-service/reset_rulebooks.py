from database import get_database

def reset_rulebooks():
    try:
        db = get_database()
        db.connect()
        with db.conn.cursor() as cur:
            # Force reset all rulebooks for Ark Nova (game_id=2)
            cur.execute("UPDATE rulebooks SET status = 'pending' WHERE game_id = 2")
            rows = cur.rowcount
            
            # Also reset game translation status if needed
            cur.execute("UPDATE game_translations SET status = 'pending' WHERE game_id = 2")
            
            db.conn.commit()
            print(f"Force reset {rows} rulebooks for Game 2 to pending")
    except Exception as e:
        print(f"Error: {e}")
    finally:
        if db:
            db.close()

if __name__ == "__main__":
    reset_rulebooks()
