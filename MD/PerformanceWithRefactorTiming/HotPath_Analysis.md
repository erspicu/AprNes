# Hot Path 分析 — 效能優化下一階段

分析日期：2026-04-07  
目前效能：96.25 FPS（baseline 87.19, +10.4%）  
目標：逼近 master 264.45 FPS

## 呼叫頻率（NTSC, per second）

| Method | 頻率 | 佔比 |
|--------|------|------|
| MasterClockTick | 21.48M | 100% |
| ppu_step_new | 5.37M | 25% |
| ppu_half_step_new | 5.37M | 25% |
| cpu_step_one_cycle | 1.79M | 8.3% |
| apu_step | 1.79M | 8.3% |

## TOP 5 熱點

### #1 Sprite 合成迴圈（ppu_step_new 內 CalculatePixel）
- **位置**：8-sprite unrolled loop（每 visible dot 跑）
- **頻率**：61,440 次/frame
- **問題**：95% 的 dots 沒有 sprite 顯示，但 8 個 slot 全部評估
- **優化方案**：precompute 1-byte active sprite bitmask（dot 339 時計算），loop 前 `if (mask == 0) skip`
- **預期改善**：5-10%

### #2 BG Tile Fetch 8-phase（ppu_step_new 內）
- **位置**：8 個 if-else phase 分支
- **頻率**：dots 1-256 + 321-336 per scanline
- **問題**：每 phase 獨立計算地址，mapper callback 檢查
- **優化方案**：合併 phase 0+1, 2+3, 4+5, 6+7 為 4 個 dual-phase handler
- **預期改善**：3-5%

### #3 Pixel 合成 + Palette 查表（ppu_step_new 內）
- **位置**：`NesColors[ppu_ram[0x3f00 + pa] & 0x3f]` 每 dot 查兩次
- **頻率**：61,440 次/frame
- **問題**：palette 寫入很少（1-100 次/frame），但每 dot 都重新查表
- **優化方案**：palette write 時建 64-entry cache，per-dot 直接讀 cache
- **預期改善**：2-3%

### #4 APU Pulse/Noise Timer（apu_step 內）
- **位置**：Pulse 1/2 timer + Noise LFSR
- **頻率**：1.79M/sec
- **問題**：靜音聲道仍然完整計算 duty lookup 和 LFSR
- **優化方案**：`if (lc > 0 && period >= 8)` 門控 duty 查表；靜音時跳過 LFSR
- **預期改善**：8-12%（取決於遊戲使用的聲道數）

### #5 BG Shift Register（ppu_half_step_new 內）
- **位置**：4 × 16-bit shift 每 visible dot
- **頻率**：61,440 次/frame
- **問題**：4 次獨立 shift，無法利用 ILP
- **優化方案**：pack 成 ulong 做 1 次 shift（需驗證 .NET Framework JIT 行為）
- **預期改善**：1-2%

## 優先建議（投資報酬率排序）

| 優先序 | 項目 | 預期改善 | 工作量 |
|--------|------|----------|--------|
| 1 | APU 靜音聲道 fast-path | 8-12% | 低 |
| 2 | Sprite active mask | 5-10% | 中 |
| 3 | Palette cache | 2-3% | 低 |
| 4 | Tile fetch dual-phase | 3-5% | 高 |
| 5 | Shift register pack | 1-2% | 低 |

**理論極限**：全部優化後 ~114-132 FPS（+19-37%）
