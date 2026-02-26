# 網頁下載器 (Web Page Downloader - Playwright 版)

這是一個強大的 Python 網頁下載工具，使用 Playwright 模擬真實瀏覽器環境，可成功繞過 Cloudflare 防護與 JavaScript 驗證。

## 1. 環境需求
- Python 3.14+
- `playwright` 套件
- Chromium 瀏覽器二進制檔

## 2. 安裝步驟
在終端機中執行以下指令以安裝必要組件：

```bash
pip install playwright
python -m playwright install chromium
```

## 3. 使用方法
在終端機 (Shell / CMD / PowerShell) 中執行以下指令：

```bash
python downloader.py [網址]
```

範例 (下載原本會失敗的 NESDev Wiki)：
```bash
python downloader.py https://www.nesdev.org/wiki/DMA
```

## 4. 指定輸出檔名
你可以使用 `-o` 或 `--output` 參數來自訂存檔名稱：

```bash
python downloader.py [網址] -o [自定義檔名.html]
```

## 5. 技術原理
此版本使用 Playwright 的 `sync_api`：
1. 啟動無頭 (headless) Chromium 瀏覽器。
2. 模擬真實的 User-Agent 請求標頭。
3. 等待頁面網路活動停止 (`networkidle`)，確保 JavaScript 內容完全載入。
4. 儲存渲染後的 HTML 內容，解決傳統 `urllib` 或 `requests` 被 403 阻擋的問題。