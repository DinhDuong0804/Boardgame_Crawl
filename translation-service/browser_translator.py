import logging
import time
import os
from pathlib import Path
from playwright.sync_api import sync_playwright
from playwright_stealth import Stealth

from translator import BaseTranslator

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

class BrowserGeminiTranslator(BaseTranslator):
    def __init__(self):
        self.playwright = None
        self.browser = None
        self.context = None
        self.page = None
        self.is_loaded = False
        # Create a persistent profile directory in the project folder
        self.profile_dir = Path("d:/Downloads/board_game_scraper-2.23.1/translation-service/gemini_profile")
        
    def load(self):
        if self.is_loaded: return
        logger.info("Initializing Browser (Gemini Web) - Headful Mode...")
        self.profile_dir.mkdir(parents=True, exist_ok=True)
        self.playwright = sync_playwright().start()
        
        # Launch with PERSISTENT context and HIDE AUTOMATION BAR
        self.context = self.playwright.chromium.launch_persistent_context(
            user_data_dir=str(self.profile_dir.absolute()),
            channel="chrome", # Use real Chrome
            headless=False,
            viewport=None, # Allow full window size
            # CRITICAL: This hides the "Chrome is being controlled..." bar
            ignore_default_args=["--enable-automation"], 
            args=[
                '--disable-blink-features=AutomationControlled',
                '--no-sandbox',
                '--disable-infobars',
                '--start-maximized',
                '--disable-dev-shm-usage',
                '--no-first-run',
                '--no-service-autorun',
                '--password-store=basic'
            ]
        )
        self.page = self.context.pages[0] if self.context.pages else self.context.new_page()
        Stealth().apply_stealth_sync(self.page)
        
        try:
            logger.info("Navigating to Gemini...")
            self.page.goto("https://gemini.google.com/app", timeout=60000)
            
            # Wait for you to login manually if needed
            logger.info("Waiting for Gemini UI (up to 5 mins)...")
            try:
                self.page.wait_for_selector('div[role="textbox"]', timeout=300000)
                logger.info("Gemini Web is ready!")
                self.is_loaded = True
            except:
                logger.error("Timeout waiting for login. Please login in the browser window.")
                raise Exception("Manual Login Required")
        except Exception as e:
            logger.error(f"Failed to load Gemini: {e}")
            raise

    def translate(self, text: str) -> str:
        if not text: return ""
        if not self.is_loaded: self.load()
        
        # Scroll to bottom before starting
        self.page.keyboard.press("End")
        
        # Get count of messages before we send ours
        initial_count = self.page.evaluate("() => document.querySelectorAll('message-content').length")
        
        input_selector = 'div[role="textbox"]'
        # Prompt được tinh chỉnh theo yêu cầu mới: Chỉ giữ tên riêng
        prompt = f"Hãy dịch văn bản board game sau sang tiếng Việt mượt mà. Chỉ giữ nguyên Tên Riêng (Proper Names) bằng tiếng Anh, còn lại hãy dịch hết sang tiếng Việt phù hợp ngữ cảnh:\n\n{text}"
        
        logger.info("Pasting text into Gemini...")
        self.page.click(input_selector)
        
        # Use safe evaluation to set text (handles large strings better than fill)
        self.page.evaluate(f"(t) => {{ document.querySelector('{input_selector}').innerText = t; }}", prompt)
        self.page.dispatch_event(input_selector, 'input')
        
        time.sleep(1)
        self.page.press(input_selector, "Enter")
        
        logger.info("Gemini is thinking...")
        
        # Wait for the response to START
        try:
            self.page.wait_for_function(f"document.querySelectorAll('message-content').length > {initial_count}", timeout=30000)
        except: pass

        # Wait for generation to FINISH (Stable check)
        stop_selector = 'button[aria-label*="Ngừng"], button[aria-label*="Stop"]'
        wait_start = time.time()
        stable_seconds = 0
        while time.time() - wait_start < 600: # Max 10 mins
            is_generating = self.page.evaluate(f"() => {{ const b = document.querySelector('{stop_selector}'); return b && b.offsetParent !== null; }}")
            if not is_generating:
                stable_seconds += 1
                if stable_seconds >= 5: break # Stable for 5 seconds
            else:
                stable_seconds = 0
            time.sleep(1)
            
        logger.info("Response finished. Extracting...")
        time.sleep(2)
        
        # Extract ALL message content since our prompt
        js_code = f"""
            () => {{
                let containers = document.querySelectorAll('message-content');
                let result = "";
                for (let i = {initial_count}; i < containers.length; i++) {{
                    result += containers[i].innerText + "\\n\\n";
                }}
                return result.trim();
            }}
        """
        response = self.page.evaluate(js_code)
        
        if not response or len(response) < 100:
            logger.warning("Extraction failed or text too short. Trying fallback.")
            response = self.page.evaluate("() => document.querySelector('message-content:last-of-type')?.innerText || ''")
            
        logger.info(f"Captured {len(response)} characters.")
        
        # CLEANUP: Thay thế các ký tự xuống dòng lạ (LS, PS) bằng \n chuẩn để VS Code không báo lỗi
        response = response.replace('\u2028', '\n').replace('\u2029', '\n').replace('\r\n', '\n')
        
        return response

    def __del__(self):
        try:
            if self.context: self.context.close()
            if self.playwright: self.playwright.stop()
        except: pass
