# AprNesAvalonia UI 升級規劃

**建立日期：2026-03-31**

---

## 目標

將 AprNesAvalonia 的 UI 打造到與 AprNes (WinForms) 同等程度。先完成 UI 骨架（AXAML 佈局 + code-behind 空殼），功能後續再接線。

---

## 一、整體架構

### 1.1 NesCore 共用策略

兩個專案共用同一份 NesCore 源碼，AprNes 持續維護（效能、mapper），Avalonia 版自動受惠。

```
AprNes/NesCore/**/*.cs  ← 唯一源碼
├── AprNes.csproj         直接編譯
└── AprNesAvalonia.csproj <Compile Include="../AprNes/NesCore/**/*.cs" />
```

**目前狀態**：Mapper/ 已共用，但核心 7 檔（CPU/PPU/APU/MEM/IO/JoyPad/Main）在 `NesCoreNET/` 有獨立副本。最終目標是全部統一，但因 NesCoreNET 含 .NET 10 SIMD 最佳化，短期可維持現狀，待 SIMD 分支穩定後再合併。

### 1.2 平台抽象層

只有兩個系統需要抽象層（Avalonia 自帶畫面輸出，不需要額外的 IVideoBackend）：

| 介面 | 用途 | Win32 實作（直接引用 AprNes） | 跨平台實作 |
|------|------|------------------------------|-----------|
| `IAudioBackend` | 音效播放 | `WaveOutPlayer.cs`（WinMM） | SDL2 / OpenAL |
| `IGamepadBackend` | 手把輸入 | `joystick.cs` + `DirectInputHelper.cs`（DI8+XI） | SDL2 / evdev |

畫面濾鏡（xBRZ / ScaleX / Scanline）和類比模擬（Ntsc.cs / CrtScreen.cs）是純像素運算，平台無關，可直接引用。

---

## 二、UI 現狀 vs 目標對比

### 2.1 主視窗 (MainWindow)

| 元素 | AprNes (WinForms) | Avalonia 現狀 | 目標 |
|------|-------------------|---------------|------|
| **選單列** | MenuStrip（File/Emulation/View/Tools/Help） | 無，僅 3 個 Button | 改為 Avalonia `Menu` 控件 |
| **右鍵選單** | ContextMenuStrip（13 項） | 無 | 加入 |
| **遊戲畫面** | Panel + GDI | Border + Image 256×240 | 保持，支援動態縮放 |
| **FPS 顯示** | Label 在畫面下方 | TextBlock 在 toolbar | 移到狀態列 |
| **狀態列** | 無正式狀態列 | Grid 底部（ROM 資訊/關於） | 改為正式 StatusBar |
| **全螢幕** | F11 切換 | 無 | 加入 |
| **拖曳開啟** | 支援 | 無 | 加入 |
| **視窗縮放** | ScreenSize 1x-9x | 固定 256×240 | 支援動態大小 |

### 2.2 設定視窗 (ConfigWindow)

| 區塊 | AprNes (WinForms) | Avalonia 現狀 | 目標 |
|------|-------------------|---------------|------|
| **P1 鍵盤** | 8 鍵設定（TextBox + 點擊捕捉） | 8 鍵設定（Button + 點擊捕捉）✅ | 保持 |
| **P1 手把** | DI8+XI 完整設定 | UI 有但未接線 | 接線（Win32 優先） |
| **P2 鍵盤** | 8 鍵設定 | 無 | 新增 |
| **P2 手把** | DI8+XI 完整設定 | 無 | 新增 |
| **畫面大小** | Stage1 + Stage2 濾鏡 ComboBox | RadioButton（x1-x9）| 改為雙段式 ComboBox |
| **掃描線** | Checkbox | RadioButton（x2/x4/x6）| 改為 Checkbox |
| **音效** | Mode + Volume + 進階按鈕 | Volume slider + On/Off | 擴充 |
| **類比模式** | Enable + Size + Output + 進階按鈕 | 無 | 新增區塊 |
| **語言** | ComboBox | ComboBox ✅ | 保持 |
| **AccuracyOptA** | CheckBox | CheckBox ✅ | 保持 |
| **LimitFPS** | CheckBox | CheckBox ✅ | 保持 |
| **截圖路徑** | TextBox + 瀏覽按鈕 | TextBox + 瀏覽按鈕 ✅ | 保持 |
| **Region** | 主選單子選單 | 無 | 新增（在主選單） |
| **錄影/錄音** | 路徑 + 品質設定 | 無 | Phase 2 再加 |

### 2.3 其他視窗

| 視窗 | AprNes (WinForms) | Avalonia 現狀 | 目標 |
|------|-------------------|---------------|------|
| **About** | 版本 + 網站連結 | 有 ✅ | 微調文字 |
| **ROM Info** | iNES header + Copy | 有 ✅ | 保持 |
| **Analog Config** | NTSC/CRT 參數滑桿 | 無 | 新增（UI 骨架） |
| **AudioPlus Config** | 聲道音量/擴展晶片/後處理 | 無 | 新增（UI 骨架） |

---

## 三、MainWindow 目標 AXAML 結構

```
Window
├── DockPanel
│   ├── [Top] Menu (Avalonia native menu control)
│   │   ├── File
│   │   │   ├── Open ROM          (Ctrl+O)
│   │   │   ├── Recent ROMs ►     (子選單，最多 10 筆)
│   │   │   ├── ─────────
│   │   │   └── Exit              (Ctrl+W)
│   │   ├── Emulation
│   │   │   ├── Soft Reset        (Ctrl+R)
│   │   │   ├── Hard Reset
│   │   │   ├── ─────────
│   │   │   ├── Region ►
│   │   │   │   ├── ● NTSC
│   │   │   │   ├── ○ PAL
│   │   │   │   └── ○ Dendy
│   │   │   ├── ─────────
│   │   │   ├── ☑ Limit FPS
│   │   │   └── ☑ AccuracyOptA
│   │   ├── View
│   │   │   ├── Fullscreen        (F11)
│   │   │   ├── ─────────
│   │   │   ├── Sound ON/OFF
│   │   │   └── Ultra Analog ON/OFF
│   │   ├── Tools
│   │   │   ├── Screenshot        (Ctrl+Shift+P)
│   │   │   ├── ROM Info
│   │   │   ├── ─────────
│   │   │   ├── Record ►
│   │   │   │   ├── Record Video
│   │   │   │   ├── Record Audio
│   │   │   │   └── Record Settings
│   │   │   ├── ─────────
│   │   │   └── Configuration
│   │   └── Help
│   │       ├── Keyboard Shortcuts
│   │       └── About
│   │
│   ├── [Bottom] StatusBar (Grid)
│   │   ├── [Left]  ROM 名稱 / 狀態文字
│   │   ├── [Center] Region 顯示
│   │   └── [Right]  FPS 計數器
│   │
│   └── [Center] Border (黑底，動態大小)
│       └── Image x:Name="GameCanvas"
│           (Stretch="Fill", 隨視窗 / ScreenSize 縮放)
│
└── ContextMenu (右鍵選單)
    ├── Open ROM
    ├── Soft Reset
    ├── Hard Reset
    ├── ─────────
    ├── Configuration
    ├── ROM Info
    ├── ─────────
    ├── Screen Mode ►
    │   ├── Fullscreen
    │   └── Normal
    ├── Sound ON/OFF
    ├── Ultra Analog ON/OFF
    ├── ─────────
    └── Exit
```

---

## 四、ConfigWindow 目標 AXAML 結構

改為 **TabControl** 分頁式，更清楚也更好擴展：

```
Window (Title="功能設定", Width=780, SizeToContent=Height)
├── Grid
│   ├── [Row 0] TabControl
│   │   ├── Tab "P1 Input"
│   │   │   ├── GroupBox "鍵盤對應"
│   │   │   │   └── 8 行 (Label + Button) 點擊捕捉
│   │   │   └── GroupBox "手把對應"
│   │   │       └── 8 行 (Label + Button) 點擊捕捉
│   │   │
│   │   ├── Tab "P2 Input"
│   │   │   ├── GroupBox "鍵盤對應"
│   │   │   │   └── 8 行 (Label + Button)
│   │   │   └── GroupBox "手把對應"
│   │   │       └── 8 行 (Label + Button)
│   │   │
│   │   ├── Tab "Graphics"
│   │   │   ├── GroupBox "畫面大小"
│   │   │   │   ├── ComboBox "Stage 1 濾鏡" (None/xBRZ 2-6x/ScaleX 2-3x/NN 2-4x)
│   │   │   │   ├── ComboBox "Stage 2 濾鏡" (None/ScaleX 2-3x/NN 2-4x)
│   │   │   │   ├── CheckBox "掃描線"
│   │   │   │   └── TextBlock "輸出解析度: NNNxNNN" (即時預覽)
│   │   │   └── GroupBox "類比模式"
│   │   │       ├── CheckBox "啟用類比模式"
│   │   │       ├── CheckBox "Ultra Analog"
│   │   │       ├── ComboBox "類比倍率" (2x/4x/6x/8x)
│   │   │       ├── ComboBox "視訊輸入" (RF/S-Video/AV)
│   │   │       ├── CheckBox "CRT 效果"
│   │   │       └── Button "類比進階設定..." → AnalogConfigWindow
│   │   │
│   │   ├── Tab "Audio"
│   │   │   ├── CheckBox "啟用音效"
│   │   │   ├── ComboBox "音效模式" (Pure/Authentic/Modern)
│   │   │   ├── Slider "音量" (0-100)
│   │   │   └── Button "音效進階設定..." → AudioPlusConfigWindow
│   │   │
│   │   └── Tab "General"
│   │       ├── ComboBox "語言選擇"
│   │       ├── CheckBox "限制 FPS"
│   │       ├── CheckBox "AccuracyOptA"
│   │       └── GroupBox "截圖"
│   │           ├── TextBox "路徑" (ReadOnly)
│   │           └── Button "瀏覽..."
│   │
│   └── [Row 1] StackPanel (HorizontalAlignment=Right)
│       └── Button "確定"
```

---

## 五、AnalogConfigWindow（新增）

```
Window (Title="類比視訊設定", Width=500, SizeToContent=Height)
├── TabControl
│   ├── Tab "NTSC"
│   │   ├── CheckBox "HBI"
│   │   ├── CheckBox "Color Burst Jitter"
│   │   ├── CheckBox "Symmetric IQ"
│   │   ├── CheckBox "Ringing" + Slider (0-200)
│   │   ├── Slider "Gamma" (0-200)
│   │   └── GroupBox "色溫"
│   │       ├── Slider "R" (0-200)
│   │       ├── Slider "G" (0-200)
│   │       └── Slider "B" (0-200)
│   │
│   └── Tab "CRT"
│       ├── CheckBox "Interlace Jitter"
│       ├── CheckBox "Vignette" + Slider (0-100)
│       ├── CheckBox "Shadow Mask" + ComboBox mode + Slider
│       ├── CheckBox "Curvature"
│       ├── CheckBox "Phosphor"
│       ├── CheckBox "Horizontal Beam" + Slider
│       └── CheckBox "Convergence"
│
└── Button "確定" (右下角)
```

---

## 六、AudioPlusConfigWindow（新增）

```
Window (Title="音效進階設定", Width=600, SizeToContent=Height)
├── TabControl
│   ├── Tab "NES 聲道"
│   │   ├── ComboBox "主機型號" (Famicom/NES-001/NES-101/AV Famicom/...)
│   │   └── 5 行 × (CheckBox 啟用 + Label 名稱 + Slider 音量 + Label 數值)
│   │       Pulse 1 / Pulse 2 / Triangle / Noise / DMC
│   │
│   ├── Tab "擴展晶片"
│   │   ├── ComboBox "晶片選擇" (None/VRC6/VRC7/N163/5B/MMC5/FDS)
│   │   └── 動態 N 行 × (CheckBox + Label + Slider + Label)
│   │       根據選擇的晶片顯示對應聲道
│   │
│   └── Tab "後處理"
│       ├── Slider "Stereo Width"
│       ├── Slider "Haas Delay"
│       ├── Slider "Haas Crossfeed"
│       ├── Slider "Reverb Wet"
│       ├── Slider "Comb Feedback"
│       ├── Slider "Comb Damp"
│       ├── Slider "Bass Boost dB"
│       └── Slider "Bass Boost Freq"
│
└── Button "確定"
```

---

## 七、實施順序（UI 優先）

### Step 1：MainWindow 選單化
- 移除頂部 3 個 Button，改為 Avalonia `<Menu>` 控件
- 建立完整 5 大選單（File/Emulation/View/Tools/Help）
- 加入右鍵 ContextMenu
- 加入底部 StatusBar（ROM 名稱 + Region + FPS）
- 選單項目的 Click handler 先寫空方法（`// TODO: wire up`）
- 快捷鍵綁定（Ctrl+O, Ctrl+R, F11, Ctrl+Shift+P, Ctrl+W）

### Step 2：MainWindow 畫面動態縮放
- GameCanvas 支援 ScreenSize（1x-9x）
- 視窗大小隨 ScreenSize 動態調整
- 全螢幕切換（F11）
- 拖曳開啟 ROM（DragDrop）

### Step 3：ConfigWindow 分頁重構
- 改為 TabControl 5 分頁（P1 Input / P2 Input / Graphics / Audio / General）
- P1 鍵盤保持現有邏輯
- P1 手把 UI（先空殼）
- P2 鍵盤/手把 UI（先空殼）
- Graphics 分頁：Stage1/Stage2 ComboBox + Scanline + 類比模式區塊
- Audio 分頁：Mode + Volume + 進階按鈕
- General 分頁：語言 + LimitFPS + AccuracyOptA + 截圖路徑

### Step 4：新增 AnalogConfigWindow（UI 骨架）
- NTSC tab：所有 CheckBox + Slider
- CRT tab：所有 CheckBox + Slider + ComboBox
- 所有控件命名，handler 空殼
- INI 讀寫先不接

### Step 5：新增 AudioPlusConfigWindow（UI 骨架）
- NES 聲道 tab：主機型號 ComboBox + 5 聲道滑桿
- 擴展晶片 tab：晶片選擇 + 動態聲道
- 後處理 tab：8 條滑桿
- INI 讀寫先不接

### Step 6：Recent ROMs + INI 系統重構
- IniFile.cs 支援 `[Section]` 分區
- 多檔 INI（AprNes.ini / AprNesAnalog.ini / AprNesAudioPlus.ini）
- Recent ROMs 列表（最多 10 筆，存入 INI）

---

## 八、檔案清單（預計新增/修改）

### 修改
| 檔案 | 說明 |
|------|------|
| `MainWindow.axaml` | 移除 Button toolbar → Menu + StatusBar |
| `MainWindow.axaml.cs` | 選單 handler 空殼、ContextMenu、F11、DragDrop |
| `Views/ConfigWindow.axaml` | 改為 TabControl 5 分頁 |
| `Views/ConfigWindow.axaml.cs` | 分頁邏輯、新控件 handler |
| `IniFile.cs` | 支援 Section |
| `AprNesAvalonia.csproj` | link 更多 AprNes 源碼（tool/*.cs） |

### 新增
| 檔案 | 說明 |
|------|------|
| `Views/AnalogConfigWindow.axaml` | 類比視訊設定 UI |
| `Views/AnalogConfigWindow.axaml.cs` | handler 空殼 |
| `Views/AudioPlusConfigWindow.axaml` | 音效進階設定 UI |
| `Views/AudioPlusConfigWindow.axaml.cs` | handler 空殼 |

---

## 九、不在此階段處理的項目

以下功能留待 UI 骨架完成後，再逐步接線：

- [ ] 畫面濾鏡實際渲染（xBRZ / ScaleX / Scanline）
- [ ] 類比模式實際渲染（NTSC / CRT）
- [ ] 手把實際輸入（IAudioBackend / IGamepadBackend 抽象層）
- [ ] 錄影 / 錄音（FFmpeg）
- [ ] Region 切換實際邏輯
- [ ] AudioPlus 聲道音量實際控制
- [ ] P2 輸入實際接線
- [ ] 非 Win32 平台 backend（SDL2 等）
