# NesCoreNET SIMD 優化 TODO

> 基線（新協議）：**365.93 FPS**（.NET 10 TieredPGO, AccuracyOptA=ON, Mega Man 5 (U).nes, 20s, run2+run3平均）
> 最終基線：**381.18 FPS**（S01+S02+S03+C01 保留後）
> Benchmark 協議：第1次不算（JIT 暖機）→ 冷卻60秒 → 第2次 → 冷卻60秒 → 第3次 → 取2、3次平均

---

## 已完成

| ID | 位置 | 說明 | 結果 |
|----|------|------|------|
| S00 | PPU.cs `CompositeSpritesSimd()` | SSE4.1 sprite composite pass（原 AprNes 已有） | ✅ KEPT，已移除 SIMDEnabled flag |
| S01 | PPU.cs cx==0 BG clear | `Buffer_BG_array` scanline 清零：AVX 32B/store（8次），SSE2 fallback | ✅ KEPT |
| S02 | PPU.cs cx==0 BG fill | `ScreenBuf1x` broadcast bgColor：同 S01 路徑 | ✅ KEPT（與S01合併測試）|
| S03 | PPU.cs `RenderBGTile()` | 8像素 BG tile：bit-reversal 提取 + Vector128.Shuffle palette lookup | ✅ KEPT |
| S04 | PPU.cs sprite render loop | 8像素 sprite：SIMD bit extraction + palette lookup + conditional sprSet | ❌ REVERTED |
| C01 | CPU.cs 所有 opcode handlers | `switch(operationCycle)` → `if-else` 全面轉換（13處）| ✅ KEPT（持平）|

---

## 實測記錄

| ID | 日期 | 說明 | 第2次 FPS | 第3次 FPS | 平均 FPS | vs 前基線 | 結論 |
|----|------|------|-----------|-----------|----------|-----------|------|
| baseline | 2026-03-18 | .NET 10 無新SIMD（新協議 run2+run3）| 364.05 | 367.80 | **365.93** | — | 基線 |
| S01+S02 | 2026-03-18 | BG array AVX clear + ScreenBuf broadcast fill | 366.10 | 372.30 | **369.20** | +0.9% | ✅ 保留 |
| S03 | 2026-03-18 | RenderBGTile Vector128.Shuffle palette | 376.30 | 385.60 | **380.95** | +3.2% | ✅ 保留 |
| S04 | 2026-03-18 | Sprite loop SIMD（Sse41.Pack + Shuffle + OR sprSet）| 368.50 | 372.15 | **370.33** | -2.8% | ❌ Revert |
| M01 | 2026-03-18 | Mapper004 prgBankPtrs table lookup（branch chain 消除）| 377.35 | 374.35 | **375.85** | -1.3% | ❌ Revert |
| C01 | 2026-03-18 | CPU.cs switch(operationCycle)→if-else 全面轉換（13處）| 383.15 | 379.20 | **381.18** | +0.06% | ✅ 保留（持平）|

**S04 失敗原因**：Sprite loop 呼叫頻率（≤8 sprites × 240 scanlines）遠低於 RenderBGTile（32×240），
向量化 setup 開銷（Pack、Shuffle、OR into sprSet）超過收益。Scalar 早 exit（pixel==0 continue）
在稀疏 sprite 場景反而更有效率。

**M01 失敗原因**：JIT TieredPGO 已對原 branch chain 做了良好的 branch prediction（Mega Man 5 PRG bank
切換極少，條件幾乎固定）。改用 `prgBankPtrs[]` 後增加了 managed array bounds check + unsafe pointer
雙重間接存取開銷，反而更慢。

**M02 失敗原因**：.NET 10 TieredPGO 下 managed `Func<>`/`Action<>` 反而比 `delegate*<>` 快。
PGO 學習到「`mem_read_fun[addr]` 幾乎總是呼叫 `MapperR_RPG`」後，對 managed delegate 做 speculative
inline，使其幾乎等於 direct call。`delegate*<>` 是 raw pointer，JIT 無型別資訊可做推測性內聯，
每次都走完整 indirect call，反而更慢（-3.0%）。結論：.NET 10 PGO 場景下 managed delegate 優於 raw function pointer。

---

## Mapper 優化 TODO

> 注：`MapperR_CHR` 不在 render hot path（PPU tile fetch 直接用 `chrBankPtrs`，`MapperR_CHR` 只由 CPU $2007 read 呼叫，極少）。
> `UpdateCHRBanks` 呼叫頻率太低（~15K/s），SIMD 無意義。
> 真正的 mapper hot path 是 `MapperR_RPG`：每 CPU PRG read 都呼叫，約 1.8M 次/秒，目前為 4-8 個 if-else branch chain。

| ID | 位置 | 說明 | 熱度 | 狀態 |
|----|------|------|------|------|
| M01 | Mapper004.cs `MapperR_RPG()` | 預建 `prgBankPtrs[4]`（同 chrBankPtrs 模式），消除 branch chain → `return prgBankPtrs[(addr>>13)&3][addr&0x1FFF]` | 🔥 ~1.8M/s | ❌ REVERTED |
| M02 | MEM.cs `mem_read_fun`/`mem_write_fun` | `Func<>`/`Action<>` → `delegate*<>` unsafe function pointer（消除 virtual dispatch） | 🔥 ~3-5M/s | ❌ REVERTED |

---

## 殘餘 TODO

### 低優先 — 初始化，不影響 FPS

| ID | 位置 | 說明 | 熱度 | 狀態 |
|----|------|------|------|------|
| S05 | Main.cs:208-213 | 初始化 zero-fill（ScreenBuf1x, NES_MEM 等）| init only | 跳過（無FPS效益）|
| S06 | Main.cs:84,93 | ROM load PRG/CHR byte copy | init only | 跳過（無FPS效益）|
| S07 | Main.cs:257,263 | SRAM save/load 8KB copy | save/load only | 跳過（無FPS效益）|

### 不適合 SIMD

| 位置 | 原因 |
|------|------|
| PPU.cs `PrecomputeOverflow()` | data-dependent branch，SIMD 無益 |
| MEM.cs function pointer init | delegate 物件不可向量化 |
| APU.cs LUT init | 資料量極小（<256元素）|
| CPU.cs opcode switch | 控制流完全 data-dependent（主 opcode dispatch switch 保留，operationCycle switch 已全轉 if-else）|
| Mapper004 `UpdateCHRBanks` | 呼叫頻率太低（~15K/s），SIMD 開銷不值得 |
| Mapper nametable write mirror | CPU $2007 write 極少，cold path |

---

## Benchmark 標準程序

```bash
# 第1次（JIT 暖機，數據不採計）
AprNesAvalonia/bin/Release/net10.0/AprNesAvalonia.exe --perf "etc/ROMS/USA/Mega Man 5 (U).nes" 20 "SXX desc"
sleep 60
# 第2次（有效）
AprNesAvalonia/bin/Release/net10.0/AprNesAvalonia.exe --perf "etc/ROMS/USA/Mega Man 5 (U).nes" 20 "SXX desc"
sleep 60
# 第3次（有效）
AprNesAvalonia/bin/Release/net10.0/AprNesAvalonia.exe --perf "etc/ROMS/USA/Mega Man 5 (U).nes" 20 "SXX desc"
# 取第2、3次平均
```

> 判斷標準：平均 FPS 比前一基線高 ≥ 0.5% 才視為有效提升並保留；否則 revert。
