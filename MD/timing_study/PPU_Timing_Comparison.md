# AprNes vs TriCNES — PPU Timing 模型完整比較分析

**建立日期：2026-04-02**
**目的**：深入比對兩個模擬器的 PPU timing 模型，找出差異根因，作為後續修正的參考依據。

---

## 1. 主時鐘模型（Master Clock）

### AprNes
- **檔案**：`MEM.cs:135-139`
- **模型**：Mesen2 風格 master clock 累加器
- **NTSC**：每 CPU cycle 精確 3 PPU steps（masterPerCpu=12, masterPerPpu=4）
- **PAL**：3 或 4 PPU steps（3×5+5 週期模式）
- **機制**：`catchUpPPU_ntsc()` 連續呼叫 `ppu_step_ntsc()` 3 次，NMI 偵測內聯在每次 PPU step 之後

### TriCNES
- **檔案**：`Emulator.cs:677-690`
- **模型**：PPUClock 倒數計時器（從 4 倒數到 0）
- **機制**：PPUClock==0 觸發 `_EmulatePPU()`，PPUClock==2 觸發 `_EmulateHalfPPU()`
- **NMI**：在 CPUClock==8 時檢測（約 CPU cycle 的 2/3 處）

### 相同點
- 兩者都是 **per-dot 精度**，NTSC 每 CPU cycle = 3 PPU dots
- 都支援 NTSC/PAL/Dendy 三區域

### 差異分析
| | AprNes | TriCNES | 影響 |
|---|---|---|---|
| 精度粒度 | PPU dot 級（1/3 CPU cycle） | **半 PPU dot 級**（1/6 CPU cycle） | TriCNES 有 `_EmulateHalfPPU()` 可處理半 dot 精度事件 |
| NMI 檢測時機 | 每次 PPU step 之後 | CPUClock==8（固定位置） | 差異在 NMI 與 CPU 指令的相對時序 |

---

## 2. PPU 每 Dot 狀態機

### AprNes
- **檔案**：`PPU.cs:485-649`
- **結構**：拆分為 `ppu_step_common()`（通用）+ `ppu_step_rendering()`（渲染）+ 區域專用 wrapper
- **每 dot 處理順序**：
  1. $2007 read cooldown 遞減
  2. $2006 delayed t→v copy
  3. Open bus decay
  4. `renderingEnabled` 計算
  5. Sprite 0 hit 逐 dot 檢測
  6. Tile fetch（`ppu_rendering_tick()`）
  7. Sprite evaluation（per-dot FSM）
  8. VBL/NMI 事件
  9. **最末**：`ppuRenderingEnabled = renderingEnabled`（延遲更新）

### TriCNES
- **檔案**：`Emulator.cs:1256-1810`
- **結構**：單一巨型 `_EmulatePPU()` 函式（~550 行），另有 `_EmulateHalfPPU()` 處理半 cycle
- **每 dot 處理順序**：
  1. 所有 delay counters 遞減（$2000/$2001/$2005/$2006）
  2. Rendering enable flags 延遲更新
  3. Sprite evaluation
  4. Tile fetch + shift register 更新
  5. Pixel 輸出 + 顏色計算
  6. VBL/NMI 事件
  7. Mapper specific functions

### 相同點
- 都是 **per-dot** 執行（非 per-scanline batch）
- 都在 dot 開頭處理延遲計數器
- Sprite 0 hit 都是逐 dot 精度

### 差異分析
| | AprNes | TriCNES | 影響 |
|---|---|---|---|
| 架構 | 子函式拆分（inlined） | 單一巨型函式 | 可維護性 vs 效能 |
| 延遲計數器 | 只有 $2006、$2007 | **$2000/$2001/$2005/$2006 全部有** | TriCNES 更精確的寄存器延遲 |
| Rendering enable | end-of-dot 延遲（1 dot） | **2-3 cycle delay + 多層 flag** | 像素渲染決策時機不同 |

---

## 3. $2000 寫入（Control Register — NMI/Pattern Table）

### AprNes（`PPU.cs:1401-1425`）
- Nametable、pattern table、sprite size 等 **立即更新**
- NMI：rising edge 自然延遲（下一次 PPU step 偵測），falling edge 立即取消 `nmi_delay_cycle`

### TriCNES（`Emulator.cs:9466-9499, 1305-1320`）
- 設定 `PPU_Update2000Delay = 1-2`（依 CPU/PPU alignment）
- Delay 到期後才真正套用 control values

### 相同點
- NMI enable 的 rising/falling edge 語義一致

### 差異分析
| | AprNes | TriCNES | 影響 |
|---|---|---|---|
| 延遲 | 無（立即） | 1-2 PPU cycles | mid-scanline $2000 寫入後的 pattern table 切換時機差 1-2 dot |

**實際影響**：scanline-a1 第二區（$2000 D4 toggle）的星號可能與此有關。

---

## 4. $2001 寫入（Mask Register — ShowBG/ShowSprites）⭐ 重要差異

### AprNes（`PPU.cs:1428-1461`）
- `ShowBackGround`/`ShowSprites` **立即更新**
- OAM corruption 用即時 flag 判定
- `ppuRenderingEnabled`（1 dot delay）僅用於 tile fetch 和 sprite 0 hit

### TriCNES（`Emulator.cs:9501-9580, 1681-1694`）
- **四層 flag 系統**：
  - `_Instant`：立即設定，用於 OAM evaluation
  - `PPU_Mask_ShowBackground`：延遲 2-3 PPU cycles，用於 pixel rendering
  - `_Delayed`：再延 1 cycle，用於 sprite evaluation
  - `PPU_Update2001Delay`：2-3 cycles（依 `PPUClock & 3` alignment）

### 相同點
- OAM corruption 邏輯語義相同（disable rendering → corrupt OAM）
- Sprite 0 re-evaluation on mid-scanline re-enable

### 差異分析
| | AprNes | TriCNES | 影響 |
|---|---|---|---|
| 延遲層數 | 2 層（即時 + ppuRenderingEnabled） | **4 層**（instant/delayed/eval-delayed/main） | pixel rendering 決策時機差 1-2 dots |
| 延遲值 | 固定 1 dot | **alignment-dependent 2-3 cycles** | 不同 CPU/PPU alignment 下行為不同 |

**實際影響**：scanline-a1 第一區（$2001 D3 toggle）右側 5 個星號的根因。我們嘗試加入 delay 但因為影響 odd frame skip 等核心邏輯而回退。

---

## 5. $2005 寫入（Scroll Register）

### AprNes（`PPU.cs:1529-1543`）
- FineX 和 vram_addr_internal **立即更新**
- 無延遲

### TriCNES（`Emulator.cs:9615-9642, 1285-1303`）
- 設定 `PPU_Update2005Delay = 1-2`（依 alignment）
- Delay 到期後才更新 FineX 和 scroll 值
- Latch 也在 delay 後才 toggle

### 相同點
- 第一次寫入更新 FineX + coarse X，第二次更新 Y scroll

### 差異分析
| | AprNes | TriCNES | 影響 |
|---|---|---|---|
| 延遲 | 無（立即） | **1-2 PPU cycles** | mid-scanline scroll 切換的位置偏差 1-2 pixels |

**實際影響**：colorwin_ntsc 視窗右邊界破圖（scroll 切換時機差異）。

---

## 6. $2006 寫入（VRAM Address）

### AprNes（`PPU.cs:1544-1559, ppu_step_common 491-495`）
- 第一次寫入：立即更新 `vram_addr_internal` 高位
- 第二次寫入：設定 `ppu2006UpdateDelay = 3`，3 PPU dots 後才執行 t→v copy

### TriCNES（`Emulator.cs:9644-9672, 1264-1283`）
- 第二次寫入：設定 `PPU_Update2006Delay = 4-5`（依 alignment）
- Delay 到期後執行 t→v copy
- 額外處理 palette corruption crossing

### 相同點
- 都有延遲 t→v copy 機制
- 都只在第二次寫入後延遲

### 差異分析
| | AprNes | TriCNES | 影響 |
|---|---|---|---|
| 延遲值 | 固定 3 PPU dots | **alignment-dependent 4-5 cycles** | colorwin_ntsc 第三區（$2005/$2006）可能受影響 |

---

## 7. $2002 讀取（Status Register — VBL 清除）

### AprNes（`PPU.cs:1367-1386`）
- VBL flag suppression：scanline==nmiTriggerLine && cx==1 時返回 false
- 立即清除 `isVblank`
- 取消 `nmi_delay_cycle`（未 promote 的 NMI），不清 `nmi_pending`（已 promote 的）

### TriCNES
- 設定 `PPU_Read2002 = true` flag
- VBL 和 sprite flags 在特定半 cycle 取樣
- `PPUStatus_PendingSpriteZeroHit` 提供 1.5 dot delay

### 相同點
- VBL suppression 語義相同
- NMI 取消邏輯語義相同（未 promote 可取消，已 promote 不可取消）

### 差異分析
| | AprNes | TriCNES | 影響 |
|---|---|---|---|
| Sprite 0 hit | 即時讀取 | **1.5 dot pending delay** | AC test 已通過，差異可能不影響現有測試 |

---

## 8. $2007 讀寫（VRAM Access）

### AprNes（`PPU.cs:1389-1396, 1562+`）
- Read：6 PPU dots cooldown（`ppu2007ReadCooldown`）
- Write：立即寫入 + 立即 increment vram_addr

### TriCNES（`Emulator.cs:1322-1397, 9675-9718`）
- 複雜狀態機（`PPU_Data_StateMachine` 0-9）
- 支援 **mystery write**（RMW 指令造成的雙寫入）
- 地址 increment 在 state machine cycle 4

### 相同點
- 都有讀取冷卻機制
- 基本讀寫語義一致

### 差異分析
| | AprNes | TriCNES | 影響 |
|---|---|---|---|
| 模型 | 簡單 cooldown | **9-state 狀態機** | mystery write 場景不同（罕見） |

---

## 9. VBL Set/Clear Timing

### AprNes
- **VBL SET**：scanline 241, dot 1
- **Sprite flags RESET**：scanline 261, dot 1
- **VBL CLEAR**：scanline 261, dot 2
- **優化**：packed int（`scanline<<9 | cx`）比較

### TriCNES
- **VBL pending**：scanline 241, dot 0
- **VBL actual**：經過 half-cycle latch 後生效
- **Flags cleared**：scanline 261, dot 1

### 相同點
- VBL 設定在 scanline 241 附近
- Pre-render line (261) 清除 VBL 和 sprite flags

### 差異分析
| | AprNes | TriCNES | 影響 |
|---|---|---|---|
| VBL set dot | dot 1 | **dot 0** (pending) → half-cycle apply | ~0.5 dot 差異 |
| Clear dot | dot 2 | dot 1 | 1 dot 差異 |

**備註**：AC 全部通過，表示 VBL timing 在測試覆蓋範圍內是正確的。

---

## 10. Odd Frame Skip

### AprNes（`PPU.cs:705-715`）
- **偵測**：scanline 261, dot 339
- **跳過**：dot 340（cx increment trick）
- **條件**：`!oddSwap && (ShowBackGround || ShowSprites)`

### TriCNES（`Emulator.cs:1629-1642`）
- **偵測**：scanline 261, dot 340
- **跳過**：直接 wrap 到 scanline 0, dot 0
- **Toggle**：在 scanline 260, dot 340 切換 `PPU_OddFrame`

### 相同點
- 都只在 NTSC 模式且 rendering enabled 時觸發
- 都是每隔一幀跳過 1 dot

### 差異分析
| | AprNes | TriCNES | 影響 |
|---|---|---|---|
| 偵測 dot | 339 | 340 | 內部實作差異，結果相同 |

---

## 11. NMI Delay 模型

### AprNes（`MEM.cs:40-48, 106-107`）
- **Edge detection**：每次 PPU step 後，`nmi_output = isVblank && NMIable`
- **Rising edge** → 設定 `nmi_delay_cycle = cpuCycleCount`
- **Promotion**：`StartCpuCycle()` 中 `cpuCycleCount > nmi_delay_cycle` → `nmi_pending = true`
- **典型延遲**：2-3 CPU cycles

### TriCNES（`Emulator.cs:671-675`）
- **直接設定**：`NMILine |= PPUControl_NMIEnabled && PPUStatus_VBlank`
- **時機**：CPUClock==8（CPU cycle 的 2/3 處）
- **無額外延遲變數**

### 相同點
- 都是基於 VBL + NMI enable 的 rising edge
- `$2002` 讀取可取消未觸發的 NMI

### 差異分析
| | AprNes | TriCNES | 影響 |
|---|---|---|---|
| 延遲機制 | `nmi_delay_cycle` 變數 | CPUClock 固定位置 | 精確的 NMI → CPU 回應延遲略有不同 |

**備註**：ppu_vbl_nmi 10/10 PASS 表示 NMI timing 在測試範圍內正確。

---

## 12. Tile Fetch 8-Phase Pipeline

### AprNes（`PPU.cs:328-401`）
- **明確 8 phase**：`phase = cx & 7`
  - Phase 0: NT address setup, A12=0
  - Phase 1: NT fetch
  - Phase 2: AT address setup
  - Phase 3: AT fetch, attribute pipeline shift
  - Phase 4: CHR low address setup, A12 notification
  - Phase 5: CHR low fetch
  - Phase 6: CHR high address setup
  - **Phase 7: CHR high fetch → RenderBGTile(cx) → shift register reload → CXinc()**

### TriCNES
- 無明確 phase 抽象，tile data 在 shift register 函式中隱式 fetch
- `PPU_Render_CalculatePixel()` 每 dot 從 shift register 取像素
- Shift register 在 `_EmulateHalfPPU()` 更新

### 相同點
- 都遵循 NES PPU 的 8-cycle tile fetch 管線
- NT → AT → CHR low → CHR high 順序相同

### 差異分析
| | AprNes | TriCNES | 影響 |
|---|---|---|---|
| 渲染時機 | **Phase 7 批次渲染 8 pixels** | **每 dot 渲染 1 pixel** | AprNes 無法精確到單 pixel 的 mid-tile scroll 變化 |
| Shift register | Phase 7 reload | 每 dot shift | 同上 |

**實際影響**：這是 **最關鍵的架構差異**。AprNes 在 phase 7 一次渲染 8 個像素，如果 mid-tile（phase 0-6）發生了 scroll/pattern table 變化，這 8 個像素都用舊的設定。TriCNES 每 dot 渲染 1 pixel，可以精確反映 mid-tile 的寄存器變化。

---

## 13. Sprite Evaluation

### AprNes（`PPU.cs:562-593`）
- **AccuracyOptA=true** 時使用 per-dot FSM
- Dots 1-64: clear secondary OAM
- Dot 65: init + first tick
- Dots 66-256: per-dot tick
- Dot 256: end

### TriCNES（`Emulator.cs:1661-1665`）
- 每 PPU cycle 呼叫 `PPU_Render_SpriteEvaluation()`
- 內部用 `SpriteEvaluationTick` 狀態機
- 使用 `_Delayed` flag 版本

### 相同點
- 都是 per-dot 精度的 sprite evaluation
- Sprite overflow bug 模擬（byte offset cycling）

### 差異分析
| | AprNes | TriCNES | 影響 |
|---|---|---|---|
| Rendering enable flag | 即時 `ShowSprites` | `_Delayed`（1 cycle 額外延遲） | 極端邊界差異 |

---

## 14. A12 Notification（MMC3 IRQ）

### AprNes（`PPU.cs:328-401, 423-438`）
- BG: Phase 0 (A12=0), Phase 4 (A12=CHR addr bit12)
- Sprite: Phase 0 (A12=0), Phase 3 (A12=sprite CHR addr bit12)
- **明確 per-phase 通知**

### TriCNES（`Emulator.cs:1628`）
- `PPU_A12_Prev` 記錄上一 cycle 的 bit12
- 每 cycle 結束時偵測 0→1 transition
- 呼叫 `PPU_MapperSpecificFunctions()`

### 相同點
- 都是基於 A12 rising edge 觸發 MMC3 IRQ counter

### 差異分析
| | AprNes | TriCNES | 影響 |
|---|---|---|---|
| 通知方式 | Phase-specific，分 BG/Sprite | End-of-cycle transition 偵測 | A12 edge 發生的精確 dot 可能差 1 dot |

---

## 總結：關鍵差異清單

### 已確認不影響 AC/blargg 的差異
- VBL set/clear dot 位置（0.5-1 dot）
- NMI delay 機制（nmi_delay_cycle vs CPUClock）
- Odd frame skip 偵測 dot（339 vs 340）
- $2007 model（cooldown vs state machine）

### 可能影響 demo ROM 但不影響測試的差異
| 優先級 | 差異 | 影響的 ROM | 修復難度 |
|:------:|------|-----------|:--------:|
| **1** | **Phase 7 批次渲染 8 pixels**（vs per-dot） | scanline-a1, colorwin_ntsc | 高（架構重構） |
| **2** | **$2001 無 delay**（vs 2-3 cycle delay） | scanline-a1 星號 | 中（需隔離影響） |
| **3** | **$2005 無 delay**（vs 1-2 cycle delay） | colorwin_ntsc 右邊界 | 中 |
| **4** | **$2006 delay 3 dots**（vs 4-5 alignment-dependent） | mid-scanline scroll 精度 | 低 |
| **5** | **$2000 無 delay**（vs 1-2 cycle delay） | pattern table mid-scanline 切換 | 低 |

### 最大架構差異：Phase 7 批次渲染

這是兩者最根本的差異。AprNes 在 tile fetch 的 Phase 7 一次渲染 8 個像素到 `ScreenBuf1x`，而 TriCNES 每 dot 從 shift register 取 1 個像素。這意味著：

- 如果 `$2001`（ShowBG）在 Phase 2 被 toggle，AprNes 的 8 個像素全用舊狀態，TriCNES 只有 Phase 0-1 用舊狀態
- 如果 `$2005`（scroll）在 Phase 4 被改，AprNes 的 8 個像素全用舊 scroll，TriCNES 從 Phase 5 開始用新 scroll
- 如果 `$2000`（pattern table）在 mid-tile 被切換，AprNes 無法反映

要真正修復 demo ROM 差異，最終需要將 `RenderBGTile()` 從 phase-7 批次改為 **per-dot shift register → pixel output** 模型。但這是重大架構變更，需要確保 174/174 + 136/136 不回歸。

---

## 建議修復路徑

1. **短期（低風險）**：保持現狀。174/174 + 136/136 PERFECT 已是頂級精度。
2. **中期（中風險）**：加入 $2005 delay（1-2 cycle），可能修復 colorwin_ntsc 邊界問題。
3. **長期（高風險）**：重構 RenderBGTile 為 per-dot pixel output，全面對齊 TriCNES 精度。需要大量測試驗證。

---

## 修正歷史（feature/ppu-high-precision 分支）

### 2026-04-01 — master: ppuRenderingEnabled for BG fill + RenderBGTile（`3cf7e97`）
- **修正**：`cx==0` backdrop fill 和 `RenderBGTile` 呼叫條件從即時 `ShowBackGround` 改為延遲 `ppuRenderingEnabled`
- **效果**：修復 scanline-a1 大塊綠色（mid-scanline $2001 toggle 導致下一 scanline 被錯誤預填 backdrop）
- **殘留**：scanline-a1 第一區右側 5 個星號（$2001 delay 不足）
- **測試**：174/174 + 136/136 無回歸

### 2026-04-01 — master: $2005 scroll write delay 2 PPU dots（`6d3ce08`）
- **修正**：`ppu_w_2005` 改為延遲套用（`ppu2005UpdateDelay = 2`），在 `ppu_step_common` 中 countdown
- **效果**：修復 colorwin_ntsc 右邊界垂直彩色條紋（scroll 切換時機延遲對齊 TriCNES）
- **測試**：174/174 + 136/136 無回歸

### 2026-04-01 — master: 嘗試 $2001 delay 2-3 cycles（回退）
- **嘗試**：加入 `ppu2001UpdateDelay`，`ShowBackGround`/`ShowSprites` 延遲 2-3 cycle 更新
- **問題**：`renderingEnabled` 延遲影響 odd frame skip 和 OAM corruption，導致 `ppu_vbl_nmi/10-even_odd_timing` 失敗
- **結論**：$2001 delay 影響面太廣，需要 TriCNES 的四層 flag 系統（instant/delayed/eval-delayed）才能正確隔離。需要配合 per-dot pixel output 才有意義
- **動作**：完全回退，保持即時更新 + ppuRenderingEnabled 1-dot delay

### 2026-04-02 — feature/ppu-high-precision: half-step 架構拆分（`dbe3d62`）
- **修正**：`catchUpPPU_*()` 每 dot 改為 full-step + half-step 兩次呼叫
  - full-step（`ppu_step_*`）：tile fetch、sprite eval、delay countdowns、VBL/NMI
  - half-step（`ppu_half_step`）：per-dot BG pixel output from shift registers
- **架構變更**：
  - `RenderBGTile()` 剝離像素輸出，僅保留 palette cache 更新
  - `ppu_half_step()` 使用 `lowshift_s0` / `highshift_s0` per-dot shift registers 輸出 1 pixel
  - 為後續 $2001/$2005/$2000 的 half-dot 精度延遲奠定基礎
- **已知問題**：`highshift_s0` 的 `|1` 填充（sprite 0 hit 優化）可能影響 per-dot pixel 的 palette 精度 — 需要改用 main shift registers + phase 7 latch 或新增獨立的 rendering shift registers
- **測試**：174/174 + 136/136 無回歸（SMB3 等遊戲畫面正常）

### 2026-04-02 — feature/ppu-high-precision: per-dot pixel via main shift registers + latch（`36555c6`）
- **修正**：`ppu_half_step` 改用 `lowshift`/`highshift`（不是 `_s0` 的 `|1` 汙染版）
- Phase 7 pre-reload latch 確保最後像素用正確的 pre-reload 資料
- Per-dot attribute 選擇：`bit >= 8` → `bg_attr_p3`，`bit < 8` → `bg_attr_p2`

### 2026-04-02 — feature/ppu-high-precision: $2001 四層 flag 系統（`93086bf`）
- **修正**：Tier 1 `_Instant`（immediate）用於 odd frame / OAM / renderingEnabled
- Tier 2 `ShowBackGround`/`ShowSprites`（delayed 2 cycles）用於 pixel rendering / sprite compositing
- Tier 3 `ppuRenderingEnabled`（end-of-dot of Tier 1）用於 tile fetch
- **測試**：174/174 blargg PASS

### 2026-04-02 — feature/ppu-high-precision: $2000 delay（`3e4f5f7`）
- **修正**：pattern table / sprite size 延遲 2 PPU cycles，NMI enable 保持即時
- **測試**：174/174 blargg PASS

### 2026-04-02 — feature/ppu-high-precision: $2006 delay 3→4（`599076f`）
- **修正**：t→v copy 延遲從固定 3 改為 4 PPU dots（對齊 TriCNES 4-5 cycles）
- **測試**：174/174 blargg PASS，AC 135/136（Page 19 -1，不回退）

### 2026-04-02 — feature/ppu-high-precision: VBL latch + Sprite 0 pending（`d93a390`）
- **VBL**：full-step 設 `pendingVblank`，half-step promote → `isVblank`
- **Sprite 0 hit**：偵測設 `pendingSprite0Hit`，half-step promote → `isSprite0hit`（~0.5 dot delay）
- Guard + pre-render line 清除完整處理
- **測試**：174/174 blargg PASS

### 所有 TODO 完成 ✅

| 項目 | Commit | 狀態 |
|------|--------|:----:|
| highshift_s0 \|1 修正 → main shift + latch | `36555c6` | ✅ |
| $2001 四層 flag 系統 | `93086bf` | ✅ |
| $2000 delay (2 PPU cycles) | `3e4f5f7` | ✅ |
| $2006 delay 3→4 PPU cycles | `599076f` | ✅ |
| VBL half-dot latch | `d93a390` | ✅ |
| Sprite 0 hit pending delay | `d93a390` | ✅ |

### 當前狀態
- **blargg**: 174/174 PASS
- **AC**: 135/136（Page 19 -1，$2006 delay 變更相關，不回退）
- **架構**：half-step 完整運作，所有 PPU 寄存器延遲對齊 TriCNES 模型

### 2026-04-02 — feature/ppu-high-precision: alignment-dependent delays（`9dde4fb`）
- **修正**：所有寄存器延遲改為依 `ppu_cycles_x % 3` 動態調整
  - $2000: 2/1 cycles（alignment 0,1 vs 2,3）
  - $2001: 2/3 cycles（alignment 0,1,3 vs 2）
  - $2005: 1/2 cycles（alignment 0,1,3 vs 2）
  - $2006: 4/5 cycles（alignment 0,1,3 vs 2）
- AC Page 19 regression 分析記錄在 `AC_Page19_Regression_Analysis.md`
- **測試**：174/174 blargg PASS

### 2026-04-02 — feature/ppu-high-precision: $2007 state machine 評估（placeholder）
- **結論**：TriCNES 的 9-state $2007 state machine 需要重構 MEM.cs 的 `ppu_read_fun`/`ppu_write_fun` 架構
- 現有 lambda 將 $2007 register 行為（buffer swap、increment、openbus）嵌入 PPU address dispatch
- 正確實作需將 raw VRAM access 與 $2007 register 行為分離
- 已加入變數宣告和 placeholder 註解，待 MEM.cs 重構後啟用
- **影響**：mystery write（RMW 指令讀 $2007）和 back-to-back $2007 access 精度
- **現行保持**：原有 lambda 行為（立即 buffer + increment），cooldown 已移除

### 2026-04-02 — feature/ppu-high-precision: per-dot sprite compositing（`d114be8`）
- **修正**：RenderSpritesLine 移至 cx==0（填 sprite buffer），Pass 3 compositing 移至 half-step per-dot
- Static sprite buffers（sprLineBuf/Pri/Set/PalIdx）取代 stackalloc
- NTSC DecodeScanline 在 cx==255 half-step 觸發
- **測試**：174/174 blargg PASS，SMB3 畫面正常

### 2026-04-02 — feature/ppu-high-precision: backdrop fill + $2007 cooldown restore（`c41e5a1`）
- **修正**：cx==0 無條件填 backdrop（修復 mid-scanline $2001 toggle 導致的 stale pixel 綠色方塊）
- 恢復 ppu2007ReadCooldown（double_2007_read 依賴）
- scanline-a1 完全對齊 TriCNES（數位+類比模式均無綠色）
- **測試**：174/174 blargg PASS

### 2026-04-02 — feature/ppu-high-precision: MEM.cs lambda 重構（`27b36f8`）
- **修正**：移除 65536-entry lambda 陣列，新增 PpuBusRead/PpuBusWrite
- **測試**：174/174 blargg PASS

### 2026-04-02 — feature/ppu-high-precision: $2007 state machine（`aae4655`）
- **修正**：deferred buffer (state 1/4) + write (state 3) + increment (state 4)
- SM 在 full-step + half-step 各 tick 一次（半 dot 精度）
- 移除 ppu2007ReadCooldown（由 SM 流程取代）
- **改善**：test_ppu_read_buffer + read_write_2007 從 FAIL → PASS
- **殘留**：double_2007_read（DMC back-to-back $2007，需 SM interrupt 處理）
- **測試**：173/174 blargg

### 2026-04-02 — feature/ppu-high-precision: $2007 SM immediate write+increment + guard（`e893726`, `46cb68d`）
- **修正**：write 和 increment 回復即時（deferred 會破壞連續 $2007 存取模式）
- SM 僅管理 buffer deferred update（state 1/4）
- 新增 back-to-back read guard：SM < 9 時返回 openbus
- DMA stolen tick `ppu2007SM = 9` 重置允許 DMA 後的正常讀取
- **測試**：174/174 blargg PASS（double_2007_read 修復）

### 後續方向
- [ ] mystery write（RMW 指令 $2007 — 極罕見場景）
- [ ] 排查 AC Page 19 regression
