# AprNes 非 JIT 層優化技巧整理

> 本文整理自 **2026-03-15 起** AprNes 主線上的效能改動，聚焦在**語言／Runtime 層級以下**的優化技巧：位元運算、無分支、SWAR、SIMD、查表、Magic Number、迴圈展開、整數取代浮點、冗餘計算刪除、函式指標分派、資料佈局等。每一節都附上真實 commit 的 before／after 範例，方便對照。
>
> 與 `JIT_ICache_Tutorial.md` 互補——那份談的是 JIT / I-Cache 的宏觀哲學，本篇則是**手動逐條的工藝技巧**。
>
> 適合想把 C#／C++ 熱路徑榨到最後一滴效能的讀者。

---

## 目錄

1. [位元運算：用 `&` 取代 `%`](#1-位元運算用--取代-)
2. [Branchless：消除分支](#2-branchless消除分支)
3. [查表法（Lookup Table）](#3-查表法lookup-table)
4. [Magic Number：用數學代替分支](#4-magic-number用數學代替分支)
5. [SWAR：暫存器內的 SIMD](#5-swar暫存器內的-simd)
6. [SIMD：真正的向量化](#6-simd真正的向量化)
7. [整數取代浮點（Fixed-Point / Bresenham）](#7-整數取代浮點fixed-point--bresenham)
8. [迴圈優化（展開、ILP、Loopless）](#8-迴圈優化展開ilploopless)
9. [函式指標分派（靜態分派）](#9-函式指標分派靜態分派)
10. [資料佈局與記憶體優化](#10-資料佈局與記憶體優化)
11. [冗餘計算刪除（DRY / 提升不變量）](#11-冗餘計算刪除dry--提升不變量)
12. [技巧彙總對照表](#12-技巧彙總對照表)

---

## 1. 位元運算：用 `&` 取代 `%`

**核心觀念**：當除數 `N` 是 2 的冪次（Power of 2）時，`x % N` 在位元層面等同 `x & (N - 1)`。後者是單一 AND 指令（~1 cycle），前者則需除法器（10+ cycle 以上，依 CPU 而定）。

### AprNes 實例：Mapper ROM 分頁索引

Commit `06a35ac perf(mapper): replace % N with & mask in hot read paths (pow2 ROMs)`

**Before：**
```csharp
public byte MapperR_RPG(ushort address)
{
    int bank = /* ... */;
    return PRG_ROM[bank % PRG_ROM_count * 0x4000 + (address & 0x3FFF)];
}
```

**After：**
```csharp
// 在 MapperInit 時預先驗證 + 計算 mask
if ((_PRG_ROM_count & (_PRG_ROM_count - 1)) != 0)
    throw new Exception("PRG_ROM_count must be power of 2");
prgCountMask = _PRG_ROM_count - 1;

// hot path 用 AND 取代 %
return PRG_ROM[(bank & prgCountMask) * 0x4000 + (address & 0x3FFF)];
```

### 適用條件

| 適用 | 不適用 |
| --- | --- |
| 除數已知為 2 的冪次（如 ROM 大小、cache line 對齊、buffer 容量） | 除數是任意整數（例如 `% 3`、`% 6`） |
| 可以在初始化時檢查（不 pow2 就 throw） | 除數在執行期動態變化 |

**注意**：Mapper005（MMC5）明確支援任意 CHR/PRG 大小，因此保留 `%`；Mapper019 的音訊相位 `waveLength` 是 runtime 可變的，也不能改。

### 進階：非 pow2 的 modulo

若除數不是 pow2（例如 `% 6`），可以用「加法 + 符號位」做 wrap：

Commit `57119fb perf: eliminate 3 hot-path modulo-6 ops in NTSC demodulator`

**Before：**
```csharp
int tModQ = ((phase0 - wQ_half + 2) % 6 + 6) % 6;  // 雙重 %
```

**After：**
```csharp
int tModQ = phase0 + 5;                          // 先做減法（等效 -wQ_half+2 ≡ 5 mod 6）
tModQ += ((5 - tModQ) >> 31) & -6;               // 分支式 wrap：若 > 5 則減 6
```

原理：`(5 - tModQ) >> 31` 在 32-bit signed 中會算出 `0`（未溢位）或 `-1`（溢位）；`& -6` 選擇是否減 6。整段是純 ALU，無分支、無 div。

---

## 2. Branchless：消除分支

**核心觀念**：現代 CPU 有深度流水線與分支預測，預測錯誤要 flush 10+ cycles。規律且穩定的分支影響小，但**資料依賴型分支**（每次結果都不同）代價極高。Branchless 技巧把「if/else」轉成「算術式」，讓編譯器能吐出 CMOV 或純 ALU。

### 2.1 Branchless Y-Flip（XOR mask）

Commit `7baf6a0 perf: branchless ComputeSpritePatternAddr + FlipByte LUT`

**Before：**
```csharp
int r = flipY ? ((7 - row) & 7) : (row & 7);
```

**After：**
```csharp
// -(sprAttr >> 7) = 0（不翻）或 -1（翻）
// & 7 → 0 或 7
// row ^= 7 → 7 - row（在 3-bit 範圍）
row ^= -(sprAttr >> 7) & 7;
```

同一技巧用在 8×16 sprite：

```csharp
row ^= -(sprAttr >> 7) & 15;
return ((sprTile & 1) << 12) | ((sprTile & 0xFE) << 4) | ((row & 8) << 1) | (row & 7);
// 用純位元運算取代 4-way if/else 的 tile half 選擇
```

### 2.2 Branchless Clamp / Saturate

C# 的 `Math.Max / Math.Min` 通常會吐出 CMOV（conditional move），是 branchless 的；但更複雜的 range clamp 手寫版本更直接：

```csharp
// clamp x 到 [0, maxIdx]
int rxR = Math.Max(0, Math.Min(srcTx + ioff, maxIdx));  // RyuJIT 會變成 CMOV
```

若要完全手動：
```csharp
int v = srcTx + ioff;
v -= (v - maxIdx) & ((v - maxIdx) >> 31 ^ -1);  // min(v, maxIdx)
v &= ~(v >> 31);                                 // max(v, 0)
```

但**大多情況下 `Math.Max/Min` 已經夠好**，手動位元版除非 profile 顯示瓶頸才值得寫。

### 2.3 分支改 mask select

```csharp
// 原本：
int result = condition ? a : b;

// Branchless：
int mask = -(condition ? 1 : 0);   // C 風格；C# 多用下行
int mask = condition ? -1 : 0;     // 或直接
int result = (a & mask) | (b & ~mask);
```

同樣的，`RyuJIT` 經常自動 CMOV，因此**先看反組譯**再決定要不要手寫。

### 2.4 關鍵判斷：什麼時候值得 branchless？

| 情境 | 是否值得 |
| --- | --- |
| 分支結果**高度可預測**（例如 99% 走同一條） | **不必**。分支預測器會處理好 |
| 每次結果都不同（像素級資料依賴） | **值得**。mispredict 代價太高 |
| 分支中有「長」的副作用（大量運算） | **不值得**。改 branchless 反而每次都跑完雙支 |
| 分支兩邊成本對稱、都很短 | **值得** |

---

## 3. 查表法（Lookup Table）

**核心觀念**：如果某個函式的輸入空間很小（例如 256 種值以下），預先算好結果存成表，執行期一次 memory load 就取得。成本從 N 條 ALU 變成一次 L1 hit（~4 cycles）。

### AprNes 實例：FlipByte 256-byte LUT

Commit `7baf6a0`

**Before（12 條 ALU）：**
```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
static byte FlipByte(byte b)
{
    b = (byte)(((b & 0xF0) >> 4) | ((b & 0x0F) << 4));
    b = (byte)(((b & 0xCC) >> 2) | ((b & 0x33) << 2));
    b = (byte)(((b & 0xAA) >> 1) | ((b & 0x55) << 1));
    return b;
}
```

**After（1 次 table read）：**
```csharp
static readonly byte[] FlipTable = GenerateFlipTable();

static byte[] GenerateFlipTable()
{
    byte[] t = new byte[256];
    for (int i = 0; i < 256; i++) {
        int v = i;
        v = ((v & 0xF0) >> 4) | ((v & 0x0F) << 4);
        v = ((v & 0xCC) >> 2) | ((v & 0x33) << 2);
        v = ((v & 0xAA) >> 1) | ((v & 0x55) << 1);
        t[i] = (byte)v;
    }
    return t;
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
static byte FlipByte(byte b) => FlipTable[b];
```

256 bytes 是永久 L1-resident 的尺寸，完全不會被驅逐。

### 進階：byte[] → byte* 去除 bounds check

Commit `ad162c7 perf: FlipTable from managed byte[] to unmanaged byte*`

C# 的 `byte[]` 每次 index 都會做 bounds check。對於已知永遠合法的 index，可以改用 `Marshal.AllocHGlobal` 分配 unmanaged memory + `byte*` 指標，一次省掉 1-2 條指令。

```csharp
static byte* FlipTablePtr;  // Marshal.AllocHGlobal(256) at init

static byte FlipByte(byte b) => FlipTablePtr[b];   // 無 bounds check
```

### 何時該用 LUT？

| 條件 | 是否適合 LUT |
| --- | --- |
| 輸入空間 ≤ 256（1 byte） | **極適合**。表只 256 bytes，永駐 L1 |
| 輸入空間 ~ 64 KB（16-bit） | 視存取頻率。可能擠壓其他熱資料 |
| 輸入空間 > 1 MB | **不適合**。L2/L3 都擠不下，cache miss 成本 > 運算成本 |
| 計算本身超便宜（1-2 條指令） | **不適合**。算還比查表快 |

原則：**LUT 容量要小到幾乎不會 D-Cache miss**，否則你只是把 I-Cache 壓力換成 D-Cache 壓力。

---

## 4. Magic Number：用數學代替分支

**核心觀念**：利用位元/整數運算的巧妙恆等式，把多層分支壓成一條 ALU 鏈。代表性的技巧有 de-Bruijn sequences、modular inverse、fast reciprocal。

### AprNes 實例：de-Bruijn 風格的 bit-position 解碼

Commit `8cd97cf perf(ppu): branchless sprite-index decode via magic-multiply`

**問題**：有一個 64-bit 值 `lowest`，只有其中一個 bit 會被設，而且這個 bit 必定在 `8k+7`（k=0..7）位置。要找出 k。

**Before（3-level 二分搜尋）：**
```csharp
uint lo32 = (uint)lowest;
int i;
if (lo32 != 0) {
    if ((lo32 & 0xFFFFu) != 0) i = (lo32 & 0x80u) != 0 ? 0 : 1;
    else                       i = (lo32 & 0x800000u) != 0 ? 2 : 3;
} else {
    uint hi32 = (uint)(lowest >> 32);
    if ((hi32 & 0xFFFFu) != 0) i = (hi32 & 0x80u) != 0 ? 4 : 5;
    else                       i = (hi32 & 0x800000u) != 0 ? 6 : 7;
}
```
共 2-3 次 mispredict-prone 分支。

**After（1 次乘法）：**
```csharp
int i = (int)((0x0001020304050607UL * (lowest >> 7)) >> 56);
```

**原理**：
- `(lowest >> 7)` 將唯一一個 bit 從 `8k+7` 移到 `8k`，變成 `1 << (8k)`。
- 乘上 `0x0001020304050607` 等於把 magic 的每個 byte 往左位移 `8k` 位元。
- magic 的 byte k 存的就是數字 k（byte 0 = 0x07, byte 1 = 0x06, byte 2 = 0x05, …, byte 7 = 0x00）。
- `>> 56` 取最高 byte，得到結果。

執行成本：1 SHR + 1 IMUL + 1 SHR，約 3-5 cycle，完全無分支。

### 何時能用 Magic Number？

- **輸入有強結構**（例如「只有一個 bit 會 set」、「必定是 pow2」、「值域極窄」）。
- **資料相關分支**會讓分支預測器失準。
- **可用數學證明正確**（不能靠 try & error）。

Magic number 技巧一旦找到就極爽，但寫註解一定要解釋原理，否則三個月後連自己都看不懂。

---

## 5. SWAR：暫存器內的 SIMD

**核心觀念**：SWAR（SIMD Within A Register）是用**一般整數暫存器**（64-bit `long` / `ulong`）同時處理多個較小的值（例如 8 個 byte、4 個 short）。不需要 SIMD 指令集支援，是「平民版」的並行化。

### AprNes 實例：Loopless OAM Multiplexer

Commit `5ad35c4 perf(ppu): loopless SWAR OAM multiplexer (+5% FPS)`

NES PPU 每個像素要從 8 個 sprite slot 中挑出「X counter == 0 且 (H|L) 有高位 bit」的最低索引。原本是 `for (int i = 0; i < 8; i++) { if (...) break; }`，現在改成純 SWAR：

```csharp
ulong xc = *(ulong*)sprXCounter;   // 8 個 byte 同時載入

// 每 byte 是否 == 0？Carry-based 技巧
ulong has_bits = ((xc & 0x7F7F7F7F7F7F7F7FUL) + 0x7F7F7F7F7F7F7F7FUL) | xc;
ulong active_mask = (~has_bits & 0x8080808080808080UL);  // byte == 0 → 0x80

// 每 byte 的 H|L 高 bit
ulong pixel_mask = (*(ulong*)sprShiftH | *(ulong*)sprShiftL)
                   & 0x8080808080808080UL;

ulong valid = active_mask & pixel_mask;

if (valid != 0)
{
    // 找到「最低 bit」對應的 byte 索引
    ulong lowest = valid & (ulong)(-(long)valid);
    // ... 後續用 magic multiply 解碼索引
}
```

**收益：**
- 8-iter for loop → 單一 ulong 管線。
- 常見情況（沒有任何 sprite 命中）在 `valid != 0` 直接 super-early-exit，整段跳過。
- 實測 +5% FPS。

### SWAR 常見 idiom

| 目標 | 表達式 |
| --- | --- |
| 每 byte 檢查是否 == 0 | `(x & 0x7F...) + 0x7F... \| x`；反位元遮 0x80...每 byte 決定 |
| 每 byte 檢查是否 < N | `(x - 0x01...N) & ~x & 0x80...` |
| 每 byte 加法（不跨 byte 進位） | `(a + b - ((a ^ b) & 0x80...)) ^ ((a ^ b) & 0x80...)` |
| 廣播 byte 到所有 8 個 lane | `x * 0x0101010101010101` |
| 取出最低 set bit | `x & -x`（在 ulong 上做） |

### AprNes 另一例：APU 長度計數器 halt 批次化

Commit `0cd963d perf(apu): SWAR-batch the 4 per-cycle lenctrHalt register reads`

原本每個 APU cycle 要讀 4 個 halt bit（散落在 `$4000 / $4004 / $4008 / $400C`），改成一次讀 8 bytes（一個 `ulong`），再用 mask 抽出對應位：

```csharp
ulong rH = *(ulong*)(regs + 0);
lenctrHalt0 = (rH & 0x0000_0000_0000_0020UL) != 0;  // byte 0 bit 5
lenctrHalt1 = (rH & 0x0000_0000_0020_0000UL) != 0;  // byte 4 bit 5
lenctrHalt2 = (rH & 0x0000_0000_0000_0080UL) != 0;  // byte 8 bit 7
lenctrHalt3 = (rH & 0x0000_0020_0000_0000UL) != 0;  // byte C bit 5
```

一次 load + 4 次 bit test 取代 4 次 load。

---

## 6. SIMD：真正的向量化

**核心觀念**：SIMD（Single Instruction, Multiple Data）使用 CPU 專屬的向量暫存器與指令集（SSE2 / AVX2 / AVX-512 / NEON），一次處理 128-bit（4 × int32 / 8 × int16 / 16 × int8）甚至 256-bit / 512-bit 的資料。

.NET 在 `System.Runtime.Intrinsics` 命名空間下提供：
- `Vector128<T>`：跨平台抽象（x86 SSE2 + ARM NEON 自動選）。
- `Vector256<T>`：僅 x86 AVX2（ARM 無對應寬度）。
- `Avx2.GatherVector256`、`Sse41.Dot`、`Fma.MultiplyAdd`：平台特定 intrinsic。

### 6.1 Vector256：CRT 像素批次處理

Commit `6e7c350 perf(crt/simd): Vector256<uint> SIMD for all 3 ProcessRow*_SWAR variants`

把 scalar SWAR 升級為 `Vector256<uint>`，**一次處理 8 個像素**：

```csharp
// 原本：per-pixel scalar
for (int x = 0; x < width; x++) {
    dst[x] = (src[x] & 0xFEFEFEFE) >> 1 + ...;  // 衰減計算
}

// 改為：8 像素 / 迭代
for (int x = 0; x < width; x += 8)
{
    Vector256<uint> v = Avx2.LoadVector256((uint*)(src + x));
    Vector256<uint> decayed = Avx2.ShiftRightLogical(
        Avx2.And(v, Vector256.Create(0xFEFEFEFEu)), 1);
    // ... 更多運算
    Avx2.Store((uint*)(dst + x), result);
}
```

### 6.2 Gather：非連續讀取

Commit `87bb1b4 perf(crt/simd): Avx2.GatherVector256 in ApplyFullFrameCurvatureAndConvergence`

CRT 曲面／輻合修正需要按 `map[dstIdx]` 的間接位址讀取像素。硬體支援 `GatherVector256`：

```csharp
Vector256<int> indices = /* 8 個間接索引 */;
Vector256<uint> gathered = Avx2.GatherVector256((uint*)srcPtr, indices, 4);
```

### 6.3 沒有硬體 Gather 怎麼辦？軟體 scalar gather

Commit `06bef96 perf(crt/simd): replace Avx2.GatherVector256 with manual scalar gather`

事後實測顯示在某些 CPU（或跨平台情境）上，硬體 gather 反而比 8 次 scalar load 慢。改用手動 gather：

```csharp
var v = Vector256.Create(
    srcPtr[i0], srcPtr[i1], srcPtr[i2], srcPtr[i3],
    srcPtr[i4], srcPtr[i5], srcPtr[i6], srcPtr[i7]);
```

這點在 NEON 上更明顯——ARM 沒有對應的硬體 gather，軟體版是唯一選擇。

### 6.4 跨平台策略：Runtime Dispatch

AprNes / EnigmaBenchmark 採用的模式：

```csharp
if (Avx2.IsSupported)
    CrackImplAvx2();
else if (AdvSimd.IsSupported)    // ARM64 NEON
    CrackImplNeon();
else
    CrackImplScalar();
```

執行期偵測一次、存到 function pointer，後續直接呼叫。`Vector128<T>` 的靜態 API 在 ARM64 上會自動吐 NEON 指令，所以大多時候**只要用 Vector128 就自動跨平台**。真正需要分流的是：
- Vector256 / Vector512（AVX 才有）。
- 平台特定 intrinsic（Gather、Shuffle variants、FMA specifics）。

### 6.5 FMA：融合乘加

Commit `351e790 perf(Ntsc): FMA YIQ→RGB matrix + gamma curve (.NET 10 conditional)`

`Fma.MultiplyAdd(a, b, c)` 在硬體一條指令完成 `a * b + c`，比拆成 `mul` + `add` 精度更高（中間結果不截斷）、延遲更短。適用於矩陣運算、卷積、濾波器。

```csharp
// 不用 FMA
Vector256<float> y = Avx.Add(Avx.Multiply(a, b), c);

// 用 FMA
Vector256<float> y = Fma.MultiplyAdd(a, b, c);
```

---

## 7. 整數取代浮點（Fixed-Point / Bresenham）

**核心觀念**：浮點運算雖快，但 `float → int` 的 cast（`cvttss2si`）、`fmod`、`round` 等都比純整數貴。如果精度需求有限，用定點整數或 Bresenham 型累加器可以顯著加速。

### 7.1 定點數 16.16 累加器

Commit `fc8be3f perf: CRT Convergence fixed-point accumulator`

**Before：**
```csharp
float baseOffset = -halfW * step + 1024.5f;
for (int tx = 0; tx < dstW; tx++)
{
    int ioff = (int)(tx * step + baseOffset) - 1024;  // 每像素一次 float→int
    // ...
}
```

**After：**
```csharp
int stepFx = (int)(step * 65536f);          // 16.16 fixed-point
int baseFx = (int)((-halfW * step + 0.5f) * 65536f);

int iFx = baseFx;
for (int tx = 0; tx < dstW; tx++)
{
    int ioff = iFx >> 16;                   // 取整數部分
    // ...
    iFx += stepFx;                          // 純 int add
}
```

整個 loop 內沒有浮點，更容易被 JIT 向量化，也避免了 `cvttss2si` 在 x86 上的延遲。

### 7.2 Bresenham-style 取樣累加器

Commit `4a6ff7d perf: APU Bresenham + NTSC mod-6 single-line merge + RfBuzz fmod removal`

APU 每個 CPU cycle 要判斷是否產生一個 audio sample（sample rate 44100、CPU freq 1.79 MHz）。原本用 double 累加：

**Before：**
```csharp
static double _sampleAccum  = 0.0;
static double _cycPerSample = 1789773.0 / 44100;

_sampleAccum += 1.0;
if (_sampleAccum >= _cycPerSample) {
    _sampleAccum -= _cycPerSample;
    EmitSample();
}
```

**After（純整數 Bresenham）：**
```csharp
static int _sampleAccum = 0;
static int _cpuFreqInt  = 1789773;

_sampleAccum += 44100;                   // 每 cycle + sample_rate
if (_sampleAccum >= _cpuFreqInt) {       // 閾值 = CPU freq
    _sampleAccum -= _cpuFreqInt;
    EmitSample();
}
```

數學等效於「每 `cpu/rate` 個 cycle 發一個 sample」，但每次只做純 int add + compare，消除約 5.4M FPU ops/sec。

### 7.3 `fmod` 替換為 compare+subtract

同一 commit 還處理了 AudioPlus 的 RfBuzzPhase：

**Before：**
```csharp
phase = phase + dt;
phase = phase % 1.0f;    // ~50 cycles on x86 (microcoded fmod)
```

**After：**
```csharp
phase += dt;
if (phase >= 1.0f) phase -= 1.0f;   // 1 cycle 分支，且可預測
```

只要 `dt < 1.0`（永遠成立），compare+subtract 就等價於 fmod。**fmod 在 x86 上非常慢**（幾十個 cycle），幾乎永遠可以改。

### 7.4 div → mul

Commit `b667bc7 perf(audio): authMix_GetVoltage — drop dead clamp, avoid double round-trip, div→mul`

```csharp
// Before
float y = x / k;

// After — 預先算好 1/k
static readonly float invK = 1.0f / k;
float y = x * invK;
```

浮點 div 在 x86 約 15-30 cycle，mul 只要 4-5 cycle。若除數不變、可預先倒數，永遠值得換。

---

## 8. 迴圈優化（展開、ILP、Loopless）

### 8.1 結構性 Unroll（Loop Unrolling）

Commit `2857f35 feat(phase2b): PAL outer unroll — MasterClockTickUnrolledPAL`

NES PAL region 的 master clock 週期是 80 MC = 5 CPU cycle × 16 MC/cycle。原本是 `for (int mc = 0; mc < 80; mc++)` + 內部狀態機。改成 5 個手動展開的 gate 函式，每個 gate 處理一個 16 MC chunk：

```csharp
// 原本
while (mcCpu > 0) { MasterClockTick(); }

// Unrolled
MasterClockTickUnrolledPAL() {
    PalGate1();   // 事件：APU + 4 PPU-full + 3 PPU-half + NMI + IRQ
    PalGate2();   // 事件：APU + 3 PPU-full + 3 PPU-half + NMI + IRQ
    PalGate3();   // ...
    PalGate4();
    PalGate5();
}
```

**收益：**
- 消除迴圈控制 overhead（counter increment + compare + branch）。
- 每個 gate 的事件順序在編譯期就固定，JIT 可以更激進地 inline 與排序。
- 實測 +13.1% FPS（NTSC 對應版本）。

**代價：**
- 程式碼量膨脹 → 注意 I-Cache。AprNes 的 PAL gates 總 IL 約 10-15 KB，仍在 L1 I-Cache 範圍內。

### 8.2 ILP：Instruction-Level Parallelism

Commit `ca59cb1 perf: RunWaveformLoop ILP — 4-step herringbone lookahead + xorshift chunking`

現代 CPU 的執行單元（3+ 條整數 pipeline）可以同時執行無資料依賴的指令。關鍵是**打斷依賴鏈**，讓編譯器能重排。

**Before（序列依賴）：**
```csharp
for (int s = 0; s < 4; s++) {
    // 每個 s 依賴上一個 s 的 hRl/hIl
    x = hRl * hC - hIl * hS;
    float t = hRl * hS + hIl * hC;
    hRl = x;
    hIl = t;
}
```

**After（4-step lookahead）：**
```csharp
// 預先算好 1..4 步的旋轉矩陣（常數）
float c1 = hC, s1 = hS;
float c2 = c1*hC - s1*hS, s2 = s1*hC + c1*hS;
float c3 = c2*hC - s2*hS, s3 = s2*hC + c2*hS;
float c4 = c3*hC - s3*hS, s4 = s3*hC + c3*hS;

// 4 個 sample 並行計算，無資料依賴
float h0 = hIl;
float h1 = hRl * s1 + hIl * c1;
float h2 = hRl * s2 + hIl * c2;
float h3 = hRl * s3 + hIl * c3;
float tR = hRl * c4 - hIl * s4;
hIl = hRl * s4 + hIl * c4;
hRl = tR;
```

4 條 mul 能在 3+ pipeline 上並行，吞吐量接近線性提升。

### 8.3 xorshift 分塊再利用

同一 commit：一次 xorshift 產生 32-bit 噪音，**拆成 4 個 byte** 給 4 個 sample 用：

```csharp
// Before：每 sample 一次完整 xorshift
ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5;
x += (ns & 0xFF) * nScale - nOff;   // sample 0
ns ^= ns << 13; ...                  // sample 1
...

// After：一次 xorshift 給 4 個 sample
ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5;
n0 = (ns & 0xFF) * nScale - nOff;
n1 = ((ns >>  8) & 0xFF) * nScale - nOff;
n2 = ((ns >> 16) & 0xFF) * nScale - nOff;
n3 = ((ns >> 24) & 0xFF) * nScale - nOff;
```

12 條 bitops / dot → 3 條。

### 8.4 Loopless：直接消除迴圈

見 §5.1 的 SWAR OAM multiplexer——原本的 `for (int i = 0; i < 8; i++)` 被一段 SWAR 管線替換。這是**從「遍歷每個元素」改為「用向量並行處理全部元素」**的 mindset shift。

---

## 9. 函式指標分派（靜態分派）

**核心觀念**：每 cycle 都要走的 `if (mode == X) DoX(); else DoY();` 分支可以換成**一次設定、持續命中**的 function pointer（C# 的 `delegate*` 或 `delegate` 欄位）。設定時機通常是「模式切換時」，每 cycle 就只付一次 indirect call 的成本。

### AprNes 實例：APU 音訊輸出分派

Commit `671db3e perf(apu): function-pointer dispatch for audio output (+1.9% FPS)`

**Before：**
```csharp
void apu_step() {
    // ... 通道更新 ...
    if (AudioMode > 0) {
        // 每 cycle 推送 AudioPlus（per-cycle 精度）
        if (expansionChannelCount > 0) {
            float gain = ap_mode01ExpGain;
            int sum = 0;
            for (int i = 0; i < expansionChannelCount; i++) { /* ... */ }
            // ...
        }
    } else {
        // Catchup：只在 sample rate 時算
        _sampleAccum += APU_SAMPLE_RATE;
        if (_sampleAccum >= _cpuFreqInt) { /* ... */ }
    }
}
```

**After：**
```csharp
static delegate*<void> apuOutputFn = &ApuOutputCatchup;

public static void ApuRefreshOutputFn() {
    apuOutputFn = AudioMode > 0 ? &ApuOutputPushPlus : &ApuOutputCatchup;
}

void apu_step() {
    // ... 通道更新 ...
    apuOutputFn();   // 單次 indirect call
}

static void ApuOutputPushPlus() { /* ... */ }
static void ApuOutputCatchup()  { /* ... */ }
```

**收益：**
- 每 cycle 省掉一次 branch。
- **附加收益**（真正的大贏家）：`apu_step` 的 IL size 從 1212 → 784 bytes（−35%），整個函式更容易塞進 I-Cache，**+1.9% FPS**。

**小細節：** `ApuRefreshOutputFn()` 在初始化與 `AudioMode` 變更時呼叫，不影響熱路徑。

### 同樣技巧的其他應用

- 記憶體分派：`NesCore.MEM.cs` 用 `delegate*<ushort, byte>[]` 索引 $0000-$FFFF 的讀寫路徑，由各 mapper 在初始化時註冊。
- 區域（NTSC / PAL / Dendy / FDS）選擇：`mcTickFn` 指向對應的 `MasterClockTickUnrolled*`。

---

## 10. 資料佈局與記憶體優化

### 10.1 byte[] → byte*（移除 bounds check）

Commit `ed4ef6e perf: ntscScanBuf byte[] → byte* + palBuf signatures to byte*`

C# 陣列每次 index 都會 bounds check。對於 hot path 的固定大小 buffer，改用 `Marshal.AllocHGlobal` / `NativeMemory.AlignedAlloc` 取得 `byte*`，省下 bounds check：

```csharp
// Before
byte[] ntscScanBuf = new byte[width];

// After
byte* ntscScanBuf = (byte*)Marshal.AllocHGlobal(width);
```

**風險：** 失去 GC 記憶體安全保護；必須自己管理生命週期（Init 配置、Cleanup 釋放）。Bug 會變成 native memory corruption，除錯成本高。

### 10.2 stackalloc → static unmanaged

Commit `829b9dc perf(ntsc): replace stackalloc-per-scanline with static unmanaged buffers`

`stackalloc` 每次呼叫都會移動 stack pointer（便宜但非零成本），且只在當前函式內有效。若同一個 buffer 反覆用，改為**一次配置、永久持有**的 static unmanaged pointer：

```csharp
// Before
void DecodeScanline() {
    byte* temp = stackalloc byte[256];  // 每 scanline alloc
    // ...
}

// After
static byte* scanlineTemp;   // initNTSC 時 allocate 一次

void initNTSC() {
    if (scanlineTemp == null)
        scanlineTemp = (byte*)Marshal.AllocHGlobal(256);
}
```

好處：零 stack 操作、buffer 在 L1/L2 的位置穩定（prefetcher 更友善）。

### 10.3 對齊（AlignedAlloc）

Commit `0f47dea perf(mem): NativeMemory.AlignedAlloc via conditional helpers on .NET 10`

SIMD load/store 要求位址對齊（SSE 需 16-byte，AVX2 需 32-byte，AVX-512 需 64-byte）。沒對齊的情況下，多數新 CPU 會 silently 處理但有效能懲罰。

```csharp
#if NET10_0_OR_GREATER
    byte* buf = (byte*)NativeMemory.AlignedAlloc((nuint)size, 32);
#else
    byte* buf = (byte*)Marshal.AllocHGlobal(size);  // 不保證對齊
#endif
```

Cache-line 對齊（64-byte）還能避免跨 cache line 存取。

### 10.4 Unmanaged 遷移

Commit `9e7e494 perf(core): unmanaged memory migration + PPU $2007 SR simplify`

AprNes 將所有 NES memory（RAM、VRAM、OAM、palette）都搬到 unmanaged，理由：

| 原因 | 說明 |
| --- | --- |
| **消除 GC 壓力** | 整個 emulator core 不配置 managed 物件 → GC 幾乎不介入 |
| **記憶體位置穩定** | managed 陣列可能被 GC 搬動，導致 `fixed` 區塊頻繁 pin |
| **L1/L2 cache 可預測** | 位址穩定 → prefetcher 命中率更高 |
| **跨函式傳遞方便** | 直接傳 `byte*`，不用 `Span<T>` + fixed |

代價是開發期要手動管理生命週期，並承擔 buffer overflow 的風險——但對於熱路徑被跑千萬次的模擬器核心，這是划算的。

---

## 11. 冗餘計算刪除（DRY / 提升不變量）

很多性能收益不來自複雜演算法，而是**單純把重複的 / 不必要的計算刪掉**。

### 11.1 DRY：同一個值不要算兩次

Commit `1eda716 perf(ppu): branchless flip LUT + sprite range hack + $2001 DRY`

PPU `$2001` mask register 被 `showBG`、`showSpr`、`ShowBGLeft8`、`ShowSprLeft8` 等多個旗標使用。原本每次都 `(mask & 0x08) != 0` / `(mask & 0x10) != 0` 重新算；改為**寫入 $2001 的時候一次算好、存為 bool 欄位**，後續直接讀：

```csharp
// Before (in hot loop)
if ((mask & 0x08) != 0 && (mask & 0x10) != 0) { /* ... */ }

// After — 寫入 $2001 時一次算好
static bool showBG, showSpr, ShowBGLeft8, ShowSprLeft8;

void ppu_w_2001(byte v) {
    showBG       = (v & 0x08) != 0;
    showSpr      = (v & 0x10) != 0;
    ShowBGLeft8  = (v & 0x02) != 0;
    ShowSprLeft8 = (v & 0x04) != 0;
}
```

### 11.2 Hoist Invariant：把不變的算出迴圈

Commit `4a6ff7d perf: APU Bresenham + NTSC mod-6 single-line merge + RfBuzz fmod removal`

**Before：**
```csharp
for (int d = 0; d < kDots; d++) {
    float cosH = MathF.Cos(1.31683f);   // 常數但每次算
    float sinH = MathF.Sin(1.31683f);
    // ...
}
```

**After：**
```csharp
static readonly float CosHerring = MathF.Cos(1.31683f);
static readonly float SinHerring = MathF.Sin(1.31683f);

for (int d = 0; d < kDots; d++) {
    // 直接讀 static
}
```

編譯器通常會自動 hoist 純常數表達式，但**一旦中間有任何「可能有 side effect」的東西**（例如 `MathF.Cos` 被認為可能 throw），就不敢動。手動宣告 `static readonly` 最保險。

### 11.3 Dead code 清理

同一 commit：

```csharp
// Before
float y = Math.Max(0, Math.Min(1, x));  // clamp [0,1]
y = Math.Max(0, y);                      // 再 clamp 一次！dead
return (int)Math.Round(y);
```

`Math.Max(0, y)` 在 y 已知 >= 0 時是多餘的；profile 顯示該方法佔用 0.x% CPU，刪掉就回收了。

### 11.4 double round-trip

同一 commit 的 `authMix_GetVoltage`：

```csharp
// Before
float v = ComputeSomething();   // float
double d = v;                    // 擴展為 double
// ... double 運算
return (float)d;                // 再縮回 float — 雙重轉換浪費

// After
float v = ComputeSomething();
// ... 全程 float
return v;
```

`float ↔ double` 的 roundtrip 在 x86 上可能觸發 `cvtss2sd` + `cvtsd2ss`，各約 4 cycle。如果精度不需要就不要升等。

---

## 12. 技巧彙總對照表

| 技巧 | 典型收益 | 代表 commit | 使用前提 |
| --- | --- | --- | --- |
| `% N` → `& mask` | 單條指令取代 10+ cycle | `06a35ac` | N 是 pow2 |
| `% N` → sign-ext wrap | 3 條 ALU 取代 div | `57119fb` | N 小且固定，範圍已知 |
| Branchless Y-flip (XOR mask) | 消除 mispredict | `7baf6a0` | 分支結果資料依賴 |
| 256-byte LUT | 1 load 取代 N 條 ALU | `7baf6a0` | 輸入 ≤ 256 |
| LUT byte[] → byte* | 省 bounds check | `ad162c7` | 永遠合法 index |
| Magic-multiply de-Bruijn | 1 乘法取代 3 層分支 | `8cd97cf` | 輸入有強結構（例如單一 bit set） |
| SWAR OAM mux | 消除 8-iter 迴圈 | `5ad35c4` | 資料 8 × byte 排列 |
| SWAR lenctrHalt batch | 1 load 取代 4 load | `0cd963d` | 相關欄位連續排列 |
| `Vector256<uint>` | 8× 並行 | `6e7c350` | AVX2 可用，資料對齊 |
| `Avx2.GatherVector256` | 硬體 gather | `87bb1b4` | 硬體支援且性能比 scalar 好 |
| 軟體 scalar gather | 跨平台 fallback | `06bef96` | 不支援硬體 gather 或 NEON |
| FMA | 一條指令取代 mul+add | `351e790` | 矩陣／卷積／濾波器 |
| 16.16 fixed-point | 消除 float→int cast | `fc8be3f` | 精度需求有限 |
| Bresenham 整數累加 | 消除 FPU | `4a6ff7d` | 比例固定 |
| `fmod` → compare+subtract | 避開微指令 fmod | `4a6ff7d` | 增量 < 模數 |
| div → mul(1/k) | 15-30 cycle → 4-5 cycle | `b667bc7` | k 為常數或不變 |
| 結構性 Loop Unroll | 消除迴圈控制 | `2857f35` | 迭代數固定、小 |
| ILP 4-step lookahead | 打斷資料依賴 | `ca59cb1` | 可預先算出下一步矩陣 |
| xorshift 分塊 | 4 sample 共用 1 rand | `ca59cb1` | 精度容忍度高 |
| `delegate*` 分派 | 消除熱路徑分支、瘦身 IL | `671db3e` | 模式變化頻率 << cycle 頻率 |
| byte[] → byte* | 省 bounds check | `ed4ef6e` | 固定大小、已驗證安全 |
| stackalloc → static unmanaged | 穩定 cache 位置 | `829b9dc` | buffer 大小固定 |
| `NativeMemory.AlignedAlloc` | SIMD 對齊 | `0f47dea` | .NET 6+，SIMD 路徑 |
| DRY `$2001` flags | 避免同值反覆算 | `1eda716` | 算一次可以快取的值 |
| Hoist cos/sin 常數 | 把 const 運算搬出迴圈 | `4a6ff7d` | JIT 不敢 hoist 的 side-effect |

---

## 結語

本文列出的所有技巧都在 **2026-03-15 到 2026-04-19** 間被實測驗證過，累積約 170+ 個 commit。整體效果：

- NES core 在 Debug build 下從 ~106 FPS 拉到 ~120 FPS（+13% 以上）。
- Avalonia .NET 10 + SIMD CRT pipeline 在 4× 內部解析度穩定 60 FPS 以上。
- AccuracyCoin v2 138/138 + blargg 184/184 **全程不回歸**。

每一條技巧單看好像只值幾個 cycle，但在熱路徑被跑千萬次的模擬器核心中，累積效果是實打實的：

> **優化不是「找到一個神技巧」，而是「把 100 個 0.5% 的小勝利疊起來」。**

每次動刀前：
1. 先 profile（見 `JIT_ICache_Tutorial.md` §11 或 `profiling_workflow.md`）。
2. 選一條技巧、改動、量測。
3. 確認回歸測試全過（本專案：blargg 184/184 + AccuracyCoin 138/138）。
4. Commit，寫清楚「用了哪條技巧、收益多少」。
5. 回到 1。

這就是本專案這段時間一直在重複的循環。本文就是這個循環的副產品——希望對同樣在調效能的人有用。
