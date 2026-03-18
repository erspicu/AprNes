# NES Mapper 子變體辨識筆記

整理自 2026-02-21 開發對談，供日後擴充 mapper 時參考。

---

## 問題本質

同一個 iNES mapper number 可能對應多種不同硬體變體，行為有差異。
iNES 1.0 header **無法區分**這些子變體。NES 2.0 新增了 submapper 欄位，
但多數 ROM dump 仍為 iNES 1.0 格式。

---

## MMC3 (Mapper 004) 子變體

### Rev A vs Rev B

| 項目 | Rev A (早期) | Rev B (後期，較常見) |
|------|-------------|---------------------|
| counter==0 時 | reload from latch | reload from latch |
| reload flag 設定時 | 標記「下次歸零時 reload」 | **立即** reload from latch |
| 常見遊戲 | Star Wars, 極少數 | 絕大多數 MMC3 遊戲 |

**AprNes 目前**: 實作 Rev B 行為。`mmc3_irq_tests/5.MMC3_rev_A` 預期 FAIL。

### MMC6

- 與 MMC3 共用 mapper 004，但晶片不同
- 額外功能：PRG-RAM 有 per-1KB bank 的讀寫保護機制
- 使用遊戲極少（StarTropics 系列等）
- `mmc3_test/6-MMC6` 預期 FAIL

### 判斷方式

- **檔名不可靠**：檔名中的 "Rev A" 指遊戲軟體版本，非晶片版本
- **Header 無法判斷**：iNES 1.0 只有 mapper number，無子類型
- **唯一可靠方式**：ROM CRC32/SHA1 查表（NesCartDB、No-Intro DAT）

---

## 其他有子變體問題的 Mapper

### 嚴重（影響遊戲正確性）

| Mapper | 名稱 | 變體數 | 問題描述 |
|--------|------|--------|---------|
| 021/023/025 | VRC2/VRC4 | 7 | VRC2a, VRC2b, VRC4a~VRC4e。差異在 address line A0/A1 接線方式。NES 2.0 用 submapper 解決 |
| 024/026 | VRC6 | 2 | VRC6a vs VRC6b，A0/A1 互換 |
| 085 | VRC7 | 2 | 類似 VRC6，address line swap |
| 016 | Bandai FCG | 4 | FCG-1/2, LZ93D50, LZ93D50+EEPROM, 24C02。後來拆成 016/153/157/159，但舊 dump 仍標 016 |
| 019 | Namco 163/175/340 | 3 | 功能差異大（175/340 無擴充音源、不同 RAM 配置） |

### 中等（特定功能受影響）

| Mapper | 名稱 | 問題描述 |
|--------|------|---------|
| 001 | MMC1 | SNROM/SOROM/SUROM/SXROM 等板型，PRG-RAM 大小和 banking 方式不同 |
| 069 | Sunsoft FME-7/5B | 同為 069，但 Sunsoft 5B 多 3 個擴充音源聲道 |

### AprNes 目前實作的 Mapper

| Mapper | 名稱 | 子變體風險 |
|--------|------|-----------|
| 000 | NROM | 無 |
| 001 | MMC1 | 低（板型差異影響 PRG-RAM） |
| 002 | UxROM | 無 |
| 003 | CNROM | 無 |
| **004** | **MMC3** | **有（Rev A/B, MMC6）** |
| 005 | MMC5 | Stub，未完整實作 |
| 007 | AxROM | 無 |
| 011 | Color Dreams | 無 |
| 066 | GxROM | 無 |
| 071 | Camerica | 無 |

---

## 建議的 CRC32 查表機制（未來實作）

最小成本方案：

1. ROM 載入時計算 CRC32（資料已在記憶體，幾乎零成本）
2. 用 `Dictionary<uint, MapperSubType>` 對照已知特殊 ROM
3. 查表結果影響 mapper 初始化參數（如 Rev A/B flag）

MMC3 Rev A 已知遊戲極少（約 3-5 款），硬編即可：

```
遊戲                           CRC32       子類型
Star Wars (USA)               xxxxxxxx    MMC3 Rev A
Startropics (USA)             xxxxxxxx    MMC6
Startropics II (USA)          xxxxxxxx    MMC6
```

> 精確 CRC32 值需查 NesCartDB 或 No-Intro DAT 確認。

---

*最後更新: 2026-02-21*
