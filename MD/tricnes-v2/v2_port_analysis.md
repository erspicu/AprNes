# TriCNES v2 移植分析 — 完整架構差異與實作步驟

**日期**: 2026-04-10
**Branch**: `feature/tricnes-v2-port`（從 master 乾淨分出）
**基線**: 184/184 blargg PASS, 舊版 AC 136/136, 新版 AC 135/138
**參考**: `ref/tricnes_md/emulator-core-diff-analysis.md`（2248 行完整差異分析）

---

## 一、核心設計轉向

新版 TriCNES 不是局部修補，而是**整個 PPU bus 模型的重構**：

| 面向 | 舊版（AprNes 目前） | 新版（TriCNES v2） |
|------|-------------------|-------------------|
| $2007 | 整數計數器 SM（state 0-9） | SR latch 管線 + 3 sub-dot phase |
| PPU 讀取 | `PpuBusRead(addr)` 直接讀 | `FetchPPU()` 走 OctalLatch + AddressBus |
| 地址模型 | `ppuAddressBus` = 完整 14-bit | `AddressBus` 高位 + `OctalLatch` 低 8-bit 分離 |
| Tile fetch | 直接計算 addr → 讀 CHR bank | PAR_MUX → AddressBus → ALE → OctalLatch → FetchPPU |
| Register write | delay counter 延後套用 | `EmulateNMasterClockCycles(N)` + 分段套用 |
| Mapper PPU 路徑 | Core 統一 FetchPPU | Mapper 可 override `FetchPPU()` |
| Base mapper | 預設 PRGRAM 行為 | 移除預設，mapper 自行負責 |

**關鍵結論（從 catchup-experiment 學到的教訓）**：
T1（$2007 SM）和 T2（OctalLatch/FetchPPU）**不能分開移植**。新版的 $2007 Phase1 函數會設定 `PPU_READ`/`PPU_ALE` 信號，這些信號在新版中同時驅動 rendering fetch 和 $2007 access。單獨移植 $2007 而不改 rendering fetch 會導致 Phase1 干擾現有渲染管線（已驗證：30 項回歸）。

---

## 二、必須一起移植的統一 bus 模型

### 2.1 PPU Address Path（新版核心）

```
               ┌──────────┐
$2007 access → │ ALE 信號  │ → ppuOctalLatch = low 8 bits of AddressBus
               └──────────┘
                    ↓
               ┌──────────┐
tile fetch  →  │ PAR_MUX  │ → ppuAddressBus = selected PAR source
               └──────────┘
                    ↓
               ┌──────────────────┐
               │ FetchPPU()       │ ← addr = (AddressBus & 0x3F00) | OctalLatch
               │ Mapper override  │
               └──────────────────┘
                    ↓
               read data → AddressBus low 8 bits（共用 bus）
```

### 2.2 三個 sub-dot phase 的位置

```
PPU dot 開頭 (ppu_step_new):
  → PPU_DATA_StateMachine()     ← Phase1: 建立 ALE/PD_RB/PPU_READ 信號
  → sprite eval
  → rendering (tile fetch → 走 bus 模型)
  → PPU_DATA_StateMachine2()    ← Phase2: buffer refill (PD_RB 驅動)

PPU half step (ppu_half_step_new):
  → BG shift
  → PPU_DATA_StateMachine_Half() ← Phase3: v increment + 實際 write + latch 推進
  → VSET latch
  → sprite 0 pipeline
```

### 2.3 為什麼必須一起改

Phase1 計算的 `PPU_READ` 信號 = `PD_RB || (!BLNK && odd_dot)`。後者（`!BLNK && odd_dot`）就是 **rendering fetch 的 read 信號**。如果 rendering fetch 不走 bus 模型，這個信號就和現有渲染管線衝突。

---

## 三、移植步驟（逐行翻寫策略）

### Phase A — PPU bus 基礎設施（零行為變更）

| Step | 內容 | 檔案 |
|------|------|------|
| A1 | 新增 `ppuOctalLatch`, `PPU_PAR_MUX`, `PPU_PatternAddressRegister_*` 欄位 | PPU.cs |
| A2 | 新增 `FetchPPU()` 統一讀取函數（暫時包裝現有 PpuBusRead） | PPU.cs |
| A3 | 新增 $2007 SR latch 欄位（已有，從 catchup branch 帶過來） | PPU.cs |
| A4 | 新增三個 Phase 函數骨架（不啟用） | ppu_new.cs |

**驗證**: 184/184（純新增欄位和函數，不改行為）

### Phase B — Rendering fetch 改走 bus 模型

這是**最關鍵的一步** — 把 tile fetch 和 sprite fetch 從直接讀取改成走 OctalLatch + FetchPPU。

| Step | 內容 | 檔案 |
|------|------|------|
| B1 | BG tile fetch (dots 1-256, 321-336) 改成: even dot 設 AddressBus, odd dot 走 FetchPPU | ppu_new.cs |
| B2 | Sprite fetch (dots 257-320) 同上 | ppu_new.cs (PpuPhase4) |
| B3 | Garbage NT fetch (dots 336-340) 同上 | ppu_new.cs (PpuPhase4) |
| B4 | ALE timing: 在 address 設定時 latch OctalLatch | ppu_new.cs |

**驗證**: 每步 184/184。如果任何步驟回歸，該步獨立 revert。

### Phase C — $2007 SR latch model 啟用

Phase B 完成後，rendering 已走 bus 模型。現在啟用 $2007 的 SR pipeline 不會衝突。

| Step | 內容 | 檔案 |
|------|------|------|
| C1 | 修改 ppu_r_2007：回傳 buffer → EmulateUntilEndOfRead(7 tick) → 設 ReadSR | PPU.cs |
| C2 | 修改 ppu_w_2007：保存 WriteData → EmulateNMasterClockCycles(7 tick) → 設 WriteSR | PPU.cs |
| C3 | 在 ppu_step_new 嵌入 Phase1（dot 開頭）+ Phase2（rendering 後） | ppu_new.cs |
| C4 | 在 ppu_half_step_new 嵌入 Phase3_Half | ppu_new.cs |
| C5 | 移除舊 Process2007StateMachine + 所有 ppu2007SM* 欄位 | ppu_new.cs, PPU.cs |

**關鍵注意**：C1 和 C2 的 7-tick MasterClockTick 是**新版 TriCNES 的正確行為**。之前在 catchup branch 移除它們導致問題更多，因為沒有 Phase B 的 bus 模型配合。有了 Phase B 之後，7-tick 推進不會干擾 rendering（rendering 已走 bus 模型，Phase1 的信號正確驅動）。

**驗證**: C1-C4 一起做（新舊不能混用），用 flag 切換。184/184 + AC test。

### Phase D — Register write timing 更新

| Step | 內容 | 檔案 |
|------|------|------|
| D1 | $2000 write：改成先 dataBus 立即影響 → EmulateNMasterClockCycles(2) → 正確值 | PPU.cs |
| D2 | $2005 open bus glitch（已完成，從 catchup branch 帶過來） | PPU.cs |
| D3 | Palette corruption（已完成，從 catchup branch 帶過來） | PPU.cs, ppu_new.cs |

**驗證**: 每步獨立

### Phase E — Mapper 架構更新

| Step | 內容 | 檔案 |
|------|------|------|
| E1 | IMapper 加 virtual FetchPPU() | IMapper.cs |
| E2 | Base mapper 移除 PRGRAM 預設行為 | 需審計 65 mappers |
| E3 | MMC3 override FetchPPU（nametable VRAM 路由） | Mapper004.cs |
| E4 | MMC3 M2Filter 完全移回 mapper 內部 | Mapper004.cs |

### Phase F — FDS 升級

| Step | 內容 | 檔案 |
|------|------|------|
| F1 | DiskDrive 加 1792-tick byte transfer clock | FDS.cs |
| F2 | FDS mapper 加 $4025 control + ByteTransferFlag IRQ | Mapper FDS |

---

## 四、風險矩陣

| Phase | 風險 | 回歸面 | 說明 |
|-------|------|--------|------|
| A | 零 | — | 純新增欄位 |
| **B** | **高** | **所有 rendering** | tile fetch 改走 bus — 如果 OctalLatch timing 差一拍，全部壞 |
| **C** | **高** | **所有 $2007 行為** | SR pipeline timing — 但有 Phase B 配合應該正確 |
| D | 低 | register timing | 獨立修正 |
| E | 中 | 特定 mapper | 可逐 mapper 處理 |
| F | 中 | FDS 遊戲 | 獨立模組 |

---

## 五、Phase B 的具體翻寫邏輯

### 目前 BG tile fetch（ppu_new.cs Phase 5）:

```csharp
// 現有：直接計算地址 + PpuBusRead
if (fetchPair == 0) { // NT fetch
    int ntAddr = 0x2000 | (vram_addr & 0x0FFF);
    ppuAddressBus = ntAddr;
    renderTemp = PpuBusRead(ntAddr);
}
```

### 新版應改成:

```csharp
// 新版：even dot 放地址 + ALE latch, odd dot 走 FetchPPU
if ((cx & 1) == 0) { // even dot: set address
    int ntAddr = 0x2000 | (vram_addr & 0x0FFF);
    ppuAddressBus = ntAddr;
    ppuOctalLatch = (byte)ppuAddressBus;  // ALE: latch low byte
} else { // odd dot: fetch data
    renderTemp = FetchPPU();  // 用 (AddressBus & 0x3F00) | OctalLatch
}
```

但要注意：目前的 tile fetch 已經只在 odd dot 做 bus read（`if ((cx & 1) != 0)`），even dot 是「dead ioaddr write」（已被優化掉）。所以 Phase B 需要**恢復 even dot 的地址設定**，加上 OctalLatch。

---

## 六、從 catchup-experiment 帶過來的已完成項目

| 項目 | 可直接用？ | 說明 |
|------|-----------|------|
| $2005 open bus glitch (T5) | ✅ | ppu_w_2005 立即 apply cpubus |
| Palette corruption (T3) | ✅ | CorruptPalettes ~100 行 + trigger |
| Sprite in-range gate (T4) | ✅ | sprFetchEnabled 內移 |
| DMC discard (fix) | ✅ | 已在 master |
| SR latch 欄位 (T1-A) | ✅ | PPU.cs 欄位宣告 |
| Phase 函數骨架 (T1-B) | ⚠️ 需修正 | Phase1 去掉 rendering 信號部分（Phase B 完成後不需要去掉） |

---

## 七、預估工期

| Phase | 預估 | 說明 |
|-------|------|------|
| A | 1 小時 | 欄位 + FetchPPU 包裝 |
| **B** | **1-2 天** | tile/sprite fetch 全面改走 bus — 最大工程量 |
| **C** | **半天** | SR pipeline 啟用（有 B 配合應順利） |
| D | 2 小時 | register delay 微調 |
| E | 半天 | mapper 架構清理 |
| F | 2 小時 | FDS 升級 |

**Phase B 是整個移植的關鍵路徑。** 它決定了 rendering 是否走統一 bus 模型。完成 B 之後，C 就是水到渠成。
