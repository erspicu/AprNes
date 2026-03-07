# Master Clock 架構研究

**目的**: 分析現有 timing 架構的限制，規劃 Master Clock 重構方向
**日期**: 2026-03-07

---

## 現有架構：CPU-Driven Tick Model

目前以 CPU cycle 為最小時間單位。每次記憶體存取（`Mem_r()`/`Mem_w()`）呼叫 `tick()`，一次性推進所有子系統：

```csharp
// MEM.cs - tick()
static void tick()
{
    // promote NMI delay → nmi_pending
    ppu_step_new();  // PPU dot 1
    ppu_step_new();  // PPU dot 2
    ppu_step_new();  // PPU dot 3
    apu_step();      // 1 APU half-cycle
}
```

**特性**：
- 1 CPU cycle = 3 PPU dots = 1 APU step，全部綁定
- CPU 的 opcode switch 是同步執行，每個 `Mem_r()`/`Mem_w()` 觸發一次 tick
- 沒有獨立的時鐘排程器，CPU 驅動一切

---

## Sub-Cycle 近似模擬

目前多處使用「近似」方式模擬 sub-cycle 行為：

| 行為 | 目前做法 | 真實硬體行為 |
|------|---------|-------------|
| DMC DMA stolen cycles | `dmc_stolen_tick()` 多跑幾次 tick，用 `cpuBusIsWrite` 近似 GET/PUT | Master Clock 精確排程每個 stolen cycle 的 M2 phase |
| $2002 flag stagger | sprite flags dot 1, VBL dot 2 (BUGFIX45) | M2 rise 讀 VBL, M2 fall 讀 sprite（差 7.5 master clocks） |
| DMA halt/alignment | `cpuBusIsWrite` 判斷 read/write cycle | M2 duty cycle 15/24 決定 GET(high)/PUT(low) |
| NMI 1-cycle delay | `nmi_delay` → 下次 tick promote | NMI edge 在特定 master clock edge 被偵測 |
| Load DMA parity | `apucycle & 1` 決定 delay 2 或 3 | CPU cycle count parity 對應 M2 phase |
| OAM DMA bus state | `cpuBusAddr`/`cpuBusIsWrite` tracking | 實際 M2 phase 決定 bus 方向 |

**限制**：這些近似在大部分情況下足夠，但無法精確到 M2 rise/fall edge。AccuracyCoin 剩餘的 17 FAIL 幾乎全部卡在需要區分 M2 phase 的場景。

---

## 真實 NES 時鐘關係

```
Master Clock = 21.477272 MHz (NTSC)

CPU  = Master / 12 = 1.789773 MHz
PPU  = Master / 4  = 5.369318 MHz
APU  = Master / 24 = 0.894886 MHz (= CPU / 2)

1 CPU cycle = 12 master clocks = 3 PPU dots
1 APU cycle = 24 master clocks = 2 CPU cycles = 6 PPU dots
```

### M2 Duty Cycle

CPU 的 M2 信號（類似 clock enable）在 RP2A03G 上的 duty cycle 為 15/24：

```
Master clock: |0|1|2|3|4|5|6|7|8|9|A|B|  (12 clocks per CPU cycle)
M2 signal:    |_|_|_|‾|‾|‾|‾|‾|‾|‾|‾|‾|  (low 3, high 9 → ~15/24 over 2 phases)

M2 rise: master clock 3  → CPU read (GET) begins
M2 fall: master clock 12 → CPU write (PUT) begins, register latch
```

- **M2 high (GET phase)**: 資料匯流排由外部設備驅動，CPU 讀取
- **M2 low (PUT phase)**: CPU 驅動資料匯流排，外部設備鎖存

### DMA 與 M2 Phase 的關係

DMA 控制器（2A03 內部）在 M2 的特定 phase 決定行為：
- **Halt**: 等到下一個 GET cycle 才停止 CPU
- **Alignment**: 如果 halt 發生在 PUT cycle，多等一個 cycle 對齊
- **Phantom reads**: DMA 非活動期間，CPU bus 上的地址決定 phantom read 目標

AccuracyCoin 的 `DMADMASync_PreTest` 精確測試這些 phase 行為，我們的近似模型差 1 cycle。

---

## 目標 Master Clock 架構

### 核心概念

```
while (running) {
    master_clock++;

    // PPU: 每 4 master clocks 推進 1 dot
    if (master_clock % 4 == 0)
        ppu_dot();

    // CPU M2 edges: 每 12 master clocks 一個完整 cycle
    if (master_clock % 12 == 3)   // M2 rise
        cpu_m2_rise();            // GET phase 開始
    if (master_clock % 12 == 0)   // M2 fall (next cycle boundary)
        cpu_m2_fall();            // PUT phase 開始

    // APU: 每 24 master clocks
    if (master_clock % 24 == 0)
        apu_tick();
}
```

### CPU State Machine 改造

目前 CPU 是同步的 giant switch：

```csharp
// 現在：同步執行，每個 Mem_r/Mem_w 觸發 tick
case 0xAD: // LDA absolute
    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
    r_A = Mem_r(ushort1);
    // ...
    break;
```

需要改為可暫停/恢復的 state machine：

```csharp
// 目標：每個 master clock 推進一步
enum CpuState { FetchOpcode, FetchLow, FetchHigh, Execute, ... }

void cpu_step() {
    switch (state) {
        case CpuState.FetchOpcode:
            opcode = bus_read(r_PC++);
            state = decode(opcode);
            break;
        case CpuState.FetchLow:
            addr_lo = bus_read(r_PC++);
            state = CpuState.FetchHigh;
            break;
        // ...
    }
}
```

### 影響範圍

| 元件 | 改動量 | 說明 |
|------|--------|------|
| CPU.cs | **極大** | ~5000 行 giant switch 改為 state machine |
| MEM.cs | **大** | tick() 消失，改為 master clock scheduler |
| PPU.cs | **中** | ppu_step_new() 基本不變，但呼叫時機改為獨立排程 |
| APU.cs | **中** | apu_step() 不變，DMC DMA 改為 master clock 精確排程 |
| IO.cs | **小** | register dispatch 不變 |
| JoyPad.cs | **小** | strobe timing 自然精確 |

---

## 可能的漸進式重構路徑

### 方案 A：完整重寫（風險高，收益最大）

直接建立 master clock scheduler，CPU 改為 state machine。

- **優點**: 一步到位，架構乾淨
- **缺點**: CPU.cs 5000 行全部重寫，回歸風險極高
- **工時**: 極長

### 方案 B：M2 Phase Tracking（風險中，針對性強）

不改 tick() 結構，但在每個 tick 內區分 M2 rise/fall phase：

```csharp
static void tick() {
    m2_phase = M2Phase.Rise;  // GET
    ppu_step_new();
    ppu_step_new();
    m2_phase = M2Phase.Fall;  // PUT (約在 dot 2 的位置)
    ppu_step_new();
    apu_step();
}
```

DMA 決策時讀取 `m2_phase` 而非 `cpuBusIsWrite`。

- **優點**: 改動最小，針對 DMA 問題
- **缺點**: 仍是近似（M2 rise/fall 在 3 PPU dots 內的位置是硬編碼的）
- **預估收益**: 可能解決 P13 前置條件 (+6~+12)

### 方案 C：Micro-Tick 細分（風險中低）

將 tick() 細分為 sub-ticks，但不完全改為 master clock：

```csharp
static void tick() {
    // 12 master clocks per CPU cycle
    for (int m = 0; m < 12; m++) {
        if (m % 4 == 0) ppu_step_new();
        if (m == 3) on_m2_rise();
        if (m == 0) on_m2_fall();
    }
    apu_step();
}
```

- **優點**: tick() 內部精度提升，外部 API 不變
- **缺點**: 效能下降（12x 迴圈），PPU/APU 需要適配
- **預估收益**: 理論上可解決所有 M2 相關問題

---

## 預期收益

| 修復目標 | 項數 | 依賴的精度 |
|----------|------|-----------|
| P13 DMA 前置條件 | 6 | M2 phase 精確的 DMA halt/alignment |
| P10 SH* DMA bus conflict | 5 | DMA 期間 bus state 精確 |
| P20 Implied Dummy Reads | 1 | 同 P13 前置條件 |
| P14 DMC Channel | 1 | DMC DMA 完整行為 |
| P14 APU Reg Activation | 1 | DMA bus + $4000 range detection |
| P14 Controller Strobing | 1 | PUT/GET parity 精確 |
| P12 IFlagLatency | 1 | DMC DMA 累積誤差消除 |
| P19 PPU per-dot | 3 | per-dot OAM evaluation（方案 A 才能解決） |
| **合計** | **19** | |

方案 B/C 預估可解決 12~15 項，方案 A 理論上可解決全部 19 項（達 136/136）。

---

## 參考實作

### Mesen2 (C++)

- `NesConsole.cpp`: Master Clock scheduler，CPU/PPU/APU 各自有 `Run()` 方法
- `NesCpu.cpp`: CPU 不是 state machine，而是用 `Exec()` + callback 在每個 bus access 時 yield
- `NesPpu.cpp`: 獨立的 dot-by-dot execution
- `NesApu.cpp`: `ClockDmc()` 在精確的 master clock 時機呼叫

### BeesNES (C++)

- `ref/BeesNES-main/` 已下載完整原始碼
- 使用真正的 master clock scheduler
- CPU 使用微碼表（microcode table）驅動 state machine

---

## 結論

目前 **blargg 100% + AccuracyCoin 87% (118/136)** 是 CPU-driven tick 架構的實際上限。
剩餘 17+1 項 FAIL 的根因集中在 M2 phase 精度，必須透過架構重構才能突破。

建議從**方案 B（M2 Phase Tracking）**開始嘗試，以最小改動驗證 P13 前置條件是否能通過。
若成功（+6~+12），再決定是否進一步走向方案 A 的完整 Master Clock。
