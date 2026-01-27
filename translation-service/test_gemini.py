from translator import get_translator
import logging
import os
import time

logging.basicConfig(level=logging.INFO)

def test():
    t = get_translator()
    t.load()
    
    # Check key
    key = os.getenv("GEMINI_API_KEY", "")
    print(f"Key loaded: {key[:5]}...{key[-5:] if len(key)>10 else ''}")
    print(f"Provider: {os.getenv('TRANSLATION_PROVIDER')}")
    
    text = "Hello World. This is a board game rulebook."
    
    print("--- Original ---")
    print(text)
    print("\n--- Translated ---")
    try:
        res = t.translate(text)
        print(f"Result: '{res}'")
    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    test()
