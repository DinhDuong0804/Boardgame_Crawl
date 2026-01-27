from playwright.sync_api import sync_playwright
import time
import config

game_url = "https://boardgamegeek.com/filepage/226279/rules"

print(f"Testing browser login and download for: {game_url}")

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True) # set False to see
    context = browser.new_context()
    page = context.new_page()
    
    # Login
    print("Logging in...")
    page.goto("https://boardgamegeek.com/login")
    page.fill("input[name='username']", config.BGG_USERNAME)
    page.fill("input[name='password']", config.BGG_PASSWORD)
    
    try:
        with page.expect_navigation(timeout=10000):
            page.click("button[type='submit'], input[type='submit'], .btn-primary")
    except:
        print("Navigation timeout or no nav needed")
        
    print("Login submitted. Checking page...")
    
    # Go to file page
    print(f"Navigating to {game_url}")
    page.goto(game_url)
    page.wait_for_load_state("networkidle")
    
    print("Page Title:", page.title())
    
    # Screenshot
    page.screenshot(path="debug_page.png")
    print("Saved screenshot to debug_page.png")
    
    # List all links
    links = page.query_selector_all("a")
    print(f"Found {len(links)} total links")
    for link in links:
        href = link.get_attribute("href")
        text = link.inner_text()
        if href and "download" in href.lower():
            print(f"MATCH HREF: {text} -> {href}")
        if "download" in text.lower():
            print(f"MATCH TEXT: {text} -> {href}")
        
    browser.close()
