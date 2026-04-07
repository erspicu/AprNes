# AC Page 19 Regression 分析

**日期**：2026-04-02
**分支**：feature/ppu-high-precision
**狀態**：135/136（Page 19 -1），不回退

---

## 現象

- AC test 從 136/136 降至 135/136
- Page 19 報告 5 PASS / 1 FAIL
- 截圖（`ac_page_19.webp`）顯示 6 個 test 全部 PASS（視覺上無錯誤標記）
- 可能是截圖時機與 test runner 記憶體讀取的差異（某個 test 在截圖時 PASS 但記憶體結果地址未正確設定）

## Page 19 的 6 個 Test

| # | 地址 | 測試名稱 | 說明 |
|---|------|---------|------|
| 1 | $0481 | ATTRIBUTES AS TILES | Attribute table 作為 tile data 使用 |
| 2 | $0482 | T REGISTER QUIRKS | t register 的特殊行為 |
| 3 | $0483 | STALE BG SHIFT REGISTERS | BG shift register 的 stale data 行為 |
| 4 | $0487 | BG SERIAL IN | BG serial-in（位移暫存器載入）精度 |
| 5 | $0484 | SPRITES ON SCANLINE 0 | Scanline 0 的 sprite 行為 |
| 6 | $048C | $2004 STRESS TEST | OAM read stress test |

## Regression 出現時間點

透過 git bisect 可確認，regression 出現在以下 commit 之間：

| Commit | 變更 | AC 結果 |
|--------|------|---------|
| `93086bf` | $2001 四層 flag | 174/174 blargg（未測 AC） |
| `3e4f5f7` | $2000 delay 2 cycles | 174/174 blargg（未測 AC） |
| `599076f` | **$2006 delay 3→4** | 174/174 blargg |
| `c2fdefa` | 文件更新 | AC 135/136（首次發現） |

最可能的觸發點：**`599076f` — $2006 delay 從 3 改為 4 PPU dots**

## 可能原因分析

### 假設 1：$2006 delay 影響 STALE BG SHIFT REGISTERS（$0483）
- `STALE BG SHIFT REGISTERS` 測試檢查 $2006 寫入後 BG shift register 的舊資料何時消失
- 延遲從 3→4 dots 意味著 t→v copy 晚了 1 dot，shift register 的 stale data 存在時間延長
- 如果 test 精確到 dot 級別，多出 1 dot 的 stale 可能導致 FAIL

### 假設 2：$2006 delay 影響 BG SERIAL IN（$0487）
- `BG SERIAL IN` 測試 BG tile data 載入 shift register 的精確時機
- $2006 的 t→v copy 時機影響 vram_addr，進而影響下一次 tile fetch 的來源地址
- 延遲多 1 dot 可能讓 tile fetch 讀到上一個 vram_addr 的 tile 而非新的

### 假設 3：$2000 delay 影響 ATTRIBUTES AS TILES（$0481）
- `ATTRIBUTES AS TILES` 可能依賴 BgPatternTableAddr 的切換時機
- $2000 delay 2 cycles 讓 pattern table 地址晚 2 dot 生效
- 如果 test 在特定 dot 切換 $2000，pattern table 變更晚了可能導致 FAIL

### 假設 4：$2001 delay 影響 T REGISTER QUIRKS（$0482）
- `T REGISTER QUIRKS` 可能測試 $2001 寫入對 t register 的副作用
- 四層 flag 系統改變了 ShowBgLeft8/ShowSprLeft8 的生效時機
- 可能影響 rendering enable 狀態下的 t register 更新

## Bisect 結果

- **$2006 delay 4/5 → 3/4**：仍 135/136 → **排除 $2006**
- **$2000 delay 停用（即時）**：仍 135/136 → **排除 $2000**
- **$2001 delay 停用（即時）**：仍 135/136 → **排除 $2001**
- **所有寄存器 delay 都已排除**

## 根因範圍縮小

三個 delay 都不是根因。可能的原因：
1. per-dot pixel output 架構（RenderBGTile batch → half-step per-dot）
2. per-dot sprite compositing（RenderSpritesLine 移至 cx==0）
3. backdrop fill 改為無條件（cx==0 always fill）
4. half-step tick 改變了某個 timing 精度
5. $2007 state machine buffer deferred update

TEST_BGSerialIn 是最可能受影響的 test — 它測試 rendering disable/enable 的精確 dot 位置對 shift register 的影響。per-dot 架構改變了 shift register 的 pixel 輸出時機。

## 修復方向（後續）

1. 精確 bisect：分別測試 `$2000 delay only`、`$2001 delay only`、`$2006 delay only` 的 AC 結果
2. 如果是 $2006 delay：嘗試 alignment-dependent（部分情況 3、部分 4），或回到 3 但加 half-step 精度
3. 如果是 $2000 delay：將 pattern table 改回即時，只延遲其他項目
4. 不回退更精確的模型 — 可能需要配合其他精度改善才能收斂

## 備註

- 截圖顯示全 PASS 但 runner 報 FAIL 的現象，可能是 race condition：test ROM 在某個 timing window 內狀態不穩定
- 也可能是 hex dump 讀取時機問題（截圖在 test 完成後，但 hex dump 在不同的時間點讀取）
- 需要帶 `--json` 重跑確認具體失敗的地址
