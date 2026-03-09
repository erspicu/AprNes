# AprNes 測試報告

- 日期: 2026-02-20 (v2 — 修復後)
- 測試框架: Console Test Runner (headless mode)
- 測試 ROM 來源: `nes-test-roms-master/checked/`
- 預設參數: `--wait-result --max-wait 30`
- 合併 ROM 參數: `--max-wait 120`

## 總結: 95 PASS / 17 FAIL (共 112 個 ROM)

### 本次修復內容

| 修復項目 | 檔案 | 說明 |
|---|---|---|
| APU open bus | IO.cs | IO_read() default 從 `return 0x40` 改為 `return openbus` |
| Read 指令 dummy read | CPU.cs | 25 個 opcode 加入跨頁 dummy read (abs,X/abs,Y/(ind),Y) |
| STA abs,Y dummy read | CPU.cs | 0x99 補 dummy read (store 指令永遠需要) |
| RMW abs,X dummy read | CPU.cs | ASL/LSR/ROR/DEC/INC abs,X 補 wrong-page dummy read |
| 非官方 NOP page-cross | CPU.cs | 0x1C/3C/5C/7C/DC/FC 加 page-cross 偵測 + dummy read |
| NOP imm cycle 修正 | CPU.cs | 0xE2 cycle table 從 3 改為 2 |
| PPU even/odd frame | PPU.cs | skip cycle 改為 339，條件改為 ShowBG \|\| ShowSprites |

> 修復後官方指令的 dummy read 全數通過 (04-dummy_reads_apu 現只列出非官方 opcode)。
> NOP page-cross cycle 已修正 (instr_timing 現只列出非官方指令 timing 問題)。
> 測試結果數量不變 (95/17) 但多個失敗測試的內部進展大幅改善。

---

## 各測試套件結果

| 測試套件 | 通過 | 總數 | 狀態 |
|---|---|---|---|
| blargg_nes_cpu_test5 | 2 | 2 | PASS |
| blargg_ppu_tests_2005 | 5 | 5 | PASS |
| branch_timing_tests | 3 | 3 | PASS |
| cpu_dummy_reads | 1 | 1 | PASS |
| cpu_dummy_writes | 2 | 2 | PASS |
| cpu_exec_space | 1 | 2 | 1 FAIL |
| cpu_interrupts_v2 | 4 | 6 | 2 FAIL |
| cpu_timing_test6 | 1 | 1 | PASS |
| instr_misc | 2 | 5 | 3 FAIL |
| instr_test-v3 | 17 | 17 | PASS |
| instr_test-v5 | 18 | 18 | PASS |
| instr_timing | 1 | 3 | 2 FAIL |
| nes_instr_test | 11 | 11 | PASS |
| oam_read | 1 | 1 | PASS |
| ppu_open_bus | 1 | 1 | PASS |
| ppu_vbl_nmi | 3 | 10 | 7 FAIL |
| sprite_hit_tests | 11 | 11 | PASS |
| sprite_overflow_tests | 5 | 5 | PASS |
| vbl_nmi_timing | 7 | 7 | PASS |

---

## 完全通過的測試套件 (14 套)

### blargg_nes_cpu_test5 (2/2)
- official.nes
- cpu.nes

### blargg_ppu_tests_2005 (5/5)
- palette_ram.nes
- power_up_palette.nes
- sprite_ram.nes
- vbl_clear_time.nes
- vram_access.nes

### branch_timing_tests (3/3)
- 1.Branch_Basics.nes
- 2.Backward_Branch.nes
- 3.Forward_Branch.nes

### cpu_dummy_reads (1/1)
- cpu_dummy_reads.nes

### cpu_dummy_writes (2/2)
- cpu_dummy_writes_oam.nes
- cpu_dummy_writes_ppumem.nes

### cpu_timing_test6 (1/1)
- cpu_timing_test.nes

### instr_test-v3 (17/17)
- official_only.nes — All 15 tests passed
- all_instrs.nes — All 15 tests passed (max-wait 120)
- rom_singles: 01-implied ~ 15-special 全過

### instr_test-v5 (18/18)
- official_only.nes — All 16 tests passed (max-wait 120)
- all_instrs.nes — All 16 tests passed (max-wait 120)
- rom_singles: 01-basics ~ 16-special 全過

### nes_instr_test (11/11)
- 01-implied ~ 11-special 全過

### oam_read (1/1)
- oam_read.nes

### ppu_open_bus (1/1)
- ppu_open_bus.nes

### sprite_hit_tests (11/11)
- 01.basics ~ 11.edge_timing 全過

### sprite_overflow_tests (5/5)
- 1.Basics ~ 5.Emulator 全過

### vbl_nmi_timing (7/7)
- 1.frame_basics ~ 7.nmi_timing 全過

---

## 失敗清單 (17 個)

### 類別 1: PPU VBL/NMI 子掃描線時序 (7 個)

| ROM | 錯誤訊息 |
|---|---|
| ppu_vbl_nmi/ppu_vbl_nmi.nes | 合併 ROM，在 test 2/10 (vbl_set_time) 失敗 |
| ppu_vbl_nmi/02-vbl_set_time.nes | VBL flag 設定時間差 1 PPU cycle |
| ppu_vbl_nmi/03-vbl_clear_time.nes | VBL flag 清除時間不準 |
| ppu_vbl_nmi/04-nmi_control.nes | "Immediate occurence should be after NEXT instruction" |
| ppu_vbl_nmi/06-suppression.nes | VBL read suppression 時序不準 |
| ppu_vbl_nmi/07-nmi_on_timing.nes | NMI enable 生效時序差異 |
| ppu_vbl_nmi/08-nmi_off_timing.nes | NMI disable 生效時序差異 |

> 根本原因: PPU 的 VBL flag set/clear 與 NMI enable/disable 的生效時機在 sub-scanline 層級不夠精確。需要 cycle-accurate CPU/PPU 同步。

### 類別 2: CPU Dummy Read / APU I/O (5 個)

| ROM | 錯誤訊息 | 修復進展 |
|---|---|---|
| cpu_exec_space/test_cpu_exec_space_apu.nes | APU I/O 空間執行程式碼時跳到錯誤位址 | 需要 CPU fetch 經過 IO_read |
| instr_misc/instr_misc.nes | 合併 ROM，test 4 (dummy_reads_apu) 卡住 | 官方 opcode 已全過，卡在非官方 opcode |
| instr_misc/03-dummy_reads.nes | "STA abs,x" PPU dummy read 行為不正確 | STA abs,X 已有 dummy read，可能是 PPU 副作用問題 |
| instr_misc/04-dummy_reads_apu.nes | 只剩非官方 opcode 的 dummy read 失敗 | **官方 opcode 全數通過** |
| ppu_vbl_nmi/10-even_odd_timing.nes | "Clock is skipped too late" (08 vs 07) | 需要 cycle-accurate CPU/PPU 同步 |

> 修復進展: APU open bus 已修正。25 個官方 opcode 的 dummy read 已補齊。剩餘失敗皆為非官方 opcode 或需要 cycle-accurate 模擬。

### 類別 3: CPU 中斷 + DMA 交互 (3 個)

| ROM | 錯誤訊息 |
|---|---|
| cpu_interrupts_v2/cpu_interrupts.nes | 合併 ROM，在 test 4/5 (irq_and_dma) 失敗 |
| cpu_interrupts_v2/4-irq_and_dma.nes | IRQ 與 OAM DMA 的 cycle 精度交互不準 |
| cpu_interrupts_v2/5-branch_delays_irq.nes | 分支指令延遲 IRQ 的 cycle 計數差異 |

> 根本原因: OAM DMA 期間的 IRQ 取樣時機，以及分支跨頁時的 IRQ 延遲行為不正確。需要 cycle-accurate DMA 實作。

### 類別 4: 指令 Timing — Illegal Opcode (2 個)

| ROM | 錯誤訊息 | 修復進展 |
|---|---|---|
| instr_timing/instr_timing.nes | 非官方指令 timing 為 0 | **NOP page-cross 已修正**，剩餘皆為未實作的非官方指令 |
| instr_timing/1-instr_timing.nes | E2 cycle 已修正; 8B/93/9B/9F/BB 等未實作 | **NOP page-cross 已修正** |

> 修復進展: NOP abs,X page-cross +1 cycle 已修正。E2 cycle 已修正為 2。剩餘失敗全部是未實作的非官方指令 (8B, 93, 9B, 9F, BB 等)，timing 為 0。

---

## 未來修復方向

| 優先度 | 項目 | 影響數 | 難度 | 備註 |
|---|---|---|---|---|
| 1 | 非官方指令 timing | 2 | 中 | 需實作 ~20 個非官方 opcode |
| 2 | CPU fetch 經過 IO_read | 1 | 中 | cpu_exec_space_apu |
| 3 | IRQ + DMA 交互時序 | 3 | 高 | 需要 cycle-accurate OAM DMA |
| 4 | PPU sub-cycle VBL/NMI | 8 | 極高 | 需要 cycle-accurate CPU/PPU 同步 |
