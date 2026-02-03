import logging
import time
import os
from pathlib import Path
from playwright.sync_api import sync_playwright

try:
    from playwright_stealth import stealth_sync
    HAS_STEALTH = True
except ImportError:
    HAS_STEALTH = False

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
        self.profile_dir = Path(__file__).parent / "gemini_profile"
        
    def load(self):
        if self.is_loaded: return
        logger.info("Initializing Browser (Gemini Web)...")
        self.profile_dir.mkdir(parents=True, exist_ok=True)
        self.playwright = sync_playwright().start()
        
        # SUDO STEALTH: Khong dung channel="chrome" nua
        # Su dung Chromium mac dinh cua Playwright thuong bypass tot hon
        self.context = self.playwright.chromium.launch_persistent_context(
            user_data_dir=str(self.profile_dir.absolute()),
            headless=False,
            viewport=None,
            # Chi ignore duy nhat automation, khong dung cac flag gay canh bao
            ignore_default_args=["--enable-automation"],
            args=[
                '--start-maximized',
            ]
        )
        self.page = self.context.pages[0] if self.context.pages else self.context.new_page()
        
        if HAS_STEALTH:
            stealth_sync(self.page)
            
        # Trick: Ghi de webdriver ve false ngay lap tuc
        self.page.add_init_script("Object.defineProperty(navigator, 'webdriver', {get: () => undefined})")
        
        try:
            logger.info("Navigating to Gemini...")
            self.page.goto("https://gemini.google.com/app", timeout=90000)
            
            # Neu van bi chan dang nhap, hay de nguoi dung tu xu ly o day
            logger.info("Waiting for Gemini UI (Waiting for you to login if needed)...")
            
            # Doi cho den khi thay o nhap lieu - day la dau hieu login thanh cong
            try:
                self.page.wait_for_selector('div[role="textbox"]', timeout=300000) # 5 phut de login
                logger.info("Gemini Web is ready!")
                self.is_loaded = True
            except:
                logger.error("Timeout hoac bi chan. Hay kiem tra man hinh trinh duyet.")
                raise Exception("Manual Action Required")
                
        except Exception as e:
            logger.error(f"Failed to load Gemini: {e}")
            raise

    def translate(self, text: str) -> str:
        if not text: return ""
        if not self.is_loaded: self.load()
        
        input_selector = 'div[role="textbox"]'
        prompt = f"Hãy dịch văn bản board game sau sang tiếng Việt mượt mà. Chỉ giữ nguyên Tên Riêng (Proper Names) bằng tiếng Anh, còn lại hãy dịch hết sang tiếng Việt phù hợp ngữ cảnh:\n\n{text}"
        
        self.page.click(input_selector)
        self.page.keyboard.type(prompt, delay=10) # Type chậm như người thật
        time.sleep(1)
        self.page.keyboard.press("Enter")
        
        # ... (phần code extract giữ nguyên như cũ)
        return "Result here" # Rut ngon de test

    def __del__(self):
        try:
            if self.context: self.context.close()
            if self.playwright: self.playwright.stop()
        except: pass
