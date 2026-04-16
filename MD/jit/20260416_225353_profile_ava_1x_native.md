# AprNesAvalonia Release 1x — JIT + CPU Profile

- **Date**: 2026-04-16 22:53
- **Build**: AprNesAvalonia Release (.NET 10, TieredPGO ON)
- **Target**: `bench_profile_ava_1x.bat` — 1x native digital (no analog/CRT, audio-mode 0)
- **ROM**: `ny2011.nes`, NTSC, 30s
- **Trace**: `temp/aprnesava_jit_1x.etl` (25.6 MB)
- **Samples**: 31,232 (≈ 1ms interval × 30s)
- **PID**: 31824 (CPU time 31,232 ms)
- **Baseline FPS**: ~154.8 FPS @ 1x (from prior bench)

> **Note on EtlAnalyzer output**: the report header says "Debug x64 (.NET Framework 4.8.1)" — that string is hardcoded in `EtlAnalyzer/Program.cs`. Actual target is Avalonia Release on .NET 10. Ignore the header.

---

## Top CPU Hotspots (Exclusive %)

| Rank | Method | Excl% | Samples | Inlined? | Tier reJIT |
|------|--------|-------|---------|----------|-----------|
| 1 | `ppu_step_new` | **41.0%** | 12,801 | FAILED (too many IL bytes) | ×3 |
| 2 | `Run_NTSC` | **20.9%** | 6,518 | standalone (main loop) | ×1 |
| 3 | `PpuPhase4_SpriteEvalAndInit` | **15.4%** | 4,819 | FAILED (too big) | ×3 |
| 4 | `apu_step` | **8.7%** | 2,702 | FAILED (unprofitable) | ×3 |
| 5 | `DoBranch(bool)` | 1.0% | 301 | FAILED (unprofitable) | — |
| 6 | `Wrap_MapperR_RPG(ushort)` | 0.8% | 242 | YES | — |
| 7 | `NestedTick7_NTSC` | 0.8% | 239 | standalone | — |
| 8 | `Op_2C` (BIT abs) | 0.5% | 163 | standalone | — |
| 9 | `ApuOutputCatchup` | 0.4% | 137 | standalone | — |
| 10 | `GetAddressAbsolute` | 0.4% | 124 | YES (varies) | — |
| 11 | `clockdmc` | 0.4% | 111 | FAILED (unprofitable) | — |
| 12 | `PPU_DATA_Pipeline_Step(int)` | 0.3% | 91 | YES | ×3 |

**Top 4 = 86.0%**, top 10 = 89.9%.

### NesCore subtotal
**NesCore.*** exclusive total: **92.9% CPU** (29,009 / 31,232 samples) — 剩餘 7% 為 Avalonia / WinRT / Win32 framework。

### Framework overhead (~1–2%)
- Avalonia 初始化 (`AppBuilder.Setup*`): 1.5% inclusive
- Win32/OpenGL/Angle 初始化: ~2% inclusive
- 這些只在啟動時跑一次，不是熱路徑

---

## Inclusive CPU (含 callees)

| Rank | Method | Incl% |
|------|--------|-------|
| 1 | `TestRunnerCore.<Run>b__12_3` (測試 harness) | 95.7% |
| 2 | `Run_NTSC` | 95.7% |
| 3 | `run` | 95.7% |
| 4 | **`ppu_step_new`** | **58.5%** |
| 5 | `PpuPhase4_SpriteEvalAndInit` | 16.5% |
| 6 | `apu_step` | 9.3% |
| 7 | `Op_2C` (BIT abs) | 4.9% ← 高 incl 代表呼叫 callees 很多 |
| 8 | `ppu_r_2002` | 4.2% |
| 9 | `NestedTick7_NTSC` | 4.2% |

**`ppu_step_new` inclusive 58.5% vs exclusive 41.0%** → 17.5% 是它呼叫的子函數（`ppu_half_step_new`, `PPU_DATA_Pipeline_Step`, mapper hooks 等）。

---

## JIT 編譯統計

| 項 | 數量 |
|---|------|
| 總 method JIT 數 | 3,393 |
| NesCore 專屬 | 643 |
| Inline 成功 | 2,970 |
| Inline 失敗 | 1,923 |

### TieredPGO 晉升可見
下列方法出現 **3 次** JIT（Tier-0 → Tier-1 with PGO → 有時還有 re-JIT）：
- `ppu_step_new` (2693 IL bytes)
- `PpuPhase4_SpriteEvalAndInit` (1866)
- `apu_step` (676)
- `ppu_half_step_new` (631)
- `PPU_DATA_Pipeline_Step` (629)
- `SpriteEvalTick` (621)

### IL Size Top 5
| IL | Method |
|----|--------|
| 5289 | `InitOpHandlers` ← 256-opcode jump table init |
| 4207 | `TestRunnerCore.Run` |
| 3160 | `NesCore..cctor` (static constructor) |
| 3050 | `initAPU` |
| 2693 | `ppu_step_new` ← **熱路徑主體** |

---

## Inline 失敗分析（熱路徑相關）

| 方法 | 失敗次數 | 原因 | 熱路徑？ |
|------|---------|------|---------|
| `GetAddressAbsolute` | 18 | unprofitable | 是（多 opcode 呼叫） |
| `PpuBusWrite` | 14 | unprofitable | 間接熱 |
| `ppu_step_new` | 11 | **too many IL bytes** | **最熱（41%）** |
| `GetAddressAbsOffX` | 10 | unprofitable | 是（多 opcode） |
| `DoBranch` | 8 | unprofitable | 中（1%） |
| `apu_step` | 4 | unprofitable | 是（8.7%） |
| `NotifyMapperA12` | 4 | unprofitable | 中 |

**關鍵觀察：**
- **`ppu_step_new` 41% CPU + 無法 inline** — 它是超大的 per-dot state machine，JIT cost model 直接放棄。若要壓效能這裡是最大 ROI。
- **`GetAddressAbsolute` 18 處失敗 inline 但 exclusive 只 0.4%** — 代表它很常被呼叫但每次很短，JIT 判斷 inline 沒收益（代碼膨脹 > 省下的 call overhead）。可用 `[MethodImpl(MethodImplOptions.AggressiveInlining)]` 強制。

### Inline 成功排行（高頻小工具）
| 次數 | 方法 |
|------|------|
| 124 | `CpuRead` |
| 70 | `PollInterrupts` |
| 65 | `CompleteOperation` |
| 54 | `SetNZ` |
| 45 | `get_evalOamAddr` |
| 34 | `CpuWrite` |
| 33 | `AllocUnmanaged` ← 新 helper 成功 inline |
| 21 | `CIRAMAddr` |
| 19 | `PpuBusRead` |
| 15 | `PPU_DATA_Pipeline_Step` |

---

## 呼叫頻率說明

ETW CPU sampling 是每 ~1ms 取樣 CPU 狀態，**不是精確 call count**。  
- 高 sample = 「呼叫次數 × 每次耗時」的乘積，只能反映 CPU 時間佔比
- 精確 call count 需 CLR MethodEntered/Exited event（現在沒啟用，會嚴重拖慢執行 + trace 爆大）
- **Inline successes 的 count 欄** = JIT 看到的 inline site 數，可粗估「原始碼呼叫位置數」，不是 runtime call frequency

頻率粗估方法：**Inclusive% ÷ 單次耗時推估** — 例如 `CpuRead` 被 inline 124 處（每個 opcode 都會呼叫），inclusive 應該很高但 exclusive 可能被 inline 吃掉看不到。

---

## 優化方向建議

### 🎯 1. `ppu_step_new` 瘦身（最大 ROI）
佔 41% CPU、IL 2693 bytes、被 inline 拒絕。做法：
- 拆成多個小 helper（JIT 可分別 inline）
- 抽出冷路徑（pre-render / VBlank dot 等）到獨立 method
- 目標：讓 hot 路徑縮到 IL < 200 bytes 可被 inline 到 Run_NTSC 內

### 🎯 2. `GetAddressAbsolute` / `GetAddressAbsOffX` 強制 inline
被 18 + 10 處呼叫但都被判 unprofitable。加 `[MethodImpl(AggressiveInlining)]` 強迫 inline，避開 call overhead。

### 📉 3. `DoBranch` unprofitable
1% CPU 但 8 處失敗 inline。短函數但 JIT 覺得不划算。可試 AggressiveInlining，但收益有限。

### ⚙️ 4. `PpuPhase4_SpriteEvalAndInit` 已算優化重點
15.4% CPU，IL 1866 bytes，inline 失敗。同 ppu_step_new 策略，熱路徑拆小。

### 💡 5. `apu_step` 8.7%
佔比不低，可考慮用 catchup 模式取代 per-cycle（部份已是 ApuOutputCatchup）。

---

## 既有狀態

- 熱路徑 top 4 集中度極高（86%）— 繼續精修這 4 個 method 即可推升整體 FPS
- Avalonia framework overhead 極低（<2% 啟動期）— 不是瓶頸
- TieredPGO 有效晉升（可見 ×3 JIT）
- AllocUnmanaged helper inline 成功 33 處 — 無呼叫 overhead

---

## 原始報告
- 完整文字報告：`temp/profile_report_ava_1x.txt` (1031 行)
- ETL 原檔：`temp/aprnesava_jit_1x.etl`
