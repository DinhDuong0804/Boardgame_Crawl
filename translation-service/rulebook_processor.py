"""
Rulebook Processor - Browser Automation Edition
Downloads PDF/DOC files from BGG using Playwright to handle login & redirects
"""
import logging
import re
import time
from pathlib import Path
from urllib.parse import urlparse, urljoin
import config
from translator import get_translator
from playwright.sync_api import sync_playwright, TimeoutError as PlaywrightTimeoutError

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)


class RulebookProcessor:
    """
    Process rulebook files from BGG using Browser Automation:
    1. Login to BGG (headless browser)
    2. Navigate to file page
    3. Trigger download
    4. Extract -> Translate -> Markdown
    """
    
    def __init__(self, translator=None):
        if translator:
            self.translator = translator
        else:
            self.translator = get_translator()
        # Browser instance is created per-task or reused? 
        # For simplicity and stability, we'll create a fresh context per process call
        # or manage a persistent browser. Let's do persistent for efficiency.
        self.playwright = None
        self.browser = None
        self.context = None
        
    def set_shared_playwright(self, playwright_instance):
        """Use an existing Playwright instance"""
        self.playwright = playwright_instance
        
    def _start_browser(self):
        """Start browser and login"""
        if self.browser:
            return

        logger.info("Starting Playwright browser...")
        if not self.playwright:
             self.playwright = sync_playwright().start()
        
        # Launch headless - NOTE: If sharing playwright from BrowserTranslator (which uses 'chrome' channel),
        # we should be careful. But launch() creates a new browser process usually.
        self.browser = self.playwright.chromium.launch(headless=True)
        
        # Create context with English locale
        self.context = self.browser.new_context(
            user_agent='Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
            locale='en-US'
        )
        
        # Login
        self._login()

    def _login(self):
        """Login to BGG via UI"""
        if not config.BGG_USERNAME or not config.BGG_PASSWORD:
            logger.warning("No BGG credentials provided. Skipping login.")
            return

        page = self.context.new_page()
        try:
            logger.info("Navigating to BGG login page...")
            page.goto("https://boardgamegeek.com/login", timeout=60000)
            
            # Check if already logged in (optional, but good practice)
            if "Sign Out" in page.content():
                logger.info("Already logged in.")
                page.close()
                return

            logger.info(f"Logging in as {config.BGG_USERNAME}...")
            
            # Use selectors for BGG's new login form
            # Sometimes BGG has a legacy or new login. 
            # New login usually has id="inputUsername" or name="username"
            
            # Fill username
            page.fill("input[name='username']", config.BGG_USERNAME)
            page.fill("input[name='password']", config.BGG_PASSWORD)
            
            # Click login button (it's often a submit type)
            # Try specific selector or generic button
            with page.expect_navigation(timeout=30000):
                page.click("button[type='submit'], input[type='submit'], .btn-primary")
            
            # Check success (look for 'Sign Out' or username in header)
            # BGG usually redirects to homepage or dashboard
            logger.info("Login submitted. Verifying...")
            
            # Wait a bit for cloudflare or redirects
            page.wait_for_load_state("networkidle")
            
            if config.BGG_USERNAME in page.content() or "Sign Out" in page.content():
                logger.info("BGG Login successful via Browser!")
            else:
                logger.warning("Login verification failed (username not found in page). Continuing anyway...")
                
        except Exception as e:
            logger.error(f"Error during browser login: {e}")
            # Save screenshot for debug
            try:
                page.screenshot(path=config.OUTPUT_DIR / "login_error.png")
            except:
                pass
        finally:
            page.close()

    def _close_browser(self):
        """Cleanup browser resources"""
        if self.context:
            self.context.close()
        if self.browser:
            self.browser.close()
        if self.playwright:
            self.playwright.stop()
        
        self.context = None
        self.browser = None
        self.playwright = None

    def process_rulebook(
        self, 
        bgg_id: int, 
        game_name: str,
        rulebook_url: str, 
        rulebook_title: str,
        rulebook_id: int = None
    ) -> Optional[Dict]:
        """
        Process a single rulebook
        """
        logger.info(f"Processing rulebook: {rulebook_title}")
        
        file_content = None
        file_type = 'unknown'
        local_path = None
        
        # Check database for local file
        if rulebook_id:
            try:
                from database import get_database
                db = get_database()
                rb_info = db.get_rulebook(rulebook_id)
                if rb_info and rb_info.get('local_file_path'):
                    local_path = Path(rb_info['local_file_path'])
                    if local_path.exists():
                        logger.info(f"Found local file: {local_path}")
                        file_content = local_path.read_bytes()
                        # Guess type
                        if str(local_path).endswith('.pdf'): file_type = 'pdf'
                        elif str(local_path).endswith('.docx'): file_type = 'docx'
            except Exception as e:
                logger.warning(f"Failed to check local file: {e}")
        
        try:
            # Start browser if needed (if no local file)
            if not file_content:
                self._start_browser()
                
                # Step 1 & 2: Download directly using browser
                file_content, file_type = self._browser_download(rulebook_url)
                
                if not file_content:
                    logger.error("Download failed.")
                    return None
                
                # SAVE TO DISK immediately
                if rulebook_id:
                    try:
                        from database import get_database
                        db = get_database()
                        
                        save_dir = Path("d:/Downloads/board_game_scraper-2.23.1/translation-service/downloads") / str(bgg_id)
                        save_dir.mkdir(parents=True, exist_ok=True)
                        
                        safe_title = "".join([c for c in rulebook_title if c.isalnum() or c in (' ', '-', '_')]).rstrip()
                        filename = f"{rulebook_id}_{safe_title}.{file_type}"
                        local_path = save_dir / filename
                        
                        local_path.write_bytes(file_content)
                        
                        # Update DB
                        with db.conn.cursor() as cur:
                            cur.execute("UPDATE rulebooks SET local_file_path = %s WHERE id = %s", (str(local_path), rulebook_id))
                            db.conn.commit()
                        logger.info(f"Saved local file to {local_path}")
                    except Exception as save_err:
                        logger.warning(f"Could not save local file: {save_err}")
                
            # Step 3: Extract text from file
            english_text = self._extract_text(file_content, file_type)
            if not english_text or len(english_text.strip()) < 100:
                logger.warning(f"Insufficient text extracted from {rulebook_title}")
                return None
            
            # Step 4: Translate to Vietnamese
            logger.info(f"Translating {len(english_text)} characters...")
            vietnamese_text = self.translator.translate(english_text)
            
            # Step 5: Create Markdown
            markdown_content = self._create_markdown(
                game_name=game_name,
                bgg_id=bgg_id,
                rulebook_title=rulebook_title,
                english_text=english_text,
                vietnamese_text=vietnamese_text
            )
            
            # Step 6: Save to file
            output_path = self._save_markdown(bgg_id, game_name, rulebook_title, markdown_content)
            
            return {
                "bgg_id": bgg_id,
                "rulebook_title": rulebook_title,
                "original_url": rulebook_url,
                "output_file": str(output_path),
                "char_count_en": len(english_text),
                "char_count_vi": len(vietnamese_text)
            }
            
        except Exception as e:
            logger.error(f"Error processing rulebook {rulebook_title}: {e}")
            return None
            
    def _browser_download(self, url: str) -> tuple[Optional[bytes], str]:
        """Download file using Playwright's expect_download"""
        page = self.context.new_page()
        try:
            logger.info(f"Navigating to: {url}")
            
            # BGG file pages usually have a download link or start download automatically
            # If it's a filepage/{id}, we need to click the download link
            # If it's a direct download URL, it might trigger immediately
            
            if "/filepage/" in url and "/download/" not in url:
                # E.g. https://boardgamegeek.com/filepage/226279/rules
                page.goto(url, timeout=60000)
                
                # Look for download button/link
                # Usually .btn-primary or contains "Download"
                # Need to be smart here.
                # Update selector to match new BGG format - try multiple patterns
                download_selector = "a[href*='/file/download_redirect/'], a[href*='/filepage/download/'], a.btn-primary, button:has-text('Download'), a:has-text('Download')"
                try:
                    # Wait for selector
                    page.wait_for_selector(download_selector, timeout=10000)
                    
                    # Get all download buttons
                    buttons = page.query_selector_all(download_selector)
                    if not buttons:
                        logger.warning("No download buttons found with selectors.")
                        return None, 'unknown'
                        
                    logger.info(f"Found {len(buttons)} download buttons. Clicking the first one...")
                    
                    # Setup download handler before clicking
                    with page.expect_download(timeout=60000) as download_info:
                        # Click the first button (usually the main file)
                        # We force click because sometimes elements are covered
                        buttons[0].click(force=True)
                        
                    download = download_info.value
                    path = download.path()
                    suggested_filename = download.suggested_filename
                    logger.info(f"Downloaded: {suggested_filename}")
                    
                    # Read content
                    file_content = Path(path).read_bytes()
                    
                    # Detect type based on filename
                    if suggested_filename.lower().endswith('.pdf'):
                        return file_content, 'pdf'
                    elif suggested_filename.lower().endswith(('.doc', '.docx')):
                        return file_content, 'docx'
                        
                    return file_content, 'unknown'
                    
                except Exception as click_err:
                    logger.warning(f"Click download failed: {click_err}. Trying strict API download fallback...")
                    # Fallback: Extract the href and download via API Request (bypassing weird download events)
                    try:
                         # Re-query to be safe
                         btn = page.query_selector(download_selector)
                         if btn:
                             href = btn.get_attribute("href")
                             if href:
                                 direct_url = urljoin(url, href)
                                 logger.info(f"Fallback downloading via API from: {direct_url}")
                                 
                                 # Use Playwright's APIRequestContext which shares cookies
                                 resp = self.context.request.get(direct_url, timeout=60000)
                                 if resp.ok:
                                     # Guess extension from content-type or url
                                     content_type = resp.headers.get("content-type", "")
                                     file_bytes = resp.body()
                                     
                                     ftype = 'unknown'
                                     if 'pdf' in content_type or direct_url.endswith('.pdf'):
                                         ftype = 'pdf'
                                     elif 'word' in content_type or direct_url.endswith('.docx'):
                                         ftype = 'docx'
                                     else:
                                         # Magic bytes check
                                         if file_bytes[:4] == b'%PDF': ftype = 'pdf'
                                         elif file_bytes[:2] == b'PK': ftype = 'docx'

                                     logger.info(f"API Download success. Size: {len(file_bytes)} bytes. Type: {ftype}")
                                     return file_bytes, ftype
                                 else:
                                     logger.error(f"API Download failed: {resp.status} {resp.status_text}")
                    except Exception as api_err:
                        logger.error(f"API Fallback failed: {api_err}")
                        
                    # If all else fails
                    return None, 'unknown'
                        
            else:
                # Direct download URL or different structure
                try:
                    # In Playwright, if you goto a URL that responds with a file (attachment), 
                    # it triggers a download event.
                    with page.expect_download(timeout=60000) as download_info:
                        page.goto(url)
                        
                    download = download_info.value
                    logger.info(f"Downloaded direct: {download.suggested_filename}")
                    
                    file_content = Path(download.path()).read_bytes()
                    
                    if download.suggested_filename.lower().endswith('.pdf'):
                        return file_content, 'pdf'
                    
                    return file_content, 'unknown'
                    
                except Exception as direct_err:
                    logger.error(f"Direct download failed: {direct_err}")
                    return None, 'unknown'

        except Exception as e:
            logger.error(f"Browser download error: {e}")
            return None, 'unknown'
        finally:
            page.close()

    def _extract_text(self, content: bytes, file_type: str) -> Optional[str]:
        """Extract text from PDF or DOC file"""
        try:
            if file_type == 'pdf':
                return self._extract_from_pdf(content)
            elif file_type in ('doc', 'docx'):
                return self._extract_from_docx(content)
            else:
                # Try magic bytes as fallback
                if content[:4] == b'%PDF':
                    return self._extract_from_pdf(content)
                elif content[:2] == b'PK':
                    return self._extract_from_docx(content)
                    
                logger.warning(f"Unsupported or unknown file type: {file_type}")
                return None
        except Exception as e:
            logger.error(f"Text extraction error: {e}")
            return None
    
    def _extract_from_pdf(self, content: bytes) -> Optional[str]:
        """Extract text from PDF using PyMuPDF"""
        try:
            import fitz  # PyMuPDF
            
            doc = fitz.open(stream=content, filetype="pdf")
            text_parts = []
            
            page_count = min(len(doc), config.MAX_PDF_PAGES)
            
            for page_num in range(page_count):
                page = doc[page_num]
                text = page.get_text()
                if text.strip():
                    text_parts.append(text)
            
            doc.close()
            
            full_text = "\n\n".join(text_parts)
            return self._clean_text(full_text)
            
        except Exception as e:
            logger.error(f"PDF extraction error: {e}")
            return None
    
    def _extract_from_docx(self, content: bytes) -> Optional[str]:
        """Extract text from DOCX using python-docx"""
        try:
            from docx import Document
            from io import BytesIO
            
            doc = Document(BytesIO(content))
            text_parts = []
            
            for para in doc.paragraphs:
                if para.text.strip():
                    text_parts.append(para.text)
            
            full_text = "\n\n".join(text_parts)
            return self._clean_text(full_text)
            
        except Exception as e:
            logger.error(f"DOCX extraction error: {e}")
            return None
    
    def _clean_text(self, text: str) -> str:
        """Clean extracted text"""
        # Remove excessive whitespace
        text = re.sub(r'\n{3,}', '\n\n', text)
        text = re.sub(r' {2,}', ' ', text)
        
        # Remove page numbers and headers that repeat
        lines = text.split('\n')
        cleaned_lines = []
        seen_short_lines = set()
        
        for line in lines:
            stripped = line.strip()
            # Skip very short repeated lines (likely headers/footers)
            if len(stripped) < 30:
                if stripped in seen_short_lines:
                    continue
                seen_short_lines.add(stripped)
            cleaned_lines.append(line)
        
        return '\n'.join(cleaned_lines).strip()
    
    def _create_markdown(
        self, 
        game_name: str, 
        bgg_id: int,
        rulebook_title: str,
        english_text: str,
        vietnamese_text: str
    ) -> str:
        """Create bilingual Markdown document"""
        
        provider_name = "Google Gemini" if "gemini" in config.TRANSLATION_PROVIDER else "OPUS-MT (Helsinki-NLP)"
        
        md = f"""# {game_name} - Luật Chơi / Rules

**BGG ID:** {bgg_id}  
**Nguồn / Source:** {rulebook_title}  
**Dịch bởi / Translated by:** {provider_name}

---

## Tiếng Việt

{vietnamese_text}

---

## English (Original)

{english_text}
"""
        return md
    
    def _save_markdown(
        self, 
        bgg_id: int, 
        game_name: str, 
        rulebook_title: str,
        content: str
    ) -> Path:
        """Save Markdown to file"""
        # Create safe filename
        safe_name = re.sub(r'[^\w\s-]', '', game_name.lower())
        safe_name = re.sub(r'\s+', '_', safe_name)[:50]
        
        safe_title = re.sub(r'[^\w\s-]', '', rulebook_title.lower())
        safe_title = re.sub(r'\s+', '_', safe_title)[:30]
        
        filename = f"{bgg_id}_{safe_name}_{safe_title}.md"
        output_path = config.RULEBOOKS_OUTPUT_DIR / filename
        
        output_path.write_text(content, encoding='utf-8')
        logger.info(f"Saved: {output_path}")
        
        return output_path
