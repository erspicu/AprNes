# NesCore 架構重構建議報告

> 目標：讓 NesCore 成為純粹的模擬邏輯核心，所有系統層（顯示、音效、輸入、錯誤回報）由外部負責實作。

---

## 核心原則

```
NesCore 只做：
  ✅ 計算（CPU 指令、PPU 像素、APU 樣本值、Mapper 邏輯）
  ✅ 產生輸出資料（畫面 buffer、PCM 數值、事件通知）
  ✅ 接受輸入注入（手把按鍵狀態）

NesCore 不做：
  ❌ 決定如何播放聲音（winmm、SDL、NAudio...）
  ❌ 決定如何顯示畫面（WinForms、DirectX、OpenGL...）
  ❌ 控制 FPS 節流（Sleep/Stopwatch）
  ❌ 彈出錯誤視窗（MessageBox）
  ❌ 設定作業系統計時器精度（timeBeginPeriod）
```

---

## 現況問題清單

### 問題 1 — APU.cs：音效「輸出」混在音效「模擬」裡

**檔案：** `NesCore/APU.cs`

**目前狀況：**
`APU.cs` 同時負責兩件性質不同的事：

| 職責 | 類型 | 應該在哪 |
|------|------|----------|
| `apu_step()` 計算各聲道波形、包絡、掃頻、混音，產生 PCM 樣本數值 | 模擬邏輯 | ✅ 留在 NesCore |
| `DllImport winmm.dll`（waveOutOpen/Write/Close 等） | 系統層 | ❌ 移出 |
| `WAVEFORMATEX` / `WAVEHDR` 結構宣告 | 系統層 | ❌ 移出 |
| `openAudio()` / `closeAudio()` | 系統層 | ❌ 移出 |
| 管理 4 個 audio buffer 的 GCHandle Pin | 系統層 | ❌ 移出 |
| 直接將樣本寫入 WaveOut buffer 並送出 | 系統層 | ❌ 移出 |

**影響：**
- NesCore 強制綁定 Windows WaveOut，無法在其他平台或音效後端使用。
- 無頭測試模式（TestRunner）必須靠 `AudioEnabled = false` 旗標繞過，而非乾淨地不注入音效實作。

**建議做法：**

APU 在產生每個 PCM 樣本時，改為透過 callback 或 ring buffer 交出數值：

```csharp
// NesCore 只暴露介面
public static Action<short> AudioSampleReady;
// 或 ring buffer 方式
public static short[] AudioRingBuffer;
public static int     AudioWritePos;
```

外部另建 `WaveOutPlayer`（或任何後端）去消費資料。  
未來換成 SDL2、NAudio、OpenAL 等，完全不需要修改 NesCore。

---

### 問題 2 — PPU.cs：FPS 節流混在畫面產生裡

**檔案：** `NesCore/PPU.cs`

**目前狀況：**

| 職責 | 類型 | 應該在哪 |
|------|------|----------|
| 每個 dot 計算像素，填入 `ScreenBuf1x` | 模擬邏輯 | ✅ 留在 NesCore |
| 掃完第 240 條掃描線後呼叫 `Thread.Sleep(1)` | 系統層 | ❌ 移出 |
| `Stopwatch` + `_fpsDeadline` 累積計時 | 系統層 | ❌ 移出 |
| `LimitFPS` 旗標控制節流行為 | 系統層 | ❌ 移出 |

**建議做法：**

PPU 掃完第 240 條掃描線後只需觸發已有的 `VideoOutput` event，不做任何等待。  
FPS 節流由外部（UI 或 TestRunner）在 `VideoOutput` handler 中自行決定。

```csharp
// PPU 只做這件事
VideoOutput?.Invoke(null, VideoOut_arg);

// UI 端自己在 handler 裡加 FPS 限制
NesCore.VideoOutput += (s, e) => {
    RenderToScreen();
    ThrottleToTargetFps();   // UI 自己管
};
```

---

### 問題 3 — Main.cs：對 Windows.Forms 的相依性

**檔案：** `NesCore/Main.cs`

**目前狀況：**

| 職責 | 類型 | 應該在哪 |
|------|------|----------|
| ROM 解析（iNES header）、記憶體分配 | 模擬邏輯 | ✅ 留在 NesCore |
| `init()` / `run()` / `SaveRam()` | 模擬邏輯 | ✅ 留在 NesCore |
| `using System.Windows.Forms` → `MessageBox.Show()` | 系統層 | ❌ 移出 |
| `timeBeginPeriod(1)` / `timeEndPeriod(1)` | 系統層 | ❌ 移出 |

**建議做法：**

`ShowError` 改用 delegate，NesCore 只負責發出通知，由外部決定如何顯示：

```csharp
// NesCore
public static Action<string> OnError;

static public void ShowError(string msg)
{
    OnError?.Invoke(msg);
}
```

```csharp
// WinForms UI 端設定
NesCore.OnError = msg => MessageBox.Show(msg);

// TestRunner 設定
NesCore.OnError = msg => Console.Error.WriteLine("ERROR: " + msg);
```

`timeBeginPeriod` / `timeEndPeriod` 移到 `run()` 的呼叫方（UI 或 TestRunner）執行，  
NesCore 的 `run()` 不應該自行調整作業系統計時器精度。

---

### 問題 4 — JoyPad.cs（現況已接近正確，小幅確認）

**檔案：** `NesCore/JoyPad.cs`

目前設計已相當合理：

| 職責 | 類型 | 評估 |
|------|------|------|
| `gamepad_r_4016()` / `gamepad_w_4016()` | NES 硬體暫存器行為 | ✅ 正確，留在 NesCore |
| `P1_ButtonPress()` / `P1_ButtonUnPress()` | 外部注入介面 | ✅ 正確，是對外 API |

外部（UI/鍵盤/手把）在適當時機呼叫這兩個方法即可，無需修改。

---

## 重構後的整體架構

```
┌───────────────────────────────────────────────────────┐
│                    NesCore（純模擬）                    │
│                                                       │
│  CPU.cs   MEM.cs   PPU.cs   APU.cs   IO.cs            │
│  JoyPad.cs   Mapper/                                  │
│                                                       │
│  ── 對外輸出 ──────────────────────────────────────── │
│  ScreenBuf1x          → 256×240 ARGB 像素 buffer      │
│  AudioSampleReady     → PCM short 樣本 callback       │
│  VideoOutput (event)  → 每幀結束通知                  │
│  OnError (delegate)   → 錯誤訊息通知                  │
│                                                       │
│  ── 對外輸入 ──────────────────────────────────────── │
│  P1_ButtonPress()     → 按下按鍵                      │
│  P1_ButtonUnPress()   → 放開按鍵                      │
└───────────────────┬───────────────────────────────────┘
                    │
        ┌───────────┴──────────────┐
        ▼                          ▼
┌───────────────┐        ┌─────────────────────┐
│  WinForms UI  │        │     TestRunner      │
│               │        │    （無頭模式）      │
│ 訂閱 Video-   │        │                     │
│  Output 顯示  │        │ 訂閱 VideoOutput    │
│ WaveOutPlayer │        │  做 blargg 檢測     │
│  播放音效     │        │ 無音效，全速執行    │
│ 鍵盤/手把     │        │ 輸入透過 InputEvent │
│  注入按鍵     │        │  注入              │
│ FPS 節流      │        │ FPS 不限制          │
└───────────────┘        └─────────────────────┘
```

---

## 重構優先順序

| 優先 | 項目 | 改動範圍 | 預期效益 |
|------|------|----------|----------|
| 🔴 高 | APU 音效輸出移出，改用 callback/buffer 介面 | `APU.cs` + 新建 `WaveOutPlayer.cs` | 移除 winmm P/Invoke，跨平台音效後端 |
| 🔴 高 | `ShowError` 改 delegate，移除 `using System.Windows.Forms` | `Main.cs` | NesCore 零 WinForms 相依 |
| 🟡 中 | FPS 限制器移出 PPU，由外部 handler 控制 | `PPU.cs` + `AprNesUI.cs` | 模擬速度完全由外部決定 |
| 🟢 低 | `timeBeginPeriod` 移到呼叫方 | `APU.cs`/`Main.cs` + UI/TestRunner | 系統計時器設定責任分離 |

---

## 完成後可驗證的指標

- `NesCore/` 資料夾內所有 `.cs` 不再有 `using System.Windows.Forms`
- `NesCore/` 資料夾內所有 `.cs` 不再有 `DllImport("winmm.dll")` 的音效輸出呼叫
- TestRunner 不需要設定 `AudioEnabled = false` 就能乾淨執行（因為根本沒注入音效）
- 替換音效後端只需修改 `WaveOutPlayer.cs`（或新建另一個播放器），不動 NesCore

---

*建立日期：2026-02-26*
