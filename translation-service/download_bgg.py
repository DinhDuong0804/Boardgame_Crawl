import sys
import os
from pathlib import Path
from rulebook_processor import RulebookProcessor

def main():
    if len(sys.argv) < 2:
        print("ERROR: Missing URL")
        sys.exit(1)
        
    url = sys.argv[1]
    output_path = sys.argv[2] if len(sys.argv) > 2 else "temp_download.pdf"
    
    print(f"Downloading {url} to {output_path}...")
    
    processor = RulebookProcessor()
    try:
        processor._start_browser()
        content, ftype = processor._browser_download(url)
        
        if content:
            # Ensure directory exists
            Path(output_path).parent.mkdir(parents=True, exist_ok=True)
            
            # If output_path is just a dir or doesn't have extension, fix it
            if not output_path.endswith(f'.{ftype}'):
                if os.path.isdir(output_path):
                    output_path = os.path.join(output_path, f"downloaded_file.{ftype}")
                else:
                    output_path = f"{output_path}.{ftype}"

            with open(output_path, 'wb') as f:
                f.write(content)
            print(f"SUCCESS:{output_path}")
        else:
            print("FAILED: No content downloaded")
            sys.exit(1)
    except Exception as e:
        print(f"ERROR: {str(e)}")
        sys.exit(1)
    finally:
        processor._close_browser()

if __name__ == "__main__":
    main()
