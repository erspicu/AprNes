# AprNes - C# NES Emulator

這是一個使用 C# 開發的 NES (Nintendo Entertainment System) 模擬器專案。

## 目錄結構說明

### 核心程式碼 (AprNes/)
*   **`AprNes/NesCore/`**: 模擬器的核心邏輯。
    *   `CPU.cs`: 6502 處理器模擬。
    *   `PPU.cs`: 圖像處理單元模擬。
    *   `APU.cs`: 音效處理單元模擬。
    *   `Mapper/`: 各類遊戲卡匣控制晶片 (Mappers) 的實作。
    *   `MEM.cs` / `IO.cs`: 記憶體與輸入輸出管理。
*   **`AprNes/UI/`**: 基於 Windows Forms 的使用者介面，包含設定視窗與遊戲資訊顯示。
*   **`AprNes/tool/`**: 輔助工具與渲染庫，包含 XBRz 放大演算法、圖形渲染介面與語系檔處理。

### 專案開發與測試
*   **`bugfix/`**: 詳細記錄開發過程中發現的 Bug 及其修復過程，按日期與編號排序。
*   **`nes-test-roms-master/`**: 整合各類 NES 測試 ROM，用於驗證模擬器的正確性 (如 blargg 測試、PPU/CPU 測試等)。
*   **`report/`**: 測試自動化產生的報告、執行截圖以及開發方法論文件。
*   **`ref/`**: 存放 NES 開發文件、硬體規格說明以及其他模擬器 (如 Mesen) 的參考源碼片段。
*   **`page_getter/`**: 用於抓取與下載技術文檔 (如 NESdev Wiki) 的 Python 工具腳本。

### 建置與設定
*   **`AprNes.sln`**: Visual Studio 解決方案檔案。
*   **`build.bat` / `do_build.bat`**: 用於快速編譯專案的批次檔。
*   **`.gitignore`**: 已配置排除 Visual Studio 暫存檔與大型參考源碼，但保留了 `bin/Debug` 下的 `AprNes.ini` 配置。

## 開發環境
*   **語言**: C#
*   **框架**: .NET Framework (Windows Forms)
*   **IDE**: Visual Studio
