# 第 4 部：PPU（Page 16–19 等）

> 對應 page：**PPU RAM / Palette RAM / PPU Reset Flag**、**PPU Register Mirroring / Open Bus / Read Buffer / Palette RAM Quirks / Rendering Flag / $2007 read w/ rendering**、**VBlank/NMI 系列**、**Sprite Evaluation / Sprite 0 Hit / OAM Corruption / Misaligned OAM / $2004 / Suddenly Resize Sprite / Arbitrary Sprite Zero**、**Attributes As Tiles / t Register Quirks / Stale BG & Sprite Shift Registers / BG Serial In / Sprites On Scanline 0 / $2004 & $2007 Stress**。
> 前置：[`00_timing_model.md`](00_timing_model.md)（**PPU half-step、VBL/NMI 1-cycle delay** 是這一整篇的前提）。

PPU 是整個 AC 最大、也最吃 **dot / 半-dot 精度**的部分。CPU/APU/DMA 頁多半在 cycle 級就能解，PPU 頁卻常常在驗「同一個 PPU dot 內、半個 dot 的先後」。這正是為什麼我們的主迴圈在一個 CPU cycle（12 master clock）裡塞了 `ppu_step_new`（MC 0/4/8）+ `ppu_half_step_new`（MC 2/6/10）—— 半步就是為了 PPU 這些題目而存在。

---

## 1. VBlank / NMI 時序（最先要站穩的）

**測試**：VBlank beginning / end、NMI Control / Timing / Suppression / at VBlank end / disabled at VBlank。

三個硬體事實：
1. **VBL flag 在 scanline 241 dot 1 set、在 pre-render line（261）dot 1 clear**。差一個 dot 就一票測試掛。
2. **NMI 是邊緣觸發 + 1-cycle delay**：VBL flag 與 `$2000` bit7（NMI enable）的 AND 上升沿 → `nmi_delay` → 下個 tick 升 `nmi_pending` → CPU 檢查。
3. **NMI suppression**：在 VBL flag set 的「那個 dot 前後」讀 `$2002`，會抑制這一幀的 NMI（讀 `$2002` 清 `nmi_delay`，可取消；但不清 `nmi_pending`，不可逆）。

這套 1-cycle delay 模型在 blargg 階段就建立了（當年 139→154 的關鍵跳），細節見 [`00_timing_model.md`](00_timing_model.md) §4。PPU 頁的 NMI 系列就是把它逼到極限。

---

## 2. `$2002` Flag Clear Timing Stagger —— 半-dot 精度的代表作

**測試（$2002 flag timing）**：sprite flags（sprite 0 hit、sprite overflow）看起來會比 VBL flag **早約 2 個 PPU dot 清除**。

**硬體真相**（[BUGFIX45](../../bugfix/2026-03-07_BUGFIX45.md)）：讀 `$2002` 時，**VBL flag 在 M2 上升沿取樣、sprite flags 在 M2 下降沿取樣**。RP2A03G 的 M2 duty cycle 是 **15/24**，所以 sprite flags 比 VBL 晚讀約 1.875 PPU dot —— 反過來看，就是 sprite flags「看起來」比 VBL 早約 2 dot 清掉。

**修法**：把 pre-render line 的 flag 清除拆成兩個 dot：
- **dot 1**：清 `isSprite0hit` + `isSpriteOverflow`
- **dot 2**：清 VBL flag

> 這題是「為什麼非要半-dot 精度」的最佳教材：三個 flag 不能在同一個 dot 一起清，差的就是 M2 duty cycle 造成的 ~2 dot。沒有 sub-dot 模型，這題無解。

---

## 3. `$2007` Read Buffer / 渲染期存取 / `$2006` 延遲 t→v copy

### Read buffer
讀 `$2007`（non-palette 區）回傳的是**上一次**讀的 buffered 值，這次讀的值先進 buffer。palette 區則直接回傳 + 同時更新 buffer（讀的是 nametable 底下的 mirror）。

### 渲染期存取
rendering 開著時讀寫 `$2007` 會用「渲染用的 v 暫存器」並觸發詭異的 address increment（coarse X + Y 同時動）。測試 `$2007 read w/ rendering`、`$2004 read/write during rendering` 專驗這些。

### `$2006` 延遲 t→v copy（會影響真實遊戲！）
**硬體真相**（[BUGFIX57](../../bugfix/2026-03-23_BUGFIX57_PPU2006_Delayed_Copy.md)）：CPU 對 `$2006` 第二次寫入後，`t→v` copy **不是立刻**生效，而是延遲約 **4–5 PPU dots**（PPU 內部匯流排傳播訊號需要時間）。

我們原本立即 copy → **洛克人 5 電梯關卡的平台每幀上下抖 1 scanline**。改成延遲 copy 後平穩。

> 這個坑特別值得講：它**不只影響 AC，還影響真實遊戲**。很多人以為「過了 AC 就萬事 OK」，但 AC 136/136 之後我們才靠實機畫面（洛克人 5、`scanline-a1`、`colorwin_ntsc`）抓到 PPU timing 還有精度不足 —— 這就是當初決定[整套換成 TriCNES per-master-clock 模型](00_timing_model.md#2-aprnes-的演進三代-timing-模型)的導火線。`$2005` scroll write 也有類似的 2-dot 延遲（後來照 TriCNES model 補上）。

---

## 4. Palette RAM Quirks

**測試（Palette RAM Quirks）**：
- `$3F10/$3F14/$3F18/$3F1C` 是 `$3F00/$3F04/$3F08/$3F0C` 的 mirror（背景色共用）。
- grayscale mask（`$2001` bit0）讓讀 palette 時 `& $30`。
- palette RAM 的 open bus 行為（讀 palette 時 data bus 只更新低 6 bit，上 2 bit 維持 open bus）。

這些是查表 + mask 邏輯，不太吃 cycle，但 mirror 位址要對。

---

## 5. Sprite Evaluation / Sprite 0 Hit / OAM Corruption

這是 PPU 頁工程量最大的一塊，全部建立在 **secondary OAM + per-dot sprite evaluation FSM** 上。

- **Sprite evaluation FSM**：visible scanline 的 dots 1–256 做 sprite evaluation（掃 primary OAM、把 in-range 的塞進 secondary OAM），dots 257–320 取 sprite tile data。這必須**逐 dot** 跑狀態機，不能一次算完。
- **Sprite 0 hit**：sprite 0 的不透明 pixel 撞上背景不透明 pixel 的**精確 dot**才 set flag（含 x=255 不觸發、dot 0 不觸發等 quirk）。
- **OAM Corruption**（[BUGFIX36](../../bugfix/2026-03-07_BUGFIX36.md)）：rendering 在特定 dot enable/disable 時，OAM 會被硬體 bug 破壞 —— 要模擬這個「壞法」。
- **$2004 read during sprite evaluation**（[BUGFIX41](../../bugfix/2026-03-07_BUGFIX41.md)）：渲染中讀 `$2004` 回傳的是「evaluation 當下指到的 OAM buffer 值」，不是靜態 OAM。
- **Suddenly Resize Sprite**（[BUGFIX42](../../bugfix/2026-03-07_BUGFIX42.md)）：sprite size（8x8/8x16）在 CHR fetch 的特定 dot 才 latch —— 在 scanline 中途改 `$2000` sprite size 會有過渡行為。
- **Sprites On Scanline 0**（[BUGFIX47](../../bugfix/2026-03-08_BUGFIX47.md)）：pre-render line（261）的 dots 257–320 用 `(261 & 255) = 5` 當有效 scanline 做 in-range 檢查；secondary OAM 還留著前一條 visible line（239）的結果。若有 sprite 落在 scanline 5 範圍，它的 tile data 會載入 shift register 並延續到 scanline 0。

> 這些全部要求「secondary OAM 是個真的 buffer，sprite evaluation 是個逐 dot 推進的狀態機」。我們有個 `AccuracyOptA` 開關控制 per-dot secondary OAM FSM（效能 vs 精度）—— 跑 AC 驗證時強制開（headless 預設 on）。

---

## 6. Shift Registers（Stale BG / Sprite + Rendering Flag）

- **Stale BG Shift Registers**（[BUGFIX40](../../bugfix/2026-03-07_BUGFIX40.md)）/ **Rendering Flag Behavior**（[BUGFIX43](../../bugfix/2026-03-07_BUGFIX43.md)）：rendering 關掉時，BG shift register **凍結**（不再 shift、不再 reload），重開時還是舊值。`BG Serial In`、`Attributes As Tiles` 等進階題就靠在「rendering 開/關的精確 dot 凍結/解凍 shift register」做出視覺花樣。
- **Stale Sprite Shift Regs**：類似，但針對 sprite shift register（最近 AC 20260521 還重排了它的 in-range 清除時機，見[版本差異](../../notes/AccuracyCoin_20260521_diff_and_result.md)）。
- **t Register Quirks**：`$2005`/`$2006`/`$2000` 對內部 `t` 暫存器各 bit 的寫入時機。

---

## 小結

PPU 頁的母題：**很多事件發生在「半個 dot」的精度上，而且 latch 更新有延遲。**

- VBL/NMI：1-cycle delay + 邊緣觸發 + suppression。
- `$2002` flag：sprite flags 比 VBL 早 ~2 dot（M2 duty cycle）。
- `$2006`/`$2005`：t→v / scroll 更新延遲 4–5 / 2 dot。
- Sprite：secondary OAM + 逐 dot evaluation FSM + 精確 sprite 0 hit dot。
- Shift register：rendering 開/關的精確 dot 凍結/解凍。

這些就是主迴圈為何要有 `ppu_half_step_new`（半-dot）的原因。**PPU 頁過不了，幾乎都是 timing 模型粒度不夠 —— 不是某個渲染公式錯。** 也因此，PPU 是當年從 v1 136/136 推進到對齊 TriCNES per-master-clock、達 v2 138/138 的主戰場。

下一篇（附錄）：[`appendix_error_code_index.md`](appendix_error_code_index.md)（各頁 error code 速查）、[`appendix_tricnes_reference.md`](appendix_tricnes_reference.md)（TriCNES 當 ground truth + 它的已知錯誤）。
