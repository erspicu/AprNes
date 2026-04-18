# AprNes Avalonia — CRT GPU 加速設計（v3 修訂版）

日期：2026-04-18
適用專案：`AprNesAvalonia/`（Avalonia 11.3.13 + SkiaSharp 3.119.3-preview + .NET 10）
狀態：**Phase 0-3A+3B 已完成，Phase 3C 決議不實作**（見 [NTSC_GPU_Porting_Design.md §12](NTSC_GPU_Porting_Design.md)）

## 完成狀態快覽

| Phase | 內容 | 狀態 |
|:-----:|:----:|:----:|
| 0 | MSBuild build-time CrtImpl 切換 | ✅ |
| 1 | Runtime dispatch (scalar/simd/gpu)，Shared + 靜態類別 | ✅ |
| 2 | SkSL GPU backend (raster SKSurface headless) | ✅ |
| 3A | Avalonia render-thread D3D11 真 GPU | ✅ |
| 3B | Phosphor writeback snapshot 優化（蕭師 shader 單 pass） | ✅ |
| **3C** | **NTSC fast-path GPU 化** | **❌ 不實作**（見 NTSC doc §12） |

最終成果：10x 解析度下 presented 58 FPS（逼近 vsync），emu thread 相對 SIMD 2.0× 加速。

---

## 0. 設計決策確認（來自使用者）

| # | 決策 | 說明 |
|:-:|------|------|
| 1 | **v1 跳過 NTSC 合成** | 簡化 MVP，NTSC 延後到 v3 |
| 2 | **Fallback 走 SIMD** | GPU 建立失敗（shader 編譯錯、GPU 不可用）自動切 `CrtImpl = Simd` |
| 3 | **Shader 從檔案讀取** | 不硬編字串；新增 `AprNesAvalonia/Shaders/` 目錄放 `.sksl` 檔 |
| 4 | **Phosphor decay v1 必備** | CPU 純量與 SIMD 都已實作（`CrtScreen.cs:63`、`CrtScreen.Simd.cs:87`）；GPU 版 v1 必須跟上 |
| 5 | **Headless mode 要支援 GPU** | Skia runtime effect 在無視窗環境也能以 CPU rasterizer 執行，保 deterministic；真 GPU 在 headless 為 v2 目標 |
| 6 | **動態派發機制** | 各重運算階段可獨立選 Scalar / SIMD / GPU，透過 config + capability detection 動態分配 |
| 7 | **AprNes vs AprNes Ava 分工** | AprNes (.NET 4.8.1) = 只走 Scalar；AprNes Ava (.NET 10) = 可選 Scalar / SIMD / GPU |
| 8 | **Ava 開發期預設** | 預設 `CrtImpl = Gpu`（fallback Simd）；未完成期間暫時以 Simd 為預設 |
| 9 | **ARM 平台考量** | .NET 10 on ARM → SIMD 用 NEON；透過 `Vector<T>` 跨平台抽象（AVX2 與 NEON 皆自動）|
| 10 | **.NET 10 動態連結 method** | 以 `delegate*<>` 函式指標 / `ICrtBackend` 介面派發；runtime 決定實作 |

---

## 0.5 .NET 10 動態 Method 派發機制選型

使用者提到 ".NET10 好像有一種呼叫同一隻 METHOD，但 method 可以動態連結到哪個真實實作的處理方式"。對應的 .NET 10 可用選項：

| 選項 | 語法 | 開銷 | 適合本情境 |
|------|------|:----:|:---------:|
| **Function pointer** | `delegate*<void>` | ~0（直接跳轉）| ★★★ |
| Static abstract members in interface | `interface IBackend { static abstract void Render(); }` + generic | 0（monomorphize）| ★★（要 generic，runtime 難切）|
| Virtual interface dispatch | `ICrtBackend.Render()` | 1-2 ns（vtable）| ★★★（可讀性佳）|
| Dynamic PGO / inlining | JIT 自動 | 0 | 不可預期 |

**選定**：以 `ICrtBackend` 介面為主要派發（可讀性 + 可測試性），per-frame 調用不需要極致效能；內部熱路徑可用 `delegate*<>` 存底（若有需要）。

```csharp
internal interface ICrtBackend {
    void Init();
    void Render();
    void ApplyProfile();
    void Dispose();
}

internal sealed class CrtScalarBackend : ICrtBackend { ... }
internal sealed class CrtSimdBackend   : ICrtBackend { ... }  // 只在 Ava 編譯
internal sealed class CrtGpuBackend    : ICrtBackend { ... }  // 只在 Ava 編譯
```

### ARM 平台（NEON）考量
`CrtScreen.Simd.cs` 目前直接用 `Avx2.GatherVector256` 等 x86 專屬 intrinsics。ARM 版需要：
- **短期**：以 `Vector<T>`（`System.Numerics`）取代 `Vector256<T>` 寫法 — `Vector<T>` 自動對應 SSE/AVX/NEON 最大寬度
- **長期**：若要 NEON 專屬最佳化，另建 `CrtScreen.Neon.cs` 用 `AdvSimd` intrinsics（對應 ARM64）
- `CrtBackend` 工廠選 `CrtSimdBackend` 時再細分 x86 / ARM 版本

目前 AprNesAvalonia 尚未在 ARM 上發布，`Vector<T>` 抽象就足夠。**NEON 專屬 backend** 列為 v4 目標。

---

## 1. 目的與定位

### 為什麼做
目前 CRT 模擬（scanline / shadow mask / horizontal blur / phosphor decay / convergence / barrel distortion）在 CPU SIMD（`CrtScreen.Simd.cs`，`Vector256<T>` / AVX2）上執行，4x 解析度時仍佔用顯著 CPU 時間（MEMORY.md：DSP Mode 2 4x 僅 82.65 FPS）。用 GPU fragment shader 可：

- 釋放 CPU（騰出給 emulator core / APU / mapper）
- 隨輸出解析度擴張近乎免費（fragment 平行度 >> AVX2 lane 數）
- 未來 NTSC 合成（per-dot 1D composite 訊號）在 GPU 更自然

### 三條路並存（按專案區分）
| 專案 | 可用 Impl | 預設 |
|------|-----------|:----:|
| **AprNes** (.NET 4.8.1 WinForms) | Scalar | Scalar（固定，無選項）|
| **AprNesAvalonia** (.NET 10) | Scalar / Simd / Gpu | Gpu（fallback Simd；v1 未完前暫為 Simd）|

| Impl | 依賴 | 跨平台 | 用途 |
|------|------|:------:|------|
| `Scalar` | 純 C# | ✅ | 最低相容性、WinForms 版唯一 |
| `Simd` | `Vector<T>` + `Vector256` (x86) / `AdvSimd` (ARM) | ✅ | 生產預設；ARM 自動切 NEON |
| `Gpu` | SkiaSharp `SKRuntimeEffect` | ✅（CPU fallback）| 實驗性，性能為主 |

**不動** `CrtScreen.cs`、`CrtScreen.Simd.cs`。GPU 是 Avalonia 專屬的第三條路。

---

## 2. 技術堆疊

| 層 | 技術 | 說明 |
|----|------|------|
| Host | Avalonia 11.3.13 | 已整合 |
| 繪圖 | SkiaSharp 2.88.9 | Avalonia 內建 backend |
| Shader | **SkSL**（SkiaSharp Shading Language）| `SKRuntimeEffect.CreateShader` |
| 橋接 | `ISkiaSharpApiLeaseFeature` | 已用於 `EmuScreenControl.EmuDrawOperation` |
| 資料 | `SKBitmap.InstallPixels` + `SKSurface` ping-pong | NES 輸入仍 zero-copy |

### SkSL 能力與限制
- ✅ Fragment shader（`half4 main(float2 coord)`）、多個 `uniform shader` child、`uniform` 純量 / vec2-4
- ✅ Skia 在有 GPU 時走 GPU、無 GPU 時用 CPU rasterizer 執行 runtime effect（headless 可用）
- ❌ 無 compute shader / structured buffer
- ❌ 動態長度迴圈受限（常數邊界）

---

## 3. 現有 CRT Pipeline（CPU 版）重點

流程摘要（`CrtScreen.Simd.cs`）：
```
NES 256x240 RGB
  ↓ (optional) NTSC Ntsc_FlushPendingRows → 1024x240 YIQ float
  ↓ Crt_Render (Parallel.For scanline)
    - scanline 權重
    - horizontal blur (RF/SVideo)
    - gamma + brightness boost
    - mask（aperture grille / shadow mask）
    - phosphor decay（frame blend with _prevFrame，已實作）
    - convergence
    - curvature（barrel/pincushion LUT）
  ↓ uint* ARGB (up to 1024x840)
送 Avalonia zero-copy path
```

所有參數（`PhosphorDecay`、`ScanlineStrength`、`Gamma`、`Brightness`、`Convergence`、`Curvature`、`MaskType`、`AnalogOutput`、`AnalogSize`）都要能映射成 SkSL uniforms。

---

## 4. 分階段 GPU Pipeline

### 總體策略：**分階段推進**

```
v1 (MVP)              v2 (多 Pass)            v3 (NTSC)
============          ==============          ==============
[NES 256x240]         [NES 256x240]           [NES 256x240]
    ↓                     ↓                        ↓
 crt_core.sksl        hblur.sksl              ntsc_modulate.sksl
(單 pass，含           (separable gauss)       (composite 訊號)
 phosphor)               ↓                        ↓
    ↓                crt_core.sksl            ntsc_demod.sksl
 [畫面]                   ↓                    (YIQ→RGB)
                     [畫面]                        ↓
                                             hblur.sksl
                                                  ↓
                                             crt_core.sksl
                                                  ↓
                                              [畫面]
```

### v1 MVP 必備功能
- scanline 調變
- mask（aperture grille / shadow mask）
- gamma + brightness
- convergence（RGB sub-pixel 位移）
- barrel curvature
- **phosphor decay**（ping-pong `SKSurface`）

### v1 不做
- horizontal blur（v2）
- NTSC 合成（v3）
- 多 pass（v2 起）

---

## 5. 檔案式 Shader 載入機制

### 目錄結構
```
AprNesAvalonia/
  Shaders/
    crt_core_v1.sksl      # v1 MVP 單 pass
    hblur.sksl            # v2
    ntsc_modulate.sksl    # v3
    ntsc_demod.sksl       # v3
    shadow_mask_lut.sksl  # v2 可選（shadow mask 查表）
```

### MSBuild 整合
`AprNesAvalonia.csproj` 加：
```xml
<ItemGroup>
  <None Include="Shaders\**\*.sksl">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

執行期路徑：`{AppContext.BaseDirectory}/Shaders/crt_core_v1.sksl`

### `ShaderLoader` 類別
```csharp
static class ShaderLoader {
    static readonly Dictionary<string, SKRuntimeEffect> _cache = new();
    static readonly string _shaderDir = Path.Combine(AppContext.BaseDirectory, "Shaders");

    public static SKRuntimeEffect Load(string fileName) {
        if (_cache.TryGetValue(fileName, out var eff)) return eff;
        string path = Path.Combine(_shaderDir, fileName);
        string src = File.ReadAllText(path);
        eff = SKRuntimeEffect.CreateShader(src, out string errors);
        if (eff == null)
            throw new InvalidOperationException($"Shader compile failed [{fileName}]: {errors}");
        _cache[fileName] = eff;
        return eff;
    }

    public static void Reset() {
        foreach (var e in _cache.Values) e.Dispose();
        _cache.Clear();
    }
}
```

### Hot reload（開發時期，stretch goal）
`FileSystemWatcher` 監聽 `Shaders/*.sksl`，檔案變更呼叫 `ShaderLoader.Reset()`，下一次 Render 重編。Release build 關閉。

---

## 6. 動態派發機制（核心新設計）

### 動機
不同硬體、不同使用情境下，最適策略不同：
- 老 CPU + 好 GPU → Gpu
- 新 CPU 無 GPU 或虛擬機 → Simd
- 極度相容性需求（.NET Framework、headless deterministic） → Scalar

不只 CRT core，未來 NTSC encode、horizontal blur、phosphor decay 都有可能各自選不同實作。所以派發機制要設計成 **per-stage 可獨立挑選**。

### 核心抽象
```csharp
public enum PipelineStrategy { Scalar, Simd, Gpu }

public interface ICrtStage : IDisposable {
    PipelineStrategy Strategy { get; }
    bool IsAvailable();                  // 硬體 / 函式庫 capability check
    void Execute(CrtStageContext ctx);
}

public class CrtStageContext {
    public IntPtr InputPtr;              // NES/前段輸出
    public int InputW, InputH;
    public IntPtr OutputPtr;             // CPU 路徑輸出
    public SKSurface? GpuTarget;         // GPU 路徑輸出
    public SKRect DstRect;
    public CrtParams Params;             // uniforms 打包（strength、gamma...）
    public FrameState Frame;             // _prevFrame / _prevSurface 等
}
```

### 派發器
```csharp
public class CrtPipeline : IDisposable {
    ICrtStage _crtCore;

    public void Configure(CrtConfig cfg) {
        _crtCore = TryCreate(cfg.CoreStrategy, cfg)
                ?? TryCreate(PipelineStrategy.Simd, cfg)   // fallback #1
                ?? TryCreate(PipelineStrategy.Scalar, cfg); // fallback #2
    }

    static ICrtStage? TryCreate(PipelineStrategy s, CrtConfig cfg) {
        try {
            ICrtStage stage = s switch {
                PipelineStrategy.Gpu    => new CrtCoreStage_Gpu(cfg),
                PipelineStrategy.Simd   => new CrtCoreStage_Simd(cfg),
                PipelineStrategy.Scalar => new CrtCoreStage_Scalar(cfg),
                _ => throw new ArgumentException(),
            };
            if (!stage.IsAvailable()) { stage.Dispose(); return null; }
            return stage;
        } catch (Exception ex) {
            Log($"[CrtPipeline] {s} init failed: {ex.Message}");
            return null;
        }
    }
}
```

### Capability Detection
`CrtCoreStage_Gpu.IsAvailable()` 檢查：
1. SkiaSharp 版本 ≥ 2.88 （已預設）
2. `SKRuntimeEffect.CreateShader` 能編出最簡 shader（執行期 smoke test）
3. Shaders 目錄存在且至少有一個 `.sksl`

`CrtCoreStage_Simd.IsAvailable()` 檢查：
- `Avx2.IsSupported` 或 `Vector.IsHardwareAccelerated && Vector<float>.Count >= 8`

`CrtCoreStage_Scalar.IsAvailable()` 恆 true。

### Config 設計
`AprNes.ini` 新增：
```ini
[CRT]
CoreStrategy=Auto            ; Auto | Scalar | Simd | Gpu
AutoPolicy=PreferGpu         ; PreferGpu | PreferSimd | PreferScalar
; per-stage override（可選，未來擴充）
; BlurStrategy=Gpu
; PhosphorStrategy=Gpu
```

`Auto` 模式展開規則（由 `PolicyResolver`）：
| AutoPolicy | 選擇順序 |
|------------|---------|
| PreferGpu（預設）| Gpu → Simd → Scalar |
| PreferSimd | Simd → Gpu → Scalar |
| PreferScalar | Scalar（僅相容性測試用）|

UI：`AnalogConfigWindow` 新增下拉選單「CRT 實作：Auto / Scalar / SIMD / GPU」+ 小字說明當前實際選到的策略。

### 執行期策略切換
- Config 存檔後 → `CrtPipeline.Configure()` 重新執行（丟棄舊 stage，重建新的）
- 避免 mid-frame 切換；在下一幀 begin 之前套用

---

## 7. Phosphor Decay（v1 必備）

### CPU 版現況
`CrtScreen.cs:63` 宣告 `PhosphorDecay = 0.15f`，`_prevFrame` uint\* buffer；`SWAR` 實作於 `ProcessRowPhosphor_SWAR`。SIMD 版等同設計（`CrtScreen.Simd.cs:87/117`）。

### GPU 版設計
- `_prevSurface` 持有 `SKSurface`（尺寸同輸出）
- 每幀 Render 序：
  1. Begin frame → target canvas
  2. Fragment shader 讀 `uPrev`（前幀 surface snapshot）+ `uScreen`（NES 原始）
  3. 輸出 blend 結果到 canvas
  4. `canvas.Snapshot()` 結果 → 複製到 `_prevSurface`（下幀 `uPrev`）
- Resize / strategy 切換時 → 重建 `_prevSurface` 並清零（與 CPU 版 `_prevFrameValid = false` 同語義）

### 首幀處理
`_prevSurface` 為全黑 → phosphor blend 結果 = 當幀 × (1 − decay)，與 CPU 版 `!_prevFrameValid` 首幀行為一致。

---

## 8. Headless GPU 支援

### 為什麼可行
Skia 的 `SKRuntimeEffect` 在 **有 GPU context 時走 GPU，無 GPU context 時 fallback 到 CPU rasterizer**。TestRunner / 無頭模式沒有視窗，Skia 仍可 create `SKSurface` on CPU backend，shader 於 CPU 執行 — 結果 deterministic。

### 預期副作用
- Headless 下 GPU 路徑不加速（同 CPU runtime effect 性能，約與純量相近）
- 但仍可做 **shader 正確性驗證**：CI 截圖比對 SIMD 與 GPU 的像素差
- 真正的 GPU 加速在 headless 要用 off-screen GPU context（v2 目標）

### TestRunner 對應

#### CLI 入口（新增）
為讓無頭模式能精確指定策略（benchmark、正確性比對），新增兩個 CLI flag：

```
--crt-strategy=<auto|scalar|simd|gpu>   指定主策略；預設 auto（讀 ini）
--crt-force                              關閉 fallback：指定策略若不可用則 abort
--crt-policy=<gpu|simd|scalar>           Auto mode 的偏好順序；預設讀 ini
```

範例：
```bash
# 明確指定 GPU，capability 不足自動 fallback 到 SIMD
AprNes.exe --rom test.nes --crt-strategy=gpu --wait-result

# 強制 GPU（benchmark 比對用；無 GPU 就直接失敗）
AprNes.exe --rom test.nes --crt-strategy=gpu --crt-force

# Auto mode 但偏好 SIMD（避免測到 GPU 差異）
AprNes.exe --rom test.nes --crt-strategy=auto --crt-policy=simd
```

#### 解析流程
```csharp
// TestRunner / Main 啟動時
var cli = CommandLineArgs.Parse(args);
CrtConfig cfg = CrtConfig.LoadFromIni();          // 先讀 ini
cli.OverlayOnto(cfg);                              // CLI 覆寫 ini
if (cli.ForceMode) cfg.AllowFallback = false;

CrtPipeline pipeline = new();
try { pipeline.Configure(cfg); }
catch (CrtStrategyUnavailableException ex) {
    if (!cfg.AllowFallback) {
        Console.Error.WriteLine($"[CRT] force strategy {ex.Requested} unavailable; abort");
        Environment.Exit(2);
    }
    // 回到 fallback chain
}
```

#### 三軸矩陣（策略 × 環境 × force）

| strategy | 有 GPU | 無 GPU / 無 AVX2 | force 行為 |
|----------|:-----:|:-----------------:|-----------|
| `gpu`    | ✅ 走 GPU | fallback → Simd / Scalar | force 時 abort |
| `simd`   | 走 SIMD | 有 AVX2 走 SIMD，否則 fallback Scalar | force 時若無 AVX2 abort |
| `scalar` | 走 Scalar | 走 Scalar | 恆可用 |
| `auto`   | 依 policy 決定 | 依 policy 決定 | force 無意義（auto 本身含 fallback） |

#### Headless 底層行為
- 有視窗 + GPU：Skia 走 GPU 加速
- 有視窗 無 GPU：Skia 走 CPU rasterizer（正確但無加速）
- 無視窗（TestRunner）：Skia `SKSurface.Create()` 於 raster backend → CPU rasterizer
- 三者語義相同，僅效能不同；正確性 bit-exact 於同版 Skia

#### 典型使用場景
| 場景 | 指令 |
|------|------|
| Blargg 測試（預設，跟 MEMORY.md 基準同） | `AprNes.exe --rom X.nes --wait-result` （ini 預設 auto）|
| GPU 正確性 CI | `--crt-strategy=gpu --crt-force` |
| SIMD 基準複現 | `--crt-strategy=simd --crt-force` |
| 跨 strategy 截圖比對 | 分別跑 `scalar` / `simd` / `gpu` 三次，diff |

不再強制 TestRunner 走某固定策略；讓 strategy 自然 resolve 且可 CLI override。

### Determinism 策略
headless Skia CPU rasterizer 在同一版本 Skia + 同平台下 bit-exact；跨平台容差 ±1/255。比對時採容差 diff 而非 exact match。

---

## 9. 與 Avalonia 整合的具體作法

### 現況
`AprNesAvalonia/Views/EmuScreenControl.cs`：
- 用 `ISkiaSharpApiLeaseFeature` → `SKCanvas`
- `SKBitmap.InstallPixels` 零拷貝 NES 畫面
- `canvas.DrawBitmap` + `SKFilterQuality.Low`

### 重構
1. `IEmuRenderer` 介面：
   ```csharp
   interface IEmuRenderer : IDisposable {
       void Render(SKCanvas canvas, IntPtr framePtr, int w, int h, SKRect dst);
   }
   ```
2. 兩種實作：
   - `BitmapBlitRenderer` ← 現狀（非 analog 模式）
   - `CrtRenderer` ← analog 模式（內部包 `CrtPipeline`，動態派發 CPU/GPU）
3. `EmuScreenControl` 依 `CrtMode (off/on)` 動態選 renderer
4. Resize 偵測 → 通知 `CrtPipeline.Resize()` → 重建 GPU surfaces

### Shader 打包驗證
Build 完成後，`bin/Debug/net10.0/Shaders/crt_core_v1.sksl` 應存在並可讀。

---

## 10. 實作步驟（里程碑）

### M0 — 基礎骨架（0.5 天）
- [ ] 新增 `AprNesAvalonia/Shaders/` 目錄 + `crt_core_v1.sksl` 空檔
- [ ] `.csproj` 加 `<None>` + `CopyToOutputDirectory=PreserveNewest`
- [ ] `ShaderLoader` 類別（含快取、錯誤訊息）
- [ ] `IEmuRenderer` + `BitmapBlitRenderer` 介面重構
- [ ] `ICrtStage` / `CrtPipeline` / `PipelineStrategy` 列舉
- [ ] Config 讀寫 `CoreStrategy` + `AutoPolicy` + `AllowFallback`
- [ ] CLI flag 解析：`--crt-strategy`、`--crt-force`、`--crt-policy`（AprNes.exe 與 AprNesAvalonia.exe 皆支援）
- [ ] `AnalogConfigWindow` 下拉選單 + 顯示實際生效策略

### M1 — GPU 最小可驗證 shader（1 天）
- [ ] `crt_core_v1.sksl` 內容：僅做 UV 取樣 + gamma（其他關閉）
- [ ] `CrtCoreStage_Gpu`：建 effect、bind `uScreen` child、執行單 pass
- [ ] `CrtCoreStage_Simd` 包 `Crt_Render` 方法（橋接現有 SIMD code）
- [ ] `CrtCoreStage_Scalar` 包 `CrtScreen.cs` 的 `Crt_Render`
- [ ] 三條路畫面肉眼等效（差異在 gamma 誤差容忍內）

### M2 — 派發器與 fallback（0.5 天）
- [ ] `PolicyResolver`：Auto → 依 capability 決定
- [ ] `CrtPipeline.Configure` fallback 鏈完整（Gpu 失敗自動退 Simd）
- [ ] Log strategy 選擇結果到 console
- [ ] 手動強制 config 指定 Gpu，若硬體不支援 → 自動降級不當機

### M3 — CRT 核心 shader 完整化（1-2 天）
- [ ] `crt_core_v1.sksl` 加：scanline、mask (aperture grille)、brightness、convergence、barrel curvature
- [ ] Uniform 全數連到 `CrtParams`
- [ ] 與 Simd 輸出逐像素比對（容差 ±2/255）
- [ ] 所有 `AnalogOutput` 模式 (AV/SVideo/RF) 參數映射正確

### M4 — GPU Phosphor Decay（1 天）
- [ ] `_prevSurface` ping-pong
- [ ] `uPrev` child shader 連線
- [ ] 首幀黑初始
- [ ] Resize / config 變 → `_prevSurface` 重建
- [ ] 視覺驗證拖影與 Simd 一致

### M5 — Shadow Mask + 細節（0.5 天）
- [ ] Shadow mask 2D 圖案（非 aperture grille）
- [ ] 用 hardcode pattern 或外部 8x8 SKBitmap LUT

### M6 — Headless GPU 驗證（0.5 天）
- [ ] TestRunner 強制啟 GPU strategy 跑一輪 blargg
- [ ] 比對截圖與 SIMD 版本（容差）
- [ ] CI friendly：失敗輸出 diff 圖

### M7 — Benchmark & 文件（0.5 天）
- [ ] Benchmark 2x/4x/6x/8x × {Scalar, SIMD, GPU}
- [ ] `MD/PerformanceWithAV/CRT_GPU_Benchmark_YYYY-MM-DD.md`
- [ ] 更新 `README.md`

### v2（多 Pass）預計
- M8 — `hblur.sksl`（separable Gaussian，中繼 SKSurface）— 1 天
- M9 — `CrtMultiPassOrchestrator`（串 Pass 1 → Pass 2）— 0.5 天
- M10 — AV/SVideo/RF blur 差異化 — 0.5 天

### v3（NTSC，stretch）預計
- M11 — `ntsc_modulate.sksl`（composite 訊號）— 1 天
- M12 — `ntsc_demod.sksl`（相位解調）— 1 天
- M13 — `UltraAnalog` 模式走 GPU path — 0.5 天

---

## 11. 風險與對策

| 風險 | 機率 | 對策 |
|------|:----:|------|
| `SKRuntimeEffect.CreateShader` 在某 Skia 版本爆錯 | 低 | `CrtCoreStage_Gpu.IsAvailable()` smoke test；失敗自動 fallback |
| `SKBitmap.InstallPixels` 做 child shader 在某些 GPU 驅動失敗 | 中 | 備案：每幀拷到 `SKImage`（256×240 微成本）|
| Avalonia Render Thread 的 GPU context 在 resize 中斷裂 | 中 | 監聽 `Bounds` 變更 → 下一幀重建 `_prevSurface` |
| Shader 檔案漏複製到 output | 中 | MSBuild `PreserveNewest`；`ShaderLoader` 檢查 File.Exists 並明確報錯 |
| 不同 GPU 驅動 SkSL 精度差異 → 截圖不 bit-exact | 低 | 接受容差 ±2/255；測試比對用容差 diff |
| Phosphor ping-pong 在 strategy 中途切換資料遺失 | 低 | 切換時 reset `_prevSurface`（同 CPU 版 `_prevFrameValid=false` 行為）|
| Headless Skia 無 GPU context 但 config 指定 Gpu | 已設計處理 | Runtime effect 自動走 CPU rasterizer；功能正確但無加速 |
| Hot reload 在執行中 race condition | 低 | 僅 Debug build 啟用；`lock(_cache)` 保護 |

---

## 12. 效能目標

**基準**（現有 SIMD，MEMORY.md 紀錄）：
- Analog 4x no DSP：109.59 FPS
- Analog 6x no DSP：82.38 FPS
- Analog 8x no DSP：79.03 FPS
- Analog 4x DSP Mode 2：82.65 FPS
- Analog 8x DSP Mode 2：64.49 FPS

**GPU 目標**（保守 2×，挑戰 3×）：
| 模式 | SIMD | GPU 保守 | GPU 挑戰 |
|------|:----:|:--------:|:--------:|
| 4x no DSP | 109.59 | ≥ 220 | ≥ 330 |
| 6x no DSP | 82.38 | ≥ 165 | ≥ 250 |
| 8x no DSP | 79.03 | ≥ 160 | ≥ 240 |
| 4x DSP Mode 2 | 82.65 | ≥ 165 | ≥ 250 |
| 8x DSP Mode 2 | 64.49 | ≥ 130 | ≥ 195 |

若 GPU < 60% SIMD 基準 → 視為不達標，暫緩。

---

## 13. 驗收清單

v1 完成必須通過：
- [ ] 所有三個 strategy（Scalar / Simd / Gpu）都能獨立跑起來不當機
- [ ] `Auto` mode 在 AVX2 + Skia GPU 環境 → 選到 `Gpu`
- [ ] `Auto` mode 在僅 AVX2 環境 → 選到 `Simd`
- [ ] `Auto` mode 在純量環境 → 選到 `Scalar`
- [ ] CLI `--crt-strategy=gpu/simd/scalar/auto` 可從命令列覆寫 ini
- [ ] CLI `--crt-force` 時 capability 不足明確 abort (exit code 2)
- [ ] GPU 視覺與 SIMD 差異 ≤ ±2/255 per channel
- [ ] Phosphor decay 在 GPU 運作且 resize 後不 crash
- [ ] TestRunner headless 模式走 GPU 不 crash（效能可接受）
- [ ] Shader 檔案缺失時清楚報錯而非靜默 fallback
- [ ] Config UI 切換即時生效（下一幀套用）

---

## 14. 分階段執行計畫（新增）

將工作拆成 4 個 Phase，使用者可在鎖頻環境下先取得 scalar/SIMD baseline，避免被大重構擋住。

### Phase 0 — 立即可 benchmark（最低風險，0.5 天）
**目標**：AprNesAvalonia 能在同一個 build 裡切換 Scalar / SIMD，取 baseline 對比。

**作法**：**MSBuild property 切換**（不改 code，只改 csproj）
```xml
<PropertyGroup>
  <CrtImpl Condition="'$(CrtImpl)' == ''">Simd</CrtImpl>
</PropertyGroup>

<Compile Condition="'$(CrtImpl)' == 'Simd'"
         Include="../AprNes/NesCore/**/*.cs"
         Exclude="../AprNes/NesCore/NTSC_CRT/CrtScreen.cs" />
<Compile Condition="'$(CrtImpl)' == 'Scalar'"
         Include="../AprNes/NesCore/**/*.cs"
         Exclude="../AprNes/NesCore/NTSC_CRT/CrtScreen.Simd.cs" />
```

使用：
```bash
dotnet build AprNesAvalonia/AprNesAvalonia.csproj -c Debug -p:CrtImpl=Simd
# benchmark → 存結果
dotnet build AprNesAvalonia/AprNesAvalonia.csproj -c Debug -p:CrtImpl=Scalar
# benchmark → 存結果
```

**優點**：不動 code、0 風險、立即可用；scalar & SIMD 都能跑完整 test 套件
**缺點**：需要重編才能切；不是 runtime dispatch

**驗收**：
- [x] `-p:CrtImpl=Simd` 編出的 exe 使用 SIMD CrtScreen
- [x] `-p:CrtImpl=Scalar` 編出的 exe 使用 scalar CrtScreen
- [x] 兩個 build 都能 headless 跑 `--analog --ultra-analog --analog-size 8 --analog-output RF --audio-dsp --audio-mode 2 --benchmark 30`

### Phase 1 — Runtime dispatch 重構（Scalar/SIMD）（2-3 天）
**目標**：同一 build 內 runtime 切換 Scalar / SIMD；CLI / ini 可指定。

**架構**：
1. 新增 `CrtScreen.Shared.cs`（partial class `NesCore`）：
   - 所有公開 config 欄位：`VignetteStrength`、`PhosphorDecay`、`ShadowMaskMode` 等
   - 所有解耦參數：`crt_analogOutput`、`crt_analogSize`、`crt_analogScreenBuf`、`crt_frameCount`
   - 顯示尺寸：`Crt_SrcW/H`、`Crt_DstW/H`、`_fullscreenW/H`
   - 公開 API：`Crt_Init`、`Crt_Render`、`Crt_ApplyConfig`、`Crt_UpdateScreenBuf` 等
   - Backend enum + `Crt_SetBackend` / `Crt_GetBackend`
2. 重構 `CrtScreen.cs`（改名為 `CrtScreen.Scalar.cs`）：
   - `partial class NesCore` → `internal static class CrtScreenScalar`
   - 移除 shared 欄位（已在 Shared）
   - `Init/Render/ApplyProfile` 內部化
   - 讀 shared 欄位透過 `NesCore.xxx` 限定
3. 重構 `CrtScreen.Simd.cs` → `internal static class CrtScreenSimd`（同上）
4. Shared 的 dispatch 以 `#if CRT_SIMD_AVAILABLE` 條件編譯：
   ```csharp
   public static void Crt_Render() {
   #if CRT_SIMD_AVAILABLE
       if (_crtBackend == CrtBackend.Simd) { CrtScreenSimd.Render(); return; }
   #endif
       CrtScreenScalar.Render();
   }
   ```
5. `AprNesAvalonia.csproj` 加 `<DefineConstants>CRT_SIMD_AVAILABLE</DefineConstants>`
6. `AprNes.csproj` 不加（仍只走 scalar）

**驗收**：
- [ ] AprNes 照常跑（只 scalar）
- [ ] AprNesAvalonia 預設 SIMD，`--crt-strategy=scalar` 可 runtime 切
- [ ] blargg 184/184 PASS 雙路徑都過
- [ ] 截圖像素差 ≤ ±2/255

### Phase 2 — GPU backend 加入（依 §6-§13，4-6 天）
見前述 M0-M7 里程碑。Phase 1 完成後，第三個 backend 塞進 dispatch 即可。

### Phase 3 — ARM NEON 專屬 backend（stretch，2-3 天）
僅在 `RuntimeInformation.ProcessArchitecture == Arm64` 時啟用；用 `System.Runtime.Intrinsics.Arm.AdvSimd`。

---

## 15. NTSC GPU 適配性分析（基於 Ntsc.cs survey）

Phase 0 baseline 顯示：只加速 CRT 頂多 1.05x（阿姆達爾定律）。要有意義加速必須把 NTSC 的一部分也搬到 GPU。但 NTSC 是 **時序密集的訊號處理**，並非所有 method 都適合 GPU。以下為逐 method 分類。

### 15.1 NTSC 總體瓶頸結構

| 模式 | CPU 熱點 | GPU 潛力 |
|------|---------|:-------:|
| Analog（非 UltraAnalog）| 調色盤 LUT、demod、YIQ→RGB | **高**（~80% 可搬）|
| UltraAnalog Fast | 加上 IIR chroma filter | 中（可 refactor 為 FIR）|
| UltraAnalog Physical (RF/SVideo) | 加上 slew-rate 波形模擬（序列 IIR）| 低（根本性 CPU-bound）|

### 15.2 Method 分類表

#### CPU-ONLY（序列狀態 / PPU 驅動，不能搬 GPU）

| Method | 行 | 不能搬原因 |
|--------|:-:|-----------|
| `Ntsc_CaptureScanline` | 380 | PPU 於 cx==260 逐 scanline 呼叫；寫 `scanPhase6`/`scanPhaseBase` 序列相位狀態。Race 會破壞色相 |
| `DecodeScanline_Fast`（legacy）| 426 | 更新共用 phase 狀態（已被 `Ntsc_FlushPendingRows` 取代，列入只為記錄）|
| `DecodeScanline_Physical`（legacy）| 530 | 同上 |
| `RunWaveformLoop` | 601 | 4-sample 鬆弛率濾波 IIR：`vPrev`、`vVel` 前後依賴；第 N 取樣輸出是 N+1 輸入 |
| `RunWaveformLoop_SVideo` | 682 | 同上，S-Video 變體的 slew chain |

**關鍵**：UltraAnalog + RF/SVideo 的 **physical waveform simulation** 核心就在 `RunWaveformLoop*` 裡。這是 **RF 雜訊、buzz、slew-limiting** 等類比特徵的真正來源，不能搬 GPU。

#### GPU-MAYBE（需 refactor 掉 IIR 才能搬）

| Method | 行 | Refactor 方向 |
|--------|:-:|--------------|
| `RunDecodeLoop` | 491 | IIR chroma filter `iF += ChromaBlur * (chroma * c - iF)`；改為 2-3 tap 分離式 FIR（Hamming 窗），畫質損失 <1% |
| `DecodeAV_SVideo` | 514 | 同上，較簡單的 chroma path |
| `Ntsc_FlushPendingRows` | 400 | 主派發者，本身無狀態；但 worker 內部才是重點 |

#### GPU-OK（純無狀態計算，可直搬 SkSL）

| Method | 行 | 功能 |
|--------|:-:|------|
| `Ntsc_Init` / `UpdateColorTemp` / `UpdateGammaLUT` | 131/230/244 | LUT 預計算；上傳為 texture/uniform |
| `GenerateSignal` | 444 | 調色盤 → YIQ LUT 查表 |
| `DecodeScanline_Fast_Worker` | 437 | 無狀態 dispatcher（phase0 param）|
| `DecodeScanline_Physical_Worker` | 541 | 無狀態 dispatcher + RF jitter（確定性）|
| `DemodulateRow_Core` | 732 | YIQ→RGB + 6-tap Hann 窗，標準 separable filter |
| `YiqToRgb` | 857 | 矩陣乘 + gamma LUT，per-pixel |
| `ResampleH_Bilinear` | 295 | 水平 bilinear 重取樣 |
| `VerticalFillRows` | 313 | 垂直 bilinear 插值 |

### 15.3 天然的 CPU/GPU 切分點

```
PPU ---cx==260---> Ntsc_CaptureScanline (CPU-ONLY)
                      ↓ 寫入 palBuf + phase 狀態
              Ntsc_FlushPendingRows (CPU orchestrator)
                      ↓ Parallel.For 240 rows
        ┌─────────────┴─────────────┐
        │ Fast path                 │ Physical path
        │   GenerateSignal (OK)     │   RunWaveformLoop (CPU-ONLY)
        │   RunDecodeLoop (MAYBE)   │     ↓
        │   DecodeAV_SVideo (MAYBE) │   DemodulateRow_Core (OK)
        └─────────────┬─────────────┘
                      ↓ linearBuffer[Y/I/Q]  ← 天然 CPU/GPU 邊界！
                   CRT stage
                      ↓
                 AnalogScreenBuf
```

### 15.4 Phase 2 修正策略

原計畫「只搬 CRT」→ 1.05x 速度上限。修正為**依照類比模式切分**：

| 模式 | GPU 策略 | 預期加速 |
|------|----------|:-------:|
| Analog (non-Ultra) | 全 GPU：GenerateSignal + RunDecodeLoop→FIR + DemodulateRow + YiqToRgb + CRT | **1.8-2.5x** |
| UltraAnalog Fast | GPU：除 IIR 外全部；IIR refactor 為 FIR | **1.5-2x** |
| UltraAnalog Physical (RF/SVideo) | **混合**：slew loop 留 CPU，demod + CRT 去 GPU | **1.2-1.3x** |

### 15.5 Phase 2 工作項（取代原 M11-M13）

- [ ] M11 — 定義 `INtscBackend` 抽象（Scalar / Simd / Gpu）
- [ ] M12 — CPU 端 `linearBuffer` → GPU texture 上傳（per-frame，`SKImage.FromPixels`）
- [ ] M13 — Fast path shader：`ntsc_fast.sksl`（palette LUT + FIR chroma + demod）
- [ ] M14 — Physical path：**只做 demod + YiqToRgb 去 GPU**，slew loop 留 CPU 寫 `linearBuffer`
- [ ] M15 — `crt_core.sksl` 接在 NTSC GPU 輸出之後，免來回 CPU
- [ ] M16 — 三模式加速量測，對比 Phase 0 baseline

### 15.6 風險與取捨

| 風險 | 對策 |
|------|------|
| IIR → FIR 視覺差異 | 先做 A/B 截圖比對；差異 ≤ ±2/255 才採用 |
| Physical mode CPU↔GPU 來回拷貝成本 | `linearBuffer` 以 `SKBitmap.InstallPixels` 零拷貝上傳；Physical mode 上限可能只有 1.2x，用戶該接受 |
| CPU-only slew loop 在 ARM 效能 | `RunWaveformLoop*` 用 `Vector<T>`，NEON 自動加速，短期不做 NEON 專屬 |
| Shader LUT 容量限制 | palette LUT 64×8 = 512 entries × 3 floats = 6KB，遠低於任何 GPU 限制 |

### 15.7 建議優先序

1. **先做 Analog (non-Ultra) 的 GPU path** — 最乾淨、加速比最高、技術風險最低
2. UltraAnalog Fast 接著做 — 只需要 IIR→FIR 重寫
3. UltraAnalog Physical 最後做 — 需要設計 CPU↔GPU 混合 pipeline，最複雜，加速比最低
4. **不做**：`RunWaveformLoop*` 的 GPU 化（本質不適合）

---

## 14. 參考資料

- [Avalonia Custom Skia Rendering](https://docs.avaloniaui.net/docs/guides/graphics-and-animation/custom-drawing-operation)
- [SkiaSharp SKRuntimeEffect API](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skruntimeeffect)
- [SkSL Spec](https://skia.org/docs/user/sksl/)
- 本專案：`CrtScreen.cs`、`CrtScreen.Simd.cs`、`Views/EmuScreenControl.cs`
- 同家族前例：RetroArch CRT-Royale GLSL、Mesen2 CRT shader
