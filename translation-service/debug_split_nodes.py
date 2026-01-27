"""Check if long responses are split into multiple message-content nodes"""
from browser_translator import BrowserGeminiTranslator
import time

t = BrowserGeminiTranslator()
t.load()
page = t.page

# Use a reasonably long text that might trigger splitting
long_text = "Repeat this sentence 100 times: 'The quick brown fox jumps over the lazy dog.' " * 50

prompt = f"Dịch đoạn này sang tiếng Việt:\n\n{long_text}"

input_box = page.wait_for_selector("div[contenteditable='true']")
input_box.click()

import pyperclip
pyperclip.copy(prompt)
page.keyboard.press("Control+V")
time.sleep(1)
page.keyboard.press("Enter")

print("Waiting for generation...")
# Simple wait for stop button
stop_selector = 'button[aria-label="Ngừng tạo câu trả lời"], button[aria-label="Stop responding"]'
page.wait_for_selector(stop_selector, timeout=60000)
while True:
    is_gen = page.evaluate(f"() => {{ const b = document.querySelector('{stop_selector}'); return b && b.offsetParent !== null; }}")
    if not is_gen: break
    time.sleep(2)

time.sleep(5) # Let it render

# Now count message-content elements and their parentage
info = page.evaluate("""
    () => {
        const containers = document.querySelectorAll('message-content');
        const parents = Array.from(containers).map(c => {
            // Find common parent like model-response
            let p = c.parentElement;
            while(p && p.tagName !== 'MODEL-RESPONSE' && !p.classList.contains('model-response-text')) {
                p = p.parentElement;
            }
            return {
                parent: p ? (p.tagName + (p.className ? "." + p.className : "")) : "none",
                length: c.innerText.length
            };
        });
        return parents;
    }
""")

print("Message Content Nodes Info:")
for i, item in enumerate(info):
    print(f"Node {i}: Parent={item['parent']}, Length={item['length']}")

t.page.close()
t.context.close()
t.browser.close()
