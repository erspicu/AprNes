# PPU 最終審計 — AprNes vs TriCNES 差異清單

**日期：2026-04-02　分支：feature/ppu-high-precision**

---

## 已修正的差異（`3f5cc19`）

### 1. ✅ Attribute shift register 載入方式（已修正）
- **修正前**：Phase 7 時預填 0xFF 或 0x00
- **修正後**：存 2-bit `attrLatch`，每 dot 從 latch shift-in 1 bit

### 2. ✅ Rendering disabled 時 shift register 行為（已修正）
- **修正前**：disabled 時繼續 shift
- **修正後**：disabled 時不 shift（匹配 TriCNES）

### 3. ✅ $2005/$2006/$2000 alignment delay（已修正）
- **$2000**：align[0,1]=2, align[2,3]=1
- **$2005**：align[0,3]=1, align[1,2]=2
- **$2006**：align[0,3]=4, align[1,2]=5

## 已知但保持的差異（AC/blargg 已通過）

### 4. VBL set dot（dot 1 vs dot 0）
- AprNes 在 dot 1，TriCNES 在 dot 0
- 但 AprNes 通過所有 VBL timing tests — 可能是 pending latch 補償

### 5. NMI edge detection 機制
- AprNes 用 `nmi_delay_cycle`，TriCNES 用硬體 latch 模擬
- 語義等價，通過所有 NMI tests

### 6. Odd frame skip dot
- AprNes: scanline 261 dot 339，TriCNES: scanline 0 相關邏輯
- 效果相同（跳過 1 dot）
