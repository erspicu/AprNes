# TriCNES Emulator Core / Mapper 差異分析

## 範圍

本文件只分析模擬器核心與 mapper：

- `old-TriCNES-main/Emulator.cs`
- `new-TriCNES-main/Emulator.cs`
- `old-TriCNES-main/mappers/*.cs`
- `new-TriCNES-main/mappers/*.cs`

不分析 GUI、forms、Program、測試輔助檔。

## 一開始的快速結論

最初比對 `old-Emulator.cs` 與 `new-Emulator.cs` 單檔時，整體 diff 規模約為：

- `649` 行新增
- `680` 行刪除

這不是單純重命名或重排，而是一次偏架構層級的改寫。主軸不是 CPU 指令核心，而是：

- `PPU` 內部時序
- `$2007` 讀寫流程
- PPU bus / address latch 模型
- `FDS` 裝置時鐘與 IRQ
- mapper 對 PRGRAM / nametable / PPU fetch 的責任重新分配

一句話總結：

`old` 比較像「高階行為模擬」，`new` 比較明顯往「匯流排/鎖存器/半拍時序」的硬體模型前進。

---

## 檔案層級變化

### `Emulator.cs`

- 是這次主要改動來源
- diff 統計：`649 insertions / 680 deletions`

### `mappers/`

只有以下檔案有實質差異：

- `Mapper_FDS.cs`
- `Mapper_MMC3.cs`
- `Mapper_MMC2.cs`
- `Mapper_AOROM.cs`
- `Mapper_CNROM.cs`
- `Mapper_UxROM.cs`

沒有差異的 mapper：

- `Mapper_FME7.cs`
- `Mapper_MMC1.cs`
- `Mapper_NROM.cs`
- `Mapper_NULL.cs`

這代表本次不是全面重做所有 mapper，而是集中修正和核心新模型直接相關的部分。

---

## 初步分析整理

這是最早看單檔 diff 時整理出的關鍵判讀，後面詳細章節會展開：

1. `new` 把 `$2007` 從「軟體推測 state machine」改成更接近硬體匯流排模型。
2. `PPU` 讀取路徑被重寫成真正走 `address bus + octal latch`。
3. PPU 內部寄存器改用 `v / t / read buffer` 語意。
4. `$2000 / $2007` 等寄存器寫入加入更細的 master clock 等待。
5. CPU / PPU / APU 事件觸發相位整體改成較明確的 phase 模型。
6. `FDS` 從「可載入資料」升級成「有 byte transfer 時鐘與 IRQ」的裝置。
7. base mapper 移除 PRGRAM 預設讀寫責任，改由各 mapper 明確決定。

---

## 核心架構差異

## 1. Base `Mapper` 的責任被重新劃分

### `old`

在 `old-TriCNES-main/Emulator.cs` 的 `Mapper` 類別中：

- `FetchPRG()` 內建 `$6000-$7FFF` 的 PRGRAM 讀取
- `StorePRG()` 內建 `$6000-$7FFF` 的 PRGRAM 寫入

也就是說，mapper 即使不特別處理，仍會自動獲得一套「通用 PRGRAM 行為」。

### `new`

在 `new-TriCNES-main/Emulator.cs`：

- base `FetchPRG()` 拿掉了通用 PRGRAM 路徑
- base `StorePRG()` 變成空函式
- 新增 `virtual byte FetchPPU()`
- 新增 `virtual void FDS_ByteTransferFlag()`

### 設計意義

這個改動非常重要，因為它把責任從核心移回 mapper：

- 某個 mapper 有沒有 PRGRAM
- 哪些位址可讀可寫
- 是否有保護位元
- 是否需要特殊映射

都不再依賴 core 的預設行為。

這樣的設計比較正確，因為不同 mapper 的 PRG RAM 行為並不一致，尤其像 MMC3 / MMC6 / FDS 都有各自限制。

---

## 2. `DiskDrive` 從資料容器變成有時序的裝置

### `old`

`DiskDrive` 幾乎只有：

- `Disk`
- `ShiftRegister`
- `IRQ`
- `InsertDisk()`

本質上更像一個資料結構。

### `new`

新增：

- `Cart`
- `clock`
- `Status_ByteTransferFlag`
- `Clock()`

其行為是：

- 每 `1792` 個 master clocks 觸發一次 byte transfer ready
- 設定 `Status_ByteTransferFlag = true`
- 呼叫 `Cart.MapperChip.FDS_ByteTransferFlag()`

### 設計意義

`new` 不是只知道「有 FDS 檔案」，而是開始模擬 FDS 驅動器的傳輸節奏與 IRQ 觸發點。

這使得：

- FDS IRQ 時序可進入核心事件流
- mapper 可以依照控制寄存器決定是否 raise IRQ
- savestate 需要保存 FDS clock 與 shift register

---

## 3. CPU / PPU / APU 主時鐘模型改寫

### `old`

`_EmulatorCore()` 採用倒數式模型：

- `CPUClock == 0` 執行 CPU
- `CPUClock == 5` 處理 IRQ level / `CPUClockRise()`
- `PPUClock == 0` 執行 PPU
- 最後 `PPUClock--`, `CPUClock--`

### `new`

改成遞增相位模型：

- `CPUClock == 12` 執行 CPU
- `CPUClock == 7` 處理 M2 相位 / `CPUClockRise()`
- `PPUClock == 4` 執行 PPU
- `PPUClock == 2` 執行 half PPU
- `CPUClock == 0` 執行 APU
- 最後 `PPUClock++`, `CPUClock++`

另外新增：

- `EmulateNMasterClockCycles(int n)`

### 設計意義

兩種寫法理論上可以等價，但 `new` 的好處是更容易把「某個事件發生在某個 master clock phase」寫清楚。

這對下列功能尤其重要：

- `$2007` 讀寫
- `$2000/$2001` 寄存器寫入副作用
- MMC3 的 M2/A12 互動
- FDS byte transfer 計時

---

## PPU 設計差異

## 4. PPU 內部寄存器從描述性名稱改成 `v/t` 模型

### `old`

主要名稱：

- `PPU_ReadWriteAddress`
- `PPU_TempVRAMAddress`
- `PPU_VRAMAddressBuffer`

### `new`

改為：

- `PPU_v`
- `PPU_t`
- `PPU_ReadBuffer`

### 設計意義

這不只是重命名。

`new` 的大量邏輯開始直接依附 NES PPU 慣例中的：

- `v`: current VRAM address
- `t`: temporary VRAM address
- read buffer: `$2007` 延遲讀出緩衝

好處是：

- 對照 nesdev 文件更直接
- scroll copy / increment 邏輯更好理解
- 之後做更細的時序修正時比較容易

---

## 5. `$2007` 從高階例外狀態機改成 bus / latch 模型

### `old`

`old` 有一整套高階狀態欄位：

- `PPU_Data_StateMachine`
- `PPU_Data_StateMachine_Read`
- `PPU_Data_StateMachine_Read_Delayed`
- `PPU_Data_StateMachine_PerformMysteryWrite`
- `PPU_Data_StateMachine_UpdateVRAMAddressEarly`
- `PPU_Data_StateMachine_UpdateVRAMBufferLate`
- `PPU_Data_StateMachine_NormalWriteBehavior`
- `PPU_Data_StateMachine_InterruptedReadToWrite`

這套模型的特色是：

- 作者已經知道真機上有很多 back-to-back `$2007` 邊界案例
- 於是直接針對觀察到的現象做規則化描述
- 包含 `mystery write`、RMW 中斷、alignment 特例

這種方式可以很實用，但本質上是「效果導向」。

### `new`

這整塊幾乎被替換成較接近硬體內部訊號的欄位：

- `PPU_2007_Read`
- `PPU_2007_Read_SR`
- `PPU_2007_Read_Latches[]`
- `PPU_2007_PD_RB`
- `PPU_2007_ReadALE`
- `PPU_2007_Read_H0_Latch`
- `PPU_2007_Read_XRB`
- `PPU_READ`
- `PPU_2007_Write`
- `PPU_2007_Write_SR`
- `PPU_2007_Write_Latches[]`
- `PPU_2007_DB_PAR`
- `PPU_2007_WriteALE`
- `PPU_2007_TStep_Latch`
- `PPU_2007_TStep`
- `PPU_2007_BLNK_Latch`
- `PPU_2007_PaletteRAMEnable`
- `PPU_2007_WriteData`
- `PPU_WRITE`

另外把流程拆成：

- `PPU_DATA_StateMachine()`
- `PPU_DATA_StateMachine2()`
- `PPU_DATA_StateMachine_Half()`

### 設計意義

這代表作者不再只想描述「這個 timing 會看到什麼怪現象」，而是想描述：

- 什麼時候 SR latch 被拉起
- 什麼時候 ALE 有效
- 什麼時候 address bus 的低 8 位來自 octal latch
- 什麼時候資料從 PPU memory path 回到 read buffer
- 什麼時候 `v` 真的遞增

也就是把 `$2007` 問題從「高階補丁」轉成「底層資料路徑模型」。

這是整份 diff 裡最關鍵的設計轉向。

---

## 6. `FetchPPU()` 改成真正走 PPU bus

### `old`

核心自己提供：

- `FetchPPU(ushort Address)`

呼叫端通常先算好邏輯位址，再直接讀。

### `new`

base mapper 新增：

- `virtual byte FetchPPU()`

其基本流程是：

1. 使用 `PPU_AddressBus` 的高位
2. 使用 `PPU_OctalLatch` 補上低位
3. 如果是 pattern table，走 `CHRROM/CHRRAM`
4. 如果是 nametable，先走 `MirrorNametable()`
5. 最後把讀回的資料寫回 `PPU_AddressBus` 低位

### 設計意義

這是從「給位址，回傳資料」轉為「模擬 bus currently driven by which source」。

差異很大：

- `old` 比較像 memory helper
- `new` 比較像硬體 bus transaction

這也讓 mapper 可以介入 PPU fetch 本身，而不是只能介入 CHR bank 計算。

---

## 7. 新增 `PPU_OctalLatch` 與 Pattern Address Register 模型

### `new` 新增欄位

- `PPU_OctalLatch`
- `PPU_PatternAddressRegister_CHR`
- `PPU_PatternAddressRegister_NT`
- `PPU_PatternAddressRegister_AT`
- `PPU_PAR_MUX`

### 設計意義

這表示新版在背景與 sprite fetch 時，不再只是：

- 計算一個 address
- 直接讀

而是開始拆成：

- 目前 PAR 要輸出哪一種地址
- address 高位何時放上 bus
- 低位何時由 octal latch 補上
- mapper 何時真正看見該 bus 值

這對需要觀察 PPU A12 / mapper IRQ 的晶片特別重要，尤其 MMC3。

---

## 8. `$2000`、`$2001`、`$2007` 的 CPU 寫入 timing 更細

### `$2000`

`new` 在寫 `$2000` 時：

- 先使用目前 `dataBus` 造成早期效果
- 再 `EmulateNMasterClockCycles(2)`
- 然後再把最終值寫入各控制位元

這代表作者在模擬 CPU 寫 cycle 中：

- PPU 先看到什麼
- CPU data bus 什麼時候穩定

### `$2001`

`new` 對 `$2001` 留下大量註解，表示目前仍有暫時性做法，但已經開始用更細的 master clock 推進取代舊式 delay 欄位。

### `$2007`

`new` 對 `$2007` 寫入流程是：

1. `PPUBus = In`
2. 保存 `PPU_2007_WriteData`
3. `EmulateNMasterClockCycles(7)`
4. 再拉起 `PPU_2007_Write` / `PPU_2007_Write_SR`

### 對比 `old`

舊版主要是：

- 記下 `PPU_Data_StateMachine_InputValue`
- 視目前 state 決定是否進入 mystery write / interrupted read-write case
- 重設或啟動 state machine

### 設計意義

`old` 比較像在管理「例外狀態」。
`new` 比較像在管理「訊號何時翻轉」。

---

## 9. `$2007` 讀取流程也改成更硬體化

### `old`

`$2007` 讀取時：

- 根據 `PPU_Data_StateMachine` 與 `PPUClock` 判斷是否發生 edge case
- 視情況回傳 `PPU_VRAMAddressBuffer`
- 或直接回傳 `PPU_ReadWriteAddress` 低位
- 再設定各種 delayed / early flags

### `new`

`$2007` 讀取時：

- 若目前是 palette RAM，直接返回 palette 資料與 open bus 組合值
- 否則回傳 `PPU_ReadBuffer`
- `EmulateUntilEndOfRead()`
- 再拉起 `PPU_2007_Read_SR` 與 `PPU_2007_Read`

後續由 `PPU_DATA_StateMachine()` 與 half-step 處理：

- ALE
- buffer refill
- `v` increment
- palette RAM enable

### 設計意義

新版把 CPU visible read result 與 PPU internal follow-up actions 拆得更清楚。

---

## 10. `PPU_VSET` 註解修正

`PPU_VSET` 註解從：

- scanline `240`

改成：

- scanline `241`

這看起來只是註解，但實際上表示作者在新版更仔細校正 vblank 邊界的語義。

---

## 11. 舊版 `MMC3_M2Filter` 從核心移除

### `old`

核心本體有：

- `MMC3_M2Filter`

debug log 也會顯示這個值。

### `new`

這個概念被收回到 `Mapper_MMC3` 內部：

- 由 MMC3 自己保存 `Mapper_4_M2Filter`

### 設計意義

這代表作者把 mapper-specific timing state 從 core 拆回 mapper。

這在設計上更乾淨，因為：

- core 不需要知道 MMC3 私有實作細節
- savestate 也應由 mapper 自己負責保存其內部狀態

---

## 12. SaveState / LoadState 已切換到新硬體模型

### `old`

savestate 會保存：

- `PPU_ReadWriteAddress`
- `PPU_TempVRAMAddress`
- `PPU_VRAMAddressBuffer`
- `PPU_Data_StateMachine*`
- `MMC3_M2Filter`

### `new`

savestate 改保存：

- `PPU_v`
- `PPU_t`
- `PPU_ReadBuffer`
- `PPU_OctalLatch`
- `PPU_PatternAddressRegister_CHR`
- `PPU_2007_Read*`
- `PPU_2007_Write*`
- `PPU_READ`
- `PPU_WRITE`

另把部分舊 delay 欄位保留成註記為 `TEMPORARY` 的過渡狀態。

### 設計意義

新版 state 存的是新的電路/匯流排模型，因此：

- 兩版 state 格式不可視為相容
- 新版 debug / replay 也會更依賴內部 latch 狀態

---

## mapper 差異

## 13. `Mapper_FDS` 是功能性升級最多的 mapper

### `old`

主要功能只有：

- FDS BIOS 讀取
- PRGRAM 讀取
- `FetchCHR()` 走 `CHRRAM`
- 少量 `$403x` stub

缺少：

- `$4025` 控制寄存器
- byte transfer flag IRQ
- `$4031` disk data input 實作
- FDS clock / shift register savestate

### `new`

新增：

- `FDS_4025_Control`
- `$4025` 寫入處理
- `FDS_ByteTransferFlag()`
- `$4031` 讀回 `Cart.FDS.ShiftRegister`
- 讀 `$4031` 後清 `IRQ_LevelDetector`
- savestate 保存：
  - `FDS_4025_Control`
  - `Cart.FDS.clock`
  - `Cart.FDS.ShiftRegister`

### 設計意義

新版 FDS 已經不是「能載入 BIOS 和 RAM」而已，而是開始真正實作：

- 磁碟位元組傳輸節拍
- IRQ enable / acknowledge
- 裝置狀態可序列化

這是明顯的硬體化升級。

---

## 14. `Mapper_MMC3` 的差異是 PPU 路徑被正式收回 mapper

### 新增 `FetchPPU()`

這是 `Mapper_MMC3` 最重要的改動。

新版 `Mapper_MMC3.FetchPPU()` 會：

- 使用 `PPU_AddressBus + PPU_OctalLatch`
- 區分 CHR / CIRAM
- 在 `AlternativeNametableArrangement` 時，位址可導向：
  - `Cart.PRGVRAM`
  - 或 `Cart.Emu.VRAM`

### 為什麼這很重要

在 `old` 中，雖然 `MirrorNametable()` 已經知道有 alternative arrangement，但實際 PPU memory read 還是由 core 的 `FetchPPU(address)` 完成。

這會造成設計上有一個限制：

- mapper 能決定 mirror，但未必能完整掌控 PPU fetch 的實際 bus 回應

在 `new` 中，MMC3 自己接手 `FetchPPU()`，因此：

- mapper 決定的不只是「位址該怎麼折疊」
- 而是「當下 PPU bus 讀回什麼資料」

這對 MMC3 類型板子的 nametable RAM / VRAM / 額外記憶體路由比較合理。

### 其他差異

新版 `SaveMapperRegisters()` / `LoadMapperRegisters()` 對 `Cart.PRGVRAM` 做 `null` 檢查。

這是穩定性修正，避免在沒有 PRGVRAM 的 MMC3 變體上直接崩潰。

---

## 15. `Mapper_MMC2` / `AOROM` / `CNROM` / `UxROM` 的改動很小，但語意上重要

這四個 mapper 的差異幾乎一致：

- 移除了 `else { base.StorePRG(Address, Input); }`

### 代表什麼

因為新版 base mapper 已經不再提供預設 PRGRAM 寫入行為，所以這些 mapper 不再「順便」繼承一份通用 `$6000-$7FFF` RAM 寫入。

這是正確的架構方向：

- 有些 mapper 沒有 PRGRAM
- 有些雖然有，但保護方式不同
- 有些地址根本不該寫

讓 mapper 顯式決定比依賴 core 預設更安全。

---

## 16. 沒有變動的 mapper 同樣提供訊號

雖然 `Mapper_FME7`、`Mapper_MMC1` 等檔案沒有 diff，但在新架構下它們仍然受益於：

- base `Mapper.FetchPPU()`
- core 的 `PPU_OctalLatch` / PAR / bus 模型
- PRGRAM 責任重新定義

也就是說，即使 mapper 檔案本身沒改，執行語境已經改了。

---

## 設計哲學上的總結

## 17. `old` 的核心哲學

`old` 偏向：

- 先把可觀察行為做對
- 遇到 timing bug 或 test case，再補 edge-case 規則
- 容忍較高階的狀態抽象

優點：

- 開發速度快
- 容易直接對應特定 bug
- 對已知測試案例很實用

缺點：

- 邊界案例越多，狀態機越難維護
- 某些現象是「因果反推」不是「硬體推導」
- mapper 與 PPU/匯流排責任邊界容易混雜

## 18. `new` 的核心哲學

`new` 偏向：

- 建立 bus / latch / phase / address path
- 讓可觀察現象從模型自然浮現
- 將 mapper-specific 狀態與責任收回 mapper
- 將 FDS 等裝置納入時鐘事件流

優點：

- 更接近真機
- 對未知 edge case 的延展性更好
- 有利於解釋「為什麼」某 bug 會發生

缺點：

- 實作更複雜
- 除錯門檻更高
- 若底層模型某個 phase 有誤，影響面會很大

---

## 最終結論

本次 `old -> new` 的核心演進可以濃縮成三句話：

1. `PPU/$2007` 從高階特殊規則，轉向匯流排與鎖存器模型。
2. mapper 從「被 core 幫忙處理記憶體」轉成「自己定義自己的實際硬體行為」。
3. `FDS` 從資料載入支援，升級為具備時序與 IRQ 的裝置。

如果要判斷哪一版設計比較成熟，從架構角度看：

- `old` 比較像功能導向、逐步補丁式精化
- `new` 比較像往硬體準確度與系統邊界清晰化發展

這不是小改版，而是整個模擬核心思路的轉向。

---

## 建議後續可再補的章節

如果後續要繼續擴充這份文件，最值得再深入的有三塊：

1. 把 `$2007` 讀寫流程做成逐拍對照表
2. 把 `old/new` 的 scroll / `v,t,x` 更新規則做成對照
3. 針對 `MMC3 IRQ`、`PPU A12`、`M2 filter` 做專章分析

---

## 追加細部分析

以下章節是在初版整理後，進一步對照實際程式片段補上的更細流程說明。

## 19. `$2007` 讀取流程的實作哲學差異

### `old` 的讀取路徑

`old` 在 CPU 讀 `$2007` 時，核心先檢查目前是否卡在舊的 `PPU_Data_StateMachine` 中。

如果是 back-to-back read：

- 會依 `PPUClock` 分四種 alignment
- 有些情況回傳 `PPU_VRAMAddressBuffer`
- 有些情況直接回傳 `PPU_ReadWriteAddress` 低位
- 有些情況同時設 `PPU_Data_StateMachine_UpdateVRAMAddressEarly = true`

如果不是 back-to-back read：

- palette 區直接讀 palette
- 非 palette 區讀 `PPU_VRAMAddressBuffer`

然後：

- 若 state machine 尚未啟動，就把 `PPU_Data_StateMachine` 設為 `0`
- 依 phase 決定 `PPU_Data_StateMachine_UpdateVRAMBufferLate`
- 可能因 DMC DMA 多增一次 `PPU_ReadWriteAddress`
- 最後標記這次是 read

### `new` 的讀取路徑

`new` 在 CPU 讀 `$2007` 時，邏輯被明顯切成兩段：

1. CPU 當下要看見什麼資料
2. PPU 內部在這次讀取完成後要怎麼推進

CPU visible result：

- 若 `PPU_AddressBus` 指向 palette RAM，直接回傳 palette 值與 `PPUBus` 高位組合
- 否則回傳 `PPU_ReadBuffer`

接著：

- 更新 `PPUBus`
- `EmulateUntilEndOfRead()`
- 在讀 cycle 結束後才拉起 `PPU_2007_Read_SR`
- 再將 `PPU_2007_Read = true`

後續的內部副作用不是在 `Fetch()` 函式直接硬編碼，而是交給：

- `PPU_DATA_StateMachine()`
- `PPU_DATA_StateMachine2()`
- `PPU_DATA_StateMachine_Half()`

去處理：

- ALE
- read buffer refill
- `v` increment
- palette RAM enable
- read latch 釋放

### 差異本質

`old` 比較像：

- 在 CPU read handler 內直接推導各種可見特例

`new` 比較像：

- CPU read handler 只決定本 cycle 對 CPU 可見的值
- 其餘由 PPU 內部訊號在後續半拍自然完成

這是從「在 API 層補現象」轉向「在底層訊號層重現現象」。

---

## 20. `$2007` 寫入流程的差異

### `old`

`old` 寫 `$2007` 時核心立刻做這些事：

- 保存 `PPU_Data_StateMachine_InputValue`
- 檢查目前是否剛好打斷前一個 `$2007` 狀態
- 若打斷的是某些 phase，計算 `PPU_VRAM_MysteryAddress`
- 設 `PPU_Data_StateMachine_PerformMysteryWrite`
- 或 `PPU_Data_StateMachine_InterruptedReadToWrite`
- 否則標記 `PPU_Data_StateMachine_NormalWriteBehavior`
- 最後把整個 state machine 重設或切到新狀態

這代表「寫入的效果」主要由一組高階 state 旗標主導。

### `new`

`new` 對 `$2007` 寫入的流程明顯更接近硬體事件：

1. `PPUBus = In`
2. 保存 `PPU_2007_WriteData`
3. 等待 `EmulateNMasterClockCycles(7)`
4. 然後才把：
   - `PPU_2007_Write = true`
   - `PPU_2007_Write_SR = true`

接著寫入是否真正落地，不是在 CPU write handler 直接完成，而是後續由：

- `PPU_2007_Write_Latches`
- `PPU_2007_DB_PAR`
- `PPU_WRITE`

控制，最後在 `PPU_DATA_StateMachine_Half()` 中呼叫 `StorePPUData(PPU_AddressBus, PPU_2007_WriteData)`。

### 差異本質

舊版把問題描述成：

- 這次 write 屬於哪種 case
- 後面應該發生哪種例外

新版把問題描述成：

- SR latch 何時拉起
- write latch 經過哪些翻轉
- 何時資料真的從 bus 寫入記憶體

新版的模型更適合處理複合 timing 問題，因為資料落地時間被顯式建模了。

---

## 21. `$2000` 寫入：從延遲修補改成分段推進

### `old`

`old` 寫 `$2000` 的思路是：

- 當下先讓部分位元暫時使用 `dataBus`
- 再根據 `PPUClock & 3` 設定 `PPU_Update2000Delay`
- 下幾個 PPU cycle 之後再套用 `PPU_Update2000Value`

這種做法本質上仍是「延遲應用最終結果」。

### `new`

`new` 保留了「早期看到舊 databus 造成 scanline bug」這個硬體事實，但實作方式改成：

1. 先立即把 `PPU_t` 的 nametable bits 用目前 `dataBus` 更新
2. 同時更新 `PPU_EXT_Enable`
3. `EmulateNMasterClockCycles(2)`
4. 再把最終輸入值 `In` 寫入：
   - `PPUControl_NMIEnabled`
   - increment mode
   - sprite size
   - pattern selects
   - `PPU_t` 的 nametable bits
   - `PPU_EXT_Enable`

### 差異本質

兩版都想保留 SMB1 那類 scanline bug 的根源，但：

- `old` 用延遲欄位近似「過一會才修正」
- `new` 直接模擬「先看見錯的 bus 值，再過幾個 master clocks 看見對的值」

新版的因果鏈更明確。

---

## 22. `$2005/$2006` 寫入：差異較小，但新版本語意更清楚

### 相同點

這兩版在 `$2005/$2006` 的核心演算法其實非常接近：

- 都保留 alignment-dependent delay
- 都承認在 delay 期間 PPU 會暫時用 databus 值
- 都把第二次 `$2006` 寫入延後一段時間才真正反映到目前 VRAM address

### 不同點

不同主要在於：

- `old` 用 `PPU_TempVRAMAddress / PPU_ReadWriteAddress`
- `new` 用 `PPU_t / PPU_v`

另外新版把這些更新放進更完整的 `v/t` 語意系統裡，使 scroll copy 與後續 `$2007` state machine 更一致。

### 結論

`$2005/$2006` 不是這次最根本的演進點，但它們在 `new` 中被納入同一套 `v/t/bus` 模型，所以整體一致性變高。

---

## 23. Scroll / `v,t` 路徑：演算法沒大改，但模型位置改了

### `PPU_IncrementScrollX()`

兩版幾乎相同：

- coarse X 到 31 時回捲並翻 nametable bit
- 否則直接加 1

差異只是變數名稱：

- `old`: `PPU_ReadWriteAddress`
- `new`: `PPU_v`

### `PPU_IncrementScrollY()`

兩版邏輯也基本相同：

- 若 `CopyV` 為真，使用 `PPU_Update2006Value_Temp & PPU_Update2006Value`
- 否則依 fine Y / coarse Y / nametable bit 執行標準 PPU Y increment

### `PPU_ResetXScroll()` / `PPU_ResetYScroll()`

兩版演算法一樣，差異仍集中在語意：

- `old` 是把 `PPU_TempVRAMAddress` 複製回 `PPU_ReadWriteAddress`
- `new` 是把 `PPU_t` 複製回 `PPU_v`

### 真正的設計差異

scroll 演算法本身沒有被大幅重寫，但在 `new` 裡：

- 它不再像單獨的一組「地址更新 helper」
- 而是更明確地成為 PPU 內部 `v/t` 狀態機的一部分

這個差異不在演算法公式，而在它在整個模型中的角色。

---

## 24. `PPU_DATA_StateMachine` 的結構反映了新模型的分層

### `new` 的三段拆分

新版把 `$2007` 相關內部流程拆成：

- `PPU_DATA_StateMachine()`
- `PPU_DATA_StateMachine2()`
- `PPU_DATA_StateMachine_Half()`

可以粗略理解為：

- 第一段：建立本輪 control signal
- 第二段：在特定點做 read buffer 更新
- 第三段：半拍後處理 `v` increment、latch 反相、真正 write 落地

### 為什麼值得注意

這種分段反映作者已經不把 PPU cycle 視為單一不可分割事件，而是開始區分：

- 一個 PPU cycle 上半拍
- 一個 PPU cycle 下半拍
- 某些訊號在何時對內部寄存器生效

這正是 `new` 相比 `old` 最重要的硬體化特徵之一。

---

## 25. `MMC3 IRQ`：演算法本體沒改，但掛接點更合理

### `Mapper_MMC3.PPUClock()`

對照新版與舊版 mapper 內容，`MMC3 IRQ` 的核心演算法基本沒變：

- 偵測 `PPU_A12` 由低轉高
- 要求 `Mapper_4_M2Filter == 3`
- 若 `ReloadIRQCounter` 為真就重載
- 否則遞減 counter
- 在適當時機拉起 `IRQ_LevelDetector`

也就是說，MMC3 的 IRQ 規則並沒有重寫。

### 真正的差異在掛接上下文

因為 `new` 的 PPU fetch 已改成：

- 更依賴 `PPU_AddressBus`
- 更依賴 `PPU_OctalLatch`
- mapper 自己可以參與 `FetchPPU()`

所以 MMC3 看到的 `A12` 變化，會更接近「真正的 PPU bus 活動」。

### 設計意義

舊版雖然也能跑 MMC3 IRQ，但 core 與 mapper 的責任邊界比較模糊。
新版則讓：

- bus activity 由 core 產生
- mapper 觀察 bus 並自行決定 IRQ

這個結構更清楚，也更適合後續修正 MMC3 的細節問題。

---

## 26. `MMC3` 的 `FetchPPU()` 讓 alternative nametable arrangement 更完整

舊版 `MMC3` 雖然有 `MirrorNametable()`，也知道 `AlternativeNametableArrangement`，但真正讀 nametable 時還是由 core 的 `FetchPPU(address)` 決定資料來源。

新版 `MMC3.FetchPPU()` 則把這段完整收回 mapper，特別是：

- 若 `Address >= 0x2000`
- 經 `MirrorNametable()` 後
- 若 `Cart.AlternativeNametableArrangement` 啟用
- 且 bit 11 為 1
- 則讀 `Cart.PRGVRAM`
- 否則讀 `Cart.Emu.VRAM`

### 為什麼這比單純 `MirrorNametable()` 更重要

因為 mirror 只決定位址變形，不決定最終 memory source。
新版是連「資料來自哪一塊 RAM」都交給 mapper 決定。

這讓 MMC3 不再只是「重新映射位址」，而是「真正接管 PPU memory path 的一段」。

---

## 27. `FDS`：從 stub 狀態進入可互動裝置

### 舊版 `Mapper_FDS`

舊版的 FDS mapper 狀態相當初步：

- BIOS 可讀
- PRGRAM 可讀
- `$4033` 固定回 `0x80`
- `$4031` 沒有真正行為
- 沒有 `$4025`
- 沒有 byte transfer IRQ
- 沒有 FDS 狀態進 savestate

### 新版 `Mapper_FDS`

新增了完整得多的互動面：

- `FDS_4025_Control`
- `StorePRG()` 解析 `$4025`
- `FDS_ByteTransferFlag()`
- 若 `FDS_4025_Control & 0x80 != 0` 則 raise IRQ
- 讀 `$4031` 會回傳 `Cart.FDS.ShiftRegister`
- 讀 `$4031` 同時清 `Cart.Emu.IRQ_LevelDetector`
- savestate 保存：
  - `FDS_4025_Control`
  - `Cart.FDS.clock`
  - `Cart.FDS.ShiftRegister`

### 設計意義

舊版比較像「我知道 FDS 有 BIOS 和 RAM」。
新版開始接近「我知道 FDS 是個會定時送出 byte、可觸發 IRQ、可被 CPU acknowledge 的裝置」。

這是功能完成度的大幅提升。

---

## 28. 為什麼 `AOROM/CNROM/UxROM/MMC2` 的小改動其實很有價值

表面上這幾個 mapper 只是刪掉對 `base.StorePRG()` 的呼叫，看起來像很小的清理。

但這代表了架構層次的明確化：

- core 不再偷偷幫 mapper 處理 `$6000-$7FFF`
- mapper 若要支援 PRGRAM，必須自己聲明
- mapper 若不該支援，預設就不會被誤寫

這能減少一種很常見的模擬器設計問題：

- 某 mapper 因為繼承預設 RAM 行為而「意外地太寬鬆」

換句話說，這些小 diff 是新架構責任邊界落地的證據。

---

## 29. 目前最值得優先再深入的技術點

如果要把這份文件繼續擴成更完整的設計比較，優先順序建議如下：

1. `$2007` 的 read/write 事件序列畫成 timeline
2. `PPU_DATA_StateMachine()` 三段拆分與 PPU cycle 上下半拍關係
3. `PPU_OctalLatch` / `PAR` / `PPU_AddressBus` 在背景 fetch 與 sprite fetch 的交互
4. `MMC3 A12` 何時被新版 bus 模型驅動得更接近真機
5. `FDS` 的 `ShiftRegister` 與 byte transfer flag 還有哪些未完成區塊

---

## 30. 補充總結

在加入更細的流程比對後，可以更清楚看出：

- `old` 的強項是對已知怪異行為做明確補丁
- `new` 的強項是把怪異行為的形成機制往底層訊號模型搬

所以 `new` 的價值不只是「更複雜」，而是：

- 更容易延伸到未知 bug
- 更容易把 mapper 與 PPU 的責任分清
- 更容易讓後續修正建立在硬體因果關係上

這點在 `$2007`、`MMC3`、`FDS` 三塊都非常明顯。

---

## 逐拍與事件序列對照

這一節把 `old/new` 在 `$2007` 與 PPU bus 上的事件順序攤平成較接近 timeline 的形式。

## 31. `old` 的 `$2007` 內部事件序列

雖然 `old` 不是用實體鎖存器模型表示，但實際上仍然在用一個「以 PPU cycle 為節點」的微型狀態機。

可以把它粗略理解成以下序列。

### `old`：read/write 啟動階段

當 CPU 對 `$2007` 發生存取時：

- CPU handler 立即決定這次是 read 還是 write
- 若是 read：
  - 直接在 CPU handler 判斷本次 visible value
  - 依 `PPUClock` 決定是否發生特殊 alignment case
  - 設定 `PPU_Data_StateMachine_Read`
  - 視情況設 `UpdateVRAMAddressEarly`
  - 視情況設 `UpdateVRAMBufferLate`
- 若是 write：
  - 保存 `PPU_Data_StateMachine_InputValue`
  - 視目前 state 判定是不是打斷前一個 `$2007`
  - 設定 `PerformMysteryWrite` / `InterruptedReadToWrite` / `NormalWriteBehavior`

### `old`：PPU cycle 1

若 state machine 進到 `1`：

- 對 read 而言，可能在這個 timing 補做 buffer refill
- 但如果 `UpdateVRAMBufferLate` 為真，這一步會延後

### `old`：PPU cycle 3

這是舊模型裡最關鍵的一拍：

- 正常 write 會在這裡發生
- 若遇到特定 edge case，這裡可能改成 mystery write
- 這也是 write 與 read-to-write interruption 最容易糾纏的點

### `old`：PPU cycle 4

在 `old` 裡，很多事情都被塞到 cycle 4：

- late buffer refill
- early increment 導致的新位址讀取
- 正常的 `v` increment
- rendering enabled 時，甚至改為同時 `IncrementScrollX/Y`
- mystery write 的後續補寫也可能落在這一拍

### `old`：PPU cycle 8

- 處理 `InterruptedReadToWrite`
- 補最後一次 write
- 再次 increment `PPU_ReadWriteAddress`

### 對 `old` 的解讀

`old` 的 timeline 雖然細，但其實是「把多種觀察到的結果塞進幾個離散節點」：

- cycle 1：某些 read 後續
- cycle 3：主要 write 點
- cycle 4：大量例外與 increment
- cycle 8：最後補尾

這種結構在工程上是可行的，但它的節點是「為了解釋結果而定義」，不是從 bus 相位自然長出來的。

---

## 32. `new` 的 `$2007` 事件序列

新版不再用單一整數狀態值，而是把事件分散到：

- CPU read/write handler
- `PPU_DATA_StateMachine()`
- `PPU_DATA_StateMachine2()`
- `PPU_DATA_StateMachine_Half()`

### `new`：CPU read 啟動

當 CPU 讀 `$2007`：

- 若 palette 命中，CPU 直接拿到 palette 可見值
- 否則 CPU 拿到 `PPU_ReadBuffer`
- 然後 `EmulateUntilEndOfRead()`
- 在 read 結束時刻拉高：
  - `PPU_2007_Read_SR`
  - `PPU_2007_Read`

### `new`：CPU write 啟動

當 CPU 寫 `$2007`：

- 先記下 `PPU_2007_WriteData`
- 等待 `7` 個 master clocks
- 再拉高：
  - `PPU_2007_Write`
  - `PPU_2007_Write_SR`

### `new`：`PPU_DATA_StateMachine()` 第一段

第一段主要做「控制訊號建立」：

- 根據 blanking 狀態計算 `PPU_2007_BLNK_Latch`
- 判斷 palette RAM enable
- 將 SR latch 送進 read/write latches 第 0 級
- 反相傳遞出第 2 / 第 4 級 latch
- 推導：
  - `PPU_2007_PD_RB`
  - `PPU_2007_ReadALE`
  - `PPU_2007_WriteALE`
  - `PPU_READ`
  - `PPU_ALE`

若在 ALE 點上，而且當下不是 read-driving phase：

- `PPU_AddressBus = PPU_v`
- `PPU_OctalLatch = low(PPU_AddressBus)`

這一步是新版裡非常關鍵的「把位址放上 bus」。

### `new`：`PPU_DATA_StateMachine2()` 第二段

第二段主要負責 read buffer refill：

- 若 `PPU_2007_PD_RB` 為真
- 呼叫 `Cart.MapperChip.FetchPPU()`
- 結果寫入 `PPU_ReadBuffer`
- 若此刻 `PPU_ALE` 仍有效，更新 `PPU_OctalLatch`

也就是說，read buffer refill 不是直接在 CPU read handler 完成，而是在 PPU 內部讀資料路徑中完成。

### `new`：`PPU_DATA_StateMachine_Half()` 第三段

第三段主要做真正的 state 推進：

- `PPU_2007_TStep = PPU_2007_TStep_Latch || PPU_2007_PD_RB`
- 若 `TStep` 成立：
  - `PPU_v += increment`
  - 若非 blanking，再做 `PPU_IncrementScrollY()`
- 再次在 `PPU_2007_PD_RB` 條件下補做 read buffer refill
- 更新 read/write latches 的另一半反相信號
- 清除已完成的 SR latch
- 推導 `PPU_2007_DB_PAR`
- 若 `PPU_2007_DB_PAR` 成立，真正呼叫 `StorePPUData()`

### 對 `new` 的解讀

新版的關鍵不在「有沒有狀態」，而在「狀態被拆成訊號與半拍事件」：

- read 何時有效
- write 何時有效
- address 何時被 latched
- read buffer 何時被 refill
- `v` 何時真的往後走
- write 何時真的落地

這個模型雖然複雜，但更像是電路時序圖。

---

## 33. `old/new` 的 `$2007` timeline 對照表

以下是抽象化後的對照。

### `old`

1. CPU handler 先決定大部分可見行為與例外分支
2. 後面幾個 PPU cycle 依整數 state 值跑補丁式後續
3. 特殊情況直接用旗標表示：
   - early increment
   - late buffer refill
   - mystery write
   - interrupted read-to-write

### `new`

1. CPU handler 只負責「本次對 CPU 可見的結果」與拉起 SR
2. PPU state machine 第一段建立 control signal
3. 第二段走 bus read path / refill buffer
4. 半拍階段推進 `v` 與真正 write
5. 所有 edge case 都試圖從 signal timing 自然出現

### 結論

如果把兩版想成不同等級的模型：

- `old` 比較像「時序驅動的行為規則表」
- `new` 比較像「用 latch 與 bus 組裝出的微型電路模型」

---

## 34. 背景 fetch 的 8-cycle 模型是新版 bus 架構的另一個證據

新版背景 fetch 在每 8 dot 的節奏中，明確拆成：

- cycle 0：把 nametable address 放到 PAR / address bus
- cycle 1：用 `(high address | octal latch)` 讀 nametable byte
- cycle 2：放 attribute address
- cycle 3：讀 attribute byte
- cycle 4：準備 pattern low address
- cycle 5：讀 pattern low byte
- cycle 6：準備 pattern high address
- cycle 7：讀 pattern high byte

這裡每一個「真正的資料讀取」都走：

- `PPU_AddressBus = (PAR 高位 | PPU_OctalLatch)`
- `Cart.MapperChip.FetchPPU()`

### 為什麼這很重要

這顯示新版不是只把 `$2007` 做硬體化，而是把整個 PPU fetch path 都往同一個 bus 模型統一。

也就是說：

- 背景 fetch
- sprite fetch
- `$2007` access

正在共用同一種 address/bus/latch 思維。

這種一致性是新版結構成熟度的重要指標。

---

## 35. Sprite pattern fetch 同樣改成 bus-first 模型

在新版 sprite evaluation / sprite pattern fetch 中，也能看到相同模式：

- 先準備 `PPU_PatternAddressRegister_CHR`
- 再把 `PPU_AddressBus` 組成 `(PAR 高位 | PPU_OctalLatch)`
- 然後用 `Cart.MapperChip.FetchPPU()` 取回 bitplane

這意味著：

- sprite pattern 低位平面
- sprite pattern 高位平面

都不再只是「算位址直接讀 CHR」，而是明確走過 bus 模型。

### 設計意義

這對 MMC3 尤其重要，因為 MMC3 IRQ 依賴 A12 的真實切換時機。
當背景與 sprite fetch 都統一用 bus 模型後，mapper 更有機會看到正確的 A12 活動。

---

## 36. `PPU_DATA_StateMachine()` 被插入在 PPU rendering 流程中的位置也有意義

新版在 `_EmulatePPU()` 裡，`PPU_DATA_StateMachine()` 的位置在：

- 部分 mask delayed 更新之後
- sprite evaluation 之前
- rendering/mask handling 的某些步驟之前

這表示作者不是把 `$2007` 當成一個完全獨立的 side process，而是把它視為：

- 與當前 PPU cycle 的 bus 行為同步存在的子系統

也就是：

- 一個 PPU cycle 內既有 rendering fetch
- 也可能同時有 `$2007` 相關 bus 活動

這是更貼近真機的視角。

---

## 37. 為什麼新版比較有機會正確重現 SMB1 類 timing bug

新版在 `PPU_DATA_StateMachine_Half()` 裡有一個非常明確的註解：

- 如果把 `PPU_2007_TStep` 的處理放在 `PPU_DATA_StateMachine()` 內而不是 half-step，SMB1 title screen 會壞掉

這句話很有價值，因為它說明：

- 作者已經不是只在猜「某個現象大概會在哪個整數 PPU cycle」
- 而是發現「同一個 PPU cycle 的前半拍與後半拍」都可能改變結果

這正是從高階模擬走向精細時序模擬時常見的分水嶺。

---

## 38. 逐拍對照後的整體結論

加入 timeline 視角後，兩版差異可以進一步濃縮成：

### `old`

- 用少數幾個關鍵 PPU cycle 當作行為掛點
- 在這些掛點上塞入多種特殊規則
- 目標是復現已知 observable glitch

### `new`

- 將 CPU access、ALE、read buffer refill、`v` increment、write commit 分散到不同相位
- 透過 latch 與 bus 訊號傳遞把事件串起來
- 目標是讓 glitch 從模型自然長出

### 因此

新版不是只是「更複雜的舊版」，而是換了一種描述同一顆 PPU 的語言：

- `old` 的語言是狀態與特例
- `new` 的語言是訊號與相位

這也是為什麼新版雖然程式變難讀，但從架構上更值得往下發展。

---

## PPU Address Path / MMC3 專章

這一節專門整理新版引入的：

- `PPU_OctalLatch`
- `PPU_PatternAddressRegister_*`
- `PPU_PAR_MUX`
- `PPU_AddressBus`

如何共同形成一條比較接近真機的 address path，並讓 `MMC3 A12` 偵測更合理。

## 39. `PPU_OctalLatch` 在新版中的角色

新版有一個很關鍵的新欄位：

- `PPU_OctalLatch`

從程式行為來看，它不是單純的暫存變數，而是：

- 承接目前 `PPU_AddressBus` 低 8 位
- 在後續 fetch 階段與 address 高位重新組合
- 形成真正送進 `FetchPPU()` 的位址

### 典型使用方式

新版的很多讀取都遵循同一模式：

1. 先把某個 address source 放進 `PPU_AddressBus`
2. 某個 timing 點把低位鎖到 `PPU_OctalLatch`
3. 真正讀資料時用：
   - `(address 高位 | PPU_OctalLatch)`

也就是說：

- `PPU_AddressBus` 不再被視為永遠完整穩定的 14-bit 位址
- 而是像硬體一樣，在不同 phase 有不同來源與用途

### 設計意義

這個 latch 的存在，讓新版可以自然描述：

- 先放高位地址
- 再在另一拍決定低位
- 最後 mapper 實際看到的是哪一組 address bits

這是從「記憶體讀 helper」走向「address bus activity」的核心元件。

---

## 40. `PPU_PatternAddressRegister_*` 與 `PPU_PAR_MUX`

新版額外引入：

- `PPU_PatternAddressRegister_CHR`
- `PPU_PatternAddressRegister_NT`
- `PPU_PatternAddressRegister_AT`
- `PPU_PAR_MUX`

### 各自代表什麼

- `NT`：nametable fetch 要用的位址
- `AT`：attribute fetch 要用的位址
- `CHR`：pattern table fetch 要用的位址
- `PAR_MUX`：表示當前哪個 PAR source 正在驅動 address path

### 為什麼需要這些欄位

在舊版裡，多半是：

- 當下直接算出某一個位址
- 直接去讀記憶體

在新版裡則明顯變成：

- 先準備 address register
- 再經過 mux 選擇
- 再把選中的高位灌到 `PPU_AddressBus`
- 再配合 `PPU_OctalLatch` 形成實際 fetch address

### 設計意義

這表示新版不是把 nametable / attribute / pattern 視為三種獨立 helper，而是把它們視為：

- 同一條 address path 上的不同來源

這與真機上「某些 address bits 來自不同路徑，再被內部 mux 選中」的思維更接近。

---

## 41. 背景 fetch 如何使用 `PAR + OctalLatch`

新版背景 fetch 的 8-cycle 流程非常能說明這個模型。

### nametable fetch

在背景 fetch 的 cycle 0：

- `PPU_PatternAddressRegister_NT = 0x2000 + (PPU_v & 0x0FFF)`
- `PPU_PAR_MUX = PPU_PatternAddressRegister_NT`
- `PPU_AddressBus = PPU_PAR_MUX`

在 cycle 1：

- `PPU_AddressBus = (PPU_PatternAddressRegister_NT 高位 | PPU_OctalLatch)`
- `PPU_RenderTemp = Cart.MapperChip.FetchPPU()`

也就是：

- 先準備 address
- 再用 latch 補完低位
- 最後才真正 fetch

### attribute fetch

cycle 2 / 3 也用同樣模式：

- cycle 2：先準備 `PPU_PatternAddressRegister_AT`
- cycle 3：再用 `(AT 高位 | octal latch)` 取資料

### pattern fetch

cycle 4 / 5 / 6 / 7 也一樣：

- 先由 `PPU_CheckPAR()` 決定 `CHR` address
- 再讓 `PPU_AddressBus` 指向 `CHR` address
- 實際讀取時用 `(CHR 高位 | octal latch)`

### 這代表什麼

背景 fetch 的整個流程已經不是：

- 直接算位址後一次性讀資料

而是：

- address source 準備
- address bus 驅動
- low byte latch
- 真正記憶體讀取

這就是新版 PPU 模型最核心的風格。

---

## 42. Sprite fetch 也被納入同一條 address path

新版 sprite fetch 不是另一套獨立機制，而是共用同一個 `PAR + OctalLatch + AddressBus` 思維。

### 例子

在 sprite pattern low / high fetch 之前，程式會：

- 先用 `PPU_CheckPAR()` 依 sprite 模式、翻轉、8x8/8x16 狀態更新 `PPU_PatternAddressRegister_CHR`
- 再讓 `PPU_AddressBus` 指向 `PPU_PAR_MUX`
- 真正讀資料時用：
  - `(PPU_PatternAddressRegister_CHR 高位 | PPU_OctalLatch)`

### 意義

這表示：

- 背景 pattern fetch
- sprite pattern fetch
- `$2007` 造成的 PPU memory access

在新版中開始被統一到相似的 address path 邏輯下。

這種一致性會直接影響：

- A12 什麼時候跳變
- mapper 何時看到該跳變
- IRQ 何時被時脈條件接受

---

## 43. `PPU_CheckPAR()` 顯示新版已經開始建模「位址來源因上下文改變」

`PPU_CheckPAR()` 很值得單獨點出，因為它做的事不是單純數學計算，而是在描述：

- 背景 fetch 時，CHR address 該怎麼構成
- sprite fetch 時，CHR address 又該怎麼構成
- 8x16 sprite 時，又有不同 bit 排列

### 背景情況

若目前 dot 屬於背景 fetch：

- `PPU_PatternSelect_Background`
- `PPU_v` 的 fine Y bits

共同決定 `PPU_PatternAddressRegister_CHR`

### sprite 情況

若目前 dot 屬於 sprite fetch：

- `PPU_PatternSelect_Sprites`
- `OAM2` 裡的 pattern index
- `InRangeCheck`
- `flipy`
- 8x8 / 8x16 模式

共同決定 `PPU_PatternAddressRegister_CHR`

### 設計意義

這個函式表示新版不再把 pattern 位址視為「讀到 nametable byte 後順手算一下」，而是視為：

- 一個具有明確上下文來源的 address register

這與 `PAR` 的概念是完全一致的。

---

## 44. `PPU_AddressBus` 在新版中更像真實外部可觀察訊號

舊版也有 `PPU_AddressBus`，但它比較常扮演：

- 某段程式內暫時存一下現在要讀哪裡

新版則明顯把它當成：

- mapper 可以觀察的 bus
- `PPU_A12_Prev` 的來源
- `PPU_OctalLatch` 的來源
- debug log 的核心訊號

尤其新版在多個地方都會明確做：

- 若 `PPU_READ` 成立，`PPU_OctalLatch = low(PPU_AddressBus)`
- 或在 `PPU_ALE && !PPU_READ` 時 latch low byte

這表示作者已經把 address bus 當成「具有時序意義的狀態」，而不是普通變數。

---

## 45. `PPU_A12_Prev` 與 `MMC3` 的關係在新版更自然

新版核心保存：

- `PPU_A12_Prev`

它在 PPU cycle 開始時記錄目前 `PPU_AddressBus` 的 A12 狀態，後續交給 mapper 使用。

`Mapper_MMC3.PPUClock()` 中的判定是：

- `!PPU_A12_Prev`
- 且目前 `PPU_AddressBus` 的 A12 為 1
- 且 `Mapper_4_M2Filter == 3`

才會視為一次有效的 A12 低到高跳變。

### 為什麼新版更合理

因為新版裡：

- 背景 fetch 與 sprite fetch 都用更真實的 address path
- `PPU_AddressBus` 的變化更接近實際 fetch 時序
- `PPU_OctalLatch` 與 `PAR` 參與了位址形成

因此 `MMC3` 所觀察到的 A12，不再只是「核心臨時計算的某個地址 bit」，而比較像：

- 真正被送上 bus 的位址位元

這對 IRQ timing 的正確性是根本性的改善。

---

## 46. `Mapper_4_M2Filter` 與 `CPUClockRise()` 的互動

新版 `MMC3` 仍保留 `Mapper_4_M2Filter`，但這個狀態已經被完整收回 mapper。

其大意是：

- 每次 `CPUClockRise()`，若條件允許就增加 `Mapper_4_M2Filter`
- 若 `PPU_AddressBus` 的 A12 為高，則在 `PPUClock()` 中把 filter 清零

所以 MMC3 IRQ 不是只看 A12，還要同時看：

- A12 是否經過足夠時間維持低
- 這段期間 M2 是否累積到條件

### 新版比較好的地方

因為現在：

- M2 phase 由 core 的新時鐘模型更清楚地呼叫 `CPUClockRise()`
- A12 由更真實的 PPU bus activity 形成

所以 `A12 + M2 filter` 的結合條件比舊架構更有機會貼近真機。

---

## 47. 為什麼 `MMC3 IRQ` 本體沒改，但整體可信度提高

對照 mapper 程式碼可見：

- IRQ counter reload / decrement 規則本身沒有顯著改動

因此新版的進步不在 IRQ 演算法本體，而在於它依賴的輸入訊號品質變好了：

- `PPU_AddressBus`
- `PPU_A12_Prev`
- `CPUClockRise()` 的 phase
- `Mapper_4_M2Filter`

換句話說，`MMC3 IRQ` 的數學沒變，但它的觀測環境更接近硬體。

這是很多模擬器演進時常見的一種提升方式：

- 不改公式
- 改公式所依賴的時序輸入

---

## 48. 為什麼新版的 address path 也能解釋 palette / rendering off 類問題

新版在 `$2001` 的註解裡已經提到一個重要觀點：

- VRAM address mux 會在不同來源之間切換
- rendering 關閉時，某些地址輸入來源可能瞬間改變
- 這會導致 attribute path 與 `v` 的交錯，進而造成 palette corruption

這與 `PAR/MUX/AddressBus` 模型是同一條設計線。

### 也就是說

新版不只是在為 `MMC3` 服務，這套 address path 模型同時也在為：

- palette corruption
- rendering on/off 邊界
- `$2007` timing
- sprite/background fetch 交錯

提供同一個統一框架。

這種統一性比單獨修一個 bug 更有長期價值。

---

## 49. `PPU_OctalLatch + PAR + AddressBus` 專章結論

這一組新增結構的價值，可以濃縮成三點：

1. 它讓 PPU memory access 不再是單純函式呼叫，而是經過 address path 的事件。
2. 它讓 mapper 尤其是 `MMC3` 能觀察到更接近真機的 bus 活動。
3. 它讓 `$2007`、背景 fetch、sprite fetch、palette corruption 開始共享同一套底層語言。

從架構角度看，這是新版最值得保留與繼續深化的部分之一。

---

## 50. 現在最合理的下一步延伸

在完成這一節之後，若要繼續把文件做得更完整，下一個最值得補的方向是：

1. 針對 `PPU_ALE / PPU_READ / PPU_WRITE / PPU_2007_* latches` 畫出更細的訊號關係
2. 把 `$2001` rendering on/off 與 palette corruption 的新註解單獨拆成一章
3. 列出哪些地方作者自己已標註 `TODO`, `TEMPORARY`, `not accurate`

這三塊可以幫助判斷：

- 新版目前已經變得更正確的地方
- 新版仍然暫時硬編碼或未完成的地方

---

## 新版仍未完成 / 作者自知不準確處

這一節不再只談設計優勢，而是整理 `new` 版程式裡作者自己明確留下的風險訊號。

## 51. 明確標註為 `TODO / TEMPORARY / wrong / unimplemented` 的區塊

從新版核心程式中的註解來看，以下是最重要的未完成區：

### PPU / `$2001`

- `StorePPURegisters($2001)` 內有明確註解：
  - `jank and sloppy`
  - `temporary`
  - `fix it later`
  - `Remove this hard-coded junk`
  - `This is temp. I know it's wrong`

這不是一般保守註解，而是作者直接承認這段目前仍是過渡方案。

### rendering off / palette corruption

新版在 `$2001` 關閉 rendering 的註解裡，提到：

- 目前理解來自外部討論與觀察
- 對 PAR / NT / AT / `v` address mux 的具體交互還在補強
- palette corruption 某些細節仍未完全實作

例如還有：

- `TODO: emulate this part`
- `TODO: Nybble 7 can corrupt color F. It's inconsistent though`

### scroll / `CopyV`

`PPU_IncrementScrollY()` 中仍有：

- `This isn't actually accurate. More research needed.`

也就是 `CopyV` 路徑本身仍然是作者認知中的近似做法。

### rendering disabled 時 address bus 的相位

新版有一條很關鍵的註解：

- `TODO: Is this occurring one ppu cycle too late???`

位置是在：

- rendering 關閉時，直接把 `PPU_AddressBus = PPU_v`

這很重要，因為它意味著新版雖然 address path 更硬體化，但某些「rendering 關閉時 bus 到底何時切回 v」的相位仍未完全確定。

### `PPU_EXT_Enable`

新版新增：

- `PPU_EXT_Enable`

但註解明確寫：

- `otherwise unimplemented`

也就是這個控制位目前只有狀態切換，沒有真正進入功能模型。

### sprite evaluation / OAM corruption 某些細節

仍有註解像：

- `I have no idea`
- `TODO: Can we test for this with a well timed write to $2000?`

這表示 sprite fetch / OAM corruption 某些分支雖已比舊版更細，但並未完全定案。

---

## 52. 這些未完成處大多集中在哪裡

值得注意的是，這些未完成區並不是均勻分布，而是高度集中在幾個主題：

1. `$2001` 的 rendering on/off timing
2. palette corruption 的真正形成機制
3. rendering 關閉後 PPU address source 如何切換
4. `CopyV` 相關 scroll 邊界
5. sprite evaluation / OAM corruption 的極端 timing

### 這代表什麼

新版最穩固的地方是：

- `$2007` read/write bus 模型
- PPU fetch path 的 address path 統一
- mapper 責任邊界重整
- FDS 與 MMC3 的掛接方式

新版相對還在過渡中的地方是：

- `$2001` 周圍的 rendering 邊界現象
- 某些極端 PPU glitch 行為

這個分布很合理，因為 rendering on/off 本來就是 NES PPU 最難的區域之一。

---

## `$2001` / Rendering On-Off / OAM / Palette Corruption 專章

這一節單獨整理新版在 `$2001` 與 rendering 開關邊界上做了什麼，以及哪裡仍然不夠完整。

## 53. `old` 與 `new` 對 `$2001` 的共同點

兩版其實都承認：

- `$2001` 的效果不是在 CPU 寫入那一瞬間全部生效
- 不同 alignment 會有不同 delay
- rendering 的 enable / disable 會影響：
  - background/sprite 顯示
  - OAM corruption
  - palette corruption
  - emphasis bits / greyscale

所以這不是新版才開始重視的問題。

---

## 54. 新版 `$2001` 的進步點

雖然作者自己承認這段還不完美，但新版相較舊版仍有幾個明顯進展。

### 1. 與 address path 理論連結更完整

新版註解已經不是單純說：

- 「關閉 rendering 某時刻會造成 palette corruption」

而是進一步嘗試用下面這套機制解釋：

- VRAM address mux 會在不同來源間切換
- rendering disabled 時，`v` 可能作為某個輸入來源出現
- `AT` input 也會共享部分位址來源
- 因此在特定 timing 下，AT path 可能短暫指向 palette 區域

這是一個比舊版更有因果性的解釋。

### 2. 區分多種 delay

新版把 `$2001` 相關效果拆成：

- `PPU_Update2001Delay`
- `PPU_Update2001OAMCorruptionDelay`
- `PPU_Update2001EmphasisBitsDelay`

也就是不再假設所有效果同時落地。

### 3. 區分 `Instant`、`Delayed`、最終寄存器值

新版同時維護：

- `PPU_Mask_ShowBackground`
- `PPU_Mask_ShowSprites`
- `PPU_Mask_ShowBackground_Instant`
- `PPU_Mask_ShowSprites_Instant`
- `PPU_Mask_ShowBackground_Delayed`
- `PPU_Mask_ShowSprites_Delayed`

這雖然讓狀態很多，但反映作者正試圖表示：

- CPU 寫入後，PPU 各個子系統並不是同時感知到這個變化

這個方向是合理的。

---

## 55. `$2001` disable rendering 時的 OAM corruption 模型

新版對 disable rendering 的處理大意如下：

1. 先判斷寫入前是否正在 rendering
2. 再判斷新值是否關閉 background 與 sprite
3. 若是在非 vblank 區域關閉 rendering：
   - 設 `PPU_OAMCorruptionRenderingDisabledOutOfVBlank_Instant`
   - 後續再透過 delay 與 sprite evaluation 流程決定是否真正形成 corruption

另外：

- 若 `PPU_PendingOAMCorruption` 已存在
- 又在特定 alignment 重新 enable rendering
- 可能設 `PPU_OAMCorruptionRenderingEnabledOutOfVBlank`

### 設計意義

這表示新版已經不把 OAM corruption 視為單一旗標，而是視為：

- 一段在 rendering 開關與 sprite evaluation 流程之間傳遞的狀態

這比單點式硬編碼更接近真機。

---

## 56. `$2001` disable rendering 時的 palette corruption 模型

新版目前的做法是：

- 若原本在 rendering
- 新值把 rendering 關閉
- 且發生在非 vblank
- 且 dot 落在 nametable fetch 早期兩個點
- 且 `PPU_v >= 0x3C00`

則設：

- `PPU_PaletteCorruptionRenderingDisabledOutOfVBlank = true`

### 與舊版的差異

舊版也有相似效果判斷，但新版的註解給出了更深入的理論：

- 問題不是單純「v 指到 palette」
- 而是 address mux / AT source / rendering disable 交會時，某個本不該指向 palette 的路徑瞬間指到了 palette

### 這代表什麼

即使新版還沒完全實作所有細節，但設計理解已經提升到：

- 不是只知道「會壞」
- 而是開始知道「可能為什麼會壞」

這與新版整體風格一致。

---

## 57. 為什麼 `$2001` 反而是新版最明顯的過渡區

這份 diff 中最有趣的反差是：

- `$2007` 明顯是從舊模型跨進新模型
- `$2001` 則是半新半舊

### `$2007`

新版對 `$2007`：

- 願意重建 signal / latch / half-step
- 不怕大改

### `$2001`

新版對 `$2001`：

- 開始用 address mux 理論重新解釋
- 但實作層仍保留不少 old-style hard-coded delay
- 作者自己也承認這段暫時不乾淨

### 判讀

這很像一次大型重構中的中間狀態：

- 核心骨幹已經往新方向走
- 但最麻煩的 rendering 開關角落案例還沒完全遷移完

這不是缺點，而是很典型的重構過渡痕跡。

---

## 58. 新版 savestate 也保留了這些過渡狀態

新版在 savestate 裡仍保存：

- `PPU_Update2001Delay`
- `PPU_Update2001OAMCorruptionDelay`
- `PPU_Update2001EmphasisBitsDelay`

而且還明確註記：

- `TEMPORARY`

### 設計意義

這代表作者知道：

- 雖然這些狀態將來可能被更底層的模型取代
- 但在目前版本中，它們仍然是行為正確性的一部分

這是一種很務實的工程折衷。

---

## 59. 風險評估：新版目前最可信與最需要小心的區域

綜合前面幾節，可以把新版大致分成兩類。

### 相對可信度較高的區域

- `$2007` 的 bus/latch state machine
- `PPU_OctalLatch / PAR / AddressBus` 路徑
- mapper 責任重新劃分
- FDS byte transfer / IRQ 掛接
- MMC3 對 A12 bus 的觀測環境

### 需要小心看待的區域

- `$2001` 寫入後的多段 delay
- rendering on/off 的極端 phase
- palette corruption 細節
- OAM corruption 的一些 odd/even edge case
- `CopyV` 路徑
- `PPU_EXT_Enable`

### 這對分析版本優劣的意義

如果要問「新版是不是全面比舊版穩」：

- 從架構方向看，是
- 從所有 edge case 都已經收斂完成來看，還不是

也就是：

- 新版方向更對
- 但仍處於若干高難區域的過渡期

---

## 60. 這一輪補充後的總結

加入未完成處與 `$2001` 專章後，整份分析可以更平衡地描述新版：

- 它不是只有理想化的硬體模型
- 也不是已經全面完成的精確模擬

更準確的說法是：

- 新版已經在 `$2007`、PPU address path、MMC3 bus 觀測、FDS device timing 上跨出很大一步
- 但在 `$2001`、rendering 開關與某些 corruption 邊界行為上，仍保留相當多過渡狀態與暫時做法

這樣看，`new` 是一個方向非常明確、但仍未完全收尾的架構升級版。

---

## 總表與分類結論

這一節把前面所有分析壓縮成兩種最實用的形式：

- `old vs new` 總表
- 改動分類表

## 61. `old vs new` 總表

| 面向 | `old` | `new` | 判讀 |
| --- | --- | --- | --- |
| 核心風格 | 高階行為模擬 | bus / latch / phase 模型 | `new` 架構方向更明確 |
| `$2007` | 以 `PPU_Data_StateMachine` 與多個 edge-case flag 近似 | 以 `PPU_2007_*`、ALE、SR latch、half-step 重建 | `new` 是最大架構升級 |
| PPU 記憶體讀取 | `FetchPPU(address)`，呼叫端直接給位址 | `FetchPPU()` 由 `PPU_AddressBus + PPU_OctalLatch` 決定位址 | `new` 更接近真機 bus |
| PPU 內部位址寄存器 | `PPU_ReadWriteAddress`, `PPU_TempVRAMAddress` | `PPU_v`, `PPU_t`, `PPU_ReadBuffer` | `new` 語意更標準、更利於深究 timing |
| 背景 / sprite fetch | 以直接算位址為主 | 以 `PAR + MUX + AddressBus + OctalLatch` 為主 | `new` 的 address path 比較完整 |
| CPU/PPU/APU 時鐘模型 | 倒數式 phase | 遞增式 phase | `new` 更容易描述事件發生點 |
| FDS 支援 | 以 BIOS/PRGRAM/stub 為主 | 有 byte transfer clock、IRQ enable/ack、savestate | `new` 是實質功能補完 |
| MMC3 | IRQ 演算法存在，但依賴較高階的 bus 語境 | IRQ 演算法近似相同，但觀測到的 bus 更真實 | `new` 提升的是輸入訊號品質 |
| base mapper PRGRAM | core 預設處理 `$6000-$7FFF` | 交由各 mapper 明確決定 | `new` 責任邊界更正確 |
| savestate | 保存舊 state machine 狀態 | 保存 bus/latch/PAR/`PPU_2007_*` 狀態 | `new` state 更貼近新模型 |
| `$2001` rendering on/off | 以 delay 與旗標處理 | 仍有進步，但仍混合 hard-coded delay 與新理論 | `new` 在這一塊仍過渡中 |
| palette/OAM corruption | 有行為模擬 | 理論解釋更深入，但細節未完全收尾 | `new` 方向更好，完成度未滿 |

---

## 62. 改動分類總表

以下把主要差異分成三類：

- `架構升級`
- `功能補完`
- `仍未完成`

| 主題 | 分類 | 說明 |
| --- | --- | --- |
| `$2007` 從舊 state machine 改成 `PPU_2007_* + ALE + latch + half-step` | 架構升級 | 這是最核心的結構性變更 |
| `FetchPPU()` 改成依賴 `PPU_AddressBus + PPU_OctalLatch` | 架構升級 | PPU memory path 從 helper 變成 bus 模型 |
| `PPU_v / PPU_t / PPU_ReadBuffer` 取代舊命名與舊語意 | 架構升級 | 與 nesdev 模型對齊，利於精細 timing |
| `PPU_PatternAddressRegister_*` / `PPU_PAR_MUX` / `PPU_OctalLatch` | 架構升級 | 建立完整 address path |
| 背景 fetch / sprite fetch 共用 bus-first 模型 | 架構升級 | 讓多個子系統共用同一底層語言 |
| base mapper 拿掉預設 PRGRAM 行為 | 架構升級 | 責任從 core 移回 mapper |
| `MMC3` 自己接管 `FetchPPU()` | 架構升級 | mapper 可以真正控制 PPU memory source |
| `MMC3` 的 `Mapper_4_M2Filter` 留在 mapper 內 | 架構升級 | mapper-specific timing state 不再污染 core |
| CPU/PPU/APU phase 模型改成遞增相位 | 架構升級 | 讓 M2、PPU 半拍、APU 對齊更自然 |
| FDS byte transfer clock | 功能補完 | 從資料結構變成裝置事件 |
| FDS `$4025` 控制寄存器 | 功能補完 | IRQ enable 行為開始成形 |
| FDS `$4031` data input + IRQ acknowledge | 功能補完 | CPU 可與 FDS 互動，而非只有 stub |
| FDS savestate 新增控制/clock/shift register | 功能補完 | 裝置狀態可序列化 |
| `MMC3` savestate 對 `PRGVRAM` 做 null-safe 處理 | 功能補完 | 穩定性補強 |
| `AOROM/CNROM/UxROM/MMC2` 移除 `base.StorePRG()` 回退 | 功能補完 | 配合新責任邊界，避免誤寫 PRGRAM |
| `$2001` 延遲模型 | 仍未完成 | 作者明確標示 temporary / jank / wrong |
| rendering on/off 與 palette corruption 細節 | 仍未完成 | 已有較好理論，但尚未完全落地 |
| `CopyV` 路徑 | 仍未完成 | 註解直接說 not accurate / more research needed |
| rendering disabled 時 `PPU_AddressBus = PPU_v` 的相位 | 仍未完成 | 作者懷疑可能晚 1 個 PPU cycle |
| `PPU_EXT_Enable` | 仍未完成 | 可切換但實際未實作 |
| OAM corruption 極端邊界 | 仍未完成 | 仍有 `I have no idea` / TODO 類註解 |
| palette corruption 某些 nybble 細節 | 仍未完成 | 作者已明示不一致、待回頭處理 |

---

## 63. 哪些改動最能代表 `new` 的價值

若只選最有代表性的幾項，`new` 的價值主要來自以下幾個點：

1. `$2007` 不再只是例外規則集合，而是有自己的 bus/latch/phase 模型。
2. 背景 fetch、sprite fetch、`$2007` access 開始共享同一套 PPU address path。
3. `MMC3` 開始站在比較真實的 bus 觀測點上工作，而不是只靠高階地址計算。
4. `FDS` 終於從靜態支援跨進裝置時序支援。
5. mapper 與 core 的責任邊界變清楚。

這幾點合在一起，才構成「新版是架構升級版」這個判斷。

---

## 64. 哪些地方不能高估 `new`

雖然 `new` 的方向更好，但不能把它看成完全收斂的最終版。

最不能高估的區域是：

- `$2001`
- rendering on/off 的 phase 細節
- palette corruption 細節
- OAM corruption 邊界
- 若干 `CopyV` / address mux 邊界

也就是：

- `new` 很像一個「核心骨架已經升級完成」
- 但「某些最棘手角落案例仍在搬遷中」的版本

這個判斷比單純說「新版比較好」更精確。

---

## 65. 最終總結

如果把 `old -> new` 的變化用最濃縮的方式分類：

### 架構升級

- `$2007` state machine 重建
- PPU address path 重建
- mapper/core 責任重分配
- MMC3 bus 觀測環境重建

### 功能補完

- FDS clock / IRQ / register interaction
- MMC3 / mapper 的穩定性與記憶體來源處理補強

### 仍未完成

- `$2001` 相關 timing
- rendering on/off 造成的 corruption 邊界
- 若干作者已明講尚未精確的 PPU 細節

因此，最準確的評價不是：

- `new` 已經完全完成

而是：

- `new` 已經建立了更好的底層架構，並且完成了一部分重要功能補完，但在最難的 PPU 邊界案例上仍保留過渡痕跡。
