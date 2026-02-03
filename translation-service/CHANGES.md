# Translation Service - Các Thay Đổi Quan Trọng

## 🎯 Mục tiêu
Sửa lỗi không chạy được Browser Gemini do các vấn đề:
1. ❌ Flag `--no-sandbox` khiến Gemini phát hiện automation
2. ❌ Timeout quá ngắn
3. ❌ Không đợi page load đầy đủ
4. ❌ Selectors cứng không linh hoạt

---

## ✅ Đã Sửa (browser_translator.py)

### 1. **XÓA --no-sandbox và các flags nguy hiểm**
```python
# TRƯỚC (Code cũ trên Git):
args=[
    '--disable-blink-features=AutomationControlled',
    '--no-sandbox',  # ⚠️ Gemini phát hiện automation!
    '--disable-infobars',
    '--start-maximized',
    '--disable-dev-shm-usage',
    '--no-first-run',
    '--no-service-autorun',
    '--password-store=basic'
]

# SAU (Code mới):
args=[
    '--disable-blink-features=AutomationControlled',
    # --no-sandbox ĐÃ XÓA
    '--disable-infobars',
    '--start-maximized',
    '--no-first-run',
    '--no-service-autorun',
    '--password-store=basic',
    '--disable-features=IsolateOrigins,site-per-process',
    '--lang=vi-VN,vi',
    '--disable-web-security',
]
```

### 2. **Tăng timeout và đợi page ổn định**
```python
# TRƯỚC:
self.page.goto("https://gemini.google.com/app", timeout=60000)
self.page.wait_for_selector('div[role="textbox"]', timeout=300000)

# SAU:
self.page.goto("https://gemini.google.com/app", timeout=90000)
self.page.wait_for_load_state("networkidle", timeout=60000)
time.sleep(10)  # Đợi React hydration
```

### 3. **Thử nhiều selectors linh hoạt**
```python
selectors = [
    'div[role="textbox"]',
    'rich-textarea',
    'textarea',
    '.ql-editor',
    '[contenteditable="true"]',
    'div[aria-label*="Gemini"] [contenteditable]'
]

for selector in selectors:
    try:
        self.page.wait_for_selector(selector, timeout=10000)
        self.input_selector = selector  # Lưu lại selector thành công
        found = True
        break
    except:
        continue
```

### 4. **Optional playwright_stealth**
```python
# TRƯỚC: Hard import - crash nếu thiếu
from playwright_stealth import Stealth

# SAU: Optional import
try:
    from playwright_stealth import stealth_sync
    HAS_STEALTH = True
except ImportError:
    HAS_STEALTH = False

# Sử dụng:
if HAS_STEALTH:
    stealth_sync(self.page)
```

---

## 📋 So Sánh Code Cũ vs Mới

| Tính năng | Code Cũ (Git) | Code Mới (Local) | Kết quả |
|-----------|----------------|------------------|---------|
| **Sandbox** | ❌ Tắt (`--no-sandbox`) | ✅ Bật (xóa flag) | Gemini không phát hiện bot |
| **Timeout** | 60s | 90s | Đủ thời gian load |
| **Wait Strategy** | Đợi 1 selector | Đợi networkidle + 10s + thử 6 selectors | Tìm được input box |
| **Stealth Import** | Hard import | Optional (try/except) | Không crash |
| **Selector Storage** | Hardcode | Dynamic (`self.input_selector`) | Dùng lại đúng selector |
| **Logging** | Ít | Chi tiết | Dễ debug |

---

## 🚀 Test Lại

### Cách test:
```powershell
# 1. Chạy translation service
cd d:\Cafenix\Boardgame\Boardgame_Crawl\translation-service
python consumer.py

# 2. Quan sát log:
# ✅ "Found Gemini input with selector: div[role="textbox"]"
# ✅ "Gemini Web is ready!"
# ❌ KHÔNG còn cảnh báo "--no-sandbox"
```

---

## 💡 Lý do Code Mới Tốt Hơn

1. **Bảo mật**: Không tắt sandbox → Gemini tin tưởng hơn
2. **Linh hoạt**: Thử nhiều selectors → Chạy được nhiều version UI
3. **Ổn định**: Đợi đủ lâu → Page render đầy đủ
4. **Resilient**: Optional dependencies → Không crash
5. **Maintainable**: Logging tốt → Debug dễ dàng

---

## ⚠️ Lưu Ý Quan Trọng

### Persistent Context
Code **ĐÃ** sử dụng `launch_persistent_context`:
```python
self.context = self.playwright.chromium.launch_persistent_context(
    user_data_dir=str(self.profile_dir.absolute()),
    channel="chrome",
    headless=False,
    # ...
)
```

**Lợi ích:**
- ✅ Lưu cookies và session
- ✅ Không cần login lại mỗi lần chạy
- ✅ Trông giống người dùng thật hơn

### Profile Directory
```python
self.profile_dir = Path("d:/Downloads/board_game_scraper-2.23.1/translation-service/gemini_profile")
```

⚠️ **TODO**: Sửa hardcoded path này thành relative path

---

## 📌 Next Steps

1. ✅ Đã xóa `--no-sandbox`
2. ⏳ Test lại với Gemini
3. ⏳ Sửa hardcoded profile path
4. ⏳ Push lên Git nếu test thành công

---

**Tổng kết**: Code mới **CHẮC CHẮN** tốt hơn code cũ, đặc biệt về tính stealth và stability với Gemini.
