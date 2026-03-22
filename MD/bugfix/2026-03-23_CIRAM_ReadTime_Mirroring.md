# BUGFIX: Read-Time CIRAM Mirroring

**日期**: 2026-03-23
**Commit**: 20a63f9
**影響範圍**: PPU.cs, MEM.cs — 全部 30+ 個動態切換 mirroring 的 mapper

---

## 問題

洛克人5 (Mega Man 5, Mapper 004/MMC3) 電梯場景中，畫面上方出現不應存在的重複平台。
Mesen2 對照正常（只有一個平台 + 天空），AprNes 顯示兩個平台。

此問題從極早期版本就存在，非近期回歸。

## 根因

AprNes 使用 **write-time mirroring**（寫入時複製資料到鏡像位置）：
- ppu_ram 有 4 KB nametable 空間 ($2000-$2FFF)
- 寫入時根據當前 mirroring 模式，將資料同時寫入兩個鏡像位置
- 渲染時直接讀取 `ppu_ram[ioaddr]`，不做任何地址轉換

**真實 NES 只有 2 KB CIRAM**，mirroring 是在硬體地址線層級做的（address decode）。
切換 mirroring 模式時，所有讀取**立即**反映新的映射，不需要移動資料。

**洛克人5 的情況**：
1. 水平捲動關卡：使用 vertical mirroring ($2000=$2800)
2. 進入電梯場景：遊戲切換為 horizontal mirroring ($2000=$2400, $2800=$2C00)
3. 但 ppu_ram[$2800] 仍保有 vertical mirroring 時寫入的舊平台資料
4. 在真實硬體上，切換後 $2800 會指向不同的 CIRAM page，看到的是正確的天空資料
5. 在 AprNes 中，$2800 殘留舊資料 → 兩個 nametable 顯示相同的平台 → 重複出現

## 修復

改為 **read-time mirroring**，與 TriCNES / Mesen2 相同做法。

### 新增 `CIRAMAddr()` (PPU.cs)

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
static int CIRAMAddr(int addr)
{
    int mirror = *Vertical;
    if (mirror == 0) // H-mirror: $2000=$2400(p0), $2800=$2C00(p1)
        return (addr & 0x23FF) | ((addr & 0x0800) >> 1);
    if (mirror == 1) // V-mirror: $2000=$2800(p0), $2400=$2C00(p1)
        return addr & 0x27FF;
    if (mirror == 2) // 1-screen A: all → page 0
        return addr & 0x23FF;
    return (addr & 0x23FF) | 0x0400; // 1-screen B: all → page 1
}
```

將所有 nametable 地址映射到 2 個實體 CIRAM 頁面：
- Page 0: $2000-$23FF
- Page 1: $2400-$27FF

### 修改清單

| 檔案 | 位置 | 變更 |
|------|------|------|
| PPU.cs | BG tile fetch (phase 1) | `ppu_ram[ioaddr]` → `ppu_ram[CIRAMAddr(ioaddr)]` |
| PPU.cs | Attribute fetch (phase 3) | `ppu_ram[ioaddr]` → `ppu_ram[CIRAMAddr(ioaddr)]` |
| MEM.cs | $2007 nametable write | 移除雙寫鏡像邏輯 → `ppu_ram[CIRAMAddr(addr)] = val` |
| MEM.cs | $2007 nametable read | `ppu_ram[val & 0x2FFF]` → `ppu_ram[CIRAMAddr(val & 0x2FFF)]` |
| MEM.cs | $2007 palette read (buffer) | 同上 |

## 影響的 Mapper

所有動態切換 `*Vertical` 的 mapper（30+ 個）都受益：
- MMC1 (001), MMC3 (004), MMC2/4 (009/010)
- VRC 系列 (021-026), FME-7 (069), VRC7 (085)
- Bandai (016/153), Jaleco (018), Irem (032/065)
- TxSROM (118), TQROM (119), Namco (210) 等

## 測試結果

- **Blargg**: 174/174 PASS (0 回歸)
- **AccuracyCoin**: 136/136 PASS (0 回歸)
- **洛克人5 電梯場景**: 修復確認，不再出現重複平台
