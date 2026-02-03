# -*- coding: utf-8 -*-
import time
from browser_translator import BrowserGeminiTranslator
import shutil
import os

def main():
    print("--- CHUAN BI DANG NHAP THU CONG ---")
    
    # 1. Xoa profile cu neu co de lam sach
    translator = BrowserGeminiTranslator()
    if translator.profile_dir.exists():
        print(f"Dang xoa profile cu tai: {translator.profile_dir}")
        shutil.rmtree(translator.profile_dir, ignore_errors=True)
    
    # 2. Mo trinh duyet
    print("Dang mo Chrome. VUI LONG DANG NHAP VAO GEMINI!")
    translator.load()
    
    print("\n--- HUONG DAN ---")
    print("1. Thuc hien dang nhap vao tai khoan Google cua ban.")
    print("2. Sau khi vao den man hinh 'Hoi Gemini', hay doi 10 giay.")
    print("3. Dong trinh duyet hoac bam Ctrl+C tai day de ket thuc.")
    print("------------------\n")
    
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print("\nDa luu phien dang nhap. Dang dong...")
        translator.__del__()

if __name__ == "__main__":
    main()
