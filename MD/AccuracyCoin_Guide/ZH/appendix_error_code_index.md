# 附錄 A：各頁 / Error Code 速查索引

> 這不是 error code 的全文（那在 ROM 自帶的 `README.md` 裡，逐碼都有官方說明）。這是**導覽索引** —— 20 頁分別考什麼、對應本指南哪一章、最該注意哪些坑。
> 完整 error code 文字：`nes-test-roms-master/AccuracyCoin-main-20260521/README.md`。
> 看不懂某碼在驗什麼：搜 `.asm` 的 `TEST_<測試名>:`，數 `INC <ErrorCode` 之間的 sub-test（`ErrorCode` 從 1 起算，fail N = 第 N 個 sub-test）。詳見 [`00_methodology.md`](00_methodology.md) §3。

AccuracyCoin `20260521` 共 **20 頁、139 項 PASS/FAIL 測試 + 5 項 DRAW**。

---

## 頁面總表

| Page | 主題 | 本指南章節 | 備註 / 招牌坑 |
|------|------|-----------|---------------|
| **P1** | CPU Behavior | [`01_cpu`](01_cpu.md) | ROM 不可寫、RAM mirroring、PC wraparound、**decimal flag**、**B flag**、dummy read/write、**open bus**、all NOP |
| **P2–P9** | Unofficial Opcodes | [`01_cpu`](01_cpu.md) §4 | SLO/RLA/SRE/RRA、SAX/LAX、DCP、ISC… —— 多為官方指令組合，補齊 cycle + dummy read |
| **P10** | Unofficial: SH\* | [`01_cpu`](01_cpu.md) §4 | **SHA/SHX/SHY/SHS** 的 `&(H+1)`；DMA 插在 write 前要 **ignoreH** |
| **P11** | Unofficial: Misc | [`01_cpu`](01_cpu.md) §4 | ANC/ASR/ARR/ANE/LXA/AXS/SBC immediate |
| **P12** | CPU Interrupts | [`01_cpu`](01_cpu.md) §5 | **Interrupt flag latency**、NMI Overlap BRK/IRQ —— penultimate-cycle 取樣、中斷序列不 poll NMI |
| **P13** | DMA Tests | [`02_dma`](02_dma.md) | DMA + Open Bus/$2002/$2007R/$2007W/$4015R/$4016R、Bus Conflicts、**Explicit / Implicit DMA Abort** |
| **P14** | APU Tests | [`03_apu`](03_apu.md) | Length Counter/Table、**Frame Counter IRQ**（deferred clear）、DMC、**APU Register Activation**、Controller Strobing/Clocking |
| **P15** | Power On State | — | **DRAW only**（PPU Reset Flag / CPU RAM / CPU Registers / PPU RAM / Palette RAM 的開機值）；不自動判定，截圖看 |
| **P16** | PPU Rendering / Registers | [`04_ppu`](04_ppu.md) §3,4,6 | CHR ROM 不可寫、Register Mirroring/Open Bus、**Read Buffer**、**Palette RAM Quirks**、**Rendering Flag**、$2007 read w/ rendering |
| **P17** | PPU VBlank Timing | [`04_ppu`](04_ppu.md) §1 | VBlank begin/end、**NMI Control/Timing/Suppression**、NMI at/disabled-at VBlank |
| **P18** | Sprite Evaluation | [`04_ppu`](04_ppu.md) §2,5 | Sprite overflow、**Sprite 0 Hit**、**$2002 flag timing**（M2 stagger）、Suddenly Resize、Arbitrary Sprite Zero、Misaligned OAM、$2004、**OAM Corruption**、INC $4014 |
| **P19** | PPU Misc（進階）| [`04_ppu`](04_ppu.md) §5,6 | Attributes As Tiles、t Register Quirks、**Stale BG / Sprite Shift Registers**、BG Serial In、**Sprites On Scanline 0**、$2004 / $2007 Stress |
| **P20** | CPU Behavior 2 | [`01_cpu`](01_cpu.md) §1,2,6 | Instruction Timing、Implied / Branch Dummy Reads、JSR Edge Cases、**Internal Data Bus** |

---

## 最容易卡關的 error code（按難度，附我們的修法）

> 「招牌題」—— 過了這些，整套 AC 也就差不多了。每條都連到完整修復紀錄。

| 測試（page）| code | 在驗什麼 | 修法 |
|------------|------|----------|------|
| Internal Data Bus (P20) | 2 | `$4015` bit5 internal vs external bus | [dual data-bus](../../bugfix/2026-05-22_AC_InternalDataBus_DualDataBus.md)（CPU 讀 internal、DMA 讀 external）|
| APU Register Activation (P14) | 6/7 | OAM DMA 讀 APU 暫存器 + $20 mirror + bus conflict | [BUGFIX46](../../bugfix/2026-03-08_BUGFIX46.md) + dual-bus |
| Implicit DMA Abort (P13) | 2 | 1-byte sample 將盡時 enable → 幽靈 1-cycle DMA | [BUGFIX56](../../bugfix/2026-03-14_BUGFIX56_Implicit_DMA_Abort.md)（衝上 v1 136/136 的最後一題）|
| Explicit DMA Abort (P13) | 2 | DMA 中 disable 的 deferred delay parity | [BUGFIX55](../../bugfix/2026-03-13_BUGFIX55_Explicit_DMA_Abort.md) |
| Frame Counter IRQ (P14) | 7 | 讀 `$4015` 延遲清 IRQ flag + inhibit 是「發生後撤回」 | [BUGFIX37](../../bugfix/2026-03-07_BUGFIX37.md) |
| $2002 flag timing (P18) | 1 | sprite flags 比 VBL 早 ~2 dot 清（M2 duty 15/24）| [BUGFIX45](../../bugfix/2026-03-07_BUGFIX45.md) |
| Sprites On Scanline 0 (P19) | 2 | pre-render line `(261&255)=5` + secondary OAM 殘留 | [BUGFIX47](../../bugfix/2026-03-08_BUGFIX47.md) |
| $2004 Stress Test (P19) | — | 渲染中逐 dot 讀 OAM buffer | [BUGFIX48](../../bugfix/2026-03-08_BUGFIX48.md) |
| SH\* opcodes (P10) | — | DMA 插在 write 前消除 H masking | [BUGFIX51](../../bugfix/2026-03-10_BUGFIX51_SH_opcodes.md) |
| Open Bus (P1) | 1,4,9 | open bus = data bus 殘值；ZP 也要更新；$4015 bit5 | [BUGFIX29](../../bugfix/2026-03-04_BUGFIX29.md) |
| Branch Dummy Reads (P20) | 4,5 | taken branch 的 dummy read 要真的讀記憶體 | [BUGFIX29](../../bugfix/2026-03-04_BUGFIX29.md) |

> 完整由前到後的修復順序見 [`00_fix_history.md`](00_fix_history.md)。
