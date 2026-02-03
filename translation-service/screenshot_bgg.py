from playwright.sync_api import sync_playwright
import sys

def run(url, username, password):
    with sync_playwright() as p:
        browser = p.chromium.launch()
        context = browser.new_context()
        page = context.new_page()
        
        # Login
        print(f"Logging in as {username}...")
        page.goto("https://boardgamegeek.com/login")
        page.fill("input[name='username']", username)
        page.fill("input[name='password']", password)
        page.click("button[type='submit']")
        page.wait_for_load_state("networkidle")
        
        print(f"Navigating to {url}")
        page.goto(url)
        page.wait_for_load_state("networkidle")
        page.screenshot(path="bgg_logged_in.png")
        
        # List links
        links = page.query_selector_all("a")
        print(f"Total links found: {len(links)}")
        for link in links:
            href = link.get_attribute("href")
            text = link.inner_text().strip()
            if href and ("/file/" in href or "/filepage/" in href):
                 print(f"BGG Link: [{text}] -> {href}")
        
        browser.close()

if __name__ == "__main__":
    run(sys.argv[1], sys.argv[2], sys.argv[3])
