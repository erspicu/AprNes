# Gemini Query Tool (Knowledgebase)

這是一個簡單的 Python 工具，用於透過 Google Gemini API 快速查詢技術資料。

## 檔案說明
- `gemini_query.py`: 主程式腳本。
- `config.json`: API 設定檔（包含 API Key 與模型設定，此檔案已被 git 忽略）。
- `config.json.example`: 設定檔範本。

## 快速上手

1. **設定 API Key**
   編輯 `knowledgebase/config.json`，在 `api_key` 欄位填入您的 Gemini API Key：
   ```json
   {
       "api_key": "您的_API_KEY",
       "model": "gemini-1.5-flash"
   }
   ```

2. **基本查詢**
   在終端機執行以下指令，結果將直接輸出於螢幕：
   ```bash
   python knowledgebase/gemini_query.py "NES PPU 的暫存器 $2007 是做什麼用的？"
   ```

3. **儲存查詢結果**
   使用 `-o` 參數將結果儲存至文字檔：
   ```bash
   python knowledgebase/gemini_query.py "請列表說明 6502 所有定址模式" -o addressing_modes.txt
   ```

## 注意事項
- 本工具使用 Python 標準庫，無需安裝第三方套件（如 requests）。
- 預設使用 `gemini-1.5-flash` 模型，若需更高精準度可於 `config.json` 改為 `gemini-1.5-pro`。
