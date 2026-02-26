# AprNes - C# NES Emulator

這是一個使用 C# 開發的 NES (Nintendo Entertainment System) 模擬器專案。

## 目錄結構說明

### 核心程式碼 (AprNes/)
*   **`AprNes/NesCore/`**: 模擬器的核心邏輯（純模擬層，不依賴任何系統/UI 函式庫）。
    *   `CPU.cs`: MOS 6502 處理器模擬。
    *   `PPU.cs`: 圖像處理單元模擬。
    *   `APU.cs`: 音效處理單元模擬，透過 `AudioSampleReady` callback 輸出音訊樣本。
    *   `MEM.cs` / `IO.cs`: 記憶體與輸入輸出管理。
    *   `JoyPad.cs`: NES 手把輸入暫存器模擬。
    *   `Main.cs`: 核心初始化、執行迴圈、SRAM 存取 API (`LoadSRam` / `DumpSRam`)。
    *   `Mapper/`: 各類遊戲卡匣控制晶片實作（Mapper 0/1/2/3/4/5/7/11/66/71）。
*   **`AprNes/UI/`**: 基於 Windows Forms 的使用者介面。
    *   `AprNesUI.cs`: 主視窗，負責 FPS 節流、SRAM 檔案讀寫、手把事件處理。
    *   `AprNes_ConfigureUI.cs`: 鍵盤/手把按鍵設定視窗。
    *   `AprNes_RomInfoUI.cs` / `AprNes_Info.cs`: ROM 資訊顯示。
*   **`AprNes/tool/`**: 系統層輔助工具與渲染函式庫。
    *   `WaveOutPlayer.cs`: WinMM WaveOut 音訊輸出，訂閱 `NesCore.AudioSampleReady`。
    *   `joystick.cs`: 手把輸入輪詢，**雙 API 並行**：WinMM（一般 USB 手把）+ XInput（Xbox / Xbox One / Xbox Series 手把）。
    *   `NativeAPIShare.cs`: WinMM / GDI / XInput 的 P/Invoke 宣告與 struct 定義。
    *   `NativeRendering.cs` / `InterfaceGraphic.cs`: GDI 直接繪圖介面。
    *   `libXBRz.cs` / `LibScanline.cs` / `Scalex.cs`: 畫面放大濾鏡（xBRZ、Scanline）。
    *   `LangINI.cs`: 多語系 INI 設定讀取。
*   **`AprNes/TestRunner.cs`**: 自動化測試執行器（搭配 NES 測試 ROM 使用）。

### 專案開發與測試
*   **`bugfix/`**: 詳細記錄開發過程中發現的 Bug 及修復過程，按日期排序。
*   **`nes-test-roms-master/`**: 整合各類 NES 測試 ROM，用於驗證模擬器正確性（blargg、PPU/CPU 測試等）。
*   **`report/`**: 測試自動化產生的報告及開發紀錄。
*   **`ref/`**: NES 開發文件、硬體規格說明及其他模擬器的參考源碼片段。
*   **`MD/`**: 設計文件與重構規劃筆記。
    *   `NesCore_refactor_proposal.md`: NesCore 系統層分離的設計提案。
    *   `DEVELOPMENT.md` / `TODO.md`: 開發備忘與待辦事項。
    *   `模擬器 Mapper 遊戲相容性建議.md`: Mapper 相容性分析。

### 輔助工具 (tools/)
*   **`tools/page_getter/`**: 抓取技術文件（如 NESdev Wiki）的 Python 腳本。
*   **`tools/KeyTest/`**: 鍵盤/手把輸入測試工具。
*   **`tools/knowledgebase/`**: 開發知識庫筆記。

### 建置與設定
*   **`AprNes.sln`**: Visual Studio 解決方案檔案。
*   **`build.bat` / `do_build.bat` / `build.ps1`**: 快速編譯專案的批次/PowerShell 腳本。
*   **`.gitignore`**: 已配置排除 Visual Studio 暫存檔與大型參考源碼，保留 `bin/Debug/AprNes.ini`。

## 開發環境
*   **語言**: C#
*   **框架**: .NET Framework 4.6.1（Windows Forms）
*   **IDE**: Visual Studio 2019+
*   **平台**: Windows x64

## 手把支援
| 裝置類型 | API | 說明 |
|---------|-----|------|
| 一般 USB 手把 / 老式搖桿 | WinMM（joyGetPos） | 自動偵測，最多 256 個裝置 |
| Xbox 360 / Xbox One / Xbox Series | XInput（xinput1_4.dll） | 自動偵測 player 0–3，支援 D-Pad、類比搖桿、全部按鍵 |

## 支援的 Mapper
| Mapper | 代表遊戲 |
|--------|---------|
| 0 (NROM) | 超級瑪利歐兄弟、大金剛 |
| 1 (MMC1) | 薩爾達傳說、惡魔城 II |
| 2 (UxROM) | 洛克人、惡魔城 |
| 3 (CNROM) | 惡魔城、忍者龍劍傳 |
| 4 (MMC3) | 超級瑪利歐兄弟 3、忍者神龜 |
| 5 (MMC5) | 百戰天蟲 |
| 7 (AxROM) | 戰鬥城市 |
| 11 | 高爾夫俱樂部 |
| 66 (GxROM) | 超級馬利歐兄弟 / 打鴨子 |
| 71 | 生化戰士 |
