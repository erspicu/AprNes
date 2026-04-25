# AprNes PPU / NTSC / CRT 效能改善評估文章版

日期：2026-04-25

這份文件是 `PPU_NTSC_CRT_Optimization_Notes.md` 的文章版整理，目的不是重新設計 PPU，而是把目前看起來仍有機會改善的地方，用比較接近實作說明書的方式寫清楚。分析範圍只包含 AprNes 專案中 PPU、NTSC 與 CRT 輸出相關的 `.cs` 檔案；mapper 相關檔案不列入討論，也不建議在這一輪最佳化中動到 mapper。

從目前程式結構來看，AprNes 的 PPU 已經不是單純的直譯式 tick 迴圈。`ppu_dispatch.cs` 已經用 341 個 dot 對應的 function pointer dispatch table 來切分不同階段，visible pixel zone、sprite fetch、prefetch、dummy、tail、vblank 與 pre-render 都有分開處理。visible pixel 的熱路徑也已經高度內聯，sprite shifter 使用 64-bit SWAR 技巧，palette 顏色也透過 `palCache` 快取。NTSC 類比輸出則是先累積 scanline，再透過 `Parallel.For` 延後處理。CRT scalar path 也已經在部分迴圈中使用 `Vector<T>`。

因此，數位 PPU 本體剩下的效能空間大多不是大幅度重寫，而是針對熱路徑做更細的專門化。比較有潛力的區塊，反而是在輸出模式分流，以及 NTSC / CRT 後處理的內層迴圈。

## 一、拆開數位與類比 visible pixel handler

目前 `Ppu_Tick_Visible_PixelZone()` 同時維護兩條輸出管線。一條是數位 RGB 輸出使用的 `dotColor`、`prevDotColor`、`prevPrevDotColor`、`prevPrevPrevDotColor`；另一條是類比 NTSC 輸出使用的 `dotPalIdx`、`prevDotPalIdx`、`prevPrevDotPalIdx`、`prevPrevPrevDotPalIdx`。

實際執行時，數位模式只需要最後把 `prevPrevPrevDotColor` 寫入 `ScreenBuf1x[pos]`；類比模式只需要把 `prevPrevPrevDotPalIdx` 寫入 `ntscScanBuf[cx - 4]`，再交給 NTSC capture / flush 流程處理。也就是說，兩種模式目前都在 visible pixel 熱路徑中維護另一種模式不需要的狀態。

建議作法是把 `Ppu_Tick_Visible_PixelZone()` 拆成兩個專門版本：一個是 `Ppu_Tick_Visible_PixelZone_Digital()`，另一個是 `Ppu_Tick_Visible_PixelZone_Analog()`。初始化或切換輸出模式時，再依照 `AnalogEnabled` 重新配置 visible 區段的 dispatch table。數位版本只保留顏色管線，像素合成後直接透過 `palCache[pa]` 得到 `uint` 顏色；類比版本只保留 palette index 管線，像素合成後只計算 `ppu_ram[0x3f00 + pa] & 0x3f`。

這個修改不一定要急著抽共用 helper。因為這段是 PPU 最熱的路徑之一，如果為了減少重複程式碼而引入額外呼叫或難以內聯的 helper，反而可能抵消收益。AprNes 目前在 PPU 熱路徑已經傾向用重複的專門化程式碼換取速度，因此這個方向和現有風格一致。

需要特別注意的是，sprite 0 hit、背景與 sprite priority、left-edge mask、palette corruption，以及 delayed pixel pipeline 都必須在兩個版本中維持完全一致。類比版本也不能漏掉 `Ppu_Tick_Visible_SpriteFetch()` 內對 `ntscScanBuf[255]` 的處理，以及 `cx == 260` 時的 `Ntsc_CaptureScanline()`。如果 `AnalogEnabled` 可以在 ROM 執行中切換，就必須在同一個重建 render buffer 或重新初始化的流程裡重建 dispatch table。

這項改善的預估效益是：數位模式約 2% 到 5%，類比模式中 PPU 側約 3% 到 8%。風險屬於中等，主要不是演算法難，而是兩份 handler 必須保持時序與畫面行為一致。

## 二、替 `$2007` pipeline 加入保守 idle fast path

`PPU_DATA_Pipeline_Step(int phase)` 會在 PPU full step 與 half step 中被呼叫。多數時間 CPU 並沒有正在進行 `$2007` read / write，也沒有待處理的 buffered read / write，但目前仍會在每個 phase 評估 pipeline 狀態。

可以加入一個簡單的活動旗標，例如 `ppu2007PipelineActive`。當 `ppu_r_2007()` 設定 `ppu2007_Read_SR = true`，或 `ppu_w_2007()` 設定 `ppu2007_Write_SR = true` 時，把這個旗標打開。只要 `ppu2007_Read_SR`、`ppu2007_Write_SR`、`ppu2007_PD_RB`、`ppu2007_DB_PAR` 或其他 read / write latch 還沒有回到 idle 狀態，就不要關閉它。

第一版不建議直接對所有 phase 做 aggressive early return。比較安全的做法是先只針對 phase 3 加 idle fast path，因為 phase 1 仍然會產生 `ppu2007_PPU_READ`、`ppu2007_PPU_ALE` 等訊號，這些訊號會影響 tile fetch、octal latch 與 address bus。等測試確認 phase 3 的快速返回沒有破壞 `$2007` 行為後，再考慮 phase 2。

這項改善的效益約 2% 到 6%，PAL / Dendy 模式可能因為每 frame 有更多 scanline / dot 而略高。不過風險也較高，因為 `$2007` read buffer、palette read、open bus、延遲 write 都是 emulator 測試常抓的細節。建議先搭配 `ppu_read_buffer`、`vram_access`、`ppu_open_bus`、palette RAM，以及 mid-frame VRAM read / write 的測試再推進。

## 三、依 `AnalogSize` 專門化 NTSC decode

`Ntsc.cs` 的 decode 內層迴圈目前會做類似 `x / N` 或 `outX / N` 的計算，其中 `N` 是 `ntsc_analogSize`，通常是 2、4、6 或 8。這種 division 發生在每個輸出像素上，在類比輸出解析度放大時成本會變得明顯。

建議把 decode path 依照 `AnalogSize` 拆成專門版本，例如 `RunDecodeLoopScale2()`、`RunDecodeLoopScale4()`、`RunDecodeLoopScale6()`、`RunDecodeLoopScale8()`，其他非標準倍率則保留 generic path。專門版本的迴圈應該改成先走 NES dot，再輸出對應數量的類比像素。以 scale 4 為例，可以先讀取一次 `dotY[d]`、`dotI[d]`、`dotQ[d]`，再連續輸出 4 個 pixel。

這樣可以拿掉內層 division，也可以減少重複的陣列索引計算。Composite decode 要特別注意 subcarrier phase：phase 必須仍然依照每一個輸出像素前進，而不是每個 NES dot 才前進一次。RF noise 與 herringbone 相關狀態也要維持 per output pixel 的更新規則。`DecodeAV_SVideo()` 也可以用同樣方式拆。

這項改善在類比 fast decode 上預估有 5% 到 15% 的效益，`AnalogSize` 越大越有機會明顯。風險屬於中低，只要 phase 與 noise state 沒有改變，這主要是機械性的迴圈重排。驗證時應該對 AV、SVideo、RF，以及 `AnalogSize` 2、4、6、8 都做 screenshot pixel diff。

## 四、重用 CRT horizontal blur scratch buffer

`CrtScreenScalar.ApplyHorizontalBlur()` 目前在 `Parallel.For` 的 row worker 裡使用 `stackalloc float[Crt_SrcW]`。`Crt_SrcW` 是 1024，所以每次大約配置 4 KB stack 空間。這個成本單次不算大，但 CRT render 每 frame 會處理多個 color plane 與 240 條 source row，因此累積起來仍然值得處理。

建議改成 per-thread scratch buffer，做法可以參考 `Ntsc.cs` 的 thread-local scratch 設計。也就是用 `[ThreadStatic]` 保存每個 worker thread 自己的暫存 row buffer，第一次使用時透過 `NesCore.AllocUnmanaged()` 配置，後續每一列直接重用。

原本的 `Buffer.MemoryCopy()` snapshot 仍然要保留，因為 horizontal blur 是讀寫同一條 row 的資料，如果不先複製一份來源，會產生 read-after-write 問題。不能使用單一 global scratch buffer，否則 `Parallel.For` 下不同 row 會互相覆蓋。

這項改善主要影響 CRT path，預估效益約 3% 到 10%，在 `HBeamSpread > 0` 時比較看得出來。風險偏低到中等，主要要確認 thread-local buffer 沒有共用錯誤，並且重複 resize / fullscreen 切換時沒有 row corruption。

## 五、加入 no-sprite visible scanline fast path

visible pixel 熱路徑目前每個 pixel 都會檢查 sprite 狀態，例如 `showSpr && (cx > 8 || ShowSprLeft8) && spriteAnyActive`。當某條 scanline 沒有任何 sprite pixel 會出現時，這個判斷雖然很便宜，但仍然會在每條可見 scanline 重複 256 次。

可以在 sprite eval / fetch 已經知道這條 scanline 是否有 sprite 後，保存一個 `scanlineSpritesActive` 或類似旗標。如果確定該 scanline 沒有 sprite，就走 BG-only 的 visible handler；如果有 sprite，就走 BG+sprite handler。

這項優化看起來簡單，但實際上風險不低。原因是 sprite 0 hit、left-8 sprite masking，以及 `$2001` mid-scanline 改變 sprite visibility 都會影響正確性。保守版本可以先只快取「這條 scanline 是否可能有 sprite」，不要立即切完整 handler；或是在偵測到 `$2001` mid-scanline write 時停用這個 fast path。

預估效益約 1% 到 4%，比較容易在 title screen、menu 或其他 sprite 很少的畫面看出來。因為風險中等，建議等前面幾個比較直接的最佳化完成並 profile 後再做。

## 六、把 `PPU_DATA_Pipeline_Step(int phase)` 拆成 phase-specific methods

目前 `PPU_DATA_Pipeline_Step(int phase)` 內部會依照 `phase` 分支，但呼叫端其實都知道自己要呼叫哪一個 phase。理論上 JIT 有可能內聯並做 constant-folding，不過在 .NET Framework 4.8.1、方法本身又偏大的情況下，不應完全假設 JIT 一定會幫忙消掉。

建議把它拆成三個方法：`PPU_DATA_Pipeline_Phase1()`、`PPU_DATA_Pipeline_Phase2()`、`PPU_DATA_Pipeline_Phase3()`。每個方法只保留該 phase 需要的原始程式區塊，呼叫端直接呼叫對應方法。這樣不只可能減少 phase branch，也會讓前面提到的 `$2007` idle fast path 更容易推理。

這項改善預估約 1% 到 3%，在 .NET Framework 上可能比現代 JIT 更有價值。風險屬於中低，但仍要把它當成 `$2007` timing 相關變更來測，因為 phase update 的順序只要錯一點，就可能影響 read buffer 或 open bus 行為。

## 七、謹慎評估 sprite overflow precompute fusion

`PrecomputeOverflow()` 目前會在每條 visible scanline 的 dot 1 掃描 OAM，用來預先計算 sprite overflow cycle。後續 sprite evaluation 又會走 OAM，因此理論上有重複工作。

可行方向是把 overflow 的判斷融合進 `SpriteEvalTick()`，例如在正常 evaluation 中維護找到的 sprite 數量，當第八個 sprite 出現後，再開始追蹤 overflow bug 需要的 pseudo-index，最後在與現有 precompute 相同的 cycle 設定 `spriteOverflowCycle`。

不過這項不建議優先做。NES sprite overflow 行為本來就很容易出錯，目前 `PrecomputeOverflow()` 雖然多掃一次 OAM，但它是獨立且容易驗證的邏輯。把它融合進 sprite evaluation state machine 會增加理解與除錯成本，收益卻大約只有 1% 到 3%。除非 profile 明確顯示它是熱點，否則建議排在最後，甚至可以先不做。

## 建議實作順序

第一優先是 `AnalogSize` 專門化 NTSC decode。這項變更的效益相對明顯，而且主要集中在後處理迴圈，對 PPU timing 的風險較低。

第二優先是 CRT horizontal blur scratch buffer 重用。這同樣偏向後處理最佳化，容易獨立驗證，也不會影響 mapper 或 PPU 核心時序。

第三優先才是拆分數位與類比 visible pixel handler。這項對數位與類比 PPU 都有機會帶來收益，但必須很仔細比對 sprite 0 hit、palette corruption、left-edge mask 與 delayed output pipeline。

第四可以拆 `PPU_DATA_Pipeline_Step()` 成 phase-specific methods。完成後，再以非常保守的方式加入 `$2007` phase 3 idle fast path。

no-sprite scanline fast path 建議等 profile 證明值得做再排入。sprite overflow fusion 則建議最後才考慮，因為它的風險明顯高於收益。

## 驗證與效益評估方式

建議把 benchmark 分成數位 rendering、Analog AV、Analog SVideo、Analog RF、UltraAnalog + CRT，以及 PAL / Dendy 幾組。數位路徑和類比 / CRT 路徑的瓶頸不同，混在一起測容易看不出單一修改的效果。

每一項修改都應該至少記錄 average frame time 與 FPS。如果有工具可以取得 1% low frame time，也應該一起記。正確性方面，數位模式可以用 screenshot CRC 或 pixel diff；類比與 CRT 模式則應該針對 AV、SVideo、RF、不同 `AnalogSize` 做 pixel diff。PPU timing 相關修改必須跑 `$2007` read buffer、VRAM access、open bus、sprite 0 hit、sprite overflow、palette RAM，以及 `$2001` mid-frame toggle 測試。

整體來看，AprNes 的 PPU 核心已經有不少最佳化基礎，所以不建議期待單一改動帶來非常大的提升。比較合理的策略是先做 NTSC / CRT 後處理這類低風險、可量測的改善，再逐步處理 visible pixel handler 與 `$2007` pipeline 這種高敏感度熱路徑。這樣比較容易在維持相容性的前提下，把效能穩定地往上推。
