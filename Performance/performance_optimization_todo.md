# AprNes Performance Optimization TODO

## Benchmark Method

### Tool
```powershell
AprNes.exe --perf "Performance\Mega Man 5 (USA).nes" 20 "description"
```
- ROM: Mega Man 5 (USA).nes（Mapper 004 MMC3，代表性複雜場景）
- Duration: 20 seconds, no FPS cap, no audio, headless mode
- Result: automatically saved to `Performance/YYYY-MM-DD_perf_vN.md`

### Pass/Fail Criteria
- **Improvement > 0.25%** → keep change, commit, create new perf report
- **Improvement ≤ 0.25% or flat** → revert, mark as FAILED in TODO
- **Regression** → revert, mark as FAILED in TODO

### Test Steps for Each Optimization
1. Record baseline FPS (latest `_perf_vN.md`)
2. Apply code change
3. Build: `MSBuild AprNes.csproj /p:Configuration=Debug /p:Platform=x64`
4. Run: `AprNes.exe --perf "Performance\Mega Man 5 (USA).nes" 20 "description"`
5. Compare FPS delta
6. Update this file with result

---

## Baseline

| Date | Frames (20s) | Avg FPS | Report |
|------|-------------|---------|--------|
| 2026-03-14 | 3634 | 181.70 | [perf_v1](2026-03-14_perf_v1.md) |

---

## Results Log

| # | Optimization | Before FPS | After FPS | Delta | Result | Report |
|---|-------------|-----------|----------|-------|--------|--------|
| 1 | Baseline | — | 181.70 | — | — | [v1](2026-03-14_perf_v1.md) |
| 2 | Priority 11: managed array → unsafe pointer (TRI_SEQ, DUTYLOOKUP, secondaryOAM, corruptOamRow) | 181.70 | 187.70 | **+3.3%** | ✅ KEEP | [v2](2026-03-14_perf_v2.md) |
| 3 | Priority 8: RenderBGTile pre-compute bgColor + merge duplicate condition | 187.70 | 188.55 | +0.4% | ✅ KEEP (cleaner code) | [v6](2026-03-14_perf_v6.md) |
| 4 | Priority 4+5 v1: APU int counter + stackalloc palette | 188.55 | 187.90 | -0.3% | ❌ REVERT | [v7](2026-03-14_perf_v7.md) |
| 5 | Priority 5 v2: static palCacheR/N (Marshal.AllocHGlobal, reuse) | 188.55 | 189.75 | **+0.6%** | ✅ KEEP | [v8](2026-03-14_perf_v8.md) |
| 6 | Priority 4 v2: APU int counter (單獨測試) | 189.75 | 186.50 | -1.7% | ❌ REVERT | [v10](2026-03-14_perf_v10.md) |
| 7 | Priority 9: CPU opcode dispatch Action[256] table | 189.75 | 204.10 | **+7.6%** | ✅ KEEP | [v11](2026-03-14_perf_v11.md) |
| 8 | Priority 6: remove redundant screenX > 255 check in RenderBGTile() | 204.10 | 216.45 | **+6.1%** | ✅ KEEP | [v12](2026-03-14_perf_v12.md) |
| 9 | Priority 3: ProcessPendingDma early-exit guard (5-flag) | 216.45 | 227.40 | **+5.1%** | ✅ KEEP | [v14](2026-03-14_perf_v14.md) |
| 10 | Priority 12: RAM read fast-path in Mem_r() / CpuRead() | 227.40 | 233.75 | **+2.8%** | ✅ KEEP | [v15](2026-03-14_perf_v15.md) |
| 11 | Priority 17ABC: ppu_cycles_x local shadow + cx param + apu pulse/tri shadow | 233.75 | 237.00 | **+1.4%** | ✅ KEEP | [v17](2026-03-14_perf_v17.md) |
| 12 | Priority 14: Buffer_BG_array / ScreenBuf1x pointer loop clear | 237.00 | 239.95 | **+1.2%** | ✅ KEEP | [v19](2026-03-14_perf_v19.md) |
| 13 | Priority 18A: AggressiveInlining on Yinc/SpriteEvalInit/SpriteEvalEnd/SpriteEvalTick | 239.95 | 245.30 | **+2.2%** | ✅ KEEP | [v21](2026-03-14_perf_v21.md) |

---

## Optimization Tasks

### PRIORITY 0 — Remove DMA trace logging code
- **Target**: APU.cs, IO.cs, TestRunner.cs — `dmcTraceEnabled` 條件式 `[DMA-TRACE]` Console.Error.WriteLine 及相關 field
- **Expected gain**: 微小（消除每次 APU write 路徑上的條件判斷）
- **Effort**: 已完成
- **Method**: 移除所有 `if (dmcTraceEnabled)` trace block、`dmcTraceStart` unused field、`--dma-trace` CLI 參數及 `NesCore.dmcTraceEnabled` 設定
- **Risk**: 無 — 純 debug 輸出，不影響模擬邏輯
- **Status**: ✅ DONE (build 0 errors)

---

### PRIORITY 1 — Remove PollInterrupts() calls
- **Target**: CPU.cs — 255+ empty stub calls in instruction dispatch switch
- **Expected gain**: 5–8%
- **Effort**: ~5 minutes
- **Method**: Delete all `PollInterrupts();` calls from each `case 0xXX:` in cpu_step_one_cycle()
- **Risk**: ~~Low~~ → **INVALID** — PollInterrupts() has actual runtime function (interrupt polling timing per-instruction), NOT a pure empty stub
- **Status**: ❌ FAILED — 移除後測試全部失敗，已 revert。**此方法不可行。**

---

### PRIORITY 2 — AudioEnabled early-exit guard in apu_step()
- **Target**: APU.cs — `apu_step()` runs all pulse/noise/triangle/DMC timers even when audio disabled
- **Expected gain**: 3–5% (when running headless/benchmark)
- **Status**: ❌ REMOVED — 邏輯正確性問題，不實作
- **原因**:
  - `apu_step()` 中以下邏輯**必須在 audio disabled 時繼續執行**，無法 guard：
    1. **`clockdmc()`** — DMC timer 驅動 DMA 週期偷取（stolen cycles），影響 CPU timing，與音效無關
    2. **frame counter / IRQ** — `framectrdiv` 倒數、`statusframeint` 設定，影響 IRQ 時序
    3. **`lengthctr_snapshot[]`** — `$4015` read 的 length counter 狀態來源
    4. **envelope/sweep/length 時鐘** — 影響 frame counter 狀態一致性
  - 只有最末段的「sample 輸出」可 guard，但那部分本身已有 `if (AudioEnabled)` 路徑（WaveOut 不播放）
  - 有效 guard 範圍極小，幾乎無收益，且容易遺漏，風險高

---

### PRIORITY 3 — Early-exit in ProcessPendingDma()
- **Target**: MEM.cs — `ProcessPendingDma()` called on every CpuRead even when no DMA active
- **Expected gain**: 3–5%
- **Method**: Add fast-path guard at entry（**修正版**，原版不完整）：
  ```csharp
  if (!dmaNeedHalt && !dmcDmaRunning && !dmcNeedDummyRead && !spriteDmaTransfer && !dmcImplicitAbortPending) return;
  ```
  - 原版 guard 漏掉 `!dmcNeedDummyRead`（dummy read 進行中會跳過）和 `!dmcImplicitAbortPending`（1-cycle phantom DMA 會被跳過）
- **Risk**: Low — 確認 5 個 flag 都覆蓋所有 DMA 入口條件
- **Status**: ✅ DONE — **+5.1%** (216.45 → 227.40 FPS)；blargg 174/174 + AC 136/136 驗證通過

---

### PRIORITY 4 — Integer fixed-point sample accumulator in APU
- **Target**: APU.cs — floating-point `_sampleAccum += 1.0` comparison done every CPU cycle
- **Expected gain**: 1–2%
- **Method**: Replace `double _sampleAccum` + `_cycPerSample` with `int _sampleCounter += APU_SAMPLE_RATE`，觸發條件 `>= (int)CPU_FREQ`
- **Risk**: Low — only affects sample timing precision (negligible)
- **Status**: ❌ FAILED — 兩次實測均為負效益
  - v1（與 Priority 5 stackalloc 合併）：188.55 → 187.90（-0.3%）
  - v2（單獨測試）：189.75 → 186.50（**-1.7%**），已 revert
  - 原因：Debug JIT 對 `int += int` 與 `double += double` 的差異不大，反而 `(int)CPU_FREQ` 的 cast overhead 可能更高

---

### PRIORITY 5 — Cache palette lookups in RenderBGTile()
- **Target**: PPU.cs `RenderBGTile()` — redundant ppu_ram[] + NesColors[] lookups per pixel
- **Expected gain**: 1–2%
- **Method**: 用靜態 `palCacheR / palCacheN`（`Marshal.AllocHGlobal(uint*4)` in Main.cs init），每次 RenderBGTile 開頭更新 6 個 entry，loop 內直接 `pal[bgPixel]`
- **Risk**: Low — pure refactor, no logic change
- **Status**: ✅ DONE — **+0.6%** (188.55 → 189.75 FPS)
  - ❌ v1（stackalloc）: -0.3%（每次 tile 都 allocate，開銷更大）
  - ✅ v2（static pre-alloc）: +0.6%（一次分配，重複使用）

---

### PRIORITY 6 — Remove redundant screenX > 255 check in RenderBGTile()
- **Target**: PPU.cs `RenderBGTile()` line ~173 — bounds check every iteration when only last tile pixel can overflow
- **Expected gain**: <1%（實際遠超預期）
- **Effort**: ~30 minutes
- **Method**: 確認呼叫方已有 `ppu_cycles_x < 256` guard，直接移除 `if (screenX > 255) break;`
- **Risk**: Low
- **Status**: ✅ DONE — **+6.1%** (204.10 → 216.45 FPS)；移除 inner loop branch 後 JIT 能更好 unroll/最佳化，遠超預期

---

### PRIORITY 7 — Memory write dispatch inlining
- **Target**: MEM.cs — `mem_write_fun[addr](addr, val)` function pointer call on every write
- **Expected gain**: 1–2%
- **Status**: ❌ REMOVED — 無效且有正確性風險，不實作
- **原因**:
  - `mem_write_fun[]` 已是 O(1) array 索引，dispatch overhead 本身幾乎為零
  - 真正的成本在 **handler 內部**（`IO_write` 21-case switch、mapper bank switching），inline dispatch 層無法改善
  - Mapper handler（`MapperW_PRG` 等）為虛擬方法，**runtime polymorphism 無法 inline**（10+ mapper 類型，JIT 無法確定單一目標）
  - 若自行複製 mapper dispatch 邏輯，mapper 新增/修改時極易產生不一致 → 靜默錯誤（bank switch 失效、MMC3 IRQ 錯誤等）
  - 結論：此最佳化在架構上無效，且正確性風險不對等

---

### PRIORITY 8 — If/else branch reordering (high-frequency path first)
- **Target**: PPU.cs `RenderBGTile()`、MEM.cs `init_function()` 讀寫 dispatch 設定
- **Expected gain**: 1–3%
- **Effort**: ~1 hour
- **Method**:
  - **PPU.cs RenderBGTile()（已實作）**：pre-compute `bgColor = NesColors[ppu_ram[0x3f00]&0x3f]` 移至 loop 外；合併重複 `(!ShowBgLeft8 && inLeft8)` 雙重判斷為單一 `masked` 變數；cases 1+2 合流為 `(masked || bgPixel == 0) ? bgColor : ...`
  - **MEM.cs init_function() mem_read_fun / VRAM dispatch**：這兩個是 init 時期的 if/else，執行時已是 O(1) function pointer 陣列索引，重排無效
- **Risk**: Low
- **Status**: ✅ DONE — **+0.4%** (187.70 → 188.55 FPS) — 效果微小但程式碼更簡潔，保留

---

### PRIORITY 9 — CPU opcode dispatch: function pointer table
- **Target**: CPU.cs `cpu_step_one_cycle()` — 目前外層 `switch(opcode)` 共 256 個 case，每個 case 內再用 `if/switch(operationCycle)` 判斷執行週期
- **Expected gain**: 2–5%（取決於 JIT 是否已最佳化 switch；function pointer table 可減少 branch miss）
- **Effort**: 4–6 hours（大型重構）
- **Method**:
  ```csharp
  // 建立 256 個 delegate，每個對應一個 opcode 的所有週期邏輯
  static Action[] opHandlers = new Action[256];

  static void InitOpHandlers()
  {
      opHandlers[0x09] = () => { GetImmediate(); Op_ORA(dl); CompleteOperation(); };
      opHandlers[0x05] = () => {
          if (operationCycle == 1) GetAddressZeroPage();
          else { Op_ORA(CpuRead(addressBus)); CompleteOperation(); }
      };
      // ... 依此類推
  }

  // cpu_step_one_cycle() 改為：
  opHandlers[opcode]();
  ```
  - 外層 switch 改為單一 array index，消除 JIT switch jump table 的間接跳躍
  - 每個 handler 仍保有內層 operationCycle 邏輯
- **Risk**: High — 256 個 opcode 全部重構，容易遺漏；需要完整跑過 AccuracyCoin 136/136 + blargg 174/174 驗證
- **Note**: JIT 本身會將 switch 編譯為 jump table，實際增益需實測才能確認；可先實作 10-20 個高頻 opcode（LDA/STA/BNE/JMP/JSR/RTS）做對比測試再決定是否全面替換
- **Status**: ✅ DONE — **+7.6%** (189.75 → 204.10 FPS)；Debug JIT 對 switch 未完全 jump table 最佳化，delegate array 明顯更快

---

### PRIORITY 10 — Instruction dispatch table (full replace, defer)
- **Target**: CPU.cs — 完整替換 256-case switch（PRIORITY 9 的延伸）
- **Expected gain**: 2–4%（與 Priority 9 合併後確認）
- **Risk**: High — large refactor
- **Note**: Priority 9 已全面替換，Priority 10 視為已完成
- **Status**: ✅ DONE (合併到 Priority 9)

---

### PRIORITY 11 — 將 managed array 改為 unsafe pointer 存取
- **Target**: APU.cs、PPU.cs — 熱路徑中仍使用 managed array 的部分
- **Expected gain**: 1–3%（消除 managed array bounds check 及 2D index 計算）
- **Effort**: ~2 hours
- **Method**:

  **① APU.cs `DUTYLOOKUP[4,8]`（2D → 1D flatten + unsafe pointer）**
  - 目前：`static int[,] DUTYLOOKUP = new int[4,8];`，每次存取 `DUTYLOOKUP[duty, seq]`
  - 改為：`static int* DUTYLOOKUP;`（Marshal.AllocHGlobal 32 ints）
  - 存取：`DUTYLOOKUP[_pulseDuty[i] * 8 + _pulseSeq[i]]`
  - 熱路徑：`apu_step()` 每 CPU cycle 呼叫，約 **3.6M 次/秒**

  **② APU.cs `TRI_SEQ[32]`（managed readonly → unsafe pointer）**
  - 目前：`static readonly int[] TRI_SEQ = { 15,14,...,15 };`
  - 改為：`static int* TRI_SEQ;`（Marshal.AllocHGlobal 32 ints，initAPU 初始化）
  - 熱路徑：`apu_step()` 每 CPU cycle，約 **1.8M 次/秒**

  **③ PPU.cs `secondaryOAM[32]`（managed byte[] → unsafe byte*）**
  - 目前：`static byte[] secondaryOAM = new byte[32];`
  - 改為：`static byte* secondaryOAM;`（Marshal.AllocHGlobal 32 bytes）
  - 熱路徑：sprite evaluation，每 scanline dot 多次存取

  **④ PPU.cs `corruptOamRow[32]`（managed bool[] → unsafe byte*）**
  - 目前：`static bool[] corruptOamRow = new bool[32];`
  - 改為：`static byte* corruptOamRow;`（0=false, 1=true）
  - 判斷由 `if (corruptOamRow[i])` 改為 `if (corruptOamRow[i] != 0)`

- **Risk**: Low — codebase 已大量使用相同的 `Marshal.AllocHGlobal` + unsafe pointer 模式（NES_MEM、ppu_ram、ScreenBuf1x 等），此改法為既有慣例
- **Verify**: 改後跑 AccuracyCoin 136/136 + blargg 174/174
- **Status**: ✅ DONE — **+3.3% improvement** (181.70 → 187.70 FPS, +120 frames/20s)

---

### PRIORITY 12 — RAM read fast-path in Mem_r() / CpuRead()
- **Target**: MEM.cs `Mem_r()` + CPU.cs `CpuRead()` — 目前所有記憶體讀取都走 `mem_read_fun[addr](addr)` 陣列 dispatch
- **Expected gain**: 5–10%
- **Effort**: ~30 分鐘
- **Method**: 在陣列 dispatch 前加 `$0000–$1FFF` fast path，直接 `return NES_MEM[addr & 0x7FF]`：
  ```csharp
  // Mem_r() 最前面
  if (addr < 0x2000) { tick(); return NES_MEM[addr & 0x7FF]; }
  // 其他地址走原有 mem_read_fun[addr](addr)
  ```
  - RAM read 是最高頻的記憶體操作（stack、zero page、PRG code 大量落在此範圍）
  - 省掉 delegate lookup + 間接 call overhead，直接 inline array access
  - `mem_read_fun[addr]` 在 $0000–$1FFF 只是 `NES_MEM[addr & 0x7FF]`，等效替換
- **Risk**: Low — RAM 行為固定，不涉及 mapper 或 PPU；需確認 open bus 行為在此範圍不需要特殊處理
- **Verify**: blargg 174/174 + AC 136/136
- **Status**: ✅ DONE — **+2.8%** (227.40 → 233.75 FPS)；blargg 174/174 + AC 136/136 驗證通過

---

### PRIORITY 13 — Cache mapper==4 static bool（A12 通知 + Increment2007 fast path）
- **Target**: PPU.cs `ppu_rendering_tick()` + `Increment2007()` — 每次呼叫都做 `if (mapper == 4)` 或 cast 判斷
- **Expected gain**: 1–2%
- **Effort**: ~15 分鐘
- **Method**: 在 `init()` 或 mapper 載入時設置一次：
  ```csharp
  static bool isMapper4 = false;  // set in init_function() after mapper load
  // ppu_rendering_tick() / Increment2007() 改用 if (isMapper4) 取代 if (mapper == 4)
  ```
  - `ppu_rendering_tick()` 每 PPU dot 呼叫（每幀 ~89,342 次），目前每次都要做 int 比較
  - `Increment2007()` 每次 $2007 存取都呼叫，遊戲中頻率高
  - 非 MMC3 遊戲（佔多數）完全跳過 A12 通知邏輯
- **Risk**: Low — 純 cache，mapper 不會中途改變
- **Status**: ❌ FAILED — 實測 **-2.2%** (237.00 → 231.80 FPS)，已 revert
  - 原因：`bool` 欄位 load + branch vs. `int` load + cmp + branch，在 Debug JIT 下沒有改善；推測 int `mapper` 與其他高頻欄位在同一 cache line，而新增的 `isMapper4` 位於不同位置，反而增加 cache miss

---

### PRIORITY 14 — Buffer_BG_array scanline clear 改用 Buffer.BlockCopy / InitBlock
- **Target**: PPU.cs `ppu_step_new()` scanline 開頭 — 目前用 for loop 清 256 int（1KB）
- **Expected gain**: 1–3%
- **Effort**: ~15 分鐘
- **Method**:
  ```csharp
  // 目前: for (int i = 0; i < 256; i++) Buffer_BG_array[i] = 0;
  // 改為: unsafe { fixed (int* p = Buffer_BG_array) NativeMemory equivalent or:
  Array.Clear(Buffer_BG_array, 0, 256);
  // 或 unsafe InitBlock:
  fixed (int* p = Buffer_BG_array) { for (int* q=p; q<p+256; q++) *q=0; }
  ```
  - 或改為 `Buffer_BG_array` 是 `int*`（unsafe pointer），用 `NativeMemory`/`Unsafe.InitBlock`
  - `Array.Clear()` 在 .NET 內部有 JIT 向量化，比逐元素 loop 快
  - 每幀 240 scanline × 1KB = 240KB 清零工作
- **Risk**: Low — 純清零，無邏輯變動
- **Status**: ✅ DONE — **+1.2%** (237.00 → 239.95 FPS)；blargg 174/174 + AC 136/136 驗證通過
  - 實際改法：`Buffer_BG_array` 和 `ScreenBuf1x` 改為指標 loop（`int* p; *p++ = 0`），避免每次迭代的 `scanOff + i` index 加法

---

### PRIORITY 15 — generateSample() Volume 乘法改為預計算 float 係數
- **Target**: APU.cs `generateSample()` — 每個 sample 執行 `mixed * Volume / 100`（整數除法）
- **Expected gain**: 1–2%
- **Effort**: ~10 分鐘
- **Method**:
  ```csharp
  // 目前: short sample = (short)(mixed * Volume / 100);
  // 改為: 在 Volume setter 或初始化時: static float _volumeScale = Volume / 100f;
  // generateSample(): short sample = (short)(mixed * _volumeScale);
  ```
  - 整數除法（`/ 100`）在 x86 是較貴的指令；改為 float 乘法更快
  - 每秒 44,100 次 × Volume change 很少 → 預計算幾乎沒有 cache miss
  - `_volumeScale` 在 Volume 改變時（UI 操作）重新計算即可
- **Risk**: Low — 輸出差異在 ±1 sample 內（float 精度），聽覺上無差別
- **Status**: ⏭ SKIP — `generateSample()` 開頭有 `if (!AudioEnabled) return;`，benchmark 模式（無音效）下整個函式直接回傳，此優化對 FPS 無效。僅在實際音效播放時有意義。

---

### PRIORITY 16 — Lazy N/Z flag evaluation（cache lastNZResult）
- **Target**: CPU.cs — `SetNZ()` 每次呼叫做 2–3 ops（bit shift + comparison），每幀被呼叫 ~57 種位置
- **Expected gain**: 0.5–1.5%
- **Effort**: ~1 小時（需修改多處，非單一函式）
- **Method**:
  1. 新增 `static byte lastNZResult = 0;` 作為 lazy cache
  2. `SetNZ(byte val)` 改為只存 `lastNZResult = val;`（1 assignment，省去 bit shift + comparison）
  3. 8 個 branch handler 改讀 cache：
     - `BPL/BMI`：`(lastNZResult & 0x80) == 0` / `!= 0`
     - `BEQ/BNE`：`lastNZResult == 0` / `!= 0`
  4. `GetFlag()` 從 cache 派生 N,Z：
     ```csharp
     byte n = (byte)((lastNZResult & 0x80) >> 7);
     byte z = (lastNZResult == 0) ? (byte)1 : (byte)0;
     ```
  5. `SetFlag()`（PLP/RTI 用）加上 cache 同步：
     ```csharp
     // 重建 lastNZResult：N→bit7，Z→0或1
     lastNZResult = (byte)((flagN != 0 ? 0x80 : 0) | (flagZ == 0 ? 0xFF : 0x00));
     ```
  6. `Op_CMP()` 和 BIT 指令直接寫 flagN/flagZ 的地方加 `lastNZResult = sub_result;`
- **Risk**: Low — 邏輯等效替換；需確認所有直接讀 `flagN`/`flagZ` 的地方都改為讀 cache
  - 特別注意：`flagN` / `flagZ` 在 CPU.cs 以外是否被讀取（需 grep 確認）
  - 若有遺漏會導致 branch 條件錯誤 → blargg 立即暴露
- **Verify**: blargg 174/174 + AC 136/136
- **Status**: ❌ FAILED — 實測 **-1.9%** (239.95 → 235.50 FPS)，已 revert
  - blargg 174/174 通過（邏輯正確），但效能退步
  - 實際實作：用 `nz_n`（bit-7 position）+ `nz_z`（raw value, 0=Z set）取代 `flagN`/`flagZ`，省去 `>>7` shift 和 `==0?1:0` conditional
  - 原因推測：GetFlag 的 `(nz_z == 0 ? 1 : 0) << 1` 比 `flagZ << 1` 更貴；branch handler lambda 內的反向 Z 編碼增加計算；新 field 位置可能增加 cache miss

---

### PRIORITY 17 — JIT enregistration: 將熱路徑 static 讀取改為 method-local 變數

- **Target**: PPU.cs `RenderBGTile()` / `ppu_step_new()` / `ppu_rendering_tick()`；APU.cs `apu_step()`
- **Expected gain**: 3–6%（總和，分三個子項）
- **Effort**: ~1–2 小時
- **背景原理**:
  - **static 欄位**在 JIT 中永遠是 memory-resident（無論 Debug/Release），每次讀取都是記憶體 load，無法放入 CPU register，因為它可能被其他執行緒或 debugger 觀察
  - **local 變數**可被 JIT enregister（Debug 模式保守但仍比 static 少一層間接定址）
  - 「在 loop 開頭 shadow 一次 → loop 內使用 local → loop 結束後 writeback（若有修改）」是標準做法
  - 適合對象：在緊密 loop 或高呼叫頻率 function 內**多次讀取、中途不被外部修改**的 static 欄位

---

#### 17A — `RenderBGTile()` 8-pixel 內層 loop：cache 4 個 statics

- **呼叫頻率**: 每幀 `240 scanlines × 33 tiles = ~7,920 次`；每次 8 iterations = ~63,360 次 loop body
- **候選 statics**（每次 loop iteration 都從記憶體重讀）:

  | Static 欄位 | 類型 | 目前讀取次數/call | 可否 shadow |
  |------------|------|-----------------|-------------|
  | `FineX` | `int` | 8×（`15 - loc - FineX`） | ✅ loop 中不變 |
  | `ShowBgLeft8` | `bool` | 8×（`!ShowBgLeft8 && screenX < 8`） | ✅ loop 中不變 |
  | `lowshift` | `ushort` | 8×（bit extraction） | ✅ loop 中不變（RenderBGTile 不寫 lowshift） |
  | `highshift` | `ushort` | 8×（bit extraction） | ✅ loop 中不變 |

- **Method**:
  ```csharp
  static void RenderBGTile()
  {
      // ... palette cache setup (unchanged) ...
      int fineX = FineX;          // shadow: 1 load → 8 register reads
      bool bgLeft8 = ShowBgLeft8; // shadow
      ushort ls = lowshift;       // shadow
      ushort hs = highshift;      // shadow
      int baseX = ppu_cycles_x - 7;
      int scanOff = scanline << 8;
      for (int loc = 0; loc < 8; loc++)
      {
          int bit = 15 - loc - fineX;       // register
          int bgPixel = ((ls >> bit) & 1) | (((hs >> bit) & 1) << 1);  // register
          bool masked = !bgLeft8 && (baseX + loc) < 8;  // register
          // ...
      }
      // lowshift/highshift 不需要 writeback（RenderBGTile 不修改它們）
  }
  ```
- **Risk**: Very Low — 純 shadow read，無 writeback（lowshift/highshift 在 ppu_step_new case 7 才更新，RenderBGTile 只讀不寫）
- **Expected gain**: ~1–2%

---

#### 17B — `ppu_rendering_tick()` + `ppu_step_new()` 傳遞 `cx` 參數

- **呼叫頻率**: `ppu_step_new()` 每幀 89,342 次（341 dots × 262 scanlines），`ppu_rendering_tick()` 每幀 ~59,000 次（僅 visible/pre-render + rendering enabled）
- **問題**: `ppu_cycles_x`（全域 static）在 `ppu_rendering_tick()` 中被讀取 **約 15 次**（多層 if/else if + switch）；在 `ppu_step_new()` 中讀取 **約 12 次**（多個 `ppu_cycles_x == N` 條件）
  - 每次讀取都是一次 memory load（static 欄位無法 enregister）
  - 總計約 27 次 static load 可被消除

- **Method**: 改為傳參
  ```csharp
  // ppu_step_new() 開頭 shadow：
  int cx = ppu_cycles_x;
  int sl = scanline;
  // ... 函式本體全部用 cx / sl 取代 ppu_cycles_x / scanline ...
  ppu_cycles_x = cx + 1;   // writeback（目前的 ppu_cycles_x++ 改為此）

  // ppu_rendering_tick() 改為接受參數：
  static void ppu_rendering_tick(int cx, int sl)
  { ... }  // 內部全改用 cx / sl

  // RenderBGTile() 同樣接受：
  static void RenderBGTile(int cx, int sl)
  { int baseX = cx - 7; int scanOff = sl << 8; ... }
  ```
- **注意**: `ppu_rendering_tick()` 呼叫 `NotifyMapperA12()`，後者讀取 `scanline` 計算 `scanline * 341 + ppu_cycles_x`；需一併傳入或改用 sl*341+cx 表達式
- **Risk**: Low — 純 refactor，行為等效；函式簽名改變但全在 PPU.cs partial class 內
- **Expected gain**: ~2–3%

---

#### 17C — `apu_step()` pulse block：shadow per-channel timer/period/seq/duty

- **呼叫頻率**: 每幀 ~29,830 次；pulse block（`if ((apucycle & 1) == 0)`）每幀 ~14,915 次
- **候選 statics**（在 pulse block 內重複讀取）:

  | Static | 讀取次數/call | 寫入？ |
  |--------|-------------|--------|
  | `_pulsePeriod[0]` / `[1]` | 3× each（timer reset + output guard × 2） | ❌ 只讀 |
  | `_pulseDuty[0]` / `[1]` | 1× each（DUTYLOOKUP index） | ❌ 只讀 |
  | `lengthctr[0]` / `[1]` / `[3]` | 1× each | ❌ 只讀 |
  | `sweepsilence[0]` / `[1]` | 1× each | ❌ 只讀 |

- **Method**:
  ```csharp
  if ((apucycle & 1) == 0)
  {
      int p0 = _pulsePeriod[0], p1 = _pulsePeriod[1];  // shadow
      int d0 = _pulseDuty[0],   d1 = _pulseDuty[1];
      int lc0 = lengthctr[0], lc1 = lengthctr[1];
      int sw0 = sweepsilence[0], sw1 = sweepsilence[1];

      // Pulse 1
      if (--_pulseTimer[0] < 0) { _pulseTimer[0] = p0; _pulseSeq[0] = (_pulseSeq[0]+1)&7; }
      _pulseOut[0] = (p0 >= 8 && lc0 > 0 && sw0 == 0) ? DUTYLOOKUP[d0*8+_pulseSeq[0]] : 0;

      // Pulse 2 (similar)
      // Noise (lengthctr[3] / _noiseLfsr — shorter, less benefit)
  }
  ```
  - `_pulseTimer` 仍需讀寫（有 `--_pulseTimer[i]` 修改），不能 shadow
  - `_pulseSeq` 在輸出計算後已取新值，需用更新後的值
- **Risk**: Very Low — 只有 read-only statics 被 shadowed，寫入路徑（timer/seq）保持直接存取
- **Expected gain**: ~0.5–1%

---

- **實作順序建議**: 17A（最安全最快）→ 17B（需要改 signature）→ 17C（小補充）
- **Verify**: blargg 174/174 + AC 136/136（各子項分開驗證）
- **Status**: ✅ DONE — **+1.4%** (233.75 → 237.00 FPS)；blargg 174/174 + AC 136/136 驗證通過
  - 17A（RenderBGTile loop shadow）：單獨測試 0%，整合後協同有效
  - 17B（ppu_cycles_x local + cx param）：主要貢獻
  - 17C（apu pulse/tri shadow）：小幅貢獻

---

### PRIORITY 18 — AggressiveInlining：符合 JIT 條件的候選方法

- **Target**: PPU.cs — 高頻呼叫但缺少 `[MethodImpl(AggressiveInlining)]` 的小型方法
- **Expected gain**: 0.5–2%（各子項分開測試）
- **背景原理**:
  - Debug JIT 預設不 inline 任何方法（即使很小），除非有 `AggressiveInlining` 屬性或方法體極度簡單（~1-2 行）
  - `AggressiveInlining` 繞過大小限制，強制嘗試 inline，但以下情況 **JIT 仍無法 inline**：
    1. 方法含 `goto` label（`goto` 指令本身 JIT 無法追蹤）→ 代碼跳轉超出 inline 分析範圍
    2. 方法含 `try/catch/finally`（異常表結構複雜）
    3. 虛擬方法（runtime polymorphism，JIT 不知道實際型別）
  - 過大的方法（>30 lines）force-inline 會 **降低效能**（I-cache 壓力 + 呼叫方 JIT 最佳化困難）

---

#### 不可加 AggressiveInlining（即使加了也沒效果）

| 方法 | 原因 |
|------|------|
| `clockdmc()` APU.cs | 含 `goto deferredStatus:` label → JIT 無法 inline |
| `ppu_rendering_tick(int cx)` PPU.cs | ~80 lines → 太大，force-inline 造成 I-cache 壓力 |
| `ppu_step_new()` PPU.cs | ~180 lines → 遠超合理 inline 大小 |
| `SpriteEvalWrite()` PPU.cs | ~100 lines → 太大 |
| `RenderBGTile(int cx)` PPU.cs | ~35 lines → borderline，若要試放在 18B |

---

#### 18A — PPU.cs 小型 sprite eval 方法

- **候選方法**（無 `goto`、無 exception、體積小）：

  | 方法 | 行數 | 呼叫頻率 | 說明 |
  |------|------|----------|------|
  | `SpriteEvalInit()` | 8 lines | 240/frame（dot 65 per scanline） | 初始化 FSM，8 個賦值 |
  | `SpriteEvalEnd()` | 3 lines | 240/frame（dot 256 per scanline） | 3 個賦值，極小 |
  | `SpriteEvalTick()` | 15 lines | ~46K/frame（dots 65-256 per scanline） | 最熱，但呼叫 SpriteEvalWrite() |
  | `Yinc()` | 18 lines | 240/frame（dot 256 via ppu_rendering_tick） | Y scroll increment |

- **Method**: 在各方法定義前加 `[MethodImpl(MethodImplOptions.AggressiveInlining)]`
- **Risk**: Very Low — 純 attribute 加法，不改邏輯
- **Status**: ✅ DONE — +2.2% (239.95 → 245.30)；blargg 174/174 + AC 136/136 驗證通過

---

#### 18B — PPU.cs `RenderBGTile(int cx)` inline into ppu_rendering_tick

- **背景**: `RenderBGTile()` 每幀 7,920 次（每 visible scanline 每 tile call 一次），體積 ~35 lines
- **風險評估**:
  - 35 lines 屬 borderline — Debug JIT 的 I-cache 影響不如 Release 嚴重
  - `ppu_rendering_tick()` 本身已是 80 lines，加上 inline 後約 115 lines，可能造成 JIT 最佳化困難
- **Verdict**: 先嘗試 18A；若效果不足再試 18B
- **Status**: ❌ FAILED — 實測負效益 -1.5%（245.30 → 241.65）
  - 35 lines inline 後 `ppu_rendering_tick()` 膨脹至 ~115 lines，I-cache 壓力劣化。

---

- **Verify**: blargg 174/174 + AC 136/136
- **Status**: ✅ DONE（18A）；18B ❌ FAILED

---

### PRIORITY 19 — APU apu_step() 稀有路徑合併 guard

- **Target**: APU.cs — `apu_step()` 中每個 CPU cycle 都執行兩個幾乎永遠為 false 的獨立 if 判斷
- **Expected gain**: 0.3–1%（每 CPU cycle 省一次 branch + memory load）
- **Effort**: ~5 min
- **背景**:
  - `frameIrqClearPending`：每幀僅在極少數 cycle 為 true（$4015 write 後才設置）
  - `irqAssertCycles`：每幀僅在 frame counter step 後 2–3 個 cycle 內 > 0
  - 兩者每個 CPU cycle（~1.79M/frame）各自需一次 memory load + branch
  - Branch predictor 通常預測正確（always not-taken），但仍需每次 load static field

- **Method**: 合併成一個 outer guard，讓絕大多數 cycle 只需一次判斷即可跳過：
  ```csharp
  // 目前（兩個獨立 if，各需一次 load + branch）：
  if (frameIrqClearPending && (cpuCycleCount & 1) == 0) { ... }
  if (irqAssertCycles > 0) { ... }

  // 改為（一個 outer guard 合併兩個稀有路徑）：
  if (frameIrqClearPending || irqAssertCycles > 0)
  {
      if (frameIrqClearPending && (cpuCycleCount & 1) == 0)
      {
          statusframeint = false;
          frameIrqClearPending = false;
      }
      if (irqAssertCycles > 0)
      {
          statusframeint = true;
          frameIrqClearPending = false;
          --irqAssertCycles;
          if (irqAssertCycles == 0 && apuintflag)
              statusframeint = false;
      }
  }
  ```
  當兩者都 false 時（99.99% 的情況），只需一次 `||` 短路判斷即可跳過兩個 block。

- **Risk**: Low — 邏輯不變，僅結構調整
- **Verify**: blargg 174/174 + AC 136/136
- **Status**: ❌ FAILED — 實測負效益 -1.0%（245.30 → 242.90）
- **失敗原因**: 加入 outer `||` guard 本身需要一次 combined bool evaluate（兩次 load + OR），
  比兩個獨立 if 的分支預測 overhead 還高。JIT 對兩個獨立的永遠-not-taken branch 預測效率更好，
  outer guard 反而增加了 code path complexity，造成 JIT 最佳化劣化。

---

### PRIORITY 20 — RenderBGTile() 內層 8-pixel 迴圈展開

- **Target**: PPU.cs — `RenderBGTile(int cx)` 中 `for (int loc = 0; loc < 8; loc++)` 固定 8 次迴圈
- **Expected gain**: 0.5–1.5%（消除迴圈控制 overhead，每幀 7,680–9,600 次呼叫）
- **Effort**: ~15 min
- **背景**:
  - 每個可見 scanline 有 32 tiles + 8 prefetch tiles = 最多 40 次 RenderBGTile 呼叫（可見 240 scanlines）
  - 每次呼叫都有 `for (int loc = 0; loc < 8; loc++)` 共 8 次疊代
  - 固定 8 次 = 可完全手動展開，完全消除 `loc++`、`loc < 8`、迴圈 counter 的 overhead
  - Debug JIT 不自動展開小型迴圈

- **展開 pattern**（8 次手動展開）：
  ```csharp
  // 展開 loc=0..7，每個都是：
  {
      const int loc = 0;  // 或直接用常數
      int bit = 15 - loc - fineX;  // = 15 - fineX（loc=0 固定）
      byte attrUse = (bit >= 8) ? renderAttr : nextAttr;
      int bgPixel = ((ls >> bit) & 1) | (((hs >> bit) & 1) << 1);
      bool masked = !bgLeft8 && (baseX + loc) < 8;
      int slot = scanOff + baseX + loc;
      Buffer_BG_array[slot] = masked ? 0 : bgPixel;
      uint* pal = (attrUse == renderAttr) ? palCacheR : palCacheN;
      ScreenBuf1x[slot] = masked ? bgColor : pal[bgPixel];
  }
  // ... loc=1, loc=2, ..., loc=7
  ```
  注意：`bit = 15 - loc - fineX` 中的 `loc` 是已知常數 0–7，JIT 可進一步常數折疊。

- **Risk**: Low — 邏輯完全等價，只是把迴圈展開
- **Verify**: blargg 174/174 + AC 136/136
- **Status**: ❌ FAILED — 實測負效益 -1.8%（245.30 → 240.85）
- **失敗原因**: 展開後 RenderBGTile 方法體積大幅增加，造成 JIT I-cache 壓力 + 方法過大難以最佳化。
  JIT 對 8 次小型迴圈的 loop unrolling 已內建優化，手動展開反而劣化。

---

### PRIORITY 21 — RenderBGTile() 調色盤快取去重複（attr 不變時跳過刷新）

- **Target**: PPU.cs — `RenderBGTile()` 每次都重新讀取 palette 顏色（7 次 ppu_ram + NesColors 查詢）
- **Expected gain**: 0.5–2%（對 attr 穩定的畫面，大量節省記憶體查詢）
- **Effort**: ~15 min
- **背景**:
  - 每個 tile 開始時，從 `bg_attr_p3` / `bg_attr_p2` 讀取 attr，再查 7 個調色盤顏色
  - 連續的 tile 若屬於同一 attribute block（8×8 tile 共享 palette），attr 不會改變
  - 一般遊戲中，同一 scanline 上大量相鄰 tile 有相同 attr（實心背景、單一色彩區域）
  - 可快取上次 renderAttr / nextAttr，只有變化時才刷新 palCacheR / palCacheN

- **Method**:
  ```csharp
  static byte lastRenderAttr = 0xFF; // impossible attr value
  static byte lastNextAttr   = 0xFF;

  static void RenderBGTile(int cx)
  {
      byte renderAttr = bg_attr_p3;
      byte nextAttr   = bg_attr_p2;

      if (renderAttr != lastRenderAttr || nextAttr != lastNextAttr)
      {
          lastRenderAttr = renderAttr;
          lastNextAttr   = nextAttr;
          int baseAddrR = 0x3f00 | (renderAttr << 2);
          int baseAddrN = 0x3f00 | (nextAttr   << 2);
          uint bgColor = NesColors[ppu_ram[0x3f00] & 0x3f];
          palCacheR[0] = palCacheN[0] = bgColor;
          palCacheR[1] = NesColors[ppu_ram[baseAddrR + 1] & 0x3f];
          // ... etc.
      }
      uint bgColor2 = palCacheR[0]; // 從快取讀取
      // ... render pixels
  }
  ```

- **Risk**: Medium
  - `ppu_ram` 調色盤可在渲染途中被寫入（mid-frame palette write）
  - 若 palette 顏色被寫入但 attr 未改變，快取會返回舊顏色
  - 正確處理：同時在 `ppu_w_palette()` 設置 `paletteChanged = true` flag，強制下一 tile 刷新
  - 若加入 paletteDirty flag 較繁瑣，可先不加、跑測試，若通過則 palette mid-write 問題不常見

- **Verify**: blargg 174/174 + AC 136/136
- **Status**: ❌ FAILED — 實測負效益 -1.4%（245.30 → 241.85）
- **失敗原因**: 每個 tile 需額外執行 3 個條件判斷（renderAttr != last, nextAttr != last, paletteChanged），
  Mega Man 5 中 attr 變化頻繁（複雜背景），cache miss 率高，條件判斷 overhead 超過節省的 palette lookup。
  對 attr 穩定的畫面（純色背景）理論上有效，但不適用於複雜場景。

---

### PRIORITY 22 — setenvelope() / setsweep() 小型迴圈展開

- **Target**: APU.cs — `setenvelope()` 4-iteration loop、`setsweep()` 2-iteration loop
- **Expected gain**: 0.1–0.3%（低頻呼叫，僅約 240/frame，但展開乾淨）
- **Effort**: ~20 min
- **背景**:
  - `setenvelope()`：`for (int i = 0; i < 4; ++i)` — 固定 4 次
  - `setsweep()`：`for (int i = 0; i < 2; ++i)` — 固定 2 次
  - 均由 frame counter 驅動，每幀 ~240 次（4-step mode 4 次 step，每 step 觸發 envelope + sweep）
  - Debug JIT 不展開，手動展開消除迴圈 overhead
  - 影響相對低，但展開後程式碼更清晰且無 counter variable
- **Risk**: Low — 純展開，邏輯完全等價
- **Verify**: blargg 174/174 + AC 136/136
- **Status**: ❌ FAILED — 實測負效益 -1.0%（245.30 → 242.85）
- **失敗原因**: 展開後方法體積變大，JIT 最佳化困難。Debug JIT 對 4/2 次小型迴圈已有良好處理，
  手動展開反而造成 I-cache 壓力，與 P20 同樣模式。

---

### PRIORITY 23 — IO_read / IO_write switch → function pointer array

- **Target**: IO.cs — `IO_read()` 11-case switch + `IO_write()` 27-case switch
- **Expected gain**: 0.1–0.5%（呼叫頻率低，但消除 switch 比對 overhead）
- **Effort**: ~30 min
- **背景**:
  - `IO_read(addr)` / `IO_write(addr, val)` 對 PPU ($2000-$2007) 和 APU ($4000-$4017) 寄存器做 switch 分派
  - 與 Priority 9（CPU opcode dispatch，+7.6%）同樣模式，但**呼叫頻率差很多**：
    - CPU opcode dispatch：~1.7M 次/frame
    - IO_read：~幾百次/frame（ppu_r_2002/joypad/APU status）
    - IO_write：~幾十到幾百次/frame（遊戲對 PPU/APU 寄存器的設定）
  - Switch 27 case → JIT 可能已生成 jump table（case 連續），但仍需範圍 bound check
  - 改成 `Action<byte>[]` / `Func<byte>[]` 陣列（indexed by `addr - 0x2000`）
    可直接消除所有 switch 比對

- **Method**:
  ```csharp
  // 陣列大小：0x4018 - 0x2000 = 0x2018 = 8216 entries（含 gap 全填 default handler）
  static Action<byte>[] io_write_table = new Action<byte>[0x2018];
  static Func<byte>[]   io_read_table  = new Func<byte>[0x2018];

  // 初始化（在 init_function() 中）：
  // 先全填 default：
  for (int i = 0; i < 0x2018; i++) {
      io_write_table[i] = _ => {};               // no-op
      io_read_table[i]  = () => cpubus;          // open bus
  }
  // 再填各寄存器：
  io_write_table[0x2000 - 0x2000] = v => ppu_w_2000(v);
  io_write_table[0x2001 - 0x2000] = v => ppu_w_2001(v);
  // ... etc.
  io_read_table[0x2002 - 0x2000] = () => ppu_r_2002();
  // ... etc.

  // 分派（不再需要 switch）：
  static byte IO_read(ushort addr) {
      if (addr < 0x4000) addr = (ushort)(0x2000 | (addr & 7));
      int idx = addr - 0x2000;
      if ((uint)idx < 0x2018) return io_read_table[idx]();
      return cpubus;
  }
  static void IO_write(ushort addr, byte val) {
      if (addr < 0x4000) addr = (ushort)(0x2000 | (addr & 7));
      int idx = addr - 0x2000;
      if ((uint)idx < 0x2018) io_write_table[idx](val);
  }
  ```

- **注意事項**:
  - `$4017` write 有複雜 inline 邏輯（jitter 計算、frame counter 初始化），需提取為 helper method `apu_4017(val)`
  - `$4009` / `$400d` 目前 switch 無對應 case → 填 no-op
  - memory footprint 增加：8216 × 2 張 delegate table（每個 ~8 bytes ptr）= ~131 KB，可接受
  - Lambda delegate 呼叫比直接方法呼叫略慢（virtual dispatch）；但比 switch chain 快

- **Risk**: Low — 邏輯完全等價，只是分派方式改變
- **Verify**: blargg 174/174 + AC 136/136
- **Status**: 🔲 TODO

---

## Failed / Ineffective Attempts

*(None yet)*

---

## Notes

- All tests use the same ROM, same machine, same duration (20s)
- Build must be **Debug x64** to match baseline (same JIT optimization level)
- If machine load varies, re-run baseline before comparing
- AccuracyCoin 136/136 and blargg 174/174 must still pass after each change
