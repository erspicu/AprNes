# NesCore 整合使用說明

NesCore 是 AprNes 的 NES 模擬器核心，提供三種整合方式：

| 方式 | 適合語言 | 需要 .NET Runtime | 說明 |
|------|---------|:-----------------:|------|
| **C# 原始碼引用** | C# / .NET | ✅ | 直接 link 原始檔，彈性最高，效能最佳 |
| **NesCore.dll（Managed）** | C# / .NET | ✅ | 編譯為標準 .NET DLL，適合不想帶原始碼的情境 |
| **NesCoreNative.dll（AOT）** | C、C++、Python、Rust… | ❌ | Native AOT C ABI，不需安裝 .NET Runtime |

---

## 方式一：C# 直接引用

### 1. 加入原始碼

在你的 `.csproj` 加入 NesCore 的 source link（與 AprNesAOT10 相同做法）：

```xml
<ItemGroup>
  <Compile Include="../AprNes/NesCore/Main.cs"    Link="NesCore/Main.cs" />
  <Compile Include="../AprNes/NesCore/CPU.cs"     Link="NesCore/CPU.cs" />
  <Compile Include="../AprNes/NesCore/PPU.cs"     Link="NesCore/PPU.cs" />
  <Compile Include="../AprNes/NesCore/APU.cs"     Link="NesCore/APU.cs" />
  <Compile Include="../AprNes/NesCore/MEM.cs"     Link="NesCore/MEM.cs" />
  <Compile Include="../AprNes/NesCore/IO.cs"      Link="NesCore/IO.cs" />
  <Compile Include="../AprNes/NesCore/JoyPad.cs"  Link="NesCore/JoyPad.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/IMapper.cs"       Link="NesCore/Mapper/IMapper.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper000.cs"     Link="NesCore/Mapper/Mapper000.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper001.cs"     Link="NesCore/Mapper/Mapper001.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper002.cs"     Link="NesCore/Mapper/Mapper002.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper003.cs"     Link="NesCore/Mapper/Mapper003.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper004.cs"     Link="NesCore/Mapper/Mapper004.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper004RevA.cs" Link="NesCore/Mapper/Mapper004RevA.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper004MMC6.cs" Link="NesCore/Mapper/Mapper004MMC6.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper007.cs"     Link="NesCore/Mapper/Mapper007.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper011.cs"     Link="NesCore/Mapper/Mapper011.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper066.cs"     Link="NesCore/Mapper/Mapper066.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper071.cs"     Link="NesCore/Mapper/Mapper071.cs" />
</ItemGroup>
```

專案需啟用 `unsafe`：
```xml
<PropertyGroup>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
</PropertyGroup>
```

---

### 2. 最小啟動範例

```csharp
using AprNes;
using System.Threading;

// 1. 設定 headless（不開視窗）
NesCore.HeadlessMode = true;
NesCore.AudioEnabled = false;   // 不需要音效時關掉
NesCore.LimitFPS     = true;    // true = ~60 FPS；false = 全速

// 2. 錯誤處理
NesCore.OnError = msg => Console.Error.WriteLine("[NES ERROR] " + msg);

// 3. 訂閱畫面更新事件
NesCore.VideoOutput += (sender, e) =>
{
    // 每 frame 呼叫一次（約 60 FPS）
    // NesCore.ScreenBuf1x 是 256×240 ARGB pixels（uint*）
    RenderFrame(NesCore.ScreenBuf1x);
};

// 4. 載入 ROM
byte[] romBytes = File.ReadAllBytes("game.nes");
if (!NesCore.init(romBytes))
{
    Console.Error.WriteLine("ROM 載入失敗");
    return;
}

// 5. 在背景執行緒跑模擬器
NesCore.exit = false;
var emuThread = new Thread(NesCore.run) { IsBackground = true };
emuThread.Start();

// 6. 停止模擬器
// NesCore.exit = true;
// NesCore._event.Set();   // 喚醒暫停中的模擬器（LimitFPS=true 時）
// emuThread.Join(2000);
```

---

### 3. 全部公開 API

#### 初始化與生命週期

| 成員 | 類型 | 說明 |
|------|------|------|
| `NesCore.init(byte[] romBytes)` | `bool` | 載入 ROM 並初始化所有子系統。成功 `true`，失敗 `false` |
| `NesCore.run()` | `void` | 主模擬迴圈（阻塞）。應在獨立 Thread 執行 |
| `NesCore.exit` | `bool` | 設為 `true` 讓 `run()` 迴圈結束 |
| `NesCore._event` | `ManualResetEvent` | `LimitFPS=true` 時模擬器等待此事件；呼叫 `_event.Set()` 可喚醒 |
| `NesCore.SoftReset()` | `void` | 軟重置（相當於按下 Reset 按鈕） |

#### 畫面輸出

| 成員 | 類型 | 說明 |
|------|------|------|
| `NesCore.VideoOutput` | `event EventHandler` | 每 frame 完成後觸發 |
| `NesCore.ScreenBuf1x` | `uint*` | 256×240 ARGB 畫素緩衝（61,440 個 `uint32`），每 frame 更新 |
| `NesCore.screen_lock` | `bool` | 設為 `true` 時暫停 PPU 寫入（讀取畫面時防止撕裂） |
| `NesCore.frame_count` | `int` | 累計 frame 數（volatile） |

畫面 buffer 存取範例：
```csharp
unsafe void RenderFrame(uint* buf)
{
    for (int y = 0; y < 240; y++)
        for (int x = 0; x < 256; x++)
        {
            uint argb = buf[y * 256 + x];
            byte r = (byte)(argb >> 16);
            byte g = (byte)(argb >> 8);
            byte b = (byte)(argb);
            // 繪製 (x, y) 點
        }
}
```

#### 音效輸出

| 成員 | 類型 | 說明 |
|------|------|------|
| `NesCore.AudioSampleReady` | `Action<short>` | 44100 Hz，16-bit mono 樣本回呼 |
| `NesCore.AudioEnabled` | `bool` | `false` 時停止產生音效樣本（節省 CPU） |
| `NesCore.Volume` | `int` | 音量 0～100（預設 70） |

```csharp
NesCore.AudioEnabled = true;
NesCore.AudioSampleReady += sample =>
{
    // 44100 Hz, 16-bit signed mono
    audioPlayer.Write(sample);
};
```

#### 手把輸入（Player 1）

| 按鈕索引 | 對應按鈕 |
|----------|---------|
| 0 | A |
| 1 | B |
| 2 | Select |
| 3 | Start |
| 4 | Up |
| 5 | Down |
| 6 | Left |
| 7 | Right |

```csharp
// 按下 A 鍵
NesCore.P1_ButtonPress(0);

// 放開 A 鍵
NesCore.P1_ButtonUnPress(0);
```

#### SRAM 存取（電池備份記憶體）

```csharp
// 存檔：讀出 8KB SRAM
if (NesCore.HasBattery)
{
    byte[] save = NesCore.DumpSRam();   // 傳回 8192 bytes
    File.WriteAllBytes("game.sav", save);
}

// 讀檔：在 init() 後、run() 前載入
byte[] saveData = File.ReadAllBytes("game.sav");
NesCore.LoadSRam(saveData);
```

#### ROM 資訊（init 後可讀）

```csharp
int  mapper    = NesCore.RomMapper;       // Mapper 號碼
int  prgCount  = NesCore.RomPrgCount;     // PRG-ROM 頁數（16KB/頁）
int  chrCount  = NesCore.RomChrCount;     // CHR-ROM 頁數（8KB/頁）
bool horizMirr = NesCore.RomHorizMirror;  // true=水平鏡射, false=垂直
bool hasBatt   = NesCore.HasBattery;      // 是否有電池備份
```

#### 其他控制旗標

| 成員 | 預設 | 說明 |
|------|------|------|
| `NesCore.LimitFPS` | `false` | `true` = 限速 ~60 FPS；`false` = 全速執行 |
| `NesCore.HeadlessMode` | `false` | `true` = headless（無 UI）模式，抑制部分 Console 輸出 |
| `NesCore.OnError` | `null` | `Action<string>` 錯誤處理回呼 |
| `NesCore.Mapper_Allow` | `{0,1,2,3,4,7,11,66}` | 允許的 Mapper 清單，可自行擴充 |

#### 支援的 Mapper

| 號碼 | 常見名稱 | 代表遊戲 |
|------|---------|---------|
| 0 | NROM | 超級瑪利歐兄弟、俄羅斯方塊 |
| 1 | MMC1 (SxROM) | 薩爾達傳說、Final Fantasy |
| 2 | UxROM | 洛克人、惡魔城 |
| 3 | CNROM | 忍者龜 |
| 4 | MMC3 (TxROM) | 超級瑪利歐兄弟3、洛克人3-6 |
| 7 | AxROM | 棒球明星 |
| 11 | Color Dreams | 部分早期遊戲 |
| 66 | GxROM | 超級瑪利歐兄弟+大金剛 |

---

## 方式一B：NesCore.dll（Managed .NET DLL）

適合情境：你有另一個 .NET 專案想引用 NesCore，但**不想把原始碼一起帶進去**（例如發佈二進位、或多個專案共用同一份編譯好的 DLL）。

### 1. 建立 NesCore 類別庫專案

建立 `NesCore.csproj`（放在任意目錄，例如 `NesCore/`）：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net8.0</TargetFramework>   <!-- 或 net10.0 / net6.0 -->
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <AssemblyName>NesCore</AssemblyName>
    <RootNamespace>AprNes</RootNamespace>
  </PropertyGroup>

  <!-- 引用 AprNes 的 NesCore 原始檔 -->
  <ItemGroup>
    <Compile Include="../AprNes/NesCore/Main.cs"    Link="NesCore/Main.cs" />
    <Compile Include="../AprNes/NesCore/CPU.cs"     Link="NesCore/CPU.cs" />
    <Compile Include="../AprNes/NesCore/PPU.cs"     Link="NesCore/PPU.cs" />
    <Compile Include="../AprNes/NesCore/APU.cs"     Link="NesCore/APU.cs" />
    <Compile Include="../AprNes/NesCore/MEM.cs"     Link="NesCore/MEM.cs" />
    <Compile Include="../AprNes/NesCore/IO.cs"      Link="NesCore/IO.cs" />
    <Compile Include="../AprNes/NesCore/JoyPad.cs"  Link="NesCore/JoyPad.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/IMapper.cs"       Link="NesCore/Mapper/IMapper.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper000.cs"     Link="NesCore/Mapper/Mapper000.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper001.cs"     Link="NesCore/Mapper/Mapper001.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper002.cs"     Link="NesCore/Mapper/Mapper002.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper003.cs"     Link="NesCore/Mapper/Mapper003.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper004.cs"     Link="NesCore/Mapper/Mapper004.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper004RevA.cs" Link="NesCore/Mapper/Mapper004RevA.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper004MMC6.cs" Link="NesCore/Mapper/Mapper004MMC6.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper007.cs"     Link="NesCore/Mapper/Mapper007.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper011.cs"     Link="NesCore/Mapper/Mapper011.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper066.cs"     Link="NesCore/Mapper/Mapper066.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper071.cs"     Link="NesCore/Mapper/Mapper071.cs" />
  </ItemGroup>
</Project>
```

建置：
```powershell
dotnet build NesCore.csproj -c Release
# 輸出：NesCore/bin/Release/net8.0/NesCore.dll
```

---

### 2. 在你的專案中引用

**選項 A：ProjectReference（推薦，開發期間自動 rebuild）**

```xml
<ItemGroup>
  <ProjectReference Include="../NesCore/NesCore.csproj" />
</ItemGroup>
```

**選項 B：直接引用編譯好的 DLL**

```xml
<ItemGroup>
  <Reference Include="NesCore">
    <HintPath>../NesCore/bin/Release/net8.0/NesCore.dll</HintPath>
  </Reference>
</ItemGroup>
```

你的專案仍需啟用 unsafe：
```xml
<AllowUnsafeBlocks>true</AllowUnsafeBlocks>
```

---

### 3. 使用方式

Managed DLL 的 API 與「方式一：C# 原始碼引用」**完全相同**，都是透過 `AprNes.NesCore` 靜態類別存取。

```csharp
using AprNes;
using System.Threading;

// 設定
NesCore.HeadlessMode = true;
NesCore.AudioEnabled = false;
NesCore.LimitFPS     = true;
NesCore.OnError      = msg => Console.Error.WriteLine("[NES] " + msg);

// 訂閱畫面事件
unsafe
{
    NesCore.VideoOutput += (_, _) =>
    {
        uint* screen = NesCore.ScreenBuf1x;  // 256×240 ARGB
        // 繪製 screen...
    };
}

// 載入 ROM
byte[] rom = File.ReadAllBytes("game.nes");
if (!NesCore.init(rom)) return;

// 跑模擬器
NesCore.exit = false;
var t = new Thread(NesCore.run) { IsBackground = true };
t.Start();
```

完整 API 說明見「方式一」的 [全部公開 API](#3-全部公開-api) 章節，兩者完全一致。

---

### 方式一 vs 方式一B 的選擇

| | C# 原始碼引用 | NesCore.dll（Managed） |
|---|---|---|
| 編譯速度 | 稍慢（每次重新編譯 NesCore） | 快（引用預編譯 DLL） |
| 偵錯 | ✅ 可直接 step into NesCore 程式碼 | ⚠️ 需要 PDB 檔才能 step in |
| 發佈 | 原始碼一起帶走 | 只需帶 DLL |
| 版本管理 | NesCore 修改立即生效 | 需重新 build DLL 再更新 |
| 適合場景 | 開發中、需要調試 NesCore | 發佈給他人、多專案共用 |

---

## 方式二：NesCoreNative.dll（C ABI）

適用於 C、C++、Python、Rust 等任何能呼叫 native DLL 的語言。**不需要安裝 .NET Runtime**。

### 建置 DLL

```powershell
cd NesCoreNative
dotnet publish -r win-x64 -c Release
# 輸出：NesCoreNative\bin\Release\net8.0\win-x64\publish\NesCoreNative.dll
```

### C API 一覽

```c
// 回呼設定（在 nescore_init 之前呼叫）
void nescore_set_video_callback(void (*cb)());
void nescore_set_audio_callback(void (*cb)(short sample));
void nescore_set_error_callback(void (*cb)(const char* msg, int len));

// 核心控制
int      nescore_init(uint8_t* romData, int len);  // 1=成功, 0=失敗
void     nescore_run();                             // 在背景 Thread 跑（非阻塞）
void     nescore_stop();                            // 停止模擬器

// 畫面
uint32_t* nescore_get_screen();                     // 256×240 ARGB pixels

// 手把（btn: 0=A 1=B 2=SEL 3=START 4=UP 5=DOWN 6=LEFT 7=RIGHT）
void nescore_joypad_press(uint8_t btn);
void nescore_joypad_release(uint8_t btn);

// 設定
void nescore_set_volume(int vol);      // 0～100
void nescore_set_limitfps(int enable); // 0=全速, 1=~60fps

// 效能測試（阻塞）
int  nescore_benchmark(int seconds);   // 傳回 frame 總數
```

---

### C 範例

```c
#include <windows.h>
#include <stdio.h>
#include <stdint.h>

typedef void     (*fn_set_video_cb)(void (*cb)());
typedef void     (*fn_set_error_cb)(void (*cb)(const char*, int));
typedef int      (*fn_init)(uint8_t*, int);
typedef void     (*fn_run)();
typedef void     (*fn_stop)();
typedef uint32_t*(*fn_get_screen)();
typedef void     (*fn_joypad_press)(uint8_t);
typedef void     (*fn_joypad_release)(uint8_t);
typedef void     (*fn_set_limitfps)(int);

// 全域 frame 計數
volatile int g_frames = 0;

void on_video() { g_frames++; }
void on_error(const char* msg, int len) { fprintf(stderr, "[NES] %.*s\n", len, msg); }

int main()
{
    HMODULE dll = LoadLibraryA("NesCoreNative.dll");
    if (!dll) { fprintf(stderr, "DLL not found\n"); return 1; }

#define LOAD(T, name) T name = (T)GetProcAddress(dll, #name)
    LOAD(fn_set_video_cb,  nescore_set_video_callback);
    LOAD(fn_set_error_cb,  nescore_set_error_callback);
    LOAD(fn_init,          nescore_init);
    LOAD(fn_run,           nescore_run);
    LOAD(fn_stop,          nescore_stop);
    LOAD(fn_get_screen,    nescore_get_screen);
    LOAD(fn_joypad_press,  nescore_joypad_press);
    LOAD(fn_joypad_release,nescore_joypad_release);
    LOAD(fn_set_limitfps,  nescore_set_limitfps);
#undef LOAD

    // 設定回呼
    nescore_set_video_callback(on_video);
    nescore_set_error_callback(on_error);

    // 載入 ROM
    FILE* f = fopen("game.nes", "rb");
    fseek(f, 0, SEEK_END); int len = ftell(f); rewind(f);
    uint8_t* rom = (uint8_t*)malloc(len);
    fread(rom, 1, len, f); fclose(f);

    if (!nescore_init(rom, len)) { fprintf(stderr, "Init failed\n"); return 1; }
    free(rom);

    // 限速 60 FPS 執行
    nescore_set_limitfps(1);
    nescore_run();

    // 按下 Start 鍵（index=3）後放開
    Sleep(1000);
    nescore_joypad_press(3);
    Sleep(100);
    nescore_joypad_release(3);

    // 主迴圈（每 frame 取畫面）
    while (1) {
        uint32_t* screen = nescore_get_screen();
        // 用 screen[y*256+x] 取像素並繪製
        Sleep(16);
    }

    nescore_stop();
    FreeLibrary(dll);
    return 0;
}
```

---

### Python 範例

```python
import ctypes, pathlib, time

dll = ctypes.CDLL(str(pathlib.Path("NesCoreNative.dll").resolve()))

# 定義函式簽名
dll.nescore_set_video_callback.argtypes = [ctypes.c_void_p]
dll.nescore_init.argtypes  = [ctypes.c_char_p, ctypes.c_int]
dll.nescore_init.restype   = ctypes.c_int
dll.nescore_run.argtypes   = []
dll.nescore_stop.argtypes  = []
dll.nescore_get_screen.restype = ctypes.POINTER(ctypes.c_uint32)
dll.nescore_joypad_press.argtypes   = [ctypes.c_uint8]
dll.nescore_joypad_release.argtypes = [ctypes.c_uint8]
dll.nescore_set_limitfps.argtypes   = [ctypes.c_int]
dll.nescore_benchmark.argtypes = [ctypes.c_int]
dll.nescore_benchmark.restype  = ctypes.c_int

# 設定畫面回呼
frames = 0
@ctypes.CFUNCTYPE(None)
def on_video():
    global frames
    frames += 1

dll.nescore_set_video_callback(on_video)

# 載入 ROM
with open("game.nes", "rb") as f:
    rom = f.read()
rom_buf = (ctypes.c_uint8 * len(rom))(*rom)

if not dll.nescore_init(rom_buf, len(rom)):
    raise RuntimeError("ROM init failed")

# 效能測試（10 秒）
count = dll.nescore_benchmark(10)
print(f"Benchmark: {count} frames in 10s = {count/10:.1f} FPS")

# 一般執行
dll.nescore_set_limitfps(1)
dll.nescore_run()

time.sleep(2)

# 取畫面 buffer（256×240 uint32 ARGB）
screen = dll.nescore_get_screen()
pixel_0_0 = screen[0]   # 左上角像素

dll.nescore_stop()
```

---

## 重要限制與注意事項

### 1. 所有狀態是 static（單例）

NesCore 的全部狀態是 `static`，**同一個 process 只能跑一個 NES 實例**。若需要同時跑兩個 ROM（例如對戰、多開），必須開不同 process。

### 2. run() 必須在獨立 Thread

`NesCore.run()` 是阻塞迴圈。主 Thread 不可直接呼叫（會 block UI）。

```csharp
// 正確
var t = new Thread(NesCore.run) { IsBackground = true };
t.Start();

// 錯誤：直接呼叫會卡住
NesCore.run();
```

### 3. ScreenBuf1x 的執行緒安全

`ScreenBuf1x` 在模擬器 Thread 中寫入，在你的 render Thread 中讀取。若需要嚴格防止畫面撕裂：

```csharp
// 讀取前鎖住
NesCore.screen_lock = true;
// ... 讀取 ScreenBuf1x ...
NesCore.screen_lock = false;
```

> 注意：`screen_lock = true` 期間 PPU 仍在跑，只是不寫入 buffer；短暫鎖定（<1ms）通常安全。

### 4. init() 之前不可呼叫 run()

`init()` 會分配所有 unmanaged 記憶體（ROM、VRAM、SRAM、screen buffer）。未呼叫 `init()` 就呼叫 `run()` 會導致 null pointer 存取崩潰。

### 5. Mapper 不支援時的行為

`init()` 會呼叫 `ShowError()` 然後回傳 `false`。請確認你的 ROM 使用支援的 Mapper（見上方列表）。不支援的 Mapper 不會拋出 Exception，會透過 `OnError` 回呼通知。

---

*文件更新：2026-03-03*
