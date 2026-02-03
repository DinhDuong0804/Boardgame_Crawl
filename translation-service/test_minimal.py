# -*- coding: utf-8 -*-
import os
import time
from pathlib import Path
from playwright.sync_api import sync_playwright

# Tìm đường dẫn Chrome thật
local_app_data = os.environ.get('LOCALAPPDATA')
profile_dir = Path(local_app_data) / "Google/Chrome/User Data"

print(f"DANG SU DUNG PROFILE CHROME THAT: {profile_dir}")
print("!!! QUAN TRONG: VUI LONG TAT TAT CA CUA SO CHROME TRUOC KHI CHAY !!!")
print("")

try:
    playwright = sync_playwright().start()

    context = playwright.chromium.launch_persistent_context(
        user_data_dir=str(profile_dir.absolute()),
        channel="chrome",
        headless=False,
        viewport=None,
        args=[
            '--profile-directory=Default',  # Dung profile mac dinh
            '--start-maximized',
        ],
        ignore_default_args=["--enable-automation"]
    )

    page = context.pages[0] if context.pages else context.new_page()

    print("Dang mo Gemini...")
    page.goto("https://gemini.google.com/app", timeout=90000)

    print("KIEM TRA: Trinh duyet se mo voi day du lich su va dang nhap cua ban.")
    print("Neu ban da dang nhap Gemini trong Chrome truoc do, no se vao luon ma khong can login.")
    
    time.sleep(60)

    context.close()
    playwright.stop()
except Exception as e:
    print(f"LOI: {e}")
    print("Co the ban chua tat het Chrome. Hay kiem tra Task Manager va tat het chrome.exe")
