# AprNes 待修復問題清單

**基線**: blargg 174/174 PASS | AccuracyCoin 117/136 PASS, 18 FAIL, 1 SKIP
**最後更新**: 2026-03-07 (BUGFIX44)

優先權排序原則：**影響大 + 好修** 排最前面

---

## AccuracyCoin 剩餘 18 FAIL + 1 SKIP

### 優先級 A: 可修（預期可行，不需架構改動）

| # | 頁面 | 測試 | 地址 | err | 修復方向 | 預估影響 |
|---|------|------|------|-----|---------|---------|
| 1 | P10 | SH* 5 項 (SHA/SHS/SHY/SHX) | $0446-$044A | 7 | DMA 發生在 dummy read cycle 時，SHX→STX、SHY→STY 等（去掉 `& (H+1)` masking）。需偵測 dummy read 是否觸發 DMA | +5 |

### 優先級 B: 中等難度（需要理解但可能可行）

| # | 頁面 | 測試 | 地址 | err | 修復方向 | 預估影響 |
|---|------|------|------|-----|---------|---------|
| 2 | P19 | Sprites On Scanline 0 | $0484 | 2 | Pre-render line (261) sprite tile fetch 需用 `261 & 255 = 5` 做 Y range check，需 secondary OAM 持久化 | +1 |
| 3 | P18 | $2002 flag clear timing | $048D | 1 | Pre-render line flag 清除時序需精確到 sub-dot（M2 duty cycle: VBL 與 sprite flags 分開清除）。**注意**: 嘗試過 dot 1→3 stagger 會回歸 P17 VBL timing (+2 FAIL) | +1 |
| 4 | P19 | $2004 Stress Test | $048C | 1 | 需 OAM Latch + Secondary OAM buffer，$2004 在 rendering 期間返回 evaluation latch 值而非直接 OAM | +1 |
| 5 | P19 | BG Serial In | $0487 | 2 | 需 per-dot BG shift register model（目前 shadow-only），shift register reload 時序 | +1 |
| 6 | P14 | APU Register Activation | $045C | 6 | Test 4 已修（BUGFIX44）。Test 5-7 需從 $3FFE 執行 STA $4014 + DMC DMA 配合，極度複雜的 bus 行為 | +0→+1 |
| 7 | P14 | Controller Strobing | $045F | 1 | DEC $4016 的 PUT/GET cycle 對齊。目前 strobe deferred 機制與 Mesen2 一致，可能是 OAM DMA 後 parity 不準 | +1 |

### 優先級 C: 困難（需架構改動或精確子周期時序）

| # | 頁面 | 測試 | 地址 | err | 說明 |
|---|------|------|------|-----|------|
| 8 | P13 | DMA 6 項 | $0488,$045D,$046B,$0477,$0479,$0478 | 2 | 共用前置條件 DMADMASync_PreTest 失敗。DMC DMA open bus 更新 1-cycle drift，需 sub-cycle M2 duty cycle | +6 |
| 9 | P14 | DMC | $046A | 21 | DMC 通道測試，高錯誤碼表示多個子測試失敗 | +1 |
| 10 | P20 | Implied Dummy Reads | $046D | 3 | 前置條件失敗（DMA timing），本身的 implied dummy reads 已正確實作 | +1 |
| 11 | P12 | IRQ Flag Latency | $0461 | SKIP | Test E 掛住（DMC DMA 累積時序偏移 ~12 cycles），需 Master Clock scheduler | +1 |

---

## 建議修復順序

### Step 1: P10 SH* 5 項 (+5)
**影響最大**。DMA 發生在 SH* 指令的 dummy read cycle 時，指令退化為簡單 store。
- 在 `Mem_r()` 或 `dmcfillbuffer()` 中設定 `dmcOccurredDuringRead = true` flag
- SH* opcodes (0x93, 0x9B, 0x9C, 0x9E, 0x9F) 的 dummy read 後檢查此 flag
- 若 flag 為 true，寫入值改為不含 `& (H+1)` masking（SHX→X, SHY→Y, SHA→A, SHS→SP）

### Step 2: P13 DMA 6 項 (+6)
最大潛在收益但最難。需修正 DMADMASync_PreTest 前置條件。
- DMC DMA open bus 更新需精確到 M2 duty cycle 內的時序點
- 可能需要 sub-cycle PPU dot splitting

---

## 已嘗試但未成功的修復

| 目標 | 嘗試 | 結果 | 原因 |
|------|------|------|------|
| P18 $2002 flag clear | Dot 2→3 VBL stagger | P17 -2 回歸 | VBL end timing 被延後 1 dot |
| P14 APU Reg Test 6 | $4020-$40FF mirror to $4000-$401F | P14 -1 回歸 | Controller Strobing 受影響 |
| P14 Controller Strobing | 翻轉 strobe parity | -1 回歸 | 其他測試依賴當前 parity |

---

## 已完成的 AccuracyCoin 修復（BUGFIX30-44）

| BUGFIX | 日期 | 修復內容 | AC 分數 |
|--------|------|---------|---------|
| 30 | 03-04 | Branch dummy reads, CPU open bus, controller open bus | 76/81→? |
| 31 | 03-06 | Load DMA parity fix | blargg 174/174 |
| 32 | 03-06 | Load DMA cpuCycleCount parity | blargg 174/174 |
| 33 | 03-07 | AccuracyCoin page-by-page test runner | 測試框架 |
| 34 | 03-07 | Unofficial opcodes batch fix | 103→108 |
| 35 | 03-07 | Arbitrary sprite zero + misaligned OAM + parallel runner | 108→110 |
| 36 | 03-07 | OAM corruption | 110→111 |
| 37 | 03-07 | PPU register open bus + $2004 during rendering | 111→112 |
| 38 | 03-07 | INC $4014 + palette RAM quirks | 112→113 |
| 39 | 03-07 | Attributes as tiles + t register quirks | 113→114 |
| 40 | 03-07 | Stale BG shift registers + deferred Load DMA | 114→115 |
| 41 | 03-07 | $2007 read during rendering | 115→116 |
| 42 | 03-07 | Suddenly resize sprite (sprite size latch at dot 261) | 116→117 |
| 43 | 03-07 | Rendering flag behavior (freeze BG shift regs when off) | 116→117 |
| 44 | 03-07 | OAM DMA APU activation bypass + debug cleanup | 117→117 |

---

*blargg 174/174 全 PASS 自 BUGFIX31 起維持至今*
