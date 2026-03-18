# AprNes 雙核心架構規劃

*撰寫日期：2026-03-17*

---

## 一、動機

| | Accurate Core（現有） | Speed Core（規劃） |
|--|----------------------|-------------------|
| **定位** | 精準模擬，通過全部測試 ROM | 高效模擬，最大化 FPS |
| **CPU 模型** | Per-cycle 狀態機（operationCycle） | OPCODE_CYCLE_TABLE 查表一次性執行 |
| **PPU 模型** | Per-dot 完整 pipeline | 掃描線批次渲染 |
| **APU 模型** | 完整 frame counter + IRQ timing | 簡化通道合成 |
| **DMA 模型** | 完整 bus conflict / abort / parity | 簡化 cycle steal |
| **目標硬體** | 現代 x64 PC | 低端 x64 / 未來嵌入式 |
| **測試通過率** | blargg 174/174、AC 136/136 | 商業遊戲可玩性為主 |

---

## 二、目錄結構規劃

```
AprNes/
├── AprNes/
│   ├── NesCore/                    ← 現有 Accurate Core（不動）
│   │   ├── Main.cs
│   │   ├── CPU.cs
│   │   ├── PPU.cs
│   │   ├── APU.cs
│   │   ├── MEM.cs
│   │   ├── IO.cs
│   │   ├── JoyPad.cs
│   │   └── Mapper/
│   │
│   ├── NesCoreSpeed/               ← 新增 Speed Core
│   │   ├── Main_S.cs               (ROM 載入、init、run loop)
│   │   ├── CPU_S.cs                (OPCODE_CYCLE_TABLE + 一次性執行)
│   │   ├── PPU_S.cs                (掃描線批次 PPU)
│   │   ├── APU_S.cs                (簡化 APU)
│   │   ├── MEM_S.cs                (簡化 tick + memory dispatch)
│   │   ├── IO_S.cs                 (暫存器讀寫)
│   │   ├── JoyPad_S.cs             (可直接從 NesCore 共用)
│   │   └── Mapper/                 (共用 IMapper + 各 Mapper*.cs)
│   │
│   ├── UI/                         ← 共用 UI（不動）
│   └── tool/                       ← 共用工具（不動）
│
├── NesCore/                        ← Accurate 核心（原 AprNes/NesCore 同步）
└── NesCoreSpeed/                   ← Speed 核心（新增）
```

**Mapper 共用**：`IMapper` 介面與所有 `Mapper*.cs` 由兩個核心共用，不需要複製。Mapper 不依賴 NesCore 內部 timing，只透過 `NesCore.chrBankPtrs[]`、`NesCore.NES_MEM` 存取。

---

## 三、類別設計

### 3.1 命名規則

| 類別 | Accurate | Speed |
|------|----------|-------|
| 主類別 | `partial class NesCore` | `partial class NesCoreSpeed` |
| 命名空間 | `namespace AprNes` | `namespace AprNes` |
| 靜態欄位 | 現有（全 static） | 全 static，獨立命名空間隔離 |

兩個核心完全獨立的 static 狀態，不會互相污染。

### 3.2 共用元件

以下可直接共用，不需要複製：

```
IMapper.cs          ← 介面不變
Mapper*.cs          ← 所有 mapper 實作共用
LangINI.cs          ← UI 語言
tool/               ← 渲染、音效、搖桿工具全部共用
UI/                 ← Windows Forms 共用
```

### 3.3 Speed Core 差異點

#### CPU_S.cs — OPCODE_CYCLE_TABLE 模型

```csharp
// 查表取 cycle 數，一次性執行整條指令
static readonly byte[] OPCODE_CYCLE_TABLE = {
    7,6,2,8,3,3,5,5,3,2,2,2,4,4,6,6, // 0x00
    2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7, // 0x10
    // ... 256 entries
};
static readonly byte[] OPCODE_PAGE_CROSS = { ... }; // 跨頁額外 cycle

static void cpu_step_S()
{
    opcode = Mem_r_S(r_PC++);
    int cycles = OPCODE_CYCLE_TABLE[opcode];
    Execute_S(opcode);          // 執行指令邏輯（不含個別 tick）
    for (int i = 0; i < cycles; i++) tick_S(); // 補足 PPU/APU cycle
}
```

無 `operationCycle` 狀態機，無 `opFnPtrs` 函式指標表；每指令一次性完成。

#### PPU_S.cs — 掃描線批次模型

```csharp
static void RenderScanline_S()
{
    // 一次算出整條掃描線 256 像素
    // 不做 per-dot tile fetch pipeline
    // 不做 per-dot secondary OAM FSM
    // sprite 0 hit 用簡化判斷（start-of-scanline precompute）
    // NMI 在 dot 1 直接觸發，不做 1-dot delay 精確模型
}
```

每 scanline 僅呼叫一次渲染函式，不需要 89,342 次 `ppu_step_new()`。

#### MEM_S.cs — 簡化 tick

```csharp
static void tick_S()
{
    // 每 CPU cycle：+3 PPU dot（批次），+1 APU step
    // 不做 StartCpuCycle/EndCpuCycle 精確拆分
    // 不做 per-cycle IRQ polling（只在 opcode fetch 時 poll）
    // 不做 DMA bus conflict / parity / implicit abort
    ppu_dot_count += 3;
    if (ppu_dot_count >= 341) EndScanline_S();
    apu_step_S();
}
```

#### APU_S.cs — 簡化 APU

- Frame counter 保留（影響 length counter / envelope，遊戲可聽性關鍵）
- DMC DMA cycle steal 簡化為固定 4 cycle steal
- IRQ 時序簡化：frame counter step 時直接設 irq_pending，不做 irqLinePrev 機制

---

## 四、UI 整合方式

### 4.1 核心切換選項

在設定對話框新增「核心模式」選項：

```
[Core Mode]
○ Accurate（精準，預設）
○ Speed（高效）
```

存入 `AprNes.ini`：
```ini
CoreMode=accurate   ; 或 speed
```

### 4.2 切換機制

```csharp
// AprNesUI.cs
if (AppConfigure["CoreMode"] == "speed")
{
    NesCoreSpeed.init(romBytes);
    NesCoreSpeed.VideoOutput += VideoOutputDeal;
    NesCoreSpeed.run();
}
else
{
    NesCore.init(romBytes);
    NesCore.VideoOutput += VideoOutputDeal;
    NesCore.run();
}
```

UI 其他部分（按鍵輸入、截圖、設定）透過介面或各自靜態方法呼叫，由於兩個核心均有對應方法，切換成本低。

### 4.3 共用介面（可選）

若要讓 UI 完全不感知核心差異，可提取介面：

```csharp
interface INesCore {
    bool init(byte[] rom);
    void run();
    void SoftReset();
    void P1_ButtonPress(byte btn);
    void P1_ButtonUnPress(byte btn);
    event EventHandler VideoOutput;
}
```

但這涉及把 static 方法包裝為 instance，短期內可先不做，直接條件式切換即可。

---

## 五、實作順序

### Phase 1 — 目錄與骨架（低風險）
1. 建立 `AprNes/NesCoreSpeed/` 目錄
2. 建立 `partial class NesCoreSpeed` 骨架（Main_S.cs / CPU_S.cs / PPU_S.cs / APU_S.cs / MEM_S.cs / IO_S.cs）
3. 複製 NesCore 的 ROM 載入 / Mapper 初始化邏輯（基本相同）
4. 建立 `OPCODE_CYCLE_TABLE`（可從舊 commit 或 NESDev 文件重建）
5. 加入 csproj 編譯

### Phase 2 — Speed CPU（核心差異）
1. 實作 `cpu_step_S()`：查表 cycle 數 + 一次性指令執行
2. 以現有 CPU.cs 的指令邏輯為基礎，移除 `operationCycle` 層，重寫為 flat 函式
3. 驗證：能載入並執行 ROM 不 crash

### Phase 3 — Speed PPU（掃描線批次）
1. 從現有 RenderBGTile / RenderSpritesLine 提取掃描線批次渲染
2. 實作簡化 NMI timing（dot 1 直接觸發）
3. 驗證：畫面可正常顯示

### Phase 4 — Speed APU + DMA
1. 簡化 frame counter + 5 通道合成
2. 簡化 OAM DMA（固定 513 cycle）、DMC DMA（固定 4 cycle）

### Phase 5 — UI 整合
1. 在設定對話框加入核心切換
2. AprNesUI.cs 加入條件切換邏輯

---

## 六、風險與注意事項

### 主要風險

| 風險 | 說明 | 對策 |
|------|------|------|
| Mapper 相容性 | Mapper 目前依賴 `NesCore.chrBankPtrs` / `NesCore.NES_MEM` 等靜態欄位 | NesCoreSpeed 複製相同欄位名稱，或提取為共用靜態容器 |
| NMI 時序 | 掃描線批次模型的 NMI 時序誤差可能導致部分遊戲不穩定 | 先確保主流遊戲（Mario、Contra 等）正常，不追求測試 ROM |
| MMC3 IRQ | MMC3 依賴 PPU A12 per-dot 通知 | Speed 版 MMC3 改為 per-scanline A12 通知（近似但非精確） |
| static 衝突 | 兩個核心均為 static，若同命名空間可能混用 | NesCoreSpeed 使用獨立命名空間或全部加 `_S` suffix |

### 不建議做的事

- 用繼承或多型整合兩個核心（static partial class 不支援繼承核心邏輯，強行合併會破壞兩邊的可維護性）
- 讓兩個核心共用 static 欄位（會互相干擾 state）
- Phase 1-3 完成前就整合 UI（先讓 Speed 核心能獨立執行）

---

## 七、預期效能目標

| 指標 | Accurate (目前) | Speed (目標) |
|------|----------------|-------------|
| FPS（Mega Man 5） | ~280 | **400+** |
| 每幀 ppu_step_new 呼叫 | 89,342 | ~240（每 scanline 一次） |
| 每幀 cpu_step 呼叫 | ~29,829 | ~29,829（相同，指令數不變） |
| per-cycle overhead | operationCycle 狀態機 | 無 |
| IRQ polling overhead | 每 CPU cycle | 每 opcode fetch |

---

*此文件為規劃草案，實作前應再確認 Mapper 共用方式與靜態欄位隔離策略。*

---

## 八、Speed Core CPU 起點參考

### Git Commit

```
commit: 0fdd934
message: add CPU dummy reads for unofficial opcodes, implement TAS/SHA/LAS: 112 PASS / 44 FAIL
date:    2026-02-xx（tick-on-access 改寫前的最後一版）
```

此 commit 是 `cycle_table` 查表架構的**最後版本**。下一個 commit `4c1c7de`（`[Architecture] CPU tick-on-access cycle-accurate conversion v1`）就完全改為 per-cycle 模型。

取出完整 CPU.cs（2490 行）：
```bash
git show 0fdd934:AprNes/NesCore/CPU.cs > temp/CPU_cycle_table_ref.cs
```

### cycle_table 內容（256 entries）

```csharp
static byte[] cycle_tableData = new byte[]{
/*0x00*/ 7,6,2,8,3,3,5,5,3,2,2,2,4,4,6,6,
/*0x10*/ 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
/*0x20*/ 6,6,2,8,3,3,5,5,4,2,2,2,4,4,6,6,
/*0x30*/ 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
/*0x40*/ 6,6,2,8,3,3,5,5,3,2,2,2,3,4,6,6,
/*0x50*/ 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
/*0x60*/ 6,6,2,8,3,3,5,5,4,2,2,2,5,4,6,6,
/*0x70*/ 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
/*0x80*/ 2,6,2,6,3,3,3,3,2,2,2,2,4,4,4,4,
/*0x90*/ 2,6,2,6,4,4,4,4,2,5,2,5,5,5,5,5,
/*0xA0*/ 2,6,2,6,3,3,3,3,2,2,2,2,4,4,4,4,
/*0xB0*/ 2,5,2,5,4,4,4,4,2,4,2,4,4,4,4,4,
/*0xC0*/ 2,6,2,8,3,3,5,5,2,2,2,2,4,4,6,6,
/*0xD0*/ 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
/*0xE0*/ 2,6,2,8,3,3,5,5,2,2,2,2,4,4,6,6,
/*0xF0*/ 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7
};
static byte* cycle_table; // unsafe pointer to above, set in init
```

### 當時 cpu_step() 的使用方式

```csharp
static void cpu_step()
{
    opcode = Mem_r(r_PC++);          // fetch opcode（含 tick PPU/APU）
    cpu_cycles = cycle_table[opcode]; // 查表取 cycle 數
    cpu_cycles += Interrupt_cycle;    // 加上 interrupt 額外 cycle
    Interrupt_cycle = 0;

    switch (opcode)                   // 一次性執行整條指令
    {
        case 0x69: /* ADC imm */ ...
        // ...
    }
    // 指令結束後，剩餘 cycle 由 Mem_r/Mem_w 內的 tick 補足
    // （當時版本 tick 在每次 Mem_r/w 呼叫時觸發）
}
```

### 注意事項

- 此版本 CPU 已有 dummy reads（unofficial opcodes TAS/SHA/LAS），可直接複用
- 指令邏輯（switch cases）2490 行，可作為 Speed CPU_S.cs 的基礎，移除 operationCycle 相關程式碼
- 當時的 NMI/IRQ 處理較簡單（`Interrupt_cycle` 補償），Speed Core 可沿用此模型
- 跨頁 cycle penalty（branch / abs,X / abs,Y）在當時是直接在指令內加 cycle，Speed Core 可保留此方式
