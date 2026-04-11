# TriCNES v2 移植 — 剩餘差異清單

**Branch**: `feature/tricnes-v2-port`
**日期**: 2026-04-11
**當前基線**: 180/184 blargg, ~136/138 AC v2

---

## 重大架構差異：ALE/READ convention 反轉

**TriCNES**: `H0_DASH = (PPU_Dot - 1 & 1)` → 奇數 dot = ALE, 偶數 dot = READ
**AprNes**: `H0_DASH = (cx & 1)` → 奇數 cx = READ, 偶數 cx = ALE

這是整個 AprNes PPU 的基礎設計差異，不是 bug。主 tile fetch 已經根據此 convention 正確實作（even cx=ALE 設地址, odd cx=READ 用 OctalLatch）。

**影響**: Sprite fetch（dots 257-320）的 OctalLatch 在 ALE/READ 反轉下時序不對。
- TriCNES sprPhase 0,2,4,6（奇數 dot）= ALE → after-guard 更新 OctalLatch
- AprNes sprPhase 0,2,4,6（奇數 cx）= READ → before-guard 用舊值更新 OctalLatch

**解決方式**: sprite fetch 的 read 不用 OctalLatch model，直接用 ppuAddressBus。
OctalLatch guards 仍然運行（供 SM 使用），但 sprite read 地址來自完整 bus。

---

## 已完成的移植

| 項目 | TriCNES 位置 | 狀態 |
|------|-------------|------|
| SR latch 3-phase model (SM / SM2 / SM_Half) | line 1761/1807/1827 | ✅ |
| 7MC EmulateUntilEndOfRead ($2007 R/W) | line 750, 9059, 9675 | ✅ |
| PPU_READ / PPU_ALE / BLNK 信號 | line 1782, 1796 | ✅ |
| OctalLatch field + ALE 更新 | line 8852, 1803, 3589 | ✅ |
| v increment → half-step (v += inc + Yinc) | line 1829-1837 | ✅ |
| 2nd FetchPPU in half-step | line 1840-1848 | ✅ |
| FetchPPU bus side effect (all fetch points) | line 149-176 | ✅ |
| OctalLatch guards (BG + sprite + DummyNT) | line 3587, 3643, 2833 | ✅ |
| Tile fetch range cx>=1 && <=256 (TriCNES PPU_Dot) | line 1585 | ✅ |
| CalculatePixel range cx>0 && <=256 | line 1600 | ✅ |
| Deferred commit (renderTemp + commit flags) | line 3650-3692 | ✅ |
| $2006/$2005/$2000 delayed updates (phase-dependent) | line 1263-1320 | ✅ |
| Palette corruption on v change | — | ✅ |

---

## 剩餘差異

### D1. Mapper FetchPPU 路徑 — 影響：高

**TriCNES v2** (line 149-176, Mapper base class):

```csharp
public virtual byte FetchPPU()
{
    ushort Address = (ushort)((PPU_AddressBus & 0x3F00) | PPU_OctalLatch);
    if (Address < 0x2000)
    {
        PPU_AddressBus = (PPU_AddressBus & 0xFF00) | FetchCHR(Address);
    }
    else
    {
        Address = MirrorNametable(Address) & 0x7FF;
        PPU_AddressBus = (PPU_AddressBus & 0xFF00) | VRAM[Address];
    }
    return (byte)PPU_AddressBus;
}
```

所有 PPU memory read 都經過 mapper 的 `FetchPPU()`，mapper 可 override：
- **MMC3** override `FetchPPU()` 處理 alternative nametable arrangement (PRGVRAM routing)
- **Base mapper** 使用 `(AddressBus & 0x3F00) | OctalLatch` 構成地址
- Read 後 `AddressBus` low byte 被 data 覆蓋（bus side effect）

**AprNes 現況**:

- Tile fetch: 直接 `PpuBusRead(readAddr)` + 手動 bus side effect
- Sprite fetch: 直接 `chrBankPtrs[bank][offset]`（完全繞過 bus）
- SM FetchPPU: `PpuBusRead((ppuAddressBus & 0x3F00) | ppuOctalLatch)`
- Mapper 沒有 FetchPPU override 機制

**影響**:
- MMC3 scanline timing（2 個 pre-existing FAIL）
- mapper 看到的 bus 狀態不完整
- $2007 Stress Test（SM 和 rendering 共用 bus 的行為）

**修正方向**:
- 不需要改 AprNes 的 IMapper 介面加 FetchPPU
- 但需要確保所有 PPU read point 都走統一的 bus read 路徑（含 bus side effect）
- Sprite fetch 的 CHR read 也需要走 bus（目前直接用 chrBankPtrs 繞過）

---

### D2. Sprite fetch dots 257-320 缺少 bus read — 影響：中

**TriCNES** sprite eval case 1 和 case 3 (line 2876, 2900):

呼叫完整的 `PPU_Render_ShiftRegistersAndBitPlanes()`，which 對應 cycleTick 1/3：
- Case 1 (dot 258,266,...): 實際做 NT FetchPPU（bus side effect: AddressBus low = NT data）
- Case 3 (dot 260,268,...): 實際做 AT FetchPPU（bus side effect: AddressBus low = AT data）

**AprNes 現況** (ppu_new.cs line 551-557):

Dummy BG fetch 只設 `ppuAddressBus`（ALE），不做 read：
```csharp
if (bgPhase == 1) ppuAddressBus = 0x2000 | (vram_addr & 0x0FFF);  // 只設地址
else if (bgPhase == 3) ppuAddressBus = 0x23C0 | ...;               // 只設地址
```

**差異**: 缺少 PpuBusRead + bus side effect。OctalLatch 在 sprite fetch 期間的值可能不正確。

**影響**: sprite 0 hit timing（可能是 sprite_hit 05/09 回歸的原因）

---

### D3. Sprite fetch CHR read 不走 bus — 影響：中

**TriCNES** sprite eval case 5/7 (line 2927, 2969):

```csharp
PPU_AddressBus = (PPU_PatternAddressRegister_CHR & 0xFF00) | PPU_OctalLatch;
PPU_SpritePatternL = Cart.MapperChip.FetchPPU();  // 走 mapper FetchPPU
```

**AprNes** (ppu_new.cs line 571):

```csharp
int addr = ppuAddressBus;
byte tile = chrBankPtrs[(addr >> 10) & 7][addr & 0x3FF];  // 直接從 bank pointer 讀
```

**差異**:
1. AprNes 不用 OctalLatch 構成地址（直接用 ppuAddressBus）
2. 不走 PpuBusRead（無 bus side effect）
3. 不經 mapper FetchPPU

**影響**: mapper 看不到 sprite fetch 的 bus 活動，OctalLatch 在 sprite CHR fetch 後未更新

---

### D4. Dummy BG fetch 計算 cycleTick 不同 — 影響：中

**TriCNES** 在 sprite eval case 1/3 呼叫 `PPU_Render_ShiftRegistersAndBitPlanes()`：

```csharp
cycleTick = (byte)((PPU_Dot + 7) & 7);  // dot 258 → cycleTick=1 (NT READ)
```

**AprNes** sprite fetch dummy BG (ppu_new.cs line 554):

```csharp
int bgPhase = evalDot & 7;  // dot 258 → bgPhase=2 (沒有 +7 的 offset)
```

這個 offset 差異導致 ALE/READ 相位完全對不上。`evalDot & 7` 和 `(evalDot + 7) & 7` 差 7 mod 8。

不過目前 AprNes 的 dummy BG fetch 只設地址不做 read，所以 cycleTick 差異暫時不影響實際行為。一旦加入 read，需要修正 cycleTick 計算。

---

### D5. DummyNT (dots 337-340) FetchPPU 走法 — 影響：低

**TriCNES** (line 3718-3731):

```csharp
case 1: // dt=1, dot 338
    PPU_AddressBus = (ushort)(0x2000 + (PPU_v & 0x0FFF));
    PPU_RenderTemp = Cart.MapperChip.FetchPPU();  // 走 mapper
    PPU_Commit_NametableFetch = true;
```

**AprNes** (ppu_new.cs line 652-657):

```csharp
else if (dt == 1)
{
    ppuAddressBus = 0x2000 | (vram_addr & 0x0FFF);
    renderTemp = (byte)PpuBusRead((ppuAddressBus & 0xFF00) | ppuOctalLatch);
    // ...
}
```

AprNes 用 `PpuBusRead` 而非 mapper FetchPPU。邏輯上等效（PpuBusRead 同樣讀 VRAM），但 bus side effect 的處理方式不同。

---

### D6. Rendering OFF 多餘的 ppuChrFetchA12 設定 — 影響：低

**TriCNES** (line 1530-1535):

```csharp
if (!ShowBG && !ShowSpr) PPU_AddressBus = PPU_v;  // 只設 bus
```

**AprNes** (ppu_new.cs line 158-162):

```csharp
if (!ShowBackGround && !ShowSprites)
{
    ppuAddressBus = vram_addr;
    ppuChrFetchA12 = (vram_addr >> 12) & 1;  // TriCNES 沒有
}
```

多設了 ppuChrFetchA12。影響應該很小（這是 AprNes 自己的 mapper A12 快取機制）。

---

### D7. Pattern Address Register (PAR) 模型缺失 — 影響：低~中

**TriCNES** 新增 3 個 PAR:

```
PPU_PatternAddressRegister_NT   — NT 地址暫存
PPU_PatternAddressRegister_AT   — AT 地址暫存
PPU_PatternAddressRegister_CHR  — CHR 地址暫存（含 BG/sprite table select + fine Y）
```

PPU_CheckPAR() 根據當前 dot 範圍設定 CHR PAR 的 pattern table select 和 fine Y。
Tile fetch ALE 設 PAR → READ 用 `(PAR & 0xFF00) | OctalLatch`。

**AprNes** 直接計算地址，沒有 PAR 中間暫存器。

**影響**: 正常情況下等效（ALE 和 READ 之間 ppuAddressBus 沒被修改）。但當 SM ALE/READ 和 rendering fetch 在同一 dot 衝突時，PAR 能保護 rendering 的高位元地址。AprNes 缺少這層保護。

---

## 回歸分析

### 4 個 blargg FAIL

| 測試 | 性質 | 原因分析 |
|------|------|---------|
| mmc3_test/4-scanline_timing | pre-existing | 需要 D1 (mapper FetchPPU) |
| mmc3_test_2/4-scanline_timing | pre-existing | 同上 |
| sprite_hit 05.left_clip #4 | **回歸** | 可能 D2 (sprite dummy fetch 缺 bus read) |
| sprite_hit 09.timing_basics #3 | **回歸** | 可能 D2 或 tile fetch 邊界差異 |

### AC v2 FAIL (約 2 項)

- $2007 Stress Test: 需要完整 bus 共用模型（D1 + D7 的組合效果）
- 其他 FAIL: 待確認具體項目

---

## 修正優先建議

| 優先 | 項目 | 預期效果 |
|------|------|---------|
| 1 | D2: sprite fetch case 1/3 加入 PpuBusRead | 修復 sprite_hit 05/09（+2 blargg） |
| 2 | D3: sprite CHR fetch 走 bus + OctalLatch model | 更正確的 bus 狀態 |
| 3 | D1: 統一 PPU read 路徑 (tile/sprite/SM 都走 bus) | 修復 MMC3 timing + $2007 Stress |
| 4 | D4: 修正 sprite dummy BG cycleTick 計算 | 配合 D2 |
| 5 | D7: 評估是否需要 PAR | 可能改善 bus conflict edge case |
| 6 | D5/D6: 微調 | 低優先 |

---

## TriCNES v2 核心設計要點（供移植參考）

### 匯流排多工 (Multiplexed Bus)

NES PPU 的 address/data bus 是共用的。TriCNES v2 的 FetchPPU 精確模擬這個行為：
1. **ALE**: 完整地址放上 AddressBus，OctalLatch 記住低 8 bit
2. **READ**: 用 `(AddressBus & 0x3F00) | OctalLatch` 讀取（高 6 bit + 低 8 bit = 14 bit）
3. **Bus overwrite**: 讀出的 data 覆蓋 AddressBus 低 8 bit

SM 和 rendering 共用同一條 bus，OctalLatch 是衝突時的仲裁點。

### Mapper 責任重分配

v2 把 PPU fetch 路徑從 core 移到 mapper：
- Base mapper: 通用 FetchPPU（CHR ROM/RAM + VRAM mirror）
- MMC3: override FetchPPU 處理 alternative nametable（PRGVRAM 路由）
- 其他 mapper: 移除 base PRGRAM fallback，各自顯式處理

### 半步進精度

v increment、write commit、latch odd-index 推進都在 half-step 執行，不是 full dot。
