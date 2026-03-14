# AprNes C# NES 模擬器效能最佳化技術分析

> **研究對象**: AprNes — C# / .NET 4.8 / unsafe code / Windows Forms NES 模擬器
> **測試平台**: Mega Man 5 (USA).nes（Mapper 004 MMC3，複雜場景，代表性負載）
> **Benchmark 方法**: 20 秒無上限 FPS 跑分，headless 模式，無音效；每次測試前 `sleep 60` 等 CPU 降溫
> **結果（Debug 組態 #1–12）**: 181.70 FPS → 247.95 FPS（**+36.5%**）
> **結果（Release 組態 #13–14）**: 241.45 FPS → ~259 FPS（**+7.3%**，等效 Debug 基線 +42.5%）
> **.NET 10 RyuJIT**: ~348 FPS（同程式碼，JIT 升級帶來額外 ~34%）
> **正確性驗證**: blargg 174/174 + AccuracyCoin 136/136（每項最佳化後皆需全數通過）

---

## 一、背景與架構限制

### 1.1 執行環境特殊性

AprNes 的核心架構帶來幾個不尋常的最佳化限制：

- **Debug JIT**：由於使用 `.NET Framework 4.8 Debug` 組態（方便掛載 debugger），JIT 編譯器的最佳化等級遠低於 Release。許多在 Release 模式下由 JIT 自動處理的最佳化（loop unrolling、register allocation、inlining），在 Debug 模式下必須由程式設計師手動完成。
- **static-only 架構**：整個模擬器核心是一個龐大的 `partial class NesCore`，所有欄位均為 `static`。這代表 JIT 無法將任何欄位「enregister」（放入 CPU register），因為 static 欄位永遠是 memory-resident（可能被其他執行緒或 debugger 觀察）。每次讀取 `static int ppu_cycles_x` 都是一次記憶體 load。
- **unsafe pointer 為主**：NES 記憶體（64KB CPU space、PPU registers、framebuffer 等）均以 `Marshal.AllocHGlobal` 配置於非託管堆積，透過 `byte*` 直接存取，無 GC 壓力，亦無 managed array bounds check。
- **函式指標表分派**：65536 個 `mem_read_fun[]` / `mem_write_fun[]` managed delegate 在 init 時期依地址區間初始化，執行期 O(1) 分派，無地址範圍 if/switch。

### 1.2 效能熱點分佈（每幀）

| 子系統 | 主要呼叫頻率 | 備註 |
|--------|------------|------|
| CPU `cpu_step_one_cycle()` | ~29,830 次/幀 | 1.79 MHz ÷ 60 FPS |
| opcode dispatch | ~29,830 次/幀 | 其中 1/3 多週期指令 |
| `ppu_step_new()` | ~89,342 次/幀 | 341 dots × 262 scanlines |
| `ppu_rendering_tick()` | ~59,000 次/幀 | 僅 visible + pre-render |
| `RenderBGTile()` | ~7,920 次/幀 | 240 scanlines × 33 tiles |
| `apu_step()` | ~29,830 次/幀 | 每 CPU cycle 一次 |
| `Mem_r()` / `CpuRead()` | ~50,000–80,000 次/幀 | 估計，每指令 2–7 次 |

---

## 二、成功最佳化項目分析

### 2.1 Priority 9 / 24：CPU opcode dispatch 函式指標化（+7.6% + 1.08% = +8.7%）

這是整個最佳化過程中效益最大的單一改動，分兩個階段完成。

**階段一（P9）：`switch(opcode)` → `Action[] opHandlers`**

原始架構是 256-case switch，每個 case 用 `if/switch(operationCycle)` 判斷執行週期。Debug JIT 對 switch 的處理是「逐一比較 + conditional jump」，不一定生成 jump table（尤其 case 值不連續時）。

改為：

```csharp
static Action[] opHandlers = new Action[256];

// dispatch
opHandlers[opcode]();
```

每個 opcode 對應一個 `Action` delegate，dispatch 退化為陣列索引 + delegate call。結果：**+7.6%**（189.75 → 204.10 FPS）。

**階段二（P24）：`Action[]` → `delegate*<void>[]` unsafe function pointer**

`Action` delegate 在 JIT 層面的呼叫路徑：
1. Load `Action` 物件 reference（heap object）
2. Load `_methodPtr` from delegate struct
3. Load `_target` from delegate struct
4. Null check → 選擇 static/instance invoke path
5. Indirect `call` through `_methodPtr`

共 4–5 個額外 load/check。改為 C# 9 `delegate*<void>`（原生函式指標）後：
1. Load function pointer from array（一次 load）
2. Single `calli` 指令

等效於 C 語言的 `void (*table[256])()` function table。

**實作限制**：`delegate*` 不接受 lambda（closure）。原本 226 個 `() => { ... }` 全部改為具名靜態方法（`static void Op_09()`），工作量約 2 小時。

結果：**+1.08%**（245.30 → 247.95 FPS）。比預期的 5–15% 低，因為 `Mem_r()` / `tick()` 等函式的開銷已遠超 dispatch 本身。

---

### 2.2 Priority 6：移除 `RenderBGTile()` 內層冗餘 bounds check（+6.1%）

原始程式碼在 8-pixel 內層迴圈的最後一行有：

```csharp
if (screenX > 255) break;  // 防止超出螢幕邊界
```

但呼叫方 `ppu_rendering_tick()` 已有：

```csharp
if (ppu_cycles_x < 256) RenderBGTile(cx);
```

內層 check 是 dead code。移除後，**JIT 能對整個 8-pixel loop 做更積極的 unrolling 和向量化**，而不是保守地在每個 iteration 插入 exit condition。

結果遠超預期：**+6.1%**（204.10 → 216.45 FPS）。

**關鍵洞察**：一個看似無害的冗餘 branch，在 Debug JIT 下嚴重阻礙 loop 最佳化。任何內層迴圈中的非必要 conditional exit 都值得審視。

---

### 2.3 Priority 3：`ProcessPendingDma()` early-exit guard（+5.1%）

每次 `CpuRead()` 都會呼叫 `ProcessPendingDma()`，但絕大多數情況下並無 DMA 進行。原始實作是在函式開頭逐一檢查各 DMA flag。

加入 5-flag 組合 early-exit：

```csharp
if (!dmaNeedHalt && !dmcDmaRunning && !dmcNeedDummyRead &&
    !spriteDmaTransfer && !dmcImplicitAbortPending) return;
```

在無 DMA 的正常情況下（99%+ 的呼叫），函式在 5 次 bool load + OR 後立即返回，省去整個 DMA 邏輯。

結果：**+5.1%**（216.45 → 227.40 FPS）。

**注意**：原版 guard 漏掉 `!dmcNeedDummyRead` 和 `!dmcImplicitAbortPending`，導致正確性問題。完整覆蓋所有 DMA 入口條件至關重要。

---

### 2.4 Priority 11：managed array → unsafe pointer（+3.3%）

將四個熱路徑 managed array 改為 `Marshal.AllocHGlobal` unsafe pointer：

| 欄位 | 改前 | 改後 | 呼叫頻率 |
|------|------|------|---------|
| `DUTYLOOKUP[4,8]` | `int[,]` | `int*` | ~3.6M/秒（APU每cycle） |
| `TRI_SEQ[32]` | `int[]` | `int*` | ~1.8M/秒（APU每cycle） |
| `secondaryOAM[32]` | `byte[]` | `byte*` | ~每scanline多次 |
| `corruptOamRow[32]` | `bool[]` | `byte*` | ~每scanline多次 |

改善來源：
- 消除 managed array bounds check（每次存取）
- `int[,]` 2D array 需計算 `base + row*cols + col`，改為 flat pointer 後只需 `ptr[idx]`
- Pointer 存取比 managed reference 少一層間接定址

結果：**+3.3%**（181.70 → 187.70 FPS）。

---

### 2.5 Priority 12：RAM read fast-path（+2.8%）

$0000–$1FFF（2KB RAM，mirrored）是 NES 最高頻的讀取目標（zero page、stack、code）。原本每次都走完整 `mem_read_fun[addr](addr)` delegate dispatch：

```csharp
// 改前：每次都走 delegate dispatch
static byte Mem_r(ushort addr) {
    tick();
    cpubus = mem_read_fun[addr](addr);
    return cpubus;
}

// 改後：$0000–$1FFF fast path
static byte Mem_r(ushort addr) {
    if (addr < 0x2000) { tick(); return cpubus = NES_MEM[addr & 0x7FF]; }
    tick();
    cpubus = mem_read_fun[addr](addr);
    return cpubus;
}
```

RAM range 行為固定（只需 `& 0x7FF` miroring），完全不需 delegate lookup。

結果：**+2.8%**（227.40 → 233.75 FPS）。

---

### 2.6 Priority 18A：`[AggressiveInlining]` 小型 PPU 方法（+2.2%）

Debug JIT 預設不 inline 任何方法（除了極度簡單的 1–2 行 getter）。`[MethodImpl(AggressiveInlining)]` 強制 JIT 嘗試 inline。

成功的候選：

| 方法 | 行數 | 呼叫頻率 | 效果 |
|------|------|---------|------|
| `SpriteEvalTick()` | 15 | ~46K/幀 | 最主要貢獻 |
| `SpriteEvalInit()` | 8 | 240/幀 | 小幅 |
| `SpriteEvalEnd()` | 3 | 240/幀 | 小幅 |
| `Yinc()` | 18 | 240/幀 | 小幅 |

結果：**+2.2%**（239.95 → 245.30 FPS）。

**JIT inline 限制**（即使加了 attribute 也無效）：
- 含 `goto` label（如 `clockdmc()` 中的 `goto deferredStatus:`）
- 含 try/catch/finally
- 虛擬方法（runtime polymorphism）
- 方法體過大（>30 行）→ I-cache 壓力抵消收益

---

### 2.7 Priority 17：static 欄位 local shadow（+1.4%）

原理：Debug JIT 無法 enregister static 欄位，每次讀取都是記憶體 load。在函式開頭 shadow 至 local variable，JIT 可能將其放入 register（Debug 下仍保守，但比 static 少一層間接定址）。

主要改動：

**17B（主要貢獻）**：`ppu_step_new()` 中 shadow `ppu_cycles_x` 和 `scanline`：
```csharp
int cx = ppu_cycles_x;   // shadow：函式內 ~12 次讀取 → 1 次 load
int sl = scanline;        // shadow：函式內 ~8 次讀取 → 1 次 load
ppu_cycles_x = cx + 1;  // writeback（原本的 ppu_cycles_x++）
```

**17A**：`RenderBGTile()` 8-pixel 迴圈 shadow `FineX`、`ShowBgLeft8`、`lowshift`、`highshift`。

**17C**：`apu_step()` pulse block shadow `_pulsePeriod[]`、`_pulseDuty[]` 等。

結果：**+1.4%**（233.75 → 237.00 FPS）。

---

### 2.8 Priority 14：scanline buffer 清零用 pointer loop（+1.2%）

原本用 managed array loop 清零：
```csharp
for (int i = 0; i < 256; i++) Buffer_BG_array[i] = 0;
```

改為 unsafe pointer loop（`Buffer_BG_array` 和 `ScreenBuf1x` 均已是 `int*`/`uint*`）：
```csharp
int* p = Buffer_BG_array + scanOff;
for (int i = 0; i < 256; i++) *p++ = 0;
```

省去每次迭代的 `scanOff + i` index 計算和 managed bounds check。

結果：**+1.2%**（237.00 → 239.95 FPS）。

---

### 2.9 Priority 5：靜態調色盤快取（+0.6%）

`RenderBGTile()` 每次 8-pixel tile 都重新查詢調色盤（7 次 `ppu_ram[]` + `NesColors[]` lookup）。改為在 tile 開頭更新一次靜態快取 `palCacheR[4]` / `palCacheN[4]`（`Marshal.AllocHGlobal uint*`），loop 內直接 `palCacheR[bgPixel]`。

重要教訓：**v1 用 stackalloc 失敗（-0.3%）**，每次 tile 呼叫重新 allocate stack frame 的開銷超過節省。改為一次性 `Marshal.AllocHGlobal` 靜態分配（v2）才有效。

結果：**+0.6%**（188.55 → 189.75 FPS）。

---

### 2.10 Priority 8：`RenderBGTile()` 條件合流（+0.4%）

將 BG 遮罩邏輯的兩層重複判斷合併：
```csharp
// 改前：兩處各自判斷 (!ShowBgLeft8 && screenX < 8)
// 改後：
bool masked = !bgLeft8 && (baseX + loc) < 8;
Buffer_BG_array[slot] = masked ? 0 : bgPixel;
ScreenBuf1x[slot] = masked ? bgColor : pal[bgPixel];
```

結果：**+0.4%**（187.70 → 188.55 FPS）。效果微小但程式碼更簡潔。

---

## 三、失敗嘗試分析

### 3.1 「手動展開固定次數迴圈」反效果模式

P20（RenderBGTile 8-pixel 迴圈展開）、P22（setenvelope/setsweep 展開）均失敗，原因一致：

> 展開後方法體積大幅增加 → JIT I-cache 壓力上升 → 呼叫方 JIT 最佳化困難

Debug JIT 對 4–8 次固定迴圈已有良好內建處理，手動展開適得其反。**結論：不要手動展開固定次數的小型迴圈。**

---

### 3.2 「AggressiveInlining 過大方法」反效果模式

P18B（`RenderBGTile` inline into `ppu_rendering_tick`）：35 行 + 80 行 → ~115 行的超大方法，-1.5%。

**閾值約為 30 行**：超過此大小的方法，force-inline 造成的 I-cache 壓力往往超過消除 call overhead 的收益。

---

### 3.3 「Static field 加 bool cache 」未必有效

P13（cache `mapper == 4` → `isMapper4 bool`）：-2.2%。

原因：新增的 `isMapper4 bool` 欄位位於不同 cache line，每次存取還是一次 load；而原本的 `int mapper` 欄位與其他高頻欄位可能共在同一 cache line，一起被 prefetch。增加欄位數量有時**惡化** cache line 使用效率。

---

### 3.4 「合併 if 判斷」不一定節省 branch cost

P19（APU IRQ guard 合併）：-1.0%。

兩個獨立的永遠-not-taken branch，CPU branch predictor 各自預測正確（幾乎無 misprediction penalty）。加一個 outer `||` guard 需要同時 load 兩個欄位再 OR，實際上增加了計算量。

**結論**：Branch predictor 已很好地處理「幾乎永遠不成立」的條件。合併 guard 的前提是兩個 check 本身有 misprediction 問題，而非呼叫頻率問題。

---

### 3.5 `delegate*` 無法解決 instance method dispatch（P25/P26：-4.8%）

這是最值得深入討論的失敗案例。

**問題**：`mem_read_fun[]` 的 handler 中，ROM 讀取（$8000–$FFFF）走的是 `MapperObj.MapperR_RPG(addr)`，這是一個 instance method。`delegate*` 是原生函式指標，不能攜帶 `this` 指標。

唯一解法：加 static wrapper：
```csharp
static byte MemRead_MapperRPG(ushort addr) => MapperObj.MapperR_RPG(addr);
```

但這樣的呼叫路徑變成：

```
function pointer call → static wrapper → MapperObj.MapperR_RPG (virtual dispatch via interface)
```

比原來的 managed delegate（已內嵌 `_target = MapperObj`，一次呼叫直達）多一個 call hop。

ROM fetch 是所有記憶體操作中最頻繁的（每個 opcode fetch + 所有運算元），每次多一層 call 的累積效應壓垮了 dispatch 節省，導致 **-4.8%**。

**根本限制**：`delegate*` 的收益完全依賴「handler 是靜態函式」。凡是需要透過 wrapper 呼叫 instance method 的場景，不適合使用 `delegate*` 取代 managed delegate。

---

### 3.6 Mapper004 precomputed bank pointer（P27：-1.46%）

概念：預先計算 offset-corrected 指標（`prg0 = PRG_ROM + (PRG0_Bankselect << 13) - 0x8000`），讀取時直接 `prg0[address]`，省去 shift 和 subtract。

**失敗原因**：新增 12 個指標欄位（4 PRG + 8 CHR = 96 bytes）使 Mapper 物件散佈於更多 cache line。原本 `PRG_ROM`、`PRG0_Bankselect`、`PRG_Bankmode` 可能同在一個 cache line，被整包 prefetch；新增欄位後，存取 `prg0` 需要額外的 cache line load，抵消了省去算術的收益。

在 Debug JIT（無 register allocation 最佳化）下，field 數量對 cache line 佈局的影響比 Release 更敏感。

---

### 3.7 Low-frequency target 的函式指標化（P23：-0.1%）

IO_read/write（PPU/APU 寄存器分派）每幀僅被呼叫幾百次。將 27-case switch 改為 delegate table，每次呼叫的 overhead 從「switch 比對」改為「delegate call」，而後者本身的 overhead 反而更高。

**結論**：函式指標表最佳化只對高頻呼叫路徑（>100K 次/幀）有意義。CPU opcode dispatch（~30K/幀 × 多週期 ≈ 1.7M/幀）值得改；IO register dispatch（幾百次/幀）不值得。

---

## 四、整體規律與結論

### 4.1 最有效的最佳化類型

| 類型 | 代表項目 | 效益 | 為何有效 |
|------|---------|------|---------|
| **消除 inner loop 不必要 branch** | P6（bounds check） | +6.1% | JIT loop 最佳化解鎖 |
| **高頻呼叫路徑的 dispatch 函式指標化** | P9（opcode table） | +7.6% | 1.7M/幀的 virtual dispatch 積累 |
| **early-exit guard 跳過整個函式** | P3（DMA guard） | +5.1% | 99%+ 情況直接跳過重邏輯 |
| **managed array → unsafe pointer** | P11 | +3.3% | 消除 bounds check + 2D index 計算 |
| **熱路徑地址範圍 fast-path** | P12（RAM fast-path） | +2.8% | 最高頻地址範圍繞過所有 dispatch |
| **AggressiveInlining 小型高頻方法** | P18A | +2.2% | Debug JIT 不主動 inline |
| **static 欄位 local shadow** | P17 | +1.4% | 減少高頻函式的 memory load 次數 |

### 4.2 不有效或反效果的最佳化類型

| 類型 | 代表項目 | 教訓 |
|------|---------|------|
| **手動展開固定迴圈** | P20、P22 | JIT 已處理，方法膨脹惡化 I-cache |
| **過大方法 AggressiveInlining** | P18B | >30 行即有 I-cache 壓力風險 |
| **低頻路徑函式指標化** | P23 | 呼叫頻率太低，dispatch 節省 < delegate 開銷 |
| **instance method 的 delegate* 化** | P25/P26 | wrapper 增加呼叫層，比 managed delegate 更慢 |
| **增加 cache 欄位數量** | P13、P27 | 額外欄位佔 cache line，反而 cache miss 增加 |
| **合併永遠-not-taken 的 if** | P19 | Branch predictor 已預測正確，合併增加 load 次數 |
| **float 替換 double 計算** | P4（APU） | Debug JIT 下兩者差異不大，反而 cast overhead |

### 4.3 Debug JIT 的特殊行為

這個最佳化工作全在 **Debug JIT** 下進行，與 Release 有幾個關鍵差異：

1. **不自動 inline**：需要 `[AggressiveInlining]` 才能強制 inline。
2. **不 enregister static 欄位**：每次讀取都是記憶體 load，所以 local shadow 有效。
3. **不展開小型固定迴圈**：JIT 會保守處理，但手動展開通常得不償失。
4. **switch 不一定生成 jump table**：特別是 case 值不連續或範圍過大時。
5. **float vs double 差異不大**：x86-64 的 SSE2 浮點，兩者均為相似成本。

### 4.4 Benchmark 方法論與硬體波動

#### 測試污染問題

整個過程發現一個關鍵陷阱：**在同一 `&&` 鏈中連接 `python run_tests.py -j 10` 和 `--perf` 會污染 benchmark 結果**。

原因：`run_tests.py -j 10` 啟動 10 個平行 emulator process，20 秒後：
1. **Thermal throttle**：CPU 溫度升高，boost clock 降回 base clock，FPS 可能低 5–10%
2. **Cache 污染**：174 個不同 ROM 的大量讀寫，清空 L2/L3 cache，使 benchmark 在 cold cache 狀態下執行

**正確方法**：`--perf` 必須在獨立、乾淨的 shell 中執行，與任何 test suite 分開。

#### 硬體波動的本質問題

即使在相同的乾淨環境下，同一份程式碼重複執行 `--perf 20` 仍會看到 **±2–3% 的自然波動**。這是現代 x86 系統的根本特性：

**波動來源一：CPU 頻率的動態調整（Boost / DVFS）**

現代 CPU 有多個效能狀態：
- **Boost clock**（如 4.8GHz）：短時間可達，但散熱不足時降回 base clock（如 3.6GHz）
- **Power Limit Throttle（PL1/PL2）**：Intel CPU 有 28W（PL1）和 64W（PL2）的功耗限制，超過時降頻
- 20 秒 benchmark 期間，CPU 可能在不同頻率之間漂移，導致同一段程式碼在不同時間點跑出不同結果

**波動來源二：作業系統背景活動**

- Windows Defender 定期掃描
- 系統更新、索引服務
- 其他程序的記憶體 pressure，導致 OS 觸發 page eviction
- CPU affinity 排程：process 在不同核心之間遷移，L1/L2 cache 狀態重置

**波動來源三：記憶體子系統**

- DRAM refresh（每 ~7.8ms 一次）導致短暫 stall
- Memory bank 爭用（本 benchmark 單 thread，影響小）
- L3 cache 的 MESI protocol 和 prefetcher 行為受溫度影響

**波動來源四：Timer 精度（特別是 Windows）**

- `--perf 20` 用 `Stopwatch.Elapsed` 計時，精度約 100ns，本身影響小
- 但 `Thread.Sleep` 和 OS 排程的精度是 10–15ms，會影響音效緩衝（audio disabled 後已消除此問題）

#### 實際觀察到的波動

本次最佳化過程中，連續三次測試的 FPS 數字：

```
Clean baseline:     247.95 FPS
After run_tests:    244.15 FPS  (污染後，-1.5%)
Clean re-run:       246.30 FPS  (自然波動範圍內)
```

**判斷門檻的設定**：基於此波動特性，本研究採用 **> 0.25% 才算有效改善** 的標準，以排除統計雜訊。實際上，± 1.0% 以內的差異都應視為「可能是雜訊」，只有 > 1.5–2.0% 的一致性改善才是確定有效的最佳化。

#### 嚴謹 benchmark 的建議做法

若需要更高信心的結果（如評估 0.3–0.5% 的微小改善）：

1. **固定 CPU 頻率**：在 BIOS 或 OS 電源計畫中關閉 boost，固定在 base clock
2. **多次跑取平均**：至少 3 次，取中位數（去除最高最低後的平均）
3. **熱機後再測**：先跑一次 warm-up，確保 CPU 已達穩定溫度
4. **排除背景程序**：關閉 Defender 即時保護、避免在更新期間測試
5. **使用更長 duration**：20s → 60s，降低短期波動的相對影響

本研究因為最佳化效益普遍在 1–8% 之間，20 秒已足夠區分有效改善與雜訊。

---

## 五、成果摘要

### 最終累計（14 項成功最佳化）

| # | 項目 | Build | FPS 前 | FPS 後 | 改善 |
|---|------|-------|-------|-------|------|
| 1 | 基線 | Debug | — | 181.70 | — |
| 2 | P11：managed array → unsafe pointer | Debug | 181.70 | 187.70 | **+3.3%** |
| 3 | P8：RenderBGTile 條件合流 | Debug | 187.70 | 188.55 | +0.4% |
| 4 | P5：靜態調色盤快取 | Debug | 188.55 | 189.75 | **+0.6%** |
| 5 | P9：opcode dispatch table | Debug | 189.75 | 204.10 | **+7.6%** |
| 6 | P6：移除冗餘 screenX bounds check | Debug | 204.10 | 216.45 | **+6.1%** |
| 7 | P3：ProcessPendingDma early-exit | Debug | 216.45 | 227.40 | **+5.1%** |
| 8 | P12：RAM read fast-path | Debug | 227.40 | 233.75 | **+2.8%** |
| 9 | P17：static 欄位 local shadow | Debug | 233.75 | 237.00 | **+1.4%** |
| 10 | P14：pointer loop scanline clear | Debug | 237.00 | 239.95 | **+1.2%** |
| 11 | P18A：AggressiveInlining 小型方法 | Debug | 239.95 | 245.30 | **+2.2%** |
| 12 | P24：opHandlers → delegate*<void>[] | Debug | 245.30 | 247.95 | **+1.08%** |
| — | *切換至 Release 組態* | **Release** | — | 241.45 | (新基線) |
| 13 | catchUpPPU/APU loop unroll | Release | 241.45 | 252.00 | **+4.3%** |
| 14 | Sprite 0 hit range check 條件重排 | Release | 252.00 | ~259.00 | **+2.8%** |

**Debug 組態累計：181.70 → 247.95 FPS（+36.5%）**
**Release 組態新增：241.45 → ~259 FPS（+7.3%）**
**.NET 10 RyuJIT（相同程式碼）：~348 FPS**

### 失敗嘗試（14 項，已全數 revert）

P1、P2、P4（v1+v2）、P13、P16、P18B、P19、P20、P21、P22、P23、P25/P26、P27

---

## 六、延伸閱讀建議

若要繼續改善效能，以下方向尚未嘗試且理論上可行：

- **P16 改版**：lazy N/Z flag 評估，但避免 GetFlag 中的 reverse encoding overhead
- ~~**Release 組態 benchmark**~~：已完成，Release ~259 FPS；.NET 10 RyuJIT ~348 FPS（+34%）
- **SIMD 化 framebuffer 操作**：`ScreenBuf1x` 是 `uint*`，可用 `System.Runtime.Intrinsics` AVX2 一次處理 8 像素（palette lookup 仍需 scalar，預估收益 <1%）
- **PPU tile fetch 快取**：相同 tile index 在同一幀內可能重複 fetch，快取 tile pattern data 避免重複 mapper call
- **P3 IRQ dirty flag**：每 CPU 週期省下 4-field OR 運算，但 mutation sites 多（APU 8+、IO 2、Mapper），漏掉一處會造成 IRQ timing 錯誤，風險高
