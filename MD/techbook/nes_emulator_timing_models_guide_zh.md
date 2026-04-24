# NES 模擬器 Timing 模型教學

## 這篇文章想回答什麼

如果你想寫一個模擬器，或只是對模擬器很有興趣，你很快就會碰到一個問題：

> 模擬器到底要把「時間」模擬到多細？

這個問題看起來像效能問題，但其實它同時是：

- 架構問題
- 正確性問題
- 工程成本問題
- 維護問題

對 NES 這類老主機來說，很多 bug 不是「算錯」，而是「時間點不對」。

例如：

- VBlank 什麼時候發生
- sprite 0 hit 什麼時候成立
- MMC3 IRQ 什麼時候計數
- `$2005/$2006/$2007` 什麼時候真正生效
- open bus / OAM corruption / palette corruption 在哪個 timing 邊界出現

所以，NES 模擬器開發者真正要決定的，不只是「要不要快」，而是：

> 我要把硬體的時間結構模擬到哪一層？

這篇文章會從最粗糙、效能最高的方式開始，一路講到非常細的硬體導向 timing 模型，最後再談一個更極端的方向：`Visual6502` 類型的 netlist 模擬。

文章會同時照顧兩類讀者：

- 一般對資訊技術有興趣的人
- 真正打算開發模擬器的人

## 一個先記住的核心概念

模擬器不是只有「邏輯正確」和「邏輯錯誤」。

它還有一個維度叫做：

- 同樣的邏輯，是不是在正確的時間點發生

越粗糙的 timing 模型：

- 越快
- 越容易寫
- 越容易維護
- 但越難處理硬體邊界行為

越精細的 timing 模型：

- 越慢
- 越難寫
- 越難優化
- 但越有能力處理真實硬體細節

所以，選 timing 模型，本質上是在選：

- 你要的正確性範圍
- 你願意付出的工程代價

## 一張先看懂全局的總表

| 層級 | 典型時間粒度 | 相容性潛力 | 執行效能 | 開發難度 | 適合用途 |
|---|---|---|---|---|---|
| 1 | 每幀 / 每 scanline | 低 | 很高 | 很低 | 教學、概念驗證 |
| 2 | 每 CPU 指令 | 中低 | 高 | 低 | 早期原型、基本遊戲執行 |
| 3 | 每 CPU cycle | 中 | 中高 | 中 | 一般兼容型 emulator |
| 4 | CPU cycle + PPU dot | 中高 | 中 | 中高 | 認真做 NES 相容性 |
| 5 | master clock / signal / delayed effect | 很高 | 低 | 很高 | 高擬真、硬體研究、測試導向 |
| 6 | transistor / netlist | 極高 | 極低 | 極高 | 硬體研究、驗證、歷史保存 |

下面就從第 1 層開始講。

---

## 1. 最粗糙的 timing：每幀 / 每 scanline

### 它是什麼

這種作法最直觀。

你不是去模擬「每個硬體週期做什麼」，而是直接說：

- 一幀到了，畫面更新一次
- 一條 scanline 到了，做一次背景與 sprite 計算

這種模型比較像在模擬：

- 結果

而不是：

- 過程

### 它的優點

- 非常快
- 很容易理解
- 很適合做教學原型
- 很適合先把 CPU、記憶體、基本畫面流程跑起來

### 它的缺點

它幾乎一定會在很多地方出問題，因為 NES 很多行為不是「一條 scanline 結束後發生」，而是：

- 某個 dot
- 某個 CPU cycle
- 某個寄存器 write 後延遲幾拍

這種模型通常很難正確處理：

- sprite 0 hit
- sprite overflow
- scanline IRQ
- VBlank 邊界讀取
- `$2007` read buffer
- open bus
- mid-scanline register effect

### 什麼情況值得用

如果你的目的只是：

- 先學習架構
- 先做出畫面
- 先驗證基本 CPU / PPU 流程

它非常合理。

但如果你想做一個成熟 NES emulator，這通常只是一個起點，不是終點。

---

## 2. 指令級 timing：每 CPU instruction

### 它是什麼

這是很多人開始寫 emulator 時最自然的方式。

CPU 每執行完一條 instruction，就讓其他元件前進一段對應時間，例如：

- CPU 執行一條指令耗費 N cycles
- PPU 就補跑 `N * 3`
- APU 依 CPU instruction 結束時補一段

### 它的優點

- 還是很快
- 程式架構簡單
- 對很多普通遊戲可能已經夠用
- debug 比較好做

### 它的問題

NES 很多硬體現象發生在「指令中間」，不是「指令做完後」。

所以這種模型會很容易遇到：

- 事件時機偏移
- NMI / IRQ 發生點不準
- PPU register 的讀寫副作用不準
- mid-instruction DMA / bus interaction 不準

### 給開發者的判斷

如果你只是想先做出：

- 能跑 ROM
- 能進遊戲
- 畫面大致對

它很適合作為第一版。

但你要有心理準備：

- 之後如果要升級到更準的模型，通常要重構 scheduler

---

## 3. CPU cycle-accurate：以 CPU 為主時鐘

### 它是什麼

這是 NES emulator 很常見、也很實用的折衷方案。

核心思路通常是：

- CPU 每前進 1 cycle
- PPU 跑 3 個 dot
- APU 依 CPU cycle 更新
- mapper 依 CPU / PPU 邊界做同步

這一類模型的本質是：

- 還是以 CPU 為全機節拍中心
- 但時間粒度已經細到「每 CPU cycle」

### 它的優點

- 相容性通常會比 instruction-level 高很多
- scheduler 還算直觀
- 實作成本仍然可控
- 很多 NES emulator 最終都會落在這個附近

### 它的缺點

它仍然會遇到一些更細的 PPU / bus / latch 問題：

- 有些現象其實發生在 PPU dot 內部
- 有些行為不是「同步立刻生效」，而是有延遲鏈
- 有些 mapper 需要更細的 PPU A12 邊界

### 什麼情況適合

如果你的目標是：

- 做出一個實用、相容性不錯的 emulator
- 而且你不想一開始就掉進極高的 timing 複雜度

這通常是很好的平衡點。

---

## 4. CPU cycle + PPU dot：真正進入 PPU timing 世界

### 它是什麼

到了這一層，你已經不再滿足於：

- CPU 跑一下，PPU 補一大段

而是開始明確模擬：

- 每個 PPU dot 發生什麼
- 每條 scanline 的每個區段在做什麼

你會開始把 PPU 拆成像這樣的概念：

- visible line
- pre-render line
- vblank line
- background fetch
- sprite evaluation
- sprite fetch
- dummy fetch

### 它的優點

- 可以正確處理更多 NES 專屬邊界
- 很適合對應 nesdev 文件與測試 ROM
- 比較容易對準 scanline IRQ / sprite hit / VBlank timing

### 它的缺點

- PPU 程式碼會變得很大
- 很多邏輯開始被 timing 主導
- 如果設計不好，很容易出現一個巨大的 `step()` 函式

這通常也是很多 emulator 開始分岔的地方。

分岔方向大概有兩種：

- 繼續維持單一大狀態機
- 開始走 table-driven / specialized handler 路線

---

## 5. 更細的硬體導向 timing：master clock / delayed effect / signal-oriented 模型

這一層就是你們目前 `AprNes` 比較接近的方向。

而且更具體地說，你們最後採用了接近 `TriCNES` 的作法。

### 它是什麼

這類模型不只是問：

- CPU 跑了幾個 cycle？
- PPU 到了哪個 dot？

而是開始問更硬體的問題：

- 這個寄存器 write 是立刻生效，還是延遲幾拍？
- 這個值現在是在 latch、bus，還是在 pending pipeline？
- 這個事件是在 full-step，還是 half-step？
- 這個 corruption flag 是哪個 alignment 下才會觸發？
- A12 邊緣到底在哪個 bus phase 被 mapper 看到？

這時候程式會開始出現大量概念：

- delayed update
- pending flag
- latch chain
- phase 1 / phase 2 / phase 3
- full-step / half-step
- bus state machine
- corruption timing

### 為什麼有人要做到這麼細

因為有些 NES 行為，真的不是靠「大概對」就能穩定複製。

例如：

- `$2002` read 的邊界
- `$2005/$2006/$2007` 生效延遲
- OAM corruption
- palette corruption
- open bus
- MMC3 scanline counter / A12
- sprite evaluation bug

這些東西，如果你只是用「每 dot 大概做什麼」去想，常常會差一點點。
而 NES 的很多 bug，就是差這一點點。

### AprNes / TriCNES 風格的代價

這一類作法有很大好處，但代價也很大。

`AprNes` 走到這裡後，實際碰到的問題就是：

- 正確性更高
- 但效能嚴重下降

這不是偶然，是這種模型的自然代價。

因為你做的事情變成：

- 模擬更多狀態
- 模擬更多中間相位
- 模擬更多延遲生效
- 模擬更多本來在粗模型裡被壓扁的細節

而且這種成本不是線性的。

很多時候你不是只多做一個判斷，而是：

- 整個 hot path 形狀改了
- cache 行為改了
- JIT inline 形狀改了
- branch predictability 變差了

### 為什麼還要做 dispatch specialization

當你把 timing 模型做得這麼細，下一個自然問題就是：

> 這麼細的硬體模型，怎麼才不會慢到不能用？

這時候你們現在這種 `dispatch table + dot specialization` 就很合理。

因為它在做的事情是：

- 不降低 timing fidelity
- 但盡量讓每個 dot handler 只承擔它該承擔的邏輯

例如：

- `visible / pre-render / vblank` 分表
- visible 再切 `PixelZone / SpriteFetch / Prefetch / Dummy / Tail`
- 再把不可能成立的 branch 從 handler 內刪掉

這其實是在做一件很重要的事：

- 把硬體導向的精細模型，重新整理成對 JIT / CPU 比較友善的程式形狀

### 這種模型適合誰

它適合：

- 真的想做高擬真 NES emulator 的人
- 願意大量看測試 ROM、對時序 bug 有耐心的人
- 願意做 profile、熱路徑優化、JIT 友好重構的人

它通常不適合：

- 第一次寫 emulator 的人
- 目標只是跑大多數遊戲的人
- 沒有時間做大量驗證的人

### 對開發者最重要的一句話

如果你選這條路，你不是只在寫 emulator。
你其實是在寫：

- 一個硬體行為模型
- 再加上一個效能工程專案

---

## 6. 更極端的未來方向：Visual6502 / netlist 模擬

### 它是什麼

到了這一層，你已經不是在模擬「CPU 規格」或「PPU 行為」，而是直接模擬：

- 電路網表
- transistor / gate / node 層級的狀態

`Visual6502` 最有名的就是把 6502 拆到 transistor/netlist 層去跑。

對 emulator 開發者來說，這代表一種極端思路：

- 不再手寫高階 timing model
- 而是讓網表本身決定行為

### 為什麼它值得尊敬

因為這種作法最接近：

- 真正的硬體原始行為

它的價值不只是「更準」，還包括：

- 驗證規格書沒寫到的行為
- 研究 undocumented behavior
- 歷史保存
- 對其他高階模型做對照

### 為什麼它目前不會是大多數 emulator 的主流

因為代價太高。

這個代價不只在執行速度，還包括：

- 建模難度
- 資料整理難度
- debug 難度
- 開發工具鏈難度
- 可維護性難度

如果高階 timing model 已經很難優化，那 netlist 模型通常會更難很多。

### 什麼情況值得挑戰

未來如果有人想做下面這類方向，netlist 很值得挑戰：

- 硬體研究型 emulator
- 對照高階模型 correctness 的 reference engine
- 教學 / 視覺化硬體行為平台
- 長期保存與驗證專案

但如果你的目標是：

- 一個日常可玩的 NES emulator

那它目前大多不會是最務實的第一選擇。

---

## 怎麼選？給真正開發者的實用建議

### 如果你是第一次寫 emulator

建議從：

- instruction-level
- 或 CPU cycle 主導模型

開始。

因為你最先要學會的是：

- CPU / memory / mapper / PPU 基本互動
- debug 方法
- ROM 相容性問題怎麼找

不是一開始就把自己困在最細 timing 裡。

### 如果你的目標是「能玩、相容性不錯」

建議目標放在：

- CPU cycle-accurate
- 加上足夠細的 PPU dot model

這通常是最好的工程平衡點。

### 如果你的目標是「高擬真 NES」

那你最終很可能要走向：

- signal-oriented
- delayed effect
- master clock / sub-phase
- 更接近 `TriCNES` 類型的風格

但請你先接受三件事：

1. 效能會明顯變差
2. 架構會明顯變複雜
3. 你之後一定得做大量重構與優化

### 如果你的目標是「硬體研究」

那就可以把目標放到：

- 高階 signal model
- 甚至未來做 netlist / transistor 模型

但那已經不是一般 emulator 專案的難度了。

---

## 一個很務實的開發路線圖

如果你今天真的要開發一個 NES emulator，我會建議這樣走：

### 第 1 階段：先做出能跑的東西

- CPU instruction / cycle 基礎執行
- 記憶體 map
- 基本 mapper
- 粗略 PPU 流程

### 第 2 階段：把 timing 提升到實用等級

- CPU cycle 準確
- PPU dot 級行為
- sprite hit / sprite overflow / VBlank timing
- MMC3 IRQ 等常見敏感路徑

### 第 3 階段：開始處理硬體邊界

- delayed register effect
- `$2007` pipeline
- open bus
- OAM / palette corruption
- 更精細的 A12 / bus state

### 第 4 階段：如果你真的還想更進一步

- master clock / half-step / signal model
- dispatch specialization
- 大量 benchmark
- JIT / code shape / hot path 專門優化

### 第 5 階段：研究型方向

- reference-grade signal model
- netlist / transistor 層級挑戰

---

## 你該怎麼評估一種 timing 模型的代價

每次選模型時，可以問自己五個問題：

### 1. 我想解決的是哪一類錯誤？

如果你只是普通遊戲偶爾黑屏，可能還不用上最細模型。

如果你碰到的是：

- `$2002` 邊界
- sprite eval bug
- scanline IRQ
- bus glitch

那你多半真的需要更細的 timing。

### 2. 我能不能先用粗模型，再逐步升級？

很多時候答案是可以。

而且這通常比一開始就硬上最細模型更務實。

### 3. 我的目標是「玩遊戲」還是「研究硬體」？

這兩件事重疊，但不是同一件事。

### 4. 我能不能承受效能下降？

越細的 timing 模型，越不能假設效能「之後再優化就好」。

很多時候它不是小幅下降，而是：

- 架構一換，整機吞吐就掉下去

### 5. 我有沒有能力驗證？

高 fidelity 模型真正難的不是「寫出來」。
而是：

- 你怎麼知道它真的更準？

如果沒有測試 ROM、沒有對照機制、沒有 benchmark，越細的模型不一定越有意義。

---

## 對 `AprNes` / 類似專案的一個工程結論

像 `AprNes` 這種最後採用接近 `TriCNES` 作法的專案，已經不是在做普通兼容型 emulator，而是在追求：

- 高 fidelity timing

這條路是合理的，但代價一定很高。

而實際經驗也正好說明了這一點：

- 正確性往上走
- 效能顯著下降
- 然後必須再花非常大工夫做：
- 架構重整
- hot path specialization
- JIT 友好調整
- generic 殘留清理

所以，如果有人想直接複製這條路，我的建議不是「不要做」，而是：

> 先確定你的目標真的值得你付出這個代價。

---

## 最後總結

NES 模擬器的 timing 模型，不是一條單純從「差」走到「好」的直線。

它比較像一組取捨：

- 越粗，越快，越容易做
- 越細，越準，越難維護

從最粗糙的每幀 / 每 scanline，到 CPU cycle、PPU dot，再到像 `TriCNES` / `AprNes` 這種硬體導向 micro-timing 模型，最後到 `Visual6502` 這種 netlist 路線，本質上都是在回答同一個問題：

> 我要為了多少正確性，付出多少工程代價？

如果你只是想開始做 emulator：

- 從粗一點的模型開始

如果你想做出成熟可用的 NES emulator：

- CPU cycle + PPU dot 通常是最務實的路

如果你要追硬體級 fidelity：

- 你就要接受效能、複雜度、驗證成本都會一起爆炸

如果你想挑戰更極端的未來：

- netlist / transistor 模型會是很值得尊敬的方向
- 但那是一條研究型道路，不是一般專案的自然起點

把這篇文章濃縮成一句話：

> Timing 模型不是「寫得越細越好」，而是「要和你的目標、代價承受能力、驗證能力一起設計」。  
