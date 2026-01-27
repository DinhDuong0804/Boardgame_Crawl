"""
Translation Engine
Supports multiple providers:
1. Local: Helsinki-NLP/opus-mt-en-vi (Offline, Basic quality)
2. Gemini: Google Generative AI (Online, High quality, Free tier available)
"""
import logging
import torch
from abc import ABC, abstractmethod
import os
import time

import config

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

class BaseTranslator(ABC):
    @abstractmethod
    def load(self):
        pass

    @abstractmethod
    def translate(self, text: str) -> str:
        pass

class LocalTranslator(BaseTranslator):
    """Uses Helsinki-NLP/opus-mt-en-vi"""
    def __init__(self):
        self.model = None
        self.tokenizer = None
        self.device = config.DEVICE

    def load(self):
        if self.model:
            return
            
        from transformers import MarianMTModel, MarianTokenizer
        
        logger.info(f"Loading local model: {config.MODEL_NAME}")
        self.tokenizer = MarianTokenizer.from_pretrained(config.MODEL_NAME)
        self.model = MarianMTModel.from_pretrained(config.MODEL_NAME).to(self.device)
        logger.info("Local model loaded successfully!")

    def translate(self, text: str) -> str:
        if not text or not text.strip():
            return ""
            
        if not self.model:
            self.load()

        # Split into chunks if too long
        chunks = self._split_text(text, config.CHUNK_SIZE)
        translated_chunks = []
        
        for chunk in chunks:
            try:
                inputs = self.tokenizer(chunk, return_tensors="pt", padding=True, truncation=True, max_length=512).to(self.device)
                
                with torch.no_grad():
                    translated = self.model.generate(**inputs)
                    
                decoded = self.tokenizer.batch_decode(translated, skip_special_tokens=True)[0]
                translated_chunks.append(decoded)
            except Exception as e:
                logger.error(f"Error translating chunk: {e}")
                translated_chunks.append(chunk)  # Keep original on error
                
        return " ".join(translated_chunks)

    def _split_text(self, text, max_length):
        words = text.split()
        chunks = []
        current_chunk = []
        current_length = 0
        
        for word in words:
            if current_length + len(word) + 1 > max_length:
                chunks.append(" ".join(current_chunk))
                current_chunk = [word]
                current_length = len(word)
            else:
                current_chunk.append(word)
                current_length += len(word) + 1
        
        if current_chunk:
            chunks.append(" ".join(current_chunk))
        return chunks

class GeminiTranslator(BaseTranslator):
    """Uses Google Gemini API"""
    def __init__(self):
        self.model = None
        self.api_key = os.getenv("GEMINI_API_KEY")

    def load(self):
        if not self.api_key:
            logger.error("GEMINI_API_KEY not found in environment variables")
            raise ValueError("GEMINI_API_KEY required")
            
        import google.generativeai as genai
        genai.configure(api_key=self.api_key)
        generation_config = genai.types.GenerationConfig(
            temperature=0.1,
            max_output_tokens=8192,
        )
        self.model = genai.GenerativeModel('models/gemini-2.0-flash', generation_config=generation_config)
        logger.info("Gemini 2.0 Flash model configured (State of the art)")

    def translate(self, text: str) -> str:
        if not text or not text.strip():
            return ""

        chunks = self._split_text(text, 5000) # Chia nhỏ hơn để dễ dịch và tránh lỗi
        translated_chunks = []

        prompt_template = """
BẠN LÀ MỘT DỊCH GIẢ BOARD GAME. DỊCH VĂN BẢN SAU SANG TIẾNG VIỆT.
GIỮ NGUYÊN THUẬT NGỮ: Round, Era, Token, Link, Brewery, Coal Mine, Iron Works, Cotton Mill, Manufacturer, Pottery, Build, Network, Develop.

VĂN BẢN (TIẾNG ANH):
{text}

BẢN DỊCH (TIẾNG VIỆT):
"""
        
        for i, chunk in enumerate(chunks):
            try:
                if i > 0:
                    logger.info("Waiting 10s to avoid rate limits...")
                    time.sleep(10)
                
                logger.info(f"Sending chunk {i+1}/{len(chunks)} to Gemini...")
                response = self.model.generate_content(prompt_template.format(text=chunk))
                
                if response.text and response.text.strip():
                   result = response.text.strip()
                   logger.info(f"Received {len(result)} characters.")
                   logger.info(f"Sample: {result[:50]}...")
                   translated_chunks.append(result)
                else:
                   logger.warning(f"Empty response for chunk {i}.")
                   translated_chunks.append(f"\n[LỖI: Gemini trả về nội dung trống cho đoạn {i+1}]\n")

            except Exception as e:
                logger.error(f"Gemini API error: {e}")
                translated_chunks.append(f"\n[LỖI DỊCH THUẬT: {str(e)}]\n")
        
        return "\n\n".join(translated_chunks)

    def _split_text(self, text, max_length):
        if not text:
            return []
            
        # Try splitting by double newline first (paragraphs)
        chunks = self._try_split(text, '\n\n', max_length)
        if chunks:
            return chunks
            
        # Fallback: Try splitting by single newline
        chunks = self._try_split(text, '\n', max_length)
        if chunks:
            return chunks
            
        # Fallback: Split by character limit
        return [text[i:i+max_length] for i in range(0, len(text), max_length)]

    def _try_split(self, text, separator, max_length):
        parts = text.split(separator)
        chunks = []
        current_chunk = []
        current_length = 0
        
        for part in parts:
            # If a single part is too long, this method fails, return None to trigger fallback
            if len(part) > max_length:
                return None
                
            if current_length + len(part) + len(separator) > max_length:
                chunks.append(separator.join(current_chunk))
                current_chunk = [part]
                current_length = len(part)
            else:
                current_chunk.append(part)
                current_length += len(part) + len(separator)
        
        if current_chunk:
            chunks.append(separator.join(current_chunk))
            
        return chunks

def get_translator() -> BaseTranslator:
    """Factory method to get translator based on config"""
    provider = os.getenv("TRANSLATION_PROVIDER", "local").lower()
    
    if provider == "browser_gemini":
        # Import here to avoid circular dependency
        from browser_translator import BrowserGeminiTranslator
        return BrowserGeminiTranslator()
    elif provider == "gemini":
        return GeminiTranslator()
    else:
        return LocalTranslator()
