# Famicom Disk System (FDS) 實作規劃

## 硬體概述

FDS (Famicom Disk System) 是任天堂 1986 年發售的紅白機磁碟機周邊，透過 RAM Adapter 插入卡帶槽。

### 硬體組成
- **RAM Adapter**: 插入卡帶槽，提供 32KB PRG-RAM + 8KB CHR-RAM
- **磁碟機**: 讀寫 Quick Disk 格式磁碟片（每面 65,500 bytes）
- **BIOS ROM**: 8KB，內含磁碟 I/O 常式、遊戲載入器
- **音效晶片**: Wavetable 合成器 + Frequency Modulation Unit
- **IRQ Timer**: 16-bit countdown timer

## 記憶體映射

```
$0000-$1FFF  CHR-RAM (8KB, PPU pattern table)
$6000-$DFFF  PRG-RAM (32KB, 遊戲程式/資料由磁碟載入)
$E000-$FFFF  BIOS ROM (8KB, disksys.rom)
```

## 暫存器映射 ($4020-$4092)

### 磁碟 I/O 暫存器
| 位址 | R/W | 名稱 | 說明 |
|------|-----|------|------|
| $4020 | W | IRQ Reload Low | IRQ 計數器重載值 (低 8 bit) |
| $4021 | W | IRQ Reload High | IRQ 計數器重載值 (高 8 bit) |
| $4022 | W | IRQ Control | bit0=重複, bit1=啟用 |
| $4023 | W | Master I/O Enable | bit0=磁碟 I/O, bit1=音效 |
| $4024 | W | Write Data | 磁碟寫入資料暫存器 |
| $4025 | W | Drive Control | bit0=馬達, bit2=讀/寫, bit3=鏡像, bit4=CRC, bit6=就緒, bit7=磁碟 IRQ |
| $4026 | W | External Connector | 外部連接器輸出 |
| $4030 | R | Disk Status | bit0=Timer IRQ, bit1=Transfer完成, bit4=CRC錯誤, bit6=資料就緒 |
| $4031 | R | Read Data | 磁碟讀取資料暫存器 |
| $4032 | R | Drive Status | bit0=碟片已插入, bit1=碟片就緒, bit2=碟片保護 |
| $4033 | R | External Connector | 外部連接器輸入 (bit7=電池狀態) |

### 音效暫存器
| 位址 | R/W | 名稱 | 說明 |
|------|-----|------|------|
| $4040-$407F | R/W | Wave Table | 64 bytes wavetable (6-bit samples) |
| $4080 | W | Volume Envelope | bit[5:0]=速度, bit6=增/減, bit7=停用 |
| $4082 | W | Freq Low | 主頻率低 8 bit |
| $4083 | W | Freq High | bit[3:0]=主頻率高 4 bit, bit6=envelope halt, bit7=wave halt |
| $4084 | W | Mod Envelope | bit[5:0]=速度, bit6=增/減, bit7=停用 |
| $4085 | W | Mod Counter | 7-bit signed modulation (-64~+63) |
| $4086 | W | Mod Freq Low | 調變頻率低 8 bit |
| $4087 | W | Mod Freq High | bit[3:0]=調變頻率高 4 bit, bit7=停用調變 |
| $4088 | W | Mod Table | 調變查表值 (bit[2:0]) |
| $4089 | W | Master Volume | bit[1:0]=音量(0-3), bit7=wave write enable |
| $408A | W | Envelope Speed | 主 envelope 速率乘數 |

## 磁碟格式

### .fds 檔案結構
```
[16 bytes header]  "FDS\x1a" + side count + padding
[Side 0]           65,500 bytes
[Side 1]           65,500 bytes (如果有)
...
```

### 碟片區塊結構
```
Gap (28,300 bits) → Block 1 (Disk Header, 56 bytes) → CRC (2 bytes)
→ Gap (976 bits) → Block 2 (File Count, 2 bytes) → CRC
→ Gap → Block 3 (File Header, 16 bytes) → CRC
→ Gap → Block 4 (File Data, variable) → CRC
→ Gap → Block 3 → CRC → Gap → Block 4 → CRC → ...
```

### 區塊類型
- **Type 0x01**: Disk Header (56 bytes) — 磁碟識別資訊
- **Type 0x02**: File Count (2 bytes) — 檔案數量
- **Type 0x03**: File Header (16 bytes) — 檔案名稱/大小/載入位址
- **Type 0x04**: File Data (variable) — 檔案內容

### CRC 演算法
```
CRC-CCITT, polynomial 0x8408
accumulator ^= byte;
for 8 bits: if carry → accumulator ^= 0x8408
```

## 磁碟 I/O 狀態機

```
Motor Off
    │
    ▼ ($4025 bit0=1)
Motor On → 等待 50,000 cycles (磁頭定位)
    │
    ▼
Gap Scanning (讀取 0x00 bytes)
    │
    ▼ (遇到第一個非 0x00 byte)
Gap End → 設 transferComplete, 觸發 IRQ (if enabled)
    │
    ▼
Block Read/Write Loop
    │  每 byte: 149 CPU cycles
    │  讀: disk[position] → $4031
    │  寫: $4024 → disk[position]
    │  更新 CRC (if crcControl=0)
    │
    ▼ (position >= diskSize)
End of Disk → 自動退碟 (77 frames 後)
```

### 關鍵時序參數
- **傳輸速率**: 149 CPU cycles/byte (~96.3 bits/second)
- **初始延遲**: 50,000 CPU cycles (磁頭就位)
- **區塊間延遲**: 149 cycles
- **自動退碟**: 77 frames after end-of-disk

## FDS 音效

### Wavetable 合成器
- 64-sample, 6-bit 波形表 ($4040-$407F)
- 12-bit 頻率暫存器 ($4082-$4083)
- Volume envelope (可選自動增減)
- 16-bit phase accumulator

### Frequency Modulation Unit
- 獨立 12-bit 頻率
- 32-entry modulation table ($4088 寫入)
- Modulation lookup: `{0, 1, 2, 4, reset, -4, -2, -1}`
- 調變輸出加到主頻率上

### 音量計算
```
level = min(gain, 32) × WaveVolumeTable[masterVolume]
output = (waveTable[position] × level) / 1152

WaveVolumeTable = {36, 24, 17, 14}  // masterVolume 0-3
```

### 與 AprNes expansion audio 系統整合
- `ExpansionChipType.FDS` (已在 enum 中預留)
- `expansionChannelCount = 1` (單 channel 輸出)
- `DefaultChipGain[FDS] = 20` (已預留，參考 Mesen2)

## 實作計畫

### Phase 1: 基礎框架
- [ ] 建立 MapperFDS.cs (或 Mapper020.cs)
- [ ] .fds 檔案解析 (header, side data, gap padding)
- [ ] BIOS ROM 載入 (disksys.rom)
- [ ] 記憶體映射 (PRG-RAM $6000-$DFFF, BIOS $E000-$FFFF, CHR-RAM)
- [ ] IRQ Timer ($4020-$4022, $4030 bit0)
- [ ] MapperRegistry 登記

### Phase 2: 磁碟 I/O
- [ ] 磁碟讀取狀態機 (gap scanning → block read → CRC)
- [ ] $4024/$4025/$4030/$4031/$4032 暫存器完整實作
- [ ] 傳輸完成 IRQ
- [ ] 基本遊戲測試 (能載入並執行)

### Phase 3: 音效
- [ ] Wavetable 合成 (64-sample, 6-bit)
- [ ] Volume envelope
- [ ] Frequency Modulation Unit
- [ ] 接入 expansion audio per-channel 系統
- [ ] 音效測試

### Phase 4: 使用體驗
- [ ] 手動換碟 (UI 快捷鍵: 退碟/插入指定面)
- [ ] 自動換碟偵測 (偵測 BIOS 呼叫 $E18C/$E445)
- [ ] BIOS 路徑設定 UI
- [ ] 存檔支援 (IPS patch 或直接寫回)

## IMapper 介面適配

現有 IMapper 介面大致可用：
- `MapperR_RPG` / `MapperW_PRG` → PRG-RAM 讀寫
- `MapperR_ExpansionROM` / `MapperW_ExpansionROM` → $4020-$4092 暫存器
- `CpuCycle()` → IRQ timer + 磁碟 I/O + 音效 (每 CPU cycle)
- `UpdateCHRBanks()` → CHR-RAM 映射

### 需要額外處理
- **BIOS 載入**: MapperInit 時額外讀取 disksys.rom 到 $E000-$FFFF
- **多碟面儲存**: 內部管理碟面資料陣列 (byte[][])
- **碟片操作**: 需要新的公開方法 (InsertDisk/EjectDisk) 供 UI 呼叫

## 預估工作量

| 模組 | 行數 | 難度 |
|------|------|------|
| 磁碟 I/O 狀態機 | ~300 行 | ★★★ |
| 暫存器處理 | ~200 行 | ★★ |
| FDS 音效 | ~200 行 | ★★ |
| .fds 檔案解析 | ~150 行 | ★ |
| BIOS 載入 | ~50 行 | ★ |
| 換碟邏輯 | ~100 行 | ★★ |
| **合計** | **~1,000-1,200 行** | |

## 參考資料

- **Mesen2 FDS 實作**: `ref/Mesen2-master/Core/NES/Mappers/FDS/`
  - `Fds.cpp` (623 行) — 主邏輯
  - `FdsAudio.cpp` (188 行) — 音效
  - `ModChannel.h` — 調變器
  - `BaseFdsChannel.h` — Envelope 基底
- **Mesen2 FDS Loader**: `ref/Mesen2-master/Core/NES/Loaders/FdsLoader.cpp` (199 行)
- **NESdev Wiki**: FDS 技術文件 (需下載)

## 測試 ROM

- `etc/fds_out/` 目錄下有 72 個 .fds 遊戲可供測試
- 建議優先測試: Super Mario Bros. 2, Zelda no Densetsu, Metroid
