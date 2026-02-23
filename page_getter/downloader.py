import argparse
import sys
import os
import time
from playwright.sync_api import sync_playwright

def download_page(url, output_file=None):
    try:
        if not output_file:
            output_file = url.rstrip('/').split('/')[-1]
            if not output_file:
                output_file = 'index.html'
            if not output_file.lower().endswith(('.html', '.htm', '.txt', '.php', '.jsp')):
                output_file += '.html'
        
        print(f"Downloading {url} to {output_file} (using Playwright)...")
        
        with sync_playwright() as p:
            # Launch the browser (headless=True is standard, but some sites detect it)
            browser = p.chromium.launch(headless=True)
            
            # Create a browser context with a realistic user agent
            context = browser.new_context(
                user_agent='Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36'
            )
            
            page = context.new_page()
            
            # Navigate to the URL
            # wait_until='networkidle' helps ensure JavaScript content is loaded
            response = page.goto(url, wait_until='networkidle', timeout=60000)
            
            if response and response.status == 200:
                # Wait a bit longer just in case of delayed JS rendering
                time.sleep(2)
                
                # Get the page content
                content = page.content()
                
                with open(output_file, 'w', encoding='utf-8') as f:
                    f.write(content)
                
                print(f"Success! Saved to {output_file}")
                browser.close()
                return True
            else:
                status = response.status if response else "Unknown"
                print(f"Failed to download. Status code: {status}", file=sys.stderr)
                browser.close()
                return False
                
    except Exception as e:
        print(f"Error downloading {url}: {e}", file=sys.stderr)
        return False

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="A webpage downloader (supports Playwright for JS/Cloudflare).")
    parser.add_argument("url", help="The URL of the webpage to download.")
    parser.add_argument("-o", "--output", help="The output filename (optional).")
    
    args = parser.parse_args()
    
    if download_page(args.url, args.output):
        sys.exit(0)
    else:
        sys.exit(1)
