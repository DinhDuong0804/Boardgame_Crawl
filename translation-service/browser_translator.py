import logging
import time
import os
import pyperclip
from playwright.sync_api import sync_playwright
from translator import BaseTranslator
import config

logger = logging.getLogger(__name__)

class BrowserGeminiTranslator(BaseTranslator):
    """
    Automates gemini.google.com via Playwright
    """
    def __init__(self):
        self.playwright = None
        self.browser = None
        self.page = None
        # Store profile in 'gemini_browser_profile' to persist login
        self.user_data_dir = os.path.join(config.BASE_DIR, "gemini_browser_profile")
        
    def load(self):
        if self.page:
            logger.info("Browser Translator already loaded. Skipping initialization.")
            return

        logger.info("Initializing Browser Translator (Gemini Web)...")
        self.playwright = sync_playwright().start()
        
        # Launch persistent context (saves cookies/login)
        # Headless=False so user can see and login if needed
        self.browser = self.playwright.chromium.launch_persistent_context(
            user_data_dir=self.user_data_dir,
            headless=False, 
            channel="chrome", # Try to use installed Chrome
            args=["--start-maximized", "--disable-blink-features=AutomationControlled"]
        )
        
        self.page = self.browser.pages[0]
        self.page.goto("https://gemini.google.com/app")
        
        # Check if logged in
        try:
            # Wait for either the chat input or the login button
            # Chat input usually has role="textbox" and contenteditable="true"
            logger.info("Waiting for Gemini UI...")
            self.page.wait_for_selector("div[contenteditable='true']", timeout=10000)
            logger.info("Gemini Web is ready (Logged in).")
        except:
            logger.warning("IMPORTANT: It seems you are NOT logged in.")
            logger.warning("Please Login to Google in the opened browser window.")
            logger.warning("Waiting 60 seconds for you to login...")
            try:
                self.page.wait_for_selector("div[contenteditable='true']", timeout=60000)
                logger.info("Login detected! Proceeding...")
            except:
                logger.error("Login timeout. The script might fail.")

    def translate(self, text: str) -> str:
        if not text or not text.strip():
            return ""

        chunks = self._split_text(text, 6000) # Gemini Web can handle larger context
        translated_chunks = []
        
        # Reset chat for new file/session to avoid context pollution? 
        # Actually, keeping context is good, but maybe "New Chat" is safer for distinct files.
        # Let's try to click "New Chat" if possible, or just continue.
        try:
            # Selector for "New chat" button (often has text "New chat" or icon)
            # This is fragile, so we wrap in try/catch
            new_chat_btn = self.page.query_selector("div[data-test-id='new-chat-button']")
            if new_chat_btn:
                new_chat_btn.click()
                time.sleep(1)
        except:
            pass

        for i, chunk in enumerate(chunks):
            try:
                logger.info(f"Translating chunk {i+1}/{len(chunks)} via Browser...")
                
                prompt = f"""
Dịch văn bản sau sang Tiếng Việt chuẩn Board Game.
Quy tắc:
- Giữ nguyên tên riêng/thuật ngữ Game (ví dụ: Round, Turn, Token, Appeal Track, Conservation Project, Setup, Zoo Map...).
- Dịch trôi chảy, tự nhiên.
- CHỈ TRẢ VỀ KẾT QUẢ DỊCH.

Nội dung:
{chunk}
"""
                # Type prompt
                input_box = self.page.wait_for_selector("div[contenteditable='true']", state="visible")
                input_box.click()
                
                # Use clipboard to paste large text (faster than typing)
                pyperclip.copy(prompt)
                
                # Press Control+V
                self.page.keyboard.press("Control+V")
                time.sleep(0.5)
                self.page.keyboard.press("Enter")
                
                # Wait for response
                # We need to wait until the "Stop generating" button disappears or "Copy" icon appears
                # Gemini UI is complex. A simple trick: wait for the latest response container to stabilize
                time.sleep(3) # Wait for generation to start
                
                # Wait for generation to end.
                # Usually there's a spinner or 'responding' state. 
                # Or we can simply wait for a reasonable time or check for the 'copy' button of the last message.
                
                # Better approach: Polling for the copy button of the *last* turn
                self.page.wait_for_function("""
                    () => {
                        const turns = document.querySelectorAll('message-content');
                        if (turns.length === 0) return false;
                        const lastTurn = turns[turns.length - 1];
                        return !lastTurn.closest('model-response')?.classList.contains('sparkle-thinking'); // pseudo-code check
                    }
                """, timeout=5000) 
                
                # Fallback wait (Gemini is fast but variable)
                time.sleep(10) 
                
                # Get text
                # We extract text from the last response bubble
                # Try multiple selectors for better robustness against UI changes
                response = self.page.evaluate("""
                    () => {
                        // Helper to get text from an element
                        function getText(el) {
                            if (!el) return "";
                            return el.innerText || el.textContent || "";
                        }

                        // STRICTER SELECTOR: Only look for model responses, ignore user prompts
                        // Angular structure often involves <model-response> or specific classes
                        
                        // Strategy: Get all 'message-content'. 
                        // Filter out those that start with "Dịch văn bản sau" (our prompt)
                        const allContents = Array.from(document.querySelectorAll('message-content, .model-response-text'));
                        
                        // Reverse to find the latest valid response
                        for (let i = allContents.length - 1; i >= 0; i--) {
                            const text = getText(allContents[i]);
                            // Relaxed check: just not empty and not our prompt
                            // Also ignore "Showing thought process" or similar UI texts
                            if (text.length > 5 && !text.trim().startsWith("Dịch văn bản sau")) {
                                return text;
                            }
                        }
                        
                        return "";
                    }
                """)
                
                logger.info(f"Extracted text length: {len(response)}")
                if len(response) > 0:
                    logger.info(f"Snippet: {response[:50]}...")

                # If automated extraction fails, we can try to find the "Copy" button
                if not response:
                    logger.warning("JS extraction empty or only found prompts. Waiting more...")
                    time.sleep(5)
                    # Retry with simpler selector but still trying to avoid prompt
                    response = self.page.evaluate("""
                        () => {
                             // Fallback: look for ANY paragraph that contains Vietnamese keywords
                             // This is a desperate measure
                             const ps = document.querySelectorAll('p, message-content');
                             for (let i = ps.length - 1; i >= 0; i--) {
                                 const t = ps[i].innerText;
                                 if (t.includes('Ark Nova') && !t.includes('Dịch văn bản sau')) return t;
                             }
                             return '';
                        }
                    """)

                cleaned = self._clean_response(response)
                translated_chunks.append(cleaned)
                
                # Limit speed
                time.sleep(2)

            except Exception as e:
                logger.error(f"Browser translation error: {e}")
                translated_chunks.append(chunk)

        return "\n\n".join(translated_chunks)

    def _clean_response(self, text):
        # Remove Gemini's conversational filler if any
        lines = text.split('\n')
        # Filter out lines like "Here is the translation:" if needed
        return text

    def _split_text(self, text, max_length):
        # Reuse simple splitting
        parts = text.split('\n\n')
        chunks = []
        current = []
        curr_len = 0
        for p in parts:
            if curr_len + len(p) > max_length:
                chunks.append("\n\n".join(current))
                current = [p]
                curr_len = len(p)
            else:
                current.append(p)
                curr_len += len(p)
        if current:
            chunks.append("\n\n".join(current))
        return chunks if chunks else [text]
