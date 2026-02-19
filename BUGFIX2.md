# AprNes Bug 修復紀錄（第二輯）

本文件接續 `BUGFIX.md`（Bug 1–9），記錄後續 session 修復的 Bug 10–15。
所有修復均已 commit 至 master 分支。

---

## 目錄

10. [UpdateVramRegister 條件未包含 ShowSprites](#10-updatevramregister-條件未包含-showsprites)
11. [背景關閉時精靈優先權判斷使用過時的 Buffer_BG_array](#11-背景關閉時精靈優先權判斷使用過時的-buffer_bg_array)
12. [Cycle-accurate PPU：BG 屬性 Ring Buffer 每條掃描線讀寫數量不匹配](#12-cycle-accurate-ppubg-屬性-ring-buffer-每條掃描線讀寫數量不匹配)
13. [Cycle-accurate PPU：scanline = -1 初始值造成 ScreenBuf1x 越界寫入](#13-cycle-accurate-ppuscanline---1-初始值造成-screenbuf1x-越界寫入)
14. [Cycle-accurate PPU：MMC3 IRQ 在 PPU cycle 4 觸發而非 cycle 260](#14-cycle-accurate-ppummc3-irq-在-ppu-cycle-4-觸發而非-cycle-260)

---

## 背景說明：Cycle-Accurate PPU 重寫（commit 24687f0）

為修復 SMB3 蘑菇精靈穿透 "?" 磚頭的 CHR bank 時序問題，將舊的批次渲染
（在掃描線 cycle 254 一次輸出整行像素）改為 cycle-accurate 逐 8-cycle tile 讀取：

- **BG tile 讀取時序**：每 8 個 PPU cycles 為一組（phase 0–7），在 phase 4/6 設定
  CHR 地址，phase 5/7 讀取 CHR 資料，phase 7 渲染並載入 shift register。
- **16-bit shift register**（`lowshift`/`highshift`）：高位元組 = 當前 tile，低位元組 = 下一個 tile。
- **A12 偵測**：改用實際 CHR 地址的 bit 12 觸發 MMC3 IRQ（後因時序問題再次調整，見 Bug 14）。
- **Mapper 003/004 的精靈優先權**：通過正確 CHR bank 讀取時機，讓精靈與背景的
  遮蔽關係符合 NES 硬體行為。

重寫後引入了 Bug 12、13、14，均在後續 commit 中修復。

---

## 10. UpdateVramRegister 條件未包含 ShowSprites

**Commit**：`718b085`

### 現象
部分僅開啟精靈渲染（`ShowSprites=true`）但關閉背景渲染（`ShowBackGround=false`）的場景，
VRAM 地址（Y increment + hori copy）未更新，導致後續掃描線的捲軸地址錯誤，畫面滾動異常。

### 根本原因
`PPU.cs` 的 `ppu_step()` 中，cycle 256 呼叫 `UpdateVramRegister()`（執行 Y increment 與 hori copy）
的條件只檢查 `ShowBackGround`：

```csharp
// 修復前
else if (ppu_cycles_x == 256 && ShowBackGround) UpdateVramRegister();
```

NES 硬體規格：只要渲染**任一**開啟（背景**或**精靈），PPU 都會在 cycle 256 執行 Y increment
與 cycle 257 執行 hori copy。條件少了 `ShowSprites`。

### 影響位置
`PPU.cs`：`ppu_step()` cycle 256 的 `UpdateVramRegister()` 呼叫條件。

### 修復方式
```csharp
// 修復後
else if (ppu_cycles_x == 256 && (ShowBackGround || ShowSprites)) UpdateVramRegister();
```

---

## 11. 背景關閉時精靈優先權判斷使用過時的 Buffer_BG_array

**Commit**：`718b085`

### 現象
`ShowBackGround=false`（背景渲染關閉）時，priority=1（後景精靈）的精靈應該能顯示在
所有位置（因為沒有背景像素可以遮蔽它），但實際上部分精靈消失不見。

### 根本原因
```csharp
// 修復前
if (pixel != 0 && (Buffer_BG_array[array_loc] == 0 || !priority))
    ScreenBuf1x[array_loc] = ...;
```

當 `ShowBackGround=false` 時，`Buffer_BG_array` 仍保留**上一幀**的數據（或全零，視背景是否
曾被填充）。NES 硬體規格：背景關閉時，背景像素視為全透明（pixel=0），因此 priority=1
的精靈應無條件顯示，不應讀取 `Buffer_BG_array`。

### 影響位置
`PPU.cs`：`RenderSpritesLine()` 中精靈輸出的條件判斷。

### 修復方式
```csharp
// 修復後：加入 !ShowBackGround，背景關閉時不參考 Buffer_BG_array
if (pixel != 0 && (!ShowBackGround || Buffer_BG_array[array_loc] == 0 || !priority))
    ScreenBuf1x[array_loc] = ...;
```

完整判斷邏輯：
| 條件 | 結果 |
|------|------|
| pixel = 0 | 不繪製（透明像素） |
| 背景關閉（`!ShowBackGround`） | 直接繪製精靈 |
| 背景像素為透明（`Buffer_BG_array == 0`） | 直接繪製精靈 |
| priority = 0（前景精靈） | 直接繪製精靈 |
| priority = 1（後景精靈）且背景不透明 | **不繪製**（被背景遮蔽） |

---

## 12. Cycle-Accurate PPU：BG 屬性 Ring Buffer 每條掃描線讀寫數量不匹配

**Commit**：`be3f979`

### 現象
Cycle-accurate PPU 實作後，畫面大量花屏：背景 tile 的調色盤（attribute）完全錯誤，
色彩一片混亂，下半部畫面尤為嚴重，且問題隨時間累積惡化。

### 根本原因
原始實作使用 4-slot ring buffer（`bg_at_queue[4]`）延遲 BG tile 屬性 2 個 fetch group：

```
Phase 3 寫入：bg_at_queue[bg_at_q_wr++ & 3] = ATVal
Phase 7 讀取：bg_at_queue[bg_at_q_rd++ & 3]
```

**每條掃描線的寫入次數 ≠ 讀取次數**：

| 區段 | 事件 | 次數 |
|------|------|------|
| Cycle 0–255（32 個 tile）| Phase 3 寫入 | **32** 次 |
| Cycle 320–335（2 個預取 tile）| Phase 3 寫入 | **2** 次 |
| Phase 7 渲染（只在 cycle < 256） | Phase 7 讀取 | **32** 次 |
| **合計** | 寫入 34、讀取 32 | **+2 偏移/掃描線** |

每條掃描線結束後 `q_wr - q_rd` 增加 2。當差值超過 4（ring buffer 大小）時，
後續讀取槽的內容是**幾條掃描線前的過時屬性**，造成調色盤系統性錯誤。

**具體追蹤（第 0 條掃描線結束後）**：
- `q_wr = 36`，`q_rd = 32`（差值 = 4，ring buffer 全被舊資料佔滿）
- 第 1 條掃描線第一次讀取：`q_rd=32 → slot 32&3=0` → 讀到 tile32_attr（預取資料），
  而不是 scan1_tile0_attr

### 影響位置
`PPU.cs`：`bg_at_queue`、`bg_at_q_wr`、`bg_at_q_rd` 欄位，以及
`ppu_rendering_tick()` case 3 與 `RenderBGTile()`。

### 修復方式
以 **3-stage pipeline** 取代 ring buffer：

```csharp
// 修復前：ring buffer（每掃描線讀寫不平衡）
static byte[] bg_at_queue = new byte[4];
static int bg_at_q_wr = 0, bg_at_q_rd = 0;

// 修復後：3 段流水線（自動對齊，無索引偏移問題）
static byte bg_attr_p1 = 0, bg_attr_p2 = 0, bg_attr_p3 = 0;
```

**Phase 3**（無條件，每個 tile fetch group 都執行）：
```csharp
bg_attr_p3 = bg_attr_p2; bg_attr_p2 = bg_attr_p1; bg_attr_p1 = ATVal;
```

**Phase 7 渲染**：
```csharp
byte renderAttr = bg_attr_p3;  // 2 個 fetch group 前讀到的屬性（對應 shift reg 高位元組）
byte nextAttr   = bg_attr_p2;  // 1 個 fetch group 前讀到的屬性（對應 shift reg 低位元組）
```

**正確性驗證（以掃描線 0 為例）**：
1. 上條掃描線預取 tile0（phase 3）→ `p1=attr0`
2. 上條掃描線預取 tile1（phase 3）→ `p3=old, p2=attr0, p1=attr1`
3. 本掃描線 cycle 3（fetch tile2）→ `p3=attr0, p2=attr1, p1=attr2`
4. 本掃描線 cycle 7 渲染 tile0：`renderAttr=p3=attr0` ✓，`nextAttr=p2=attr1` ✓

Pipeline 在掃描線邊界也能自我修正：pre-render scanline（261）的
cycles 0–255 會寫入 32 次雜訊，但隨後 cycles 320–335 的 2 次預取
會正確覆寫 p2/p3，使下一條可見掃描線得到正確屬性。

---

## 13. Cycle-Accurate PPU：scanline = -1 初始值造成 ScreenBuf1x 越界寫入

**Commit**：`be3f979`

### 現象
模擬器啟動（加載 ROM）後，部分情況下立即崩潰或出現記憶體損毀，
畫面在第一幀就有大量雜訊或 crash。

### 根本原因
`PPU.cs` 中 `scanline` 靜態欄位初始值為 `-1`（NES 上電後 PPU 從 pre-render 前開始）：

```csharp
static public int ppu_cycles_x = 0, scanline = -1;
```

Cycle-accurate PPU 的 `ppu_step_new()` 中，背景關閉時的填充迴圈判斷條件為：

```csharp
// 修復前（條件錯誤）
if (scanline < 240)
{
    if (ppu_cycles_x == 0 && !ShowBackGround)
    {
        int scanOff = scanline << 8;  // = (-1) << 8 = -256 ！！
        for (int i = 0; i < 256; i++)
        {
            ScreenBuf1x[scanOff + i] = bgColor;  // 寫入 ScreenBuf1x[-256+i]，越界！
            Buffer_BG_array[scanOff + i] = 0;
        }
    }
    ...
}
```

`-1 < 240` 為真，且系統上電時 `ShowBackGround=false`，觸發迴圈。
`(-1) << 8 = -256`，對 `ScreenBuf1x[-256]` 寫入，越界存取未分配記憶體，
造成記憶體損毀。

### 影響位置
`PPU.cs`：`ppu_step_new()` 中背景填充迴圈與精靈渲染觸發的外層條件判斷。

### 修復方式
```csharp
// 修復後：加入 scanline >= 0 下界檢查
if (scanline >= 0 && scanline < 240)
{
    if (ppu_cycles_x == 0 && !ShowBackGround)
    {
        int scanOff = scanline << 8;  // scanline 必定 >= 0，安全
        ...
    }
    if (ppu_cycles_x == 257)
        RenderSpritesLine();
}
```

`scanline = -1` 只存在於模擬器剛啟動、第一個 PPU tick 執行前的一瞬間，
正常遊戲執行期間掃描線在 0–261 之間循環，不會觸及這個邊界條件。

---

## 14. Cycle-Accurate PPU：MMC3 IRQ 在 PPU cycle 4 觸發而非 cycle 260

**Commit**：`9375bd0`

### 現象
Cycle-accurate PPU 重寫後，SMB3 等 MMC3 遊戲畫面下半部（狀態列區域，
約 scanline 192–239）出現嚴重花屏：捲軸分割位置偏差、CHR bank 切換時機錯誤，
表現為下半部 tile 圖案完全錯亂。

### 根本原因
**原始批次渲染的 IRQ 時序**：在每條掃描線的 `ppu_cycles_x == 260`（sprite fetch
區段中間）呼叫 `Mapper04step_IRQ()`，此時機對應 NES 硬體的 A12 上升沿計數點。

**Cycle-accurate 版本的錯誤實作**：使用 `Clock_A12()` 偵測 CHR 地址 bit 12 上升沿，
呼叫點在 BG tile fetch 的 **phase 4**（每條掃描線 cycle 4 = 第一個 tile 的第 4 個 cycle）：

```csharp
case 4:
    ioaddr = BgPatternTableAddr | (NTVal << 4) | ((vram_addr >> 12) & 7);
    Clock_A12(ioaddr);  // A12 從 0→1 → 立即觸發 IRQInterrupt()！
    break;
```

**時序差異對 CPU 的影響**：

`IRQInterrupt()` 是**同步呼叫**，立即修改 CPU 的 PC 和 SP：
```csharp
public static void IRQInterrupt()
{
    // 直接 push PC、跳到 IRQ vector，CPU 下一步就執行 IRQ handler
    r_PC = (ushort)(Mem_r(0xfffe) | (Mem_r(0xffff) << 8));
    Interrupt_cycle = 7;
}
```

| 方案 | IRQ 觸發 PPU cycle | handler 開始（+7 CPU × 3） | $2005/$2006 寫入時機 |
|------|-------------------|--------------------------|----------------------|
| 原始（cycle 260）| cycle 260 | cycle ≈ 281 | scanline 邊界附近 |
| 錯誤（cycle 4）  | cycle 4   | cycle ≈ 25  | **同一條掃描線中途** |

IRQ 提前 **256 個 PPU cycles**（≈ 85 個 CPU cycles）觸發，使遊戲的 IRQ handler
在掃描線**中途**寫入捲軸暫存器（`$2005`/`$2006`），而非在掃描線邊界。
SMB3 的畫面分割效果依賴在正確時機才寫入，中途寫入導致下半部水平捲軸錯位花屏。

**額外問題**：`Clock_A12` 在 sprite 起始點（cycle 257）使用：
```csharp
Clock_A12(Spritesize8x16 ? 0 : SpPatternTableAddr);
```
對 8x16 精靈傳入 `0`（A12=0），而實際上部分 8x16 精靈 tile 會存取 0x1000 區段（A12=1），
此近似值在不同 ROM 下會造成 IRQ 計數不穩定。

### 影響位置
`PPU.cs`：
- `Clock_A12()` 函式本身
- `ppu_rendering_tick()` case 4、case 6 中的 `Clock_A12()` 呼叫
- `ppu_step_new()` cycle 257 的 `Clock_A12()` 呼叫

### 修復方式
移除 `Clock_A12()` 及其所有呼叫，恢復原始的 **cycle 260 固定觸發**方案：

```csharp
// 修復後（ppu_step_new 內）

// 可見掃描線（0–239）
if (scanline >= 0 && scanline < 240)
{
    ...
    // MMC3 IRQ：每條可見掃描線在 cycle 260 計時一次
    if (ppu_cycles_x == 260 && renderingEnabled && mapper == 4)
        (MapperObj as Mapper004).Mapper04step_IRQ();
}

// Pre-render 掃描線 261 同樣計時（與原始 ppu_step 一致，見 Bug 9）
if (scanline == 261 && ppu_cycles_x == 260 && renderingEnabled && mapper == 4)
    (MapperObj as Mapper004).Mapper04step_IRQ();
```

**CHR tile 讀取時序不受影響**：BG tile 的 `MapperR_CHR()` 呼叫仍在 phase 5/7 的
正確 PPU cycle 執行，與 IRQ 觸發時機無關，cycle-accurate 的優勢完整保留。

### 為何不繼續用 A12 偵測？
A12 偵測的根本問題是 `IRQInterrupt()` 為同步呼叫，沒有「延遲到掃描線邊界再投遞」
的機制。要真正正確地在 cycle 260 附近觸發 IRQ，需要引入 IRQ 排程機制（pending IRQ
在下一個 CPU 指令邊界才被處理）。在目前架構下，最簡單且等效正確的方式是回到
固定 cycle 260 觸發。

---

## 修復對應 commit 彙整

| Bug # | Commit | 說明 |
|-------|--------|------|
| 10 | `718b085` | `UpdateVramRegister` 條件加入 `ShowSprites` |
| 11 | `718b085` | 精靈優先權加入 `!ShowBackGround` 條件 |
| — | `24687f0` | **Cycle-accurate PPU 重寫**（引入 Bug 12–14）|
| 12 | `be3f979` | BG 屬性 ring buffer → 3-stage pipeline |
| 13 | `be3f979` | scanline = -1 OOB 條件改為 `>= 0 && < 240` |
| 14 | `9375bd0` | MMC3 IRQ 改回 cycle 260 固定觸發 |

---

## 修復對應檔案彙整

| # | 檔案 | 函式/位置 | 修復內容 |
|---|------|-----------|----------|
| 10 | `PPU.cs` | `ppu_step()` cycle 256 | `ShowBackGround` → `ShowBackGround \|\| ShowSprites` |
| 11 | `PPU.cs` | `RenderSpritesLine()` 輸出條件 | 加入 `!ShowBackGround` |
| 12 | `PPU.cs` | `ppu_rendering_tick()` case 3、`RenderBGTile()` | ring buffer → 3-stage pipeline |
| 13 | `PPU.cs` | `ppu_step_new()` 掃描線範圍檢查 | `scanline < 240` → `scanline >= 0 && scanline < 240` |
| 14 | `PPU.cs` | `Clock_A12()`、`ppu_step_new()` | 移除 A12 偵測，恢復 cycle 260 固定觸發 |
