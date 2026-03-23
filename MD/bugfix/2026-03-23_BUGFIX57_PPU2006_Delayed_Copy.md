# BUGFIX57: PPU $2006 Delayed t→v Copy

**日期**: 2026-03-23
**Commit**: a2fd0ec
**影響範圍**: PPU.cs, Main.cs

---

## 問題

洛克人5 (Mega Man 5, Mapper 004/MMC3) 電梯關卡中，腳下的垂直移動平台不斷上下震動（每幀偏移 1 scanline）。Mesen2 對照正常（平台平穩移動）。

此問題影響所有在 rendering 期間透過 $2006 設定 scroll 的遊戲。

## 根因

AprNes 在 CPU 對 $2006 第二次寫入時，**立即**執行 `vram_addr = vram_addr_internal`（t→v copy）。

真實 NES 硬體的 PPU 內部 latch 更新存在延遲——在 CPU write 之後約 **4-5 PPU dots** 才實際更新 `v` 暫存器。這是因為 PPU 內部匯流排需要時間傳播寫入訊號。

TriCNES（AC 136/136 滿分模擬器）實作了此延遲，而 AprNes 之前沒有。

**洛克人5 的情況**：
1. 遊戲在 HBlank 期間寫入 $2006 設定垂直捲動位置
2. 立即更新使 vram_addr 比預期提早生效 1 dot
3. 渲染引擎在同一 scanline 開始使用新的 vram_addr
4. 造成平台在相鄰幀之間偏移 1 scanline → 視覺震動

## 修復

在 PPU.cs 新增延遲機制，$2006 第二次寫入不再立即 copy，改用 countdown timer。

### 新增欄位 (PPU.cs)

```csharp
// $2006 delayed t→v copy (TriCNES model: 3 PPU dots after CPU write)
static int ppu2006UpdateDelay = 0;
static int ppu2006PendingAddr = 0;
```

### 修改 `ppu_w_2006()` (PPU.cs)

```csharp
// Before (immediate copy):
vram_addr = vram_addr_internal;
if (mapperNeedsA12) NotifyMapperA12(vram_addr);

// After (delayed copy):
ppu2006PendingAddr = vram_addr_internal;
ppu2006UpdateDelay = 3;
```

### 新增 countdown 處理 (`ppu_step_new()`)

```csharp
// $2006 delayed t→v copy
if (ppu2006UpdateDelay > 0 && --ppu2006UpdateDelay == 0)
{
    vram_addr = ppu2006PendingAddr;
    if (mapperNeedsA12) NotifyMapperA12(vram_addr);
}
```

### 延遲值計算

AprNes 使用 tick-before-write 模型：CPU write 時，該 cycle 的 3 PPU dots 已經執行完畢。
因此設定 `ppu2006UpdateDelay = 3` 表示再等 3 個 PPU dot，從 CPU cycle 起點算共 ~5-6 PPU dots，
與 TriCNES 的 4-5 dot delay 一致。

### Reset 初始化 (Main.cs)

```csharp
ppu2006UpdateDelay = 0; ppu2006PendingAddr = 0;
```

## 測試結果

- **Blargg**: 174/174 PASS (0 回歸)
- **AccuracyCoin**: 136/136 PASS (0 回歸)
- **洛克人5 電梯場景**: 平台震動修復

## 未來改進

- TriCNES 還實作了 `CopyV` bus conflict 行為：當 $2006 copy 與 Y increment 落在同一 dot 時，
  vram_addr 取 old 與 new 的 AND 值。目前 AprNes 尚未實作此機制，若日後有測試需要可補上。
