# TriCNES v2 — $2007 FSM 完整呼叫鏈

## 執行順序總覽

```
CPU Read $2007 (line 9036)
  ├── return PPU_ReadBuffer (non-palette) 或 palette data
  ├── EmulateUntilEndOfRead() (line 9059) → 推進 7 master clocks
  │     └── _EmulatorCore() ×7 (line 756)
  │           ├── PPUClock==4 → _EmulatePPU()
  │           │     ├── PPU_DATA_StateMachine() (line 1511)
  │           │     ├── rendering (tile fetch, pixel calc)
  │           │     └── PPU_DATA_StateMachine2() (line 1657)
  │           └── PPUClock==2 → _EmulateHalfPPU()
  │                 └── PPU_DATA_StateMachine_Half() (line 1734)
  ├── PPU_2007_Read_SR = true (line 9060)
  └── PPU_2007_Read = true (line 9061)

CPU Write $2007 (line 9670)
  ├── PPU_2007_WriteData = data (line 9673)
  ├── EmulateNMasterClockCycles(7) (line 9675) → 同上推進 7 master clocks
  ├── PPU_2007_Write = true (line 9676)
  └── PPU_2007_Write_SR = true (line 9677)
```

---

## 三階段 FSM

### Phase 1: PPU_DATA_StateMachine() — line 1761
**呼叫時機**: _EmulatePPU() line 1511（full dot 開頭，rendering 之前）

```
輸入: PPU_2007_Read_SR, PPU_2007_Read, PPU_2007_Write_SR, PPU_2007_Write
      PPU_Dot, ShowBackground, ShowSprites, PPU_Scanline, PPU_AddressBus

計算:
  BLNK = (!ShowBG && !ShowSpr) || (sl >= 240 && sl < 261)
  H0_DASH = (PPU_Dot - 1 & 1) != 0

Latch 推進 (偶數 index):
  Read_Latches[0] = Read_SR        ← SR 輸入
  Read_Latches[2] = !Read_Latches[1]
  Read_Latches[4] = !Read_Latches[3]
  Write_Latches[0] = Write_SR
  Write_Latches[2] = !Write_Latches[1]
  Write_Latches[4] = !Write_Latches[3]

清除觸發 flag:
  PPU_2007_Read = false
  PPU_2007_Write = false

信號輸出:
  PD_RB = Latches[4] && !Latches[2]        ← buffer refill 觸發
  ReadALE = !Latches[4] && Latches[2]       ← 讀 ALE 觸發
  WriteALE = !Write_Latches[4] && Write_Latches[2]
  TStep_Latch = DB_PAR                      ← 來自上一個 half-step 的寫入信號
  PPU_READ = PD_RB || (!BLNK && H0_DASH)   ← 控制 rendering fetch
  PPU_ALE = ReadALE || WriteALE || (!BLNK && !H0_DASH)

SM ALE → bus:
  if (ReadALE || WriteALE) && !PPU_READ:
    PPU_AddressBus = PPU_v
    PPU_OctalLatch = (byte)PPU_AddressBus
```

### Phase 2: PPU_DATA_StateMachine2() — line 1807
**呼叫時機**: _EmulatePPU() line 1657（rendering 之後）

```
if PD_RB:
  PPU_ReadBuffer = FetchPPU()               ← 第 1 次 buffer refill
  if PPU_ALE: OctalLatch = (byte)AddressBus
```

### Phase 3: PPU_DATA_StateMachine_Half() — line 1827
**呼叫時機**: _EmulateHalfPPU() line 1734（half-step，mid-dot）

```
TStep = TStep_Latch || PD_RB
if TStep:
  PPU_v += increment (1 or 32)              ← v increment 在此！
  if !BLNK_Latch: IncrementScrollY()        ← rendering 中 = CXinc + Yinc

PPU_ALE = ReadALE || WriteALE
if PD_RB:
  PPU_ReadBuffer = FetchPPU()               ← 第 2 次 buffer refill（v increment 後）
  if PPU_ALE: OctalLatch = (byte)AddressBus

Latch 推進 (奇數 index):
  Read_Latches[1] = !Read_Latches[0]
  Read_Latches[3] = !Read_Latches[2]
  if !Read_Latches[3]: Read_SR = false      ← SR 重置

  Write_Latches[1] = !Write_Latches[0]
  Write_Latches[3] = !Write_Latches[2]
  if !Write_Latches[3]: Write_SR = false    ← SR 重置

Write 執行:
  DB_PAR = Write_Latches[1] && !Write_Latches[3]
  PPU_WRITE = !PaletteRAMEnable && DB_PAR
  if DB_PAR: StorePPUData(AddressBus, WriteData)  ← 實際 VRAM 寫入
```

---

## 輔助 Methods

### FetchPPU() — line 149
```csharp
ushort Address = (ushort)((PPU_AddressBus & 0x3F00) | PPU_OctalLatch);
// 用 Address 從 CHR ROM/RAM 或 VRAM 讀取
// 讀完後: PPU_AddressBus = (PPU_AddressBus & 0xFF00) | data
return (byte)PPU_AddressBus;
```
**呼叫處**: StateMachine2 line 1820, StateMachine_Half line 1842

### StorePPUData(Address, Data) — line 9687
```csharp
Address = PPUAddressWithMirroring(Address);
if (Address < 0x2000) CHRRAM[Address] = Data;
else if (Address >= 0x3F00) PaletteRAM[Address & 0x1F] = Data;
else VRAM[Address & 0x7FF] = Data;
```
**呼叫處**: StateMachine_Half line 1866

### EmulateUntilEndOfRead() — line 750
```csharp
for (int i = 0; i < 7; i++) _EmulatorCore();  // 7 master clocks = 1.75 PPU cycles
```
**呼叫處**: $2007 read handler line 9059

### EmulateNMasterClockCycles(7) — line 760
```csharp
for (int i = 0; i < n; i++) _EmulatorCore();
```
**呼叫處**: $2007 write handler line 9675

---

## SR Latch Pipeline 狀態追蹤

### Idle 狀態
```
Read_Latches  = [F, T, F, T, F]
Read_SR = true (但被 !Latches[3] 重置後為 false，然後 Read_SR 在下次 read 才設 true)
```

### Read 觸發流程
```
CPU read → EmulateUntilEndOfRead(7 MC) → Read_SR=true, Read=true

Full dot: Latches[0]=SR(true), [2]=![1], [4]=![3]; Read=false
Half dot: Latches[1]=![0], [3]=![2]; if !Latches[3] → SR=false

每個 full dot + half dot 推進 pipeline 一步
PD_RB = Latches[4] && !Latches[2] → 在特定 dot 變 true → 觸發 buffer refill
ReadALE = !Latches[4] && Latches[2] → 在 PD_RB 之前 1 dot 變 true
TStep = TStep_Latch || PD_RB → v increment
```

### Write 觸發流程
```
CPU write → EmulateNMasterClockCycles(7) → Write=true, Write_SR=true

同樣的 latch pipeline → WriteALE → DB_PAR → StorePPUData
TStep_Latch = DB_PAR（上一 half-step）→ 下一 full dot TStep_Latch=true → half dot TStep → v increment
```

---

## 關鍵差異：PD_RB 觸發 TStep

```
TStep = TStep_Latch || PD_RB
```

- **Read**: PD_RB 直接觸發 TStep（同一 dot）
- **Write**: DB_PAR 先經過 TStep_Latch（延遲 1 half-dot），下一 dot 的 half-step 才 TStep

所以 v increment 對 read 和 write 的時機不同：
- Read: PD_RB 的同一 dot 的 half-step
- Write: DB_PAR 後的下一個 dot 的 half-step
