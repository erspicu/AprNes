# 2026-03-23 更新日誌

---

## 1. CRT 後處理架構重構（CrtScreen.cs → NTSC_CRT/CrtScreen.cs）

- 全部 CRT 效果搬遷至 `NesCore/NTSC_CRT/CrtScreen.cs`
- **Per-row fused pipeline**：Shadow Mask + Phosphor Persistence + Beam Convergence 在同一 `Parallel.For` 迴圈內融合處理
- 減少高解析度時的記憶體流量（不再多 pass 掃描整張畫面）
- 舊版 `NesCore/CrtScreen.cs` 與 `NesCore/Ntsc.cs` 從 git 移除

## 2. SWAR Shadow Mask 色彩修正（CrtScreen.cs）

**問題**：原始 SWAR 寫法將不同乘數（256 / udim）包進同一 R+B 運算，造成 R→B cross-product 污染。
Shadow mask phase 0（保留 R、衰減 G+B）出現偏紅或偏藍的色偏。

**修正方式**：
- 統一用 `udim` 對所有通道做 SWAR 乘法衰減
- 再從原始像素 bit-mask restore 需保留的通道
- 三相各自正確：Phase 0 保留 R、Phase 1 保留 G、Phase 2 保留 B

## 3. Branchless Positional Math.Max（CrtScreen.cs）

**問題**：Phosphor persistence 路徑每 pixel 有 9 個 `if (pr > mr) mr = pr;` 分支（3 通道 × 3 相）。

**修正方式**：
- 各通道保持在原始 bit position（R=0x00FF0000, G=0x0000FF00, B=0x000000FF）
- 使用 `Math.Max(uint, uint)` 直接比較，JIT 生成 CMOV 指令
- 消除 shift-extract / shift-reassemble / 分支共 21 條指令

## 4. CRT Benchmark（多解析度比較）

| AnalogSize | 舊版 (multi-pass) | SWAR+branchless | 差異 |
|:---:|:---:|:---:|:---:|
| 2x | 127.80 | 121.93 | -4.6% |
| 4x | 113.91 | 110.90 | -2.6% |
| 6x | 81.81 | 84.32 | +3.1% |
| 8x | 74.03 | 81.94 | +10.7% |

高解析度（6x/8x）明顯提升，低解析度（2x/4x）因 fused pipeline 增加單次迴圈複雜度略降。

## 5. CRT 文件修正（5 項）

`MD/Analog/Analog_CRT_Simulation_Report.md`（中文）及 `_EN.md`（英文）修正：
1. **BrightnessBoost**：1.25 → 1.10（已改正實作）
2. **I/Q 頻寬**：YIQ 的 I/Q 頻寬反向標註（已修正為 I=1.3MHz, Q=0.4MHz）
3. **Interlace Jitter**：效果實為 Even/Odd Field Offset，非 interlace jitter
4. **Phosphor Persistence**：公式修正為 max(current, decayed_prev)
5. **Beam Convergence**：非獨立 pass，而是 fused 在 per-row pipeline 內

## 6. Read-Time CIRAM Mirroring（PPU.cs, MEM.cs）

- 修復洛克人5 電梯場景重複平台問題
- 將 write-time mirroring 改為 read-time mirroring（與真實硬體一致）
- 新增 `CIRAMAddr()` 函數，所有 nametable R/W 經過地址轉換
- 影響 30+ 個動態切換 mirroring 的 mapper

## 7. PPU $2006 Delayed t→v Copy（PPU.cs）— BUGFIX57

- 修復洛克人5 垂直平台震動問題
- $2006 第二次寫入不再立即 copy `vram_addr_internal → vram_addr`
- 新增 3 PPU dot countdown timer（匹配 TriCNES 模型的 4-5 dot delay）
- 影響所有在 rendering 期間透過 $2006 設定 scroll 的遊戲

## 8. 雜項

- 移除 `repost/` 目錄（非必要檔案）
- 移除舊版 `NesCore/Ntsc.cs` 和 `NesCore/CrtScreen.cs`（已被 `NTSC_CRT/` 取代）
- Benchmark 腳本 `bench_analog_resolutions.sh` 多解析度對照

---

**測試結果**: 174/174 blargg PASS, 136/136 AccuracyCoin PASS（無回歸）
