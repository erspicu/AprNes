# AccuracyCoin FAIL 修復 TODO

**基線**: 110/136 PASS, 25 FAIL, 1 SKIP (BUGFIX35 後)
**目標**: 逐步提升至 Master Clock 驅動模型

---

## 分類統計

| 分類 | 數量 | 說明 |
|------|------|------|
| INDEPENDENT | 7 | 不依賴 timing 改動，可獨立修復 |
| TIMING-CORE | 10 | 需要 DMA/IRQ/PPU cycle-level 改動 |
| TIMING-DEPENDENT | 7 | 依賴正確 timing 基礎設施 |
| HARDWARE-EDGE | 5 | SH* RDY line，極端硬體行為 |

---

## Phase 1: INDEPENDENT（獨立修復，優先處理）

### 1.1 EASY（快速修復）

- [x] **Controller Strobing** (P14, $045F, err=2→4 partial, BUGFIX33)
  - 修復 bit 0 check。Tests 1-3 pass, test 4 (put/get cycle parity) 需 TIMING-CORE

- [x] **Address $2004 behavior** (P18, $045B, err=4→8, BUGFIX34)
  - 修復: $2004 reads during dots 1-64/257-320 return $FF; writes during rendering increment by 4 & align
  - Tests 1-7 pass, test 8 需 OAM evaluation latch (TIMING-DEPENDENT)

### 1.2 MEDIUM（中等難度）

- [x] **Rendering Flag Behavior** (P16, $0486, PASS - BUGFIX33)
  - 修復: OAMADDR dot 257 reset + PrecomputeSprite0Line 條件 + per-pixel S0H 兩旗標檢查

- [x] **Arbitrary Sprite zero** (P18, $0458, PASS - BUGFIX35)
  - 修復: sprite0_eval_addr 在 dot 65 儲存 OAMADDR，PrecomputeSprite0Line 改用此值
  - 支援 OAMADDR 非零時的 sprite 0 判定

- [x] **Misaligned OAM behavior** (P18, $045A, PASS - BUGFIX35)
  - 修復: PrecomputeSprite0Line 支援未對齊 OAMADDR，正確模擬 addrH/addrL 遞增

- [ ] **OAM Corruption** (P18, $047B, err=2, MEDIUM)
  - rendering enable/disable 轉換時，OAM row 0 應被複製到其他 row
  - OAMADDR corruption bug 未實作

- [ ] **APU Register Activation** (P14, $045C, err=4, MEDIUM)
  - OAM DMA 頁面非 $40 時，DMA 不應與 APU 暫存器產生 bus conflict

---

## Phase 2: TIMING-CORE（核心時序，最重要的基礎設施）

### 2.1 中等難度（先處理）

- [ ] **Frame Counter IRQ** (P14, $0467, err=7, MEDIUM)
  - $4017 在 odd/even CPU cycle 寫入時，IRQ flag 未正確清除
  - 涉及 frame counter 與 CPU bus cycle parity 的交互

- [ ] **$2002 flag clear timing** (P18, $048D, err=1, MEDIUM)
  - VBL/S0H/overflow flags 未在正確的 PPU dot 清除
  - 清除發生在 pre-render scanline dot 1，但 sub-cycle 精度不足

### 2.2 高難度（DMA cluster，互相關聯）

- [ ] **DMA + $4015 Read** (P13, $045D, err=2, HARD)
  - DMC DMA halt/alignment 的 phantom reads 碰到 $4015 時應清除 frame counter IRQ flag

- [ ] **DMA + $2002 Read** (P13, $0488, err=2, HARD)
  - DMC DMA halt/alignment 的 phantom reads 碰到 $2002 時應有 side effects

- [ ] **DMC DMA Bus Conflicts** (P13, $046B, err=2, HARD)
  - DMC DMA 讀取 $4000-$401F 範圍時應與 APU 暫存器產生 bus conflict

- [ ] **DMC DMA + OAM DMA** (P13, $0477, err=2, HARD)
  - DMC DMA 與 OAM DMA 重疊時的 cycle count 不正確

- [ ] **Explicit DMA Abort** (P13, $0479, err=2, HARD)
  - DMA 被中止時的 stolen cycle 數量不正確

- [ ] **Implicit DMA Abort** (P13, $0478, err=2, HARD)
  - DMA 被隱式中止時的 stolen cycle 數量不正確

- [ ] **Delta Modulation Channel** (P14, $046A, err=21/L, HARD)
  - $4015 寫入時 DMC timer 即將到期，新 DMA 應延遲 3-4 cycle 才觸發

- [ ] **INC $4014** (P18, $0480, err=3, HARD)
  - RMW 指令對 $4014 的兩次寫入應只觸發一次 OAM DMA

---

## Phase 3: TIMING-DEPENDENT（依賴正確 timing 才能修復）

### 3.1 中等難度

- [x] **$2007 read w/ rendering** (P16, $048A, PASS - BUGFIX34)
  - 修復: Increment2007() — rendering 期間 CXinc+Yinc 取代 +1/+32

- [ ] **Stale BG Shift Registers** (P19, $0483, err=1, MEDIUM)
  - rendering 關閉後再開啟，BG shift registers 應保留 stale data

- [ ] **$2004 Stress Test** (P19, $048C, err=1, MEDIUM)
  - dot 0 讀取 $2004 應返回 Secondary OAM Index 0

### 3.2 高難度

- [ ] **Implied Dummy Reads** (P20, $046D, err=3, HARD)
  - 前置條件：DMC DMA 必須正確更新 data bus（被 DMA timing 擋住）

- [ ] **Suddenly Resize Sprite** (P18, $0489, err=4, HARD)
  - HBlank 寫入 $2000 改變 sprite size 應影響當前 scanline 的 sprite 範圍判定

- [ ] **Sprites On Scanline 0** (P19, $0484, err=2, HARD)
  - pre-render scanline 的 sprite evaluation 使用 stale secondary OAM 資料

- [ ] **BG Serial In** (P19, $0487, err=2, HARD)
  - BG shift registers 在 bit 0 shift in 1（非 0）
  - 透過精確定時 $2001 寫入來測試

---

## Phase 4: HARDWARE-EDGE（最低優先，SH* RDY line）

- [ ] **SHA (ind),Y** (P10, $0446, err=7, HARD)
- [ ] **SHA abs,Y** (P10, $0447, err=7, HARD)
- [ ] **SHS abs,Y** (P10, $0448, err=7, HARD)
- [ ] **SHY abs,X** (P10, $0449, err=7, HARD)
- [ ] **SHX abs,Y** (P10, $044A, err=7, HARD)

所有 5 項的 err=7 表示：RDY low 2 cycles before write 時 target address 不正確。
需要模擬 DMA 期間 RDY line 對 CPU 內部 bus 的影響，幾乎沒有遊戲依賴此行為。

---

## 建議修改路徑

```
Phase 1 (INDEPENDENT)     → 快速提升 PASS 數（+7，達 113/136）
  ↓
Phase 2.1 (TIMING-CORE easy) → 修復核心 timing 基礎（+2，達 115/136）
  ↓
Phase 2.2 (DMA cluster)   → Master Clock 重構，一次解決 DMA 相關問題（+8，達 123/136）
  ↓
Phase 3 (TIMING-DEPENDENT)→ 在正確 timing 基礎上修復剩餘（+7，達 130/136）
  ↓
Phase 4 (HARDWARE-EDGE)   → 追求極致精確度（+5，達 135/136）
  ↓
Page 12 item 1 (SKIP→PASS)→ 需要完整 Master Clock（136/136）
```

---

*最後更新：2026-03-08*
