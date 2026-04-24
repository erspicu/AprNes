# AprNes PPU Dispatch 維護準則

## 目的

這份文件是給目前 `AprNes/NesCore` 的 PPU dispatch-table 架構使用的長期維護 checklist。

它不是在講「NES PPU 一般理論」，而是針對目前這套做法：

- `visible / pre-render / vblank` 三類 dispatch
- visible 再按 dot 區段切 handler
- visible hot path 盡量 specialized
- `Phase4` 與 pre-render / vblank 路徑做有限度 helper 拆分

目的只有一個：

- 讓後續重構不會把已經清出來的 hot path 又抽回 generic

## 一句話總結

這套 PPU 的核心維護原則是：

> 讓 dispatch-table 真正決定每個 dot handler 的責任邊界，而不是只當入口分派器。

## 這套寫法的定位

和傳統 monolithic per-dot `step()/clock()` 相比，這套寫法比較像：

- 把 PPU 當成一組 per-dot micro-handlers
- 讓 slot 事實直接塑造 code shape

它的好處是：

- hot path branch 更少
- handler 責任更清楚
- 比較容易識別死邏輯與 generic 殘留

它的代價是：

- 重構成本高
- 邊界時序風險高
- 很容易留下半 specialized、半 generic 的過渡狀態

所以後續維護一定要有紀律。

## 核心原則

### 1. 先問這段 code 屬於哪一個 dot 區段

每次要改 PPU，先回答：

- 這段邏輯屬於 visible、pre-render 還是 vblank？
- 如果是 visible，它屬於哪個 slot 區段？
- 這個 handler 的 `cx` / `evalDot` 範圍是不是已經固定？

如果答案是固定的，就不應該保留 generic branch。

### 2. 接受少量 duplication，不要過度 DRY

在這個 PPU 上，少量 duplication 不是壞事。

反而這些做法常常是不好的：

- 把很多 handler 共用的前後段抽成 single wrapper + callback
- 把 PixelZone 的 fetch / pixel / shift 回抽成 generic helper
- 為了 source 乾淨，重新引入本來已被 slot 事實消掉的 range check

判斷標準很簡單：

- 如果抽 helper 會讓 hot path 更 generic，就要非常小心
- 如果抽 helper 只是縮 source，但讓 branch 或 call boundary 增加，通常不值得

### 3. 先刪死碼，再做架構重排

安全順序應該永遠是：

1. 刪除已證明不可能成立的條件
2. 刪除已證明沒有 `.cs` 消費者的 state / buffer
3. 再考慮 helper 拆分或 slot-specialization

不要反過來。

### 4. 要區分「高收益重構」和「微優化」

高收益重構通常長這樣：

- 一整段 branch 已知永遠不會成立
- 一個 buffer 已知沒有人讀
- 一個 generic helper 已知只服務單一很窄的區段

微優化通常長這樣：

- 把 `258/259/320` 類 slot 再拆細
- 把 `VisibleTail` 再拆成三個 slot handler
- 再把 prefetch helper 多切一層

兩者不要混在一起評估。

## 目前這套程式最該守住的邊界

下面這些區域是已知高風險區：

- visible 尾端 `256 / 257 / 340`
- wrap 到 `dot 0`
- pre-render odd frame skip
- `skippedPreRenderDot341`
- `Phase4` 的 sprite evaluation / sprite overflow
- `sprSlotCount` / `sprZeroInSlots`
- visible prefetch 與 pre-render 的 BG tile fetch
- MMC3 A12 / IRQ
- vblank 進入點
- `SL240 cx1` frame render
- `$2001` 延遲 OAM corruption

只要改到這些區域，就要自動提高驗證標準。

## 什麼改法通常是安全的

下面這些通常屬於低風險：

- 刪除已知死條件
- 刪除已知死呼叫
- 刪除已知死資料與配置
- 把多個非 hot handler 的共用骨架抽成小 helper
- 把 pre-render / vblank / visible 非像素 handler 的共用流程做有限度收斂

這類改法成立的前提是：

- 不去動 PixelZone 的可見像素熱路徑
- 不改變現有 helper 的時序責任
- 不把 slot-specialized handler 又抽回 generic

## 什麼改法要非常保守

下面這些都不是不能做，而是要先有 benchmark / 明確理由：

- 把 `258/259` 從 `Visible_SpriteFetch` 再拆出來
- 把 `320` 從 `Visible_Prefetch` 再拆出來
- 把 `VisibleTail` 再拆成 `256 / 257 / 340`
- 把 `PpuBgTileFetchRange()` 再拆成 visible prefetch / pre-render 專用版本
- 清 `InitPpuDispatchTable()` 的 preprocessor 重複碼

這些改法通常：

- 收益偏小
- 驗證成本偏高
- 很容易從「合理精修」變成「過度雕刻」

## 明確不建議的方向

目前階段不建議做下面這些事：

- 把 `Ppu_Tick_Visible_PixelZone` 的 tile fetch / pixel composition / sprite shift 抽回共用 helper
- 把所有 handler 都套一層 common wrapper
- 把 `dispatch` 結構重新拉回大 `switch/case`
- 為了消除 duplication，把已經 specialized 的 visible 路徑重新 generic 化

原因：

- 這些改法很可能讓 source 變短
- 但實際上會讓 hot path 變胖

## 日常重構 Checklist

每次要改之前，先過下面這份清單。

### A. 範圍判定

- 這次改動是 visible、pre-render，還是 vblank？
- 有沒有碰到 `256 / 257 / 340 / dot0 / 339 / 321 / 322` 這些特殊點？
- 有沒有碰到 `Phase4`、A12、sprite overflow、frame render、odd frame skip？

### B. 死碼判定

- 這段 branch 在目前 handler 的 `cx` / `evalDot` 範圍內真的可能成立嗎？
- 這段 helper call 在目前 handler 內真的做得到事嗎？
- 這個欄位 / buffer / state 有沒有任何 `.cs` 讀取消費者？

### C. helper 判定

- 這個 helper 是在縮冷路徑，還是在污染 hot path？
- 它是讓責任更清楚，還是只是讓 source 比較短？
- 它有沒有把本來已經被 slot-specialization 消掉的條件又帶回來？

### D. 風險判定

- 這次改動是「刪已證明冗餘」還是「改執行形狀」？
- 是 code-size 重構，還是 timing-sensitive 重構？
- 如果測試失敗，是否容易回退與定位？

## 變更後測試 Checklist

至少要檢查這些類別：

- `ppu_vbl_nmi`
- `sprite_hit`
- `sprite_overflow`
- `ppu_open_bus`
- `ppu_read_buffer`
- `oam_read`
- MMC3 IRQ / A12 敏感 ROM

如果這次改的是 dispatch 邊界或 pre-render / vblank，建議另外特別看：

- `256 / 257 / 340`
- wrap 到 `dot 0`
- `339`
- `321 / 322`
- odd frame skip
- `SL240 cx1`

## Benchmark Checklist

如果這次不是純死碼刪除，而是做 slot 微分裂或 helper 再拆分，就應該做 benchmark。

建議至少分：

- Digital 1x
- Analog
- Ultra Analog / CRT

觀察重點：

- 整體 FPS / frame time
- hot handler 的時間占比
- branch / I-cache 壓力是否下降
- source 變短但 JIT 後熱路徑是否反而變胖

## Commit 策略

這套 PPU 重構很適合小 commit。

建議：

- 每一輪只做一種重構意圖
- 每一輪先測過再 commit
- commit message 要能反映這輪到底是：
- 刪死碼
- 清 dead state
- phase specialization
- shared aux path
- pre-render split

這樣回溯時才看得出重構脈絡。

## 文件維護策略

如果後續又多做幾輪，建議更新下列文件：

- `ppu_dispatch_refactor_study_zh.md`
- 本文件

更新時要分清楚：

- 提交前的原始分析
- 已落地的實作進度
- 下一批候選

不要把它們混成一段。

## 目前這個時點的結論

到目前為止，`ppu_dispatch.cs` 的第一批高價值低風險重構已經做得差不多。

也就是說：

- 再往下做不是不行
- 但已經不再是「明顯冗餘清理」
- 而是「小收益、高驗證成本」的階段

所以後續決策應該改成：

- 沒 benchmark，不亂做微分裂
- 沒證據，不回抽 hot path
- 沒把責任邊界講清楚，不做 helper 重組

如果要把這份文件濃縮成一句話：

> 對這套 PPU，最重要的不是一直重構，而是知道什麼時候該停。  
