import database
import logging

logging.basicConfig(level=logging.INFO)

def check_db():
    db = database.get_database()
    db.connect()
    with db.conn.cursor() as cur:
        cur.execute("SELECT id, title, local_file_path, status FROM rulebooks WHERE game_id = 2")
        rows = cur.fetchall()
        for row in rows:
            print(f"ID: {row[0]} | Title: {row[1]} | Status: {row[3]}")
            print(f"   -> Path: {row[2]}")

if __name__ == "__main__":
    check_db()
