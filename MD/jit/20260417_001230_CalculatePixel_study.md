# CalculatePixel — Algorithmic Optimization Study (STUDY ONLY, NO CHANGES)

- **Date**: 2026-04-17 00:12
- **Target**: `CalculatePixel` block inside `ppu_step_new()` (lines 234-305)
- **Purpose**: 評估 algorithmic 優化空間 + 風險，**不改任何 code**
- **Current file**: `AprNes/NesCore/ppu_new.cs`

---

## 1. 現況結構

```csharp
// [1] 可見 scanline gate
if (scanline < 240)
{
    // [2] Backdrop default
    byte backdropIdx = ppu_ram[0x3f00] & 0x3f;
    uint compositeColor = palCache[0];
    byte compositePalIdx = backdropIdx;
    int bgColor = 0, bgPalette = 0;

    // [3] BG pixel extract — shift register bit pick
    if (showBG && (cx > 8 || ShowBgLeft8)) { ... 6 ops (shift/AND/OR) ... }

    // [4] Sprite mux (SWAR, de-Bruijn magic multiply)
    if (showSpr && ... && spriteAnyActive) {
        ulong xc = *(ulong*)sprXCounter;
        ulong has_bits = ((xc & 0x7F..) + 0x7F..) | xc;
        ulong active_mask = skippedPreRenderDot341 ? 0x80.. : (~has_bits & 0x80..);
        ulong pixel_mask = (sprShiftH | sprShiftL) & 0x80..;
        ulong valid = active_mask & pixel_mask;
        if (valid != 0) {
            ulong lowest = valid & -(long)valid;
            int i = (0x0001020304050607UL * (lowest >> 7)) >> 56;  // ← hot candidate
            // ... sprite 0 hit + priority compose ...
        }
    }

    // [5] Palette corruption (rare)
    if (ppuPaletteCorruptionFromVChange | ppuPaletteCorruptionFromDisable) { CorruptPalettes(...); }

    // [6] Final palette lookup
    if (showBG || showSpr) { pa = (bgPalette << 2) | bgColor; ... palCache[pa] ... }
    else if ((vram_addr & 0x3F1F) >= 0x3F00) { ... }  // v-reg in palette range edge case

    dotColor = compositeColor;
    dotPalIdx = compositePalIdx;
}
```

**熱度：** 可見 scanline × cx 1-256 = 240 × 256 = **61,440 次/frame × 60 fps = 3.69M 次/秒**

---

## 2. 優化候選清單

### ✅ A. 用 `BitOperations.TrailingZeroCount` 取代 de-Bruijn 魔法乘法

**目前寫法（第 274 行）：**
```csharp
ulong lowest = valid & (ulong)(-(long)valid);   // 隔離最低 bit
int i = (int)((0x0001020304050607UL * (lowest >> 7)) >> 56);  // 3 ops: mul + shift
```

**提議：**
```csharp
int lowBit = BitOperations.TrailingZeroCount(valid);  // 0..63
int i = lowBit >> 3;  // 0..7
```

**正確性驗證：**  
`valid` 的 set bit 只會在位置 `8k+7`（k=0..7，因為 active_mask 和 pixel_mask 都 `& 0x80...UL`）
- k=0: `TZC=7`,  `7>>3=0` ✓
- k=1: `TZC=15`, `15>>3=1` ✓
- k=7: `TZC=63`, `63>>3=7` ✓
✅ 100% 等價

**效能：**
- `BitOperations.TrailingZeroCount` 在 .NET 10 下遇到 **BMI1** 硬體（Zen 2+, Haswell+）編譯成 `tzcnt`（1 cycle, latency 3）
- 無 BMI1 fallback 為 branch-heavy software 迴圈（很慢）
- 現用 de-Bruijn: `mul` + `shr` ≈ 4-5 cycles
- **淨省 ~2-3 cycles/call × 30-50% sprite-active rate × 3.69M calls/s ≈ 2-4M cycles/s = 0.05-0.1% CPU**

**風險：** 極低（純數學等價，BMI1 在目標硬體皆有）

**推薦：🟢 最安全、收益最小的確定勝率**

---

### 🟡 B. BG pixel extract 用 BMI1 `BEXTR`

**目前寫法：**
```csharp
int bit = 15 - FineX;
bgColor = (((renderHigh >> bit) & 1) << 1) | ((renderLow >> bit) & 1);
int ab = 7 - FineX;
bgPalette = (((renderAttrHigh >> ab) & 1) << 1) | ((renderAttrLow >> ab) & 1);
```

**提議（.NET 10 BMI1）：**
```csharp
// BEXTR(value, start, length): extract `length` bits starting at `start`
// 需要把 2 個 shift register 的同一 bit 併成一個 2-bit value
```
實際上 BMI1 `BEXTR` 抽單 bit 不比 `(x >> n) & 1` 快。這段已經是 RyuJIT 能產出 `bt` 指令的標準形式。

**效能：** 預估 0（或微負）。RyuJIT 已生成近乎最佳 code。

**風險：** 低

**推薦：❌ 跳過**（不是真的 win）

---

### ❌ C. 把整個 `active_mask` / `pixel_mask` 計算改 SSE2 向量

**想法：**
```csharp
Vector128<byte> vXc = Vector128.LoadUnaligned((byte*)sprXCounter);
Vector128<byte> vEqZero = Sse2.CompareEqual(vXc, Vector128<byte>.Zero);  // 0xFF where counter==0
```

**問題：**
- `sprXCounter` 只有 8 bytes（1 ulong）
- Vector128 載入 16 bytes，後 8 byte 是垃圾
- 比較結果要再 mask 前 8 bytes
- 整體 ops 數不比 pure SWAR 少

**效能：** 可能 0 或微負

**推薦：❌ 跳過**

---

### 🟡 D. `pa = 0 if bgColor == 0` 分支消除

**目前：**
```csharp
int pa = (bgPalette << 2) | bgColor;
if (bgColor == 0) pa = 0;
```

**等價無分支：**
```csharp
int pa = (bgPalette << 2) | bgColor;
pa &= -(bgColor != 0 ? 1 : 0);  // 或等價 bitwise
```

**問題：** RyuJIT 已經 cmov 處理。改成 bitwise 可能更慢（depends on JIT）。

**推薦：❌ 跳過**（交給 JIT）

---

### ⚠️ E. Sprite 0 hit 條件重排

**目前（line 282-283）：**
```csharp
if (canDetectSprite0Hit && i == 0 && sprZeroInSlots && showBG && bgColor != 0)
{ if ((ShowSprLeft8 || cx > 8) && cx < 256) { pendingSprite0Hit = true; canDetectSprite0Hit = false; } }
```

5 個 AND + nested 2 個。條件中最可能 false 的應先判斷（short-circuit 早期退出）。

**分析順序：**
- `canDetectSprite0Hit` — 一旦 hit 就關掉，大多數時候 = true（直到首次 hit）
- `i == 0` — 1/8 機率（若 sprite 0 剛好是最低 index）
- `sprZeroInSlots` — 若 sprite 0 在 visible range 則 true，一般大多 true
- `showBG` — 通常 true（除非黑畫面）
- `bgColor != 0` — 50-70% 機率（視畫面）

**推薦順序（最可能 false 先）：**  
`i == 0` → 1/8 = 最可能 false，放第一
```csharp
if (i == 0 && canDetectSprite0Hit && sprZeroInSlots && showBG && bgColor != 0) { ... }
```

**效能：** 微小，branch predictor 已 learn to handle。

**風險：** 極低（semantic 完全不變）

**推薦：🟡 可順手做**（微幅幫助 BP）

---

### ❌ F. 整個 CalculatePixel 改 vectorized batch（處理多個 pixel 一次）

**想法：**
一次算 8 個 pixel（cx-range 展開成 vector）。

**問題：**
- 每個 pixel 的 `FineX`, `renderHigh/Low` 都會 per-dot 改變（shift register）
- Sprite mux 的狀態（xc decrement）也跨 pixel 相依
- Sprite 0 hit 的狀態（canDetectSprite0Hit）跨 pixel 相依
- **狀態相依導致無法平行**

**推薦：❌ 不可能**（算法本質是 sequential state machine）

---

## 3. 風險總覽

| 優化 | 確定正確 | 破壞 TriCNES accuracy 風險 | 測試代價 | 預期收益 |
|------|---------|------------------------|---------|---------|
| A (TZC) | ✅ | 極低 | 低（blargg + AC 跑一次）| 0.05-0.1% CPU |
| B (BEXTR) | ✅ | 極低 | 低 | ~0% |
| C (SSE2 zero cmp) | ✅ | 極低 | 低 | ~0% |
| D (pa 無分支) | ✅ | 極低 | 低 | ~0% |
| E (條件重排) | ✅ | 零 | 無 | 微小 |
| F (批次) | ❌ 演算法不允許 | — | — | — |

## 4. 必要的回歸測試

若套 A+E：
1. **blargg 174 全跑** — 特別注意：
   - `sprite_hit_tests` (11/11) ← sprite 0 timing
   - `sprite_overflow_tests` (5/5) ← overflow eval
   - `ppu_vbl_nmi` (10/10) ← 整個 PPU state machine
2. **AccuracyCoin 136/136** — 特別注意：
   - sprite 相關測試
   - palette corruption tests
   - Sprite0Hit edge cases
3. **遊戲實測**（視覺比對）：
   - Mega Man 5 (sprite-heavy)
   - Castlevania 3 (MMC5, 複雜 sprite)
   - Battletoads (sprite timing 敏感)
   - Punch-Out!! (sprite 0 hit 關鍵)

---

## 5. 建議與結論

### 最務實方案：**只做 A + E**

**收益估計：+0.1% CPU（~0.2 FPS at 1x）**

- **A (`TrailingZeroCount`)**: 1 行改動，純數學等價，微收益
- **E (條件重排)**: 1 行改動，純順序調整，幾乎零風險

### 其他跳過
- **B, C, D**：收益 ~0，不值得寫 intrinsic
- **F**：演算法本質不允許

### 誠實判斷

**CalculatePixel 已經高度優化**：
- SWAR sprite mux 是教科書級
- BG pixel extract 已經 minimum ops
- 所有 branch 都有 cmov 潛力且 JIT 生成合理

**真正能推進的方向：**
1. **GPU migration**（SkiaSharp shaders）— CPU 讓出 NTSC decode + CRT，預期 30-40% 系統級收益
2. **接受目前水位** — NES 60 fps target，Avalonia 158 FPS = 2.6× 實機，沒有 user-visible 問題

**不建議動 CalculatePixel 其他地方** — 風險/收益比最差。

---

## 6. 原始資料參考

- `ppu_new.cs:234-305` — CalculatePixel 區塊
- `ppu_new.cs:282-283` — sprite 0 hit 條件
- `ppu_new.cs:274` — de-Bruijn 魔法乘法
- TriCNES reference: line 3073 (CalculatePixel 對應)
