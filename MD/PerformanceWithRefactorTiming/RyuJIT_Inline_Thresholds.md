# RyuJIT Inlining 門檻與機制

- **來源**: `dotnet/runtime` → `src/coreclr/jit/inlinepolicy.cpp`, `inline.h`, `inline.def`
- **日期**: 2026-04-07
- **適用**: .NET Framework 4.8 (RyuJIT x64) / .NET 8/9/10 (RyuJIT)

---

## 1. 核心閾值

| 參數 | 常數名 | .NET Framework 4.8 | .NET 8/9+ (Default) | .NET 8/9+ (PGO Extended) |
|------|--------|-------------------|---------------------|--------------------------|
| 自動 inline（無獲利性檢查） | `ALWAYS_INLINE_SIZE` | ≤ **16 IL bytes** | ≤ 16 | ≤ 16 |
| 最大 IL size | `DEFAULT_MAX_INLINE_SIZE` | ≤ **100 IL bytes** | ≤ 100 | ≤ 128 (無 profile) / 256 (root profiled) / **1024** (callee profiled) |
| 最大 basic blocks | `MAX_BASIC_BLOCKS` | ≤ **5** | ≤ 5 | 5（有 profile 時放寬） |
| 最大 stack depth | `SMALL_STACK_SIZE` | ≤ **16** | ≤ 16 | ≤ 16 |
| 最大 inline 深度 | `DEFAULT_MAX_INLINE_DEPTH` | ≤ **20** | ≤ 20 | ≤ 20 |
| Inline 預算 | `DEFAULT_INLINE_BUDGET` | **22x** | 22x | 22x |
| 絕對上限 | `IMPLEMENTATION_MAX_INLINE_SIZE` | 65535 | 65535 | 65535 |
| Local tracking 上限 | `JitMaxLocalsToTrack` | 1024 | 1024 | 1024 |

---

## 2. 決策流程

```
方法被呼叫
  │
  ├─ 檢查 FATAL 條件 → 有任一項 → ❌ 絕對拒絕（見第 3 節）
  │
  ├─ [NoInlining] 標記？ → ❌ 絕對拒絕
  │
  ├─ IL size ≤ 16 bytes？ → ✅ 自動 inline（跳過獲利性檢查）
  │
  ├─ [AggressiveInlining]？ → 繞過 IL size / basic blocks / stack depth / 獲利性
  │                            但仍受 FATAL 條件限制
  │
  ├─ IL size > 100 bytes？ → ❌ 拒絕 "too many il bytes"
  │
  ├─ Basic blocks > 5？ → ❌ 拒絕
  │
  ├─ 獲利性評估（16-100 bytes 區間）
  │   ├─ 估算 callee native size vs callsite native size × multiplier
  │   ├─ 加分因素：constructor (+1.5x), promotable struct (+3.0x),
  │   │           SIMD (+configurable), wrapper (+1.0x),
  │   │           const propagation (+3.0x), load/store-heavy (+3.0x)
  │   └─ native size 超過閾值？ → ❌ 拒絕 "unprofitable inline"
  │
  ├─ Inline 預算檢查（累積 inline 成本 vs 22x 預算）
  │   ├─ IL ≤ 12 bytes 可超預算
  │   └─ force-inline 可超預算
  │
  └─ ✅ 允許 inline
```

### throw block 內的特殊規則
- `alwaysInlineSize` 降為 **8**（平時 16）
- `maxCodeSize` 限制為 **min(9, maxCodeSize)**

---

## 3. 絕對拒絕條件（FATAL — 無論大小或 attribute）

以下任一項成立即**永遠不會被 inline**，`[AggressiveInlining]` 也無效：

| 條件 | 說明 |
|------|------|
| **含 try/catch/finally/filter** | Exception handling |
| **含 endfinally/endfilter** | EH 相關 opcode |
| **含 leave** opcode | EH 相關 |
| **synchronized 方法** | MethodImplOptions.Synchronized |
| **async 方法**（含 await） | 狀態機 |
| **managed/native varargs** | 可變參數 |
| **abstract/extern** | 無方法體 |
| **explicit tail prefix** | tail. call |
| **unmanaged calling convention** | P/Invoke |
| **internal array method** | 陣列內建方法 |
| **`[NoInlining]`** | 明確禁止 |
| **localloc 過大** | stackalloc |
| **stack crawl mark** | 安全性相關 |

---

## 4. `[AggressiveInlining]` 的實際效果

| 檢查項目 | 正常方法 | AggressiveInlining |
|----------|---------|-------------------|
| IL size ≤ 100 | 必須 | **繞過**（上限 65535） |
| Basic blocks ≤ 5 | 必須 | **繞過** |
| Stack depth ≤ 16 | 必須 | **繞過** |
| 獲利性評估 | 必須通過 | **繞過** |
| Inline 預算 | 受限 | **可超預算** |
| FATAL 條件 | 拒絕 | **仍然拒絕** |

**結論**：`[AggressiveInlining]` 是強力的「建議」但不是命令。FATAL 條件仍然生效。

---

## 5. 環境變數覆寫

可透過 `COMPlus_` (.NET Framework) 或 `DOTNET_` (.NET 5+) 設定：

| 變數 | 預設值 | 說明 |
|------|--------|------|
| `JitInlineSize` | 100 | 覆寫 `DEFAULT_MAX_INLINE_SIZE` |
| `JitInlineDepth` | 20 | 覆寫 `DEFAULT_MAX_INLINE_DEPTH` |
| `JitInlineBudget` | 22 | 覆寫 `DEFAULT_INLINE_BUDGET` |
| `JitExtDefaultPolicyMaxIL` | 128 | ExtendedPolicy 最大 IL（.NET 7+） |
| `JitExtDefaultPolicyMaxILProf` | 1024 | ExtendedPolicy + profile 最大 IL |
| `JitExtDefaultPolicyMaxILRoot` | 256 | ExtendedPolicy root profiled 最大 IL |

---

## 6. AprNes 實測對照

### ppu_step_new inline 失敗歷程

| 版本 | 估計 IL size | 失敗原因 | 說明 |
|------|-------------|----------|------|
| Phase 4 extraction 前 | ~2000+ bytes | too many il bytes | 遠超 100 bytes 門檻 |
| Phase 4 extraction 後 | ~1200+ bytes | too many locals | IL 縮減但 local 數影響獲利性 |
| Phase 2+3+4 extraction 後 | ~1000+ bytes | too many il bytes | locals 問題解決，IL 仍超標 |

### 各方法 inline 結果分類

**自動 inline 成功（IL ≤ ~100, basic blocks ≤ 5）：**
CIRAMAddr, CXinc, CopyHoriV, Yinc, FlipByte, SetNZ, PpuBusRead, setvolumes, ppu_half_step_new, UpdateIRQLine

**AggressiveInlining 成功（IL > 100 但無 FATAL 條件）：**
MasterClockTick（已標 AggressiveInlining，成功 inline 進 run）

**AggressiveInlining 仍失敗：**
ComputeSpritePatternAddr — 標了 AggressiveInlining 但仍報 "too many il bytes"
（注：這可能是 JIT cache 機制，非真正的 IL 超標。重新啟動可能改變結果）

**[NoInlining] 生效：**
PpuPhase2_DeferredUpdates, PpuPhase3_Events, PpuPhase4_SpriteEvalAndInit, ApuFrameCounterStep, clockdmc

**FATAL 拒絕（含 EH）：**
init（has exception handling）, LoadAndValidateFdsBios, initFDS

### .NET Framework 4.8 vs .NET 10 的根本差異

| | .NET Framework 4.8 | .NET 10 (AprNesAvalonia) |
|---|---|---|
| 最大 inline IL | **100 bytes** | **1024 bytes** (PGO) |
| PGO | 無 | 有（TieredPGO） |
| OSR (On-Stack Replacement) | 無 | 有 |
| Baseline FPS | **104.31** | **380.95** |

PGO 將 inline 上限提高 10x（100 → 1024），這是 .NET 10 快 3.65x 的重要因素之一。
