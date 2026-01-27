from database import get_database
import logging
from psycopg2.extras import RealDictCursor

logging.basicConfig(level=logging.INFO)
db = get_database()
db.connect()

def check_columns():
    with db.conn.cursor(cursor_factory=RealDictCursor) as cur:
        cur.execute("""
            SELECT column_name, data_type 
            FROM information_schema.columns 
            WHERE table_name = 'rulebooks';
        """)
        columns = cur.fetchall()
        print("Columns in rulebooks table:")
        for col in columns:
            print(f"- {col['column_name']} ({col['data_type']})")

if __name__ == "__main__":
    check_columns()
