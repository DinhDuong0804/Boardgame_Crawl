"""
Quick test script for BrowserGeminiTranslator
Tests if the browser can load Gemini properly without --no-sandbox warning
"""
import logging
from browser_translator import BrowserGeminiTranslator

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

def main():
    logger.info("=== Testing BrowserGeminiTranslator ===")
    logger.info("This will open Chrome and navigate to Gemini")
    logger.info("Watch for:")
    logger.info("  ✅ NO '--no-sandbox' warning")
    logger.info("  ✅ 'Found Gemini input with selector: ...'")
    logger.info("  ✅ 'Gemini Web is ready!'")
    logger.info("")
    
    translator = BrowserGeminiTranslator()
    
    try:
        # Load Gemini (this should open browser and wait for login)
        logger.info("Loading Gemini browser...")
        translator.load()
        
        logger.info("")
        logger.info("✅ SUCCESS! Gemini loaded successfully!")
        logger.info(f"✅ Using selector: {getattr(translator, 'input_selector', 'unknown')}")
        logger.info("")
        
        # Test a simple translation
        test_text = "Welcome to the game! Roll the dice and move your token."
        logger.info(f"Testing translation with: '{test_text}'")
        
        result = translator.translate(test_text)
        
        logger.info("")
        logger.info("=== Translation Result ===")
        logger.info(f"Original: {test_text}")
        logger.info(f"Translated: {result}")
        logger.info("")
        logger.info("✅ Test completed successfully!")
        
    except Exception as e:
        logger.error(f"❌ Test failed: {e}", exc_info=True)
    finally:
        # Keep browser open for 10 seconds to see the result
        logger.info("Browser will close in 10 seconds...")
        import time
        time.sleep(10)

if __name__ == "__main__":
    main()
