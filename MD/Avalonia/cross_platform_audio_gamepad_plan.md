# AprNesAvalonia 跨平台 Audio + Gamepad 實作計畫

> 撰寫日期：2026-04-30
> 目標平台：Linux x86_64、Linux ARM64、macOS ARM64
> 目標分支：（待定，建議 `feature/cross-platform-backends`）

## 總目標

把 AprNesAvalonia 從 Windows-only 擴展到 **Windows + Linux + macOS**，核心子系統照下面分工：

| 子系統 | Windows | Linux x64/ARM64 | macOS ARM64 |
|---|---|---|---|
| Audio | 既有 `Win32WaveOutBackend` | `Hexa.NET.MiniAudio` | `Hexa.NET.MiniAudio` |
| Gamepad | 既有 `Win32GamepadBackend` (DirectInput8 + XInput) | `Hexa.NET.SDL3` | `Hexa.NET.SDL3` |
| Window / Render | Avalonia + SkiaSharp（已跨平台） | 同 | 同 |

CRT shader 走 SkSL，理論上 Avalonia 在 Linux 走 OpenGL、macOS 走 Metal 都能跑（Skia 自動處理 GR backend）。GPU CRT 路徑在三平台應該都能 work，但需要實測。

## 前置確認

讀完這份計畫前先確認三件事：

- ✅ 已決定 audio backend：Hexa.NET.MiniAudio（見 [memory: project_aprnesava_audio_backend.md](../../MEMORY.md)）
- ✅ 已決定 gamepad backend：Hexa.NET.SDL3（見 [memory: project_aprnesava_gamepad_backend.md](../../MEMORY.md)）
- ⚠️ macOS ARM64 驗證需要實體 Mac 或 GitHub Actions runner（Windows 開發機沒辦法）

## 預估時程

| 階段 | 內容 | 預估時間 |
|---|---|---|
| Phase 0 | 環境 + csproj 設置 | 0.5 天 |
| Phase 1 | Audio backend 實作 + Linux 驗證 | 1-2 天 |
| Phase 2 | Gamepad backend 實作 + Linux 驗證 | 1-2 天 |
| Phase 3 | Linux x64 完整整合測試 | 0.5 天 |
| Phase 4 | Linux ARM64 驗證 | 0.5-1 天 |
| Phase 5 | macOS ARM64 驗證 | 1-2 天（看 CI 還是借 Mac） |
| Phase 6 | Release 打包 + 文件 | 0.5 天 |
| **總計** | | **5-9 天** |

---

## Phase 0：環境與專案設置

### 目標
讓專案在不破壞 Windows 既有功能的前提下，能夠 build / publish 到 Linux x64、Linux ARM64、macOS ARM64 三個 RID。

### 步驟

1. **加入 NuGet 套件**（`AprNesAvalonia/AprNesAvalonia.csproj`）：
   ```xml
   <ItemGroup>
     <PackageReference Include="Hexa.NET.MiniAudio" Version="*" />
     <PackageReference Include="Hexa.NET.SDL3"      Version="*" />
   </ItemGroup>
   ```

2. **設定 RuntimeIdentifiers**（同 csproj）：
   ```xml
   <PropertyGroup>
     <RuntimeIdentifiers>win-x64;linux-x64;linux-arm64;osx-arm64</RuntimeIdentifiers>
   </PropertyGroup>
   ```

3. **驗證 Windows build 不退**：
   ```bash
   dotnet build AprNesAvalonia/AprNesAvalonia.csproj -c Release
   AprNesAvalonia/bin/Release/net10.0/AprNesAvalonia.exe   # 開個 ROM 跑 30 秒
   ```
   音訊 / 手把都應該維持原樣（PlatformFactory 還沒改）。

4. **驗證 publish 三個 Linux/macOS RID 都能 build**（不需執行）：
   ```bash
   dotnet publish AprNesAvalonia/AprNesAvalonia.csproj -c Release -r linux-x64   --self-contained true -o publish/linux-x64
   dotnet publish AprNesAvalonia/AprNesAvalonia.csproj -c Release -r linux-arm64 --self-contained true -o publish/linux-arm64
   dotnet publish AprNesAvalonia/AprNesAvalonia.csproj -c Release -r osx-arm64   --self-contained true -o publish/osx-arm64
   ```
   每個目錄裡確認：
   - 主執行檔存在
   - `runtimes/{rid}/native/` 內有 `libminiaudio.so/.dylib` 跟 `libSDL3.so.0/libSDL3.0.dylib`
   - 沒 build error / warning 暴增

### 驗收
- Windows Release build 跟原來一樣能跑能玩。
- 三個跨平台 publish 各自輸出完整目錄、含 native binaries。

### 文件影響
- `AprNesAvalonia/AprNesAvalonia.csproj`

---

## Phase 1：Audio Backend (MiniAudio)

### 目標
寫一個 `MiniAudioBackend : IAudioBackend`，在 Linux x64 上能正確輸出 NES 音訊。

### 步驟

1. **檢視 `IAudioBackend` 介面**（`AprNesAvalonia/Platform/IAudioBackend.cs`）：
   - 確認方法簽名：`Init(int sampleRate, int channels, int bufferFrames)`、`PushSamples(short[])` / `Start()` / `Stop()` / `Dispose()`
   - 跟 `Win32WaveOutBackend` 對照，確認 model（push-based 還是 callback-based？應該是 push-based）

2. **建立 `Platform/MiniAudioBackend.cs`**：
   - 用 `Hexa.NET.MiniAudio` 的 `ma_device_init` 開 playback device
   - 設定 `dataCallback` 為一個 unmanaged static method
   - 在 callback 內從 thread-safe ring buffer 拉 sample 出來填 output buffer
   - `PushSamples()` 把 NES emu thread 產生的 16-bit PCM 寫進 ring buffer
   - 預估：~150-250 行（含 P/Invoke struct definitions、ring buffer、生命週期管理）

3. **Ring buffer 設計**：
   - 容量：~ 4 frames worth of audio（4 × 1/60 秒 = 67 ms ≈ 2950 samples @ 44.1kHz mono）
   - 寫入：emu thread `PushSamples()` 直接 copy
   - 讀取：miniaudio callback thread 取 N 個 sample
   - 同步：lock-free ring buffer（`Interlocked` 操作 read/write index）—— 模擬器是單 producer 單 consumer，最適合 SPSC ring。

4. **修改 `Platform/PlatformFactory.cs`**：
   ```csharp
   public static IAudioBackend CreateAudio() {
       if (OperatingSystem.IsWindows()) return new Win32WaveOutBackend();
       return new MiniAudioBackend();
   }
   ```

5. **WSL2 Linux x64 驗證**：
   - 在 WSL2 Ubuntu 內：`apt install pulseaudio alsa-utils`（一般已有）
   - WSL2 要設 PulseAudio forwarding 到 Windows host（用 WSLg / pulseaudio-server）
   - 跑 `dotnet publish -r linux-x64 ...` 後在 WSL2 內執行
   - 開個有音樂的 NES ROM（例如 *Super Mario Bros.*）
   - **驗收：能聽到聲音、不破音、不爆音、延遲 < 100ms**

6. **如果 WSL2 audio forwarding 太麻煩**：
   - 改在 native Linux 機器或 VM 測（VirtualBox / Hyper-V Ubuntu desktop）
   - 或先做 unit test：寫一個 console 程式 `dotnet run` 直接吃 MiniAudioBackend 播 1kHz sin wave 5 秒，確認聲音能出來

### 驗收
- Linux x64 環境下 NES 音訊能正確輸出，沒有破音 / 延遲 / crash。
- Windows 版本不退（PlatformFactory 改完後 Windows 仍走 WaveOut path）。

### 文件影響
- `AprNesAvalonia/Platform/MiniAudioBackend.cs`（新增）
- `AprNesAvalonia/Platform/PlatformFactory.cs`（修改）
- `AprNesAvalonia/AprNesAvalonia.csproj`（package reference）

### 風險
- **MiniAudio default backend 在 Linux 上挑哪個？** 可能會優先 PulseAudio，落到 ALSA。如果系統只有 ALSA 沒 PulseAudio 要驗證仍能跑。
- **延遲**：miniaudio default buffer size 約 10-30ms，NES 60fps 一個 frame = 16.67ms。如果 buffer 比 frame 短會 underrun。需要實測調整。
- **Sample rate mismatch**：NES APU output 是 ~44.1 kHz，但部分音效卡 native rate 是 48 kHz —— miniaudio 內建 resampler 會自動處理，無需 emu 端調整。

---

## Phase 2：Gamepad Backend (SDL3)

### 目標
寫一個 `Sdl3GamepadBackend : IGamepadBackend`，Linux 上能讀到 USB 手把按鈕跟搖桿。

### 步驟

1. **檢視 `IGamepadBackend` 介面**：
   - 既有的 `Win32GamepadBackend` API：`GetState(int playerIdx)` / `GetConnectedCount()` 之類
   - 確認介面 model 是 polling 還是 event-driven（NES emu 通常 per-frame poll）

2. **建立 `Platform/Sdl3GamepadBackend.cs`**：
   ```csharp
   internal sealed unsafe class Sdl3GamepadBackend : IGamepadBackend, IDisposable
   {
       public void Init() {
           SDL.SDL_SetMainReady();
           SDL.SDL_Init(SDL_InitFlags.SDL_INIT_GAMEPAD);  // 只開 gamepad 子系統
       }

       public GamepadState GetState(int playerIdx) {
           // 跑 SDL_PumpEvents() pump
           // 找對應 playerIdx 的 SDL_Gamepad*
           // 用 SDL_GetGamepadButton(...) / SDL_GetGamepadAxis(...) 拉狀態
           // 映射到 NES 手把按鈕（A, B, Select, Start, Up/Down/Left/Right）
       }
   }
   ```
   - 預估 ~200-300 行（含 SDL event handling、controller mapping、hot-plug）

3. **Mapping 邏輯**：
   - NES 手把：A, B, Select, Start, ↑↓←→ —— 共 8 個按鈕
   - SDL gamepad standard mapping：
     - NES A → SDL `SDL_GAMEPAD_BUTTON_EAST`（Xbox A / PS X / Switch B）
     - NES B → SDL `SDL_GAMEPAD_BUTTON_SOUTH`（Xbox B / PS O / Switch A）
     - NES Select → `SDL_GAMEPAD_BUTTON_BACK`
     - NES Start → `SDL_GAMEPAD_BUTTON_START`
     - 方向 → `SDL_GAMEPAD_BUTTON_DPAD_*`（或左類比 deadzone 後當 D-pad）

4. **Hot-plug 處理**：
   - SDL 會發 `SDL_EVENT_GAMEPAD_ADDED` / `SDL_EVENT_GAMEPAD_REMOVED`
   - 在 `Init` 後維護一個「目前接著的 gamepad list」
   - 每 frame `SDL_PumpEvents()` 後處理插拔事件

5. **修改 `PlatformFactory.cs`**：
   ```csharp
   public static IGamepadBackend CreateGamepad() {
       if (OperatingSystem.IsWindows()) return new Win32GamepadBackend();
       return new Sdl3GamepadBackend();
   }
   ```

6. **WSL2 / Linux 驗證**：
   - WSL2 一般不能直接接 USB 手把（需 USB-IP forwarding，麻煩）。建議直接在 native Linux 機跑，或用 VM with USB passthrough。
   - 接一個 USB 手把（Xbox / PS / 8BitDo 任一），執行：
     ```bash
     ./AprNesAvalonia
     ```
   - 進設定畫面確認手把被偵測（顯示廠牌名稱）
   - 開 ROM 玩 30 秒，按所有方向鍵 + A/B/Select/Start，畫面動作正確

### 驗收
- Linux x64 上 USB 手把被正確偵測。
- 8 個按鈕全部映射對。
- 拔掉手把不 crash，重插能再認到。
- Windows 版仍用 Win32 backend（沒退）。

### 文件影響
- `AprNesAvalonia/Platform/Sdl3GamepadBackend.cs`（新增）
- `AprNesAvalonia/Platform/PlatformFactory.cs`（修改）
- `AprNesAvalonia/AprNesAvalonia.csproj`（package reference）

### 風險
- **SDL3 0.x 還在 active development**：API 可能跟之後版本有差異。挑 Hexa.NET.SDL3 對應 SDL3 stable release（3.4.x 已穩定）。
- **`SDL_PumpEvents()` 呼叫頻率**：太頻繁浪費 CPU，太少漏事件。在 emu 60Hz frame loop 內呼叫一次剛好。
- **Wayland vs X11**：現代 Linux 桌面有 Wayland 跟 X11 之分，SDL 都支援 —— 但 SDL3 的 input subsystem 不依賴顯示伺服器（它讀 evdev / libinput），所以這層不影響。

---

## Phase 3：Linux x64 完整整合測試

### 目標
在實機 Linux x64 環境完整驗證 audio + gamepad + 畫面 + 完整模擬器體驗。

### 步驟

1. **準備測試環境**：
   - 推薦：實體 Linux x64 機器（Ubuntu 24.04 LTS / Fedora 40）
   - 替代：VirtualBox / VMware / Hyper-V Ubuntu desktop VM with USB passthrough
   - 不推薦：純 WSL2（音訊跟手把 forwarding 都麻煩，不如實機）

2. **打包並複製到目標機器**：
   ```bash
   dotnet publish AprNesAvalonia/AprNesAvalonia.csproj -c Release -r linux-x64 \
     --self-contained true \
     -p:PublishSingleFile=true \
     -p:IncludeNativeLibrariesForSelfExtract=true \
     -o publish/AprNesAvalonia-linux-x64
   tar czvf AprNesAvalonia-linux-x64.tar.gz -C publish AprNesAvalonia-linux-x64
   # 把 tar.gz 複製到 Linux 機器
   ```

3. **測試清單**：
   | 測試項 | 預期 | 通過? |
   |---|---|---|
   | 解壓 + `chmod +x`，雙擊執行 | 主視窗出現 | |
   | 拖一個 NES ROM 進視窗 | 開機畫面 + 音樂出來 | |
   | 接 USB 手把，玩 30 秒 *Super Mario Bros.* | 流暢、按鈕全對、聲音穩 | |
   | Analog mode 開啟 | NTSC + CRT 畫面正常 | |
   | GPU CRT backend 切換 | 畫面切到 GPU shader 路徑（不 crash） | |
   | 視窗縮放、全螢幕切換 | 畫面跟手把保持運作 | |
   | 拔手把 → 重插 | 不 crash，重插能繼續玩 | |
   | 音量調整、音效靜音 | 即時生效 | |

### 驗收
- 上面所有測試項目通過。
- 60 FPS 穩定（看 status bar 或 benchmark）。
- 沒 crash log（檢查 `~/.local/share/AprNesAvalonia/` 或 stdout）。

### 風險
- **Avalonia 在 Linux 的 GPU 後端**：Windows 是 D3D11，Linux 是 OpenGL（或 Vulkan）。GPU CRT shader 是 SkSL，理論上 SkiaSharp 跨平台都支援，但實測前不能 100% 保證。
- **解析度 / DPI scaling**：Linux 桌面 DPI 處理跟 Windows 不同，視窗大小可能要 hint。
- **沒有 system tray**：之後可能會用，現階段先不管。

---

## Phase 4：Linux ARM64 驗證

### 目標
確認 ARM64 版本能在 Raspberry Pi 5 / Apple Silicon 跑 Linux 的場合也跑得起來。

### 步驟

1. **目標機器**（任選）：
   - **Raspberry Pi 5**（Ubuntu Server ARM64 / Ubuntu Desktop ARM64）
   - **Pine64 / Orange Pi 5** 等 ARM64 SBC
   - **Apple Silicon Mac 跑 Asahi Linux** 或 UTM ARM64 VM
   - **AWS Graviton / Azure ARM** 雲端機（需開 X forwarding 看畫面）

2. **打包並部署**：
   ```bash
   dotnet publish ... -r linux-arm64 ...
   ```

3. **執行測試清單**（同 Phase 3 縮小版）：
   - ROM 載入 → 畫面 + 音訊正常
   - 接 USB 手把（如可）→ 操作正常
   - 60 FPS 能維持？（SBC 可能掉到 30-50，要實測）

### 驗收
- ARM64 binary 能跑、不 crash。
- 至少能聽到音訊、看到畫面、有手把回應（FPS 不要求 60）。

### 風險
- **效能**：Raspberry Pi 5 大概能撐 60 FPS digital mode，analog + CRT 可能要降設定。
- **沒有 ARM64 機器**：如果手邊沒實機，可以用 QEMU 跑 ARM64 Ubuntu image（很慢但能驗證 binary 沒 fail）。
- **多媒體 codec / gpu driver**：ARM Linux 桌面成熟度比 x64 弱，Skia 的 GR backend 可能落到 software raster。

---

## Phase 5：macOS ARM64 驗證

### 目標
在 Apple Silicon Mac 上驗證能跑。

### 步驟

1. **取得 macOS 環境**（任選）：
   - **借一台 Apple Silicon Mac**（最快）
   - **GitHub Actions `macos-latest` runner**（CI 自動 build + 上傳 artifact，但不能互動測試）
   - **租 MacStadium / MacInCloud**（每月 $20-50）

2. **打包**：
   ```bash
   dotnet publish ... -r osx-arm64 ...
   ```

3. **macOS-specific 處理**：
   - **App bundle 結構**：理想上要包成 `AprNesAvalonia.app/Contents/MacOS/...`，雙擊才能跑。可以暫時不做（從 terminal 執行 binary）。
   - **Code signing**：未簽章的 app 第一次跑會被 Gatekeeper 擋，需要使用者 `xattr -dr com.apple.quarantine` 或在「系統設定 → 隱私權與安全性」按「仍要打開」。在 README 寫清楚即可，不必馬上申請 Apple Developer ID。
   - **Notarization**：完整商業發行才需要。Hobby release 可以暫不做。

4. **測試清單**（同 Phase 3）：
   - ROM 載入
   - 音訊（CoreAudio）
   - 手把（GameController.framework via SDL）
   - GPU CRT（Metal backend via Skia）—— **這個值得特別關注**，Metal 路徑跟 D3D11 / GL 不一樣

### 驗收
- macOS ARM64 binary 能跑、能玩。
- 至少 digital mode 在 native resolution 下 60 FPS。

### 風險
- **沒 Mac**：最大障礙。優先級依手邊資源決定。
- **GPU CRT Metal 路徑**：SkSL 要在 Metal 上跑，理論可行但沒實測過。Fallback 到 CPU CRT backend 的話一樣可玩。
- **App bundle / signing**：最早可以先 ship terminal-runnable binary，之後再做 .app 包裝。

---

## Phase 6：Release 打包 + 文件

### 目標
把跨平台版本納入既有的 release 流程，更新文件跟 download 頁。

### 步驟

1. **更新 `PublishContent/`**：
   - 確認 `tools/ffmpeg/` 之類目錄在 Linux/macOS 也適用（FFmpeg binary 三平台都不同 —— 用戶自己下載）
   - `ReadMe/` 加上 Linux / macOS 安裝說明 HTML

2. **更新 csproj 的 publish target / `AprNesPublishPostProcess`**：
   - 確認 strip 的 `.pdb` / `createdump.exe` 在非 Windows 平台 publish 不會出錯
   - 可能要對 `runtimes/{rid}/` 內容做選擇性 trim（例如不要把 win-x86 native 也包進 linux build）

3. **更新 GitHub Release workflow**（如果有 CI）：
   - `dotnet publish` 三個 RID + Windows
   - Zip / tar.gz 各自打包
   - 上傳成 release artifact

4. **更新文件**：
   - `MD/Avalonia/aprnesava_vs_netfx_features_zh.md` / `_en.md`：
     - 平台支援欄位從「目前只實作 Windows」改成「Windows + Linux + macOS」
     - 跨平台抽象層段落從「未來目標」改成「已完成」
   - `README.md`：加 Linux / macOS 下載連結
   - `site/index.html` 跟 `site/sections/ava_release.html`：更新平台支援標示

5. **新版 release**：
   - 開新 GitHub release tag `aprnesava-YYYYMMDD-multiplatform`
   - 上傳 4 個 binary：`win-x64.zip`、`linux-x64.tar.gz`、`linux-arm64.tar.gz`、`osx-arm64.tar.gz`

### 驗收
- 三平台 release artifact 都能下載解壓並執行。
- 文件、網站、release notes 都反映新的多平台支援。

---

## 風險登記

| 風險 | 嚴重度 | 緩解 |
|---|---|---|
| MiniAudio Linux 上 ALSA-only 環境 underrun | 中 | 加大 buffer size，或允許 fallback 到 PulseAudio |
| SDL3 hot-plug 在不同 Linux 桌面行為不一 | 中 | 至少測 GNOME + KDE 兩種 |
| Avalonia Skia 在 Linux 上 GR backend 不穩 | 高 | GPU CRT 失敗就 fallback CPU SIMD backend；保留切換選項 |
| 沒有 Mac 無法驗證 macOS | 高 | 優先用 GitHub Actions CI 自動 build；功能驗證等借到 Mac |
| ARM64 SBC 效能不足 | 中 | 提供「low-power preset」，預設關 CRT 跟 analog |
| SDL3 binary 跟系統 PulseAudio 版本衝突 | 低 | 罕見；遇到時把 SDL3 audio backend 強制設成 ALSA |
| macOS code signing / Gatekeeper 阻擋 | 低 | README 寫清楚 `xattr -dr com.apple.quarantine` |

---

## 開放問題

下面這些問題現階段沒有定論，等實作中再決定：

1. **是否同時支援 macOS x64（Intel Mac）？**
   - Apple 已停售 Intel Mac，但仍有用戶
   - Hexa.NET.MiniAudio / SDL3 的 NuGet 都有 osx-x64 native binary
   - 加一個 RID 工作量小，但要考慮 release artifact 數量
   - **建議**：先不做，看有沒有人問

2. **Linux ARM32（如老 Raspberry Pi）支援？**
   - Hexa.NET.SDL3 有 linux-arm
   - .NET 10 在 ARM32 Linux 是 EoL
   - **建議**：不做

3. **Wayland-only 環境**（純 Wayland 桌面 + 沒 XWayland）：
   - SDL3 跟 Avalonia 都聲稱支援 Wayland，但需實測
   - **建議**：先以 X11 / XWayland 為主要目標

4. **CI / 自動化**：
   - 是否設立 GitHub Actions 跑跨平台 build + 自動 release？
   - **建議**：等手動流程穩定後再投資 CI

5. **Audio device 選擇 UI**：
   - 現在 PlatformFactory 只挑「default audio device」
   - 未來是否要在 ConfigWindow 加 device selector？
   - **建議**：v1 不做，看用戶回饋

---

## 完成定義（Definition of Done）

當下列全部成立，就視為跨平台支援第一版完成：

- [ ] Windows x64 既有功能不退（regression test 通過）
- [ ] Linux x64 native 機器：能載入 ROM、有聲音、手把可玩、60 FPS
- [ ] Linux ARM64：至少 binary 能跑、有畫面跟聲音（FPS 不要求 60）
- [ ] macOS ARM64：至少 binary 能跑、有畫面跟聲音
- [ ] 三個跨平台 RID 各自有 release artifact 可下載
- [ ] README / 網站更新標示新平台支援
- [ ] memory 內 `project_aprnesava_audio_backend.md` / `project_aprnesava_gamepad_backend.md` 更新為「已實作」狀態
- [ ] 新 GitHub release tag 含三個跨平台 binary
