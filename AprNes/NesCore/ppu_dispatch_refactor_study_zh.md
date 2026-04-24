# AprNes PPU Dispatch Refactor Study

## 目的

這份文件是針對下列三個檔案的重構分析筆記：

- `PPU.cs`
- `ppu_new.cs`
- `ppu_dispatch.cs`

重點放在 `ppu_dispatch.cs`。
分析目標不是改功能，而是找出在目前 dispatch-table 架構下，還有哪些邏輯可以：

- 再移除不必要判斷
- 再進一步專門化 handler
- 在不犧牲效能前提下縮小 method / IL 體積
- 把重構後殘留的舊狀態或死資料清掉

這份報告最初是根據本機工作目錄中的程式碼靜態閱讀整理而成。
後文另外補上截至 `2026-04-24` 已實際落地的重構進度。

## 結論先講

目前 `ppu_dispatch.cs` 已經做了第一層的 dispatch-table 重構，但還沒有把 dispatch-table 的優勢用到最徹底。

最有價值的下一步不是去做大型 DRY 抽象，而是：

1. 先刪掉 `VisibleLine` 中已經不可能發生的邏輯。
2. 把 `PpuPhase4_SpriteEvalAndInit()` 依 dot 區段拆成更專門的 helper。
3. 更積極利用 visible table 的 per-slot 特性，把 `256 / 257 / 340` 這些尾端 dot 拆成專用 handler。
4. 清掉重構後留下來、目前看起來已經不再被使用的 PPU 狀態與緩衝。

真正不建議做的是：

- 把目前各 handler 的通用前後段硬抽成一個「每 dot 都要多呼叫一次」的 wrapper
- 把 PixelZone / Prefetch / Shared Render Block 大量 DRY 化成通用 helper

因為那類改法通常會讓 source 看起來比較乾淨，但會犧牲 hot path 的 branch predictability 與 JIT inline 形狀。

## 更新摘要（截至 2026-04-24）

本報告前半段是「提交前」的靜態分析。
從這份分析出發，工作目錄中的 `NesCore` 已經實際落地六個 refactor commit，而且每一輪都由使用者手動測試通過後才往下做。

### 已落地的六個 commit

| commit | 主題 | 主要變更 |
|---|---|---|
| `fbe94e0` | `refactor: simplify ppu dispatch tail path` | 把 `VisibleLine` 收斂成 `VisibleTail`，移除尾端 slot 死邏輯，同時清掉 `BaseNameTableAddr`、`Buffer_BG_array`、`sprLine*` 等死資料與配置 |
| `ee6d03f` | `refactor: specialize ppu phase4 dot paths` | 把 visible 路徑的 `Phase4` 從泛用 `PpuPhase4_SpriteEvalAndInit()` 拆成 dot-range 專用 helper，讓 visible handler 直接呼叫專用 Phase4 |
| `64dbeba` | `refactor: share visible dispatch aux paths` | 抽出 visible 非像素 handler 的共用骨架，收斂 `SpriteFetch / Prefetch / Dummy / Tail` 的共用 per-dot 前後段 |
| `2c03426` | `refactor: trim prerender and vblank dispatch paths` | 把 pre-render / vblank 的收尾骨架收斂，移除 pre-render 內不可達的 draw / frame-render / `skippedPreRenderDot341` reset |
| `05059a6` | `refactor: split prerender render block` | 抽出 `PpuBgTileFetchRange()` 與 `Ppu_PreRender_RenderBlock()`，讓 pre-render 不再背著 visible pixel composition 的死邏輯 |
| `1b467f6` | `refactor: share dispatch wrap and step1 paths` | 抽出 `PpuAdvanceAndMaybeWrap()`、`PpuDotAuxBeforeStep1Core()`、`PpuDotAuxStep1()`，收斂 `VisibleTail / PreRender / VBlank` 的 wrap 與 step1 骨架 |

### 這六個 commit 的整體效果

- 以 git commit 統計粗估，六個 commit 合計約 `323` 行新增、`569` 行刪除，淨減少約 `246` 行。
- 縮減量主要集中在 `ppu_dispatch.cs`，只有第一輪順手清掉 `PPU.cs`、`ppu_new.cs`、`Main.cs`、`FDS.cs` 中已確認無 `.cs` 消費者的殘留 state / 配置。
- 原本報告中最有價值、且風險最低的那批項目，目前大多已經完成。

### 這六輪實作實際碰到的高風險區

下列區域都已在重構過程中被碰到，因此也是目前最需要持續保守看待的區域：

- visible 尾端 slot `256 / 257 / 340` 與 wrap 到 `dot 0`
- pre-render odd frame skip 與 `skippedPreRenderDot341`
- `Phase4` 的 sprite evaluation / sprite overflow / `sprSlotCount` / `sprZeroInSlots`
- visible prefetch 與 pre-render 的 BG tile fetch，特別是 MMC3 A12 / IRQ 敏感路徑
- vblank 進入點、`SL240 cx1` frame render、`$2001` 延遲 OAM corruption 路徑

這些區域在每一輪都由使用者手動測試確認沒有回歸，但這不等於已經做過完整 benchmark 或完整測試矩陣。

### 原始優先級項目目前狀態

| 狀態 | 原始項目 | 現況 |
|---|---|---|
| 已完成 | 刪除 `Ppu_Tick_VisibleLine` 的死判斷與死呼叫 | 已改成更小的 `VisibleTail` 路徑，相關死邏輯已清除 |
| 已完成 | 拆分 `PpuPhase4_SpriteEvalAndInit()` 依 dot-range 專門化 | visible 路徑已不再共用泛用 `Phase4`，pre-render 才保留入口 |
| 已完成 | 移除疑似死資料：`BaseNameTableAddr`、`Buffer_BG_array`、`sprLine*` | 已從欄位、配置、釋放與使用點全部清除 |
| 不採用 | 將 visible slot `256/257/340` 改成三個專用 handler | 已實測 benchmark，沒有正向效益，且似乎略微回退效能；保留單一 `VisibleTail` |
| 不採用 | 將 slot `258/259`、`320` 做微分裂 | 已分別實測 benchmark，沒有正向效益，且似乎略微回退效能；維持目前共用 handler |
| 尚未處理 | 重構 `InitPpuDispatchTable()` 兩套 preprocessor 重複碼 | 仍可做，但主要是 source 體積與可讀性收益 |
| 仍不建議 | 把所有 handler 前後段統一抽成 common wrapper | 目前仍不建議，原因沒有改變 |
| 仍不建議 | 把 PixelZone / Prefetch / Shared fetch 大量 DRY 化 | 目前仍不建議，尤其 PixelZone hot path 更不應回抽 generic helper |

下面第 `一` 到第 `十` 節保留的是提交前的原始分析觀點，因此其中會出現當時的名稱與狀態，例如 `Ppu_Tick_VisibleLine`。
閱讀目前版本時，應以前面的「更新摘要」與最後的「更新後的下一批候選」為準。

## 目前架構的特點

### `ppu_dispatch.cs` 已經做對的地方

目前的設計方向是正確的：

- visible scanline 依 dot range 分成多個 handler
- pre-render 與 vblank 各自用單一 handler
- `PixelZone` 對 hot path 做了 aggressive specialization
- 重要的背景 fetch / pixel composition 已經開始從「全功能大函式」轉成「區段專用函式」

目前 visible table 的配置是：

- `0..255` → `Ppu_Tick_Visible_PixelZone`
- `256, 257, 340` → `Ppu_Tick_VisibleLine`
- `258..319` → `Ppu_Tick_Visible_SpriteFetch`
- `320..335` → `Ppu_Tick_Visible_Prefetch`
- `336..339` → `Ppu_Tick_Visible_Dummy`

這個設計已經說明一件事：

- 程式現在不是「看起來像 dispatch-table」
- 而是真的在利用 dot 區段固定性來移除 branch

### 目前還沒用滿的地方

雖然 visible line 已分區，但仍有幾個區塊其實已經知道自己只會跑特定 slot，卻還保留了 generic 邏輯。

最明顯的是：

- `Ppu_Tick_VisibleLine`
- `Ppu_Tick_Visible_SpriteFetch`
- `Ppu_Tick_Visible_Prefetch`
- `Ppu_Tick_Visible_Dummy`

它們內部仍呼叫較泛用的 `PpuPhase4_SpriteEvalAndInit()`，或仍保留一些在該區段根本不可能成立的條件。

這代表：

- dispatch-table 已經提供了 compile-time style 的 slot specialization 基礎
- 但程式還沒有完全把它轉換成更小、更純的 handler

## 原始優先級總表（提交前觀點）

| 優先級 | 項目 | 風險 | 預期收益 |
|---|---|---:|---|
| 高 | 刪除 `Ppu_Tick_VisibleLine` 的死判斷與死呼叫 | 低 | 立即縮小 handler、降低每條 visible line 尾段成本 |
| 高 | 拆分 `PpuPhase4_SpriteEvalAndInit()` 依 dot-range 專門化 | 中 | 減少 SpriteFetch/Prefetch/Dummy 區段的無效分支 |
| 高 | 移除疑似死資料：`BaseNameTableAddr`、`Buffer_BG_array`、`sprLine*` | 低到中 | 減少配置、初始化、清零與維護負擔 |
| 中 | 將 visible slot `256/257/340` 改成三個專用 handler | 中 | 更符合 dispatch-table 哲學，進一步移除 branch |
| 中 | 將 slot `258/259`、`320` 做微分裂 | 中 | 小幅減 branch，收益偏微優化 |
| 低 | 重構 `InitPpuDispatchTable()` 兩套 preprocessor 重複碼 | 低 | 主要改善 source 體積與可讀性，不影響 hot path |
| 不建議 | 把所有 handler 前後段統一抽成 common wrapper | 高 | 很可能增 call overhead、壞掉 hot path 形狀 |
| 不建議 | 把 PixelZone / Prefetch / Shared fetch 大量 DRY 化 | 高 | 容易損失 inline 與 specialization 收益 |

## 一、`Ppu_Tick_VisibleLine` 可以先直接瘦身

### 原因

目前 `Ppu_Tick_VisibleLine` 只會被這三個 slot 使用：

- visible table slot `256`
- visible table slot `257`
- visible table slot `340`

也就是說，這個 handler 的 `cx` 在 entry 時只可能是：

- `256`
- `257`
- `340`

post-increment 後只可能變成：

- `257`
- `258`
- `0`（wrap）

這個事實足以證明裡面有幾段邏輯已經是死的。

### 可以直接移除的項目

#### 1. `if (scanline >= nmiTriggerLine)` 是死條件

位於 `ppu_dispatch.cs` 的 `Ppu_Tick_VisibleLine()` 內。

原因：

- entry scanline 範圍是 visible `0..239`
- wrap 後最多只會到 `240`
- `nmiTriggerLine` 在 NTSC/PAL 是 `241`，在 Dendy 是 `291`

因此這個 handler 內不可能進到 `PpuPhase3_Events(cx)`。

這段可以直接刪。

#### 2. `if (oddSwap && ... scanline == 0 && cx == 2)` 是死條件

同一個 handler 中，post-increment 後 `cx` 只可能是 `257/258/0`，不可能是 `2`。

因此這個判斷可直接刪。

#### 3. `Ppu_ActiveScanline_RenderBlock(cx)` 在這裡是死呼叫

對這個 handler 而言，post-increment 後 `cx` 只可能是 `257/258/0`。

而 `Ppu_ActiveScanline_RenderBlock(cx)` 的有效工作範圍主要是：

- tile fetch：`1..256` 或 `321..336`
- pixel/sprite shift：`1..256`

所以在 `VisibleLine` handler 中：

- `cx = 257`：不進 tile fetch，不進 pixel block
- `cx = 258`：不進 tile fetch，不進 pixel block
- `cx = 0`：不進 tile fetch，不進 pixel block

也就是這個呼叫在這個 handler 裡只會進來再什麼都不做。
它是純死呼叫。

#### 4. `if (AnalogEnabled && cx == 260)` 是死條件

這個 handler 不會有 `cx == 260`。
可直接刪。

#### 5. `if (scanline == 240 && cx == 1) PpuPhase_FrameRender()` 是死條件

這個 handler post-increment 後不可能得到 `cx == 1`。

因此 frame render 不可能在這裡發生。
真正的 `SL240 cx1` 應該由其他 handler 路徑觸發，不是這裡。

### 建議結論

`Ppu_Tick_VisibleLine()` 是目前最容易先瘦身的一個 handler。

這一刀的特點是：

- 幾乎全是刪死碼
- 幾乎不動時序
- 對可讀性和 method 體積都有立刻收益

如果只做一個動作，我會先做這個。

## 二、`PpuPhase4_SpriteEvalAndInit()` 還太泛用

### 現況問題

`PpuPhase4_SpriteEvalAndInit()` 現在同時被多個 handler 使用：

- PixelZone
- VisibleLine
- SpriteFetch
- Prefetch
- Dummy
- PreRenderLine

但這些 caller 的 `evalDot` 範圍根本不同。

例如：

- `Visible_SpriteFetch` 的 `evalDot` 是 `259..320`
- `Visible_Prefetch` 的 `evalDot` 是 `321..336`
- `Visible_Dummy` 的 `evalDot` 是 `337..340`

在這些區段裡，`PpuPhase4_SpriteEvalAndInit()` 裡的大量條件其實都永遠不成立。

### 具體浪費

#### `Visible_SpriteFetch`

在這個 handler 中，`PpuPhase4_SpriteEvalAndInit()` 裡只有下列部分可能成立：

- OAM corruption top-path
- `PpuPhase4_SpriteFetch(evalDot)`

其他像：

- `0..64` clear OAM2
- dot 65 init
- `65..256` evaluation
- `322`
- `339`
- dummy NT fetch
- visible dot1 init

在這個 handler 裡全部是死分支。

#### `Visible_Prefetch`

這裡其實只有少量邏輯有意義：

- OAM corruption top-path
- `evalDot == 322` 時的 `oamCopyBuffer = secondaryOAM[0]`

其餘 sprite evaluation 相關檢查全部是 dead branch。

#### `Visible_Dummy`

這裡實際有意義的只剩：

- OAM corruption top-path
- `evalDot == 339` 的 `PpuPhase4_Dot339()`
- `PpuPhase4_DummyNTFetch(evalDot)`

其他部分都沒必要每 dot 經過。

### 建議拆分方式

把 `PpuPhase4_SpriteEvalAndInit()` 改成多個 dot-range 專用 helper。

推薦切法：

- `PpuPhase4_ActiveEval_0_256(int evalDot, bool ro)`
- `PpuPhase4_SpriteFetch_257_320(int evalDot)`
- `PpuPhase4_PostSpriteFetch_321_336(int evalDot)`
- `PpuPhase4_Dummy_337_340(int evalDot)`
- `PpuPhase4_WrapDot0()` 或保留在 dummy helper 處理

然後各 handler 直接呼叫自己那一段專用 helper。

### 為什麼這個改法比 DRY 更好

因為這不是在「抽共用」。
這是在把 generic phase 重新對齊 dispatch-table 已知的區段事實。

也就是：

- caller 已經知道自己的 dot 區間
- helper 應該反映這個事實

這樣做有三個好處：

- branch 減少
- helper 更短
- handler 與 helper 的語意更一致

## 三、更積極利用 dispatch table：把 visible 尾端 slot 再拆細

### 現況

visible table 已經用 slot-specialization 了，但在尾端還是把三個很不一樣的 slot 合併到同一個 `Ppu_Tick_VisibleLine()`：

- slot 256
- slot 257
- slot 340

這三個 slot 的行為差異其實很大：

- `256`：Yinc，對應 evalDot 257
- `257`：CopyHoriV，對應 evalDot 258
- `340`：wrap to next line，對應 evalDot 0

### 建議

用 dispatch table 原本就支援的 per-slot 特性，把這三個 slot 改成三個 handler：

- `Ppu_Tick_Visible_256`
- `Ppu_Tick_Visible_257`
- `Ppu_Tick_Visible_340`

這是非常「dispatch-table 正統」的改法。

### 這樣的收益

#### `Visible_256`

可以只保留：

- universal per-dot logic
- Yinc
- `PpuPhase4_SpriteFetch(257)` 及 slot count copy
- draw 尾端 pixel

#### `Visible_257`

可以只保留：

- universal per-dot logic
- CopyHoriV
- `PpuPhase4_SpriteFetch(258)`
- draw 尾端 pixel

#### `Visible_340`

可以只保留：

- wrap
- `PpuPhase4_DummyNTFetch(0)`
- 不做 draw

### 為什麼這值得

因為這三個 handler 每條 visible scanline 都會跑。
它們不算最熱，但也不是冷碼。

與其保留一個 generic handler 每次判斷：

- 是 256？
- 是 257？
- 是 340？
- 會不會 render？
- 會不會 frame render？

不如直接讓 dispatch table 幫你決定。

## 四、可選微優化：slot 258 / 259 / 320 進一步專用化

這一組屬於「做了可能有益，但要 benchmark」。

### `Visible_SpriteFetch`

這個 handler 中目前保留：

- `if (cx == 259)` 畫最後一個 pixel
- `if (AnalogEnabled && cx == 260)` capture scanline

但 visible table 對應的 slot 範圍是 `258..319`。

也就是只有：

- slot `258` 會觸發 draw
- slot `259` 會觸發 capture

其餘 `260..319` 的 slot 每次都在付兩個不必要條件。

### 建議

可再切成：

- `Ppu_Tick_Visible_SpriteFetch_258`
- `Ppu_Tick_Visible_SpriteFetch_259`
- `Ppu_Tick_Visible_SpriteFetch_Generic`

### `Visible_Prefetch`

同理，`chrABAutoSwitch` 那段只有 slot `320` 會中：

- 因為 post-increment 後 `cx == 321`

可再拆出：

- `Ppu_Tick_Visible_Prefetch_320`
- `Ppu_Tick_Visible_Prefetch_Generic`

### 評價

這一類改法：

- 對 branch 減少是正的
- 對 source / IL 體積不一定是正的

因此它比較像第二階段工作。
不是第一刀。

## 五、`PPU.cs` 與 `ppu_new.cs` 中的重構殘留清理

這一部分不直接改 dispatch-table，但很像是這次架構重構後遺留的包袱。

### 1. `BaseNameTableAddr` 看起來已經沒在用

目前搜尋結果顯示：

- reset 時賦值
- `$2000` write 時賦值
- 沒有任何讀取

這表示它很可能是舊架構殘留狀態。

建議：

- 先全 repo 再確認一次無反射/條件編譯依賴
- 若確認無讀取，直接移除

### 2. `Buffer_BG_array` 看起來是死緩衝

目前搜尋結果顯示：

- `Main.cs` / `FDS.cs` 配置
- `Main.cs` 釋放
- `ppu_new.cs` 的 `PpuPhase4_VisibleScanlineDot1Init()` 每條 visible scanline 清零一次
- 但沒有任何消費者讀取它

這表示：

- 目前每條 visible scanline 都在做一次 1024-byte memset
- 但結果沒被使用

這是非常高價值的清理點。

如果確認真的沒有外部依賴，就應該：

- 刪掉 field
- 刪掉 allocate/free
- 刪掉 dot1 init 裡的清零

### 3. `sprLineBuf / sprLinePri / sprLineSet / sprLinePalIdx` 看起來也是死遺留

目前搜尋結果顯示：

- 有配置
- 有欄位宣告
- 沒有讀取

這很像舊的 sprite compositing buffer 設計留下來的殘留。

建議處理方式和 `Buffer_BG_array` 一樣：

- 先做一次全 repo 驗證
- 若確定無使用，連配置一起刪

### 4. `PpuPhase4_VisibleScanlineDot1Init()` 可以縮

如果 `Buffer_BG_array` 真的是死資料，那這個函式可立即縮成：

- analog mode：填 backdrop index 到 `ntscScanBuf`
- digital mode：填 backdrop color 到 `ScreenBuf1x`
- `PrecomputeOverflow()`

目前這個函式的熱度不是最高，但它每條 visible scanline 都會跑一次。
少掉一個 1024-byte clear 是實質收益。

## 六、哪些 DRY 重構不建議做

### 不建議 1：把所有 handler 的 universal 前後段抽成 common wrapper

表面上看，很多 handler 都有共通片段：

- deferred update
- open bus decay
- vblank/pending handling
- eval-delay 同步
- `$2001` delay apply
- pipeline phase 1/2
- color pipeline shift

但如果把它抽成一個 common wrapper，再讓 wrapper 接一個 zone-specific callback，通常會帶來：

- 每 dot 多一次呼叫
- 甚至多一次 function pointer / indirect call
- JIT 難以跨 wrapper 和 callback 做完整常數化

這種改法通常是 source 變短，但 hot path 變慢。

### 不建議 2：把 PixelZone / Prefetch / Shared Render Block 大量共用化

目前的 duplication 雖然很多，但有其合理性：

- PixelZone 已知一定在 `1..256`
- Prefetch 已知一定在 `321..336`
- Shared Render Block 則保留給較泛用 caller

如果強行 DRY：

- 很可能把已經被刪掉的 range check 又帶回來
- 或引入 call boundary，讓 JIT 無法把 handler 當作小而穩定的 block 來處理

所以對 hot fetch/composition block，原則應該是：

- 接受少量 duplication
- 只對冷分支與死分支下刀

## 七、原始可落地重構順序（提交前版本）

### Phase 1：先做低風險死碼清理

建議順序：

1. 精簡 `Ppu_Tick_VisibleLine()`
2. 移除 `BaseNameTableAddr`
3. 驗證並移除 `Buffer_BG_array`
4. 驗證並移除 `sprLineBuf/sprLinePri/sprLineSet/sprLinePalIdx`

這一階段的特性：

- 幾乎都在刪死碼或死資料
- 風險最低
- 最容易 benchmark 出正向結果

### Phase 2：把 `PpuPhase4_SpriteEvalAndInit()` 區段化

建議做法：

1. 保留原版，先抽出新 helper
2. 讓 `Visible_SpriteFetch / Prefetch / Dummy` 改叫新 helper
3. 跑測試與 benchmark
4. 再決定是否讓 `VisibleLine` 也轉成專用 phase4 helper

這一階段會是整個 `ppu_dispatch` 真正開始往「dispatch-aware phase splitting」前進的地方。

### Phase 3：把 visible 尾端 slot 拆開

建議目標：

- `256`
- `257`
- `340`

若 benchmark 顯示正向，再考慮：

- `258`
- `259`
- `320`

## 八、benchmark 與驗證指引

每做一個重構步驟，都建議至少做下面四類驗證：

### 1. 功能正確性

至少重跑和 PPU 最相關的測試：

- `ppu_vbl_nmi`
- `sprite_hit`
- `sprite_overflow`
- `ppu_open_bus`
- `ppu_read_buffer`
- `oam_read`
- `mmc3_irq` 類測試

### 2. 邊界時序

特別注意：

- `$2002` VBlank 讀取邏輯
- `$2005/$2006/$2007` delayed effect
- MMC3 A12 / IRQ
- sprite 0 hit
- OAM corruption / palette corruption

### 3. 效能

benchmark 時建議分三種模式觀察：

- Digital 1x
- Analog 非 Ultra
- Ultra Analog + CRT

因為 code size / I-cache / JIT inline 影響在不同路徑上未必一致。

### 4. 程式體積觀察

如果目標包含「減少 code 體積」，除了看 source line 之外，也要看：

- handler 的 IL 是否縮小
- JIT 是否維持 inline
- PMU / profile 是否減少 i-cache 壓力

如果 source 變短但 hot handler 反而變胖，那不算成功。

## 九、我對後續重構的實際建議

如果是我來做，我會照這個順序下刀：

1. 清 `VisibleLine` 死碼
2. 清 `BaseNameTableAddr`
3. 驗證並清 `Buffer_BG_array`
4. 驗證並清 `sprLine*`
5. 拆 `PpuPhase4_SpriteEvalAndInit()` 成 dot-range helper
6. 把 visible `256/257/340` 拆成專用 handler
7. 最後才考慮 `258/259/320` 的微分裂

原因很簡單：

- 前四步幾乎都是刪已知冗餘
- 第五步開始才是架構重排
- 第六步才是更進一步吃滿 dispatch-table 的 slot specificity
- 第七步才是微優化

## 十、最重要的原則

在這個 PPU 上，最容易犯的錯不是「少優化」，而是：

- 用經典 OOP/DRY 思維把 hot path 又抽回 generic

這份程式目前最有價值的地方，就是它已經開始接受：

- 少量 duplication
- 多個小 handler
- 以 slot/dot 區段為核心的 specialization

因此後續重構的原則應該是：

- 刪死碼
- 刪死資料
- 強化專門化
- 避免回到共用大函式

如果要濃縮成一句話：

> 不要把 `dispatch-table` 只是當成入口分派器，而要讓它真正決定每個 dot handler 的責任邊界。

這是我認為目前這組 PPU 程式最值得往下走的方向。

## 十一、更新後的下一批候選

做到目前這個狀態後，`ppu_dispatch.cs` 內「低風險但收益明顯」的項目其實已經被清掉大半。
接下來如果還要繼續下刀，應該把預期收益下修成「中小型 code-size / branch 精修」，而不是再期待一次大幅縮減。

更新：下列三個 slot-level micro-specialization 候選已經實際嘗試並由 benchmark 驗證，結果沒有正向效益，且似乎略微回退效能。因此目前結論是「不採用」，保留現行較集中的 handler 形狀。

### 不採用 1：把 `258 / 259` 從 `Visible_SpriteFetch` 再拆出來

原因：

- 現在 `Visible_SpriteFetch` 中還保留：
- `cx == 259` 的最後一個 pixel draw
- `cx == 260` 的 NTSC scanline capture

但這兩件事只會發生在非常少數的 slot：

- entry slot `258`
- entry slot `259`

其餘 `260..319` 只是共用同一個 handler。

這代表還可以進一步切成：

- `Ppu_Tick_Visible_SpriteFetch_258`
- `Ppu_Tick_Visible_SpriteFetch_259`
- `Ppu_Tick_Visible_SpriteFetch_Generic`

這一刀屬於：

- 風險中
- 收益偏微優化
- 已 benchmark：沒有正向效益，且似乎略微回退效能；不採用

### 不採用 2：把 `320` 從 `Visible_Prefetch` 再拆出來

原因：

- `Visible_Prefetch` 現在除了共用 BG tile fetch 外，還保留 `cx == 322` 的 `oamCopyBuffer = secondaryOAM[0]`
- `chrABAutoSwitch` 的 `cx == 321` 特例也只跟 very-early prefetch slot 有關

可考慮拆成：

- `Ppu_Tick_Visible_Prefetch_320`
- `Ppu_Tick_Visible_Prefetch_Generic`

這個候選和 `258 / 259` 一樣，屬於：

- 收益不大
- 但 dispatch-table 哲學很純正
- 已 benchmark：沒有正向效益，且似乎略微回退效能；不採用

### 不採用 3：評估是否要把 `VisibleTail` 再拆成三個 slot handler

目前 `VisibleTail` 已經夠小，和最初的 `VisibleLine` 完全不是同一個量級。

所以現在再把它拆成：

- `256`
- `257`
- `340`

已經不是「清死碼」問題，而是典型的 slot-level micro-specialization。

我目前的判斷是：

- 已 benchmark：沒有正向效益，且似乎略微回退效能
- 不採用，保留單一 `VisibleTail`
- 這表示目前 handler 合併後的 IL/JIT 形狀比進一步 slot 分裂更適合現有熱路徑

### 候選 4：清理 `InitPpuDispatchTable()` 的 source 重複碼

這一項目前仍未做，但它幾乎完全不是效能問題，而是 source 管理問題。

目前 `NET10_0_OR_GREATER` 與非 `NET10` 分支裡，visible/pre-render/vblank table 的初始化邏輯基本重複。

這可以透過：

- 小型 local helper
- 或更保守的重複碼收斂

來縮小 source 體積。

但這類改動：

- 不會直接改善 hot path
- 主要是維護性收益

所以它的優先級應該排在所有時序敏感重構之後。

### 候選 5：只在 benchmark 證明有利時，才再拆 `PpuBgTileFetchRange()`

目前 `PpuBgTileFetchRange()` 已經脫離 `PixelZone`，不在最熱的像素組合路徑上。

它現在同時服務：

- visible prefetch
- pre-render

理論上還可以再拆成：

- `PpuBgTileFetchVisiblePrefetch(...)`
- `PpuBgTileFetchPreRender(...)`

好處是可以再去掉一點 range-check 與 `cx == 1 || cx == 321` 這種混合條件。

但這一刀的風險點在於：

- 會增加 source / helper 數量
- 收益不一定大
- 現在這條路徑已經不在最熱的 visible pixel block

所以我的建議是：

- 只有在 benchmark 顯示 `PpuBgTileFetchRange()` 仍然明顯佔比時才做

### 目前不建議再做的方向

下面這些方向在目前這個階段仍然不建議：

- 把 `Ppu_Tick_Visible_PixelZone` 的 tile fetch / pixel composition / sprite shift 回抽成 generic helper
- 把所有 handler 都再包回 single wrapper + callback
- 把 `PpuBgTileFetchRange()` 和 PixelZone 的內嵌 fetch block 合併回單一共用函式

原因很簡單：

- 目前已經清掉的大多是死碼或可明確專門化的 generic code
- 剩下真正還在 hot path 上的內容，多半就是不應該輕易 DRY 的部分

如果要把更新後的結論濃縮成一句話：

> `ppu_dispatch.cs` 的第一批高價值低風險重構已經基本完成；下一批工作會從「明顯冗餘清理」轉成「小幅 slot-specialization 與 source 管理」。 
