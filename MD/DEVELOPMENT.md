# AprNes 開發筆記

## 目錄

1. [專案技術特點](#1-專案技術特點)
2. [執行效能優化做法](#2-執行效能優化做法)
3. [APU 音效實作](#3-apu-音效實作)
4. [FPS 限制修正](#4-fps-限制修正)

---

## 1. 專案技術特點

### 1.1 技術堆疊

| 項目 | 說明 |
|------|------|
| 語言 | C# (.NET Framework 4.6.1) |
| UI 框架 | Windows Forms |
| 平台 | Windows x64 |
| 編譯選項 | `AllowUnsafeBlocks = true` |

### 1.2 整體架構

```
AprNes/
├── NesCore/          ← 模擬器核心
│   ├── Main.cs       ← ROM 載入、初始化、主迴圈
│   ├── CPU.cs        ← MOS 6502 CPU 模擬
│   ├── PPU.cs        ← 圖形處理器 (Picture Processing Unit)
│   ├── APU.cs        ← 音效處理器 (Audio Processing Unit)
│   ├── IO.cs         ← I/O 暫存器讀寫路由
│   ├── MEM.cs        ← 記憶體管理
│   ├── JoyPad.cs     ← 手把輸入
│   └── Mapper/       ← 卡帶映射器 (Mapper 0/1/2/3/4/5/7/11/66/71)
├── tool/             ← 工具類
│   ├── NativeAPIShare.cs   ← Windows API P/Invoke 宣告
│   ├── NativeRendering.cs  ← GDI 原生渲染
│   ├── LibScanline.cs      ← 掃描線濾波
│   ├── libXBRz.cs          ← xBRz 縮放演算法
│   └── joystick.cs         ← 遊戲手把 (WinMM)
└── UI/               ← 使用者介面
    ├── AprNesUI.cs         ← 主視窗
    ├── AprNes_ConfigureUI  ← 設定視窗
    └── AprNes_RomInfoUI    ← ROM 資訊視窗
```

### 1.3 NES 硬體模擬精度

#### CPU (MOS 6502)
- 完整實作所有官方指令（約 5000+ 行 switch/case）
- 週期精確計時：使用 `cycle_table[256]` 查找表，每條指令正確消耗對應 CPU 週期數
- 支援 NMI、IRQ、RESET 中斷向量
- 使用 unmanaged 記憶體指標（`byte*`）存取 CPU 暫存器和記憶體，避免 GC 開銷

#### PPU (圖形處理器)
- 掃描線精確（Scanline-accurate）渲染
- 每個 CPU cycle 驅動 3 個 PPU cycle（NTSC 時脈比 3:1）
- 支援背景渲染、精靈渲染（Sprite 0 碰撞）、垂直空白 NMI
- 畫面緩衝區使用 `uint* ScreenBuf1x`（unmanaged 指標）直接寫入，零 GC 壓力
- 支援多種鏡像模式：水平、垂直、四畫面

#### 記憶體管理
- 全核心使用 **unsafe 指標** 操作記憶體，避免陣列邊界檢查：
  ```csharp
  byte* NES_MEM  = AllocHGlobal(65536);  // 64KB CPU 記憶體空間
  byte* ppu_ram  = AllocHGlobal(0x4000); // 16KB PPU VRAM
  byte* spr_ram  = AllocHGlobal(256);    // OAM (Sprite RAM)
  uint* ScreenBuf1x = AllocHGlobal(256 * 240 * 4); // 畫面緩衝
  ```
- 讀寫函數表（`init_function()`）：將記憶體區段的讀寫操作映射到函式指標，避免每次都做位址判斷

#### Mapper 支援
- 支援 Mapper 0、1、2、3、4、5、7、11、66、71
- 透過 `IMapper` 介面統一操作，以 `Activator.CreateInstance` 動態載入
- 涵蓋市面大多數 NES 遊戲

### 1.4 圖像渲染管線

```
PPU 渲染 → ScreenBuf1x (uint*) → GDI SetDIBitsToDevice → 視窗
```

- 使用 `NativeRendering` 直接呼叫 GDI32 API (`SetDIBitsToDevice`) 繪製畫面，不經過 .NET Graphics 物件
- 可選圖像濾波器：掃描線濾波（LibScanline）、xBRz 2x/3x 縮放（libXBRz）

---

## 2. 執行效能優化做法

### 2.1 Unsafe 指標取代托管陣列

核心模擬器大量使用 C# `unsafe` 模式直接操作原始記憶體：

```csharp
// 例：PPU 記憶體讀取 (unsafe)
byte val = ppu_ram[addr]; // 直接指標運算，無邊界檢查，無 GC

// 相比托管寫法
byte val = ppuRamArray[addr]; // 有邊界檢查、可能觸發 GC
```

好處：
- 消除陣列邊界檢查（每次記憶體存取都省略一次比較）
- 所有核心緩衝區以 `Marshal.AllocHGlobal` 分配，永遠不會被 GC 移動或回收

### 2.2 CPU 週期精確的主迴圈

主迴圈設計（`Main.cs: run()`）讓 CPU 和 PPU 以正確的週期比例推進：

```csharp
while (!exit)
{
    cpu_step();          // 執行一條 CPU 指令 → 更新 cpu_cycles
    do
    {
        ppu_step();      // PPU: 每 CPU cycle 跑 3 次
        ppu_step();
        ppu_step();
        apu_step();      // APU: 每 CPU cycle 跑 1 次
    } while (--cpu_cycles > 0);
}
```

這樣設計避免了獨立執行緒同步的 overhead，整個模擬器在單一執行緒中以固定時序推進。

### 2.3 函數指標表（讀寫路由）

記憶體讀寫透過函式指標表分派，取代大型 if/switch 判斷：

```csharp
init_function(); // 依位址範圍將 Mem_r/Mem_w 映射到對應的處理函式
```

### 2.4 GDI 原生渲染

不使用 `Graphics.DrawImage()`，改用原生 GDI32 API 直接輸出 32-bit 像素緩衝區：

```csharp
// NativeRendering.cs
SetDIBitsToDevice(hdc, 0, 0, 256, 240, 0, 0, 0, 240, screenPtr, ref bmi, 0);
```

省去 .NET Bitmap 物件的建立和 GC 壓力。

---

## 3. APU 音效實作

### 3.1 背景

原始專案的 `APU.cs` 只有暫存器欄位和空殼函式，沒有任何音效輸出功能。此次完整實作了 NES APU 的五個聲道，並使用 Windows **WaveOut API**（`winmm.dll`）輸出音效，無需任何第三方套件。

### 3.2 修改的檔案

#### `NesCore/APU.cs`（完整重寫）

**新增 WaveOut API P/Invoke 宣告：**

```csharp
[StructLayout(LayoutKind.Sequential)]
struct WAVEFORMATEX { ... }       // PCM 格式描述

[StructLayout(LayoutKind.Sequential)]
struct WAVEHDR { ... }            // 音效緩衝區標頭

[DllImport("winmm.dll")] static extern int waveOutOpen(...);
[DllImport("winmm.dll")] static extern int waveOutWrite(...);
[DllImport("winmm.dll")] static extern int waveOutClose(IntPtr hwo);
// ... 等
```

**音效緩衝區架構（雙緩衝輪流）：**

```
APU_SAMPLE_RATE = 44100 Hz
APU_BUFFER_SAMPLES = 735  (≈ 1 幀的樣本數：44100 / 60 ≈ 735)
APU_NUM_BUFFERS = 4       (4 個緩衝區輪流，避免播放中斷)
```

緩衝區以 `GCHandle.Alloc(..., Pinned)` 鎖定，防止 GC 移動：

```csharp
_bufPins[i] = GCHandle.Alloc(_audioBufs[i], GCHandleType.Pinned);
```

**五個聲道實作：**

| 聲道 | 類型 | 實作重點 |
|------|------|----------|
| Pulse 1 & 2 | 方波 | 8 種 Duty 序列、Envelope 包絡、Sweep 頻率掃描、Length Counter |
| Triangle | 三角波 | 32 步硬編碼波形、Linear Counter、靜音條件（週期 < 2） |
| Noise | 雜音 | 15-bit LFSR、兩種 mode（bit1/bit6 回饋）、Envelope/Length |
| DMC | Delta PCM | 從 NES 記憶體讀取樣本、8-bit Delta 解碼、Loop/IRQ 支援 |

**非線性混音（NES 實際電路模擬）：**

```csharp
// Pulse 非線性混音表（避免線性疊加失真）
SQUARELOOKUP[n] = 95.52 / (8128.0 / n + 100);

// Triangle + Noise + DMC 混音表
TNDLOOKUP[n] = 163.67 / (24329.0 / n + 100);

// 最終輸出
double sample = SQUARELOOKUP[p1 + p2] + TNDLOOKUP[3*tri + 2*noise + dmc];
```

**DC 消除濾波器（防止直流偏移）：**

```csharp
_dckiller = _dckiller * 0.999 + sample - _dcprev;
_dcprev = sample;
short out = (short)(_dckiller * 30000.0);
```

**Frame Counter（~240Hz 驅動包絡/長度/掃描）：**

```csharp
// 4-step 模式：每 3728.5/7457/11186/14914.5 CPU cycle 觸發
// 5-step 模式：多一個 step，第 5 步也觸發
static void clockframecounter()
{
    setenvelope();  // Envelope 衰減
    setlength();    // Length Counter 遞減
    setsweep();     // Sweep 頻率調整
}
```

**音效與模擬同步（自然限速）：**

WaveOut 在緩衝區填滿後會自動等待播放完畢才接受新資料，讓模擬器自然地跑在 60fps 附近（當音效開啟時）。

#### `NesCore/Main.cs`

```csharp
// init() 末尾加入：
initAPU();

// run() 中加入（見下方第 4 節）：
timeBeginPeriod(1);
// ... 主迴圈 ...
timeEndPeriod(1);
```

#### `NesCore/CPU.cs`

`SoftReset()` 加入 APU 重新初始化，確保重置後音效正常：

```csharp
public static void SoftReset()
{
    softreset = true;
    closeAudio();
    initAPU();
}
```

#### `NesCore/IO.cs`

補全 APU 相關的 I/O 路由：

```csharp
// IO_read()
case 0x4015: return apu_r_4015();  // 讀取 APU 狀態

// IO_write()
case 0x4017:
    ctrmode = ((val & 0x80) != 0) ? 5 : 4;  // Frame Counter 模式
    apuintflag = (val & 0x40) != 0;
    framectr = 0;
    framectrdiv = framectrreload;
    if (ctrmode == 5) clockframecounter();
    break;
```

#### `UI/AprNesUI.cs`

- 動態加入「Sound ON/OFF」選單項目到右鍵選單
- 開啟/關閉音效呼叫 `NesCore.openAudio()` / `NesCore.closeAudio()`
- 設定讀寫：`AppConfigure["Sound"]` → `NesCore.AudioEnabled`
- 視窗關閉時呼叫 `NesCore.closeAudio()` 釋放 WaveOut 資源

---

## 4. FPS 限制修正

### 4.1 問題描述

啟用 FPS 限制功能、但關閉音效時，遊戲幀率會降至約 **30 FPS**（正常應為 60 FPS）。開啟音效後幀率恢復正常。

### 4.2 根本原因

**`PPU.cs:197` 的 FPS 限制迴圈：**

```csharp
if (LimitFPS)
    while (StopWatch.Elapsed.TotalSeconds < 0.01666)
        Thread.Sleep(1);  // 等待直到 16.66ms 過去
```

`Thread.Sleep(1)` 的實際精度取決於 **Windows 計時器解析度**：

| 狀態 | Windows 計時器解析度 | `Thread.Sleep(1)` 實際睡眠時間 | 結果 |
|------|----------------------|-------------------------------|------|
| 音效開啟（WaveOut 運作中） | **1ms**（WaveOut 內部設定） | ~1ms | 60 FPS |
| 音效關閉 | **15.6ms**（Windows 預設） | ~15ms | 迴圈執行 2 次 × 15ms = 30 FPS |

WaveOut API 在開啟時會內部呼叫 `timeBeginPeriod(1)`，將全系統計時器精度提升至 1ms。關閉 WaveOut 後精度回到預設的 15.6ms，導致 `Thread.Sleep(1)` 每次睡 15ms，FPS 限制迴圈只執行 1 次就超過 16.66ms，實際變成每幀 ~30ms（30 FPS）。

### 4.3 修正方法

在 **`NesCore/Main.cs`** 的 `run()` 方法開頭和結尾，明確設定計時器精度，使其不依賴 WaveOut 是否開啟：

```csharp
// 修改前
static public void run()
{
    StopWatch.Restart();
    while (!exit) { ... }
    Console.WriteLine("exit..");
}

// 修改後
static public void run()
{
    timeBeginPeriod(1);    // ← 新增：強制設定 1ms 計時器精度
    StopWatch.Restart();
    while (!exit)
    {
        cpu_step();
        do
        {
            ppu_step(); ppu_step(); ppu_step();
            apu_step();
        } while (--cpu_cycles > 0);
    }
    timeEndPeriod(1);      // ← 新增：結束時恢復預設計時器精度
    Console.WriteLine("exit..");
}
```

`timeBeginPeriod` / `timeEndPeriod` 同樣來自 `winmm.dll`，在 `APU.cs` 中宣告：

```csharp
[DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint uPeriod);
[DllImport("winmm.dll")] static extern uint timeEndPeriod(uint uPeriod);
```

### 4.4 修正效果

修正後，不論音效開啟或關閉，`Thread.Sleep(1)` 的精度都維持在 1ms，FPS 限制迴圈能正確地在 16.66ms 內完成調整，穩定輸出 60 FPS。

---

## 5. 建置說明

執行 `build.bat`（需已安裝 Visual Studio 2022）：

```
build.bat         ← 雙擊執行
build.ps1         ← 實際建置邏輯（由 build.bat 呼叫）
```

輸出位置：
- Debug：`AprNes\bin\Debug\AprNes.exe`
- Release：`AprNes\bin\Release\AprNes.exe`
