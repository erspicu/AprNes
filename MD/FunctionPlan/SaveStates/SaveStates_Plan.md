# Save States — 即時存檔/讀檔設計規劃

> 日期：2026-03-22

---

## 一、目標

提供即時存檔（Save State）與即時讀檔（Load State）功能，讓玩家可在任何時間點保存遊戲狀態並還原，不依賴遊戲本身的存檔機制（SRAM）。

---

## 二、快捷鍵與 Slot 設計

### 2.1 存檔槽（Slots）

- 提供 **8 個存檔槽**（Slot 1~8）
- 預設使用 Slot 1
- 右鍵選單新增「Save State」/「Load State」子選單，各列出 8 個 Slot

### 2.2 快捷鍵

| 快捷鍵 | 功能 |
|--------|------|
| `F5` | 存檔到當前 Slot |
| `F7` | 從當前 Slot 讀檔 |
| `Shift+1~8` | 切換當前 Slot（顯示於 FPS 旁） |

### 2.3 檔案命名

```
{rom_file_name}.slot{N}.state
```

例：`Super Mario Bros 3.slot1.state`

存放位置與 `.sav`（SRAM）相同目錄。

---

## 三、需要保存的狀態清單

### 3.1 CPU（CPU.cs）

| 欄位 | 型別 | 說明 |
|------|------|------|
| `reg_A`, `reg_X`, `reg_Y` | byte | 累加器、索引暫存器 |
| `reg_SP` | byte | 堆疊指標 |
| `reg_PC` | ushort | 程式計數器 |
| `P` (flag register) | byte | N/V/B/D/I/Z/C 旗標 |
| `cpuCycleCount` | long | CPU cycle 計數 |
| `nmi_pending`, `nmi_delay_cycle` | bool/long | NMI 延遲模型 |
| `irqLinePrev`, `irqLineCurrent` | bool | IRQ 邊緣偵測 |
| `nmi_just_deferred` | bool | NMI deferral flag |
| `in_tick` | bool | 重入鎖 |

### 3.2 PPU（PPU.cs）

| 欄位 | 型別 | 說明 |
|------|------|------|
| `vram_addr`, `t_addr` | ushort | VRAM 位址暫存器 |
| `fine_x` | byte | 精細 X 捲動 |
| `w_toggle` | bool | $2005/$2006 寫入切換 |
| `ppu_2007_buffer` | byte | $2007 讀取緩衝 |
| `ppu2007ReadCooldown` | int | $2007 cooldown |
| `isVblank`, `ShowBackGround`, `ShowSprites` | bool | PPU 控制旗標 |
| `ppuRenderingEnabled` | bool | 延遲渲染開關 |
| `sl` (scanline), `cx` (dot) | int | 掃描線/點位計數器 |
| `ctrl_2000`, `mask_2001` | byte | PPU 控制暫存器 |
| `OAM[256]` | byte[] | 主 OAM |
| `secondaryOAM[32]` | byte[] | 次 OAM |
| `oam_addr` | byte | OAM 位址 |
| `VRAM[0x4000]` | byte* | 完整 PPU 記憶體（含 nametable + palette） |
| Sprite evaluation FSM | 多欄位 | spriteEvalState, oamN, oamM 等 |
| BG shift registers | ushort×4 | lowshift, highshift 等 |
| Sprite 0 hit 狀態 | 多欄位 | sprite0OnLine, sprite0X 等 |

### 3.3 APU（APU.cs）

| 欄位 | 型別 | 說明 |
|------|------|------|
| Pulse 0/1 | 多欄位 | timer, sequencer, envelope, sweep, length counter |
| Triangle | 多欄位 | timer, sequencer, linear counter |
| Noise | 多欄位 | timer, LFSR, envelope, length counter |
| DMC | 多欄位 | timer, sample address/length, buffer, DMA state |
| Frame counter | int | framectrdiv, mode, step |
| IRQ 狀態 | 多欄位 | frameIrqFlag, dmcIrqFlag |
| 音訊緩衝區 | — | **不保存**（讀檔後重建） |

### 3.4 記憶體（MEM.cs）

| 欄位 | 型別 | 說明 |
|------|------|------|
| RAM[0x800] | byte* | 2KB 主記憶體 |
| SRAM[0x2000] | byte* | 8KB 電池備援記憶體 |
| cpuBusAddr | ushort | 最後 bus 位址 |
| cpuBusIsWrite | bool | 最後 bus 方向 |
| `openbus` | byte | open bus 值 |
| DMA 狀態 | 多欄位 | OAM DMA、DMC DMA countdown/flags |

> **function pointer tables**（`mem_read_fun[]`, `mem_write_fun[]`）不保存，讀檔後由 Mapper 重建。

### 3.5 I/O（IO.cs）+ JoyPad

| 欄位 | 型別 | 說明 |
|------|------|------|
| JoyPad strobe | bool | 控制器讀取狀態 |
| JoyPad shift registers | byte | 按鈕讀取位移 |

### 3.6 Mapper（各 Mapper 類別）

**這是最大的工作量。** 每個 Mapper 有不同的 bank 切換暫存器、IRQ 計數器、特殊狀態。

| Mapper | 需保存的典型狀態 |
|--------|-----------------|
| 000 (NROM) | 無額外狀態 |
| 001 (MMC1) | shift register, control, chr/prg bank |
| 002 (UxROM) | prg bank |
| 003 (CNROM) | chr bank |
| 004 (MMC3) | bank registers ×8, IRQ latch/counter/enable, mirroring |
| 005 (MMC5) | 大量內部暫存器 |
| 其他... | 各有不同 |

### 3.7 不需要保存的項目

- 音訊播放緩衝區（WaveOut buffers）→ 讀檔後靜音再恢復
- Graphics context / RenderObj → UI 層，不屬於模擬狀態
- AnalogScreenBuf / linearBuffer → 渲染緩衝，下一幀自動重繪
- function pointer tables → 由 Mapper 重建

---

## 四、檔案格式

### 4.1 二進位格式（BinaryWriter / BinaryReader）

```
[Header]
  Magic:     4 bytes  "APRS"
  Version:   uint16   格式版本號（向前相容用）
  MapperID:  uint16   Mapper 編號（讀檔時驗證）
  CRC32:     uint32   ROM CRC（讀檔時驗證是否為同一 ROM）

[CPU Section]
  reg_A:     1 byte
  reg_X:     1 byte
  reg_Y:     1 byte
  reg_SP:    1 byte
  reg_PC:    2 bytes (LE)
  flags:     1 byte
  cpuCycleCount: 8 bytes (LE)
  ...

[PPU Section]
  VRAM:      16384 bytes
  OAM:       256 bytes
  secondaryOAM: 32 bytes
  registers: N bytes
  ...

[APU Section]
  ...

[MEM Section]
  RAM:       2048 bytes
  SRAM:      8192 bytes
  ...

[Mapper Section]
  (Mapper 自行決定格式與長度)
```

### 4.2 版本相容

- Header 含版本號，未來欄位增減時可向前相容
- 讀檔時檢查 MapperID 和 CRC32，不匹配則拒絕載入

---

## 五、IMapper 介面擴展

```csharp
public interface IMapper
{
    // 既有方法...

    // Save State 擴展
    void SaveState(BinaryWriter w);
    void LoadState(BinaryReader r);
}
```

每個 Mapper 實作自己的 `SaveState`/`LoadState`，負責序列化/反序列化 Mapper 專屬的 bank 暫存器、IRQ 狀態等。

---

## 六、NesCore 存讀檔流程

### 6.1 Save State

```csharp
static void SaveState(string path)
{
    // 1. 暫停模擬執行緒
    // 2. BinaryWriter 開檔
    // 3. 寫入 Header（magic, version, mapperID, CRC32）
    // 4. CPU.SaveState(w)
    // 5. PPU.SaveState(w)
    // 6. APU.SaveState(w)
    // 7. MEM.SaveState(w)   — RAM, SRAM, DMA state
    // 8. IO.SaveState(w)
    // 9. mapper.SaveState(w)
    // 10. 關閉檔案
    // 11. 恢復模擬執行緒
}
```

### 6.2 Load State

```csharp
static void LoadState(string path)
{
    // 1. 暫停模擬執行緒
    // 2. BinaryReader 開檔
    // 3. 讀取並驗證 Header（magic, version, mapperID, CRC32）
    // 4. CPU.LoadState(r)
    // 5. PPU.LoadState(r)
    // 6. APU.LoadState(r)   — 靜音後恢復
    // 7. MEM.LoadState(r)
    // 8. IO.LoadState(r)
    // 9. mapper.LoadState(r) — 含重建 function pointer tables
    // 10. 關閉檔案
    // 11. 恢復模擬執行緒
}
```

---

## 七、UI 整合

### 7.1 右鍵選單

```
Save State  ▸  │ Slot 1          │
               │ Slot 2          │
               │ ...             │
               │ Slot 8          │

Load State  ▸  │ Slot 1          │
               │ Slot 2          │
               │ ...             │
               │ Slot 8          │
```

已使用的 Slot 顯示時間戳：`Slot 1 (2026-03-22 14:30)`

### 7.2 快捷鍵處理

在 `ProcessCmdKey` 中加入：

```csharp
case Keys.F5:
    if (running) SaveState(currentSlot);
    return true;
case Keys.F7:
    if (running) LoadState(currentSlot);
    return true;
case Keys.Shift | Keys.D1: // ~ Keys.Shift | Keys.D8
    currentSlot = N;
    ShowSlotNotification(N);
    return true;
```

### 7.3 語系支援

| Key | en-us | zh-tw | zh-cn |
|-----|-------|-------|-------|
| save_state | Save State | 即時存檔 | 即时存档 |
| load_state | Load State | 即時讀檔 | 即时读档 |
| slot | Slot | 存檔槽 | 存档槽 |
| state_saved | State saved to Slot {N} | 已存檔到槽 {N} | 已存档到槽 {N} |
| state_loaded | State loaded from Slot {N} | 已從槽 {N} 讀檔 | 已从槽 {N} 读档 |
| state_not_found | No save state in Slot {N} | 槽 {N} 無存檔 | 槽 {N} 无存档 |
| state_mismatch | Save state does not match current ROM | 存檔與目前 ROM 不符 | 存档与当前 ROM 不符 |

---

## 八、實作優先順序

### Phase 1：框架驗證（最小可行）

1. NesCore 加 `SaveState(BinaryWriter)` / `LoadState(BinaryReader)` 骨架
2. 實作 CPU + MEM（RAM/SRAM）存讀
3. 實作 PPU 存讀（VRAM, OAM, 所有暫存器）
4. 實作 APU 存讀
5. IMapper 介面加 `SaveState`/`LoadState`
6. **只實作 Mapper000 (NROM)** 的 Save/Load
7. 用簡單遊戲（如 Donkey Kong / NROM）驗證存讀正確性
8. 加入 F5/F7 快捷鍵 + 右鍵選單

### Phase 2：Mapper 擴展

9. 依使用率逐步實作各 Mapper 的 Save/Load：
   - Mapper001 (MMC1) — Super Mario Bros 2, Zelda
   - Mapper002 (UxROM) — Mega Man, Castlevania
   - Mapper003 (CNROM) — 多數早期遊戲
   - Mapper004 (MMC3) — Super Mario Bros 3, Kirby
   - 其餘 25 個 mapper 依需求推進

### Phase 3：UI 完善

10. Slot 子選單 + 時間戳顯示
11. Slot 切換快捷鍵 (Shift+1~8)
12. 存讀完成的畫面提示（短暫 OSD 或 label）

---

## 九、風險與注意事項

1. **欄位遺漏**：任何一個 timing-critical 的計數器遺漏，都會導致讀檔後音訊爆音或畫面閃爍。需逐一比對所有 static 欄位。
2. **Mapper 狀態量最大**：29 個已實作 mapper，每個都要手動寫 Save/Load。建議在每個 mapper 實作時同步加入，避免事後補。
3. **unmanaged memory**：`byte*` 緩衝區需用 `Marshal.Copy` 轉為 `byte[]` 再寫入，讀取時反向操作。
4. **執行緒安全**：存讀檔必須暫停模擬執行緒（同 ApplyRenderSettings 模式），避免讀寫衝突。
5. **版本相容**：一旦發布 Save State 功能，格式變更需向前相容（透過 version 欄位跳過未知 section）。
6. **檔案大小**：預估每個 state 約 **30~40 KB**（VRAM 16KB + RAM 2KB + SRAM 8KB + 其他），非常小。
