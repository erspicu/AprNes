# PPU 最終審計 — AprNes vs TriCNES 差異清單

**日期：2026-04-02　分支：feature/ppu-high-precision**

---

## 需修正的差異

### 1. ⚠️ Attribute shift register 載入方式不同
- **AprNes**：Phase 7 時預填 0xFF 或 0x00（整個 byte 一次填入）
- **TriCNES**：存 2-bit latch，每 dot shift 進 1 bit
- **影響**：attribute 在 tile 邊界處的過渡可能不同

### 2. ⚠️ Rendering disabled 時 shift register 行為
- **AprNes**：disabled 時繼續 shift（serial-in 持續）
- **TriCNES**：disabled 時**不 shift**
- **影響**：BGSerialIn test（AC Page 19）的核心測試點

### 3. ⚠️ $2005/$2006 alignment delay 反轉
- **AprNes**：`ppuAlignPhase == 2` 時延遲較長
- **TriCNES**：`case 1,2` 延遲較長（不只 case 2）
- **$2005**：AprNes align[2]=2, others=1 → TriCNES align[0,3]=1, align[1,2]=2
- **$2006**：AprNes align[2]=5, others=4 → TriCNES align[0,3]=4, align[1,2]=5
- **影響**：mid-scanline scroll 精度

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
