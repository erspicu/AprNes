# BUGFIX4: Headless Test Runner + CPU/PPU/APU 精度修正

日期: 2026-02-20

## 一、Headless Console Test Runner (新功能)

新增 headless 模式，可透過命令列參數自動載入 ROM、執行測試、截圖、記錄結果。

### 新增檔案
- **TestRunner.cs** — 核心測試邏輯
- **run_tests.ps1** — PowerShell 批次測試腳本
- **test_output/report.md** — 測試結果報告

### 修改檔案
- **Program.cs** — 雙模式入口：有參數 → headless，無參數 → GUI
- **AprNes.csproj** — 加入 TestRunner.cs

### 用法
```
AprNes.exe --rom test.nes --wait-result --max-wait 30 --screenshot out.png --log results.log
powershell -ExecutionPolicy Bypass -File run_tests.ps1
```

### 支援參數
| 參數 | 說明 |
|---|---|
| `--rom <path>` | ROM 檔案路徑 |
| `--wait-result` | 等待 blargg $6000 狀態完成 |
| `--max-wait <sec>` | 最大等待秒數 (預設 30) |
| `--time <sec>` | 執行指定秒數後停止 |
| `--screenshot <path>` | 截圖輸出路徑 |
| `--log <path>` | 結果 log 路徑 |
| `--debug-log <path>` | debug log 路徑 |
| `--debug-max <n>` | debug log 最大行數 |

---

## 二、Mapper WRAM 修復

blargg 測試框架寫結果到 $6000-$7FFF。以下 Mapper 的 RAM handler 原本是空的，修復為讀寫 NES_MEM：

- **Mapper000.cs** / **Mapper002.cs** / **Mapper003.cs** / **Mapper005.cs** / **Mapper011.cs** / **Mapper066.cs** / **Mapper071.cs**

```csharp
// Before:
public void MapperW_RAM(ushort address, byte value) { }
public byte MapperR_RAM(ushort address) { return 0; }
// After:
public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
```

---

## 三、CPU Headless 支援

**CPU.cs** / **Main.cs**:
- 新增 `HeadlessMode`、`DebugLogPath`、`dbgMaxConfig` 設定欄位
- `ShowError()`: HeadlessMode 時輸出到 Console.Error，否則 MessageBox
- 替換 3 處 MessageBox.Show 為 ShowError()

---

## 四、APU 修正

### Bug 1: APU open bus (IO.cs)
- `IO_read()` 的 default case 從 `return 0x40` 改為 `return openbus`
- APU write-only register 讀取應回傳 open bus 值

### Bug 2: APU Frame Counter reset delay (IO.cs)
- $4017 寫入後加入 +7 cycle offset 補償 CPU/APU 同步延遲
- 5-step mode 立即觸發後使用 `frameReload5[framectr]`
- 4-step mode 使用 `frameReload4[0]`

### Bug 3: APU Frame Counter 查表重寫 (APU.cs)
- 新增 `frameReload4[]` / `frameReload5[]` 查表陣列取代硬編碼

---

## 五、CPU Dummy Read 修正 (CPU.cs)

### Bug 4: Read 指令缺少 page-cross dummy read (25 個 opcode)

在 6502 CPU 上，indexed addressing 跨頁時會先讀一次 "wrong page" 位址（觸發 I/O 副作用），再讀正確位址。原本只加 1 cycle 不做實際讀取。

修正的 opcode：
- **abs,X** (6): ADC(0x7D), AND(0x3D), CMP(0xDD), EOR(0x5D), LDY(0xBC), SBC(0xFD)
- **abs,Y** (7): ADC(0x79), AND(0x39), CMP(0xD9), EOR(0x59), LDA(0xB9), LDX(0xBE), SBC(0xF9)
- **(ind),Y** (6): ADC(0x71), AND(0x31), CMP(0xD1), EOR(0x51), ORA(0x11), SBC(0xF1)

修正模式:
```csharp
// Before:
byte1 = Mem_r(ushort2);  // 直接讀正確位址
if ((ushort1 & 0xff00) != (ushort2 & 0xff00)) cpu_cycles++;

// After:
if ((ushort1 & 0xff00) != (ushort2 & 0xff00))
{
    Mem_r((ushort)((ushort1 & 0xFF00) | (ushort2 & 0x00FF))); // dummy read wrong page
    cpu_cycles++;
}
byte1 = Mem_r(ushort2);  // 正確讀取
```

### Bug 5: STA abs,Y (0x99) 缺少 dummy read

Store 指令每次都做 dummy read（不管是否跨頁），原本完全缺失。

```csharp
// Before:
Mem_w((ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y), r_A);

// After:
ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
ushort2 = (ushort)(ushort1 + r_Y);
Mem_r((ushort)((ushort1 & 0xFF00) | (ushort2 & 0x00FF))); // dummy read (always)
Mem_w(ushort2, r_A);
```

### Bug 6: RMW abs,X 缺少 wrong-page dummy read (5 個 opcode)

RMW (Read-Modify-Write) 指令的 abs,X 定址模式，每次都會先讀 wrong-page 位址。

修正的 opcode: ASL(0x1E), LSR(0x5E), ROR(0x7E), DEC(0xDE), INC(0xFE)

```csharp
// Before:
ushort2 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);

// After:
ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
ushort2 = (ushort)(ushort1 + r_X);
Mem_r((ushort)((ushort1 & 0xFF00) | (ushort2 & 0x00FF))); // dummy read wrong page
```

### Bug 7: 非官方 NOP abs,X 缺少 page-cross cycle (6 個 opcode)

0x1C, 0x3C, 0x5C, 0x7C, 0xDC, 0xFC — 原本只做 `r_PC += 2`，沒有 page-cross 偵測。

```csharp
// Before:
r_PC += 2;

// After:
ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
ushort2 = (ushort)(ushort1 + r_X);
if ((ushort1 & 0xff00) != (ushort2 & 0xff00))
{
    Mem_r((ushort)((ushort1 & 0xFF00) | (ushort2 & 0x00FF)));
    cpu_cycles++;
}
Mem_r(ushort2); // actual read (discarded)
```

### Bug 8: NOP abs (0x0C) 未讀取目標位址

改為實際讀取並丟棄。

### Bug 9: 0xE2 cycle table 錯誤

cycle_table[0xE2] 從 3 修正為 2（NOP immediate，2 byte 2 cycle）。

---

## 六、PPU 修正 (PPU.cs)

### Bug 10: Even/odd frame skip 條件不完整

- skip 條件從 `ShowBackGround` 改為 `ShowBackGround || ShowSprites`
- skip cycle 從 338 調整為 339（更接近硬體行為）

### Bug 11: Mapper004 IRQ scanline counter 觸發時機

- 新增 scanline 261 的 IRQ 觸發支援

---

## 七、測試結果

使用 112 個 blargg test ROM 進行回歸測試：

| 項目 | 結果 |
|---|---|
| 總計 | 95 PASS / 17 FAIL |
| 官方指令測試 | 全數通過 (instr_test-v3, v5, nes_instr_test) |
| CPU timing | 官方指令全過，非官方指令未實作 |
| Dummy reads | 官方 opcode 全過，非官方未實作 |
| PPU sprite | 全數通過 (sprite_hit, sprite_overflow) |
| PPU VBL/NMI | 基礎通過，sub-cycle 時序 7 個失敗 |

完整報告: `test_output/report.md`
